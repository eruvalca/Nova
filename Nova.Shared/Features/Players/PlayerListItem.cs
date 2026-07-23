using Nova.Shared.Enums;

namespace Nova.Shared.Features.Players;

/// <summary>
/// Represents a single player row in the roster query response.
/// </summary>
public sealed record PlayerListItem
{
    /// <summary>
    /// Gets the player identifier.
    /// </summary>
    public required long PlayerId { get; init; }

    /// <summary>
    /// Gets the player's display name.
    /// </summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Gets the player's expected graduation year.
    /// </summary>
    public required int GraduationYear { get; init; }

    /// <summary>
    /// Gets the player's current lifecycle status.
    /// </summary>
    public required LifecycleStatus LifecycleStatus { get; init; }

    /// <summary>
    /// Gets the active-campaign tags currently applied to the player.
    /// </summary>
    public required IReadOnlyList<PlayerRosterTagItem> CurrentTags { get; init; }

    /// <summary>
    /// Gets the active campaign names this player currently participates in.
    /// </summary>
    public required IReadOnlyList<string> ActiveCampaigns { get; init; }

    /// <summary>
    /// Gets the timestamp when the player was added to the roster.
    /// </summary>
    public required DateTimeOffset JoinedAt { get; init; }
}

/// <summary>
/// Represents one tag pill in the roster response.
/// </summary>
/// <param name="PlayerTagId">The tag-definition identifier.</param>
/// <param name="Name">The tag display name.</param>
/// <param name="Color">The tag color token.</param>
public sealed record PlayerRosterTagItem(long PlayerTagId, string Name, string Color);
