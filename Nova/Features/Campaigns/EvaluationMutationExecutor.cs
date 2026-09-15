using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Common;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Nova.SharedKernel.Validation;

namespace Nova.Features.Campaigns;

/// <summary>Runs evaluation effects and immutable receipts in one transaction with fresh retry contexts.</summary>
/// <param name="factory">The tenant context factory.</param>
/// <param name="currentUser">The request's tenant and actor scope.</param>
internal sealed class EvaluationMutationExecutor(IDbContextFactory<NovaDbContext> factory, ICurrentUserProvider currentUser)
{
    /// <summary>Validates, authorizes and executes or recovers exactly one logical mutation.</summary>
    /// <typeparam name="T">The immutable response type.</typeparam>
    /// <param name="input">The original, tab-retained payload.</param>
    /// <param name="mutate">The effects, inside the locked transaction.</param>
    /// <param name="cancellationToken">Cancels this attempt, without changing logical identity.</param>
    /// <returns>The original receipt-bearing response or a structured failure.</returns>
    public async Task<ServiceResult<T>> ExecuteAsync<T>(EvaluationOperationInput input,
        Func<NovaDbContext, long, long, DateTimeOffset, Task<ServiceResult<T>>> mutate,
        CancellationToken cancellationToken)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUser.UserId is not long actor || currentUser.ClubId is not long club)
        {
            return Forbidden();
        }

        var expiresAt = GetRecoveryDeadline(input.OperationId);
        if (expiresAt is null || expiresAt <= DateTimeOffset.UtcNow)
        {
            return Expired();
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            input.GetType().Name + ":" + JsonSerializer.Serialize(input, input.GetType()))));
        await using var strategyDb = await factory.CreateDbContextAsync(cancellationToken);
        var strategy = strategyDb.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(input,
            async (_, token) =>
            {
                await using var db = await factory.CreateDbContextAsync(token);
                return await AttemptAsync(db, input, actor, club, fingerprint, expiresAt.Value, mutate, token);
            },
            async (_, token) =>
            {
                await using var db = await factory.CreateDbContextAsync(token);
                await using var transaction = await db.Database.BeginTransactionAsync(token);
                if (!await AuthorizeAsync(db, actor, club, token))
                {
                    return new ExecutionResult<ServiceResult<T>>(true, Forbidden());
                }

                var recovered = await RecoverAsync<T>(db, input.OperationId, actor, club, fingerprint, token);
                return new ExecutionResult<ServiceResult<T>>(recovered is not null, recovered!);
            }, cancellationToken);
    }

    /// <summary>Acquires membership and roster locks before inspecting receipts or mutating aggregates.</summary>
    private static async Task<ServiceResult<T>> AttemptAsync<T>(NovaDbContext db, EvaluationOperationInput input,
        long actor, long club, string fingerprint, DateTimeOffset expiresAt,
        Func<NovaDbContext, long, long, DateTimeOffset, Task<ServiceResult<T>>> mutate, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await AuthorizeAsync(db, actor, club, token))
        {
            return Forbidden();
        }

        await db.AcquireClubRosterLockAsync(club, token);
        var recovered = await RecoverAsync<T>(db, input.OperationId, actor, club, fingerprint, token);
        if (recovered is not null)
        {
            return recovered;
        }

        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            return Expired();
        }

        // Keep membership/roster locks while undoing a rejected attempt's partial effects.
        // Its durable rejection prevents a delayed identical request from committing later.
        const string MutationSavepoint = "evaluation_effects";
        await transaction.CreateSavepointAsync(MutationSavepoint, token);
        var result = await mutate(db, actor, club, expiresAt);
        if (result.IsProblem)
        {
            await transaction.RollbackToSavepointAsync(MutationSavepoint, token);
            db.ChangeTracker.Clear();
            if (result.Problem.Kind == ServiceProblemKind.ServerError) { return result; }
            result = EvaluationMutationRejection.NotCommitted(result.Problem, input.OperationId);
        }

        db.EvaluationMutationReceipts.Add(new EvaluationMutationReceiptEntity
        {
            ClubId = club,
            ActorUserId = actor,
            CreatedById = actor,
            OperationId = input.OperationId,
            RequestSha256 = fingerprint,
            ResultJson = result.Match(
                value => JsonSerializer.Serialize(new StoredOutcome<T>(value, null)),
                problem => JsonSerializer.Serialize(new StoredOutcome<T>(default, problem))),
            RecoveryExpiresAt = expiresAt
        });
        await db.SaveChangesAsync(token);
        if (expiresAt <= DateTimeOffset.UtcNow)
        {
            return Expired();
        }
        await transaction.CommitAsync(token);
        return result;
    }

    /// <summary>Checks persisted membership while excluding membership removal and role changes.</summary>
    private static async Task<bool> AuthorizeAsync(NovaDbContext db, long actor, long club, CancellationToken token)
    {
        await db.AcquireUserMembershipLockAsync(actor, token);
        await db.AcquireClubMembershipLockAsync(club, token);
        return await db.Users.AnyAsync(user => user.Id == actor && user.ClubId == club, token);
    }

    /// <summary>Recovers only proof belonging to the exact actor and original request.</summary>
    private static async Task<ServiceResult<T>?> RecoverAsync<T>(NovaDbContext db, Guid operationId, long actor,
        long club, string fingerprint, CancellationToken token)
    {
        var receipt = await db.EvaluationMutationReceipts.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.ClubId == club && candidate.OperationId == operationId, token);
        if (receipt is null)
        {
            return null;
        }

        if (receipt.ActorUserId != actor || !string.Equals(receipt.RequestSha256, fingerprint, StringComparison.Ordinal))
        {
            return ServiceProblem.Conflict("This operation identity belongs to a different request. Keep the original recovery payload.");
        }

        if (receipt.RecoveryExpiresAt <= DateTimeOffset.UtcNow) { return Expired(); }
        var outcome = JsonSerializer.Deserialize<StoredOutcome<T>>(receipt.ResultJson)
            ?? throw new InvalidOperationException("The evaluation receipt is missing its original outcome.");
        return outcome.Problem is { } problem
            ? problem
            : new ServiceResult<T>(outcome.Value ?? throw new InvalidOperationException("The evaluation receipt is missing its original result."));
    }

    /// <summary>Uses the immutable UUIDv7 timestamp so deleted/expired receipts can never restart old operations.</summary>
    private static DateTimeOffset? GetRecoveryDeadline(Guid operationId)
    {
        var value = operationId.ToString("N", CultureInfo.InvariantCulture);
        if (value[12] != '7' || value[16] is not ('8' or '9' or 'a' or 'b')
            || !long.TryParse(value.AsSpan(0, 12), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var milliseconds))
        {
            return null;
        }

        // UUIDv7 allows a larger timestamp range than DateTimeOffset. Bound the primitive first.
        var now = DateTimeOffset.UtcNow;
        return milliseconds > now.AddMinutes(1).ToUnixTimeMilliseconds()
            ? null
            : DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).AddHours(24);
    }

    /// <summary>Locks and rechecks the participant, player visibility and campaign lifecycle before writes.</summary>
    public static async Task<ServiceResult<PlayerCampaignAssignmentEntity>> GetWritableParticipantAsync(
        NovaDbContext db, long assignmentId, long club, CancellationToken token)
    {
        var identity = await db.PlayerCampaignAssignments.Where(assignment => assignment.PlayerCampaignAssignmentId == assignmentId)
            .Select(assignment => new { assignment.CampaignId, assignment.PlayerId }).SingleOrDefaultAsync(token);
        if (identity is null)
        {
            return ServiceProblem.NotFound();
        }

        await db.AcquireCampaignMutationLockAsync(identity.CampaignId, token);
        await db.AcquirePlayerMutationLockAsync(identity.PlayerId, token);
        var participant = await db.PlayerCampaignAssignments.Include(assignment => assignment.Campaign).Include(assignment => assignment.Player)
            .SingleOrDefaultAsync(assignment => assignment.PlayerCampaignAssignmentId == assignmentId && assignment.ClubId == club
                && assignment.Campaign.ClubId == club && assignment.Player.ClubId == club, token);
        if (participant is null || participant.Player.LifecycleStatus == LifecycleStatus.Archived)
        {
            return ServiceProblem.NotFound();
        }

        return participant.Campaign.Status == CampaignStatus.Active
            ? participant
            : ServiceProblem.Conflict("This campaign is read-only. Refresh to see its current status; keep or copy your draft.");
    }

    /// <summary>Captures the authorized actor's name inside the creation transaction under membership locks.</summary>
    public static async Task<string> GetActorDisplayNameAsync(NovaDbContext db, long actor, CancellationToken token)
    {
        var name = await db.Users.Where(user => user.Id == actor)
            .Select(user => new { user.FirstName, user.LastName }).SingleAsync(token);
        return $"{name.FirstName} {name.LastName}".Trim();
    }

    /// <summary>Reads the current administrator role under the already-held membership locks.</summary>
    public static Task<bool> IsAdministratorAsync(NovaDbContext db, long actor, CancellationToken token)
    {
        var roleName = Roles.ClubAdmin.ToUpperInvariant();
        return (from userRole in db.UserRoles
                join role in db.Roles on userRole.RoleId equals role.Id
                where userRole.UserId == actor && role.NormalizedName == roleName
                select userRole.UserId).AnyAsync(token);
    }

    /// <summary>Creates the non-disclosing membership rejection.</summary>
    private static ServiceProblem Forbidden() => ServiceProblem.Forbidden("You must currently belong to this club to capture or recover evaluation evidence.");

    /// <summary>Creates the explicit expired-recovery rejection.</summary>
    private static ServiceProblem Expired() => ServiceProblem.Conflict("The 24-hour recovery window has expired. This operation will not be submitted again; your draft remains copyable.");

    private sealed record StoredOutcome<T>(T? Value, ServiceProblem? Problem);
}
