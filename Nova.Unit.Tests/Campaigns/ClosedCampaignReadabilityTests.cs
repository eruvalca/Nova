using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Proves the closed-history readability acceptance criterion: evaluators and administrators can
/// read a Closed campaign through every campaign read surface with the same shape as an Active
/// campaign, while another club's Closed campaign stays invisible and absent from lists/history.
/// </summary>
public sealed class ClosedCampaignReadabilityTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAViewerId = 300;
    private const long ClubAAdminId = 301;
    private const long ClubBMemberId = 400;

    private readonly TenancyTestHarness _harness = new();
    private long _closedCampaignId;
    private long _activeCampaignId;
    private long _closedCampaignBId;
    private long _histPlayerId;
    private long _histAssignmentId;

    /// <summary>Initializes seeded club, user, season, team, campaign, player, note, and event data.</summary>
    public ClosedCampaignReadabilityTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies an evaluator and an administrator read a Closed campaign through every read surface
    /// with identical shapes to an Active campaign.
    /// </summary>
    /// <param name="isClubAdmin">Whether the simulated viewer is a club administrator.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EvaluatorAndAdmin_CanReadClosedCampaign_AcrossAllReadSurfaces(bool isClubAdmin)
    {
        ActAs(ClubAViewerId, ClubAId, isClubAdmin);

        var detail = await CreateCampaignQueryService().GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = _closedCampaignId },
            TestContext.Current.CancellationToken);
        detail.IsSuccess.ShouldBeTrue();
        detail.Value.Status.ShouldBe(CampaignStatus.Closed);
        detail.Value.ClosedAt.ShouldNotBeNull();
        detail.Value.ClosedByUserId.ShouldBe(ClubAAdminId);
        detail.Value.ClosedByDisplayName.ShouldBe("Admin A");

        var roster = await CreatePlacementQueryService().GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _closedCampaignId },
            TestContext.Current.CancellationToken);
        roster.IsSuccess.ShouldBeTrue();
        roster.Value.TotalCount.ShouldBe(1);
        roster.Value.Items.Single().PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);

        var summary = await CreatePlacementQueryService().GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = _closedCampaignId },
            TestContext.Current.CancellationToken);
        summary.IsSuccess.ShouldBeTrue();
        summary.Value.AssignedCount.ShouldBe(1);
        summary.Value.TotalCount.ShouldBe(1);

        var readiness = await CreateCloseoutQueryService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = _closedCampaignId },
            TestContext.Current.CancellationToken);
        readiness.IsSuccess.ShouldBeTrue();
        readiness.Value.Status.ShouldBe(CampaignStatus.Closed);
        readiness.Value.IsReady.ShouldBeTrue();
        readiness.Value.Blockers.ShouldBeEmpty();

        var activity = await CreateCloseoutQueryService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = _closedCampaignId },
            TestContext.Current.CancellationToken);
        activity.IsSuccess.ShouldBeTrue();
        activity.Value.Events.ShouldHaveSingleItem();
        activity.Value.Events[0].EventType.ShouldBe(CampaignLifecycleEventType.Closed);
        activity.Value.Events[0].ActorDisplayName.ShouldBe("Admin A");

        var playerDetail = await CreatePlayerDetailService().GetPlayerDetailAsync(
            _histPlayerId,
            TestContext.Current.CancellationToken);
        playerDetail.IsSuccess.ShouldBeTrue();
        var history = playerDetail.Value.CampaignHistory.ShouldHaveSingleItem();
        history.CampaignId.ShouldBe(_closedCampaignId);
        history.CampaignStatus.ShouldBe(CampaignStatus.Closed);
        history.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        history.Team.ShouldNotBeNull();
        history.Team!.Name.ShouldBe("Alpha");
        history.Notes.ShouldHaveSingleItem();
        history.Notes[0].Content.ShouldBe("Closed campaign note.");
    }

    /// <summary>Verifies another club's Closed campaign is invisible and absent from lists/history.</summary>
    [Fact]
    public async Task CrossTenantClosedCampaign_IsInvisible_AcrossReadSurfaces()
    {
        ActAs(ClubBMemberId, ClubBId, isClubAdmin: false);

        var detail = await CreateCampaignQueryService().GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = _closedCampaignId },
            TestContext.Current.CancellationToken);
        detail.IsProblem.ShouldBeTrue();
        detail.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        var roster = await CreatePlacementQueryService().GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _closedCampaignId },
            TestContext.Current.CancellationToken);
        roster.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        var summary = await CreatePlacementQueryService().GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = _closedCampaignId },
            TestContext.Current.CancellationToken);
        summary.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        var readiness = await CreateCloseoutQueryService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = _closedCampaignId },
            TestContext.Current.CancellationToken);
        readiness.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        var activity = await CreateCloseoutQueryService().GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = _closedCampaignId },
            TestContext.Current.CancellationToken);
        activity.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        var playerDetail = await CreatePlayerDetailService().GetPlayerDetailAsync(
            _histPlayerId,
            TestContext.Current.CancellationToken);
        playerDetail.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);

        var list = await CreateCampaignQueryService().GetCampaignListAsync(
            new GetCampaignListInput { Status = "closed" },
            TestContext.Current.CancellationToken);
        list.IsSuccess.ShouldBeTrue();
        var campaignNames = list.Value.Seasons.SelectMany(season => season.Campaigns).Select(campaign => campaign.Name).ToList();
        campaignNames.ShouldNotContain("Closed A");
        campaignNames.ShouldContain("Closed B");
    }

    /// <summary>Sets the simulated current user for the next tenant/read context.</summary>
    /// <param name="userId">The simulated user identifier.</param>
    /// <param name="clubId">The simulated club identifier.</param>
    /// <param name="isClubAdmin">Whether the simulated user is a club administrator.</param>
    private void ActAs(long? userId, long? clubId, bool isClubAdmin)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    /// <summary>Creates the campaign query service over the shared harness.</summary>
    /// <returns>The campaign query service.</returns>
    private CampaignQueryService CreateCampaignQueryService()
        => new(
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _harness.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

    /// <summary>Creates the placement query service over the shared harness.</summary>
    /// <returns>The placement query service.</returns>
    private CampaignPlacementQueryService CreatePlacementQueryService()
        => new(
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _harness.CurrentUser,
            NullLogger<CampaignPlacementQueryService>.Instance);

    /// <summary>Creates the closeout query service over the shared harness.</summary>
    /// <returns>The closeout query service.</returns>
    private CampaignCloseoutQueryService CreateCloseoutQueryService()
        => new(
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _harness.CurrentUser,
            CreatePlacementQueryService(),
            NullLogger<CampaignCloseoutQueryService>.Instance);

    /// <summary>Creates the player detail service over the shared harness.</summary>
    /// <returns>The player detail service.</returns>
    private PlayerDetailQueryService CreatePlayerDetailService()
        => new(
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _harness.CurrentUser,
            NullLogger<PlayerDetailQueryService>.Instance);

    /// <summary>Seeds closed and active campaigns for two clubs with history, notes, and lifecycle events.</summary>
    private void Seed()
    {
        using var admin = _harness.CreateAdminContext();

        admin.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAViewerId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });
        admin.Users.AddRange(
            new NovaUserEntity { Id = ClubAViewerId, FirstName = "Casey", LastName = "Viewer", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Bobby", LastName = "Member", ClubId = ClubBId });
        admin.Seasons.AddRange(
            new SeasonEntity { SeasonId = 500, Name = "Season A", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubAId, CreatedById = ClubAViewerId },
            new SeasonEntity { SeasonId = 501, Name = "Season B", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubBId, CreatedById = ClubBMemberId });
        admin.Teams.AddRange(
            new TeamEntity { TeamId = 600, Name = "Alpha", GraduationYear = 2030, ClubId = ClubAId, CreatedById = ClubAViewerId },
            new TeamEntity { TeamId = 602, Name = "Beta", GraduationYear = 2030, ClubId = ClubBId, CreatedById = ClubBMemberId });
        admin.SaveChanges();

        var closedCampaign = new CampaignEntity { Name = "Closed A", StartDate = new DateOnly(2026, 5, 1), Status = CampaignStatus.Closed, ClosedAt = DateTimeOffset.UtcNow, ClosedById = ClubAAdminId, SeasonId = 500, ClubId = ClubAId, CreatedById = ClubAViewerId };
        var activeCampaign = new CampaignEntity { Name = "Active A", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = 500, ClubId = ClubAId, CreatedById = ClubAViewerId };
        var closedCampaignB = new CampaignEntity { Name = "Closed B", StartDate = new DateOnly(2026, 5, 1), Status = CampaignStatus.Closed, ClosedAt = DateTimeOffset.UtcNow, ClosedById = ClubBMemberId, SeasonId = 501, ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Campaigns.AddRange(closedCampaign, activeCampaign, closedCampaignB);
        admin.SaveChanges();

        var histPlayer = new PlayerEntity { FirstName = "Hist", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2030, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAViewerId };
        var activePlayer = new PlayerEntity { FirstName = "Active", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2030, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAViewerId };
        var closedBPlayer = new PlayerEntity { FirstName = "Closed", LastName = "BPlayer", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2030, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Players.AddRange(histPlayer, activePlayer, closedBPlayer);
        admin.SaveChanges();

        var histAssignment = new PlayerCampaignAssignmentEntity { PlayerId = histPlayer.PlayerId, CampaignId = closedCampaign.CampaignId, ClubId = ClubAId, CreatedById = ClubAViewerId, PlacementOutcome = PlacementOutcome.Assigned, TeamId = 600 };
        var activeAssignment = new PlayerCampaignAssignmentEntity { PlayerId = activePlayer.PlayerId, CampaignId = activeCampaign.CampaignId, ClubId = ClubAId, CreatedById = ClubAViewerId, PlacementOutcome = PlacementOutcome.Undecided };
        var closedBAssignment = new PlayerCampaignAssignmentEntity { PlayerId = closedBPlayer.PlayerId, CampaignId = closedCampaignB.CampaignId, ClubId = ClubBId, CreatedById = ClubBMemberId, PlacementOutcome = PlacementOutcome.Assigned, TeamId = 602 };
        admin.PlayerCampaignAssignments.AddRange(histAssignment, activeAssignment, closedBAssignment);
        admin.SaveChanges();

        admin.Notes.Add(new NoteEntity
        {
            PlayerCampaignAssignmentId = histAssignment.PlayerCampaignAssignmentId,
            Content = "Closed campaign note.",
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        });
        admin.SaveChanges();

        admin.CampaignLifecycleEvents.Add(new CampaignLifecycleEventEntity
        {
            CampaignId = closedCampaign.CampaignId,
            ClubId = ClubAId,
            EventType = CampaignLifecycleEventType.Closed,
            CreatedById = ClubAAdminId
        });
        admin.SaveChanges();

        _closedCampaignId = closedCampaign.CampaignId;
        _activeCampaignId = activeCampaign.CampaignId;
        _closedCampaignBId = closedCampaignB.CampaignId;
        _histPlayerId = histPlayer.PlayerId;
        _histAssignmentId = histAssignment.PlayerCampaignAssignmentId;
    }
}
