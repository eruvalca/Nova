using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies tenant-safe closeout readiness and recent-activity query behavior: composition of the
/// placement summary, foundation blocker mapping, authorization, validation, and tenant isolation.
/// </summary>
public sealed class CampaignCloseoutQueryServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAMemberId = 300;
    private const long ClubAAdminId = 301;
    private const long ClubBMemberId = 400;

    private readonly TenancyTestHarness _harness = new();

    private long _readyCampaignId;
    private long _closedCampaignId;
    private long _undecidedCampaignId;
    private long _ineligibleCampaignId;
    private long _archivedCampaignId;
    private long _multiCampaignId;
    private long _campaignBId;

    private long _undecidedFirstId;
    private long _undecidedSecondId;
    private long _ineligibleAssignedId;
    private long _archivedAssignedId;
    private long _multiUndecidedId;
    private long _multiIneligibleId;
    private long _multiArchivedId;

    /// <summary>Initializes seeded campaign and assignment data for two clubs.</summary>
    public CampaignCloseoutQueryServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>Verifies an unsigned-in caller cannot read closeout readiness.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_ReturnsForbidden_WhenNotSignedIn()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = _readyCampaignId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies a signed-in user without a club cannot read closeout readiness.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_ReturnsForbidden_WhenUserHasNoClub()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = _readyCampaignId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies invalid campaign identifiers are rejected before any query.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_ReturnsValidation_ForNonPositiveCampaignId()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>Verifies a missing campaign returns a non-disclosing not-found.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_ReturnsNotFound_ForMissingCampaign()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = 999_999 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>Verifies another club's campaign is invisible to the current tenant.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_ReturnsNotFound_ForCrossTenantCampaign()
    {
        _harness.CurrentUser.UserId = ClubBMemberId;
        _harness.CurrentUser.ClubId = ClubBId;

        var result = await CreateService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = _readyCampaignId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>Verifies a ready campaign carries a true verdict, zero blockers, and the composed summary.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_ReturnsReady_WhenNoBlockerExists()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = _readyCampaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CampaignId.ShouldBe(_readyCampaignId);
        result.Value.Status.ShouldBe(CampaignStatus.Active);
        result.Value.IsReady.ShouldBeTrue();
        result.Value.Blockers.ShouldBeEmpty();
        result.Value.Summary.AssignedCount.ShouldBe(1);
        result.Value.Summary.NotSelectedCount.ShouldBe(1);
        result.Value.Summary.WithdrawnCount.ShouldBe(1);
        result.Value.Summary.UndecidedCount.ShouldBe(0);
        result.Value.Summary.TotalCount.ShouldBe(3);
    }

    /// <summary>Verifies a Closed campaign reports ready with zero blockers.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_ReturnsReady_ForClosedCampaign()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = _closedCampaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Status.ShouldBe(CampaignStatus.Closed);
        result.Value.IsReady.ShouldBeTrue();
        result.Value.Blockers.ShouldBeEmpty();
        result.Value.Summary.TotalCount.ShouldBe(2);
    }

    /// <summary>Verifies the undecided-only blocker carries exact counts and assignment ids.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_ReturnsUndecidedBlocker_WithIds()
    {
        SetOnlyActiveCampaign(_undecidedCampaignId);
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = _undecidedCampaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReady.ShouldBeFalse();
        var blocker = result.Value.Blockers.ShouldHaveSingleItem();
        blocker.Condition.ShouldBe(CloseoutBlockerConditions.Outcomes);
        blocker.Count.ShouldBe(2);
        blocker.AssignmentIds.ShouldBe([_undecidedFirstId, _undecidedSecondId]);
        blocker.Message.ShouldContain("2 undecided participation record(s)");
    }

    /// <summary>Verifies the eligibility-only blocker carries exact counts and assignment ids.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_ReturnsEligibilityBlocker_WithIds()
    {
        SetOnlyActiveCampaign(_ineligibleCampaignId);
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = _ineligibleCampaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReady.ShouldBeFalse();
        var blocker = result.Value.Blockers.ShouldHaveSingleItem();
        blocker.Condition.ShouldBe(CloseoutBlockerConditions.Eligibility);
        blocker.Count.ShouldBe(1);
        blocker.AssignmentIds.ShouldBe([_ineligibleAssignedId]);
    }

    /// <summary>Verifies the archived-team-only blocker carries exact counts and assignment ids.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_ReturnsArchivedTeamBlocker_WithIds()
    {
        SetOnlyActiveCampaign(_archivedCampaignId);
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = _archivedCampaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReady.ShouldBeFalse();
        var blocker = result.Value.Blockers.ShouldHaveSingleItem();
        blocker.Condition.ShouldBe(CloseoutBlockerConditions.ArchivedTeams);
        blocker.Count.ShouldBe(1);
        blocker.AssignmentIds.ShouldBe([_archivedAssignedId]);
    }

    /// <summary>Verifies multi-condition blockers are ordered by the shared constants with exact ids.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_ReturnsAllBlockers_InStableOrder()
    {
        SetOnlyActiveCampaign(_multiCampaignId);
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = _multiCampaignId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.IsReady.ShouldBeFalse();
        result.Value.Blockers.Select(blocker => blocker.Condition)
            .ShouldBe(
            [
                CloseoutBlockerConditions.Outcomes,
                CloseoutBlockerConditions.Eligibility,
                CloseoutBlockerConditions.ArchivedTeams
            ]);

        var undecided = result.Value.Blockers.Single(blocker => blocker.Condition == CloseoutBlockerConditions.Outcomes);
        undecided.AssignmentIds.ShouldBe([_multiUndecidedId]);

        var eligibility = result.Value.Blockers.Single(blocker => blocker.Condition == CloseoutBlockerConditions.Eligibility);
        eligibility.AssignmentIds.ShouldBe([_multiIneligibleId]);

        var archived = result.Value.Blockers.Single(blocker => blocker.Condition == CloseoutBlockerConditions.ArchivedTeams);
        archived.AssignmentIds.ShouldBe([_multiArchivedId]);
    }

    /// <summary>Verifies the readiness summary is the composed placement summary, not a re-derived count.</summary>
    [Fact]
    public async Task GetCloseoutReadiness_EmbedsPlacementSummaryVerbatim()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var placementQueryService = CreatePlacementQueryService();
        var service = CreateService();

        var readinessResult = await service.GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = _readyCampaignId },
            TestContext.Current.CancellationToken);
        var summaryResult = await placementQueryService.GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = _readyCampaignId },
            TestContext.Current.CancellationToken);

        readinessResult.IsSuccess.ShouldBeTrue();
        summaryResult.IsSuccess.ShouldBeTrue();
        readinessResult.Value.Summary.ShouldBe(summaryResult.Value);
    }

    /// <summary>Creates the closeout query service over the shared SQLite tenancy harness.</summary>
    /// <returns>A service instance using the mutable fake current-user provider.</returns>
    private CampaignCloseoutQueryService CreateService()
        => new(
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _harness.CurrentUser,
            CreatePlacementQueryService(),
            NullLogger<CampaignCloseoutQueryService>.Instance);

    /// <summary>Creates the composed placement query service over the same harness.</summary>
    /// <returns>The placement query service.</returns>
    private CampaignPlacementQueryService CreatePlacementQueryService()
        => new(
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _harness.CurrentUser,
            NullLogger<CampaignPlacementQueryService>.Instance);

    /// <summary>Seeds clubs, users, seasons, teams, campaigns, players, and assignments.</summary>
    private void Seed()
    {
        using var admin = _harness.CreateAdminContext();

        admin.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });
        admin.Users.AddRange(
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Amelia", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Bobby", LastName = "Member", ClubId = ClubBId });
        admin.Seasons.AddRange(
            new SeasonEntity { CreationOperationId = Guid.NewGuid(), SeasonId = 500, Name = "Season A", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubAId, CreatedById = ClubAMemberId },
            new SeasonEntity { CreationOperationId = Guid.NewGuid(), SeasonId = 501, Name = "Season B", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubBId, CreatedById = ClubBMemberId });
        admin.Teams.AddRange(
            new TeamEntity { CreationOperationId = Guid.NewGuid(), TeamId = 600, Name = "Alpha", GraduationYear = 2030, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new TeamEntity { CreationOperationId = Guid.NewGuid(), TeamId = 601, Name = "Archived", GraduationYear = 2030, LifecycleStatus = LifecycleStatus.Archived, ArchivedAt = DateTimeOffset.UtcNow.AddDays(-1), ArchivedById = ClubAMemberId, ClubId = ClubAId, CreatedById = ClubAMemberId },
            new TeamEntity { CreationOperationId = Guid.NewGuid(), TeamId = 602, Name = "Beta", GraduationYear = 2030, ClubId = ClubBId, CreatedById = ClubBMemberId });
        admin.SaveChanges();

        var readyCampaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Ready", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = 500, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var closedCampaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Closed", StartDate = new DateOnly(2026, 5, 1), Status = CampaignStatus.Closed, ClosedAt = DateTimeOffset.UtcNow, ClosedById = ClubAAdminId, SeasonId = 500, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var undecidedCampaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Undecided", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Draft, SeasonId = 500, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var ineligibleCampaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Ineligible", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Draft, SeasonId = 500, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var archivedCampaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "ArchivedTeam", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Draft, SeasonId = 500, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var multiCampaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Multi", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Draft, SeasonId = 500, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignB = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "ClubB", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = 501, ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Campaigns.AddRange(readyCampaign, closedCampaign, undecidedCampaign, ineligibleCampaign, archivedCampaign, multiCampaign, campaignB);
        admin.SaveChanges();

        var readyAssigned = CreatePlayer(ClubAId, 2030, "ReadyAssigned");
        var readyNotSelected = CreatePlayer(ClubAId, 2030, "ReadyNotSelected");
        var readyWithdrawn = CreatePlayer(ClubAId, 2030, "ReadyWithdrawn");
        var closedAssigned = CreatePlayer(ClubAId, 2030, "ClosedAssigned");
        var closedNotSelected = CreatePlayer(ClubAId, 2030, "ClosedNotSelected");
        var undecidedFirst = CreatePlayer(ClubAId, 2030, "UndecidedFirst");
        var undecidedSecond = CreatePlayer(ClubAId, 2030, "UndecidedSecond");
        var undecidedAssigned = CreatePlayer(ClubAId, 2030, "UndecidedAssigned");
        var ineligibleAssigned = CreatePlayer(ClubAId, 2029, "IneligibleAssigned");
        var ineligibleNotSelected = CreatePlayer(ClubAId, 2030, "IneligibleNotSelected");
        var archivedAssigned = CreatePlayer(ClubAId, 2030, "ArchivedAssigned");
        var archivedNotSelected = CreatePlayer(ClubAId, 2030, "ArchivedNotSelected");
        var multiUndecided = CreatePlayer(ClubAId, 2030, "MultiUndecided");
        var multiIneligible = CreatePlayer(ClubAId, 2029, "MultiIneligible");
        var multiArchived = CreatePlayer(ClubAId, 2030, "MultiArchived");
        var clubBAssigned = CreatePlayer(ClubBId, 2030, "ClubBAssigned");
        admin.Players.AddRange(
            readyAssigned, readyNotSelected, readyWithdrawn, closedAssigned, closedNotSelected,
            undecidedFirst, undecidedSecond, undecidedAssigned, ineligibleAssigned, ineligibleNotSelected,
            archivedAssigned, archivedNotSelected, multiUndecided, multiIneligible, multiArchived, clubBAssigned);
        admin.SaveChanges();

        var undecidedFirstAssignment = CreateAssignment(undecidedFirst, undecidedCampaign, ClubAId, PlacementOutcome.Undecided);
        var undecidedSecondAssignment = CreateAssignment(undecidedSecond, undecidedCampaign, ClubAId, PlacementOutcome.Undecided);
        var ineligibleAssignment = CreateAssignment(ineligibleAssigned, ineligibleCampaign, ClubAId, PlacementOutcome.Assigned, teamId: 600);
        var archivedAssignment = CreateAssignment(archivedAssigned, archivedCampaign, ClubAId, PlacementOutcome.Assigned, teamId: 601);
        var multiUndecidedAssignment = CreateAssignment(multiUndecided, multiCampaign, ClubAId, PlacementOutcome.Undecided);
        var multiIneligibleAssignment = CreateAssignment(multiIneligible, multiCampaign, ClubAId, PlacementOutcome.Assigned, teamId: 600);
        var multiArchivedAssignment = CreateAssignment(multiArchived, multiCampaign, ClubAId, PlacementOutcome.Assigned, teamId: 601);

        admin.PlayerCampaignAssignments.AddRange(
            CreateAssignment(readyAssigned, readyCampaign, ClubAId, PlacementOutcome.Assigned, teamId: 600),
            CreateAssignment(readyNotSelected, readyCampaign, ClubAId, PlacementOutcome.NotSelected),
            CreateAssignment(readyWithdrawn, readyCampaign, ClubAId, PlacementOutcome.Withdrawn),
            CreateAssignment(closedAssigned, closedCampaign, ClubAId, PlacementOutcome.Assigned, teamId: 600),
            CreateAssignment(closedNotSelected, closedCampaign, ClubAId, PlacementOutcome.NotSelected),
            undecidedFirstAssignment,
            undecidedSecondAssignment,
            CreateAssignment(undecidedAssigned, undecidedCampaign, ClubAId, PlacementOutcome.Assigned, teamId: 600),
            ineligibleAssignment,
            CreateAssignment(ineligibleNotSelected, ineligibleCampaign, ClubAId, PlacementOutcome.NotSelected),
            archivedAssignment,
            CreateAssignment(archivedNotSelected, archivedCampaign, ClubAId, PlacementOutcome.NotSelected),
            multiUndecidedAssignment,
            multiIneligibleAssignment,
            multiArchivedAssignment,
            CreateAssignment(clubBAssigned, campaignB, ClubBId, PlacementOutcome.Assigned, teamId: 602));
        admin.SaveChanges();

        _readyCampaignId = readyCampaign.CampaignId;
        _closedCampaignId = closedCampaign.CampaignId;
        _undecidedCampaignId = undecidedCampaign.CampaignId;
        _ineligibleCampaignId = ineligibleCampaign.CampaignId;
        _archivedCampaignId = archivedCampaign.CampaignId;
        _multiCampaignId = multiCampaign.CampaignId;
        _campaignBId = campaignB.CampaignId;

        _undecidedFirstId = undecidedFirstAssignment.PlayerCampaignAssignmentId;
        _undecidedSecondId = undecidedSecondAssignment.PlayerCampaignAssignmentId;
        _ineligibleAssignedId = ineligibleAssignment.PlayerCampaignAssignmentId;
        _archivedAssignedId = archivedAssignment.PlayerCampaignAssignmentId;
        _multiUndecidedId = multiUndecidedAssignment.PlayerCampaignAssignmentId;
        _multiIneligibleId = multiIneligibleAssignment.PlayerCampaignAssignmentId;
        _multiArchivedId = multiArchivedAssignment.PlayerCampaignAssignmentId;
    }

    /// <summary>Chooses the sole Active campaign for a readiness scenario.</summary>
    /// <param name="campaignId">The campaign to activate.</param>
    private void SetOnlyActiveCampaign(long campaignId)
    {
        using var db = _harness.CreateAdminContext();
        var current = db.Campaigns.Single(campaign => campaign.ClubId == ClubAId
            && campaign.Status == CampaignStatus.Active);
        current.Status = CampaignStatus.Draft;
        db.SaveChanges();
        db.Campaigns.Single(campaign => campaign.CampaignId == campaignId).Status = CampaignStatus.Active;
        db.SaveChanges();
    }

    /// <summary>Creates one seeded player.</summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="graduationYear">The player graduation year.</param>
    /// <param name="suffix">A stable name suffix.</param>
    /// <returns>The new player entity.</returns>
    private PlayerEntity CreatePlayer(long clubId, int graduationYear, string suffix)
        => new()
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = $"Player{suffix}",
            LastName = "Seed",
            DateOfBirth = new DateOnly(2010, 1, 1),
            GraduationYear = graduationYear,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = clubId,
            CreatedById = clubId == ClubAId ? ClubAMemberId : ClubBMemberId
        };

    /// <summary>Creates one campaign assignment referencing the owning club.</summary>
    /// <param name="player">The participating player.</param>
    /// <param name="campaign">The owning campaign.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="outcome">The placement outcome.</param>
    /// <param name="teamId">The optional assigned team identifier.</param>
    /// <returns>The new assignment entity.</returns>
    private PlayerCampaignAssignmentEntity CreateAssignment(
        PlayerEntity player,
        CampaignEntity campaign,
        long clubId,
        PlacementOutcome outcome,
        long? teamId = null)
        => new()
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            ClubId = clubId,
            CreatedById = clubId == ClubAId ? ClubAMemberId : ClubBMemberId,
            PlacementOutcome = outcome,
            TeamId = teamId
        };
}
