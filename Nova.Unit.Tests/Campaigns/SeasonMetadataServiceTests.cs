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
/// Tests SeasonMetadataService authorization, tenancy, and duplicate-name conflict.
/// </summary>
public sealed class SeasonMetadataServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAAdminId = 200;
    private const long ClubAMemberId = 201;
    private const long ClubBAdminId = 202;
    private const long SeasonAId = 500;
    private const long SeasonBId = 501;
    private const long SeasonA2Id = 502;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>Initializes seeded data for two clubs.</summary>
    public SeasonMetadataServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>
    /// Verifies an administrator can update a season's name and dates.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_UpdatesMetadata_ForValidInput()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateSeasonMetadataInput
            {
                SeasonId = SeasonAId,
                Name = "2026 Spring Season",
                StartDate = new DateOnly(2026, 2, 1),
                EndDate = new DateOnly(2026, 8, 31)
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.SeasonId.ShouldBe(SeasonAId);
        result.Value.Name.ShouldBe("2026 Spring Season");
        result.Value.StartDate.ShouldBe(new DateOnly(2026, 2, 1));
        result.Value.EndDate.ShouldBe(new DateOnly(2026, 8, 31));
    }

    /// <summary>
    /// Verifies that updating season metadata does not delete linked campaigns.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_PreservesLinkedCampaigns_AfterMetadataChange()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        await service.UpdateAsync(
            new UpdateSeasonMetadataInput
            {
                SeasonId = SeasonAId,
                Name = "Season Renamed",
                StartDate = new DateOnly(2026, 1, 1)
            },
            TestContext.Current.CancellationToken);

        await using var verify = _harness.CreateAdminContext();
        var campaignCount = await verify.Campaigns
            .Where(c => c.SeasonId == SeasonAId)
            .CountAsync(TestContext.Current.CancellationToken);
        campaignCount.ShouldBe(1);
    }

    /// <summary>
    /// Verifies non-admin callers are forbidden from updating season metadata.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsForbidden_WhenCallerIsNotClubAdmin()
    {
        ActAs(ClubAMemberId, ClubAId, isClubAdmin: false);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateSeasonMetadataInput
            {
                SeasonId = SeasonAId,
                Name = "Non-admin attempt",
                StartDate = new DateOnly(2026, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies tenant filters prevent updating another club's season.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsNotFound_ForCrossTenantSeason()
    {
        ActAs(ClubBAdminId, ClubBId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateSeasonMetadataInput
            {
                SeasonId = SeasonAId,
                Name = "Cross-tenant attempt",
                StartDate = new DateOnly(2026, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies a duplicate season name is rejected with Conflict.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsConflict_WhenNameIsDuplicate()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateSeasonMetadataInput
            {
                SeasonId = SeasonA2Id,
                Name = "Season A",  // same name as SeasonAId
                StartDate = new DateOnly(2026, 7, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>
    /// Verifies a season can be saved with its own existing name (no false conflict with itself).
    /// </summary>
    [Fact]
    public async Task UpdateAsync_Succeeds_WhenNameIsUnchanged()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateSeasonMetadataInput
            {
                SeasonId = SeasonAId,
                Name = "Season A",    // same as current
                StartDate = new DateOnly(2026, 2, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies structural validation runs before database access.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsValidation_WhenInputIsStructurallyInvalid()
    {
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var service = CreateService();

        var result = await service.UpdateAsync(
            new UpdateSeasonMetadataInput
            {
                SeasonId = SeasonAId,
                Name = "  ",   // whitespace
                StartDate = new DateOnly(2026, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    private SeasonMetadataService CreateService()
    {
        IDbContextFactory<NovaDbContext> dbContextFactory =
            new TestDbContextFactory<NovaDbContext>(_harness.CreateTenantContext);

        return new SeasonMetadataService(
            dbContextFactory,
            _harness.CurrentUser,
            NullLogger<SeasonMetadataService>.Instance);
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
                SeasonId = SeasonAId,
                Name = "Season A",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new SeasonEntity
            {
                SeasonId = SeasonA2Id,
                Name = "Season A2",
                StartDate = new DateOnly(2026, 7, 1),
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            },
            new SeasonEntity
            {
                SeasonId = SeasonBId,
                Name = "Season B",
                StartDate = new DateOnly(2026, 1, 1),
                ClubId = ClubBId,
                CreatedById = ClubBAdminId
            });

        db.Campaigns.Add(new CampaignEntity
        {
            CampaignId = 600,
            Name = "Season A Campaign",
            StartDate = new DateOnly(2026, 3, 1),
            SeasonId = SeasonAId,
            ClubId = ClubAId,
            Status = CampaignStatus.Active,
            CreatedById = ClubAAdminId
        });

        db.SaveChanges();
    }
}
