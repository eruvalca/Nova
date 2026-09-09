using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Attention;
using Nova.Features.Campaigns;
using Nova.Features.Dashboard;
using Nova.Features.Teams;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Attention;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Security;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class EffectivePlacementConsumerTests : IDisposable
{
    private readonly TenancyTestHarness _harness = new();

    public EffectivePlacementConsumerTests()
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.Add(new ClubEntity { CreatedById = 2, ClubId = 1, CreationOperationId = Guid.NewGuid(), Name = "Club", City = "Austin", State = "TX" });
        db.Users.Add(new NovaUserEntity { Id = 2, ClubId = 1, FirstName = "Member", LastName = "One" });
        db.Seasons.Add(new SeasonEntity { CreatedById = 2, SeasonId = 3, ClubId = 1, CreationOperationId = Guid.NewGuid(), Name = "Season", StartDate = new(2026, 1, 1) });
        db.Teams.Add(new TeamEntity { CreatedById = 2, TeamId = 4, ClubId = 1, CreationOperationId = Guid.NewGuid(), Name = "Team", GraduationYear = 2028 });
        db.Players.Add(new PlayerEntity { CreatedById = 2, PlayerId = 5, ClubId = 1, CreationOperationId = Guid.NewGuid(), FirstName = "Player", LastName = "One", GraduationYear = 2028, DateOfBirth = new(2010, 1, 1) });
        db.SaveChanges();
        db.Clubs.Single().CurrentSeasonId = 3;
        db.Campaigns.AddRange(
            new CampaignEntity { CreatedById = 2, CampaignId = 6, ClubId = 1, SeasonId = 3, CreationOperationId = Guid.NewGuid(), Name = "Earlier", StartDate = new(2026, 2, 1), Status = CampaignStatus.Closed, SeasonOpeningSequence = 1, ClosedAt = DateTimeOffset.UnixEpoch, ClosedById = 2 },
            new CampaignEntity { CreatedById = 2, CampaignId = 7, ClubId = 1, SeasonId = 3, CreationOperationId = Guid.NewGuid(), Name = "Active", StartDate = new(2026, 3, 1), Status = CampaignStatus.Active, SeasonOpeningSequence = 2 });
        db.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity { CreatedById = 2, PlayerCampaignAssignmentId = 8, PlayerId = 5, CampaignId = 6, ClubId = 1, PlacementOutcome = PlacementOutcome.Assigned, TeamId = 4, DecisionRecordedAt = DateTimeOffset.UnixEpoch, DecisionRecordedById = 2, DecisionActorDisplayName = "Member One" },
            new PlayerCampaignAssignmentEntity { CreatedById = 2, PlayerCampaignAssignmentId = 9, PlayerId = 5, CampaignId = 7, ClubId = 1 });
        db.SaveChanges();
        _harness.CurrentUser.UserId = 2;
        _harness.CurrentUser.ClubId = 1;
        _harness.CurrentUser.IsClubAdmin = true;
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ZeroNeedsPlacementDoesNotWaiveMissingCampaignLocalCloseOutcomeAsync()
    {
        var service = new CampaignCloseoutQueryService(Factory(), _harness.CurrentUser,
            new CampaignPlacementQueryService(Factory(), _harness.CurrentUser, NullLogger<CampaignPlacementQueryService>.Instance),
            NullLogger<CampaignCloseoutQueryService>.Instance);
        var result = await service.GetCloseoutReadinessAsync(new() { CampaignId = 7 }, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.NeedsPlacementCount.ShouldBe(0);
        result.Value.Summary.UndecidedCount.ShouldBe(1);
        result.Value.IsReady.ShouldBeFalse();
        result.Value.Blockers.ShouldHaveSingleItem().AssignmentIds.ShouldBe([9L]);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task CampaignDashboardAndAttentionAgreeOnEffectiveNeedsPlacementAsync(bool archiveTeam, int expected)
    {
        if (archiveTeam)
        {
            using var db = _harness.CreateAdminContext();
            var team = db.Teams.Single();
            team.LifecycleStatus = LifecycleStatus.Archived;
            team.ArchivedAt = DateTimeOffset.UnixEpoch;
            team.ArchivedById = 2;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var campaigns = new CampaignQueryService(Factory(), _harness.CurrentUser, NullLogger<CampaignQueryService>.Instance);
        var list = await campaigns.GetCampaignListAsync(new() { Status = "active" }, TestContext.Current.CancellationToken);
        list.IsSuccess.ShouldBeTrue();
        list.Value.Seasons.ShouldHaveSingleItem().Campaigns.ShouldHaveSingleItem().UnresolvedCount.ShouldBe(expected);
        var dashboard = new DashboardQueryService(campaigns, Factory(), _harness.CurrentUser, NullLogger<DashboardQueryService>.Instance);
        var summary = await dashboard.GetDashboardAsync(TestContext.Current.CancellationToken);
        summary.IsSuccess.ShouldBeTrue();
        summary.Value.ActiveCampaigns.ShouldHaveSingleItem().UnresolvedCount.ShouldBe(expected);
        var attention = new ClubAttentionQueryService(Factory(), _harness.CurrentUser, NullLogger<ClubAttentionQueryService>.Instance);
        var result = await attention.GetClubAttentionAsync(TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.NeedsPlacement.Status.ShouldBe(AttentionRegionStatus.Loaded);
        result.Value.NeedsPlacement.Count.ShouldBe(expected);
        result.Value.NeedsPlacement.CampaignId.ShouldBe(expected == 0 ? null : 7L);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, 0)]
    [InlineData(true, 1)]
    public async Task TeamCountsSeparateEffectiveRosterFromActiveCampaignContributionAsync(bool saveLocal, int contribution)
    {
        if (saveLocal)
        {
            using var db = _harness.CreateAdminContext();
            var local = db.PlayerCampaignAssignments.Single(a => a.CampaignId == 7);
            local.PlacementOutcome = PlacementOutcome.Assigned;
            local.TeamId = 4;
            local.DecisionRecordedAt = DateTimeOffset.UnixEpoch.AddDays(1);
            local.DecisionRecordedById = 2;
            local.DecisionActorDisplayName = "New author";
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        var directory = new TeamRosterQueryService(Factory(), _harness.CurrentUser, NullLogger<TeamRosterQueryService>.Instance);
        var roster = await directory.GetRosterAsync(new GetTeamRosterInput(), TestContext.Current.CancellationToken);
        roster.IsSuccess.ShouldBeTrue();
        var team = roster.Value.ShouldHaveSingleItem();
        team.EffectiveCurrentSeasonPlacementCount.ShouldBe(1);
        team.CurrentCampaignPlacementContribution.ShouldBe(contribution);
        team.ActivePlacementCount.ShouldBe(contribution);
        var details = new TeamDetailQueryService(Factory(), _harness.CurrentUser, NullLogger<TeamDetailQueryService>.Instance);
        var detail = await details.GetTeamDetailAsync(4, TestContext.Current.CancellationToken);
        detail.IsSuccess.ShouldBeTrue();
        detail.Value.EffectiveCurrentSeasonPlacementCount.ShouldBe(1);
        detail.Value.CurrentCampaignPlacementContribution.ShouldBe(contribution);
        detail.Value.PlacementHistoryTotalCount.ShouldBe(1 + contribution);
    }

    [Fact]
    public async Task PlacementReadEndpointsAdvertiseMemberAuthorizationAndResponseContractsAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddSingleton(Substitute.For<IEffectivePlacementQueryService>());
        await using var app = builder.Build();
        app.MapEffectivePlacementEndpoints();
        var endpoints = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        endpoints.Length.ShouldBe(3);
        foreach (var endpoint in endpoints)
        {
            endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>().ShouldContain(data => data.Policy == Policies.RequireClubMember);
            endpoint.Metadata.GetMetadata<HttpMethodMetadata>()!.HttpMethods.ShouldBe([HttpMethods.Get]);
            endpoint.Metadata.GetMetadata<IEndpointNameMetadata>()!.EndpointName.ShouldNotBeNullOrEmpty();
            var statuses = endpoint.Metadata.GetOrderedMetadata<IProducesResponseTypeMetadata>().Select(item => item.StatusCode).ToArray();
            statuses.ShouldContain(200);
            statuses.ShouldContain(400);
            statuses.ShouldContain(401);
            statuses.ShouldContain(403);
            statuses.ShouldContain(404);
            statuses.ShouldContain(500);
            if (endpoint.RoutePattern.RawText!.Contains("campaigns", StringComparison.Ordinal))
            {
                statuses.ShouldContain(409);
            }
        }
    }

    private TestDbContextFactory<NovaReadDbContext> Factory() => new(_harness.CreateReadContext);
}
