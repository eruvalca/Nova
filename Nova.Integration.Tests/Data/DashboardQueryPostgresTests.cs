using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Dashboard;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Dashboard;
using NSubstitute;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Provider-sensitive evidence that durable activity persistence, cursor ordering, and structured
/// context translate on PostgreSQL.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class DashboardQueryPostgresTests(NovaAppHostFixture fixture)
{
    private static readonly DateTimeOffset Base = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies durable event rows translate on PostgreSQL and order deterministically.
    /// </summary>
    [Fact]
    public async Task GetActivity_Postgres_ReadsDurableEvents_AndOrdersByTimestampThenIdentity()
    {
        var seed = await SeedAsync();
        ActAs(seed.MemberUserId, seed.ClubId, isClubAdmin: false);

        await using (var db = fixture.CreateAdminContext())
        {
            db.ClubActivityEvents.AddRange(
                CampaignEvent(seed, ClubActivityEventKind.CampaignOpened, Base),
                CampaignEvent(seed, ClubActivityEventKind.CampaignClosed, Base.AddMinutes(1)),
                CampaignEvent(seed, ClubActivityEventKind.CampaignReopened, Base.AddMinutes(1)),
                CampaignEvent(seed, ClubActivityEventKind.CampaignDraftCreated, Base.AddMinutes(2)));
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();
        var result = await service.GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Count.ShouldBe(4);
        result.Value.Events.Select(item => item.Kind).ShouldBe(
        [
            DashboardActivityEventKind.CampaignDraftCreated,
            DashboardActivityEventKind.CampaignReopened,
            DashboardActivityEventKind.CampaignClosed,
            DashboardActivityEventKind.CampaignOpened
        ]);
    }

    /// <summary>
    /// Verifies the durable event timestamp and placement snapshots round-trip through PostgreSQL.
    /// </summary>
    [Fact]
    public async Task GetActivity_Postgres_PlacementEvent_RoundTripsStructuredContext()
    {
        var seed = await SeedAsync();
        ActAs(seed.MemberUserId, seed.ClubId, isClubAdmin: false);

        DateTimeOffset persistedAt;
        await using (var db = fixture.CreateAdminContext())
        {
            var activity = new ClubActivityEventEntity
            {
                ClubId = seed.ClubId,
                EventKind = ClubActivityEventKind.PlacementOutcomeChanged,
                Audience = ClubActivityAudience.AllMembers,
                ActorDisplayName = "M Member",
                CreatedById = seed.MemberUserId,
                CampaignId = seed.CampaignId,
                CampaignName = "Durable Campaign",
                PlayerId = seed.Player2Id,
                PlayerDisplayName = "P2 A",
                PlayerCampaignAssignmentId = seed.AssignmentId,
                PreviousPlacementOutcome = PlacementOutcome.Undecided,
                CurrentPlacementOutcome = PlacementOutcome.Withdrawn
            };
            db.ClubActivityEvents.Add(activity);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
            persistedAt = activity.CreatedAt;
        }

        var service = CreateService();
        var result = await service.GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var placement = result.Value.Events.Single(item => item.Kind == DashboardActivityEventKind.PlacementOutcomeChanged);
        Math.Abs((placement.EventAt - persistedAt).Ticks).ShouldBeLessThan(TimeSpan.FromMilliseconds(1).Ticks);
        var context = placement.Context.ShouldBeOfType<PlacementActivityContextDto>();
        context.Current.Outcome.ShouldBe(PlacementOutcome.Withdrawn);
        context.PlayerId.ShouldBe(seed.Player2Id);
    }

    private static ClubActivityEventEntity CampaignEvent(
        DashboardPostgresSeed seed,
        ClubActivityEventKind kind,
        DateTimeOffset createdAt)
        => new()
        {
            ClubId = seed.ClubId,
            EventKind = kind,
            Audience = ClubActivityAudience.AllMembers,
            ActorDisplayName = "M Member",
            CampaignId = seed.CampaignId,
            CampaignName = "Durable Campaign",
            CreatedById = seed.MemberUserId,
            CreatedAt = createdAt
        };

    /// <summary>Creates the dashboard query service over the live PostgreSQL read context.</summary>
    /// <returns>A service instance.</returns>
    private DashboardQueryService CreateService()
        => new(
            Substitute.For<ICampaignQueryService>(),
            Substitute.For<IClubJoinRequestService>(),
            new PostgresReadContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<DashboardQueryService>.Instance);

    /// <summary>Sets the simulated current user on the flow-local provider.</summary>
    /// <param name="userId">The simulated user identifier.</param>
    /// <param name="clubId">The simulated club identifier.</param>
    /// <param name="isClubAdmin">Whether the simulated user administers the club.</param>
    private void ActAs(long? userId, long? clubId, bool isClubAdmin)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    /// <summary>Seeds one club, member, season, campaign, players, assignment, and tag.</summary>
    /// <returns>The generated identifiers used to build activity rows.</returns>
    private async Task<DashboardPostgresSeed> SeedAsync()
    {
        ActAs(userId: null, clubId: null, isClubAdmin: false);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        var club = new ClubEntity { Name = $"Dashboard Club {suffix}", City = "Austin", State = "TX", CreatedById = actorUserId };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var member = new NovaUserEntity { FirstName = "M", LastName = "Member", ClubId = club.ClubId };
        db.Users.Add(member);
        var season = new SeasonEntity { Name = $"Dashboard Season {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = club.ClubId, CreatedById = actorUserId };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var campaign = new CampaignEntity { Name = $"Dashboard Campaign {suffix}", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = season.SeasonId, ClubId = club.ClubId, CreatedById = actorUserId };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var player = new PlayerEntity { FirstName = "P", LastName = "A", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = club.ClubId, CreatedById = actorUserId };
        var player2 = new PlayerEntity { FirstName = "P2", LastName = "A", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = club.ClubId, CreatedById = actorUserId };
        db.Players.AddRange(player, player2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tag = new PlayerTagEntity { Name = "Speed", Color = "#000000", ClubId = club.ClubId, CreatedById = actorUserId };
        db.PlayerTags.Add(tag);
        var assignment = new PlayerCampaignAssignmentEntity { PlayerId = player.PlayerId, CampaignId = campaign.CampaignId, ClubId = club.ClubId, CreatedById = actorUserId, PlacementOutcome = PlacementOutcome.Undecided };
        db.PlayerCampaignAssignments.Add(assignment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new DashboardPostgresSeed(club.ClubId, member.Id, campaign.CampaignId, assignment.PlayerCampaignAssignmentId, player2.PlayerId, tag.PlayerTagId);
    }

    /// <summary>Identifiers produced by the dashboard PostgreSQL seed.</summary>
    /// <param name="ClubId">The club identifier.</param>
    /// <param name="MemberUserId">The member user identifier.</param>
    /// <param name="CampaignId">The campaign identifier.</param>
    /// <param name="AssignmentId">The base assignment identifier.</param>
    /// <param name="Player2Id">The second player identifier for placement rows.</param>
    /// <param name="TagId">The tag definition identifier.</param>
    private sealed record DashboardPostgresSeed(
        long ClubId,
        long MemberUserId,
        long CampaignId,
        long AssignmentId,
        long Player2Id,
        long TagId);
}
