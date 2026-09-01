using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
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

/// <summary>Verifies tenant-safe, role-shaped, cursor-paged durable dashboard activity.</summary>
public sealed class DashboardActivityQueryServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAMemberId = 300;
    private const long ClubAAdminId = 301;
    private const long ClubBMemberId = 400;

    private readonly TenancyTestHarness _harness = new();
    private long _campaignId;
    private long _playerId;
    private long _assignmentId;

    /// <summary>Seeds two tenants and placement targets.</summary>
    public DashboardActivityQueryServiceTests() => SeedBase();

    /// <summary>Releases the shared SQLite connection.</summary>
    public void Dispose() => _harness.Dispose();

    /// <summary>Proves audience filtering happens before the fixed page bound.</summary>
    [Fact]
    public async Task GetActivity_FiltersAdministratorEvents_BeforePaging()
    {
        var memberEvent = CampaignEvent(ClubActivityEventKind.CampaignOpened, ClubActivityAudience.AllMembers);
        var events = new List<ClubActivityEventEntity> { memberEvent };
        events.AddRange(Enumerable.Range(0, DashboardActivityResult.PageSize)
            .Select(_ => CampaignEvent(ClubActivityEventKind.CampaignDraftCreated, ClubActivityAudience.Administrators)));
        SeedEvents(events);

        ActAs(ClubAMemberId, ClubAId, isClubAdmin: false);
        var result = await CreateService().GetActivityAsync(new(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.ShouldHaveSingleItem();
        result.Value.Events[0].EventId.ShouldBe(memberEvent.ClubActivityEventId);
        result.Value.Events[0].Kind.ShouldBe(DashboardActivityEventKind.CampaignOpened);
        result.Value.NextContinuationToken.ShouldBeNull();
    }

    /// <summary>Proves approval is shaped as membership for members and request detail for admins.</summary>
    [Fact]
    public async Task GetActivity_ShapesApprovedRequest_ByRole()
    {
        SeedEvents(new ClubActivityEventEntity
        {
            ClubId = ClubAId,
            EventKind = ClubActivityEventKind.JoinRequestApproved,
            Audience = ClubActivityAudience.AllMembers,
            ActorDisplayName = "Admin Actual",
            SubjectUserId = ClubAMemberId,
            SubjectDisplayName = "Amelia Member",
            JoinRequestId = 71,
            CreatedById = ClubAAdminId
        });

        ActAs(ClubAMemberId, ClubAId, isClubAdmin: false);
        var memberResult = await CreateService().GetActivityAsync(new(), TestContext.Current.CancellationToken);
        memberResult.Value.Events[0].Kind.ShouldBe(DashboardActivityEventKind.MemberJoined);
        memberResult.Value.Events[0].Context.ShouldBeOfType<MembershipActivityContextDto>();

        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);
        var adminResult = await CreateService().GetActivityAsync(new(), TestContext.Current.CancellationToken);
        adminResult.Value.Events[0].Kind.ShouldBe(DashboardActivityEventKind.JoinRequestApproved);
        adminResult.Value.Events[0].Context.ShouldBeOfType<JoinRequestActivityContextDto>();
    }

    /// <summary>Proves equal timestamps page deterministically by descending event identity.</summary>
    [Fact]
    public async Task GetActivity_PagesEqualTimestamps_ByDescendingEventId()
    {
        SeedEvents(Enumerable.Range(0, 25)
            .Select(_ => CampaignEvent(ClubActivityEventKind.CampaignOpened, ClubActivityAudience.AllMembers))
            .ToArray());
        ActAs(ClubAMemberId, ClubAId, isClubAdmin: false);

        var first = await CreateService().GetActivityAsync(new(), TestContext.Current.CancellationToken);
        var second = await CreateService().GetActivityAsync(
            new GetDashboardActivityInput { ContinuationToken = first.Value.NextContinuationToken },
            TestContext.Current.CancellationToken);

        first.Value.Events.Count.ShouldBe(DashboardActivityResult.PageSize);
        first.Value.NextContinuationToken.ShouldNotBeNull();
        first.Value.Events.Select(item => item.EventId).ShouldBeInOrder(SortDirection.Descending);
        second.Value.Events.Count.ShouldBe(5);
        second.Value.NextContinuationToken.ShouldBeNull();
        second.Value.Events.Max(item => item.EventId).ShouldBeLessThan(first.Value.Events.Min(item => item.EventId));
    }

    /// <summary>Proves deleted Draft targets retain their distinct kind and durable name.</summary>
    [Fact]
    public async Task GetActivity_RetainsDeletedDraftSnapshot()
    {
        SeedEvents(new ClubActivityEventEntity
        {
            ClubId = ClubAId,
            EventKind = ClubActivityEventKind.CampaignDraftDeleted,
            Audience = ClubActivityAudience.Administrators,
            ActorDisplayName = "Admin Actual",
            CampaignId = 999_999,
            CampaignName = "Deleted Draft",
            SeasonName = "Fall",
            CreatedById = ClubAAdminId
        });
        ActAs(ClubAAdminId, ClubAId, isClubAdmin: true);

        var result = await CreateService().GetActivityAsync(new(), TestContext.Current.CancellationToken);

        result.Value.Events[0].Kind.ShouldBe(DashboardActivityEventKind.CampaignDraftDeleted);
        var context = result.Value.Events[0].Context.ShouldBeOfType<CampaignActivityContextDto>();
        context.CampaignId.ShouldBeNull();
        context.CampaignName.ShouldBe("Deleted Draft");
    }

    /// <summary>Proves a live player link is resolved from Players rather than identity users.</summary>
    [Fact]
    public async Task GetActivity_PlacementResolvesLivePlayerId()
    {
        SeedEvents(new ClubActivityEventEntity
        {
            ClubId = ClubAId,
            EventKind = ClubActivityEventKind.PlacementAssigned,
            Audience = ClubActivityAudience.AllMembers,
            ActorDisplayName = "Admin Actual",
            CampaignId = _campaignId,
            CampaignName = "Active A",
            PlayerCampaignAssignmentId = _assignmentId,
            PlayerId = _playerId,
            PlayerDisplayName = "Player A",
            PreviousPlacementOutcome = PlacementOutcome.Undecided,
            CurrentPlacementOutcome = PlacementOutcome.Assigned,
            CurrentTeamName = "U14 Teal",
            CreatedById = ClubAAdminId
        });
        ActAs(ClubAMemberId, ClubAId, isClubAdmin: false);

        var result = await CreateService().GetActivityAsync(new(), TestContext.Current.CancellationToken);

        var context = result.Value.Events[0].Context.ShouldBeOfType<PlacementActivityContextDto>();
        context.PlayerId.ShouldBe(_playerId);
        context.PlayerCampaignAssignmentId.ShouldBe(_assignmentId);
    }

    /// <summary>Proves mutable note data is never synthesized into the durable club feed.</summary>
    [Fact]
    public async Task GetActivity_DoesNotSynthesizeLegacyNoteActivity()
    {
        using (var admin = _harness.CreateAdminContext())
        {
            admin.Notes.Add(new NoteEntity
            {
                Content = "Local campaign note",
                PlayerCampaignAssignmentId = _assignmentId,
                ClubId = ClubAId,
                CreatedById = ClubAAdminId
            });
            admin.SaveChanges();
        }
        ActAs(ClubAMemberId, ClubAId, isClubAdmin: false);

        var result = await CreateService().GetActivityAsync(new(), TestContext.Current.CancellationToken);

        result.Value.Events.ShouldBeEmpty();
    }

    /// <summary>Proves another club's durable rows cannot enter the page.</summary>
    [Fact]
    public async Task GetActivity_IsTenantIsolated()
    {
        SeedEvents(
            CampaignEvent(ClubActivityEventKind.CampaignOpened, ClubActivityAudience.AllMembers),
            CampaignEvent(ClubActivityEventKind.CampaignOpened, ClubActivityAudience.AllMembers, ClubBId));
        ActAs(ClubAMemberId, ClubAId, isClubAdmin: false);

        var result = await CreateService().GetActivityAsync(new(), TestContext.Current.CancellationToken);

        result.Value.Events.ShouldHaveSingleItem();
        var context = result.Value.Events[0].Context.ShouldBeOfType<CampaignActivityContextDto>();
        context.CampaignName.ShouldBe("Active A");
    }

    /// <summary>Proves malformed continuation tokens are rejected before querying a page.</summary>
    [Fact]
    public async Task GetActivity_ReturnsValidation_ForMalformedCursor()
    {
        ActAs(ClubAMemberId, ClubAId, isClubAdmin: false);

        var result = await CreateService().GetActivityAsync(
            new GetDashboardActivityInput { ContinuationToken = "not-base64" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>Proves callers without approved club membership are rejected.</summary>
    [Fact]
    public async Task GetActivity_ReturnsForbidden_WhenNotMember()
    {
        ActAs(userId: null, clubId: null, isClubAdmin: false);

        var result = await CreateService().GetActivityAsync(new(), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Creates the dashboard query service over the SQLite read context.</summary>
    private DashboardQueryService CreateService()
        => new(
            Substitute.For<ICampaignQueryService>(),
            Substitute.For<IClubJoinRequestService>(),
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _harness.CurrentUser,
            NullLogger<DashboardQueryService>.Instance);

    /// <summary>Creates campaign activity evidence for a selected tenant.</summary>
    private ClubActivityEventEntity CampaignEvent(
        ClubActivityEventKind kind,
        ClubActivityAudience audience,
        long clubId = ClubAId)
        => new()
        {
            ClubId = clubId,
            EventKind = kind,
            Audience = audience,
            ActorDisplayName = clubId == ClubAId ? "Admin Actual" : "Other Actor",
            CampaignId = clubId == ClubAId ? _campaignId : null,
            CampaignName = clubId == ClubAId ? "Active A" : "Other Campaign",
            CreatedById = clubId == ClubAId ? ClubAAdminId : ClubBMemberId
        };

    /// <summary>Persists one or more events in a single save so timestamps may tie.</summary>
    private void SeedEvents(params ClubActivityEventEntity[] events)
    {
        using var admin = _harness.CreateAdminContext();
        admin.ClubActivityEvents.AddRange(events);
        admin.SaveChanges();
    }

    /// <summary>Persists one or more events in a single save so timestamps may tie.</summary>
    private void SeedEvents(IEnumerable<ClubActivityEventEntity> events)
        => SeedEvents(events.ToArray());

    /// <summary>Sets the simulated current user for the next read context.</summary>
    private void ActAs(long? userId, long? clubId, bool isClubAdmin)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    /// <summary>Seeds two clubs and one resolvable placement target.</summary>
    private void SeedBase()
    {
        using var admin = _harness.CreateAdminContext();
        admin.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAAdminId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });
        admin.Users.AddRange(
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Amelia", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "Actual", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Other", LastName = "Member", ClubId = ClubBId });
        var season = new SeasonEntity { Name = "Season A", StartDate = new DateOnly(2026, 1, 1), ClubId = ClubAId, CreatedById = ClubAAdminId };
        admin.Seasons.Add(season);
        admin.SaveChanges();

        var campaign = new CampaignEntity { Name = "Active A", StartDate = new DateOnly(2026, 6, 1), Status = CampaignStatus.Active, SeasonId = season.SeasonId, ClubId = ClubAId, CreatedById = ClubAAdminId };
        var player = new PlayerEntity { FirstName = "Player", LastName = "A", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, ClubId = ClubAId, CreatedById = ClubAAdminId };
        admin.AddRange(campaign, player);
        admin.SaveChanges();

        var assignment = new PlayerCampaignAssignmentEntity { PlayerId = player.PlayerId, CampaignId = campaign.CampaignId, ClubId = ClubAId, CreatedById = ClubAAdminId };
        admin.Add(assignment);
        admin.SaveChanges();
        _campaignId = campaign.CampaignId;
        _playerId = player.PlayerId;
        _assignmentId = assignment.PlayerCampaignAssignmentId;
    }
}
