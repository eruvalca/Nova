using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Creates read contexts backed by the shared tenancy test harness.
/// </summary>
/// <param name="harness">The shared SQLite tenancy harness.</param>
file sealed class CampaignReadHarnessDbContextFactory(TenancyTestHarness harness) : IDbContextFactory<NovaReadDbContext>
{
    /// <summary>Creates a synchronous read context.</summary>
    /// <returns>A tenant-filtered read context.</returns>
    public NovaReadDbContext CreateDbContext() => harness.CreateReadContext();
    /// <summary>Creates an asynchronous read context.</summary>
    /// <param name="_">The cancellation token.</param>
    /// <returns>A tenant-filtered read context.</returns>
    public Task<NovaReadDbContext> CreateDbContextAsync(CancellationToken _ = default)
        => Task.FromResult(harness.CreateReadContext());
}

/// <summary>
/// Verifies tenant-safe campaign query service behavior.
/// </summary>
public sealed class CampaignQueryServiceTests : IDisposable
{
    /// <summary>Identifies the primary test club.</summary>
    private const long ClubAId = 1000;
    /// <summary>Identifies the isolated second test club.</summary>
    private const long ClubBId = 2000;
    /// <summary>Identifies the approved primary-club member.</summary>
    private const long ClubAMemberId = 1001;

    /// <summary>Provides the shared SQLite database and current-user context.</summary>
    private readonly TenancyTestHarness _harness = new();

    /// <summary>Initializes a test instance with cross-tenant campaign data.</summary>
    public CampaignQueryServiceTests()
    {
        Seed();
    }

    /// <summary>Releases the tenancy harness.</summary>
    public void Dispose() => _harness.Dispose();

