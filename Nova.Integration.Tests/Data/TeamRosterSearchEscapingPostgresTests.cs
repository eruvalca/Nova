using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Teams;
using Nova.Shared.Features.Teams;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies PostgreSQL treats <c>%</c>, <c>_</c>, and <c>\</c> in team-roster searches as literals
/// rather than <c>ILIKE</c> wildcards, and that plain substring matching still works.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TeamRosterSearchEscapingPostgresTests(NovaAppHostFixture fixture)
{
    [Fact]
    public async Task GetRoster_Search_TreatsLikeMetacharactersAsLiterals()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(cancellationToken);
        ActAs(seed.MemberUserId, seed.ClubId);

        var service = new TeamRosterQueryService(
            new PostgresReadContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<TeamRosterQueryService>.Instance);

        var percent = await service.GetRosterAsync(new GetTeamRosterInput { Search = "50%" }, cancellationToken);
        percent.Value.Select(team => team.Name).ShouldBe(["50% Wins"]);

        var underscore = await service.GetRosterAsync(new GetTeamRosterInput { Search = "a_b" }, cancellationToken);
        underscore.Value.Select(team => team.Name).ShouldBe(["a_b Squad"]);

        var backslash = await service.GetRosterAsync(new GetTeamRosterInput { Search = @"Path\T" }, cancellationToken);
        backslash.Value.Select(team => team.Name).ShouldBe([@"Path\Team"]);

        var plain = await service.GetRosterAsync(new GetTeamRosterInput { Search = "Squad" }, cancellationToken);
        plain.Value.Select(team => team.Name).ShouldBe(["a_b Squad", "axb Squad"], ignoreOrder: true);
    }

    private async Task<Seed> SeedAsync(CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        var club = new ClubEntity { Name = $"Team Escaping Club {suffix}", City = "Austin", State = "TX", CreatedById = actorUserId };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        var member = new NovaUserEntity { FirstName = "M", LastName = "Member", ClubId = club.ClubId };
        db.Users.Add(member);
        db.Teams.AddRange(
            NewTeam("50% Wins", club.ClubId, actorUserId),
            NewTeam("50 Losses", club.ClubId, actorUserId),
            NewTeam("a_b Squad", club.ClubId, actorUserId),
            NewTeam("axb Squad", club.ClubId, actorUserId),
            NewTeam(@"Path\Team", club.ClubId, actorUserId),
            NewTeam("PathTeam", club.ClubId, actorUserId));
        await db.SaveChangesAsync(cancellationToken);

        return new Seed(club.ClubId, member.Id);
    }

    private void ActAs(long? userId, long? clubId)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = false;
    }

    private static TeamEntity NewTeam(string name, long clubId, long createdById) => new()
    {
        Name = name,
        GraduationYear = 2030,
        ClubId = clubId,
        CreatedById = createdById
    };

    private sealed record Seed(long ClubId, long MemberUserId);
}
