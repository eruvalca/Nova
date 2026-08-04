using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

file sealed class CampaignReadHarnessDbContextFactory(TenancyTestHarness harness) : IDbContextFactory<NovaReadDbContext>
{
    public NovaReadDbContext CreateDbContext() => harness.CreateReadContext();
    public Task<NovaReadDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(harness.CreateReadContext());
}

public sealed class CampaignQueryServiceTests : IDisposable
{
    private const long ClubAId = 1000;
    private const long ClubBId = 2000;
    private const long ClubAMemberId = 1001;

    private readonly TenancyTestHarness _harness = new();

    public CampaignQueryServiceTests()
    {
        Seed();
    }

    public void Dispose() => _harness.Dispose();

    private void Seed()
    {
        using var admin = _harness.CreateAdminContext();

        admin.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "A", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "B", State = "MA", CreatedById = ClubAMemberId });

        var season = new SeasonEntity { Name = "Season 1", StartDate = new DateOnly(2026,1,1), ClubId = ClubAId, CreatedById = ClubAMemberId };
        var seasonB = new SeasonEntity { Name = "Season B", StartDate = new DateOnly(2025,1,1), ClubId = ClubBId, CreatedById = ClubAMemberId };
        admin.Seasons.AddRange(season, seasonB);
        admin.SaveChanges();

        var campaignA = new CampaignEntity { Name = "A1", StartDate = new DateOnly(2026,6,1), Status = CampaignStatus.Active, SeasonId = season.SeasonId, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignA2 = new CampaignEntity { Name = "A2", StartDate = new DateOnly(2026,5,1), Status = CampaignStatus.Closed, ClosedAt = DateTimeOffset.UtcNow, ClosedById = ClubAMemberId, SeasonId = season.SeasonId, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignB = new CampaignEntity { Name = "B1", StartDate = new DateOnly(2026,6,1), Status = CampaignStatus.Active, SeasonId = seasonB.SeasonId, ClubId = ClubBId, CreatedById = ClubAMemberId };
        admin.Campaigns.AddRange(campaignA, campaignA2, campaignB);
        admin.SaveChanges();

        var playerA = new PlayerEntity { FirstName = "P1", LastName = "One", DateOfBirth = new DateOnly(2010,1,1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var playerArchived = new PlayerEntity { FirstName = "P2", LastName = "Two", DateOfBirth = new DateOnly(2010,1,1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Archived, ClubId = ClubAId, CreatedById = ClubAMemberId, ArchivedAt = DateTimeOffset.UtcNow, ArchivedById = ClubAMemberId };
        admin.Players.AddRange(playerA, playerArchived);
        admin.SaveChanges();

        admin.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity { PlayerId = playerA.PlayerId, CampaignId = campaignA.CampaignId, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new PlayerCampaignAssignmentEntity { PlayerId = playerArchived.PlayerId, CampaignId = campaignA.CampaignId, ClubId = ClubAId, CreatedById = ClubAMemberId, PlacementOutcome = PlacementOutcome.Undecided }
        );
        admin.SaveChanges();
    }

    [Fact]
    public async Task GetCampaignList_ReturnsForbidden_WhenNotMember()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCampaignListAsync(new GetCampaignListInput(), TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetCampaignList_TotalCountIsBeforeLimit_AndTenantIsolated()
    {
        // Act as club A member
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCampaignListAsync(new GetCampaignListInput { Limit = 1 }, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(2); // two campaigns seeded for club A
        var rows = result.Value.Seasons.SelectMany(s => s.Campaigns).ToList();
        rows.Count.ShouldBe(1); // bounded to limit
        rows[0].ParticipantCount.ShouldBeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task GetCampaignList_StatusFiltering_IsCaseInsensitive()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var closed = await service.GetCampaignListAsync(new GetCampaignListInput { Status = "CLOSED" }, TestContext.Current.CancellationToken);
        closed.IsSuccess.ShouldBeTrue();
        var rows = closed.Value.Seasons.SelectMany(s => s.Campaigns).ToList();
        rows.ShouldAllBe(r => r.Status == CampaignStatus.Closed);
    }

    [Fact]
    public async Task GetCreationSetup_ReturnsSeasonAndActiveCounts()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCreationSetupAsync(TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalSeasonCount.ShouldBeGreaterThanOrEqualTo(1);
        result.Value.Seasons.Count.ShouldBeGreaterThanOrEqualTo(1);
        result.Value.ActivePlayerCount.ShouldBeGreaterThanOrEqualTo(1);
        result.Value.ActiveTeamCount.ShouldBeGreaterThanOrEqualTo(0);
    }
}
