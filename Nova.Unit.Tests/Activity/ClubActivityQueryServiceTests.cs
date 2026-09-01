using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Activity;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Activity;

/// <summary>
/// Verifies the tenant-safe, role-shaped club activity feed over the shared SQLite tenancy
/// harness: authorization, tenant isolation, member-versus-administrator visibility, snapshot
/// readability, and deterministic keyset paging with continuation cursors.
/// </summary>
public sealed class ClubActivityQueryServiceTests : IDisposable
{
    private const long ClubAId = 100;
    private const long ClubBId = 200;
    private const long ClubAMemberId = 300;
    private const long ClubAAdminId = 301;
    private const long ClubBMemberId = 400;
    private const long MissingActorUserId = 999_999;

    private readonly TenancyTestHarness _harness = new();

    /// <summary>Initializes seeded club and user data for two clubs.</summary>
    public ClubActivityQueryServiceTests() => SeedBase();

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    /// <summary>Verifies an unsigned-in caller cannot read the club feed.</summary>
    [Fact]
    public async Task GetClubActivity_ReturnsForbidden_WhenNotSignedIn()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies a signed-in user without a club cannot read the club feed.</summary>
    [Fact]
    public async Task GetClubActivity_ReturnsForbidden_WhenUserHasNoClub()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = null;

        var result = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>Verifies cross-tenant events are invisible through the read filter.</summary>
    [Fact]
    public async Task GetClubActivity_ReturnsNoCrossTenantEvents()
    {
        SeedEvents(ClubAId, [
            EventSpec(ActivityEventKind.CampaignOpened, ClubAAdminId, "Admin A", new CampaignLifecycleContext
            {
                CampaignId = 1,
                CampaignName = "Campaign A"
            }, Time(10, 0))
        ]);
        SeedEvents(ClubBId, [
            EventSpec(ActivityEventKind.CampaignOpened, ClubBMemberId, "Bobby Member", new CampaignLifecycleContext
            {
                CampaignId = 2,
                CampaignName = "Campaign B"
            }, Time(11, 0))
        ]);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.ShouldHaveSingleItem();
        result.Value.Events[0].ActorDisplayName.ShouldBe("Admin A");
    }

    /// <summary>Verifies members never see administrator-only rows.</summary>
    [Fact]
    public async Task GetClubActivity_HidesAdminOnlyEvents_FromMembers()
    {
        SeedEvents(ClubAId, [
            EventSpec(ActivityEventKind.JoinRequestSubmitted, ClubAAdminId, "Admin A", new JoinRequestContext
            {
                JoinRequestId = 1,
                RequesterDisplayName = "Nadia N"
            }, Time(10, 0)),
            EventSpec(ActivityEventKind.CampaignOpened, ClubAAdminId, "Admin A", new CampaignLifecycleContext
            {
                CampaignId = 1,
                CampaignName = "Campaign A"
            }, Time(9, 0))
        ]);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;

        var result = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.ShouldHaveSingleItem();
        result.Value.Events[0].Kind.ShouldBe(ActivityEventKind.CampaignOpened);
    }

    /// <summary>Verifies administrators see administrator-only rows alongside public ones.</summary>
    [Fact]
    public async Task GetClubActivity_IncludesAdminOnlyEvents_ForAdministrators()
    {
        SeedEvents(ClubAId, [
            EventSpec(ActivityEventKind.CampaignOpened, ClubAAdminId, "Admin A", new CampaignLifecycleContext
            {
                CampaignId = 1,
                CampaignName = "Campaign A"
            }, Time(9, 0)),
            EventSpec(ActivityEventKind.JoinRequestSubmitted, ClubAAdminId, "Admin A", new JoinRequestContext
            {
                JoinRequestId = 1,
                RequesterDisplayName = "Nadia N"
            }, Time(10, 0))
        ]);

        _harness.CurrentUser.UserId = ClubAAdminId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var result = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Count.ShouldBe(2);
        result.Value.Events[0].Kind.ShouldBe(ActivityEventKind.JoinRequestSubmitted);
    }

    /// <summary>Verifies stored actor snapshots survive actor removal.</summary>
    [Fact]
    public async Task GetClubActivity_ReturnsStoredActorSnapshot_ForRemovedActor()
    {
        SeedEvents(ClubAId, [
            EventSpec(ActivityEventKind.CampaignOpened, MissingActorUserId, "Former member", new CampaignLifecycleContext
            {
                CampaignId = 1,
                CampaignName = "Campaign A"
            }, Time(10, 0))
        ]);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var item = result.Value.Events.ShouldHaveSingleItem();
        item.ActorUserId.ShouldBe(MissingActorUserId);
        item.ActorDisplayName.ShouldBe("Former member");
    }

