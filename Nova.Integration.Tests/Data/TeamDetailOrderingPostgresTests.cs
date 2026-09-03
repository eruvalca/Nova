using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Teams;
using Nova.Shared.Enums;
using Nova.Shared.Features.Teams;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies PostgreSQL bounds team placement history to <see cref="TeamDetailDto.MaxPlacementHistoryItems"/>,
/// reports truncation, and orders campaign placements by lifecycle rank.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TeamDetailOrderingPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies PostgreSQL applies Active, Draft, then Closed lifecycle ordering before bounding
    /// the placement-history response.
    /// </summary>
    [Fact]
    public async Task GetTeamDetail_BoundsPlacementHistory_AndOrdersActiveDraftClosed()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(cancellationToken);
        ActAs(seed.MemberUserId, seed.ClubId, isClubAdmin: true);

        var service = new TeamDetailQueryService(
            new PostgresReadContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<TeamDetailQueryService>.Instance);

        var result = await service.GetTeamDetailAsync(seed.TeamId, cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PlacementHistory.Count.ShouldBe(TeamDetailDto.MaxPlacementHistoryItems);
        result.Value.IsPlacementHistoryTruncated.ShouldBeTrue();
        result.Value.PlacementHistoryTotalCount.ShouldBe(TeamDetailDto.MaxPlacementHistoryItems + 2);
        result.Value.ActivePlacementImpacts.Count.ShouldBe(1);
        result.Value.PlacementHistory[0].CampaignStatus.ShouldBe(CampaignStatus.Active);
        result.Value.PlacementHistory[1].CampaignStatus.ShouldBe(CampaignStatus.Draft);
        result.Value.PlacementHistory.Skip(2).ShouldAllBe(
            placement => placement.CampaignStatus == CampaignStatus.Closed);
    }

    /// <summary>
    /// Seeds countervailing campaign dates so the PostgreSQL lifecycle rank is observable.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cooperative cancellation.</param>
    /// <returns>The identifiers required to execute the tenant-scoped query.</returns>
    private async Task<Seed> SeedAsync(CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        var club = new ClubEntity { CreationOperationId = Guid.NewGuid(), Name = $"Team Detail Bound Club {suffix}", City = "Austin", State = "TX", CreatedById = actorUserId };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        var member = new NovaUserEntity { FirstName = "M", LastName = "Member", ClubId = club.ClubId };
        db.Users.Add(member);
        var team = new TeamEntity { CreationOperationId = Guid.NewGuid(), Name = $"Bound Team {suffix}", GraduationYear = 2029, ClubId = club.ClubId, CreatedById = actorUserId };
        db.Teams.Add(team);
        await db.SaveChangesAsync(cancellationToken);

        var activeSeason = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = $"Active Season {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = club.ClubId, CreatedById = actorUserId };
        db.Seasons.Add(activeSeason);
        await db.SaveChangesAsync(cancellationToken);

        var activeCampaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Active Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = activeSeason.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Campaigns.Add(activeCampaign);
        await db.SaveChangesAsync(cancellationToken);

        var activePlayer = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Active",
            LastName = "Player",
            DateOfBirth = new DateOnly(2011, 1, 1),
            GraduationYear = 2029,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Players.Add(activePlayer);
        await db.SaveChangesAsync(cancellationToken);

        db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
        {
            PlayerId = activePlayer.PlayerId,
            CampaignId = activeCampaign.CampaignId,
            TeamId = team.TeamId,
            PlacementOutcome = PlacementOutcome.Assigned,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        });

        var draftCampaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Draft Campaign {suffix}",
            StartDate = new DateOnly(2024, 6, 1),
            Status = CampaignStatus.Draft,
            SeasonId = activeSeason.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var draftPlayer = new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = "Draft",
            LastName = "Player",
            DateOfBirth = new DateOnly(2011, 6, 1),
            GraduationYear = 2029,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Campaigns.Add(draftCampaign);
        db.Players.Add(draftPlayer);
        await db.SaveChangesAsync(cancellationToken);

        db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
        {
            PlayerId = draftPlayer.PlayerId,
            CampaignId = draftCampaign.CampaignId,
            TeamId = team.TeamId,
            PlacementOutcome = PlacementOutcome.Assigned,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        });

        for (var i = 0; i < TeamDetailDto.MaxPlacementHistoryItems; i++)
        {
            var season = new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Historical Season {suffix} {i:000}",
                StartDate = new DateOnly(2025, 1, 1),
                ClubId = club.ClubId,
                CreatedById = actorUserId
            };
            db.Seasons.Add(season);
            await db.SaveChangesAsync(cancellationToken);

            var campaign = new CampaignEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = $"Historical Campaign {suffix} {i:000}",
                StartDate = new DateOnly(2025, 2, 1).AddDays(i),
                Status = CampaignStatus.Closed,
                SeasonId = season.SeasonId,
                ClubId = club.ClubId,
                CreatedById = actorUserId,
                ClosedAt = DateTimeOffset.UtcNow,
                ClosedById = actorUserId
            };
            db.Campaigns.Add(campaign);
            await db.SaveChangesAsync(cancellationToken);

            var player = new PlayerEntity
            {
                CreationOperationId = Guid.NewGuid(),
                FirstName = "Historical",
                LastName = $"Player {i:000}",
                DateOfBirth = new DateOnly(2010, 1, 1),
                GraduationYear = 2028,
                LifecycleStatus = LifecycleStatus.Active,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            };
            db.Players.Add(player);
            await db.SaveChangesAsync(cancellationToken);

            db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
            {
                PlayerId = player.PlayerId,
                CampaignId = campaign.CampaignId,
                TeamId = team.TeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            });
        }

        await db.SaveChangesAsync(cancellationToken);

        return new Seed(club.ClubId, member.Id, team.TeamId);
    }

    /// <summary>
    /// Sets the simulated tenant identity for the current asynchronous flow.
    /// </summary>
    /// <param name="userId">The simulated user identifier.</param>
    /// <param name="clubId">The simulated club identifier.</param>
    /// <param name="isClubAdmin">Whether the simulated member has club-administrator visibility.</param>
    private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    private sealed record Seed(long ClubId, long MemberUserId, long TeamId);
}
