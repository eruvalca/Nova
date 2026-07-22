using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Tests campaign close and reopen authorization, blockers, and append-only lifecycle history.
/// </summary>
public sealed class CampaignLifecycleServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAAdminId = 200;
    private const long ClubAMemberId = 201;
    private const long ClubBAdminId = 202;
    private const long ReadyCampaignId = 600;
    private const long BlockedCampaignId = 601;
    private const long ClosedCampaignId = 602;
    private const long ClubBCampaignId = 603;
    private const long EligibleTeamId = 800;
    private const long IneligibleTeamId = 801;
    private const long ArchivedTeamId = 802;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>
    /// Initializes seeded campaign lifecycle data for two clubs.
    /// </summary>
    public CampaignLifecycleServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies a club administrator can close a campaign when all close conditions succeed.
    /// </summary>
    [Fact]
    public async Task CloseAsync_ClosesCampaign_AndAppendsClosedEvent_WhenConditionsPass()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.CloseAsync(ReadyCampaignId, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var campaign = await verify.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == ReadyCampaignId, TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Closed);
        campaign.ClosedById.ShouldBe(ClubAAdminId);
        campaign.ClosedAt.ShouldNotBeNull();

        var events = await verify.CampaignLifecycleEvents
            .Where(candidate => candidate.CampaignId == ReadyCampaignId)
            .OrderBy(candidate => candidate.CampaignLifecycleEventId)
            .ToListAsync(TestContext.Current.CancellationToken);
        events.Count.ShouldBe(1);
        events[0].EventType.ShouldBe(CampaignLifecycleEventType.Closed);
        events[0].ClubId.ShouldBe(ClubAId);
        events[0].CreatedById.ShouldBe(ClubAAdminId);
    }

    /// <summary>
    /// Verifies close returns every blocker condition and does not partially transition the campaign.
    /// </summary>
    [Fact]
    public async Task CloseAsync_ReturnsAllBlockers_AndLeavesCampaignActive_WhenConditionsFail()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.CloseAsync(BlockedCampaignId, TestContext.Current.CancellationToken);

        result.IsT3.ShouldBeTrue();
        result.AsT3.Errors.ShouldContainKey("outcomes");
        result.AsT3.Errors.ShouldContainKey("eligibility");
        result.AsT3.Errors.ShouldContainKey("archivedTeams");

        await using var verify = _harness.CreateAdminContext();
        var campaign = await verify.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == BlockedCampaignId, TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        campaign.ClosedAt.ShouldBeNull();
        campaign.ClosedById.ShouldBeNull();
        (await verify.CampaignLifecycleEvents
            .AnyAsync(candidate => candidate.CampaignId == BlockedCampaignId, TestContext.Current.CancellationToken))
            .ShouldBeFalse();
    }

    /// <summary>
    /// Verifies reopening clears closure metadata, appends a reopen event, and preserves participation outcomes.
    /// </summary>
    [Fact]
    public async Task ReopenAsync_ClearsClosureMetadata_AndAppendsReopenedEvent()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ReopenAsync(ClosedCampaignId, TestContext.Current.CancellationToken);

        result.IsT0.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var campaign = await verify.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == ClosedCampaignId, TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        campaign.ClosedAt.ShouldBeNull();
        campaign.ClosedById.ShouldBeNull();

        var events = await verify.CampaignLifecycleEvents
            .Where(candidate => candidate.CampaignId == ClosedCampaignId)
            .OrderBy(candidate => candidate.CampaignLifecycleEventId)
            .ToListAsync(TestContext.Current.CancellationToken);
        events.Count.ShouldBe(2);
        events[0].EventType.ShouldBe(CampaignLifecycleEventType.Closed);
        events[1].EventType.ShouldBe(CampaignLifecycleEventType.Reopened);

        var outcomes = await verify.PlayerCampaignAssignments
            .Where(candidate => candidate.CampaignId == ClosedCampaignId)
            .Select(candidate => candidate.PlacementOutcome)
            .Distinct()
            .ToListAsync(TestContext.Current.CancellationToken);
        outcomes.ShouldBe([PlacementOutcome.Assigned], ignoreOrder: true);
    }

    /// <summary>
    /// Verifies repeated close and reopen cycles retain every lifecycle event in order.
    /// </summary>
    [Fact]
    public async Task LifecycleTransitions_PreserveAllEvents_AcrossRepeatedCloseReopenCycles()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        (await service.ReopenAsync(
            ClosedCampaignId,
            TestContext.Current.CancellationToken)).IsT0.ShouldBeTrue();
        (await service.CloseAsync(
            ClosedCampaignId,
            TestContext.Current.CancellationToken)).IsT0.ShouldBeTrue();
        (await service.ReopenAsync(
            ClosedCampaignId,
            TestContext.Current.CancellationToken)).IsT0.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var campaign = await verify.Campaigns
            .SingleAsync(candidate => candidate.CampaignId == ClosedCampaignId, TestContext.Current.CancellationToken);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        campaign.ClosedAt.ShouldBeNull();
        campaign.ClosedById.ShouldBeNull();

        var eventTypes = await verify.CampaignLifecycleEvents
            .Where(candidate => candidate.CampaignId == ClosedCampaignId)
            .OrderBy(candidate => candidate.CampaignLifecycleEventId)
            .Select(candidate => candidate.EventType)
            .ToListAsync(TestContext.Current.CancellationToken);
        eventTypes.ShouldBe(
        [
            CampaignLifecycleEventType.Closed,
            CampaignLifecycleEventType.Reopened,
            CampaignLifecycleEventType.Closed,
            CampaignLifecycleEventType.Reopened
        ]);
    }

    /// <summary>
    /// Verifies non-admin users cannot close campaigns.
    /// </summary>
    [Fact]
    public async Task CloseAsync_ReturnsForbidden_WhenCallerIsNotClubAdmin()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.CloseAsync(ReadyCampaignId, TestContext.Current.CancellationToken);

        result.IsT2.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies tenant filters hide another club's campaign from close operations.
    /// </summary>
    [Fact]
    public async Task CloseAsync_ReturnsNotFound_ForCrossTenantCampaign()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.CloseAsync(ClubBCampaignId, TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies non-admin users cannot reopen campaigns.
    /// </summary>
    [Fact]
    public async Task ReopenAsync_ReturnsForbidden_WhenCallerIsNotClubAdmin()
    {
        ActAs(ClubAMemberId, ClubAId);
        var service = CreateService();

        var result = await service.ReopenAsync(ClosedCampaignId, TestContext.Current.CancellationToken);

        result.IsT2.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies tenant filters hide another club's campaign from reopen operations.
    /// </summary>
    [Fact]
    public async Task ReopenAsync_ReturnsNotFound_ForCrossTenantCampaign()
    {
        ActAs(ClubBAdminId, ClubBId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.ReopenAsync(ClosedCampaignId, TestContext.Current.CancellationToken);

        result.IsT1.ShouldBeTrue();
    }

    /// <summary>
    /// Creates the campaign lifecycle service over the shared SQLite tenancy harness.
    /// </summary>
    /// <returns>A service instance using the mutable fake current-user provider.</returns>
    private CampaignLifecycleService CreateService()
    {
        IDbContextFactory<NovaDbContext> dbContextFactory =
            new TestDbContextFactory<NovaDbContext>(() => _harness.CreateTenantContext());

        return new CampaignLifecycleService(
            dbContextFactory,
            _harness.CurrentUser,
            NullLogger<CampaignLifecycleService>.Instance);
    }

    /// <summary>
    /// Sets the current user state for the next tenant context.
    /// </summary>
    /// <param name="userId">The current user identifier.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <param name="isClubAdmin">Whether the current user is a club administrator.</param>
    private void ActAs(long userId, long clubId, bool isClubAdmin = false)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    /// <summary>
    /// Seeds campaign lifecycle data across two clubs.
    /// </summary>
    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity
            {
                ClubId = ClubAId,
                Name = "Club A",
                City = "Austin",
                State = "TX",
                CreatedById = ClubAAdminId
            },
            new ClubEntity
            {
                ClubId = ClubBId,
                Name = "Club B",
                City = "Boston",
                State = "MA",
                CreatedById = ClubBAdminId
            });

        db.Seasons.AddRange(
            new SeasonEntity
            {
                SeasonId = 500,
                Name = "Season A",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new SeasonEntity
            {
                SeasonId = 501,
                Name = "Season B",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.Campaigns.AddRange(
            new CampaignEntity
            {
                CampaignId = ReadyCampaignId,
                Name = "Ready Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = 500,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CampaignId = BlockedCampaignId,
                Name = "Blocked Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = 500,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CampaignId = ClosedCampaignId,
                Name = "Closed Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                Status = CampaignStatus.Closed,
                ClosedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ClosedById = ClubAAdminId,
                SeasonId = 500,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CampaignId = ClubBCampaignId,
                Name = "Club B Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = 501,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.Players.AddRange(
            new PlayerEntity
            {
                PlayerId = 700,
                FirstName = "Ready",
                LastName = "Assigned",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                PlayerId = 701,
                FirstName = "Ready",
                LastName = "Final",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                PlayerId = 702,
                FirstName = "Blocked",
                LastName = "Undecided",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                PlayerId = 703,
                FirstName = "Blocked",
                LastName = "Ineligible",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2029,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                PlayerId = 704,
                FirstName = "Blocked",
                LastName = "ArchivedTeam",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                PlayerId = 705,
                FirstName = "Closed",
                LastName = "Campaign",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerEntity
            {
                PlayerId = 706,
                FirstName = "ClubB",
                LastName = "Player",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.Teams.AddRange(
            new TeamEntity
            {
                TeamId = EligibleTeamId,
                Name = "Eligible Team",
                GraduationYear = 2029,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new TeamEntity
            {
                TeamId = IneligibleTeamId,
                Name = "Ineligible Team",
                GraduationYear = 2031,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new TeamEntity
            {
                TeamId = ArchivedTeamId,
                Name = "Archived Team",
                GraduationYear = 2029,
                LifecycleStatus = LifecycleStatus.Archived,
                ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1),
                ArchivedById = ClubAAdminId,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new TeamEntity
            {
                TeamId = 803,
                Name = "Club B Team",
                GraduationYear = 2029,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.PlayerCampaignAssignments.AddRange(
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 900,
                PlayerId = 700,
                CampaignId = ReadyCampaignId,
                TeamId = EligibleTeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 901,
                PlayerId = 701,
                CampaignId = ReadyCampaignId,
                PlacementOutcome = PlacementOutcome.NotSelected,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 902,
                PlayerId = 702,
                CampaignId = BlockedCampaignId,
                PlacementOutcome = PlacementOutcome.Undecided,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 903,
                PlayerId = 703,
                CampaignId = BlockedCampaignId,
                TeamId = IneligibleTeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 904,
                PlayerId = 704,
                CampaignId = BlockedCampaignId,
                TeamId = ArchivedTeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 905,
                PlayerId = 705,
                CampaignId = ClosedCampaignId,
                TeamId = EligibleTeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerCampaignAssignmentId = 906,
                PlayerId = 706,
                CampaignId = ClubBCampaignId,
                PlacementOutcome = PlacementOutcome.NotSelected,
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.CampaignLifecycleEvents.Add(new CampaignLifecycleEventEntity
        {
            CampaignLifecycleEventId = 1000,
            CampaignId = ClosedCampaignId,
            EventType = CampaignLifecycleEventType.Closed,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        });

        db.SaveChanges();
    }
}