    /// <summary>Verifies a deleted-draft event remains readable from its payload snapshot.</summary>
    [Fact]
    public async Task GetClubActivity_ReturnsDeletedDraftEvent_FromPayloadSnapshot()
    {
        SeedEvents(ClubAId, [
            EventSpec(ActivityEventKind.CampaignDraftDeleted, ClubAAdminId, "Admin A", new CampaignLifecycleContext
            {
                CampaignId = 1,
                CampaignName = "Draft camp"
            }, Time(10, 0))
        ]);

        _harness.CurrentUser.UserId = ClubAAdminId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var result = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var item = result.Value.Events.ShouldHaveSingleItem();
        item.Kind.ShouldBe(ActivityEventKind.CampaignDraftDeleted);
        var context = item.Context.ShouldBeOfType<CampaignLifecycleContext>();
        context.CampaignName.ShouldBe("Draft camp");
    }

    /// <summary>Verifies pages are of the fixed size and the cursor yields an exclusive continuation.</summary>
    [Fact]
    public async Task GetClubActivity_PagesDeterministically_WithContinuationCursor()
    {
        var baseTime = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var seeds = Enumerable.Range(0, GetClubActivityInput.PageSize + 5)
            .Select(index => EventSpec(
                ActivityEventKind.CampaignOpened,
                ClubAAdminId,
                "Admin A",
                new CampaignLifecycleContext { CampaignId = 1, CampaignName = "Campaign A" },
                baseTime.AddMinutes(index)))
            .ToList();
        var entities = SeedEvents(ClubAId, seeds);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var first = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        first.IsSuccess.ShouldBeTrue();
        first.Value.Events.Count.ShouldBe(GetClubActivityInput.PageSize);
        first.Value.HasMore.ShouldBeTrue();
        first.Value.NextCursor.ShouldNotBeNull();

        var expectedLastId = entities
            .OrderByDescending(entity => entity.CreatedAt)
            .ThenByDescending(entity => entity.ActivityEventId)
            .Skip(GetClubActivityInput.PageSize - 1)
            .First()
            .ActivityEventId;
        first.Value.NextCursor.ActivityEventId.ShouldBe(expectedLastId);

        var second = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput
            {
                BeforeActivityEventId = first.Value.NextCursor.ActivityEventId,
                BeforeOccurredAt = first.Value.NextCursor.OccurredAt
            },
            TestContext.Current.CancellationToken);

