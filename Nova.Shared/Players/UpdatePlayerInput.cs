using System.ComponentModel.DataAnnotations;
using Nova.Shared.Enums;
using Nova.Shared.Validation;

namespace Nova.Shared.Players;

/// <summary>
/// Input model for editing a player's permanent profile fields.
/// </summary>
public sealed record UpdatePlayerInput
{
    /// <summary>The identifier of the player to update.</summary>
    [Required, Range(1, long.MaxValue)]
    public required long PlayerId { get; init; }

    /// <summary>The player's first name.</summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string FirstName { get; init; }

    /// <summary>The player's last name.</summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string LastName { get; init; }

    /// <summary>The player's date of birth.</summary>
    [Required]
    public required DateOnly DateOfBirth { get; init; }

    /// <summary>The player's expected graduation year.</summary>
    [Required, Range(2000, 2100)]
    public required int GraduationYear { get; init; }

    /// <summary>The player's gender. Optional.</summary>
    public Gender? Gender { get; init; }

    /// <summary>The player's jersey number. Optional.</summary>
    [Range(0, 9999)]
    public int? JerseyNumber { get; init; }
}
