using System.Text.Json;
using Nova.Entities;
using Nova.Features.Activity;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Shouldly;

namespace Nova.Unit.Tests.Activity;

/// <summary>
/// Verifies the deterministic feed projection and keyset paging applied over loaded activity rows:
/// ordering with shared timestamps, role-shaped membership rows, malformed payload skipping, and
/// continuation cursors.
/// </summary>
public sealed class ClubActivityFeedPolicyTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly DateTimeOffset BaseTime = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Verifies equal-timestamp events use the identifier descending tie-breaker.</summary>
    [Fact]
    public void BuildPage_OrdersEqualTimestamps_ByDescendingEventId()
    {
        var rows = new[]
        {
            Row(id: 2, kind: ActivityEventKind.CampaignOpened, time: BaseTime, campaignName: "Open"),
            Row(id: 1, kind: ActivityEventKind.CampaignClosed, time: BaseTime, campaignName: "Close"),
        };

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, JsonOptions);

        result.Events.Select(item => item.ActivityEventId).ShouldBe([2, 1]);
    }

    /// <summary>Verifies the newest page contains the page size and reports continuation.</summary>
    [Fact]
    public void BuildPage_ReturnsPageSizeAndHasMore_WhenMoreRowsExist()
    {
        var rows = Enumerable.Range(0, ClubActivityFeedPolicy.PageSize + 5)
            .Select(index => Row(
                id: index + 1,
                kind: ActivityEventKind.CampaignOpened,
                time: BaseTime.AddMinutes(index),
                campaignName: $"C{index}"))
            .ToList();

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, JsonOptions);

        result.Events.Count.ShouldBe(ClubActivityFeedPolicy.PageSize);
        result.HasMore.ShouldBeTrue();
        result.NextCursor.ShouldNotBeNull();
        result.Events.First().ActivityEventId.ShouldBe(ClubActivityFeedPolicy.PageSize + 5);
        // The cursor is the last raw row of the page: id 6 at AddMinutes(5).
        result.NextCursor.ActivityEventId.ShouldBe(6);
        result.NextCursor.OccurredAt.ShouldBe(BaseTime.AddMinutes(5));
    }

    /// <summary>Verifies a page with exactly the page size has no continuation.</summary>
    [Fact]
    public void BuildPage_HasNoNextCursor_WhenPageIsExactlyFull()
    {
        var rows = Enumerable.Range(0, ClubActivityFeedPolicy.PageSize)
            .Select(index => Row(
                id: index + 1,
                kind: ActivityEventKind.CampaignOpened,
                time: BaseTime.AddMinutes(index),
                campaignName: $"C{index}"))
            .ToList();

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, JsonOptions);

        result.Events.Count.ShouldBe(ClubActivityFeedPolicy.PageSize);
        result.HasMore.ShouldBeFalse();
        result.NextCursor.ShouldBeNull();
    }

    /// <summary>Verifies the continuation cursor resumes after the oldest returned row of the previous page.</summary>
    [Fact]
    public void BuildPage_ApplyingNextCursor_ReturnsFollowingRows()
    {
        var rows = Enumerable.Range(0, ClubActivityFeedPolicy.PageSize + 3)
            .Select(index => Row(
                id: index + 1,
                kind: ActivityEventKind.CampaignOpened,
                time: BaseTime.AddMinutes(index),
                campaignName: $"C{index}"))
            .ToList();

        var first = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, JsonOptions);
        var second = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, first.NextCursor, JsonOptions);

        second.Events.Count.ShouldBe(3);
        second.HasMore.ShouldBeFalse();
        second.NextCursor.ShouldBeNull();
        second.Events.Select(item => item.ActivityEventId).ShouldBe([3, 2, 1]);
    }

    /// <summary>Verifies a cursor on a shared timestamp boundary is exclusive and deterministic.</summary>
    [Fact]
    public void BuildPage_CursorIsExclusive_OnSharedTimestampBoundary()
    {
        var shared = BaseTime.AddMinutes(30);
        var rows = new[]
        {
            Row(id: 5, kind: ActivityEventKind.CampaignOpened, time: shared, campaignName: "C5"),
            Row(id: 4, kind: ActivityEventKind.CampaignOpened, time: shared, campaignName: "C4"),
            Row(id: 3, kind: ActivityEventKind.CampaignOpened, time: shared, campaignName: "C3"),
            Row(id: 2, kind: ActivityEventKind.CampaignOpened, time: BaseTime, campaignName: "C2"),
        };

        var cursor = new ClubActivityCursor(ActivityEventId: 4, OccurredAt: shared);
        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor, JsonOptions);

        result.Events.Select(item => item.ActivityEventId).ShouldBe([3, 2]);
    }

    /// <summary>Verifies members do not see administrator-only rows.</summary>
    [Fact]
    public void BuildPage_HidesAdminOnlyRows_ForMembers()
    {
        var rows = new[]
        {
            Row(id: 2, kind: ActivityEventKind.JoinRequestSubmitted, time: BaseTime.AddMinutes(1)),
            Row(id: 1, kind: ActivityEventKind.MemberJoined, time: BaseTime, member: "M"),
        };

        var memberResult = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: false, cursor: null, JsonOptions);
        var adminResult = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, JsonOptions);

        memberResult.Events.ShouldHaveSingleItem();
        memberResult.Events[0].Kind.ShouldBe(ActivityEventKind.MemberJoined);

        adminResult.Events.Count.ShouldBe(2);
    }

    /// <summary>Verifies MemberJoined is role-shaped: members see no approving actor name and no
    /// top-level actor identity.</summary>
    [Fact]
    public void BuildPage_ShapesMemberJoined_ForMemberViewer()
    {
        var rows = new[]
        {
            Row(
                id: 1,
                kind: ActivityEventKind.MemberJoined,
                time: BaseTime,
                member: "Sam Doe",
                approvedBy: "Jordan Lee"),
        };

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: false, cursor: null, JsonOptions);

        var item = result.Events.Single();
        item.Kind.ShouldBe(ActivityEventKind.MemberJoined);
        var context = item.Context.ShouldBeOfType<MembershipContext>();
        context.MemberDisplayName.ShouldBe("Sam Doe");
        context.ApprovedByActorName.ShouldBeNull();
        item.ActorUserId.ShouldBeNull();
        item.ActorDisplayName.ShouldBeNull();
    }

    /// <summary>Verifies MemberJoined is role-shaped: administrators see the approving actor name.</summary>
    [Fact]
    public void BuildPage_ShapesMemberJoined_ForAdminViewer()
    {
        var rows = new[]
        {
            Row(
                id: 1,
                kind: ActivityEventKind.MemberJoined,
                time: BaseTime,
                member: "Sam Doe",
                approvedBy: "Jordan Lee"),
        };

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, JsonOptions);

        var context = result.Events.Single().Context.ShouldBeOfType<MembershipContext>();
        context.ApprovedByActorName.ShouldBe("Jordan Lee");
    }

    /// <summary>Verifies a malformed payload row is skipped rather than surfaced.</summary>
    [Fact]
    public void BuildPage_SkipsRows_WithMalformedPayload()
    {
        var rows = new[]
        {
            Row(id: 3, kind: ActivityEventKind.CampaignOpened, time: BaseTime.AddMinutes(2), campaignName: "Good"),
            RawRow(id: 2, kind: ActivityEventKind.CampaignOpened, time: BaseTime.AddMinutes(1), payload: "{ not json"),
            Row(id: 1, kind: ActivityEventKind.CampaignOpened, time: BaseTime, campaignName: "Old"),
        };

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, JsonOptions);

        result.Events.Count.ShouldBe(2);
        result.Events.Select(item => item.ActivityEventId).ShouldBe([3, 1]);
    }

    /// <summary>Verifies a payload whose context family does not match the row kind is skipped.</summary>
    [Fact]
    public void BuildPage_SkipsRows_WhenContextFamilyDoesNotMatchKind()
    {
        var rows = new[]
        {
            Row(id: 2, kind: ActivityEventKind.CampaignOpened, time: BaseTime, campaignName: "C"),
            RawRow(
                id: 1,
                kind: ActivityEventKind.MemberJoined,
                time: BaseTime.AddDays(-1),
                payload: JsonSerializer.Serialize(
                    new JoinRequestContext { JoinRequestId = 9, RequesterDisplayName = "R" },
                    typeof(ClubActivityContext),
                    JsonOptions)),
        };

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, JsonOptions);

        result.Events.ShouldHaveSingleItem();
        result.Events[0].ActivityEventId.ShouldBe(2);
    }

    /// <summary>Verifies a persisted undefined kind is skipped rather than throwing out of the page build.</summary>
    [Fact]
    public void BuildPage_SkipsRows_WithUndefinedPersistedKind()
    {
        var rows = new[]
        {
            Row(id: 2, kind: ActivityEventKind.CampaignOpened, time: BaseTime.AddMinutes(1), campaignName: "Newest"),
            RawRow(
                id: 1,
                kind: (ActivityEventKind)999,
                time: BaseTime,
                payload: JsonSerializer.Serialize(
                    new CampaignLifecycleContext { CampaignId = 1, CampaignName = "C" },
                    typeof(ClubActivityContext),
                    JsonOptions)),
        };

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, JsonOptions);

        result.Events.ShouldHaveSingleItem();
        result.Events[0].ActivityEventId.ShouldBe(2);
        result.Events[0].Kind.ShouldBe(ActivityEventKind.CampaignOpened);
    }

    /// <summary>Verifies the cursor still points at the raw page boundary when a row is skipped.</summary>
    [Fact]
    public void BuildPage_SkippedRow_CursorPointsAtRawPageBoundary()
    {
        var rows = Enumerable.Range(1, ClubActivityFeedPolicy.PageSize + 1)
            .Select(index =>
            {
                // id 11 has a malformed payload; every other row is well formed.
                return index == 11
                    ? RawRow(id: index, kind: ActivityEventKind.CampaignOpened, time: BaseTime.AddMinutes(index - 1), payload: "{ broken")
                    : Row(id: index, kind: ActivityEventKind.CampaignOpened, time: BaseTime.AddMinutes(index - 1), campaignName: $"C{index}");
            })
            .ToList();

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, JsonOptions);

        // 19 projected rows (id 11 skipped) but the cursor is the raw last page row: id 2.
        result.Events.Count.ShouldBe(ClubActivityFeedPolicy.PageSize - 1);
        result.HasMore.ShouldBeTrue();
        result.NextCursor.ShouldNotBeNull();
        result.NextCursor.ActivityEventId.ShouldBe(2);
        result.NextCursor.OccurredAt.ShouldBe(BaseTime.AddMinutes(1));

        var second = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, result.NextCursor, JsonOptions);
        second.Events.ShouldHaveSingleItem();
        second.Events[0].ActivityEventId.ShouldBe(1);
    }

    /// <summary>Creates a well-formed event row with a payload for the given kind.</summary>
    private static ActivityEventEntity Row(
        long id,
        ActivityEventKind kind,
        DateTimeOffset time,
        string? campaignName = null,
        string? member = null,
        string? approvedBy = null)
    {
        ClubActivityContext context = kind switch
        {
            ActivityEventKind.CampaignOpened or ActivityEventKind.CampaignClosed => new CampaignLifecycleContext
            {
                CampaignId = 1,
                CampaignName = campaignName ?? "C",
            },
            ActivityEventKind.JoinRequestSubmitted => new JoinRequestContext
            {
                JoinRequestId = 1,
                RequesterDisplayName = "R",
            },
            ActivityEventKind.MemberJoined => new MembershipContext
            {
                MemberUserId = 99,
                MemberDisplayName = member ?? "M",
                ApprovedByActorName = approvedBy,
            },
            _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unsupported test kind."),
        };

        return new ActivityEventEntity
        {
            ActivityEventId = id,
            ClubId = 1,
            EventKind = kind,
            IsAdminOnly = ActivityEventPolicy.IsAdminOnly(kind),
            ActorUserId = 42,
            ActorDisplayName = "Actor",
            PayloadJson = JsonSerializer.Serialize(context, typeof(ClubActivityContext), JsonOptions),
            CreatedById = 42,
            CreatedAt = time,
        };
    }

    /// <summary>Creates an event row with an explicit payload string.</summary>
    private static ActivityEventEntity RawRow(
        long id,
        ActivityEventKind kind,
        DateTimeOffset time,
        string payload) =>
        new()
        {
            ActivityEventId = id,
            ClubId = 1,
            EventKind = kind,
            IsAdminOnly = ActivityEventPolicy.IsAdminOnly(kind),
            ActorUserId = 42,
            ActorDisplayName = "Actor",
            PayloadJson = payload,
            CreatedById = 42,
            CreatedAt = time,
        };
}
