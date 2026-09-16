using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class EffectivePlacementQueryServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("event-id")]
    [InlineData("actor-zero")]
    [InlineData("actor-negative")]
    [InlineData("timestamp")]
    [InlineData("actor-name")]
    public async Task ClosedRecordRejectsMalformedClosureBeforeDiscoveryAsync(string defect)
    {
        AddDecision(AddPlayer(), PriorCampaignId, PlacementOutcome.NotSelected);
        var input = new GetClosedCampaignRosterInput { CampaignId = PriorCampaignId, Search = "No match" };
        (await CreateService().GetClosedCampaignRosterAsync(input, TestContext.Current.CancellationToken)).Value.Participants.Items.ShouldBeEmpty();
        using var db = _harness.CreateAdminContext();
        var closure = db.ActivityEvents.Where(row => row.CampaignId == PriorCampaignId);
        // Inject damaged stored evidence without the audit/seed interceptors repairing it.
        var changed = defect switch
        {
            "event-id" => await closure.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ActivityEventId, -1), TestContext.Current.CancellationToken),
            "actor-zero" => await closure.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ActorUserId, 0), TestContext.Current.CancellationToken),
            "actor-negative" => await closure.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ActorUserId, -1), TestContext.Current.CancellationToken),
            "timestamp" => await closure.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.CreatedAt, default(DateTimeOffset)), TestContext.Current.CancellationToken),
            "actor-name" => await closure.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ActorDisplayName, "\t"), TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        changed.ShouldBe(1);
        var result = await CreateService().GetClosedCampaignRosterAsync(input, TestContext.Current.CancellationToken);
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Errors.ShouldNotBeNull().ShouldContainKey(ClosedCampaignRecordErrors.Integrity);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("timestamp")]
    [InlineData("token")]
    [InlineData("whitespace")]
    public async Task ClosedRecordRejectsMalformedDecisionBeforeDiscoveryAsync(string defect)
    {
        var decision = AddDecision(AddPlayer(), PriorCampaignId, PlacementOutcome.NotSelected);
        using var db = _harness.CreateAdminContext();
        var query = db.PlayerCampaignAssignments.Where(row => row.PlayerCampaignAssignmentId == decision.PlayerCampaignAssignmentId);
        var whitespace = new string(Enumerable.Range(char.MinValue, char.MaxValue + 1).Select(value => (char)value).Where(char.IsWhiteSpace).ToArray());
        var changed = defect switch
        {
            "timestamp" => await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DecisionRecordedAt, default(DateTimeOffset)), TestContext.Current.CancellationToken),
            "token" => await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.ConcurrencyToken, Guid.Empty), TestContext.Current.CancellationToken),
            "whitespace" => await query.ExecuteUpdateAsync(setters => setters.SetProperty(row => row.DecisionActorDisplayName, whitespace), TestContext.Current.CancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(defect)),
        };
        changed.ShouldBe(1);
        var result = await CreateService().GetClosedCampaignRosterAsync(new()
        { CampaignId = PriorCampaignId, Search = "No match" }, TestContext.Current.CancellationToken);
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Errors.ShouldNotBeNull().ShouldContainKey(ClosedCampaignRecordErrors.Integrity);
    }

    [Fact]
    public async Task ClosedRecordKeepsWholeTotalsAndOriginalCloserWithArchivedFilteredRowsAsync()
    {
        var archived = AddPlayer("Archived", archived: true);
        AddDecision(archived, PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        AddDecision(AddPlayer("Not selected"), PriorCampaignId, PlacementOutcome.NotSelected);
        AddDecision(AddPlayer("Withdrawn"), PriorCampaignId, PlacementOutcome.Withdrawn);
        using (var db = _harness.CreateAdminContext())
        {
            var team = await db.Teams.SingleAsync(row => row.TeamId == TeamId, TestContext.Current.CancellationToken);
            team.LifecycleStatus = LifecycleStatus.Archived;
            team.ArchivedAt = DateTimeOffset.UnixEpoch;
            team.ArchivedById = MemberId;
            (await db.Users.SingleAsync(row => row.Id == MemberId, TestContext.Current.CancellationToken)).FirstName = "Renamed";
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
            var campaign = await db.Campaigns.SingleAsync(row => row.CampaignId == ActiveCampaignId, TestContext.Current.CancellationToken);
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
