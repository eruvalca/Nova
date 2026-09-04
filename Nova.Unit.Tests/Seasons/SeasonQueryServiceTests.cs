using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Seasons;
using Nova.Shared.Enums;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Seasons;

/// <summary>Verifies tenant-safe season ordering, paging, and detail history.</summary>
public sealed class SeasonQueryServiceTests : IDisposable
{
    private const long ClubAId = 300;
    private const long ClubBId = 301;
    private const long MemberId = 400;
    private readonly TenancyTestHarness _harness = new();
    private long _currentSeasonId;
    private long _historicalSeasonId;
    private long _otherSeasonId;

    /// <summary>Seeds current, historical, and cross-tenant seasons.</summary>
    public SeasonQueryServiceTests()
    {
        _harness.CurrentUser.UserId = MemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            new ClubEntity
            {
                ClubId = ClubAId,
                CreationOperationId = Guid.NewGuid(),
                Name = "A",
                City = "Austin",
                State = "TX",
                CreatedById = MemberId
            },
            new ClubEntity
            {
                ClubId = ClubBId,
                CreationOperationId = Guid.NewGuid(),
                Name = "B",
                City = "Boston",
                State = "MA",
                CreatedById = MemberId
            });
        var current = NewSeason("Current", new DateOnly(2025, 1, 1), ClubAId);
        var history = NewSeason("History", new DateOnly(2026, 1, 1), ClubAId);
        var other = NewSeason("Other", new DateOnly(2027, 1, 1), ClubBId);
        db.Seasons.AddRange(current, history, other);
        db.SaveChanges();
        db.Clubs.Single(club => club.ClubId == ClubAId).CurrentSeasonId = current.SeasonId;
        db.Clubs.Single(club => club.ClubId == ClubBId).CurrentSeasonId = other.SeasonId;
        db.Campaigns.AddRange(
            NewCampaign("Older", new DateOnly(2026, 2, 1), history.SeasonId),
            NewCampaign("Newer", new DateOnly(2026, 3, 1), history.SeasonId));
        db.SaveChanges();
        _currentSeasonId = current.SeasonId;
        _historicalSeasonId = history.SeasonId;
        _otherSeasonId = other.SeasonId;
    }

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>Verifies currentness outranks chronology and SQL paging is bounded.</summary>
    [Fact]
    public async Task ListAsync_ReturnsCurrentFirst_ThenHistory()
    {
        var result = await CreateService().ListAsync(
            new GetSeasonListInput { Page = 1, PageSize = 2 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(2);
        result.Value.Items.Select(season => season.SeasonId)
            .ShouldBe([_currentSeasonId, _historicalSeasonId]);
        result.Value.Items[0].IsCurrent.ShouldBeTrue();
        result.Value.Items[1].IsCurrent.ShouldBeFalse();
    }

    /// <summary>Verifies list currentness is projected in the page query instead of read separately.</summary>
    [Fact]
    public async Task ListAsync_UsesOnePageStatement_ForRowsAndCurrentness()
    {
        var interceptor = new CountingCommandInterceptor();

        var result = await CreateService(interceptor).ListAsync(
            new GetSeasonListInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        interceptor.ReaderExecutionCount.ShouldBe(2);
    }

    /// <summary>Verifies a structurally valid maximum page cannot overflow the SQL offset.</summary>
    [Fact]
    public async Task ListAsync_ReturnsEmptyPage_WhenPageOffsetExceedsInt32()
    {
        var result = await CreateService().ListAsync(
            new GetSeasonListInput
            {
                Page = int.MaxValue,
                PageSize = GetSeasonListInput.MaximumPageSize
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldBeEmpty();
        result.Value.TotalCount.ShouldBe(2);
    }

    /// <summary>Verifies detail orders campaigns newest-first and hides another tenant's season.</summary>
    [Fact]
    public async Task GetAsync_ReturnsBoundedHistory_AndNonDisclosingNotFound()
    {
        var detail = await CreateService().GetAsync(
            new GetSeasonDetailInput
            {
                SeasonId = _historicalSeasonId,
                CampaignPage = 1,
                CampaignPageSize = 1
            },
            TestContext.Current.CancellationToken);

        detail.IsSuccess.ShouldBeTrue();
        detail.Value.CampaignTotalCount.ShouldBe(2);
        detail.Value.Campaigns.Single().Name.ShouldBe("Newer");

        var hidden = await CreateService().GetAsync(
            new GetSeasonDetailInput { SeasonId = _otherSeasonId },
            TestContext.Current.CancellationToken);
        hidden.IsProblem.ShouldBeTrue();
        hidden.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>Verifies detail currentness is projected with metadata instead of read separately.</summary>
    [Fact]
    public async Task GetAsync_UsesOneMetadataStatement_ForSeasonAndCurrentness()
    {
        var interceptor = new CountingCommandInterceptor();

        var result = await CreateService(interceptor).GetAsync(
            new GetSeasonDetailInput { SeasonId = _historicalSeasonId },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        interceptor.ReaderExecutionCount.ShouldBe(3);
    }

    /// <summary>Verifies maximum campaign paging cannot wrap to an earlier result page.</summary>
    [Fact]
    public async Task GetAsync_ReturnsEmptyCampaignPage_WhenPageOffsetExceedsInt32()
    {
        var result = await CreateService().GetAsync(
            new GetSeasonDetailInput
            {
                SeasonId = _historicalSeasonId,
                CampaignPage = int.MaxValue,
                CampaignPageSize = GetSeasonListInput.MaximumPageSize
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Campaigns.ShouldBeEmpty();
        result.Value.CampaignTotalCount.ShouldBe(2);
    }

    /// <summary>Verifies season reads require an authenticated club member.</summary>
    [Fact]
    public async Task Queries_ReturnForbidden_WithoutClubMembership()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var list = await service.ListAsync(new GetSeasonListInput(), TestContext.Current.CancellationToken);
        var detail = await service.GetAsync(
            new GetSeasonDetailInput { SeasonId = _currentSeasonId },
            TestContext.Current.CancellationToken);

        list.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        detail.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies a season-list read failure surfaces as a server error rather than an exception.</summary>
    [Fact]
    public async Task ListAsync_ReturnsServerError_WhenReadFails()
    {
        _harness.CurrentUser.UserId = MemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        var throwingFactory = Substitute.For<IDbContextFactory<NovaReadDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns<Task<NovaReadDbContext>>(_ => throw new InvalidOperationException("boom"));
        var service = new SeasonQueryService(
            throwingFactory,
            _harness.CurrentUser,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SeasonQueryService>.Instance);

        var result = await service.ListAsync(new GetSeasonListInput(), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies a season-detail read failure surfaces as a server error rather than an exception.</summary>
    [Fact]
    public async Task GetAsync_ReturnsServerError_WhenReadFails()
    {
        _harness.CurrentUser.UserId = MemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        var throwingFactory = Substitute.For<IDbContextFactory<NovaReadDbContext>>();
        throwingFactory.CreateDbContextAsync(Arg.Any<CancellationToken>())
            .Returns<Task<NovaReadDbContext>>(_ => throw new InvalidOperationException("boom"));
        var service = new SeasonQueryService(
            throwingFactory,
            _harness.CurrentUser,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SeasonQueryService>.Instance);

        var result = await service.GetAsync(
            new GetSeasonDetailInput { SeasonId = _currentSeasonId },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Creates a query service over fresh read contexts.</summary>
    private SeasonQueryService CreateService(CountingCommandInterceptor? interceptor = null)
        => new(
            new ReadFactory(_harness, interceptor),
            _harness.CurrentUser,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<SeasonQueryService>.Instance);

    /// <summary>Creates a season entity for the supplied club.</summary>
    private static SeasonEntity NewSeason(string name, DateOnly startDate, long clubId)
        => new()
        {
            CreationOperationId = Guid.NewGuid(),
            Name = name,
            StartDate = startDate,
            ConcurrencyToken = Guid.NewGuid(),
            ClubId = clubId,
            CreatedById = MemberId
        };

    /// <summary>Creates a closed campaign in a Club A season.</summary>
    private static CampaignEntity NewCampaign(string name, DateOnly startDate, long seasonId)
        => new()
        {
            CreationOperationId = Guid.NewGuid(),
            Name = name,
            StartDate = startDate,
            Status = CampaignStatus.Closed,
            ClosedAt = DateTimeOffset.UtcNow,
            ClosedById = MemberId,
            SeasonId = seasonId,
            ClubId = ClubAId,
            CreatedById = MemberId
        };

    /// <summary>Creates read contexts from the shared SQLite database.</summary>
    private sealed class ReadFactory(
        TenancyTestHarness harness,
        CountingCommandInterceptor? interceptor) : IDbContextFactory<NovaReadDbContext>
    {
        /// <inheritdoc />
        public NovaReadDbContext CreateDbContext()
            => interceptor is null
                ? harness.CreateReadContext()
                : harness.CreateReadContext(interceptor);

        /// <inheritdoc />
        public Task<NovaReadDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CreateDbContext());
    }
}
