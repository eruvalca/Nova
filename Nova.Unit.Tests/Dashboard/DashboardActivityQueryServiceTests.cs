using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Features.Dashboard;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Dashboard;

/// <summary>
/// Verifies the bounded, deterministically ordered club dashboard activity query: all four event
/// kinds with context, removed-tag exclusion, placement event time, cross-source ordering, limit,
/// tenant isolation, and "Former member" fallback.
/// </summary>
public sealed class DashboardActivityQueryServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAMemberId = 300;
    private const long ClubAAdminId = 301;
    private const long ClubBMemberId = 400;
    private const long MissingActorUserId = 999_999;

    private static readonly DateTimeOffset Base = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly TenancyTestHarness _harness = new();
    private long _campaignAId;
    private long _campaignBId;
    private long _playerAId;
    private long _playerA2Id;
    private long _assignmentAId;
    private long _assignmentBId;
    private long _tagAId;

    /// <summary>Initializes a test instance with two clubs and base campaign/player data.</summary>
    public DashboardActivityQueryServiceTests() => SeedBase();

    /// <summary>Releases the tenancy harness.</summary>
    public void Dispose() => _harness.Dispose();

    /// <summary>Verifies the four event kinds are returned with their kind-specific context.</summary>
    [Fact]
    public async Task GetActivity_ReturnsAllFourKinds_WithCorrectContext()
    {
        var noteId = SeedNote(_assignmentAId, ClubAId, ClubAAdminId, Base.AddMinutes(1));
        var tagApplicationId = SeedTagApplication(_assignmentAId, ClubAId, _tagAId, ClubAAdminId, Base.AddMinutes(2));
        var placementAssignmentId = SeedPlacementAssignment(_playerA2Id, _campaignAId, ClubAId, PlacementOutcome.NotSelected, ClubAAdminId, Base.AddMinutes(3));
        var lifecycleEventId = SeedLifecycleEvent(_campaignAId, ClubAId, CampaignLifecycleEventType.Closed, ClubAAdminId, Base.AddMinutes(4));

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var events = result.Value.Events;
        events.Count.ShouldBe(4);

        var note = events.Single(item => item.Kind == DashboardActivityEventKind.NoteAdded);
        note.EventId.ShouldBe(noteId);
        note.ActorUserId.ShouldBe(ClubAAdminId);
        note.CampaignId.ShouldBe(_campaignAId);
        note.CampaignName.ShouldBe("Active A");
        note.PlayerCampaignAssignmentId.ShouldBe(_assignmentAId);
        note.PlayerDisplayName.ShouldBe("P A");
        note.TagName.ShouldBeNull();
        note.PlacementOutcome.ShouldBeNull();
        note.LifecycleEventType.ShouldBeNull();

        var tag = events.Single(item => item.Kind == DashboardActivityEventKind.TagApplied);
        tag.EventId.ShouldBe(tagApplicationId);
        tag.PlayerDisplayName.ShouldBe("P A");
        tag.TagName.ShouldBe("Speed");

        var placement = events.Single(item => item.Kind == DashboardActivityEventKind.PlacementSet);
        placement.EventId.ShouldBe(placementAssignmentId);
        placement.PlayerCampaignAssignmentId.ShouldBe(placementAssignmentId);
        placement.PlayerDisplayName.ShouldBe("P2 A");
        placement.PlacementOutcome.ShouldBe(PlacementOutcome.NotSelected);
        placement.EventAt.ShouldBe(Base.AddMinutes(3));

        var lifecycle = events.Single(item => item.Kind == DashboardActivityEventKind.CampaignClosed);
        lifecycle.EventId.ShouldBe(lifecycleEventId);
        lifecycle.CampaignName.ShouldBe("Active A");
        lifecycle.LifecycleEventType.ShouldBe(CampaignLifecycleEventType.Closed);

        events.Select(item => item.Kind).ShouldBe(
        [
            DashboardActivityEventKind.CampaignClosed,
            DashboardActivityEventKind.PlacementSet,
            DashboardActivityEventKind.TagApplied,
            DashboardActivityEventKind.NoteAdded
        ]);
    }

    /// <summary>Verifies tag-removal receipts are never surfaced in the feed.</summary>
    [Fact]
    public async Task GetActivity_DoesNotSurfaceRemovedTagReceipts()
    {
        using (var admin = _harness.CreateAdminContext())
        {
            admin.CampaignTagApplicationRemovalReceipts.Add(new CampaignTagApplicationRemovalReceiptEntity
            {
                RemovalOperationId = Guid.NewGuid(),
                CampaignTagApplicationId = 12345,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            });
            admin.SaveChanges();
        }

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.ShouldNotContain(item => item.Kind == DashboardActivityEventKind.TagApplied);
    }

    /// <summary>Verifies the placement event time equals the assignment's <c>ModifiedAt</c> stamp.</summary>
    [Fact]
    public async Task GetActivity_PlacementEvent_UsesAssignmentModifiedAt()
    {
        var modifiedAt = Base.AddMinutes(5);
        var placementAssignmentId = SeedPlacementAssignment(
            _playerA2Id, _campaignAId, ClubAId, PlacementOutcome.NotSelected, ClubAAdminId, modifiedAt);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var placements = result.Value.Events
            .Where(item => item.Kind == DashboardActivityEventKind.PlacementSet)
            .ToList();
        placements.Count.ShouldBe(1);
        placements[0].EventAt.ShouldBe(modifiedAt);
        placements[0].ActorUserId.ShouldBe(ClubAAdminId);
        placements[0].PlacementOutcome.ShouldBe(PlacementOutcome.NotSelected);
        placements[0].PlayerCampaignAssignmentId.ShouldBe(placementAssignmentId);
    }

    /// <summary>Verifies equal-timestamp events across sources use the kind-rank tie-break.</summary>
    [Fact]
    public async Task GetActivity_OrdersCrossSourceTies_ByKindRank()
    {
        SeedNote(_assignmentAId, ClubAId, ClubAAdminId, Base);
        SeedTagApplication(_assignmentAId, ClubAId, _tagAId, ClubAAdminId, Base);
        SeedPlacementAssignment(_playerA2Id, _campaignAId, ClubAId, PlacementOutcome.Undecided, ClubAAdminId, Base);
        SeedLifecycleEvent(_campaignAId, ClubAId, CampaignLifecycleEventType.Reopened, ClubAAdminId, Base);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Select(item => item.Kind).ShouldBe(
        [
            DashboardActivityEventKind.CampaignReopened,
            DashboardActivityEventKind.PlacementSet,
            DashboardActivityEventKind.TagApplied,
            DashboardActivityEventKind.NoteAdded
        ]);
    }

    /// <summary>Verifies an explicit limit bounds the returned event count.</summary>
    [Fact]
    public async Task GetActivity_ReturnsOnlyRequestedLimit()
    {
        for (var index = 0; index < 10; index++)
        {
            SeedNote(_assignmentAId, ClubAId, ClubAAdminId, Base.AddMinutes(index));
        }

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetDashboardActivityInput { Limit = 5 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Count.ShouldBe(5);
    }

    /// <summary>Verifies the feed is tenant-isolated: another club's events are never visible.</summary>
    [Fact]
    public async Task GetActivity_IsTenantIsolated()
    {
        SeedNote(_assignmentAId, ClubAId, ClubAAdminId, Base);
        SeedNote(_assignmentBId, ClubBId, ClubBMemberId, Base.AddMinutes(1));

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Count.ShouldBe(1);
        result.Value.Events[0].CampaignId.ShouldBe(_campaignAId);
    }

    /// <summary>Verifies a missing actor user row falls back to the stable "Former member" text.</summary>
    [Fact]
    public async Task GetActivity_ResolvesFormerMemberFallback_ForMissingActor()
    {
        SeedNote(_assignmentAId, ClubAId, MissingActorUserId, Base);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.ShouldHaveSingleItem();
        result.Value.Events[0].ActorUserId.ShouldBe(MissingActorUserId);
        result.Value.Events[0].ActorDisplayName.ShouldBe("Former member");
    }

    /// <summary>Verifies an out-of-range explicit limit is rejected.</summary>
    [Fact]
    public async Task GetActivity_ReturnsValidation_ForOutOfRangeLimit()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetActivityAsync(
            new GetDashboardActivityInput { Limit = 51 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>Verifies callers without approved membership are rejected.</summary>
    [Fact]
    public async Task GetActivity_ReturnsForbidden_WhenNotMember()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Creates the dashboard query service over the shared SQLite tenancy harness.</summary>
    /// <returns>A service instance.</returns>
    private DashboardQueryService CreateService()
        => new(
            Substitute.For<ICampaignQueryService>(),
            Substitute.For<ICampaignPlacementQueryService>(),
            Substitute.For<IClubJoinRequestService>(),
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _harness.CurrentUser,
            NullLogger<DashboardQueryService>.Instance);

    /// <summary>Seeds a note and returns its identifier with a deterministic creation timestamp.</summary>
    private long SeedNote(long assignmentId, long clubId, long actorId, DateTimeOffset createdAt)
    {
        using var admin = _harness.CreateAdminContext();
        var note = new NoteEntity
        {
            Content = "Note",
            PlayerCampaignAssignmentId = assignmentId,
            ClubId = clubId,
            CreatedById = actorId
        };
        admin.Notes.Add(note);
        admin.SaveChanges();
        note.CreatedAt = createdAt;
        admin.SaveChanges();
        return note.NoteId;
    }

    /// <summary>Seeds a tag application and returns its identifier with a deterministic timestamp.</summary>
    private long SeedTagApplication(long assignmentId, long clubId, long playerTagId, long actorId, DateTimeOffset createdAt)
    {
        using var admin = _harness.CreateAdminContext();
        var application = new CampaignTagApplicationEntity
        {
            PlayerCampaignAssignmentId = assignmentId,
            PlayerTagId = playerTagId,
            ClubId = clubId,
            CreatedById = actorId
        };
        admin.CampaignTagApplications.Add(application);
        admin.SaveChanges();
        application.CreatedAt = createdAt;
        admin.SaveChanges();
        return application.CampaignTagApplicationId;
    }

    /// <summary>Seeds a placement-change assignment with a deterministic <c>ModifiedAt</c> stamp.</summary>
    private long SeedPlacementAssignment(
        long playerId,
        long campaignId,
        long clubId,
        PlacementOutcome outcome,
        long actorId,
        DateTimeOffset modifiedAt)
    {
        using var admin = _harness.CreateAdminContext();
        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = playerId,
            CampaignId = campaignId,
            ClubId = clubId,
            CreatedById = actorId,
            PlacementOutcome = outcome,
            ModifiedAt = modifiedAt,
            ModifiedById = actorId
        };
        admin.PlayerCampaignAssignments.Add(assignment);
        admin.SaveChanges();
        return assignment.PlayerCampaignAssignmentId;
    }

    /// <summary>Seeds a lifecycle event and returns its identifier with a deterministic timestamp.</summary>
    private long SeedLifecycleEvent(
        long campaignId,
        long clubId,
        CampaignLifecycleEventType eventType,
        long actorId,
        DateTimeOffset createdAt)
    {
        using var admin = _harness.CreateAdminContext();
        var lifecycleEvent = new CampaignLifecycleEventEntity
        {
            CampaignId = campaignId,
            ClubId = clubId,
            EventType = eventType,
            CreatedById = actorId
        };
        admin.CampaignLifecycleEvents.Add(lifecycleEvent);
        admin.SaveChanges();
        lifecycleEvent.CreatedAt = createdAt;
        admin.SaveChanges();
        return lifecycleEvent.CampaignLifecycleEventId;
    }

    /// <summary>Seeds clubs, users, seasons, campaigns, players, assignments, and a tag for two clubs.</summary>
    private void SeedBase()
    {
        using var admin = _harness.CreateAdminContext();

        admin.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });

        admin.Users.AddRange(
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Amelia", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Bobby", LastName = "Member", ClubId = ClubBId });

        var seasonA = new SeasonEntity { Name = "Season A", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubAId, CreatedById = ClubAMemberId };
        var seasonB = new SeasonEntity { Name = "Season B", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Seasons.AddRange(seasonA, seasonB);
        admin.SaveChanges();

        var campaignA = new CampaignEntity { Name = "Active A", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = seasonA.SeasonId, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var campaignB = new CampaignEntity { Name = "Campaign B", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = seasonB.SeasonId, ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Campaigns.AddRange(campaignA, campaignB);
        admin.SaveChanges();

        _campaignAId = campaignA.CampaignId;
        _campaignBId = campaignB.CampaignId;

        var playerA = new PlayerEntity { FirstName = "P", LastName = "A", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var playerA2 = new PlayerEntity { FirstName = "P2", LastName = "A", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubAId, CreatedById = ClubAMemberId };
        var playerB = new PlayerEntity { FirstName = "B", LastName = "Player", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, LifecycleStatus = LifecycleStatus.Active, ClubId = ClubBId, CreatedById = ClubBMemberId };
        admin.Players.AddRange(playerA, playerA2, playerB);

        var tagA = new PlayerTagEntity { Name = "Speed", Color = "#000000", ClubId = ClubAId, CreatedById = ClubAMemberId };
        admin.PlayerTags.Add(tagA);
        admin.SaveChanges();

        _playerAId = playerA.PlayerId;
        _playerA2Id = playerA2.PlayerId;
        _tagAId = tagA.PlayerTagId;

        var assignmentA = new PlayerCampaignAssignmentEntity { PlayerId = playerA.PlayerId, CampaignId = _campaignAId, ClubId = ClubAId, CreatedById = ClubAMemberId, PlacementOutcome = PlacementOutcome.Undecided };
        var assignmentB = new PlayerCampaignAssignmentEntity { PlayerId = playerB.PlayerId, CampaignId = _campaignBId, ClubId = ClubBId, CreatedById = ClubBMemberId, PlacementOutcome = PlacementOutcome.Undecided };
        admin.PlayerCampaignAssignments.AddRange(assignmentA, assignmentB);
        admin.SaveChanges();

        _assignmentAId = assignmentA.PlayerCampaignAssignmentId;
        _assignmentBId = assignmentB.PlayerCampaignAssignmentId;
    }
}
