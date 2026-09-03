using Microsoft.EntityFrameworkCore;
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
/// Tests CampaignMetadataService authorization, lifecycle guards, tenancy, and enrollment invariance.
/// </summary>
public sealed class CampaignMetadataServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAAdminId = 200;
    private const long ClubAMemberId = 201;
    private const long ClubBAdminId = 202;
    private const long SeasonAId = 500;
    private const long SeasonBId = 501;
    private const long HistoricalSeasonAId = 502;
    private const long ActiveCampaignId = 600;
    private const long ClosedCampaignId = 601;
    private const long DuplicateNameCampaignId = 602;
    private const long ClubBCampaignId = 603;
    private const long PlayerAId = 700;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>Initializes seeded data for two clubs.</summary>
    public CampaignMetadataServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies an administrator can update an Active campaign's name, dates, and season.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_UpdatesMetadata_WhenCampaignIsActive()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateCampaignMetadataInput
            {
                CampaignId = ActiveCampaignId,
                Name = "Renamed Campaign",
                SeasonId = SeasonAId,
                StartDate = new DateOnly(2026, 6, 15),
                PlannedEndDate = new DateOnly(2026, 8, 30)
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CampaignId.ShouldBe(ActiveCampaignId);
        result.Value.Name.ShouldBe("Renamed Campaign");
        result.Value.StartDate.ShouldBe(new DateOnly(2026, 6, 15));
        result.Value.PlannedEndDate.ShouldBe(new DateOnly(2026, 8, 30));
        result.Value.Status.ShouldBe(CampaignStatus.Active);
        result.Value.SeasonId.ShouldBe(SeasonAId);
    }

    /// <summary>
    /// Verifies that updating campaign metadata does not alter existing player assignments.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_PreservesPlayerAssignments_AfterMetadataChange()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        await service.UpdateAsync(
            new UpdateCampaignMetadataInput
            {
                CampaignId = ActiveCampaignId,
                Name = "Unchanged Roster Campaign",
                SeasonId = SeasonAId,
                StartDate = new DateOnly(2026, 6, 1)
            },
            TestContext.Current.CancellationToken);

        await using var verify = _harness.CreateAdminContext();
        var assignmentCount = await verify.PlayerCampaignAssignments
            .Where(a => a.CampaignId == ActiveCampaignId)
            .CountAsync(TestContext.Current.CancellationToken);
        assignmentCount.ShouldBe(1);
    }

    /// <summary>
    /// Verifies a Closed campaign rejects metadata updates with Conflict.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsConflict_WhenCampaignIsClosed()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateCampaignMetadataInput
            {
                CampaignId = ClosedCampaignId,
                Name = "Attempting Closed Update",
                SeasonId = SeasonAId,
                StartDate = new DateOnly(2026, 6, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>
    /// Verifies non-admin users are forbidden from updating campaign metadata.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsForbidden_WhenCallerIsNotClubAdmin()
    {
        ActAs(ClubAMemberId, ClubAId, isClubAdmin: false);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateCampaignMetadataInput
            {
                CampaignId = ActiveCampaignId,
                Name = "Non-admin attempt",
                SeasonId = SeasonAId,
                StartDate = new DateOnly(2026, 6, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies tenant filters prevent updating another club's campaign.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsNotFound_ForCrossTenantCampaign()
    {
        ActAs(ClubBAdminId, ClubBId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateCampaignMetadataInput
            {
                CampaignId = ActiveCampaignId,
                Name = "Cross-tenant attempt",
                SeasonId = SeasonBId,
                StartDate = new DateOnly(2026, 6, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies a duplicate campaign name within the same season is rejected with Conflict.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsConflict_WhenNameIsDuplicateInSeason()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateCampaignMetadataInput
            {
                CampaignId = DuplicateNameCampaignId,
                Name = "Active Campaign",   // same name as ActiveCampaignId
                SeasonId = SeasonAId,
                StartDate = new DateOnly(2026, 6, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>
    /// Verifies referencing an unknown season returns NotFound.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsNotFound_WhenSeasonDoesNotExist()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateCampaignMetadataInput
            {
                CampaignId = ActiveCampaignId,
                Name = "Valid Name",
                SeasonId = 9999,
                StartDate = new DateOnly(2026, 6, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies structural validation runs before database access and returns Validation.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsValidation_WhenInputIsStructurallyInvalid()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateCampaignMetadataInput
            {
                CampaignId = ActiveCampaignId,
                Name = "  ",   // whitespace
                SeasonId = SeasonAId,
                StartDate = new DateOnly(2026, 6, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>
    /// Verifies that a club admin cannot move a campaign into a season belonging to a different club.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsNotFound_WhenTargetSeasonBelongsToDifferentClub()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateCampaignMetadataInput
            {
                CampaignId = ActiveCampaignId,
                Name = "Valid Name",
                SeasonId = SeasonBId,   // Season B belongs to Club B
                StartDate = new DateOnly(2026, 6, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>Verifies campaign metadata cannot be moved out of the club's current season.</summary>
    [Fact]
    public async Task UpdateAsync_ReturnsConflict_WhenTargetSeasonIsHistorical()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);

        var result = await CreateService().UpdateAsync(
            new UpdateCampaignMetadataInput
            {
                CampaignId = ActiveCampaignId,
                Name = "Historical Move",
                SeasonId = HistoricalSeasonAId,
                StartDate = new DateOnly(2025, 6, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
        result.Problem.Detail.ShouldBe(
            "A campaign must belong to the club's current season to update its metadata.");

        await using var verify = _harness.CreateAdminContext();
        (await verify.Campaigns.SingleAsync(
            campaign => campaign.CampaignId == ActiveCampaignId,
            TestContext.Current.CancellationToken)).SeasonId.ShouldBe(SeasonAId);
    }

    private CampaignMetadataService CreateService()
    {
        IDbContextFactory<NovaDbContext> dbContextFactory =
            new TestDbContextFactory<NovaDbContext>(_harness.CreateTenantContext);

        return new CampaignMetadataService(
            dbContextFactory,
            _harness.CurrentUser,
            NullLogger<CampaignMetadataService>.Instance);
    }

    private void ActAs(long userId, long clubId, bool isClubAdmin = false)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                ClubId = ClubAId,
                Name = "Club A",
                City = "Austin",
                State = "TX",
                CreatedById = ClubAAdminId
            },
            new ClubEntity
            {
                CreationOperationId = Guid.NewGuid(),
                ClubId = ClubBId,
                Name = "Club B",
                City = "Boston",
                State = "MA",
                CreatedById = ClubBAdminId
            });

        db.Seasons.AddRange(
            new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                SeasonId = SeasonAId,
                Name = "Season A",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                SeasonId = SeasonBId,
                Name = "Season B",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            },
            new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                SeasonId = HistoricalSeasonAId,
                Name = "Historical Season A",
                StartDate = new DateOnly(2025, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            });

        db.Campaigns.AddRange(
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = ActiveCampaignId,
                Name = "Active Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = SeasonAId,
                ClubId = ClubAId,
                Status = CampaignStatus.Active,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = ClosedCampaignId,
                Name = "Closed Campaign",
                StartDate = new DateOnly(2026, 3, 1),
                SeasonId = SeasonAId,
                ClubId = ClubAId,
                Status = CampaignStatus.Closed,
                ClosedAt = DateTimeOffset.UtcNow,
                ClosedById = ClubAAdminId,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = DuplicateNameCampaignId,
                Name = "Another Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = SeasonAId,
                ClubId = ClubAId,
                Status = CampaignStatus.Draft,
                CreatedById = ClubAAdminId
            },
            new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                CampaignId = ClubBCampaignId,
                Name = "Club B Active Campaign",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = SeasonBId,
                ClubId = ClubBId,
                Status = CampaignStatus.Active,
                CreatedById = ClubBAdminId
            });

        db.Players.Add(new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            PlayerId = PlayerAId,
            FirstName = "Test",
            LastName = "Player",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            ClubId = ClubAId,
            CreatedById = ClubAAdminId
        });

        db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
        {
            PlayerCampaignAssignmentId = 1,
            CampaignId = ActiveCampaignId,
            PlayerId = PlayerAId,
            ClubId = ClubAId,
            PlacementOutcome = PlacementOutcome.Undecided,
            CreatedById = ClubAAdminId
        });

        db.SaveChanges();
        db.Clubs.Single(club => club.ClubId == ClubAId).CurrentSeasonId = SeasonAId;
        db.Clubs.Single(club => club.ClubId == ClubBId).CurrentSeasonId = SeasonBId;
        db.SaveChanges();
    }
}
