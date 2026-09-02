using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Attention;
using Nova.Shared.Enums;
using Nova.Shared.Features.Attention;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Attention;

/// <summary>
/// Verifies the administrator-only club attention projection: authorization, tenant scoping, the
/// refined needs-placement filters, pending-request counts, and the per-region failure isolation
/// contract.
/// </summary>
public sealed class ClubAttentionQueryServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAMemberId = 300;
    private const long ClubAAdminId = 301;
    private const long ClubBMemberId = 400;
    private const long ClubBAdminId = 401;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>Initializes seeded clubs and users for two clubs.</summary>
    public ClubAttentionQueryServiceTests() => SeedBase();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>Verifies an unsigned-in caller cannot read the attention projection.</summary>
    [Fact]
    public async Task GetClubAttention_ReturnsForbidden_WhenNotSignedIn()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies a signed-in member without club administration cannot read the projection.</summary>
    [Fact]
    public async Task GetClubAttention_ReturnsForbidden_ForNonAdministrator()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;

        var result = await CreateService().GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies an administrator without a club identifier is forbidden.</summary>
    [Fact]
    public async Task GetClubAttention_ReturnsForbidden_WhenAdminHasNoClub()
    {
        _harness.CurrentUser.UserId = ClubAAdminId;
        _harness.CurrentUser.ClubId = null;
        _harness.CurrentUser.IsClubAdmin = true;

        var result = await CreateService().GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies the pending-request count and oldest-request time for an administrator.</summary>
    [Fact]
    public async Task GetClubAttention_CountsPendingRequests_WithOldestTimestamp()
    {
        SeedJoinRequests(ClubAId, new[]
        {
            (RequestStatus.Pending, new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero)),
            (RequestStatus.Pending, new DateTimeOffset(2026, 9, 2, 9, 0, 0, TimeSpan.Zero)),
            (RequestStatus.Approved, new DateTimeOffset(2026, 9, 3, 9, 0, 0, TimeSpan.Zero)),
            (RequestStatus.Rejected, new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero))
        });
        SeedJoinRequests(ClubBId, new[]
        {
            (RequestStatus.Pending, new DateTimeOffset(2026, 9, 1, 8, 0, 0, TimeSpan.Zero))
        });

        _harness.CurrentUser.UserId = ClubAAdminId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var result = await CreateService().GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var region = result.Value.PendingJoinRequests;
        region.Status.ShouldBe(AttentionRegionStatus.Loaded);
        region.Count.ShouldBe(2);
        region.OldestRequestAt.ShouldBe(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));
    }

    /// <summary>Verifies an empty pending-request region reports zero without a timestamp.</summary>
    [Fact]
    public async Task GetClubAttention_ReportsEmptyPendingRegion_WithNoTimestamp()
    {
        _harness.CurrentUser.UserId = ClubAAdminId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var result = await CreateService().GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var region = result.Value.PendingJoinRequests;
        region.Status.ShouldBe(AttentionRegionStatus.Loaded);
        region.Count.ShouldBe(0);
        region.OldestRequestAt.ShouldBeNull();
    }

    /// <summary>Verifies the needs-placement count is scoped to the newest Active campaign's
    /// unresolved assignments (not summed across campaigns), and names that target campaign.</summary>
    [Fact]
    public async Task GetClubAttention_CountsNeedsPlacement_WithNewestCampaignName()
    {
        SeedCampaignWithPlayers(ClubAId, "Older Campaign", new DateOnly(2026, 5, 1), CampaignStatus.Active, new[]
        {
            new PlayerAssignmentSeed(PlayerName: "Old Undecided", LifecycleStatus: LifecycleStatus.Active, Outcome: PlacementOutcome.Undecided, TeamId: null),
            new PlayerAssignmentSeed(PlayerName: "Old Teamed", LifecycleStatus: LifecycleStatus.Active, Outcome: PlacementOutcome.Assigned, TeamId: 600)
        });
        SeedCampaignWithPlayers(ClubAId, "Newer Campaign", new DateOnly(2026, 6, 1), CampaignStatus.Active, new[]
        {
            new PlayerAssignmentSeed(PlayerName: "New Undecided", LifecycleStatus: LifecycleStatus.Active, Outcome: PlacementOutcome.Undecided, TeamId: null),
            new PlayerAssignmentSeed(PlayerName: "New Assigned", LifecycleStatus: LifecycleStatus.Active, Outcome: PlacementOutcome.Assigned, TeamId: 601),
            new PlayerAssignmentSeed(PlayerName: "New Archived", LifecycleStatus: LifecycleStatus.Archived, Outcome: PlacementOutcome.Undecided, TeamId: null)
        });

        _harness.CurrentUser.UserId = ClubAAdminId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var result = await CreateService().GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var region = result.Value.NeedsPlacement;
        region.Status.ShouldBe(AttentionRegionStatus.Loaded);
        // The newest campaign's unresolved count is 1 ("New Undecided"); the older campaign's
        // unresolved assignment is not summed in because the count is scoped to the target.
        region.Count.ShouldBe(1);
        region.CampaignId.ShouldNotBeNull();
        region.CampaignName.ShouldBe("Newer Campaign");
    }

    /// <summary>Verifies the needs-placement query never counts assignments in a closed campaign.</summary>
    [Fact]
    public async Task GetClubAttention_ExcludesClosedCampaignAssignments()
    {
        SeedCampaignWithPlayers(ClubAId, "Closed Campaign", new DateOnly(2026, 5, 1), CampaignStatus.Closed, new[]
        {
            new PlayerAssignmentSeed(PlayerName: "Closed Undecided", LifecycleStatus: LifecycleStatus.Active, Outcome: PlacementOutcome.Undecided, TeamId: null)
        });

        _harness.CurrentUser.UserId = ClubAAdminId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var result = await CreateService().GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var region = result.Value.NeedsPlacement;
        region.Status.ShouldBe(AttentionRegionStatus.Loaded);
        region.Count.ShouldBe(0);
        region.CampaignId.ShouldBeNull();
        region.CampaignName.ShouldBeNull();
    }

    /// <summary>Verifies cross-club assignments are never counted for the current club.</summary>
    [Fact]
    public async Task GetClubAttention_ExcludesOtherClubAssignments()
    {
        SeedCampaignWithPlayers(ClubAId, "Club A Campaign", new DateOnly(2026, 6, 1), CampaignStatus.Active, new[]
        {
            new PlayerAssignmentSeed(PlayerName: "Club A Player", LifecycleStatus: LifecycleStatus.Active, Outcome: PlacementOutcome.Undecided, TeamId: null)
        });
        SeedCampaignWithPlayers(ClubBId, "Club B Campaign", new DateOnly(2026, 6, 1), CampaignStatus.Active, new[]
        {
            new PlayerAssignmentSeed(PlayerName: "Club B Player", LifecycleStatus: LifecycleStatus.Active, Outcome: PlacementOutcome.Undecided, TeamId: null)
        });

        _harness.CurrentUser.UserId = ClubAAdminId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var result = await CreateService().GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var region = result.Value.NeedsPlacement;
        region.Status.ShouldBe(AttentionRegionStatus.Loaded);
        region.Count.ShouldBe(1);
        region.CampaignName.ShouldBe("Club A Campaign");
    }

    /// <summary>Verifies a region failure still returns a loaded result, marking both regions
    /// unavailable rather than failing the whole projection.</summary>
    [Fact]
    public async Task GetClubAttention_IsolatesRegionFailures()
    {
        var throwingFactory = Substitute.For<IDbContextFactory<NovaReadDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns<Task<NovaReadDbContext>>(_ => throw new InvalidOperationException("boom"));
        var service = new ClubAttentionQueryService(
            throwingFactory,
            _harness.CurrentUser,
            NullLogger<ClubAttentionQueryService>.Instance);

        _harness.CurrentUser.UserId = ClubAAdminId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var result = await service.GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PendingJoinRequests.Status.ShouldBe(AttentionRegionStatus.Unavailable);
        result.Value.NeedsPlacement.Status.ShouldBe(AttentionRegionStatus.Unavailable);
    }

    /// <summary>Verifies a pending-requests region failure does not hide a loadable
    /// needs-placement region (the regions read on separate contexts, in order).</summary>
    [Fact]
    public async Task GetClubAttention_IsolatesPendingRequestFailure_WhenNeedsPlacementLoads()
    {
        SeedJoinRequests(ClubAId, new[]
        {
            (RequestStatus.Pending, new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero))
        });
        SeedCampaignWithPlayers(ClubAId, "Campaign A", new DateOnly(2026, 6, 1), CampaignStatus.Active, new[]
        {
            new PlayerAssignmentSeed(PlayerName: "Player A", LifecycleStatus: LifecycleStatus.Active, Outcome: PlacementOutcome.Undecided, TeamId: null)
        });

        _harness.CurrentUser.UserId = ClubAAdminId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var calls = 0;
        var sequencedFactory = Substitute.For<IDbContextFactory<NovaReadDbContext>>();
        sequencedFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (calls++ == 0)
                {
                    throw new InvalidOperationException("boom on join requests");
                }

                return _harness.CreateReadContext();
            });
        var service = new ClubAttentionQueryService(
            sequencedFactory,
            _harness.CurrentUser,
            NullLogger<ClubAttentionQueryService>.Instance);

        var result = await service.GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PendingJoinRequests.Status.ShouldBe(AttentionRegionStatus.Unavailable);
        result.Value.NeedsPlacement.Status.ShouldBe(AttentionRegionStatus.Loaded);
        result.Value.NeedsPlacement.Count.ShouldBe(1);
        result.Value.NeedsPlacement.CampaignName.ShouldBe("Campaign A");
    }

    /// <summary>Verifies a needs-placement region failure does not hide a loadable pending
    /// requests region (the regions read on separate contexts, in order).</summary>
    [Fact]
    public async Task GetClubAttention_IsolatesNeedsPlacementFailure_WhenPendingRequestsLoad()
    {
        SeedJoinRequests(ClubAId, new[]
        {
            (RequestStatus.Pending, new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero))
        });

        _harness.CurrentUser.UserId = ClubAAdminId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var calls = 0;
        var sequencedFactory = Substitute.For<IDbContextFactory<NovaReadDbContext>>();
        sequencedFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns(async _ =>
            {
                if (calls++ > 0)
                {
                    throw new InvalidOperationException("boom on needs placement");
                }

                return _harness.CreateReadContext();
            });
        var service = new ClubAttentionQueryService(
            sequencedFactory,
            _harness.CurrentUser,
            NullLogger<ClubAttentionQueryService>.Instance);

        var result = await service.GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PendingJoinRequests.Status.ShouldBe(AttentionRegionStatus.Loaded);
        result.Value.PendingJoinRequests.Count.ShouldBe(1);
        result.Value.PendingJoinRequests.OldestRequestAt.ShouldBe(new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero));
        result.Value.NeedsPlacement.Status.ShouldBe(AttentionRegionStatus.Unavailable);
    }

    /// <summary>Creates the attention query service over the shared SQLite tenancy harness.</summary>
    /// <returns>A service instance using the mutable fake current-user provider.</returns>
    private ClubAttentionQueryService CreateService()
        => new(
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _harness.CurrentUser,
            NullLogger<ClubAttentionQueryService>.Instance);

    /// <summary>Seeds clubs, users, and club-less requesters for two clubs.</summary>
    private void SeedBase()
    {
        using var admin = _harness.CreateAdminContext();

        admin.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });
        admin.Users.AddRange(
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Amelia", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Bobby", LastName = "Member", ClubId = ClubBId },
            new NovaUserEntity { Id = ClubBAdminId, FirstName = "Admin", LastName = "B", ClubId = ClubBId });

        admin.SaveChanges();
    }

    /// <summary>Seeds pending/non-pending join requests for a club with explicit submit timestamps.
    /// Each request gets its own club-less requester because the requesting user is unique.</summary>
    /// <param name="clubId">The club identifier.</param>
    /// <param name="requests">The request status and submit-time pairs, seeded in order.</param>
    private void SeedJoinRequests(long clubId, IReadOnlyList<(RequestStatus Status, DateTimeOffset CreatedAt)> requests)
    {
        using var admin = _harness.CreateAdminContext();
        var entities = new List<ClubJoinRequestEntity>();
        for (var index = 0; index < requests.Count; index++)
        {
            var requesterId = (clubId == ClubAId ? 500L : 600L) + index;
            admin.Users.Add(new NovaUserEntity { Id = requesterId, FirstName = $"R{index}", LastName = "Requester", ClubId = null });
            var request = new ClubJoinRequestEntity
            {
                ClubId = clubId,
                RequestingUserId = requesterId,
                Status = requests[index].Status,
                CreatedById = clubId == ClubAId ? ClubAAdminId : ClubBAdminId
            };
            entities.Add(request);
            admin.ClubJoinRequests.Add(request);
        }

        admin.SaveChanges();

        // Re-stamp deterministic submit times because the audit interceptor set a uniform CreatedAt.
        for (var index = 0; index < entities.Count; index++)
        {
            entities[index].CreatedAt = requests[index].CreatedAt;
        }

        admin.SaveChanges();
    }

    /// <summary>Seeds an active or closed campaign with players and assignments for a club.</summary>
    /// <param name="clubId">The club identifier.</param>
    /// <param name="campaignName">The campaign display name.</param>
    /// <param name="startDate">The campaign start date.</param>
    /// <param name="campaignStatus">The campaign lifecycle status.</param>
    /// <param name="players">The players to create with their assignment facts.</param>
    private void SeedCampaignWithPlayers(
        long clubId,
        string campaignName,
        DateOnly startDate,
        CampaignStatus campaignStatus,
        IReadOnlyList<PlayerAssignmentSeed> players)
    {
        using var admin = _harness.CreateAdminContext();
        var season = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = $"Season {startDate:yyyy-MM-dd}", StartDate = new DateOnly(2026, 1, 1), ClubId = clubId, CreatedById = ClubAMemberId };
        admin.Seasons.Add(season);
        admin.SaveChanges();

        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = campaignName,
            StartDate = startDate,
            Status = campaignStatus,
            ClosedAt = campaignStatus == CampaignStatus.Closed ? new DateTimeOffset(2026, 7, 1, 12, 0, 0, TimeSpan.Zero) : null,
            ClosedById = campaignStatus == CampaignStatus.Closed ? ClubAAdminId : null,
            SeasonId = season.SeasonId,
            ClubId = clubId,
            CreatedById = ClubAMemberId
        };
        admin.Campaigns.Add(campaign);
        admin.SaveChanges();

        foreach (var seed in players)
        {
            var player = new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                FirstName = seed.PlayerName,
                LastName = "Player",
                DateOfBirth = new DateOnly(2010, 1, 1),
                GraduationYear = 2028,
                LifecycleStatus = seed.LifecycleStatus,
                ArchivedAt = seed.LifecycleStatus == LifecycleStatus.Archived ? new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero) : null,
                ArchivedById = seed.LifecycleStatus == LifecycleStatus.Archived ? ClubAAdminId : null,
                ClubId = clubId,
                CreatedById = ClubAMemberId
            };
            admin.Players.Add(player);
            admin.SaveChanges();

            if (seed.TeamId is { } teamId
                && !admin.Teams.Any(team => team.TeamId == teamId))
            {
                admin.Teams.Add(new TeamEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    TeamId = teamId,
                    Name = $"Team {teamId}",
                    GraduationYear = 2028,
                    ClubId = clubId,
                    CreatedById = ClubAMemberId
                });
                admin.SaveChanges();
            }

            admin.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
            {
                PlayerId = player.PlayerId,
                CampaignId = campaign.CampaignId,
                ClubId = clubId,
                CreatedById = ClubAMemberId,
                PlacementOutcome = seed.Outcome,
                TeamId = seed.Outcome == PlacementOutcome.Assigned ? seed.TeamId : null
            });
        }

        admin.SaveChanges();
    }

    /// <summary>A needs-placement player seed descriptor.</summary>
    /// <param name="PlayerName">The player display name.</param>
    /// <param name="LifecycleStatus">The player lifecycle status.</param>
    /// <param name="Outcome">The placement outcome.</param>
    /// <param name="TeamId">The assigned team identifier, if any.</param>
    private sealed record PlayerAssignmentSeed(
        string PlayerName,
        LifecycleStatus LifecycleStatus,
        PlacementOutcome Outcome,
        long? TeamId);
}
