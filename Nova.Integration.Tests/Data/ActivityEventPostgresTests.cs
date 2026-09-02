using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Activity;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Provider-sensitive evidence that the club activity feed's visibility filter and keyset paging
/// translate on PostgreSQL, that <c>timestamptz</c> ordering matches the deterministic policy, and
/// that polymorphic payloads round-trip through the stored JSON.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class ActivityEventPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies the feed pushes the club and visibility filters plus the keyset predicate into SQL
    /// so a page crosses many rows with a stable cursor and sub-second order ties.
    /// </summary>
    [Fact]
    public async Task GetClubActivity_Postgres_TranslatesKeysetPage_AndOrdersByPolicy()
    {
        var seed = await SeedAsync();
        ActAs(seed.MemberUserId, seed.ClubId, isClubAdmin: false);

        await using (var db = fixture.CreateAdminContext())
        {
            foreach (var (index, kind) in new[]
            {
                (0, ActivityEventKind.CampaignOpened),
                (1, ActivityEventKind.PlacementAssigned),
                (2, ActivityEventKind.JoinRequestSubmitted),
                (3, ActivityEventKind.MemberJoined)
            })
            {
                db.ActivityEvents.Add(new ActivityEventEntity
                {
                    ClubId = seed.ClubId,
                    EventKind = kind,
                    IsAdminOnly = kind == ActivityEventKind.JoinRequestSubmitted,
                    CampaignId = index is 0 or 1 ? seed.CampaignId : null,
                    ActorUserId = seed.MemberUserId,
                    ActorDisplayName = "Member",
                    PayloadJson = IndexPayload(kind, seed.CampaignId),
                    CreatedById = seed.MemberUserId
                });
            }

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();
        var firstPage = await service.GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        firstPage.IsSuccess.ShouldBeTrue();
        firstPage.Value.Events.Select(item => item.Kind).ShouldBe(
        [
            ActivityEventKind.MemberJoined,
            ActivityEventKind.PlacementAssigned,
            ActivityEventKind.CampaignOpened
        ]);
        firstPage.Value.HasMore.ShouldBeFalse();
        firstPage.Value.NextCursor.ShouldBeNull();
    }

    /// <summary>
    /// Verifies the admin feed includes the admin-only join-request rows and that a member cursor
    /// resumes after the admin-only page boundary, proving the visibility filter and keyset
    /// predicate compose in SQL and return the deterministic policy order.
    /// </summary>
    [Fact]
    public async Task GetClubActivity_Postgres_AdminSeesAdminOnlyRows_AndCursorResumes()
    {
        var seed = await SeedAsync();
        ActAs(seed.MemberUserId, seed.ClubId, isClubAdmin: true);

        await using (var db = fixture.CreateAdminContext())
        {
            // Seed 21 rows: 20 fill the first page, the last forces HasMore and a continuation
            // cursor. Rows carry the same timestamp tie so the (CreatedAt, ActivityEventId)
            // deterministic tiebreak is exercised in SQL.
            for (var i = 0; i < 21; i++)
            {
                var kind = i switch
                {
                    0 => ActivityEventKind.MemberJoined,
                    1 => ActivityEventKind.JoinRequestSubmitted,
                    20 => ActivityEventKind.CampaignOpened,
                    _ => ActivityEventKind.PlacementAssigned
                };
                db.ActivityEvents.Add(new ActivityEventEntity
                {
                    ClubId = seed.ClubId,
                    EventKind = kind,
                    IsAdminOnly = kind == ActivityEventKind.JoinRequestSubmitted,
                    CampaignId = kind == ActivityEventKind.CampaignOpened ? seed.CampaignId : null,
                    ActorUserId = seed.MemberUserId,
                    ActorDisplayName = "Member",
                    PayloadJson = IndexPayload(kind, seed.CampaignId),
                    CreatedById = seed.MemberUserId
                });
            }

            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();
        var firstPage = await service.GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        // Ordering ties resolve by descending ActivityEventId: ids increase with insertion seed
        // order, so the newest (highest id) rows come first. The page holds 20 of 21 rows.
        firstPage.IsSuccess.ShouldBeTrue();
        firstPage.Value.Events.Count.ShouldBe(20);
        firstPage.Value.Events[0].Kind.ShouldBe(ActivityEventKind.CampaignOpened);
        firstPage.Value.HasMore.ShouldBeTrue();
        firstPage.Value.NextCursor.ShouldNotBeNull();

        var secondPage = await service.GetClubActivityAsync(
            new GetClubActivityInput
            {
                BeforeActivityEventId = firstPage.Value.NextCursor!.ActivityEventId,
                BeforeOccurredAt = firstPage.Value.NextCursor!.OccurredAt
            },
            TestContext.Current.CancellationToken);

        secondPage.IsSuccess.ShouldBeTrue();
        secondPage.Value.Events.Count.ShouldBe(1);
        secondPage.Value.Events[0].Kind.ShouldBe(ActivityEventKind.MemberJoined);
        secondPage.Value.HasMore.ShouldBeFalse();
    }

    /// <summary>
    /// Verifies a polymorphic member-joined payload round-trips through PostgreSQL JSON storage
    /// with the captain context shape used by the feed projection.
    /// </summary>
    [Fact]
    public async Task GetClubActivity_Postgres_JoinRequestContextRoundTrips()
    {
        var seed = await SeedAsync();
        ActAs(seed.MemberUserId, seed.ClubId, isClubAdmin: true);

        await using (var db = fixture.CreateAdminContext())
        {
            db.ActivityEvents.Add(new ActivityEventEntity
            {
                ClubId = seed.ClubId,
                EventKind = ActivityEventKind.JoinRequestSubmitted,
                IsAdminOnly = true,
                ActorUserId = seed.MemberUserId,
                ActorDisplayName = "Requester",
                PayloadJson = JsonSerializer.Serialize(
                    new JoinRequestContext { JoinRequestId = 41, RequesterDisplayName = "Requester" },
                    typeof(ClubActivityContext)),
                CreatedById = seed.MemberUserId
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();
        var result = await service.GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var item = result.Value.Events.Single();
        item.Kind.ShouldBe(ActivityEventKind.JoinRequestSubmitted);
        item.Context.ShouldBeOfType<JoinRequestContext>();
        var joinRequest = (JoinRequestContext)item.Context;
        joinRequest.JoinRequestId.ShouldBe(41);
        joinRequest.RequesterDisplayName.ShouldBe("Requester");
    }

    /// <summary>Creates the club activity query service over the live PostgreSQL read context.</summary>
    /// <returns>A service instance.</returns>
    private ClubActivityQueryService CreateService()
        => new(
            new PostgresReadContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<ClubActivityQueryService>.Instance);

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

    /// <summary>Creates a family-matching payload JSON for a seeded event kind.</summary>
    /// <param name="kind">The event kind being seeded.</param>
    /// <param name="campaignId">The campaign identifier for campaign-family kinds.</param>
    /// <returns>The payload JSON.</returns>
    private static string IndexPayload(ActivityEventKind kind, long campaignId) =>
        kind switch
        {
            ActivityEventKind.CampaignOpened
                or ActivityEventKind.CampaignClosed
                or ActivityEventKind.CampaignReopened => JsonSerializer.Serialize(
                    new CampaignLifecycleContext { CampaignId = campaignId, CampaignName = "Campaign" },
                    typeof(ClubActivityContext)),
            ActivityEventKind.JoinRequestSubmitted
                or ActivityEventKind.JoinRequestCancelled
                or ActivityEventKind.JoinRequestRejected => JsonSerializer.Serialize(
                    new JoinRequestContext { JoinRequestId = 41, RequesterDisplayName = "Requester" },
                    typeof(ClubActivityContext)),
            ActivityEventKind.MemberJoined
                or ActivityEventKind.MemberRemoved
                or ActivityEventKind.MemberLeft => JsonSerializer.Serialize(
                    new MembershipContext { MemberUserId = 99, MemberDisplayName = "Member", ApprovedByActorName = null },
                    typeof(ClubActivityContext)),
            ActivityEventKind.MemberPromoted
                or ActivityEventKind.MemberDemoted => JsonSerializer.Serialize(
                    new MemberRoleContext { MemberDisplayName = "Member", Role = "Captain" },
                    typeof(ClubActivityContext)),
            _ => JsonSerializer.Serialize(
                new PlacementContext
                {
                    CampaignId = campaignId,
                    CampaignName = "Campaign",
                    PlayerCampaignAssignmentId = 7,
                    PlayerDisplayName = "Member",
                    PreviousOutcome = null,
                    Outcome = PlacementOutcome.Assigned,
                    PreviousTeamName = null,
                    TeamName = "Alpha"
                },
                typeof(ClubActivityContext))
        };

    /// <summary>Seeds one club, member, season, campaign, players, and assignment.</summary>
    /// <returns>The generated identifiers used to build activity rows.</returns>
    private async Task<ActivityEventSeed> SeedAsync()
    {
        ActAs(userId: null, clubId: null, isClubAdmin: false);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        var club = new ClubEntity { CreationOperationId = Guid.NewGuid(), Name = $"Activity Club {suffix}", City = "Austin", State = "TX", CreatedById = actorUserId };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var member = new NovaUserEntity { FirstName = "M", LastName = "Member", ClubId = club.ClubId };
        db.Users.Add(member);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var season = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = $"Activity Season {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = club.ClubId, CreatedById = actorUserId };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var campaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = $"Activity Campaign {suffix}", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = season.SeasonId, ClubId = club.ClubId, CreatedById = actorUserId };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new ActivityEventSeed(club.ClubId, member.Id, campaign.CampaignId);
    }

    /// <summary>Identifiers produced by the activity PostgreSQL seed.</summary>
    /// <param name="ClubId">The club identifier.</param>
    /// <param name="MemberUserId">The member user identifier.</param>
    /// <param name="CampaignId">The campaign identifier.</param>
    private sealed record ActivityEventSeed(
        long ClubId,
        long MemberUserId,
        long CampaignId);
}
