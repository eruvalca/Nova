using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Features.Common;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.Features.Players;

internal sealed partial class PlayerManagementService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlayerCreationCompletion>> CreateAsync(CreatePlayerInput input, CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0) { return ServiceProblem.Validation(errors); }
        if (currentUserProvider.UserId is not long actor || currentUserProvider.ClubId is not long club || input.ClubId != club)
        {
            LogPlayerCreateForbidden(currentUserProvider.UserId ?? 0);
            return CreationForbidden();
        }
        if (!PlayerCreationOperation.TryGetDeadline(input.OperationId, timeProvider.GetUtcNow(), out var deadline))
        {
            return PlayerCreationProblems.Expired();
        }

        var fingerprint = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(input))));
        return await ExecuteWithFreshContextAsync(
            db => CreateOrRecoverAsync(db, input, actor, club, fingerprint, deadline, cancellationToken),
            db => VerifyCreationAsync(db, input, actor, club, fingerprint, cancellationToken),
            cancellationToken);
    }

    /// <summary>Serializes replay, duplicate classification, and enrollment against all relevant writers.</summary>
    private async Task<ServiceResult<PlayerCreationCompletion>> CreateOrRecoverAsync(NovaDbContext db, CreatePlayerInput input,
        long actor, long club, string fingerprint, DateTimeOffset deadline, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await PlayerMutationAuthorization.AuthorizeAsync(db, actor, club, token)) { return CreationForbidden(); }
        var recovered = await RecoverCreationAsync(db, input.OperationId, actor, club, fingerprint, token);
        if (recovered is not null) { return recovered; }

        await db.AcquireClubSeasonLockAsync(club, token);
        await db.AcquireClubRosterLockAsync(club, token);
        recovered = await RecoverCreationAsync(db, input.OperationId, actor, club, fingerprint, token);
        if (recovered is not null) { return recovered; }
        if (timeProvider.GetUtcNow() >= deadline) { return PlayerCreationProblems.Expired(); }

        var duplicate = await FindDuplicateAsync(db, input, token);
        ServiceResult<PlayerCreationCompletion> result;
        if (duplicate is not null)
        {
            result = PlayerCreationProblems.Duplicate(input.OperationId, duplicate.PlayerId, duplicate.LifecycleStatus);
        }
        else
        {
            result = await StageCreationAsync(db, input, deadline, token);
            if (result.IsProblem) { return result; }
        }

        db.PlayerCreationReceipts.Add(new PlayerCreationReceiptEntity
        {
            ClubId = default,
            CreatedById = default,
            ActorUserId = actor,
            OperationId = input.OperationId,
            RequestSha256 = fingerprint,
            ResultJson = result.Match(
                value => JsonSerializer.Serialize(new CreationOutcome(value, null)),
                problem => JsonSerializer.Serialize(new CreationOutcome(null, problem))),
            RecoveryExpiresAt = deadline
        });
        await db.SaveChangesAsync(token);
        if (timeProvider.GetUtcNow() >= deadline) { return PlayerCreationProblems.Expired(); }
        await transaction.CommitAsync(token);
        result.Switch(value => LogPlayerCreated(value.Player.PlayerId, value.Enrollment is null ? 0 : 1, actor), _ => { });
        return result;
    }

    /// <summary>Loads only birth-date candidates, preserving the import's exact invariant-case identity comparison.</summary>
    private static async Task<PlayerCreationDuplicate?> FindDuplicateAsync(NovaDbContext db, PlayerProfileInput input, CancellationToken token)
    {
        var key = PlayerDuplicateKey.Create(input.FirstName, input.LastName, input.DateOfBirth);
        // Stream the narrow identity projection instead of materializing an unbounded tenant roster.
        var candidates = db.Players.Where(player => player.DateOfBirth == input.DateOfBirth)
            .OrderBy(player => player.LifecycleStatus == LifecycleStatus.Active ? 0 : 1).ThenBy(player => player.PlayerId)
            .Select(player => new { player.PlayerId, player.FirstName, player.LastName, player.DateOfBirth, player.LifecycleStatus })
            .AsAsyncEnumerable();
        await foreach (var candidate in candidates.WithCancellation(token))
        {
            if (PlayerDuplicateKey.Create(candidate.FirstName, candidate.LastName, candidate.DateOfBirth) == key)
            {
                return new PlayerCreationDuplicate { PlayerId = candidate.PlayerId, LifecycleStatus = candidate.LifecycleStatus };
            }
        }
        return null;
    }

    /// <summary>Stages the player and participation using campaign truth read after the campaign lock.</summary>
    private async Task<ServiceResult<PlayerCreationCompletion>> StageCreationAsync(NovaDbContext db, CreatePlayerInput input,
        DateTimeOffset deadline, CancellationToken token)
    {
        var activeId = await db.Campaigns.Where(campaign => campaign.Status == CampaignStatus.Active)
            .Select(campaign => (long?)campaign.CampaignId).SingleOrDefaultAsync(token);
        if (activeId.HasValue) { await db.AcquireCampaignMutationLockAsync(activeId.Value, token); }
        if (timeProvider.GetUtcNow() >= deadline) { return PlayerCreationProblems.Expired(); }
        var campaign = await db.Campaigns.Where(candidate => candidate.Status == CampaignStatus.Active)
            .Select(candidate => new { candidate.CampaignId, candidate.Name }).SingleOrDefaultAsync(token);
        var player = new PlayerEntity
        {
            ClubId = default,
            CreatedById = default,
            CreationOperationId = input.OperationId,
            FirstName = input.FirstName,
            LastName = input.LastName,
            DateOfBirth = input.DateOfBirth,
            GraduationYear = input.GraduationYear,
            Gender = input.Gender,
            JerseyNumber = input.JerseyNumber,
            LifecycleStatus = LifecycleStatus.Active
        };
        db.Players.Add(player);
        await db.SaveChangesAsync(token);
        PlayerCreationEnrollment? enrollment = null;
        if (campaign is not null)
        {
            CampaignParticipationWriter.StageEnrollments(db, input.ClubId, campaign.CampaignId, [player.PlayerId]);
            await db.SaveChangesAsync(token);
            var participationId = await db.PlayerCampaignAssignments.Where(assignment => assignment.PlayerId == player.PlayerId
                && assignment.CampaignId == campaign.CampaignId).Select(assignment => assignment.PlayerCampaignAssignmentId).SingleAsync(token);
            enrollment = new PlayerCreationEnrollment
            {
                CampaignId = campaign.CampaignId,
                CampaignName = campaign.Name,
                PlayerCampaignAssignmentId = participationId
            };
        }
        return new PlayerCreationCompletion
        {
            OperationId = input.OperationId,
            Player = ToDto(player),
            Enrollment = enrollment,
            CompletedAt = timeProvider.GetUtcNow(),
            RecoveryExpiresAt = deadline
        };
    }

    /// <summary>Checks exact immutable proof without consulting mutable player or campaign state.</summary>
    private async Task<ServiceResult<PlayerCreationCompletion>?> RecoverCreationAsync(NovaDbContext db, Guid operationId,
        long actor, long club, string fingerprint, CancellationToken token)
    {
        var receipt = await db.PlayerCreationReceipts.AsNoTracking().SingleOrDefaultAsync(
            candidate => candidate.ClubId == club && candidate.OperationId == operationId, token);
        if (receipt is null) { return null; }
        if (receipt.ActorUserId != actor || !string.Equals(receipt.RequestSha256, fingerprint, StringComparison.Ordinal))
        {
            return PlayerCreationProblems.Mismatch();
        }
        if (timeProvider.GetUtcNow() >= receipt.RecoveryExpiresAt) { return PlayerCreationProblems.Expired(); }
        var outcome = JsonSerializer.Deserialize<CreationOutcome>(receipt.ResultJson)
            ?? throw new InvalidOperationException("The player creation receipt has no outcome.");
        if (outcome.Problem is { } problem) { return problem; }
        var completion = outcome.Completion ?? throw new InvalidOperationException("The player creation receipt has no completion.");
        LogPlayerCreationCommitRecovered(completion.Player.PlayerId, operationId, club);
        return completion;
    }

    /// <summary>Membership still governs disclosure after an uncertain commit, including deletion of the original club.</summary>
    private async Task<ExecutionResult<ServiceResult<PlayerCreationCompletion>>> VerifyCreationAsync(NovaDbContext db,
        CreatePlayerInput input, long actor, long club, string fingerprint, CancellationToken token)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(token);
        if (!await PlayerMutationAuthorization.AuthorizeAsync(db, actor, club, token))
        {
            return new(true, CreationForbidden());
        }
        var recovered = await RecoverCreationAsync(db, input.OperationId, actor, club, fingerprint, token);
        return new(recovered is not null, recovered!);
    }

    private static ServiceProblem CreationForbidden() => ServiceProblem.Forbidden("You must remain a member of the original club to create or recover a player.");

    private sealed record CreationOutcome(PlayerCreationCompletion? Completion, ServiceProblem? Problem);
}