        second.IsSuccess.ShouldBeTrue();
        second.Value.Events.Count.ShouldBe(5);
        second.Value.HasMore.ShouldBeFalse();
        second.Value.NextCursor.ShouldBeNull();
        second.Value.Events.Select(item => item.ActivityEventId)
            .ShouldBe(entities
                .OrderByDescending(entity => entity.CreatedAt)
                .ThenByDescending(entity => entity.ActivityEventId)
                .Skip(GetClubActivityInput.PageSize)
                .Select(entity => entity.ActivityEventId));
    }

    /// <summary>Verifies the newest page honors the newest-first ordering with equal-time tie-breaking.</summary>
    [Fact]
    public async Task GetClubActivity_OrdersEvents_NewestFirstWithIdTieBreak()
    {
        var equalTime = new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero);
        var entities = SeedEvents(ClubAId, [
            EventSpec(ActivityEventKind.CampaignOpened, ClubAAdminId, "Admin A", new CampaignLifecycleContext
            {
                CampaignId = 1,
                CampaignName = "Campaign A"
            }, equalTime),
            EventSpec(ActivityEventKind.CampaignClosed, ClubAAdminId, "Admin A", new CampaignLifecycleContext
            {
                CampaignId = 1,
                CampaignName = "Campaign A"
            }, equalTime)
        ]);

        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.Count.ShouldBe(2);
        var expectedIds = entities
            .OrderByDescending(entity => entity.CreatedAt)
            .ThenByDescending(entity => entity.ActivityEventId)
            .Select(entity => entity.ActivityEventId)
            .ToList();
        result.Value.Events.Select(item => item.ActivityEventId).ShouldBe(expectedIds);
    }

    /// <summary>Verifies malformed payloads are skipped rather than surfaced and the page stays aligned.</summary>
    [Fact]
    public async Task GetClubActivity_SkipsMalformedPayloads_AndContinuesPaging()
    {
        var adminOnly = RawEventSpec(ActivityEventKind.JoinRequestRejected, ClubAAdminId, "Admin A", "{ not json }", Time(10, 0));
        var publicEvent = EventSpec(ActivityEventKind.CampaignOpened, ClubAAdminId, "Admin A", new CampaignLifecycleContext
        {
            CampaignId = 1,
            CampaignName = "Campaign A"
        }, Time(9, 0));
        SeedEvents(ClubAId, [adminOnly, publicEvent]);

        _harness.CurrentUser.UserId = ClubAAdminId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;

        var result = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events.ShouldHaveSingleItem();
        result.Value.Events[0].Kind.ShouldBe(ActivityEventKind.CampaignOpened);
    }

    /// <summary>Verifies both-or-neither cursor validation.</summary>
    [Fact]
    public async Task GetClubActivity_ReturnsValidation_ForCursorWithoutTime()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput { BeforeActivityEventId = 5 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>Verifies non-positive cursor identifiers are rejected before any query.</summary>
    [Fact]
    public async Task GetClubActivity_ReturnsValidation_ForNegativeCursorId()
    {
        _harness.CurrentUser.UserId = ClubAMemberId;
        _harness.CurrentUser.ClubId = ClubAId;

        var result = await CreateService().GetClubActivityAsync(
            new GetClubActivityInput
            {
                BeforeActivityEventId = -1,
                BeforeOccurredAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>Creates the club activity query service over the shared SQLite tenancy harness.</summary>
    /// <returns>A service instance using the mutable fake current-user provider.</returns>
    private ClubActivityQueryService CreateService()
        => new(
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _harness.CurrentUser,
            NullLogger<ClubActivityQueryService>.Instance);

    /// <summary>Seeds clubs and users for two clubs.</summary>
    private void SeedBase()
    {
        using var admin = _harness.CreateAdminContext();

        admin.Clubs.AddRange(
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = ClubAMemberId },
            new ClubEntity { CreationOperationId = Guid.NewGuid(), ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBMemberId });
        admin.Users.AddRange(
            new NovaUserEntity { Id = ClubAMemberId, FirstName = "Amelia", LastName = "Member", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubAAdminId, FirstName = "Admin", LastName = "A", ClubId = ClubAId },
            new NovaUserEntity { Id = ClubBMemberId, FirstName = "Bobby", LastName = "Member", ClubId = ClubBId });

        admin.SaveChanges();
    }

    /// <summary>Seeds activity events for a club and returns the persisted entities in seed order.</summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="specs">The event specifications to persist.</param>
    /// <returns>The persisted event entities in seed order.</returns>
    private IReadOnlyList<ActivityEventEntity> SeedEvents(
        long clubId,
        IReadOnlyList<IEventSpec> specs)
    {
        using var admin = _harness.CreateAdminContext();
        var entities = specs.Select(spec => new ActivityEventEntity
        {
            ClubId = clubId,
            EventKind = spec.Kind,
            IsAdminOnly = ActivityEventPolicy.IsAdminOnly(spec.Kind),
            ActorUserId = spec.ActorUserId,
            ActorDisplayName = spec.ActorDisplayName,
            PayloadJson = spec.PayloadJson,
            CreatedById = spec.ActorUserId
        }).ToList();
        admin.ActivityEvents.AddRange(entities);
        admin.SaveChanges();
        return entities;
    }

    /// <summary>Builds an event specification with a serialized polymorphic context payload.</summary>
    /// <param name="kind">The event kind.</param>
    /// <param name="actorUserId">The acting user identifier.</param>
    /// <param name="actorDisplayName">The stored actor display-name snapshot.</param>
    /// <param name="context">The family-shaped context to serialize with the polymorphic discriminator.</param>
    /// <param name="createdAt">The occurrence time.</param>
    /// <returns>The event specification.</returns>
    private static IEventSpec EventSpec(
        ActivityEventKind kind,
        long actorUserId,
        string actorDisplayName,
        ClubActivityContext context,
        DateTimeOffset createdAt)
        => new EventSpecRecord(
            kind,
            actorUserId,
            actorDisplayName,
            JsonSerializer.Serialize(context, typeof(ClubActivityContext), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            createdAt);

    /// <summary>Builds an event specification with an explicit raw payload string.</summary>
    /// <param name="kind">The event kind.</param>
    /// <param name="actorUserId">The acting user identifier.</param>
    /// <param name="actorDisplayName">The stored actor display-name snapshot.</param>
    /// <param name="payloadJson">The raw payload.</param>
    /// <param name="createdAt">The occurrence time.</param>
    /// <returns>The event specification.</returns>
    private static IEventSpec RawEventSpec(
        ActivityEventKind kind,
        long actorUserId,
        string actorDisplayName,
        string payloadJson,
        DateTimeOffset createdAt)
        => new EventSpecRecord(kind, actorUserId, actorDisplayName, payloadJson, createdAt);

    /// <summary>Provides the event seeding input surface.</summary>
    private interface IEventSpec
    {
        ActivityEventKind Kind { get; }
        long ActorUserId { get; }
        string ActorDisplayName { get; }
        string PayloadJson { get; }
        DateTimeOffset CreatedAt { get; }
    }

    /// <summary>An event seeding specification record.</summary>
    private sealed record EventSpecRecord(
        ActivityEventKind Kind,
        long ActorUserId,
        string ActorDisplayName,
        string PayloadJson,
        DateTimeOffset CreatedAt) : IEventSpec;

    /// <summary>Builds a deterministic occurrence time on 2026-10-01.</summary>
    /// <param name="hour">The hour.</param>
    /// <param name="minute">The minute, defaults to zero.</param>
    /// <returns>The occurrence time.</returns>
    private static DateTimeOffset Time(int hour, int minute = 0)
        => new(2026, 10, 1, hour, minute, 0, TimeSpan.Zero);
}
