using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class EffectivePlacementQueryServiceTests
{
    [Fact]
    public async Task ClosedCloseoutOrderKeepsAssignedBeforeTerminalOutcomesAcrossPagesAsync()
    {
        var withdrawn = AddDecision(AddPlayer("Aaron"), PriorCampaignId, PlacementOutcome.Withdrawn);
        var notSelected = AddDecision(AddPlayer("Aaron"), PriorCampaignId, PlacementOutcome.NotSelected);
        var assigned = AddDecision(AddPlayer("Zulu"), PriorCampaignId, PlacementOutcome.Assigned, TeamId);
        var ids = new List<long>();
        for (var page = 1; page <= 2; page++)
        {
            var result = await CreateService().GetClosedCampaignRosterAsync(new()
            {
                CampaignId = PriorCampaignId,
                SortBy = "closeout",
                PageSize = 2,
                Page = page,
            }, TestContext.Current.CancellationToken);
            result.IsSuccess.ShouldBeTrue();
            result.Value.Participants.TotalCount.ShouldBe(3);
            ids.AddRange(result.Value.Participants.Items.Select(row => row.PlayerCampaignAssignmentId));
        }
        ids.ShouldBe([assigned.PlayerCampaignAssignmentId, notSelected.PlayerCampaignAssignmentId, withdrawn.PlayerCampaignAssignmentId]);
    }

    [Fact]
    public async Task CloseoutOrderGroupsLocalTeamsThenTerminalOutcomesAcrossBoundedPagesAsync()
    {
        var missing = AddDecision(AddPlayer("Aaron"), ActiveCampaignId, PlacementOutcome.Undecided);
        var withdrawn = AddDecision(AddPlayer("Aaron", archived: true), ActiveCampaignId, PlacementOutcome.Withdrawn);
        var notSelected = AddDecision(AddPlayer("Aaron"), ActiveCampaignId, PlacementOutcome.NotSelected);
        var beta = AddDecision(AddPlayer("Aaron"), ActiveCampaignId, PlacementOutcome.Assigned, OtherTeamId);
        var alpha1 = AddDecision(AddPlayer("Same", "Name"), ActiveCampaignId, PlacementOutcome.Assigned, TeamId);
        var alpha2 = AddDecision(AddPlayer("Same", "Name"), ActiveCampaignId, PlacementOutcome.Assigned, TeamId);
        long[] expected = [alpha1.PlayerCampaignAssignmentId, alpha2.PlayerCampaignAssignmentId, beta.PlayerCampaignAssignmentId, notSelected.PlayerCampaignAssignmentId, withdrawn.PlayerCampaignAssignmentId, missing.PlayerCampaignAssignmentId];
        var ids = new List<long>();
        for (var page = 1; page <= 3; page++)
        {
            var result = await WorkAsync(new() { CampaignId = ActiveCampaignId, SortBy = "closeout", PageSize = 2, Page = page });
            result.Participants.TotalCount.ShouldBe(6);
            result.Participants.Items.Count.ShouldBe(2);
            ids.AddRange(result.Participants.Items.Select(row => row.PlayerCampaignAssignmentId));
        }
        ids.ShouldBe(expected);
    }

    [Fact]
    public async Task ExactBlockersIncludeInheritedAndArchivedPlayersAndOverlapWithoutFilteringTotalsAsync()
    {
        var inheritedPlayer = AddPlayer("Inherited");
        AddDecision(inheritedPlayer, PriorCampaignId, PlacementOutcome.Assigned, OtherTeamId);
        var inherited = AddDecision(inheritedPlayer, ActiveCampaignId, PlacementOutcome.Undecided);
        var archived = AddDecision(AddPlayer("Archived", archived: true), ActiveCampaignId, PlacementOutcome.Undecided);
        var overlapping = AddDecision(AddPlayer("Bad team", graduationYear: 2027), ActiveCampaignId, PlacementOutcome.Assigned, TeamId);
        using (var db = _harness.CreateAdminContext())
        {
            var team = db.Teams.Single(team => team.TeamId == TeamId);
            team.LifecycleStatus = LifecycleStatus.Archived;
            team.ArchivedAt = DateTimeOffset.UtcNow;
            team.ArchivedById = MemberId;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var missing = await WorkAsync(new() { CampaignId = ActiveCampaignId, CloseoutBlocker = "outcomes", SortBy = "closeout" });
        missing.Participants.Items.Select(row => row.PlayerCampaignAssignmentId).Order().ShouldBe(new[] { inherited.PlayerCampaignAssignmentId, archived.PlayerCampaignAssignmentId }.Order());
        missing.Counts.OptionalReassignment.ShouldBe(1);
        missing.Counts.Unavailable.ShouldBe(1);
        foreach (var condition in new[] { "eligibility", "archivedTeams" })
        {
            var result = await WorkAsync(new() { CampaignId = ActiveCampaignId, CloseoutBlocker = condition });
            result.Participants.Items.ShouldHaveSingleItem().PlayerCampaignAssignmentId.ShouldBe(overlapping.PlayerCampaignAssignmentId);
            result.Counts.ShouldBe(missing.Counts);
        }
        (await WorkAsync(new() { CampaignId = ActiveCampaignId, CloseoutBlocker = "outcomes", Search = "Inherited" }))
            .Participants.Items.ShouldHaveSingleItem().LocalDecision.ShouldBeNull();
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("unknown")]
    public async Task CloseoutRejectsMalformedConditionAsync(string condition)
    {
        var result = await CreateService().GetCampaignEffectivePlacementsAsync(new() { CampaignId = ActiveCampaignId, CloseoutBlocker = condition }, TestContext.Current.CancellationToken);
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }
}
