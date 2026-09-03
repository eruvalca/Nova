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
/// Tests placement roster paging, filter composition, deterministic ordering, summary accuracy,
/// authorization, and tenant isolation for the placement query service.
/// </summary>
public sealed class CampaignPlacementQueryServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAMemberId = 200;
    private const long ClubBMemberId = 201;

    private readonly TenancyTestHarness _harness = new();
    private long _campaignAId;
    private long _campaignBId;
    private long _zoeAdamsAssignedId;
    private long _zoeAdamsWithdrawnId;
    private long _amyBarnesId;
    private long _amyCarterId;
    private long _benDoyleId;
    private readonly Guid[] _tokens = [Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid()];

    /// <summary>
    /// Initializes seeded campaign participation data for each test.
    /// </summary>
    public CampaignPlacementQueryServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies an unauthenticated caller cannot read placement results.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ReturnsForbidden_WhenNotSignedIn()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _campaignAId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies a signed-in user without a club cannot read placement results.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ReturnsForbidden_WhenUserHasNoClub()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _campaignAId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies another club's campaign is non-disclosing not-found.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ReturnsNotFound_ForCrossTenantCampaign()
    {
        _harness.CurrentUser.UserId = ClubBMemberId;
        _harness.CurrentUser.ClubId = ClubBId;

        var result = await CreateService().GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _campaignAId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies invalid explicit paging values are rejected before database access.
    /// </summary>
    /// <param name="page">The invalid page number.</param>
    /// <param name="pageSize">The invalid page size.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0, 50)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    public async Task GetPlacementRoster_ReturnsValidation_ForInvalidPagingValues(int page, int pageSize)
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput
            {
                CampaignId = _campaignAId,
                Page = page,
                PageSize = pageSize
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>
    /// Verifies rows are ordered by display name with assignment-id tie-breaking.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ReturnsRowsOrderedByDisplayNameWithAssignmentTieBreak()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _campaignAId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(5);
        result.Value.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe(
            [
                _zoeAdamsAssignedId,
                _zoeAdamsWithdrawnId,
                _amyBarnesId,
                _amyCarterId,
                _benDoyleId
            ]);
    }

    /// <summary>
    /// Verifies each row carries the persisted fields needed for a placement update.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ReturnsRowFieldsAndConcurrencyTokens()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _campaignAId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(5);

        var assigned = result.Value.Items.Single(item => item.PlayerCampaignAssignmentId == _zoeAdamsAssignedId);
        assigned.PlayerId.ShouldBeGreaterThan(0);
        assigned.DisplayName.ShouldBe("Zoe Adams");
        assigned.GraduationYear.ShouldBe(2028);
        assigned.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        assigned.Team.ShouldNotBeNull();
        assigned.Team!.TeamId.ShouldBeGreaterThan(0);
        assigned.Team.TeamName.ShouldNotBeNullOrWhiteSpace();
        assigned.ConcurrencyToken.ShouldBe(_tokens[0]);

        result.Value.Items.ShouldAllBe(item => item.ConcurrencyToken != Guid.Empty);
    }

    /// <summary>
    /// Verifies the graduation-year and unresolved-only filters compose.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ComposesGraduationYearAndUnresolvedOnlyFilters()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        var byYear = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _campaignAId, GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        byYear.IsSuccess.ShouldBeTrue();
        byYear.Value.TotalCount.ShouldBe(3);
        byYear.Value.Items.ShouldAllBe(item => item.GraduationYear == 2028);

        var unresolved = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _campaignAId, UnresolvedOnly = true },
            TestContext.Current.CancellationToken);

        unresolved.IsSuccess.ShouldBeTrue();
        unresolved.Value.TotalCount.ShouldBe(2);
        unresolved.Value.Items.ShouldAllBe(item => item.PlacementOutcome == PlacementOutcome.Undecided);

        var composed = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput
            {
                CampaignId = _campaignAId,
                GraduationYear = 2028,
                UnresolvedOnly = true
            },
            TestContext.Current.CancellationToken);

        composed.IsSuccess.ShouldBeTrue();
        composed.Value.TotalCount.ShouldBe(2);
        composed.Value.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe([_amyBarnesId, _benDoyleId]);
    }

    /// <summary>
    /// Verifies paging is bounded in SQL and total count covers the whole filtered set.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_PagesBoundedResultsWithStableOrdering()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        var service = CreateService();

        var first = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _campaignAId, Page = 1, PageSize = 2 },
            TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        first.Value.TotalCount.ShouldBe(5);
        first.Value.Items.Count.ShouldBe(2);
        first.Value.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe([_zoeAdamsAssignedId, _zoeAdamsWithdrawnId]);

        var second = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _campaignAId, Page = 2, PageSize = 2 },
            TestContext.Current.CancellationToken);

        second.IsSuccess.ShouldBeTrue();
        second.Value.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe([_amyBarnesId, _amyCarterId]);

        var third = await service.GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _campaignAId, Page = 3, PageSize = 2 },
            TestContext.Current.CancellationToken);

        third.IsSuccess.ShouldBeTrue();
        third.Value.Items.Select(item => item.PlayerCampaignAssignmentId)
            .ShouldBe([_benDoyleId]);
    }

    /// <summary>
    /// Verifies a member sees only their club's rows and never another club's assignments.
    /// </summary>
    [Fact]
    public async Task GetPlacementRoster_ExcludesCrossTenantRows()
    {
        _harness.CurrentUser.UserId = ClubBMemberId;
        _harness.CurrentUser.ClubId = ClubBId;

        var result = await CreateService().GetPlacementRosterAsync(
            new GetCampaignPlacementRosterInput { CampaignId = _campaignBId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Items.Single().PlayerId.ShouldBeGreaterThan(0);
    }

    /// <summary>
    /// Verifies the summary reports accurate whole-campaign outcome counts.
    /// </summary>
    [Fact]
    public async Task GetPlacementSummary_ReturnsAccurateWholeCampaignCounts()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = _campaignAId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AssignedCount.ShouldBe(1);
        result.Value.NotSelectedCount.ShouldBe(1);
        result.Value.WithdrawnCount.ShouldBe(1);
        result.Value.UndecidedCount.ShouldBe(2);
        result.Value.TotalCount.ShouldBe(5);
    }

    /// <summary>
    /// Verifies the summary is tenant-scoped.
    /// </summary>
    [Fact]
    public async Task GetPlacementSummary_CountsOnlyOwnTenant()
    {
        _harness.CurrentUser.UserId = ClubBMemberId;
        _harness.CurrentUser.ClubId = ClubBId;

        var result = await CreateService().GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = _campaignBId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.AssignedCount.ShouldBe(1);
        result.Value.TotalCount.ShouldBe(1);
    }

    /// <summary>
    /// Verifies another club's campaign summary is non-disclosing not-found.
    /// </summary>
    [Fact]
    public async Task GetPlacementSummary_ReturnsNotFound_ForCrossTenantCampaign()
    {
        _harness.CurrentUser.UserId = ClubBMemberId;
        _harness.CurrentUser.ClubId = ClubBId;

        var result = await CreateService().GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = _campaignAId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies a signed-in user without a club cannot read the summary.
    /// </summary>
    [Fact]
    public async Task GetPlacementSummary_ReturnsForbidden_WhenUserHasNoClub()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = _campaignAId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Creates the placement query service over the shared SQLite tenancy harness.
    /// </summary>
    /// <returns>A service instance using the mutable fake current-user provider.</returns>
    private CampaignPlacementQueryService CreateService()
        => new(
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _harness.CurrentUser,
            NullLogger<CampaignPlacementQueryService>.Instance);

    /// <summary>
    /// Seeds two clubs with campaigns, teams, and mixed-outcome participations.
    /// </summary>
    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });

        db.Users.AddRange(
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Amelia", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Bobby", LastName = "Member", ClubId = ClubBId });

        db.Seasons.AddRange(
            new SeasonEntity { CreationOperationId = Guid.NewGuid(), SeasonId = 500, Name = "Season A", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubAId, CreatedById = ClubAMemberId },
            new SeasonEntity { CreationOperationId = Guid.NewGuid(), SeasonId = 501, Name = "Season B", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubBId, CreatedById = ClubBMemberId });

        var campaignA = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Campaign A", StartDate = new DateOnly(2026, 6, 1), SeasonId = 500, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignB = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = "Campaign B", StartDate = new DateOnly(2026, 6, 1), SeasonId = 501, ClubId = ClubBId, CreatedById = ClubBMemberId };
        db.Campaigns.AddRange(campaignA, campaignB);

        var teamA = new TeamEntity { CreationOperationId = Guid.NewGuid(), TeamId = 600, Name = "Alpha", GraduationYear = 2028, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var teamB = new TeamEntity { CreationOperationId = Guid.NewGuid(), TeamId = 601, Name = "Beta", GraduationYear = 2028, ClubId = ClubBId, CreatedById = ClubBMemberId };
        db.Teams.AddRange(teamA, teamB);
        db.SaveChanges();

        var zoeAdamsAssigned = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Zoe", LastName = "Adams", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var zoeAdamsWithdrawn = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Zoe", LastName = "Adams", DateOfBirth = new DateOnly(2010, 2, 2), GraduationYear = 2029, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var amyBarnes = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Amy", LastName = "Barnes", DateOfBirth = new DateOnly(2011, 3, 3), GraduationYear = 2028, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var amyCarter = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Amy", LastName = "Carter", DateOfBirth = new DateOnly(2011, 4, 4), GraduationYear = 2029, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var benDoyle = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Ben", LastName = "Doyle", DateOfBirth = new DateOnly(2012, 5, 5), GraduationYear = 2028, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var clubBPlayer = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "Cory", LastName = "Evans", DateOfBirth = new DateOnly(2012, 6, 6), GraduationYear = 2028, ClubId = ClubBId, CreatedById = ClubBMemberId };
        db.Players.AddRange(zoeAdamsAssigned, zoeAdamsWithdrawn, amyBarnes, amyCarter, benDoyle, clubBPlayer);
        db.SaveChanges();

        var assignment1 = new PlayerCampaignAssignmentEntity
        {
            PlayerId = zoeAdamsAssigned.PlayerId,
            CampaignId = campaignA.CampaignId,
            ClubId = ClubAId,
            CreatedById = ClubAMemberId,
            PlacementOutcome = PlacementOutcome.Assigned,
            TeamId = teamA.TeamId,
            ConcurrencyToken = _tokens[0]
        };
        var assignment2 = new PlayerCampaignAssignmentEntity
        {
            PlayerId = zoeAdamsWithdrawn.PlayerId,
            CampaignId = campaignA.CampaignId,
            ClubId = ClubAId,
            CreatedById = ClubAMemberId,
            PlacementOutcome = PlacementOutcome.Withdrawn,
            ConcurrencyToken = _tokens[1]
        };
        var assignment3 = new PlayerCampaignAssignmentEntity
        {
            PlayerId = amyBarnes.PlayerId,
            CampaignId = campaignA.CampaignId,
            ClubId = ClubAId,
            CreatedById = ClubAMemberId,
            PlacementOutcome = PlacementOutcome.Undecided,
            ConcurrencyToken = _tokens[2]
        };
        var assignment4 = new PlayerCampaignAssignmentEntity
        {
            PlayerId = amyCarter.PlayerId,
            CampaignId = campaignA.CampaignId,
            ClubId = ClubAId,
            CreatedById = ClubAMemberId,
            PlacementOutcome = PlacementOutcome.NotSelected,
            ConcurrencyToken = _tokens[3]
        };
        var assignment5 = new PlayerCampaignAssignmentEntity
        {
            PlayerId = benDoyle.PlayerId,
            CampaignId = campaignA.CampaignId,
            ClubId = ClubAId,
            CreatedById = ClubAMemberId,
            PlacementOutcome = PlacementOutcome.Undecided,
            ConcurrencyToken = _tokens[4]
        };
        var assignmentB = new PlayerCampaignAssignmentEntity
        {
            PlayerId = clubBPlayer.PlayerId,
            CampaignId = campaignB.CampaignId,
            ClubId = ClubBId,
            CreatedById = ClubBMemberId,
            PlacementOutcome = PlacementOutcome.Assigned,
            TeamId = teamB.TeamId
        };
        db.PlayerCampaignAssignments.AddRange(assignment1, assignment2, assignment3, assignment4, assignment5, assignmentB);
        db.SaveChanges();

        _campaignAId = campaignA.CampaignId;
        _campaignBId = campaignB.CampaignId;
        _zoeAdamsAssignedId = assignment1.PlayerCampaignAssignmentId;
        _zoeAdamsWithdrawnId = assignment2.PlayerCampaignAssignmentId;
        _amyBarnesId = assignment3.PlayerCampaignAssignmentId;
        _amyCarterId = assignment4.PlayerCampaignAssignmentId;
        _benDoyleId = assignment5.PlayerCampaignAssignmentId;
    }
}
