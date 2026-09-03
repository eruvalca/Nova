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
file sealed class CampaignReadHarnessDbContextFactory(
    TenancyTestHarness harness,
    CountingCommandInterceptor? interceptor = null) : IDbContextFactory<NovaReadDbContext>
{
    /// <summary>Creates a synchronous read context.</summary>
    /// <returns>A tenant-filtered read context.</returns>
    public NovaReadDbContext CreateDbContext()
        => interceptor is null
            ? harness.CreateReadContext()
            : harness.CreateReadContext(interceptor);
    /// <summary>Creates an asynchronous read context.</summary>
    /// <param name="_">The cancellation token.</param>
    /// <returns>A tenant-filtered read context.</returns>
    public Task<NovaReadDbContext> CreateDbContextAsync(CancellationToken _ = default)
        => Task.FromResult(CreateDbContext());
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
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "A", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "B", State = "MA", CreatedById = ClubAMemberId });

        admin.Users.Add(
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Amelia", LastName = "Member", ClubId = ClubAId });

        var season = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "Season 1", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubAId, CreatedById = ClubAMemberId };
        var seasonB = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = "Season B", StartDate = new DateOnly(2025, 1, 1), ClubId = ClubBId, CreatedById = ClubAMemberId };
        admin.Seasons.AddRange(season, seasonB);
        admin.SaveChanges();
        admin.Clubs.Single(club => club.ClubId == ClubAId).CurrentSeasonId = season.SeasonId;
        admin.Clubs.Single(club => club.ClubId == ClubBId).CurrentSeasonId = seasonB.SeasonId;
        admin.SaveChanges();

        var campaignA = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "A1", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = season.SeasonId, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignA2 = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "A2", StartDate = new DateOnly(2026, 5, 1), Status = CampaignStatus.Closed, ClosedAt = DateTimeOffset.UtcNow, ClosedById = ClubAMemberId, SeasonId = season.SeasonId, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignA3 = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "A3", StartDate = new DateOnly(2026, 4, 1), Status = CampaignStatus.Closed, ClosedAt = DateTimeOffset.UtcNow, ClosedById = 999_999, SeasonId = season.SeasonId, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignB = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "B1", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = seasonB.SeasonId, ClubId = ClubBId, CreatedById = ClubAMemberId };
        admin.Campaigns.AddRange(campaignA, campaignA2, campaignA3, campaignB);
        admin.SaveChanges();

        var playerA = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "P1", LastName = "One", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var playerArchived = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "P2", LastName = "Two", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Archived, ClubId = ClubAId, CreatedById = ClubAMemberId, ArchivedAt = DateTimeOffset.UtcNow, ArchivedById = ClubAMemberId };
        admin.Players.AddRange(playerA, playerArchived);
        admin.Teams.AddRange(
            new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "Active Team", GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = "Archived Team", GraduationYear = 2029, LifecycleStatus = LifecycleStatus.Archived, ClubId = ClubAId, CreatedById = ClubAMemberId, ArchivedAt = DateTimeOffset.UtcNow, ArchivedById = ClubAMemberId });
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
        _harness.CurrentUser.IsClubAdmin = true;

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCampaignListAsync(new GetCampaignListInput { Limit = 1 }, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(3); // three campaigns seeded for club A
        var rows = result.Value.Seasons.SelectMany(s => s.Campaigns).ToList();
        rows.Count.ShouldBe(1); // bounded to limit
        rows[0].ParticipantCount.ShouldBe(2);
        rows[0].UnresolvedCount.ShouldBe(1);
    }

    /// <summary>Verifies all supported status filters are case-insensitive.</summary>
    /// <param name="status">The status spelling supplied to the service.</param>
    /// <param name="expectedStatus">The expected campaign status.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("ACTIVE", CampaignStatus.Active)]
    [InlineData("DRAFT", CampaignStatus.Draft)]
    [InlineData("CLOSED", CampaignStatus.Closed)]
    public async Task GetCampaignList_StatusFiltering_IsCaseInsensitive(
        string status,
        CampaignStatus expectedStatus)
    {
        if (expectedStatus == CampaignStatus.Draft)
        {
            _ = AddDraftCampaign();
        }

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

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

    /// <summary>Verifies a member explicitly requesting Drafts receives an empty successful list.</summary>
    [Fact]
    public async Task GetCampaignList_DraftFilterReturnsEmpty_ForMember()
    {
        _ = AddDraftCampaign();
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;

        var result = await new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance).GetCampaignListAsync(
                new GetCampaignListInput { Status = "draft" },
                TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(0);
        result.Value.Seasons.ShouldBeEmpty();
    }

    /// <summary>Verifies Draft detail is visible to administrators and concealed from members.</summary>
    [Fact]
    public async Task GetCampaignDetail_EnforcesDraftVisibility()
    {
        var campaignId = AddDraftCampaign();
        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;
        var member = await service.GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = campaignId },
            TestContext.Current.CancellationToken);
        member.IsProblem.ShouldBeTrue();
        member.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        _harness.CurrentUser.IsClubAdmin = true;
        var admin = await service.GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = campaignId },
            TestContext.Current.CancellationToken);
        admin.IsSuccess.ShouldBeTrue();
        admin.Value.Status.ShouldBe(CampaignStatus.Draft);
    }

    /// <summary>Verifies setup returns tenant seasons and active lifecycle counts.</summary>
    [Fact]
    public async Task GetCreationSetup_ReturnsSeasonAndActiveCounts()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCreationSetupAsync(TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.CurrentSeason.ShouldNotBeNull();
        result.Value.CurrentSeason.Name.ShouldBe("Season 1");
        result.Value.ActivePlayerCount.ShouldBe(1);
        result.Value.ActiveTeamCount.ShouldBe(1);
    }

    /// <summary>Verifies setup reads the pointer and current-season metadata in one statement.</summary>
    [Fact]
    public async Task GetCreationSetup_UsesOneStatement_ForPointerAndSeason()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;
        var interceptor = new CountingCommandInterceptor();
        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness, interceptor),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCreationSetupAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        interceptor.ReaderExecutionCount.ShouldBe(3);
    }

    /// <summary>Verifies setup never offers historical seasons after more history is inserted.</summary>
    [Fact]
    public async Task GetCreationSetup_ReturnsOnlyCurrentSeason_AfterHistoryIsInserted()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        using (var admin = _harness.CreateAdminContext())
        {
            admin.Seasons.AddRange(Enumerable.Range(1, 101).Select(index => new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Bounded Season {index}",
                StartDate = new DateOnly(2027, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAMemberId
            }));
            admin.SaveChanges();
        }

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCreationSetupAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CurrentSeason.ShouldNotBeNull();
        result.Value.CurrentSeason.Name.ShouldBe("Season 1");
    }

    /// <summary>Verifies campaign rows follow the contracted deterministic keys.</summary>
    [Fact]
    public async Task GetCampaignList_OrdersCampaignsByContractedKeys()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        using (var admin = _harness.CreateAdminContext())
        {
            admin.Campaigns.Single(campaign => campaign.ClubId == ClubAId
                && campaign.Status == CampaignStatus.Active).Status = CampaignStatus.Draft;
            var season = new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
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
                    CreationOperationId = Guid.NewGuid(),
                    Name = "Later",
                    StartDate = new DateOnly(2027, 6, 2),
                    Status = CampaignStatus.Draft,
                    SeasonId = season.SeasonId,
                    ClubId = ClubAId,
                    CreatedById = ClubAMemberId
                },
                new CampaignEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    Name = "Z",
                    StartDate = sameDate,
                    EndDate = sameEnd,
                    Status = CampaignStatus.Draft,
                    SeasonId = season.SeasonId,
                    ClubId = ClubAId,
                    CreatedById = ClubAMemberId
                },
                new CampaignEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    Name = "Earlier End",
                    StartDate = sameDate,
                    EndDate = sameEnd.AddDays(-1),
                    Status = CampaignStatus.Draft,
                    SeasonId = season.SeasonId,
                    ClubId = ClubAId,
                    CreatedById = ClubAMemberId
                },
                new CampaignEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    Name = "A",
                    StartDate = sameDate,
                    EndDate = sameEnd,
                    Status = CampaignStatus.Draft,
                    SeasonId = season.SeasonId,
                    ClubId = ClubAId,
                    CreatedById = ClubAMemberId
                },
                new CampaignEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    Name = "Open",
                    StartDate = sameDate,
                    Status = CampaignStatus.Active,
                    SeasonId = season.SeasonId,
                    ClubId = ClubAId,
                    CreatedById = ClubAMemberId
                },
                new CampaignEntity
                {
                    CreationOperationId = Guid.NewGuid(),
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
            .ShouldBe(["Open", "Later", "A", "Z", "Earlier End", "Closed"]);
    }

    /// <summary>Verifies the detail query returns the club's campaign header payload.</summary>
    [Fact]
    public async Task GetCampaignDetail_ReturnsDetail_ForClubsCampaign()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        long campaignId;
        long seasonId;
        using (var admin = _harness.CreateAdminContext())
        {
            campaignId = admin.Campaigns.Single(campaign => campaign.Name == "A1").CampaignId;
            seasonId = admin.Campaigns.Single(campaign => campaign.Name == "A1").SeasonId;
        }

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = campaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CampaignId.ShouldBe(campaignId);
        result.Value.Name.ShouldBe("A1");
        result.Value.Status.ShouldBe(CampaignStatus.Active);
        result.Value.StartDate.ShouldBe(new DateOnly(2026, 6, 1));
        result.Value.PlannedEndDate.ShouldBeNull();
        result.Value.ParticipantCount.ShouldBe(2);
        result.Value.SeasonId.ShouldBe(seasonId);
        result.Value.SeasonName.ShouldBe("Season 1");
        result.Value.ClosedAt.ShouldBeNull();
        result.Value.ClosedByUserId.ShouldBeNull();
        result.Value.ClosedByDisplayName.ShouldBeNull();
    }

    /// <summary>Verifies a Closed campaign's detail carries populated closure fields with a resolved display name.</summary>
    [Fact]
    public async Task GetCampaignDetail_ReturnsClosureFields_ForClosedCampaign()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        long campaignId;
        using (var admin = _harness.CreateAdminContext())
        {
            campaignId = admin.Campaigns.Single(campaign => campaign.Name == "A2").CampaignId;
        }

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = campaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(CampaignStatus.Closed);
        result.Value.ClosedAt.ShouldNotBeNull();
        result.Value.ClosedByUserId.ShouldBe(ClubAMemberId);
        result.Value.ClosedByDisplayName.ShouldBe("Amelia Member");
    }

    /// <summary>Verifies a Closed campaign with a missing closer row falls back to the "Former member" display name.</summary>
    [Fact]
    public async Task GetCampaignDetail_ReturnsFormerMemberDisplayName_WhenCloserIsUnavailable()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        long campaignId;
        using (var admin = _harness.CreateAdminContext())
        {
            campaignId = admin.Campaigns.Single(campaign => campaign.Name == "A3").CampaignId;
        }

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = campaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(CampaignStatus.Closed);
        result.Value.ClosedAt.ShouldNotBeNull();
        result.Value.ClosedByUserId.ShouldBe(999_999);
        result.Value.ClosedByDisplayName.ShouldBe("Former member");
    }

    /// <summary>Verifies the detail query returns NotFound for another club's campaign.</summary>
    [Fact]
    public async Task GetCampaignDetail_ReturnsNotFound_ForOtherClubsCampaign()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        long clubBCampaignId;
        using (var admin = _harness.CreateAdminContext())
        {
            clubBCampaignId = admin.Campaigns.Single(campaign => campaign.Name == "B1").CampaignId;
        }

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = clubBCampaignId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>Verifies the detail query returns NotFound for a missing campaign id.</summary>
    [Fact]
    public async Task GetCampaignDetail_ReturnsNotFound_ForMissingCampaign()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = 999999 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>Verifies the detail query retains its service-layer membership guard.</summary>
    [Fact]
    public async Task GetCampaignDetail_ReturnsForbidden_WhenNotMember()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = 1 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies non-positive campaign identifiers are rejected before any query.</summary>
    [Fact]
    public async Task GetCampaignDetail_ReturnsValidationProblem_ForNonPositiveCampaignId()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var service = new CampaignQueryService(
            new CampaignReadHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>Adds one Draft campaign to Club A and returns its generated identifier.</summary>
    private long AddDraftCampaign()
    {
        using var db = _harness.CreateAdminContext();
        var seasonId = db.Seasons.Where(season => season.ClubId == ClubAId)
            .Select(season => season.SeasonId)
            .First();
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Draft {Guid.NewGuid():N}",
            StartDate = new DateOnly(2026, 7, 1),
            Status = CampaignStatus.Draft,
            SeasonId = seasonId,
            ClubId = ClubAId,
            CreatedById = ClubAMemberId
        };
        db.Campaigns.Add(campaign);
        db.SaveChanges();
        return campaign.CampaignId;
    }
}
