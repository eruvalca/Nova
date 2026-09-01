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
/// Provider-sensitive evidence that the four dashboard activity source queries translate on
/// PostgreSQL, that <c>timestamptz</c> ordering matches the in-memory policy, and that placement
/// <c>ModifiedAt</c> round-trips through <c>timestamptz</c>.
/// </summary>
/// <param name="fixture">The Aspire-hosted Nova application fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class DashboardQueryPostgresTests(NovaAppHostFixture fixture)
{
    private static readonly DateTimeOffset Base = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies all four source queries translate on PostgreSQL and merge in the deterministic
    /// policy order.
    /// </summary>
    [Fact]
    public async Task GetActivity_Postgres_TranslatesAllFourSources_AndOrdersByPolicy()
    {
        var seed = await SeedAsync();
        ActAs(seed.MemberUserId, seed.ClubId, isClubAdmin: false);

        await using (var db = fixture.CreateAdminContext())
        {
            var note = new NoteEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Content = "Note",
                PlayerCampaignAssignmentId = seed.AssignmentId,
                ClubId = seed.ClubId,
                CreatedById = seed.MemberUserId
            };
            var application = new CampaignTagApplicationEntity
            {
                CreationOperationId = Guid.NewGuid(),
                PlayerCampaignAssignmentId = seed.AssignmentId,
                PlayerTagId = seed.TagId,
                ClubId = seed.ClubId,
                CreatedById = seed.MemberUserId
            };
            var placement = new PlayerCampaignAssignmentEntity
            {
                PlayerId = seed.Player2Id,
                CampaignId = seed.CampaignId,
                ClubId = seed.ClubId,
                CreatedById = seed.MemberUserId,
                PlacementOutcome = PlacementOutcome.NotSelected,
                ModifiedAt = Base.AddMinutes(2),
                ModifiedById = seed.MemberUserId
            };
            var lifecycle = new CampaignLifecycleEventEntity
            {
                CampaignId = seed.CampaignId,
                ClubId = seed.ClubId,
                EventType = CampaignLifecycleEventType.Closed,
                CreatedById = seed.MemberUserId
            };
            db.AddRange(note, application, placement, lifecycle);
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);

            note.CreatedAt = Base.AddMinutes(0);
            application.CreatedAt = Base.AddMinutes(1);
            lifecycle.CreatedAt = Base.AddMinutes(3);
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
            DashboardActivityEventKind.CampaignClosed,
            DashboardActivityEventKind.PlacementSet,
            DashboardActivityEventKind.TagApplied,
            DashboardActivityEventKind.NoteAdded
        ]);
    }

    /// <summary>
    /// Verifies the placement event time equals the assignment's <c>ModifiedAt</c> with offset and
    /// sub-second precision preserved by <c>timestamptz</c>.
    /// </summary>
    [Fact]
    public async Task GetActivity_Postgres_PlacementModifiedAt_RoundTrips()
    {
        var seed = await SeedAsync();
        ActAs(seed.MemberUserId, seed.ClubId, isClubAdmin: false);

        var modifiedAt = new DateTimeOffset(2026, 10, 2, 9, 30, 0, 500, TimeSpan.Zero);
        await using (var db = fixture.CreateAdminContext())
        {
            db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
            {
                PlayerId = seed.Player2Id,
                CampaignId = seed.CampaignId,
                ClubId = seed.ClubId,
                CreatedById = seed.MemberUserId,
                PlacementOutcome = PlacementOutcome.Withdrawn,
                ModifiedAt = modifiedAt,
                ModifiedById = seed.MemberUserId
            });
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();
        var result = await service.GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var placement = result.Value.Events.Single(item => item.Kind == DashboardActivityEventKind.PlacementSet);
        placement.EventAt.ShouldBe(modifiedAt);
        placement.PlacementOutcome.ShouldBe(PlacementOutcome.Withdrawn);
        placement.ActorUserId.ShouldBe(seed.MemberUserId);
    }

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

        var club = new ClubEntity { CreationOperationId = Guid.NewGuid(), Name = $"Dashboard Club {suffix}", City = "Austin", State = "TX", CreatedById = actorUserId };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var member = new NovaUserEntity { FirstName = "M", LastName = "Member", ClubId = club.ClubId };
        db.Users.Add(member);
        var season = new SeasonEntity { CreationOperationId = Guid.NewGuid(), Name = $"Dashboard Season {suffix}", StartDate = new DateOnly(2026, 1, 1), ClubId = club.ClubId, CreatedById = actorUserId };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var campaign = new CampaignEntity { CreationOperationId = Guid.NewGuid(), Name = $"Dashboard Campaign {suffix}", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = season.SeasonId, ClubId = club.ClubId, CreatedById = actorUserId };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var player = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "P", LastName = "A", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = club.ClubId, CreatedById = actorUserId };
        var player2 = new PlayerEntity { CreationOperationId = Guid.NewGuid(), FirstName = "P2", LastName = "A", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = club.ClubId, CreatedById = actorUserId };
        db.Players.AddRange(player, player2);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var tag = new PlayerTagEntity { CreationOperationId = Guid.NewGuid(), Name = "Speed", NormalizedName = "SPEED", Color = "#000000", ClubId = club.ClubId, CreatedById = actorUserId };
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
