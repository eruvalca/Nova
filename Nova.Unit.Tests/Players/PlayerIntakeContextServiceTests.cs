using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Players;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Direct SQLite shell tests for <see cref="PlayerIntakeContextService"/>: the club's current
/// Active campaign, authorization, and tenant isolation.
/// </summary>
public sealed class PlayerIntakeContextServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 101;
    private const long ClubAUserId = 200;
    private const long ClubBUserId = 201;
    private const long QuietClubId = 102;
    private const long QuietClubUserId = 202;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>The seeded Active campaign's identity, assigned while seeding.</summary>
    private long _activeCampaignId;

    /// <summary>
    /// Initializes seeded clubs whose campaigns differ by lifecycle status.
    /// </summary>
    public PlayerIntakeContextServiceTests() => Seed();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ReturnsActiveCampaignForOrdinaryClubMemberAsync()
    {
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;

        var result = await CreateService().GetPlayerIntakeContextAsync(
            new GetPlayerIntakeContextInput { ClubId = ClubAId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CampaignId.ShouldBe(_activeCampaignId);
        result.Value.CampaignName.ShouldBe("Summer Tryouts");
    }

    [Fact]
    public async Task ReturnsExplicitNullPairWhenNoCampaignIsActiveAsync()
    {
        _harness.CurrentUser.UserId = QuietClubUserId;
        _harness.CurrentUser.ClubId = QuietClubId;

        var result = await CreateService().GetPlayerIntakeContextAsync(
            new GetPlayerIntakeContextInput { ClubId = QuietClubId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CampaignId.ShouldBeNull();
        result.Value.CampaignName.ShouldBeNull();
    }

    [Fact]
    public async Task DoesNotReturnAnotherClubsActiveCampaignAsync()
    {
        _harness.CurrentUser.UserId = QuietClubUserId;
        _harness.CurrentUser.ClubId = QuietClubId;

        var result = await CreateService().GetPlayerIntakeContextAsync(
            new GetPlayerIntakeContextInput { ClubId = QuietClubId },
            TestContext.Current.CancellationToken);

        result.Value.CampaignId.ShouldNotBe(_activeCampaignId);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, ClubAId)]
    [InlineData(ClubAUserId, ClubBId)]
    public async Task RejectsAnonymousAndCrossClubReadsAsync(long? userId, long requestedClub)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetPlayerIntakeContextAsync(
            new GetPlayerIntakeContextInput { ClubId = requestedClub },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task RejectsInvalidClubBeforeReadingAsync()
    {
        _harness.CurrentUser.UserId = ClubAUserId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetPlayerIntakeContextAsync(
            new GetPlayerIntakeContextInput { ClubId = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>The seeded Active campaign for club A, verified during setup.</summary>
    private PlayerIntakeContextService CreateService()
    {
        IDbContextFactory<NovaReadDbContext> readDbFactory =
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext);
        return new PlayerIntakeContextService(
            readDbFactory,
            _harness.CurrentUser,
            NullLogger<PlayerIntakeContextService>.Instance);
    }

#pragma warning disable MA0051 // Keep the complete arrangement of clubs, seasons and campaigns together as one fixture.
    private void Seed()
#pragma warning restore MA0051
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAUserId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBUserId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = QuietClubId, Name = "Quiet Club", City = "Denver", State = "CO", CreatedById = QuietClubUserId });
        db.SaveChanges();

        var seasonA = AddSeason(db, ClubAId, ClubAUserId, "Season A", new DateOnly(2026, 1, 1));
        var seasonQuiet = AddSeason(db, QuietClubId, QuietClubUserId, "Quiet Season", new DateOnly(2026, 1, 1));

        var activeCampaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Summer Tryouts",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = seasonA.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAUserId,
            OpeningOperationId = Guid.NewGuid(),
            OpenedAt = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
            OpenedById = ClubAUserId,
            SeasonOpeningSequence = 1,
            InitialEnrolledPlayerCount = 0,
            InitialActiveTeamCount = 0
        };
        var draftCampaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Autumn Draft",
            StartDate = new DateOnly(2026, 9, 1),
            Status = CampaignStatus.Draft,
            SeasonId = seasonA.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAUserId
        };
        var closedCampaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Prior Season",
            StartDate = new DateOnly(2025, 9, 1),
            Status = CampaignStatus.Closed,
            SeasonId = seasonA.SeasonId,
            ClubId = ClubAId,
            CreatedById = ClubAUserId,
            OpeningOperationId = Guid.NewGuid(),
            OpenedAt = new DateTimeOffset(2025, 9, 1, 0, 0, 0, TimeSpan.Zero),
            OpenedById = ClubAUserId,
            SeasonOpeningSequence = 0,
            InitialEnrolledPlayerCount = 0,
            InitialActiveTeamCount = 0,
            ClosedAt = new DateTimeOffset(2025, 12, 1, 0, 0, 0, TimeSpan.Zero),
            ClosedById = ClubAUserId
        };
        // The quiet club's only campaigns are non-Active, so its intake context is an explicit null pair.
        var quietDraft = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Quiet Draft",
            StartDate = new DateOnly(2026, 9, 1),
            Status = CampaignStatus.Draft,
            SeasonId = seasonQuiet.SeasonId,
            ClubId = QuietClubId,
            CreatedById = QuietClubUserId
        };
        db.Campaigns.AddRange(activeCampaign, draftCampaign, closedCampaign, quietDraft);
        db.SaveChanges();

        _activeCampaignId = activeCampaign.CampaignId;
        _activeCampaignId.ShouldBeGreaterThan(0);
    }

    private static SeasonEntity AddSeason(NovaAdminDbContext db, long clubId, long userId, string name, DateOnly startDate)
    {
        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = name,
            StartDate = startDate,
            ClubId = clubId,
            CreatedById = userId
        };
        db.Seasons.Add(season);
        db.SaveChanges();
        return season;
    }
}
