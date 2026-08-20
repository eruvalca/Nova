using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies PostgreSQL treats <c>%</c>, <c>_</c>, and <c>\</c> in campaign-participant searches as
/// literals rather than <c>ILIKE</c> wildcards, and that plain substring matching still works.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignParticipantSearchEscapingPostgresTests(NovaAppHostFixture fixture)
{
    [Fact]
    public async Task GetParticipantRoster_Search_TreatsLikeMetacharactersAsLiterals()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(cancellationToken);
        ActAs(seed.MemberUserId, seed.ClubId);

        var service = new CampaignParticipantQueryService(
            new PostgresReadContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<CampaignParticipantQueryService>.Instance);

        var percent = await service.GetParticipantRosterAsync(
            new GetCampaignParticipantRosterInput { CampaignId = seed.CampaignId, Search = "50%" },
            cancellationToken);
        percent.Value.Items.Select(participant => participant.DisplayName).ShouldBe(["Fifty 50% Wins"]);

        var underscore = await service.GetParticipantRosterAsync(
            new GetCampaignParticipantRosterInput { CampaignId = seed.CampaignId, Search = "a_b" },
            cancellationToken);
        underscore.Value.Items.Select(participant => participant.DisplayName).ShouldBe(["Player a_b Squad"]);

        var backslash = await service.GetParticipantRosterAsync(
            new GetCampaignParticipantRosterInput { CampaignId = seed.CampaignId, Search = @"Path\T" },
            cancellationToken);
        backslash.Value.Items.Select(participant => participant.DisplayName).ShouldBe(["Player Path\\Team"]);

        var plain = await service.GetParticipantRosterAsync(
            new GetCampaignParticipantRosterInput { CampaignId = seed.CampaignId, Search = "Squad" },
            cancellationToken);
        plain.Value.Items.Select(participant => participant.DisplayName).ShouldBe(
            ["Player a_b Squad", "Player axb Squad"],
            ignoreOrder: true);
    }

    private async Task<Seed> SeedAsync(CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        var club = new ClubEntity { Name = $"Participant Escaping Club {suffix}", City = "Austin", State = "TX", CreatedById = actorUserId };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        var member = new NovaUserEntity { FirstName = "M", LastName = "Member", ClubId = club.ClubId };
        db.Users.Add(member);
        var season = new SeasonEntity { Name = $"Escaping Season {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = club.ClubId, CreatedById = actorUserId };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(cancellationToken);

        var campaign = new CampaignEntity
        {
            Name = $"Escaping Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(cancellationToken);

        var players = new[]
        {
            NewPlayer("Fifty", "50% Wins", club.ClubId, actorUserId),
            NewPlayer("Fifty", "50 Losses", club.ClubId, actorUserId),
            NewPlayer("Player", "a_b Squad", club.ClubId, actorUserId),
            NewPlayer("Player", "axb Squad", club.ClubId, actorUserId),
            NewPlayer("Player", @"Path\Team", club.ClubId, actorUserId),
            NewPlayer("Player", "PathTeam", club.ClubId, actorUserId)
        };
        foreach (var player in players)
        {
            db.Players.Add(player);
            await db.SaveChangesAsync(cancellationToken);
            db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
            {
                PlayerId = player.PlayerId,
                CampaignId = campaign.CampaignId,
                PlacementOutcome = PlacementOutcome.Undecided,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return new Seed(club.ClubId, member.Id, campaign.CampaignId);
    }

    private void ActAs(long? userId, long? clubId)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = false;
    }

    private static PlayerEntity NewPlayer(string firstName, string lastName, long clubId, long createdById) => new()
    {
        FirstName = firstName,
        LastName = lastName,
        DateOfBirth = new DateOnly(2011, 1, 1),
        GraduationYear = 2029,
        LifecycleStatus = LifecycleStatus.Active,
        ClubId = clubId,
        CreatedById = createdById
    };

    private sealed record Seed(long ClubId, long MemberUserId, long CampaignId);
}
