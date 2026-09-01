namespace Nova.Shared.Features.Activity;

/// <summary>
/// The opaque key of the oldest returned activity row in the last page, carried by
/// <see cref="ClubActivityResult.NextCursor"/> and passed back as <see cref="GetClubActivityInput"/>
/// to fetch the next page. Because the feed can skip rows in projection (invalid or
/// administrator-only payloads), the cursor marks the page boundary, not the newest row.
/// </summary>
/// <param name="ActivityEventId">The cursor event identifier.</param>
/// <param name="OccurredAt">The cursor occurrence time.</param>
public sealed record ClubActivityCursor(long ActivityEventId, DateTimeOffset OccurredAt);
