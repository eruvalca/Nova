namespace Nova.Shared.Features.Activity;

/// <summary>
/// The opaque key of the newest returned activity row, carried by <see cref="ClubActivityResult.NextCursor"/>
/// and passed back as <see cref="GetClubActivityInput"/> to fetch the next page.
/// </summary>
/// <param name="ActivityEventId">The newest returned event identifier.</param>
/// <param name="OccurredAt">The newest returned occurrence time.</param>
public sealed record ClubActivityCursor(long ActivityEventId, DateTimeOffset OccurredAt);
