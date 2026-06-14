using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Clubs;

/// <summary>
/// Input model for creating a new club.
/// </summary>
public sealed record CreateClubInput
{
    /// <summary>The display name for the new club.</summary>
    [Required, NotWhitespace, MaxLength(200)]
    public required string Name { get; init; }

    /// <summary>The city the club is based in.</summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string City { get; init; }

    /// <summary>The state the club is based in.</summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string State { get; init; }
}
