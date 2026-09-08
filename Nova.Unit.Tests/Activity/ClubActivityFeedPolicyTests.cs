using System.Text.Json;
using Nova.Entities;
using Nova.Features.Activity;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Activity;
using Shouldly;

namespace Nova.Unit.Tests.Activity;

/// <summary>
/// Verifies the deterministic feed projection and keyset paging applied over loaded activity rows:
/// ordering with shared timestamps, role-shaped membership rows, malformed payload skipping, and
/// continuation cursors.
/// </summary>
public sealed class ClubActivityFeedPolicyTests
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    private static readonly DateTimeOffset _baseTime = new(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);

    /// <summary>Verifies equal-timestamp events use the identifier descending tie-breaker.</summary>
    [Fact]
    public void BuildPageOrdersEqualTimestampsByDescendingEventId()
    {
        var rows = new[]
        {
            Row(id: 2, kind: ActivityEventKind.CampaignOpened, time: _baseTime, campaignName: "Open"),
            Row(id: 1, kind: ActivityEventKind.CampaignClosed, time: _baseTime, campaignName: "Close"),
        };

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, _jsonOptions);

        result.Events.Select(item => item.ActivityEventId).ShouldBe([2, 1]);
    }

    /// <summary>Verifies the newest page contains the page size and reports continuation.</summary>
    [Fact]
    public void BuildPageReturnsPageSizeAndHasMoreWhenMoreRowsExist()
    {
        var rows = Enumerable.Range(0, ClubActivityFeedPolicy.PageSize + 5)
            .Select(index => Row(
                id: index + 1,
                kind: ActivityEventKind.CampaignOpened,
                time: _baseTime.AddMinutes(index),
                campaignName: $"C{index}"))
            .ToList();

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, _jsonOptions);

        result.Events.Count.ShouldBe(ClubActivityFeedPolicy.PageSize);
        result.HasMore.ShouldBeTrue();
        result.NextCursor.ShouldNotBeNull();
        result.Events[0].ActivityEventId.ShouldBe(ClubActivityFeedPolicy.PageSize + 5);
        // The cursor is the last raw row of the page: id 6 at AddMinutes(5).
        result.NextCursor.ActivityEventId.ShouldBe(6);
        result.NextCursor.OccurredAt.ShouldBe(_baseTime.AddMinutes(5));
    }

    /// <summary>Verifies a page with exactly the page size has no continuation.</summary>
    [Fact]
    public void BuildPageHasNoNextCursorWhenPageIsExactlyFull()
    {
        var rows = Enumerable.Range(0, ClubActivityFeedPolicy.PageSize)
            .Select(index => Row(
                id: index + 1,
                kind: ActivityEventKind.CampaignOpened,
                time: _baseTime.AddMinutes(index),
                campaignName: $"C{index}"))
            .ToList();

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, _jsonOptions);

        result.Events.Count.ShouldBe(ClubActivityFeedPolicy.PageSize);
        result.HasMore.ShouldBeFalse();
        result.NextCursor.ShouldBeNull();
    }

    /// <summary>Verifies the continuation cursor resumes after the oldest returned row of the previous page.</summary>
    [Fact]
    public void BuildPageApplyingNextCursorReturnsFollowingRows()
    {
        var rows = Enumerable.Range(0, ClubActivityFeedPolicy.PageSize + 3)
            .Select(index => Row(
                id: index + 1,
                kind: ActivityEventKind.CampaignOpened,
                time: _baseTime.AddMinutes(index),
                campaignName: $"C{index}"))
            .ToList();

        var first = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, _jsonOptions);
        var second = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, first.NextCursor, _jsonOptions);

        second.Events.Count.ShouldBe(3);
        second.HasMore.ShouldBeFalse();
        second.NextCursor.ShouldBeNull();
        second.Events.Select(item => item.ActivityEventId).ShouldBe([3, 2, 1]);
    }

    /// <summary>Verifies a cursor on a shared timestamp boundary is exclusive and deterministic.</summary>
    [Fact]
    public void BuildPageCursorIsExclusiveOnSharedTimestampBoundary()
    {
        var shared = _baseTime.AddMinutes(30);
        var rows = new[]
        {
            Row(id: 5, kind: ActivityEventKind.CampaignOpened, time: shared, campaignName: "C5"),
            Row(id: 4, kind: ActivityEventKind.CampaignOpened, time: shared, campaignName: "C4"),
            Row(id: 3, kind: ActivityEventKind.CampaignOpened, time: shared, campaignName: "C3"),
            Row(id: 2, kind: ActivityEventKind.CampaignOpened, time: _baseTime, campaignName: "C2"),
        };

        var cursor = new ClubActivityCursor(ActivityEventId: 4, OccurredAt: shared);
        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor, _jsonOptions);

        result.Events.Select(item => item.ActivityEventId).ShouldBe([3, 2]);
    }

    /// <summary>Verifies members do not see administrator-only rows.</summary>
    [Fact]
    public void BuildPageHidesAdminOnlyRowsForMembers()
    {
        var rows = new[]
        {
            Row(id: 2, kind: ActivityEventKind.JoinRequestSubmitted, time: _baseTime.AddMinutes(1)),
            Row(id: 1, kind: ActivityEventKind.MemberJoined, time: _baseTime, member: "M"),
        };

        var memberResult = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: false, cursor: null, _jsonOptions);
        var adminResult = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, _jsonOptions);

        memberResult.Events.ShouldHaveSingleItem();
        memberResult.Events[0].Kind.ShouldBe(ActivityEventKind.MemberJoined);

        adminResult.Events.Count.ShouldBe(2);
    }

    /// <summary>Verifies MemberJoined is role-shaped: members see no approving actor name and no
    /// top-level actor identity.</summary>
    [Fact]
    public void BuildPageShapesMemberJoinedForMemberViewer()
    {
        var rows = new[]
        {
            Row(
                id: 1,
                kind: ActivityEventKind.MemberJoined,
                time: _baseTime,
                member: "Sam Doe",
                approvedBy: "Jordan Lee"),
        };

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: false, cursor: null, _jsonOptions);

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
    public void BuildPageShapesMemberJoinedForAdminViewer()
    {
        var rows = new[]
        {
            Row(
                id: 1,
                kind: ActivityEventKind.MemberJoined,
                time: _baseTime,
                member: "Sam Doe",
                approvedBy: "Jordan Lee"),
        };

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, _jsonOptions);

        var context = result.Events.Single().Context.ShouldBeOfType<MembershipContext>();
        context.ApprovedByActorName.ShouldBe("Jordan Lee");
    }

    /// <summary>Verifies a malformed payload row is skipped rather than surfaced.</summary>
    [Fact]
    public void BuildPageSkipsRowsWithMalformedPayload()
    {
        var rows = new[]
        {
            Row(id: 3, kind: ActivityEventKind.CampaignOpened, time: _baseTime.AddMinutes(2), campaignName: "Good"),
            RawRow(id: 2, kind: ActivityEventKind.CampaignOpened, time: _baseTime.AddMinutes(1), payload: "{ not json"),
            Row(id: 1, kind: ActivityEventKind.CampaignOpened, time: _baseTime, campaignName: "Old"),
        };

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, _jsonOptions);

        result.Events.Count.ShouldBe(2);
        result.Events.Select(item => item.ActivityEventId).ShouldBe([3, 1]);
    }

    /// <summary>Verifies a payload whose context family does not match the row kind is skipped.</summary>
    [Fact]
    public void BuildPageSkipsRowsWhenContextFamilyDoesNotMatchKind()
    {
        var rows = new[]
        {
            Row(id: 2, kind: ActivityEventKind.CampaignOpened, time: _baseTime, campaignName: "C"),
            RawRow(
                id: 1,
                kind: ActivityEventKind.MemberJoined,
                time: _baseTime.AddDays(-1),
                payload: JsonSerializer.Serialize<ClubActivityContext>(new JoinRequestContext { JoinRequestId = 9, RequesterDisplayName = "R" }, _jsonOptions)),
        };

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, _jsonOptions);

        result.Events.ShouldHaveSingleItem();
        result.Events[0].ActivityEventId.ShouldBe(2);
    }

    /// <summary>Verifies a persisted undefined kind is skipped rather than throwing out of the page build.</summary>
    [Fact]
    public void BuildPageSkipsRowsWithUndefinedPersistedKind()
    {
        var rows = new[]
        {
            Row(id: 2, kind: ActivityEventKind.CampaignOpened, time: _baseTime.AddMinutes(1), campaignName: "Newest"),
            RawRow(
                id: 1,
                kind: (ActivityEventKind)999,
                time: _baseTime,
                payload: JsonSerializer.Serialize<ClubActivityContext>(new CampaignLifecycleContext { CampaignId = 1, CampaignName = "C" }, _jsonOptions)),
        };

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, _jsonOptions);

        result.Events.ShouldHaveSingleItem();
        result.Events[0].ActivityEventId.ShouldBe(2);
        result.Events[0].Kind.ShouldBe(ActivityEventKind.CampaignOpened);
    }

    /// <summary>Verifies the cursor still points at the raw page boundary when a row is skipped.</summary>
    [Fact]
    public void BuildPageSkippedRowCursorPointsAtRawPageBoundary()
    {
        var rows = Enumerable.Range(1, ClubActivityFeedPolicy.PageSize + 1)
            .Select(index =>
            {
                // id 11 has a malformed payload; every other row is well formed.
                return index == 11
                    ? RawRow(id: index, kind: ActivityEventKind.CampaignOpened, time: _baseTime.AddMinutes(index - 1), payload: "{ broken")
                    : Row(id: index, kind: ActivityEventKind.CampaignOpened, time: _baseTime.AddMinutes(index - 1), campaignName: $"C{index}");
            })
            .ToList();

        var result = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, cursor: null, _jsonOptions);

        // 19 projected rows (id 11 skipped) but the cursor is the raw last page row: id 2.
        result.Events.Count.ShouldBe(ClubActivityFeedPolicy.PageSize - 1);
        result.HasMore.ShouldBeTrue();
        result.NextCursor.ShouldNotBeNull();
        result.NextCursor.ActivityEventId.ShouldBe(2);
        result.NextCursor.OccurredAt.ShouldBe(_baseTime.AddMinutes(1));

        var second = ClubActivityFeedPolicy.BuildPage(rows, isAdmin: true, result.NextCursor, _jsonOptions);
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
            PayloadJson = JsonSerializer.Serialize<ClubActivityContext>(context, _jsonOptions),
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
