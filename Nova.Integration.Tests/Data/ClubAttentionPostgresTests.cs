using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Attention;
using Nova.Shared.Enums;
using Nova.Shared.Features.Attention;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Provider-sensitive evidence that the attention projection's needs-placement region translates on
/// PostgreSQL: the undecided-assignment filters push into SQL, the target (newest) campaign and its
/// scoped count are computed in one repeatable-read snapshot, and the region still reports
/// <see cref="AttentionRegionStatus.Loaded"/> with the target campaign's count when several
/// assignments span multiple campaigns.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class ClubAttentionPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies the needs-placement region scopes its count to the newest Active campaign: three
    /// undecided assignments across two Active campaigns resolve to the two on the newer campaign,
    /// which is named in deterministic order, all computed database-side under one snapshot
    /// transaction.
    /// </summary>
    [Fact]
    public async Task GetClubAttention_Postgres_CountsUndecidedAssignments_AndNamesNewestCampaign()
    {
        var seed = await SeedAsync();
        ActAs(seed.MemberUserId, seed.ClubId, isClubAdmin: true);

        var service = CreateService();
        var result = await service.GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var region = result.Value.NeedsPlacement;
        region.Status.ShouldBe(AttentionRegionStatus.Loaded);
        region.Count.ShouldBe(2);
        region.CampaignId.ShouldNotBeNull();
        region.CampaignName.ShouldBe(seed.NewestCampaignName);
    }

    /// <summary>
    /// Verifies the pending-join-requests region translates the count and oldest timestamp aggregate
    /// on PostgreSQL alongside the needs-placement region.
    /// </summary>
    [Fact]
    public async Task GetClubAttention_Postgres_CountsPendingJoinRequests_WithOldestTimestamp()
    {
        var seed = await SeedAsync();
        var pending = new[]
        {
            new DateTimeOffset(2026, 9, 1, 9, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 9, 4, 9, 0, 0, TimeSpan.Zero)
        };
        await SeedPendingJoinRequestsAsync(seed.ClubId, pending);
        ActAs(seed.MemberUserId, seed.ClubId, isClubAdmin: true);

        var service = CreateService();
        var result = await service.GetClubAttentionAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var region = result.Value.PendingJoinRequests;
        region.Status.ShouldBe(AttentionRegionStatus.Loaded);
        region.Count.ShouldBe(2);
        region.OldestRequestAt.ShouldBe(pending[0]);
    }

    /// <summary>Creates the attention query service over the live PostgreSQL read context.</summary>
    /// <returns>A service instance.</returns>
    private ClubAttentionQueryService CreateService()
        => new(
            new PostgresReadContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<ClubAttentionQueryService>.Instance);

    /// <summary>Sets the simulated current user on the flow-local provider.</summary>
    /// <param name="userId">The simulated user identifier.</param>
    /// <param name="clubId">The simulated club identifier.</param>
    /// <param name="isClubAdmin">Whether the simulated user administers the club.</param>
    private void ActAs(long userId, long clubId, bool isClubAdmin)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    /// <summary>
    /// Seeds one club, member, season, two Active campaigns, and three undecided assignments (one on
    /// the older campaign, two on the newer) plus one already-assigned player that must be excluded.
    /// </summary>
    /// <returns>The generated identifiers and the expected newest campaign name.</returns>
    private async Task<AttentionSeed> SeedAsync()
    {
        ActAs(0, 0, isClubAdmin: false);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        var club = new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Attention Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var member = new NovaUserEntity { FirstName = "A", LastName = "Member", ClubId = club.ClubId };
        db.Users.Add(member);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var season = new SeasonEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var olderCampaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Older Campaign {suffix}",
            StartDate = new DateOnly(2026, 5, 1),
            Status = CampaignStatus.Closed,
            ClosedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ClosedById = actorUserId,
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        var newerCampaign = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Newer Campaign {suffix}",
            StartDate = new DateOnly(2026, 6, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Campaigns.AddRange(olderCampaign, newerCampaign);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var players = Enumerable.Range(0, 4).Select(i => new PlayerEntity
        {
            CreationOperationId = Guid.NewGuid(),
            FirstName = $"P{i}",
            LastName = "Player",
            DateOfBirth = new DateOnly(2010, 1, 1),
            GraduationYear = 2028,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        }).ToArray();
        db.Players.AddRange(players);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var team = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Team {suffix}",
            GraduationYear = 2028,
            LifecycleStatus = LifecycleStatus.Active,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Teams.Add(team);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var assignments = new[]
        {
            new PlayerCampaignAssignmentEntity
            {
                PlayerId = players[0].PlayerId,
                CampaignId = olderCampaign.CampaignId,
                ClubId = club.ClubId,
                CreatedById = actorUserId,
                PlacementOutcome = PlacementOutcome.Undecided,
                TeamId = null
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerId = players[1].PlayerId,
                CampaignId = newerCampaign.CampaignId,
                ClubId = club.ClubId,
                CreatedById = actorUserId,
                PlacementOutcome = PlacementOutcome.Undecided,
                TeamId = null
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerId = players[2].PlayerId,
                CampaignId = newerCampaign.CampaignId,
                ClubId = club.ClubId,
                CreatedById = actorUserId,
                PlacementOutcome = PlacementOutcome.Undecided,
                TeamId = null
            },
            new PlayerCampaignAssignmentEntity
            {
                PlayerId = players[3].PlayerId,
                CampaignId = newerCampaign.CampaignId,
                ClubId = club.ClubId,
                CreatedById = actorUserId,
                PlacementOutcome = PlacementOutcome.Assigned,
                TeamId = team.TeamId
            }
        };
        db.PlayerCampaignAssignments.AddRange(assignments);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new AttentionSeed(club.ClubId, member.Id, newerCampaign.Name);
    }

    /// <summary>
    /// Seeds pending join requests for a club with two club-less requesters, re-stamping the
    /// deterministic submit timestamps after the audit interceptor's uniform stamp. The requests are
    /// saved in two passes because re-stamping exists in the modifier state, which cannot be applied
    /// while the entities transition to Added on the first save.
    /// </summary>
    /// <param name="clubId">The club identifier.</param>
    /// <param name="createdAts">The submit timestamps for the pending requests, in order.</param>
    private async Task SeedPendingJoinRequestsAsync(long clubId, IReadOnlyList<DateTimeOffset> createdAts)
    {
        await using var db = fixture.CreateAdminContext();
        var requests = new List<ClubJoinRequestEntity>(createdAts.Count);
        for (var index = 0; index < createdAts.Count; index++)
        {
            var requester = new NovaUserEntity { FirstName = $"R{index}", LastName = "Requester", ClubId = null };
            db.Users.Add(requester);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            var request = new ClubJoinRequestEntity
            {
                ClubId = clubId,
                RequestingUserId = requester.Id,
                Status = RequestStatus.Pending,
                CreatedById = requester.Id
            };
            db.ClubJoinRequests.Add(request);
            requests.Add(request);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        for (var index = 0; index < requests.Count; index++)
        {
            requests[index].CreatedAt = createdAts[index];
        }

        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>Identifiers produced by the attention PostgreSQL seed.</summary>
    /// <param name="ClubId">The club identifier.</param>
    /// <param name="MemberUserId">The member user identifier.</param>
    /// <param name="NewestCampaignName">The expected newest campaign name.</param>
    private sealed record AttentionSeed(long ClubId, long MemberUserId, string NewestCampaignName);
}
