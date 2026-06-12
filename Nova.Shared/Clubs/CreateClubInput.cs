using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Clubs;

/// <summary>
/// Input model for creating a new club.
/// </summary>
/// <param name="Name">The display name for the new club.</param>
/// <param name="City">The city the club is based in.</param>
/// <param name="State">The state the club is based in.</param>
public sealed record CreateClubInput(
    [Required, MaxLength(200)] string Name,
    [Required, MaxLength(100)] string City,
    [Required, MaxLength(100)] string State);
