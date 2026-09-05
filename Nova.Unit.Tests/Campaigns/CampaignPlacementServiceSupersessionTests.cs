using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Nova.Shared.Features.Campaigns;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacementServiceTests
{
    /// <summary>Checks technical enrollment has no decision attribution until an explicit member save.</summary>
    [Fact]
    public async Task UpdatePlacementAsync_RecordsDecisionAttribution_WithoutReplacingEnrollmentAuthor()
    {
        await using (var before = _harness.CreateAdminContext())
        {
            var row = await before.PlayerCampaignAssignments.FindAsync([ClubAAssignmentId], TestContext.Current.CancellationToken);
            row!.DecisionRecordedAt.ShouldBeNull();
            row.DecisionRecordedById.ShouldBeNull();
            row.DecisionActorDisplayName.ShouldBeNull();
        }

        ActAs(ClubAMemberId, ClubAId);
        var result = await SaveAsync(PlacementOutcome.NotSelected, _clubAConcurrencyToken);
        result.Value.ShouldBeOfType<PlacementMutationSuccess>();

        await using var verify = _harness.CreateAdminContext();
        var saved = await verify.PlayerCampaignAssignments.FindAsync([ClubAAssignmentId], TestContext.Current.CancellationToken);
        saved!.CreatedById.ShouldBe(ClubAAdminId);
        saved.DecisionRecordedById.ShouldBe(ClubAMemberId);
        saved.DecisionActorDisplayName.ShouldBe("Member M");
        saved.DecisionRecordedAt.ShouldNotBeNull();
    }

    /// <summary>Checks unchanged decisions preserve attribution, token, activity, and receipt counts.</summary>
    /// <param name="outcome">The saved outcome to repeat.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.Assigned)]
    [InlineData(PlacementOutcome.NotSelected)]
    [InlineData(PlacementOutcome.Withdrawn)]
    public async Task UpdatePlacementAsync_IsNoOp_WhenLocalDecisionAndTokenMatch(PlacementOutcome outcome)
    {
        ActAs(ClubAMemberId, ClubAId);
        var first = (await SaveAsync(outcome, _clubAConcurrencyToken)).Value.ShouldBeOfType<PlacementMutationSuccess>();
        DateTimeOffset? recordedAt;
        await using (var before = _harness.CreateAdminContext())
        {
            recordedAt = (await before.PlayerCampaignAssignments.FindAsync([ClubAAssignmentId], TestContext.Current.CancellationToken))!.DecisionRecordedAt;
        }

        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var repeated = (await SaveAsync(outcome, first.ConcurrencyToken)).Value.ShouldBeOfType<PlacementMutationSuccess>();
        repeated.ConcurrencyToken.ShouldBe(first.ConcurrencyToken);
        (await SaveAsync(outcome, _clubAConcurrencyToken)).Value.ShouldBeOfType<PlacementConflict>();

        await using var verify = _harness.CreateAdminContext();
        var saved = await verify.PlayerCampaignAssignments.FindAsync([ClubAAssignmentId], TestContext.Current.CancellationToken);
        saved!.DecisionRecordedAt.ShouldBe(recordedAt);
        saved.DecisionRecordedById.ShouldBe(ClubAMemberId);
        (await verify.ActivityEvents.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.PlacementMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    /// <summary>Checks owning-campaign withdrawal is terminal for both authorized member roles.</summary>
    /// <param name="isAdmin">Whether the attempting actor is an administrator.</param>
    /// <param name="outcome">The replacement outcome.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, PlacementOutcome.Assigned)]
    [InlineData(false, PlacementOutcome.NotSelected)]
    [InlineData(true, PlacementOutcome.Assigned)]
    [InlineData(true, PlacementOutcome.NotSelected)]
    public async Task UpdatePlacementAsync_RejectsReplacementOfLocalWithdrawal_WithoutWrites(bool isAdmin, PlacementOutcome outcome)
    {
        ActAs(ClubAMemberId, ClubAId);
        var first = (await SaveAsync(PlacementOutcome.Withdrawn, _clubAConcurrencyToken)).Value.ShouldBeOfType<PlacementMutationSuccess>();
        ActAs(isAdmin ? ClubAAdminId : ClubAMemberId, ClubAId, isAdmin);

        (await SaveAsync(outcome, first.ConcurrencyToken)).Value.ShouldBeOfType<PlacementConflict>();

        await using var verify = _harness.CreateAdminContext();
        var row = await verify.PlayerCampaignAssignments.FindAsync([ClubAAssignmentId], TestContext.Current.CancellationToken);
        row!.PlacementOutcome.ShouldBe(PlacementOutcome.Withdrawn);
        row.ConcurrencyToken.ShouldBe(first.ConcurrencyToken);
        row.DecisionRecordedById.ShouldBe(ClubAMemberId);
        (await verify.ActivityEvents.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
        (await verify.PlacementMutationReceipts.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(1);
    }

    /// <summary>Checks later-campaign supersession leaves the source decision unchanged and snapshots it.</summary>
    /// <param name="priorOutcome">The prior saved outcome.</param>
    /// <param name="isAdmin">Whether the actor may override prior withdrawal.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.Assigned, false)]
    [InlineData(PlacementOutcome.NotSelected, false)]
    [InlineData(PlacementOutcome.Withdrawn, true)]
    public async Task UpdatePlacementAsync_SupersedesPriorDecision_AndPreservesClosedHistory(PlacementOutcome priorOutcome, bool isAdmin)
    {
        var priorToken = await SeedPriorDecisionAsync(priorOutcome);
        ActAs(isAdmin ? ClubAAdminId : ClubAMemberId, ClubAId, isAdmin);

        (await SaveAsync(PlacementOutcome.Assigned, _clubAConcurrencyToken)).Value.ShouldBeOfType<PlacementMutationSuccess>();

        await using var verify = _harness.CreateAdminContext();
        var prior = await verify.PlayerCampaignAssignments.FindAsync([310L], TestContext.Current.CancellationToken);
        prior!.PlacementOutcome.ShouldBe(priorOutcome);
        prior.ConcurrencyToken.ShouldBe(priorToken);
        prior.DecisionRecordedById.ShouldBe(ClubAAdminId);
        prior.ModifiedAt.ShouldBeNull();
        var activity = await verify.ActivityEvents.SingleAsync(TestContext.Current.CancellationToken);
        activity.EventKind.ShouldBe(ActivityEventKind.PlacementSuperseded);
        var payload = JsonSerializer.Deserialize<ClubActivityContext>(activity.PayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).ShouldBeOfType<PlacementContext>();
        payload.PreviousOutcome.ShouldBe(priorOutcome);
        payload.Outcome.ShouldBe(PlacementOutcome.Assigned);
        payload.PreviousTeamName.ShouldBe(priorOutcome == PlacementOutcome.Assigned ? "Eligible" : null);
        payload.TeamName.ShouldBe("Eligible");
    }

    /// <summary>Checks a member cannot replace prior withdrawal and rejection creates no side effects.</summary>
    [Fact]
    public async Task UpdatePlacementAsync_ForbidsMemberPriorWithdrawalOverride_WithoutWrites()
    {
        await SeedPriorDecisionAsync(PlacementOutcome.Withdrawn);
        ActAs(ClubAMemberId, ClubAId);

        (await SaveAsync(PlacementOutcome.NotSelected, _clubAConcurrencyToken)).Value.ShouldBeOfType<PlacementForbidden>();

        await using var verify = _harness.CreateAdminContext();
        var row = await verify.PlayerCampaignAssignments.FindAsync([ClubAAssignmentId], TestContext.Current.CancellationToken);
        row!.PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        row.ConcurrencyToken.ShouldBe(_clubAConcurrencyToken);
        row.DecisionRecordedAt.ShouldBeNull();
        (await verify.ActivityEvents.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.PlacementMutationReceipts.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    /// <summary>Checks a previous season's withdrawal does not restrict the current season.</summary>
    [Fact]
    public async Task UpdatePlacementAsync_ResetsEligibility_WhenWithdrawalBelongsToPreviousSeason()
    {
        await SeedPriorDecisionAsync(PlacementOutcome.Withdrawn, previousSeason: true);
        ActAs(ClubAMemberId, ClubAId);

        (await SaveAsync(PlacementOutcome.NotSelected, _clubAConcurrencyToken)).Value.ShouldBeOfType<PlacementMutationSuccess>();

        await using var verify = _harness.CreateAdminContext();
        var activity = await verify.ActivityEvents.SingleAsync(TestContext.Current.CancellationToken);
        activity.EventKind.ShouldNotBe(ActivityEventKind.PlacementSuperseded);
        var payload = JsonSerializer.Deserialize<ClubActivityContext>(activity.PayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).ShouldBeOfType<PlacementContext>();
        payload.PreviousOutcome.ShouldBeNull();
    }

    /// <summary>Checks an Active target is rejected when its season is not the club's current season.</summary>
    [Fact]
    public async Task UpdatePlacementAsync_RejectsNonCurrentSeason_WithoutWrites()
    {
        await using (var arrange = _harness.CreateAdminContext())
        {
            (await arrange.Clubs.FindAsync([ClubAId], TestContext.Current.CancellationToken))!.CurrentSeasonId = null;
            await arrange.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        ActAs(ClubAMemberId, ClubAId);

        (await SaveAsync(PlacementOutcome.NotSelected, _clubAConcurrencyToken)).Value.ShouldBeOfType<PlacementConflict>();

        await using var verify = _harness.CreateAdminContext();
        (await verify.PlayerCampaignAssignments.FindAsync([ClubAAssignmentId], TestContext.Current.CancellationToken))!.ConcurrencyToken.ShouldBe(_clubAConcurrencyToken);
        (await verify.ActivityEvents.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
        (await verify.PlacementMutationReceipts.AnyAsync(TestContext.Current.CancellationToken)).ShouldBeFalse();
    }

    /// <summary>Checks selection uses opening order before team validity and never revives an older withdrawal.</summary>
    [Fact]
    public async Task UpdatePlacementAsync_UsesLatestDecisionBeforeTeamValidity_WithoutHistoricalFallback()
    {
        await SeedPriorDecisionAsync(PlacementOutcome.Withdrawn);
        await using (var seed = _harness.CreateAdminContext())
        {
            seed.Campaigns.Add(new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = 609,
                Name = "Latest Closed decision",
                ClubId = ClubAId,
                SeasonId = 500,
                SeasonOpeningSequence = 7,
                Status = CampaignStatus.Closed,
                ClosedAt = DateTimeOffset.UtcNow,
                ClosedById = ClubAAdminId,
                CreatedById = ClubAAdminId
            });
            seed.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 309,
                PlayerId = 700,
                CampaignId = 609,
                ClubId = ClubAId,
                PlacementOutcome = PlacementOutcome.Assigned,
                TeamId = EligibleTeamId,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedById = ClubAAdminId,
                DecisionRecordedAt = DateTimeOffset.UtcNow.AddDays(-2),
                DecisionRecordedById = ClubAAdminId,
                DecisionActorDisplayName = "Admin A"
            });
            var team = await seed.Teams.FindAsync([EligibleTeamId], TestContext.Current.CancellationToken);
            team!.LifecycleStatus = LifecycleStatus.Archived;
            team.ArchivedAt = DateTimeOffset.UtcNow;
            team.ArchivedById = ClubAAdminId;
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        ActAs(ClubAMemberId, ClubAId);

        var result = await CreateService().UpdatePlacementAsync(new UpdateCampaignPlacementInput(
            ClubAAssignmentId, PlacementOutcome.Assigned, SecondEligibleTeamId, _clubAConcurrencyToken), TestContext.Current.CancellationToken);
        result.Value.ShouldBeOfType<PlacementMutationSuccess>();

        await using var verify = _harness.CreateAdminContext();
        var activity = await verify.ActivityEvents.SingleAsync(TestContext.Current.CancellationToken);
        activity.EventKind.ShouldBe(ActivityEventKind.PlacementSuperseded);
        var payload = JsonSerializer.Deserialize<ClubActivityContext>(activity.PayloadJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).ShouldBeOfType<PlacementContext>();
        payload.PreviousOutcome.ShouldBe(PlacementOutcome.Assigned);
        payload.PreviousTeamName.ShouldBe("Eligible");
        payload.TeamName.ShouldBe("Eligible 2");
        (await verify.PlayerCampaignAssignments.FindAsync([310L], TestContext.Current.CancellationToken))!.PlacementOutcome.ShouldBe(PlacementOutcome.Withdrawn);
    }
    /// <summary>Saves the primary participation with a valid team only for Assigned.</summary>
    /// <param name="outcome">The requested saved decision.</param>
    /// <param name="token">The expected current token.</param>
    /// <returns>The actual service result.</returns>
    private Task<PlacementUpdateResult> SaveAsync(PlacementOutcome outcome, Guid token)
        => CreateService().UpdatePlacementAsync(new UpdateCampaignPlacementInput(ClubAAssignmentId, outcome,
            outcome == PlacementOutcome.Assigned ? EligibleTeamId : null, token), TestContext.Current.CancellationToken);

    /// <summary>Seeds an explicitly attributed Closed decision before the Active target campaign.</summary>
    /// <param name="outcome">The historical saved outcome.</param>
    /// <param name="previousSeason">Whether the decision is outside the current season.</param>
    /// <returns>The source decision token for immutable-history assertions.</returns>
    private async Task<Guid> SeedPriorDecisionAsync(PlacementOutcome outcome, bool previousSeason = false)
    {
        await using var db = _harness.CreateAdminContext();
        (await db.Campaigns.FindAsync([600L], TestContext.Current.CancellationToken))!.SeasonOpeningSequence = 10;
        if (previousSeason)
        {
            db.Seasons.Add(new SeasonEntity { CreationOperationId = Guid.NewGuid(), SeasonId = 510, Name = "Previous season", StartDate = new DateOnly(2025, 1, 1), ClubId = ClubAId, CreatedById = ClubAAdminId });
        }
        db.Campaigns.Add(new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            CampaignId = 610,
            Name = "Earlier decision",
            ClubId = ClubAId,
            SeasonId = previousSeason ? 510 : 500,
            SeasonOpeningSequence = 5,
            Status = CampaignStatus.Closed,
            ClosedAt = DateTimeOffset.UtcNow,
            ClosedById = ClubAAdminId,
            CreatedById = ClubAAdminId
        });
        var token = Guid.NewGuid();
        db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
        {
            PlayerCampaignAssignmentId = 310,
            PlayerId = 700,
            CampaignId = 610,
            ClubId = ClubAId,
            PlacementOutcome = outcome,
            TeamId = outcome == PlacementOutcome.Assigned ? EligibleTeamId : null,
            ConcurrencyToken = token,
            CreatedById = ClubAAdminId,
            DecisionRecordedById = ClubAAdminId,
            DecisionRecordedAt = DateTimeOffset.UtcNow.AddDays(-1),
            DecisionActorDisplayName = "Admin A"
        });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return token;
    }
}
