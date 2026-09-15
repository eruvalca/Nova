using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.Unit.Tests.Account;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacementServiceTests
{
    [Fact]
    public async Task PlacementContextPagesTwentyChangesWithoutDuplicatesAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        var token = _clubAConcurrencyToken;
        for (var index = 0; index < 23; index++)
        {
            var outcome = index % 2 == 0 ? PlacementOutcome.Assigned : PlacementOutcome.NotSelected;
            token = (await SaveAsync(outcome, token)).Value.ShouldBeOfType<PlacementMutationSuccess>().ConcurrencyToken;
        }
        var service = CreatePlacementContextService();
        var input = new GetPlacementContextInput { CampaignId = 600, PlayerCampaignAssignmentId = ClubAAssignmentId };

        var first = await service.GetContextAsync(input, TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        first.Value.History.Count.ShouldBe(20);
        first.Value.NextEventId.ShouldBe(first.Value.History[^1].EventId);
        first.Value.History.Select(item => item.EventId).ShouldBeInOrder(SortDirection.Descending);
        first.Value.History.ShouldAllBe(item => item.CampaignId == 600 && item.ActorDisplayName == "Member M");
        first.Value.History[0].Outcome.ShouldBe(PlacementOutcome.Assigned);
        first.Value.History[0].PreviousOutcome.ShouldBe(PlacementOutcome.NotSelected);
        first.Value.History[0].TeamName.ShouldBe("Eligible");
        var second = await service.GetContextAsync(input with { BeforeEventId = first.Value.NextEventId }, TestContext.Current.CancellationToken);
        second.IsSuccess.ShouldBeTrue();
        second.Value.History.Count.ShouldBe(3);
        second.Value.NextEventId.ShouldBeNull();
        first.Value.History.Select(item => item.EventId).Intersect(second.Value.History.Select(item => item.EventId)).ShouldBeEmpty();
        second.Value.History[^1].PreviousOutcome.ShouldBeNull();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviousPlacementFollowsAdvancementAncestryAndFreshTeamEligibilityAsync(bool archived)
    {
        await SeedPriorDecisionAsync(PlacementOutcome.Assigned, previousSeason: true);
        await using (var seed = _harness.CreateAdminContext())
        {
            var current = await seed.Seasons.SingleAsync(row => row.SeasonId == 500, TestContext.Current.CancellationToken);
            current.CreationPreviousSeasonId = 510;
            var previous = await seed.Seasons.SingleAsync(row => row.SeasonId == 510, TestContext.Current.CancellationToken);
            previous.StartDate = new DateOnly(2030, 1, 1);
            if (archived)
            {
                var team = await seed.Teams.SingleAsync(row => row.TeamId == EligibleTeamId, TestContext.Current.CancellationToken);
                team.LifecycleStatus = LifecycleStatus.Archived;
                team.ArchivedAt = DateTimeOffset.UtcNow;
                team.ArchivedById = ClubAAdminId;
            }
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        ActAs(ClubAMemberId, ClubAId);

        var result = await CreatePlacementContextService().GetContextAsync(
            new GetPlacementContextInput { CampaignId = 600, PlayerCampaignAssignmentId = ClubAAssignmentId }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PreviousPlacement.ShouldNotBeNull();
        result.Value.PreviousPlacement.Season.SeasonId.ShouldBe(510);
        result.Value.PreviousPlacement.Source.Decision.PlayerCampaignAssignmentId.ShouldBe(310);
        result.Value.PreviousPlacement.CanKeep.ShouldBe(!archived);
        result.Value.History.ShouldBeEmpty();
    }

    [Fact]
    public async Task PlacementContextRejectsRemovedMembershipWithStaleClaimsAsync()
    {
        ActAs(ClubAMemberId, ClubAId);
        await using (var seed = _harness.CreateAdminContext())
        {
            var user = await seed.Users.SingleAsync(row => row.Id == ClubAMemberId, TestContext.Current.CancellationToken);
            user.ClubId = null;
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var result = await CreatePlacementContextService().GetContextAsync(
            new GetPlacementContextInput { CampaignId = 600, PlayerCampaignAssignmentId = ClubAAssignmentId }, TestContext.Current.CancellationToken);

        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    private PlacementContextQueryService CreatePlacementContextService() => new(
        new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext), _harness.CurrentUser,
        NullLogger<PlacementContextQueryService>.Instance);
}
