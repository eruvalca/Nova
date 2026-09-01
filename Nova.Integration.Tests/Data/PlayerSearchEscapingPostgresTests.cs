using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies PostgreSQL treats <c>%</c>, <c>_</c>, and <c>\</c> in player-roster searches as literals
/// rather than <c>ILIKE</c> wildcards, while preserving the tryout-number search disjunction.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class PlayerSearchEscapingPostgresTests(NovaAppHostFixture fixture)
{
    [Fact]
    public async Task GetPlayerRoster_Search_TreatsLikeMetacharactersAsLiterals()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(cancellationToken);
        ActAs(seed.MemberUserId, seed.ClubId);

        var service = new PlayerService(
            new PostgresReadContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<PlayerService>.Instance);

        var percent = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = seed.ClubId, Search = "50%" },
            cancellationToken);
        percent.Value.Items.Select(player => player.DisplayName).ShouldBe(["Fifty 50% Wins"]);

        var underscore = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = seed.ClubId, Search = "a_b" },
            cancellationToken);
        underscore.Value.Items.Select(player => player.DisplayName).ShouldBe(["Player a_b Squad"]);

        var backslash = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = seed.ClubId, Search = @"Path\T" },
            cancellationToken);
        backslash.Value.Items.Select(player => player.DisplayName).ShouldBe(["Player Path\\Team"]);

        var plain = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = seed.ClubId, Search = "Squad" },
            cancellationToken);
        plain.Value.Items.Select(player => player.DisplayName).ShouldBe(
            ["Player a_b Squad", "Player axb Squad"],
            ignoreOrder: true);
    }

    private async Task<Seed> SeedAsync(CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        var club = new ClubEntity { CreationOperationId = Guid.NewGuid(), Name = $"Player Escaping Club {suffix}", City = "Austin", State = "TX", CreatedById = actorUserId };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        var member = new NovaUserEntity { FirstName = "M", LastName = "Member", ClubId = club.ClubId };
        db.Users.Add(member);
        db.Players.AddRange(
            NewPlayer("Fifty", "50% Wins", club.ClubId, actorUserId),
            NewPlayer("Fifty", "50 Losses", club.ClubId, actorUserId),
            NewPlayer("Player", "a_b Squad", club.ClubId, actorUserId),
            NewPlayer("Player", "axb Squad", club.ClubId, actorUserId),
            NewPlayer("Player", @"Path\Team", club.ClubId, actorUserId),
            NewPlayer("Player", "PathTeam", club.ClubId, actorUserId));
        await db.SaveChangesAsync(cancellationToken);

        return new Seed(club.ClubId, member.Id);
    }

    private void ActAs(long? userId, long? clubId)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = false;
    }

    private static PlayerEntity NewPlayer(string firstName, string lastName, long clubId, long createdById) => new()
    {
        CreationOperationId = Guid.NewGuid(),
        FirstName = firstName,
        LastName = lastName,
        DateOfBirth = new DateOnly(2011, 1, 1),
        GraduationYear = 2029,
        LifecycleStatus = LifecycleStatus.Active,
        ClubId = clubId,
        CreatedById = createdById
    };

    private sealed record Seed(long ClubId, long MemberUserId);
}
