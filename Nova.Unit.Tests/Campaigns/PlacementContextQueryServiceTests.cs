using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.Unit.Tests.Account;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignPlacementServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlacementOutcome.NotSelected)]
    [InlineData(PlacementOutcome.Withdrawn)]
    public async Task PreviousPlacementRetainsTheLastAssignedEvidenceWithoutReplacingCurrentSeasonDecisionsAsync(PlacementOutcome laterOutcome)
    {
        await SeedPriorDecisionAsync(PlacementOutcome.Assigned, previousSeason: true);
        await using (var seed = _harness.CreateAdminContext())
        {
            (await seed.Seasons.SingleAsync(row => row.SeasonId == 500, TestContext.Current.CancellationToken)).CreationPreviousSeasonId = 510;
            seed.Campaigns.Add(new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = 611,
                Name = "Later prior-season decision",
                ClubId = ClubAId,
                SeasonId = 510,
                SeasonOpeningSequence = 6,
                Status = CampaignStatus.Closed,
                ClosedAt = DateTimeOffset.UtcNow,
                ClosedById = ClubAAdminId,
                CreatedById = ClubAAdminId
            });
            seed.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 311,
                PlayerId = 700,
                CampaignId = 611,
                ClubId = ClubAId,
                PlacementOutcome = laterOutcome,
                ConcurrencyToken = Guid.NewGuid(),
                CreatedById = ClubAAdminId,
                DecisionRecordedById = ClubAAdminId,
                DecisionRecordedAt = DateTimeOffset.UtcNow,
                DecisionActorDisplayName = "Admin A"
            });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        ActAs(ClubAMemberId, ClubAId);
        var service = CreatePlacementContextService();
        var input = new GetPlacementContextInput { CampaignId = 600, PlayerCampaignAssignmentId = ClubAAssignmentId };
        var previous = (await service.GetContextAsync(input, TestContext.Current.CancellationToken)).Value.PreviousPlacement.ShouldNotBeNull();
        previous.Source.Decision.PlayerCampaignAssignmentId.ShouldBe(310);
        previous.Source.Decision.Outcome.ShouldBe(PlacementOutcome.Assigned);
        previous.CanKeep.ShouldBeTrue();

        (await SaveAsync(PlacementOutcome.NotSelected, _clubAConcurrencyToken)).Value.ShouldBeOfType<PlacementMutationSuccess>();
        var after = (await service.GetContextAsync(input, TestContext.Current.CancellationToken)).Value.PreviousPlacement.ShouldNotBeNull();
        after.Source.Decision.PlayerCampaignAssignmentId.ShouldBe(310);
        after.CanKeep.ShouldBeFalse();
        await using var read = _harness.CreateAdminContext();
        var current = await read.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == ClubAAssignmentId, TestContext.Current.CancellationToken);
        current.PlacementOutcome.ShouldBe(PlacementOutcome.NotSelected);
        current.TeamId.ShouldBeNull();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("undecided")]
    [InlineData("missing-team")]
    [InlineData("unexpected-team")]
    [InlineData("campaign-id")]
    [InlineData("campaign-name")]
    [InlineData("previous-outcome")]
    [InlineData("previous-undecided")]
    [InlineData("previous-not-selected-team")]
    [InlineData("previous-withdrawn-team")]
    [InlineData("previous-missing-outcome-team")]
    [InlineData("previous-blank-team")]
    [InlineData("invalid-json")]
    public async Task PlacementContextSkipsMalformedEvidenceWithoutLosingRawPageBoundaryAsync(string shape)
    {
        ActAs(ClubAMemberId, ClubAId);
        var token = _clubAConcurrencyToken;
        for (var index = 0; index < 23; index++)
        {
            token = (await SaveAsync(index % 2 == 0 ? PlacementOutcome.Assigned : PlacementOutcome.NotSelected, token))
                .Value.ShouldBeOfType<PlacementMutationSuccess>().ConcurrencyToken;
        }
        long[] ids;
        await using (var corrupt = _harness.CreateAdminContext())
        {
            var rows = await corrupt.ActivityEvents.OrderByDescending(row => row.ActivityEventId).ToListAsync(TestContext.Current.CancellationToken);
            ids = rows.Select(row => row.ActivityEventId).ToArray();
            var context = System.Text.Json.JsonSerializer.Deserialize<Nova.SharedKernel.Features.Activity.ClubActivityContext>(rows[0].PayloadJson, _caseInsensitiveJsonOptions)
                .ShouldBeOfType<Nova.SharedKernel.Features.Activity.PlacementContext>();
            var malformed = shape switch
            {
                "undecided" => context with { Outcome = PlacementOutcome.Undecided },
                "missing-team" => context with { TeamName = null },
                "unexpected-team" => context with { Outcome = PlacementOutcome.NotSelected },
                "campaign-id" => context with { CampaignId = 0 },
                "campaign-name" => context with { CampaignName = " " },
                "previous-outcome" => context with { PreviousOutcome = (PlacementOutcome)99 },
                "previous-undecided" => context with { PreviousOutcome = PlacementOutcome.Undecided },
                "previous-not-selected-team" => context with { PreviousOutcome = PlacementOutcome.NotSelected, PreviousTeamName = "Stale team" },
                "previous-withdrawn-team" => context with { PreviousOutcome = PlacementOutcome.Withdrawn, PreviousTeamName = "Stale team" },
                "previous-missing-outcome-team" => context with { PreviousOutcome = null, PreviousTeamName = "Stale team" },
                "previous-blank-team" => context with { PreviousOutcome = PlacementOutcome.Assigned, PreviousTeamName = " " },
                "invalid-json" => context,
                _ => throw new ArgumentOutOfRangeException(nameof(shape))
            };
            var json = string.Equals(shape, "invalid-json", StringComparison.Ordinal) ? "{" : System.Text.Json.JsonSerializer.Serialize<Nova.SharedKernel.Features.Activity.ClubActivityContext>(malformed);
            // Simulate corrupt stored snapshots without weakening the append-only application writer.
            await corrupt.ActivityEvents.Where(row => row.ActivityEventId == ids[1] || row.ActivityEventId == ids[19])
                .ExecuteUpdateAsync(setters => setters.SetProperty(row => row.PayloadJson, json), TestContext.Current.CancellationToken);
        }
        var input = new GetPlacementContextInput { CampaignId = 600, PlayerCampaignAssignmentId = ClubAAssignmentId };
        var first = (await CreatePlacementContextService().GetContextAsync(input, TestContext.Current.CancellationToken)).Value;
        first.History.Select(item => item.EventId).ShouldBe(ids.Take(20).Where(id => id != ids[1] && id != ids[19]));
        first.History.ShouldAllBe(item => PlacementHistoryValidation.IsValid(item));
        first.NextEventId.ShouldBe(ids[19]);
        var second = (await CreatePlacementContextService().GetContextAsync(input with { BeforeEventId = first.NextEventId }, TestContext.Current.CancellationToken)).Value;
        second.History.Select(item => item.EventId).ShouldBe(ids.Skip(20));
        second.NextEventId.ShouldBeNull();
    }

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
