using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Creates tenant contexts from the shared in-memory SQLite campaign test database.
/// </summary>
/// <param name="harness">The shared tenancy harness.</param>
file sealed class CampaignHarnessDbContextFactory(TenancyTestHarness harness)
    : IDbContextFactory<NovaDbContext>
{
    /// <inheritdoc />
    public NovaDbContext CreateDbContext() => harness.CreateTenantContext();

    /// <inheritdoc />
    public Task<NovaDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
        => Task.FromResult(harness.CreateTenantContext());
}

/// <summary>
/// Verifies campaign creation authorization, tenancy, date policies, initial enrollment, and idempotency.
/// </summary>
public sealed class CampaignCreationServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAAdminId = 101;
    private const long ClubAMemberId = 102;

    private readonly TenancyTestHarness _harness = new();
    private long _clubASeasonId;
    private long _clubBSeasonId;
    private long _activePlayerId;
    private long _archivedPlayerId;

    /// <summary>
    /// Seeds tenant data used by each campaign creation test.
    /// </summary>
    public CampaignCreationServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies an administrator can create an Active campaign in an existing season.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsActiveCampaign_ForExistingSeason()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var input = ValidExistingSeasonInput();

        var result = await CreateService().CreateAsync(
            input,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.OperationId.ShouldBe(input.OperationId);
        result.Value.SeasonId.ShouldBe(_clubASeasonId);
        result.Value.Status.ShouldBe(CampaignStatus.Active);
        result.Value.SeasonCreatedInline.ShouldBeFalse();
        result.Value.EnrolledPlayerCount.ShouldBe(1);
    }

    /// <summary>
    /// Verifies inline season and campaign metadata commit together.
    /// </summary>
    [Fact]
    public async Task Create_CreatesSeasonAndCampaign_ForInlineSeason()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var input = ValidInlineSeasonInput();

        var result = await CreateService().CreateAsync(
            input,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SeasonCreatedInline.ShouldBeTrue();
        result.Value.SeasonName.ShouldBe(input.InlineSeason!.Name);

        using var db = _harness.CreateAdminContext();
        var season = db.Seasons.Single(candidate => candidate.SeasonId == result.Value.SeasonId);
        season.CreationOperationId.ShouldBe(input.OperationId);
        var campaign = db.Campaigns.Single(candidate => candidate.CampaignId == result.Value.CampaignId);
        campaign.CreationOperationId.ShouldBe(input.OperationId);
        campaign.SeasonCreatedInline.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies only Active players in the current club receive initial participation.
    /// </summary>
    [Fact]
    public async Task Create_EnrollsOnlyCurrentClubActivePlayers()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            ValidExistingSeasonInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        using var db = _harness.CreateAdminContext();
        var assignments = db.PlayerCampaignAssignments
            .Where(assignment => assignment.CampaignId == result.Value.CampaignId)
            .ToList();
        assignments.Count.ShouldBe(1);
        assignments[0].PlayerId.ShouldBe(_activePlayerId);
        assignments.ShouldNotContain(assignment => assignment.PlayerId == _archivedPlayerId);
        assignments[0].PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
        assignments[0].TeamId.ShouldBeNull();
    }

    /// <summary>
    /// Verifies replaying a caller operation ID returns the original aggregate without duplicate writes.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsOriginalResult_WhenOperationIsRepeated()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var service = CreateService();
        var input = ValidInlineSeasonInput();

        var first = await service.CreateAsync(input, TestContext.Current.CancellationToken);
        var repeated = await service.CreateAsync(
            input with
            {
                Name = "Different ignored payload",
                InlineSeason = input.InlineSeason! with { Name = "Different ignored season" }
            },
            TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        repeated.IsSuccess.ShouldBeTrue();
        repeated.Value.ShouldBe(first.Value);

        using var db = _harness.CreateAdminContext();
        db.Campaigns.Count(candidate => candidate.CreationOperationId == input.OperationId).ShouldBe(1);
        db.Seasons.Count(candidate => candidate.CreationOperationId == input.OperationId).ShouldBe(1);
    }

    /// <summary>
    /// Verifies replay returns creation-time status and enrollment count after the aggregate changes.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsCreationSnapshot_AfterCampaignChanges()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var service = CreateService();
        var input = ValidExistingSeasonInput();
        var first = await service.CreateAsync(input, TestContext.Current.CancellationToken);
        first.IsSuccess.ShouldBeTrue();

        using (var db = _harness.CreateAdminContext())
        {
            var campaign = db.Campaigns.Single(candidate => candidate.CampaignId == first.Value.CampaignId);
            campaign.Status = CampaignStatus.Closed;
            campaign.ClosedAt = DateTimeOffset.UtcNow;
            campaign.ClosedById = ClubAAdminId;

            var latePlayer = new PlayerEntity
            {
                FirstName = "Late",
                LastName = "Player",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                LifecycleStatus = LifecycleStatus.Active,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            };
            db.Players.Add(latePlayer);
            db.SaveChanges();
            db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
            {
                PlayerId = latePlayer.PlayerId,
                CampaignId = campaign.CampaignId,
                ClubId = ClubAId,
                PlacementOutcome = PlacementOutcome.Undecided,
                CreatedById = ClubAAdminId
            });
            db.SaveChanges();
        }

        var replay = await service.CreateAsync(input, TestContext.Current.CancellationToken);

        replay.IsSuccess.ShouldBeTrue();
        replay.Value.Status.ShouldBe(CampaignStatus.Active);
        replay.Value.EnrolledPlayerCount.ShouldBe(first.Value.EnrolledPlayerCount);
    }

    /// <summary>
    /// Verifies a cross-tenant season identifier is hidden as not found.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsNotFound_ForCrossTenantSeason()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            ValidExistingSeasonInput() with { ExistingSeasonId = _clubBSeasonId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies non-administrators cannot create campaigns.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsForbidden_ForNonAdmin()
    {
        ActAs(ClubAMemberId, ClubAId, isAdmin: false);

        var result = await CreateService().CreateAsync(
            ValidExistingSeasonInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies structural validation runs before authorization or database work.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsValidation_ForInvalidInput()
    {
        ActAs(userId: null, clubId: null, isAdmin: false);

        var result = await CreateService().CreateAsync(
            ValidExistingSeasonInput() with { OperationId = Guid.Empty },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.ShouldContainKey(nameof(CreateCampaignInput.OperationId));
    }

    /// <summary>
    /// Verifies a campaign cannot begin before its selected season.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsValidation_WhenCampaignStartsBeforeSeason()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            ValidExistingSeasonInput() with
            {
                StartDate = new DateOnly(2025, 12, 31),
                PlannedEndDate = new DateOnly(2026, 6, 30)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.ShouldContainKey(nameof(CreateCampaignInput.StartDate));
        CampaignCountForClubA().ShouldBe(0);
    }

    /// <summary>
    /// Verifies a finite season rejects an open-ended campaign.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsValidation_WhenFiniteSeasonCampaignHasNoEnd()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            ValidExistingSeasonInput() with { PlannedEndDate = null },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.ShouldContainKey(nameof(CreateCampaignInput.PlannedEndDate));
        CampaignCountForClubA().ShouldBe(0);
    }

    /// <summary>
    /// Verifies a campaign cannot begin after its finite season ends.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsValidation_WhenCampaignStartsAfterSeason()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            ValidExistingSeasonInput() with
            {
                StartDate = new DateOnly(2027, 1, 1),
                PlannedEndDate = new DateOnly(2027, 6, 30)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.ShouldContainKey(nameof(CreateCampaignInput.StartDate));
        CampaignCountForClubA().ShouldBe(0);
    }

    /// <summary>
    /// Verifies a campaign cannot have a planned end after its finite season ends.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsValidation_WhenCampaignEndsAfterSeason()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);

        var result = await CreateService().CreateAsync(
            ValidExistingSeasonInput() with
            {
                PlannedEndDate = new DateOnly(2027, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldNotBeNull();
        result.Problem.Errors.ShouldContainKey(nameof(CreateCampaignInput.PlannedEndDate));
        CampaignCountForClubA().ShouldBe(0);
    }

    /// <summary>
    /// Verifies duplicate campaign names are rejected within one season.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsConflict_ForDuplicateCampaignNameInSeason()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var first = await CreateService().CreateAsync(
            ValidExistingSeasonInput(),
            TestContext.Current.CancellationToken);

        var duplicate = await CreateService().CreateAsync(
            ValidExistingSeasonInput() with { OperationId = Guid.CreateVersion7() },
            TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        duplicate.IsProblem.ShouldBeTrue();
        duplicate.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        CampaignCountForClubA().ShouldBe(1);
    }

    /// <summary>
    /// Verifies inline creation does not silently reuse a same-name existing season.
    /// </summary>
    [Fact]
    public async Task Create_ReturnsConflict_ForDuplicateInlineSeasonName()
    {
        ActAs(ClubAAdminId, ClubAId, isAdmin: true);
        var input = ValidInlineSeasonInput() with
        {
            InlineSeason = ValidInlineSeasonInput().InlineSeason! with { Name = "Club A Season" }
        };

        var result = await CreateService().CreateAsync(
            input,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        CampaignCountForClubA().ShouldBe(0);
    }

    /// <summary>
    /// Creates the service using the tenant test context factory.
    /// </summary>
    /// <returns>A campaign creation service scoped to the mutable fake current user.</returns>
    private CampaignCreationService CreateService()
        => new(
            new CampaignHarnessDbContextFactory(_harness),
            _harness.CurrentUser,
            NullLogger<CampaignCreationService>.Instance);

    /// <summary>
    /// Sets the fake current user for the next tenant context.
    /// </summary>
    /// <param name="userId">The acting user identifier.</param>
    /// <param name="clubId">The acting user's club identifier.</param>
    /// <param name="isAdmin">Whether the actor is a club administrator.</param>
    private void ActAs(long? userId, long? clubId, bool isAdmin)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isAdmin;
    }

    /// <summary>
    /// Counts campaigns persisted for Club A through the unfiltered admin context.
    /// </summary>
    /// <returns>The number of Club A campaigns.</returns>
    private int CampaignCountForClubA()
    {
        using var db = _harness.CreateAdminContext();
        return db.Campaigns.Count(campaign => campaign.ClubId == ClubAId);
    }

    /// <summary>
    /// Creates a valid existing-season campaign request.
    /// </summary>
    /// <returns>A request targeting Club A's finite season.</returns>
    private CreateCampaignInput ValidExistingSeasonInput() => new()
    {
        OperationId = Guid.CreateVersion7(),
        Name = "Summer Tryouts",
        StartDate = new DateOnly(2026, 6, 1),
        PlannedEndDate = new DateOnly(2026, 6, 30),
        ExistingSeasonId = _clubASeasonId
    };

    /// <summary>
    /// Creates a valid inline-season campaign request.
    /// </summary>
    /// <returns>A request that creates a finite inline season.</returns>
    private static CreateCampaignInput ValidInlineSeasonInput() => new()
    {
        OperationId = Guid.CreateVersion7(),
        Name = "Fall Tryouts",
        StartDate = new DateOnly(2026, 9, 1),
        PlannedEndDate = new DateOnly(2026, 9, 30),
        InlineSeason = new InlineSeasonInput
        {
            Name = "Fall 2026",
            StartDate = new DateOnly(2026, 8, 1),
            EndDate = new DateOnly(2026, 12, 31)
        }
    };

    /// <summary>
    /// Seeds clubs, finite seasons, and Active/Archived players across two tenants.
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
                CreatedById = ClubAAdminId
            });

        var seasonA = new SeasonEntity
        {
            Name = "Club A Season",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        var seasonB = new SeasonEntity
        {
            Name = "Club B Season",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            ClubId = ClubBId,
            CreatedById = ClubAAdminId
        };
        db.Seasons.AddRange(seasonA, seasonB);

        var activePlayer = new PlayerEntity
        {
            FirstName = "Active",
            LastName = "Player",
            DateOfBirth = new DateOnly(2010, 1, 1),
            GraduationYear = 2028,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        var archivedPlayer = new PlayerEntity
        {
            FirstName = "Archived",
            LastName = "Player",
            DateOfBirth = new DateOnly(2011, 1, 1),
            GraduationYear = 2029,
            LifecycleStatus = LifecycleStatus.Archived,
            ArchivedAt = DateTimeOffset.UtcNow,
            ArchivedById = ClubAAdminId,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        };
        var otherClubPlayer = new PlayerEntity
        {
            FirstName = "Other",
            LastName = "Player",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = ClubBId,
            CreatedById = ClubAAdminId
        };
        db.Players.AddRange(activePlayer, archivedPlayer, otherClubPlayer);
        db.SaveChanges();

        _clubASeasonId = seasonA.SeasonId;
        _clubBSeasonId = seasonB.SeasonId;
        _activePlayerId = activePlayer.PlayerId;
        _archivedPlayerId = archivedPlayer.PlayerId;
    }
}
