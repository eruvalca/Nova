using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies PostgreSQL campaign setup exposes only the club's current season.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignQueryOrderingPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>Proves PostgreSQL obtains the exact active-team count and bounded preview in one reader.</summary>
    [Fact]
    public async Task GetOpeningReadiness_CountsAndPreviewsActiveTeams_WithOneTeamReader()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(1, cancellationToken);
        long campaignId;
        TeamEntity[] activeTeams;
        await using (var db = fixture.CreateAdminContext())
        {
            var campaign = new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = "Readiness snapshot",
                Status = CampaignStatus.Draft,
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = seed.CurrentSeasonId,
                ClubId = seed.ClubId,
                CreatedById = seed.MemberUserId
            };
            activeTeams = new[] { "Foxtrot", "Echo", "Delta", "Charlie", "Bravo", "Alpha" }
                .Select(name => new TeamEntity
                {
                    CreationOperationId = Guid.NewGuid(),
                    Name = name,
                    GraduationYear = 2030,
                    ClubId = seed.ClubId,
                    CreatedById = seed.MemberUserId
                }).ToArray();
            db.Campaigns.Add(campaign);
            db.Teams.AddRange(activeTeams);
            db.Teams.Add(new TeamEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = "000 archived",
                GraduationYear = 2030,
                ClubId = seed.ClubId,
                CreatedById = seed.MemberUserId,
                LifecycleStatus = LifecycleStatus.Archived,
                ArchivedAt = DateTimeOffset.UtcNow,
                ArchivedById = seed.MemberUserId
            });
            await db.SaveChangesAsync(cancellationToken);
            campaignId = campaign.CampaignId;
        }

        ActAs(seed.MemberUserId, seed.ClubId, isAdmin: true);
        var counter = new CountingCommandInterceptor();
        var service = new CampaignQueryService(new PostgresReadContextFactory(fixture, counter), fixture.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetOpeningReadinessAsync(campaignId, cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ActiveTeamCount.ShouldBe(6);
        result.Value.ActiveTeams.ShouldBe(activeTeams.OrderBy(team => team.Name, StringComparer.Ordinal).Take(5)
            .Select(team => new CampaignOpeningTeam(team.TeamId, team.Name)));
        counter.ReaderCommands.Count(command => command.Contains("\"Teams\"", StringComparison.Ordinal)).ShouldBe(1,
            "the exact count and five-team preview must share one PostgreSQL reader");
    }

    /// <summary>Verifies PostgreSQL pages Closed history by actual closure time before campaign start dates.</summary>
    [Fact]
    public async Task GetCampaignList_PagesClosedCampaigns_ByClosureTime()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(1, cancellationToken);
        long expectedFirstId;
        long expectedSecondId;
        await using (var db = fixture.CreateAdminContext())
        {
            var first = new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = "Later closure",
                Status = CampaignStatus.Closed,
                StartDate = new DateOnly(2026, 2, 1),
                ClosedAt = new DateTimeOffset(2026, 8, 1, 0, 0, 0, TimeSpan.Zero),
                ClosedById = seed.MemberUserId,
                SeasonId = seed.CurrentSeasonId,
                ClubId = seed.ClubId,
                CreatedById = seed.MemberUserId
            };
            var second = new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = "Later start",
                Status = CampaignStatus.Closed,
                StartDate = new DateOnly(2026, 3, 1),
                ClosedAt = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero),
                ClosedById = seed.MemberUserId,
                SeasonId = seed.CurrentSeasonId,
                ClubId = seed.ClubId,
                CreatedById = seed.MemberUserId
            };
            db.Campaigns.AddRange(first, second);
            await db.SaveChangesAsync(cancellationToken);
            expectedFirstId = first.CampaignId;
            expectedSecondId = second.CampaignId;
        }

        ActAs(seed.MemberUserId, seed.ClubId);
        var service = new CampaignQueryService(new PostgresReadContextFactory(fixture), fixture.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);
        var firstPage = await service.GetCampaignListAsync(new GetCampaignListInput { Limit = 1 }, cancellationToken);
        var secondPage = await service.GetCampaignListAsync(new GetCampaignListInput { Limit = 1, Page = 2 }, cancellationToken);

        firstPage.IsSuccess.ShouldBeTrue();
        secondPage.IsSuccess.ShouldBeTrue();
        firstPage.Value.TotalCount.ShouldBe(2);
        secondPage.Value.TotalCount.ShouldBe(2);
        firstPage.Value.Seasons.Single().Campaigns.Single().CampaignId.ShouldBe(expectedFirstId);
        secondPage.Value.Seasons.Single().Campaigns.Single().CampaignId.ShouldBe(expectedSecondId);
        secondPage.Value.Seasons.Single().Campaigns.Single().ClosedAt
            .ShouldBe(new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task GetCreationSetup_ReturnsOnlyCurrentSeason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(101, cancellationToken);
        ActAs(seed.MemberUserId, seed.ClubId, isAdmin: true);

        var service = new CampaignQueryService(
            new PostgresReadContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<CampaignQueryService>.Instance);

        var result = await service.GetCreationSetupAsync(cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CurrentSeason.ShouldNotBeNull();
        result.Value.CurrentSeason.SeasonId.ShouldBe(seed.CurrentSeasonId);
    }

    private async Task<Seed> SeedAsync(int seasonCount, CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        var club = new ClubEntity { CreationOperationId = Guid.NewGuid(), Name = $"Campaign Bound Club {suffix}", City = "Austin", State = "TX", CreatedById = actorUserId };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        var member = new NovaUserEntity { FirstName = "M", LastName = "Member", ClubId = club.ClubId };
        db.Users.Add(member);
        var baseDate = new DateOnly(2026, 1, 1);
        SeasonEntity? currentSeason = null;
        for (var i = 0; i < seasonCount; i++)
        {
            var season = new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Season {i:000}",
                StartDate = baseDate.AddDays(i),
                ClubId = club.ClubId,
                CreatedById = actorUserId
            };
            db.Seasons.Add(season);
            currentSeason = season;
        }

        await db.SaveChangesAsync(cancellationToken);
        club.CurrentSeasonId = currentSeason!.SeasonId;
        await db.SaveChangesAsync(cancellationToken);

        return new Seed(club.ClubId, member.Id, currentSeason.SeasonId);
    }

    private void ActAs(long? userId, long? clubId, bool isAdmin = false)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isAdmin;
    }

    private sealed record Seed(long ClubId, long MemberUserId, long CurrentSeasonId);
}
