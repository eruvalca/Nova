using Nova.Features.Dashboard;
using Nova.Shared.Features.Dashboard;
using Shouldly;

namespace Nova.Unit.Tests.Dashboard;

/// <summary>
/// Verifies the pure, deterministic merge policy for the club dashboard activity feed.
/// </summary>
public sealed class DashboardActivityFeedPolicyTests
{
    private static readonly DateTimeOffset Base = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>
    /// Verifies cross-kind merge order: newest timestamp first, then kind rank descending, then
    /// event identifier descending.
    /// </summary>
    [Fact]
    public void OrderAndBound_OrdersByEventAtThenKindRankThenEventId_Descending()
    {
        var rows = new List<DashboardActivityEventRow>
        {
            Row(DashboardActivityEventKind.NoteAdded, 1, Base),
            Row(DashboardActivityEventKind.CampaignReopened, 2, Base),
            Row(DashboardActivityEventKind.PlacementSet, 3, Base),
            Row(DashboardActivityEventKind.TagApplied, 4, Base.AddMinutes(1)),
            Row(DashboardActivityEventKind.CampaignClosed, 5, Base.AddMinutes(1)),
        };

        var result = DashboardActivityFeedPolicy.OrderAndBound(rows, limit: 10);

        result.Select(row => (row.Kind, row.EventId)).ShouldBe(
        [
            (DashboardActivityEventKind.CampaignClosed, 5L),
            (DashboardActivityEventKind.TagApplied, 4L),
            (DashboardActivityEventKind.CampaignReopened, 2L),
            (DashboardActivityEventKind.PlacementSet, 3L),
            (DashboardActivityEventKind.NoteAdded, 1L)
        ]);
    }

    /// <summary>
    /// Verifies the exact kind-rank tie-break when timestamps are equal.
    /// </summary>
    [Fact]
    public void OrderAndBound_UsesKindRankDescending_WhenTimestampsEqual()
    {
        var rows = new List<DashboardActivityEventRow>
        {
            Row(DashboardActivityEventKind.NoteAdded, 1, Base),
            Row(DashboardActivityEventKind.CampaignReopened, 2, Base),
            Row(DashboardActivityEventKind.TagApplied, 3, Base),
            Row(DashboardActivityEventKind.CampaignClosed, 4, Base),
            Row(DashboardActivityEventKind.PlacementSet, 5, Base),
        };

        var result = DashboardActivityFeedPolicy.OrderAndBound(rows, limit: 10);

        result.Select(row => row.Kind).ShouldBe(
        [
            DashboardActivityEventKind.CampaignReopened,
            DashboardActivityEventKind.CampaignClosed,
            DashboardActivityEventKind.PlacementSet,
            DashboardActivityEventKind.TagApplied,
            DashboardActivityEventKind.NoteAdded
        ]);
    }

    /// <summary>
    /// Verifies the event-identifier descending tie-break when both timestamp and kind are equal.
    /// </summary>
    [Fact]
    public void OrderAndBound_UsesEventIdDescending_WhenTimestampAndKindEqual()
    {
        var rows = new List<DashboardActivityEventRow>
        {
            Row(DashboardActivityEventKind.NoteAdded, 1, Base),
            Row(DashboardActivityEventKind.NoteAdded, 3, Base),
            Row(DashboardActivityEventKind.NoteAdded, 2, Base),
        };

        var result = DashboardActivityFeedPolicy.OrderAndBound(rows, limit: 10);

        result.Select(row => row.EventId).ShouldBe([3L, 2L, 1L]);
    }

    /// <summary>
    /// Verifies the merged result is bounded to the requested limit.
    /// </summary>
    [Fact]
    public void OrderAndBound_ReturnsAtMostLimitRows()
    {
        var rows = Enumerable.Range(1, 10)
            .Select(index => Row(DashboardActivityEventKind.NoteAdded, index, Base.AddMinutes(index)))
            .ToList();

        var result = DashboardActivityFeedPolicy.OrderAndBound(rows, limit: 3);

        result.Count.ShouldBe(3);
        result.Select(row => row.EventId).ShouldBe([10L, 9L, 8L]);
    }

    /// <summary>
    /// Verifies empty input produces an empty result.
    /// </summary>
    [Fact]
    public void OrderAndBound_ReturnsEmpty_ForEmptyInput()
    {
        var result = DashboardActivityFeedPolicy.OrderAndBound([], limit: 10);

        result.ShouldBeEmpty();
    }

    /// <summary>
    /// Builds a minimal activity row carrying only the ordering keys used by the policy.
    /// </summary>
    /// <param name="kind">The event kind.</param>
    /// <param name="eventId">The per-kind entity identifier.</param>
    /// <param name="eventAt">When the event occurred.</param>
    /// <returns>A minimal activity row.</returns>
    private static DashboardActivityEventRow Row(
        DashboardActivityEventKind kind,
        long eventId,
        DateTimeOffset eventAt)
        => new(
            kind,
            eventId,
            eventAt,
            ActorUserId: 1,
            CampaignId: 1,
            CampaignName: "Campaign",
            PlayerCampaignAssignmentId: null,
            PlayerDisplayName: null,
            TagName: null,
            PlacementOutcome: null,
            LifecycleEventType: null);
}
