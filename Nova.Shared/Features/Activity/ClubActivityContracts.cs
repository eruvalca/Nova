using Nova.Shared.Enums;

namespace Nova.Shared.Features.Activity;

/// <summary>
/// One durable club activity row. The payload family is carried by <see cref="Context"/> whose
/// derived type is selected by <see cref="Kind"/>, and display names are snapshots so rows remain
/// readable after the actor or subject is removed or renamed.
/// </summary>
public sealed record ClubActivityItemDto
{
    /// <summary>
    /// Gets the event kind.
    /// </summary>
    public required ActivityEventKind Kind { get; init; }

    /// <summary>
    /// Gets the monotonically increasing activity event identifier used as the final ordering
    /// tie-break with <see cref="OccurredAt"/>.
    /// </summary>
    public required long ActivityEventId { get; init; }

    /// <summary>
    /// Gets when the event occurred.
    /// </summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// Gets the identifier of the user who performed the action.
    /// </summary>
    public required long ActorUserId { get; init; }

    /// <summary>
    /// Gets the stored actor display-name snapshot.
    /// </summary>
    public required string ActorDisplayName { get; init; }

    /// <summary>
    /// Gets the family-shaped structured event payload.
    /// </summary>
    public required ClubActivityContext Context { get; init; }
}

/// <summary>
/// A single role-shaped page of club activity, ordered newest-first with a stable keyset cursor.
/// </summary>
/// <param name="Events">The newest activity events, ordered newest-first.</param>
/// <param name="HasMore"><see langword="true"/> when at least one further event exists.</param>
/// <param name="NextCursor">The continuation cursor, or <see langword="null"/> when the feed is exhausted.</param>
public sealed record ClubActivityResult(
    IReadOnlyList<ClubActivityItemDto> Events,
    bool HasMore,
    ClubActivityCursor? NextCursor);
