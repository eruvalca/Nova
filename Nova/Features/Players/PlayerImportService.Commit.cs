using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Shared.Validation;

namespace Nova.Features.Players;

internal sealed partial class PlayerImportService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlayerImportCompletion>> CommitAsync(
        PlayerImportCommitInput input,
        CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            return ServiceProblem.Validation("input", "A confirmed preview is required.");
        }

        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        var uploadProblem = ValidateUpload(input.Upload);
        if (uploadProblem is not null)
        {
            return uploadProblem.Value;
        }

        if (!TryGetAdministrator(out var actorUserId, out var clubId))
        {
            return ImportForbidden();
        }

        // Freeze caller-owned bytes before hashing or awaiting, including for direct service callers.
        input = input with { Upload = input.Upload with { Content = input.Upload.Content.ToArray() } };
        var fileHash = Convert.ToHexString(SHA256.HashData(input.Upload.Content));
        var tokenHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(input.ConfirmationToken)));
        await PruneImportReceiptsAsync(cancellationToken);
        await using var strategyDb = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyDb.Database.CreateExecutionStrategy();
        var startedAt = Stopwatch.GetTimestamp();
        var result = await strategy.ExecuteAsync(
            input,
            async (request, token) =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                return await CommitImportAttemptAsync(db, request, actorUserId, clubId, fileHash, tokenHash, startedAt, token);
            },
            async (request, token) =>
            {
                await using var db = await dbContextFactory.CreateDbContextAsync(token);
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                if (!await AuthorizeImportAsync(db, actorUserId, clubId, token))
                {
                    return new ExecutionResult<ServiceResult<PlayerImportCompletion>>(true, ImportForbidden());
                }

                var receipt = await FindImportReceiptAsync(db, request.OperationId, clubId, token);
                return receipt is null
                    ? new ExecutionResult<ServiceResult<PlayerImportCompletion>>(false, default!)
                    : new ExecutionResult<ServiceResult<PlayerImportCompletion>>(
                        true, RecoverImport(receipt, request, actorUserId, fileHash, tokenHash));
            },
            cancellationToken);

        result.Switch(
            _ => { },
            problem => LogImportFailed(input.OperationId, clubId, actorUserId, problem.Kind.ToString()));
        return result;
    }

    /// <summary>Runs one fully locked and atomic import attempt with fresh database state.</summary>
    /// <param name="db">The fresh tenant mutation context.</param>
    /// <param name="input">The frozen request.</param>
    /// <param name="actorUserId">The requesting actor.</param>
    /// <param name="clubId">The requesting club.</param>
    /// <param name="fileHash">The frozen file digest.</param>
    /// <param name="tokenHash">The exact confirmation digest.</param>
    /// <param name="startedAt">The monotonic request start timestamp for completion timing.</param>
    /// <param name="cancellationToken">Cancels the attempt.</param>
    /// <returns>A committed or recovered result, or a safe rejection.</returns>
    private async Task<ServiceResult<PlayerImportCompletion>> CommitImportAttemptAsync(
        NovaDbContext db, PlayerImportCommitInput input, long actorUserId, long clubId,
        string fileHash, string tokenHash, long startedAt, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        if (!await AuthorizeImportAsync(db, actorUserId, clubId, cancellationToken))
        {
            return ImportForbidden();
        }

        var receipt = await FindImportReceiptAsync(db, input.OperationId, clubId, cancellationToken);
        if (receipt is not null)
        {
            return RecoverImport(receipt, input, actorUserId, fileHash, tokenHash);
        }

        await db.AcquireClubSeasonLockAsync(clubId, cancellationToken);
        await db.AcquireClubRosterLockAsync(clubId, cancellationToken);
        receipt = await FindImportReceiptAsync(db, input.OperationId, clubId, cancellationToken);
        if (receipt is not null)
        {
            return RecoverImport(receipt, input, actorUserId, fileHash, tokenHash);
        }

        if (!tokenProtector.TryValidate(input.ConfirmationToken, input.OperationId, clubId, actorUserId,
                input.Upload.Content, out var preview) || preview is null)
        {
            return ServiceProblem.Conflict("The preview confirmation is invalid or expired. Preview the original file again.");
        }

        if (!preview.RowStatuses.Contains(PlayerImportRowStatus.Ready))
        {
            return ServiceProblem.Validation("file", "The preview has no eligible players to import.");
        }

        var parsed = parser.Parse(input.Upload.Content, cancellationToken);
        return await parsed.Match<Task<ServiceResult<PlayerImportCompletion>>>(
            async file =>
            {
                if (file.Rows.Count != preview.RowStatuses.Count)
                {
                    return ServiceProblem.Conflict("The preview no longer matches this file. Preview the file again.");
                }

                var rows = await PlayerImportRowClassifier.ClassifyAsync(db, file, cancellationToken);
                var campaignId = await db.Campaigns
                    .Where(campaign => campaign.Status == CampaignStatus.Active)
                    .Select(campaign => (long?)campaign.CampaignId)
                    .SingleOrDefaultAsync(cancellationToken);
                if (campaignId.HasValue)
                {
                    await db.AcquireCampaignMutationLockAsync(campaignId.Value, cancellationToken);
                }

                // Opening/reopening is excluded by the season lock; close may have won the campaign lock.
                var campaign = await db.Campaigns
                    .Where(candidate => candidate.Status == CampaignStatus.Active)
                    .Select(candidate => new { candidate.CampaignId, candidate.Name })
                    .SingleOrDefaultAsync(cancellationToken);
                var created = new Dictionary<int, PlayerEntity>();
                for (var index = 0; index < rows.Count; index++)
                {
                    if (preview.RowStatuses[index] != PlayerImportRowStatus.Ready
                        || rows[index].Status != PlayerImportRowStatus.Ready)
                    {
                        continue;
                    }

                    var candidate = rows[index].Candidate!;
                    var player = new PlayerEntity
                    {
                        ClubId = default,
                        CreatedById = default,
                        CreationOperationId = Guid.CreateVersion7(),
                        FirstName = candidate.FirstName,
                        LastName = candidate.LastName,
                        DateOfBirth = candidate.DateOfBirth,
                        GraduationYear = candidate.GraduationYear,
                        Gender = candidate.Gender,
                        JerseyNumber = candidate.JerseyNumber,
                        LifecycleStatus = LifecycleStatus.Active
                    };
                    created.Add(rows[index].SourceRowNumber, player);
                    db.Players.Add(player);
                }

                try
                {
                    if (created.Count > 0)
                    {
                        await db.SaveChangesAsync(cancellationToken);
                        if (campaign is not null)
                        {
                            CampaignParticipationWriter.StageEnrollments(db, clubId, campaign.CampaignId,
                                created.Values.Select(player => player.PlayerId));
                        }
                    }

                    var outcomes = rows.Select((row, index) => CreateImportRowOutcome(
                        row, preview.RowStatuses[index], created)).ToArray();
                    var completedAt = timeProvider.GetUtcNow();
                    var completion = new PlayerImportCompletion(
                        input.OperationId, completedAt,
                        completedAt.AddHours(PlayerImportConstraints.RecoveryLifetimeHours), rows.Count, created.Count,
                        outcomes.Count(row => row.Status == PlayerImportCommitRowStatus.SkippedInvalidAtPreview),
                        outcomes.Count(row => row.Status == PlayerImportCommitRowStatus.SkippedDuplicateAtPreview),
                        outcomes.Count(row => row.Status == PlayerImportCommitRowStatus.BlockedAtCommit),
                        campaign is null ? 0 : created.Count, campaign is null ? created.Count : 0,
                        campaign?.CampaignId, campaign?.Name, outcomes);
                    db.PlayerImportReceipts.Add(new PlayerImportReceiptEntity
                    {
                        ClubId = default,
                        CreatedById = default,
                        OperationId = input.OperationId,
                        ActorUserId = actorUserId,
                        FileSha256 = fileHash,
                        FileLength = input.Upload.Content.Length,
                        ConfirmationTokenSha256 = tokenHash,
                        CompletedAt = completion.CompletedAt,
                        RecoveryExpiresAt = completion.RecoveryExpiresAt,
                        ResultJson = JsonSerializer.Serialize(completion)
                    });
                    await db.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                    LogImportCompleted(completion.OperationId, clubId, actorUserId, completion.CreatedRows,
                        completion.BlockedRows, Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
                    return completion;
                }
                catch (DbUpdateConcurrencyException)
                {
                    return ServiceProblem.Conflict("The import could not complete because club data changed. Retry the same confirmed import.");
                }
            },
            failure => Task.FromResult<ServiceResult<PlayerImportCompletion>>(ServiceProblem.Validation("file", failure.Message)));
    }

    /// <summary>Revalidates persisted membership and role while excluding concurrent role or membership changes.</summary>
    /// <param name="db">The open transaction context.</param>
    /// <param name="actorUserId">The authenticated actor.</param>
    /// <param name="clubId">The expected tenant.</param>
    /// <param name="cancellationToken">Cancels locks and authorization reads.</param>
    /// <returns>Whether the actor is currently an administrator of this club.</returns>
    private static async Task<bool> AuthorizeImportAsync(
        NovaDbContext db, long actorUserId, long clubId, CancellationToken cancellationToken)
    {
        await db.AcquireUserMembershipLockAsync(actorUserId, cancellationToken);
        await db.AcquireClubMembershipLockAsync(clubId, cancellationToken);
        var normalizedRole = Roles.ClubAdmin.ToUpperInvariant();
        return await (from user in db.Users
                      join userRole in db.UserRoles on user.Id equals userRole.UserId
                      join role in db.Roles on userRole.RoleId equals role.Id
                      where user.Id == actorUserId && user.ClubId == clubId && role.NormalizedName == normalizedRole
                      select user.Id).AnyAsync(cancellationToken);
    }

    /// <summary>Finds original commit evidence without loading mutable players or campaigns.</summary>
    /// <param name="db">The authorized tenant context.</param>
    /// <param name="operationId">The preview identity.</param>
    /// <param name="clubId">The expected club.</param>
    /// <param name="cancellationToken">Cancels the receipt read.</param>
    /// <returns>The immutable receipt, if the operation completed.</returns>
    private static Task<PlayerImportReceiptEntity?> FindImportReceiptAsync(
        NovaDbContext db, Guid operationId, long clubId, CancellationToken cancellationToken) =>
        db.PlayerImportReceipts.AsNoTracking().SingleOrDefaultAsync(
            receipt => receipt.ClubId == clubId && receipt.OperationId == operationId, cancellationToken);

    /// <summary>Recovers only the exact original request, including when its preview token has expired.</summary>
    /// <param name="receipt">The tenant-scoped completion proof.</param>
    /// <param name="input">The resubmitted original request.</param>
    /// <param name="actorUserId">The independently authorized actor.</param>
    /// <param name="fileHash">The submitted file digest.</param>
    /// <param name="tokenHash">The submitted confirmation digest.</param>
    /// <returns>The original result or an identity/retention conflict.</returns>
    private ServiceResult<PlayerImportCompletion> RecoverImport(
        PlayerImportReceiptEntity receipt, PlayerImportCommitInput input, long actorUserId, string fileHash, string tokenHash)
    {
        if (receipt.ActorUserId != actorUserId || receipt.FileLength != input.Upload.Content.Length
            || receipt.FileSha256 != fileHash || receipt.ConfirmationTokenSha256 != tokenHash
            || receipt.RecoveryExpiresAt <= timeProvider.GetUtcNow())
        {
            return ServiceProblem.Conflict("This import cannot be recovered with that confirmation. Preview the file again.");
        }

        var completion = JsonSerializer.Deserialize<PlayerImportCompletion>(receipt.ResultJson)
            ?? throw new InvalidOperationException("The persisted import completion is missing.");
        LogImportRecovered(input.OperationId, receipt.ClubId, actorUserId);
        return completion;
    }

    /// <summary>Maps original review eligibility and fresh validation into disjoint completion outcomes.</summary>
    /// <param name="row">The final classified row.</param>
    /// <param name="previewStatus">The protected original classification.</param>
    /// <param name="created">Created players keyed by source row.</param>
    /// <returns>The immutable row result.</returns>
    private static PlayerImportCommitRow CreateImportRowOutcome(
        PlayerImportPreviewRow row, PlayerImportRowStatus previewStatus, IReadOnlyDictionary<int, PlayerEntity> created) =>
        previewStatus switch
        {
            PlayerImportRowStatus.Invalid => new(row.SourceRowNumber, PlayerImportCommitRowStatus.SkippedInvalidAtPreview, null, [], null),
            PlayerImportRowStatus.Duplicate => new(row.SourceRowNumber, PlayerImportCommitRowStatus.SkippedDuplicateAtPreview, null, [], null),
            _ => created.TryGetValue(row.SourceRowNumber, out var player)
                ? new(row.SourceRowNumber, PlayerImportCommitRowStatus.Created, player.PlayerId, [], null)
                : new(row.SourceRowNumber, PlayerImportCommitRowStatus.BlockedAtCommit, null, row.Errors, row.Duplicate)
        };

    /// <summary>Prunes at most 500 expired snapshots globally, including receipts of deleted clubs.</summary>
    /// <param name="cancellationToken">Cancels retention maintenance.</param>
    /// <returns>A task representing bounded receipt cleanup.</returns>
    private async Task PruneImportReceiptsAsync(CancellationToken cancellationToken)
    {
        await using var db = await adminDbContextFactory.CreateDbContextAsync(cancellationToken);
        var now = timeProvider.GetUtcNow();
        if (db.Database.IsNpgsql())
        {
            await db.PlayerImportReceipts.Where(receipt => receipt.RecoveryExpiresAt <= now)
                .OrderBy(receipt => receipt.RecoveryExpiresAt).ThenBy(receipt => receipt.PlayerImportReceiptId)
                .Take(500).ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            // SQLite cannot compare or order DateTimeOffset; this branch is only the unit harness.
            var receipts = await db.PlayerImportReceipts.ToListAsync(cancellationToken);
            db.PlayerImportReceipts.RemoveRange(receipts.Where(receipt => receipt.RecoveryExpiresAt <= now)
                .OrderBy(receipt => receipt.RecoveryExpiresAt).Take(500));
            await db.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Creates the non-disclosing administrator authorization failure.</summary>
    /// <returns>The forbidden result used by initial attempts and recovery.</returns>
    private static ServiceProblem ImportForbidden() =>
        ServiceProblem.Forbidden("You must currently be a club administrator to commit or recover a player import.");

    /// <summary>Logs aggregate completion without source values.</summary>
    /// <param name="operationId">The completed preview identity.</param>
    /// <param name="clubId">The tenant identifier.</param>
    /// <param name="actorUserId">The requesting actor.</param>
    /// <param name="created">The created player count.</param>
    /// <param name="blocked">The newly blocked row count.</param>
    /// <param name="durationMs">The elapsed request duration.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Player import {OperationId} completed in ClubId={ClubId} for UserId={ActorUserId}; Created={Created}; Blocked={Blocked}; DurationMs={DurationMs}.")]
    private partial void LogImportCompleted(Guid operationId, long clubId, long actorUserId, int created, int blocked, double durationMs);

    /// <summary>Logs an expected failure without request secrets.</summary>
    /// <param name="operationId">The attempted preview identity.</param>
    /// <param name="clubId">The tenant identifier.</param>
    /// <param name="actorUserId">The requesting actor.</param>
    /// <param name="kind">The safe failure classification.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player import {OperationId} failed in ClubId={ClubId} for UserId={ActorUserId}; Kind={Kind}.")]
    private partial void LogImportFailed(Guid operationId, long clubId, long actorUserId, string kind);

    /// <summary>Logs successful immutable receipt recovery.</summary>
    /// <param name="operationId">The recovered preview identity.</param>
    /// <param name="clubId">The tenant identifier.</param>
    /// <param name="actorUserId">The requesting actor.</param>
    [LoggerMessage(Level = LogLevel.Information, Message = "Player import {OperationId} recovered in ClubId={ClubId} for UserId={ActorUserId}.")]
    private partial void LogImportRecovered(Guid operationId, long clubId, long actorUserId);
}
