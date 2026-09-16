using System.ComponentModel.DataAnnotations;

namespace Nova.SharedKernel.Features.Players;

/// <summary>
/// Input model for editing a player's permanent profile fields.
/// </summary>
public sealed record UpdatePlayerInput : PlayerProfileInput
{
    /// <summary>The identifier of the player to update.</summary>
    [Required, Range(1, long.MaxValue)]
    public required long PlayerId { get; init; }

}
