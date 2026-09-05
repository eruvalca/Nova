using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nova.Entities;
using Nova.Shared.Enums;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Supplies valid opening receipts for tests that seed Active or Closed campaigns directly instead
/// of exercising the Draft-open workflow. Production contexts never register this interceptor, and
/// provider constraint tests bypass it through <see cref="NovaAppHostFixture.CreateUnnormalizedAdminContext"/>.
/// </summary>
internal sealed class CampaignTestSeedInterceptor : SaveChangesInterceptor
{
    private long nextOpeningSequence;

    /// <inheritdoc />
    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        NormalizeCampaigns(eventData.Context);
        return result;
    }

    /// <inheritdoc />
    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        NormalizeCampaigns(eventData.Context);
        return ValueTask.FromResult(result);
    }

    /// <summary>
    /// Makes direct test seeds satisfy the same lifecycle metadata constraint as production writes.
    /// </summary>
    /// <param name="context">The test context whose tracked campaigns are being saved.</param>
    private void NormalizeCampaigns(DbContext? context)
    {
        if (context is null)
        {
            return;
        }

        // Direct test seeds represent explicit decisions without invoking the placement service.
        // Fill only wholly absent attribution; partial or invalid metadata must still fail constraints.
        foreach (var entry in context.ChangeTracker.Entries<PlayerCampaignAssignmentEntity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            var assignment = entry.Entity;
            if (assignment.PlacementOutcome != PlacementOutcome.Undecided
                && assignment.DecisionRecordedAt is null
                && assignment.DecisionRecordedById is null
                && assignment.DecisionActorDisplayName is null)
            {
                assignment.DecisionRecordedAt = DateTimeOffset.UtcNow;
                assignment.DecisionRecordedById = assignment.CreatedById > 0 ? assignment.CreatedById : 1;
                assignment.DecisionActorDisplayName = "Seeded decision actor";
            }
        }
        foreach (var entry in context.ChangeTracker.Entries<CampaignEntity>()
            .Where(entry => entry.State is EntityState.Added or EntityState.Modified))
        {
            var campaign = entry.Entity;
            if (campaign.Status == CampaignStatus.Draft)
            {
                // Leave deliberately invalid closure metadata intact so provider constraint tests
                // continue to exercise the database instead of being normalized by this seed aid.
                if (campaign.ClosedAt is not null || campaign.ClosedById is not null)
                {
                    continue;
                }

                campaign.OpeningOperationId = null;
                campaign.OpenedAt = null;
                campaign.OpenedById = null;
                campaign.SeasonOpeningSequence = null;
                campaign.InitialEnrolledPlayerCount = null;
                campaign.InitialActiveTeamCount = null;
                campaign.ClosedAt = null;
                campaign.ClosedById = null;
                continue;
            }

            campaign.OpeningOperationId ??= Guid.CreateVersion7();
            campaign.OpenedAt ??= campaign.CreatedAt == default
                ? DateTimeOffset.UtcNow
                : campaign.CreatedAt.ToUniversalTime();
            campaign.OpenedById ??= campaign.CreatedById;
            campaign.SeasonOpeningSequence ??= Interlocked.Increment(ref nextOpeningSequence);
            campaign.InitialEnrolledPlayerCount ??= 0;
            campaign.InitialActiveTeamCount ??= 0;
        }
    }
}
