using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
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
    [Fact]
    public async Task GetCreationSetup_ReturnsOnlyCurrentSeason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(101, cancellationToken);
        ActAs(seed.MemberUserId, seed.ClubId);

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

    private void ActAs(long? userId, long? clubId)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = false;
    }

    private sealed record Seed(long ClubId, long MemberUserId, long CurrentSeasonId);
}
