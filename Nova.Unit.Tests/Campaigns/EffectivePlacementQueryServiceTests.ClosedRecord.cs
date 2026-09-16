using Nova.Entities;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class EffectivePlacementQueryServiceTests
{
    [Fact]
    public async Task ClosedRecordKeepsWholeTotalsAndOriginalCloserWithArchivedFilteredRowsAsync()
    {
        var archived = AddPlayer("Archived", archived: true);
        AddDecision(archived, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(AddPlayer("Not selected"), PriorCampaignId, PlacementOutcome.NotSelected);
        AddDecision(AddPlayer("Withdrawn"), PriorCampaignId, PlacementOutcome.Withdrawn);
        using (var db = _harness.CreateAdminContext())
        {
            var team = db.Teams.Single(row => row.TeamId == TeamId);
            team.LifecycleStatus = LifecycleStatus.Archived;
            team.ArchivedAt = DateTimeOffset.UnixEpoch;
            team.ArchivedById = MemberId;
            db.Users.Single(row => row.Id == MemberId).FirstName = "Renamed";
            db.ActivityEvents.Add(new ActivityEventEntity
            {
                ClubId = ClubId,
                CampaignId = PriorCampaignId,
                EventKind = ActivityEventKind.CampaignClosed,
                ActorUserId = 999,
                ActorDisplayName = "Departed closer",
                PayloadJson = "{}",
                CreatedById = MemberId,
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = (await CreateService().GetClosedCampaignRosterAsync(new()
        { CampaignId = PriorCampaignId, Search = "Archived", LocalOutcome = "assigned", PageSize = 1 }, TestContext.Current.CancellationToken)).Value;

        result.Summary.ShouldBe(new(1, 1, 1, 0, 3));
        result.ParticipantCount.ShouldBe(3);
        result.Participants.TotalCount.ShouldBe(1);
        result.ClosingEvent.ActorDisplayName.ShouldBe("Departed closer");
        result.ClosingEvent.ActorUserId.ShouldBe(999);
        var row = result.Participants.Items.ShouldHaveSingleItem();
        row.PlayerLifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        row.TeamLifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        row.Source.Decision.ActorDisplayName.ShouldBe("Original decision maker");
        row.Source.Team!.TeamId.ShouldBe(TeamId);
    }

    [Fact]
    public async Task MissingClosureIsIntegrityConflictEvenWhenDiscoveryWouldReturnNoRowsAsync()
    {
        using (var db = _harness.CreateAdminContext())
        {
            var campaign = db.Campaigns.Single(row => row.CampaignId == ActiveCampaignId);
            campaign.Status = CampaignStatus.Closed;
            campaign.ClosedAt = DateTimeOffset.UnixEpoch;
            campaign.ClosedById = MemberId;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var result = await CreateService().GetClosedCampaignRosterAsync(new()
        { CampaignId = ActiveCampaignId, Search = "No match" }, TestContext.Current.CancellationToken);
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Errors.ShouldNotBeNull().ShouldContainKey(ClosedCampaignRecordErrors.Integrity);
    }
}
