namespace Nova.Shared.Features.Players;

/// <summary>
/// Represents a single player row in the roster query response.
/// </summary>
/// <param name="PlayerId">The player identifier.</param>
/// <param name="DisplayName">The player's display name.</param>
/// <param name="JoinedAt">The timestamp when the player was created in the club roster.</param>
public sealed record PlayerListItem(long PlayerId, string DisplayName, DateTimeOffset JoinedAt);
