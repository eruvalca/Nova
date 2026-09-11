using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Data;

public sealed partial class EffectivePlacementPostgresTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task ExactTryoutIsPromotedBeforeSqlPagingOnBothCampaignReadsAsync(bool closed, bool filtered)
    {
        var seed = await SeedAsync(23);
        var campaignId = closed ? seed.LatestClosedId : seed.ActiveId;
        var ids = await PrepareRelevanceParticipantsAsync(seed, campaignId);
        using var user = fixture.UseUser(1, seed.ClubId, isClubAdmin: false);
        var nameOrder = ids.Skip(1).Take(21).Append(ids[0]).Append(ids[22]).ToArray();
        var baseline = await ReadRelevancePageAsync(campaignId, closed, filtered, null, 1);
        baseline.ShouldBe(nameOrder.Take(20));
        baseline.ShouldNotContain(ids[22]);

        var first = await ReadRelevancePageAsync(campaignId, closed, filtered, "searchRelevance", 1);
        var second = await ReadRelevancePageAsync(campaignId, closed, filtered, "searchRelevance", 2);

        first.Length.ShouldBe(20);
        first[0].ShouldBe(ids[22]);
        second.Length.ShouldBe(3);
        first.Concat(second).ShouldBe(new[] { ids[22] }.Concat(nameOrder.Take(22)));
    }

    private async Task<long[]> PrepareRelevanceParticipantsAsync(Seed seed, long campaignId)
    {
        await using var db = fixture.CreateAdminContext();
        var assignments = await db.PlayerCampaignAssignments.Include(item => item.Player).Where(item => item.CampaignId == campaignId)
            .OrderBy(item => item.PlayerCampaignAssignmentId).ToListAsync(TestContext.Current.CancellationToken);
        for (var index = 0; index < assignments.Count; index++)
        {
            assignments[index].Player.FirstName = index switch { 0 => "Zulu", 1 => "Alpha", 22 => "Exact", _ => "Echo" };
            assignments[index].Player.LastName = index == 22 ? "Zulu" : "Able42";
            assignments[index].TryoutNumber = index == 22 ? 42 : index + 100;
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        seed.PlayerIds.Length.ShouldBe(23);
        return assignments.Select(item => item.PlayerCampaignAssignmentId).ToArray();
    }

    private async Task<long[]> ReadRelevancePageAsync(long campaignId, bool closed, bool filtered, string? sort, int page)
    {
        var service = CreateService();
        if (closed)
        {
            var result = await service.GetClosedCampaignRosterAsync(new()
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
        var active = await service.GetCampaignEffectivePlacementsAsync(new()
        {
            CampaignId = campaignId,
            Search = "42",
            SortBy = sort,
            Page = page,
            PageSize = 20,
            Eligibility = filtered ? nameof(EffectivePlacementEligibility.OptionalReassignment) : null,
        }, TestContext.Current.CancellationToken);
        active.IsSuccess.ShouldBeTrue();
        active.Value.Participants.TotalCount.ShouldBe(23);
        return active.Value.Participants.Items.Select(item => item.PlayerCampaignAssignmentId).ToArray();
    }
}