    /// <summary>Seeds campaigns, lifecycle data, and assignments for both clubs.</summary>
    private void Seed()
    {
        using var admin = _harness.CreateAdminContext();

        admin.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "A", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "B", State = "MA", CreatedById = ClubAMemberId });

        var season = new SeasonEntity { Name = "Season 1", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubAId, CreatedById = ClubAMemberId };
        var seasonB = new SeasonEntity { Name = "Season B", StartDate = new DateOnly(2025, 1, 1), ClubId = ClubBId, CreatedById = ClubAMemberId };
        admin.Seasons.AddRange(season, seasonB);
        admin.SaveChanges();

        var campaignA = new CampaignEntity { Name = "A1", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = season.SeasonId, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignA2 = new CampaignEntity { Name = "A2", StartDate = new DateOnly(2026, 5, 1), Status = CampaignStatus.Closed, ClosedAt = DateTimeOffset.UtcNow, ClosedById = ClubAMemberId, SeasonId = season.SeasonId, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignB = new CampaignEntity { Name = "B1", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = seasonB.SeasonId, ClubId = ClubBId, CreatedById = ClubAMemberId };
        admin.Campaigns.AddRange(campaignA, campaignA2, campaignB);
        admin.SaveChanges();

        var playerA = new PlayerEntity { FirstName = "P1", LastName = "One", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var playerArchived = new PlayerEntity { FirstName = "P2", LastName = "Two", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Archived, ClubId = ClubAId, CreatedById = ClubAMemberId, ArchivedAt = DateTimeOffset.UtcNow, ArchivedById = ClubAMemberId };
        admin.Players.AddRange(playerA, playerArchived);
        admin.Teams.AddRange(
            new TeamEntity { Name = "Active Team", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new TeamEntity { Name = "Archived Team", GraduationYear = 2029, LifecycleStatus = LifecycleStatus.Archived, ClubId = ClubAId, CreatedById = ClubAMemberId, ArchivedAt = DateTimeOffset.UtcNow, ArchivedById = ClubAMemberId });
        admin.SaveChanges();

        admin.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity { PlayerId = playerA.PlayerId, CampaignId = campaignA.CampaignId, ClubId = ClubAId, CreatedById = ClubAMemberId, PlacementOutcome = PlacementOutcome.NotSelected },
            new PlayerCampaignAssignmentEntity { PlayerId = playerArchived.PlayerId, CampaignId = campaignA.CampaignId, ClubId = ClubAId, CreatedById = ClubAMemberId, PlacementOutcome = PlacementOutcome.Undecided }
        );
        admin.SaveChanges();
    }

    /// <summary>Verifies list queries reject callers without approved membership.</summary>
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

    /// <summary>
    /// Verifies creation setup retains its service-layer membership guard.
    /// </summary>
    [Fact]
    public async Task GetCreationSetup_ReturnsForbidden_WhenNotMember()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;
        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCreationSetupAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies count-before-bound behavior, tenant isolation, and assignment counts.</summary>
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
        rows[0].ParticipantCount.ShouldBe(2);
        rows[0].UnresolvedCount.ShouldBe(1);
    }

    /// <summary>Verifies both supported status filters are case-insensitive.</summary>
    /// <param name="status">The status spelling supplied to the service.</param>
    /// <param name="expectedStatus">The expected campaign status.</param>
    [Theory]
    [InlineData("ACTIVE", CampaignStatus.Active)]
    [InlineData("CLOSED", CampaignStatus.Closed)]
    public async Task GetCampaignList_StatusFiltering_IsCaseInsensitive(
        string status,
        CampaignStatus expectedStatus)
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCampaignListAsync(
            new GetCampaignListInput { Status = status },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var rows = result.Value.Seasons.SelectMany(season => season.Campaigns).ToList();
        rows.ShouldNotBeEmpty();
        rows.ShouldAllBe(campaign => campaign.Status == expectedStatus);
    }

    /// <summary>Verifies setup returns tenant seasons and active lifecycle counts.</summary>
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
        result.Value.ActivePlayerCount.ShouldBe(1);
        result.Value.ActiveTeamCount.ShouldBe(1);
    }

    /// <summary>Verifies setup returns the newest bounded choices and the pre-bound total.</summary>
    [Fact]
    public async Task GetCreationSetup_ReturnsNewestHundredSeasons_AndTotalBeforeBound()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        using (var admin = _harness.CreateAdminContext())
        {
            admin.Seasons.AddRange(Enumerable.Range(1, 101).Select(index => new SeasonEntity
            {
                Name = $"Bounded Season {index}",
                StartDate = new DateOnly(2027, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            }));
            admin.SaveChanges();
        }

        using var verification = _harness.CreateAdminContext();
        var expectedIds = verification.Seasons
            .Where(season => season.ClubId == ClubAId && season.StartDate == new DateOnly(2027, 1, 1))
            .OrderByDescending(season => season.StartDate)
            .ThenByDescending(season => season.SeasonId)
            .Select(season => season.SeasonId)
            .ToList();

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCreationSetupAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalSeasonCount.ShouldBe(102);
        result.Value.Seasons.Count.ShouldBe(CampaignCreationSetupResult.MaxSeasonChoices);
        result.Value.Seasons.Select(season => season.SeasonId)
            .ShouldBe(expectedIds.Take(CampaignCreationSetupResult.MaxSeasonChoices));
    }

    /// <summary>Verifies campaign rows follow the contracted deterministic keys.</summary>
    [Fact]
    public async Task GetCampaignList_OrdersCampaignsByContractedKeys()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        using (var admin = _harness.CreateAdminContext())
        {
            var season = new SeasonEntity
            {
                Name = "Ordering Season",
                StartDate = new DateOnly(2027, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            };
            admin.Seasons.Add(season);
            admin.SaveChanges();

            var sameDate = new DateOnly(2027, 6, 1);
            var sameEnd = new DateOnly(2027, 6, 20);
            admin.Campaigns.AddRange(
                new CampaignEntity
                {
                    Name = "Later",
                    StartDate = new DateOnly(2027, 6, 2),
                    Status = CampaignStatus.Active,
                    SeasonId = season.SeasonId,
                    ClubId = ClubAId,
                    CreatedById = ClubAMemberId
                },
                new CampaignEntity
                {
                    Name = "Z",
                    StartDate = sameDate,
                    EndDate = sameEnd,
                    Status = CampaignStatus.Active,
                    SeasonId = season.SeasonId,
                    ClubId = ClubAId,
                    CreatedById = ClubAMemberId
                },
                new CampaignEntity
                {
                    Name = "Earlier End",
                    StartDate = sameDate,
                    EndDate = sameEnd.AddDays(-1),
                    Status = CampaignStatus.Active,
                    SeasonId = season.SeasonId,
                    ClubId = ClubAId,
                    CreatedById = ClubAMemberId
                },
                new CampaignEntity
                {
                    Name = "A",
                    StartDate = sameDate,
                    EndDate = sameEnd,
                    Status = CampaignStatus.Active,
                    SeasonId = season.SeasonId,
                    ClubId = ClubAId,
                    CreatedById = ClubAMemberId
                },
                new CampaignEntity
                {
                    Name = "Open",
                    StartDate = sameDate,
                    Status = CampaignStatus.Active,
                    SeasonId = season.SeasonId,
                    ClubId = ClubAId,
                    CreatedById = ClubAMemberId
                },
                new CampaignEntity
                {
                    Name = "Closed",
                    StartDate = sameDate,
                    Status = CampaignStatus.Closed,
                    ClosedAt = DateTimeOffset.UtcNow,
                    ClosedById = ClubAMemberId,
                    SeasonId = season.SeasonId,
                    ClubId = ClubAId,
                    CreatedById = ClubAMemberId
                });
            admin.SaveChanges();
        }

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCampaignListAsync(
            new GetCampaignListInput { Limit = 100 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var rows = result.Value.Seasons
            .Single(season => season.Name == "Ordering Season")
            .Campaigns;
        rows.Select(campaign => campaign.Name).Take(6)
            .ShouldBe(["Later", "A", "Z", "Earlier End", "Open", "Closed"]);
    }
}
