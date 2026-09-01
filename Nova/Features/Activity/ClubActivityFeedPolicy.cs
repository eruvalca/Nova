using System.Text.Json;
using Nova.Entities;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;

namespace Nova.Features.Activity;

/// <summary>
/// Produces the role-shaped club activity feed from ordered, loaded event rows: applies the
/// role visibility filter, projects each row onto its family context, and applies deterministic
/// keyset paging over (OccurredAt, ActivityEventId).
/// </summary>
internal static class ClubActivityFeedPolicy
{
    /// <summary>
    /// The fixed page size of the feed.
    /// </summary>
    internal const int PageSize = GetClubActivityInput.PageSize;

    /// <summary>
    /// Builds one page from the loaded rows of the club. Rows are expected to be in no particular
    /// order; this policy sorts, filters, and pages deterministically.
    /// </summary>
    /// <param name="entityRows">The event rows for the club (already visibility-filtered).</param>
    /// <param name="isAdmin">Whether the requesting user may see administrator-only rows.</param>
    /// <param name="cursor">The keyset cursor, or null for the newest page.</param>
    /// <param name="jsonOptions">The serializer options used to read persisted payloads.</param>
    /// <returns>The page of activity items with a continuation cursor.</returns>
    internal static ClubActivityResult BuildPage(
        IEnumerable<ActivityEventEntity> entityRows,
        bool isAdmin,
        ClubActivityCursor? cursor,
        JsonSerializerOptions jsonOptions)
    {
        var visible = entityRows
            .Where(row => isAdmin || !row.IsAdminOnly)
            .OrderByDescending(row => row.CreatedAt)
            .ThenByDescending(row => row.ActivityEventId)
            .ToList();

        IEnumerable<ActivityEventEntity> page;
        if (cursor is null)
        {
            page = visible;
        }
        else
        {
            page = visible.Where(row =>
                row.CreatedAt < cursor.OccurredAt
                || (row.CreatedAt == cursor.OccurredAt && row.ActivityEventId < cursor.ActivityEventId));
        }

        var pageRows = page
            .Take(PageSize + 1)
            .ToList();

        var hasMore = pageRows.Count > PageSize;
        var visiblePage = hasMore ? pageRows.Take(PageSize).ToList() : pageRows;

        var events = visiblePage
            .Select(row => Project(row, isAdmin, jsonOptions))
            .Where(static item => item is not null)
            .Cast<ClubActivityItemDto>()
            .ToList();

        var lastPageRow = visiblePage.Count > 0 ? visiblePage[^1] : null;
        var nextCursor = hasMore && lastPageRow is not null
            ? new ClubActivityCursor(lastPageRow.ActivityEventId, lastPageRow.CreatedAt)
            : null;

        return new ClubActivityResult(events, hasMore, nextCursor);
    }

    /// <summary>
    /// Projects one row onto the role-shaped DTO. The DTO is null when the persisted payload could
    /// not be read or does not match the row's kind; malformed rows are skipped rather than
    /// surfaced, and the skip affects the page cursor (the next page refetches from the cursor
    /// itself so a skipped row does not strand following pages).
    /// </summary>
    internal static ClubActivityItemDto? Project(
        ActivityEventEntity row,
        bool isAdmin,
        JsonSerializerOptions jsonOptions)
    {
        ClubActivityContext context;
        try
        {
            context = JsonSerializer.Deserialize<ClubActivityContext>(row.PayloadJson, jsonOptions)!;
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            // A missing/unknown discriminator can surface as NotSupportedException when the
            // serializer attempts to instantiate the abstract base type.
            return null;
        }

        if (context is null || !ActivityEventPolicy.ContextMatchesKind(row.EventKind, context))
        {
            return null;
        }

        var projected = context;
        long? actorUserId = row.ActorUserId;
        string? actorDisplayName = row.ActorDisplayName;
        if (row.EventKind == ActivityEventKind.MemberJoined && context is MembershipContext membership)
        {
            // The persisted payload carries the approving-admin snapshot; members see the join
            // only (subject-led without the approving actor), administrators see the approval
            // action with the actor.
            var isAdminView = isAdmin;
            projected = membership with { ApprovedByActorName = isAdminView ? membership.ApprovedByActorName : null };
            if (!isAdminView)
            {
                actorUserId = null;
                actorDisplayName = null;
            }
        }

        return new ClubActivityItemDto
        {
            Kind = row.EventKind,
            ActivityEventId = row.ActivityEventId,
            OccurredAt = row.CreatedAt,
            ActorUserId = actorUserId,
            ActorDisplayName = actorDisplayName,
            Context = projected,
        };
    }
}
