using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class EffectivePlacementQueryServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ExactTryoutRelevancePrecedesPagingAndPreservesNameAndAssignmentTiesAsync(bool closed, bool filtered)
    {
        var campaignId = closed ? PriorCampaignId : ActiveCampaignId;
        var players = Enumerable.Range(0, 23).Select(index => AddPlayer(
            index switch { 0 => "Zulu", 1 => "Alpha", 22 => "Exact", _ => "Echo" }, index == 22 ? "Zulu" : "Able42")).ToArray();
        var assignments = Enumerable.Range(0, 23).Select(index => AddDecision(
            players[index switch { 2 => 3, 3 => 2, _ => index }],
            campaignId, PlacementOutcome.NotSelected, tryoutNumber: index == 22 ? 42 : index + 100)).ToArray();
        var nameOrder = assignments.Skip(1).Take(21).Select(item => item.PlayerCampaignAssignmentId)
            .Append(assignments[0].PlayerCampaignAssignmentId).Append(assignments[22].PlayerCampaignAssignmentId).ToArray();
        var relevanceOrder = new[] { assignments[22].PlayerCampaignAssignmentId }.Concat(nameOrder.Take(22)).ToArray();
        // Existing Roster ties use player ID; relevance deliberately uses assignment ID instead.
        (nameOrder[1], nameOrder[2]) = (nameOrder[2], nameOrder[1]);
        var roster = await ReadRelevancePageAsync(campaignId, closed, filtered, null, 1);
        roster.ShouldBe(nameOrder.Take(20));
        roster.ShouldNotContain(assignments[22].PlayerCampaignAssignmentId);

        var first = await ReadRelevancePageAsync(campaignId, closed, filtered, "searchRelevance", 1);
        var second = await ReadRelevancePageAsync(campaignId, closed, filtered, "searchRelevance", 2);

        first.Length.ShouldBe(20);
        first[0].ShouldBe(assignments[22].PlayerCampaignAssignmentId);
        second.Length.ShouldBe(3);
        first.Concat(second).ShouldBe(relevanceOrder);
    }

    private async Task<long[]> ReadRelevancePageAsync(long campaignId, bool closed, bool filtered, string? sort, int page)
    {
        if (closed)
        {
            var result = await CreateService().GetClosedCampaignRosterAsync(new()
            {
                CampaignId = campaignId,
                Search = "42",
                SortBy = sort,
                Page = page,
                PageSize = 20,
            }, TestContext.Current.CancellationToken);
            result.IsSuccess.ShouldBeTrue();
            result.Value.Participants.TotalCount.ShouldBe(23);
            return result.Value.Participants.Items.Select(item => item.PlayerCampaignAssignmentId).ToArray();
        }
        var active = await WorkAsync(new()
        {
            CampaignId = campaignId,
            Search = "42",
            SortBy = sort,
            Page = page,
            PageSize = 20,
            Eligibility = filtered ? nameof(EffectivePlacementEligibility.Resolved) : null,
        });
        active.Participants.TotalCount.ShouldBe(23);
        return active.Participants.Items.Select(item => item.PlayerCampaignAssignmentId).ToArray();
    }
}
