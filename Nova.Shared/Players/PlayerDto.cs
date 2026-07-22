using Nova.Shared.Enums;

namespace Nova.Shared.Players;

/// <summary>
/// A read-only projection of a player's permanent profile.
/// </summary>
public sealed record PlayerDto
{
    /// <summary>The player's unique identifier.</summary>
    public required long PlayerId { get; init; }

    /// <summary>The identifier of the club this player belongs to.</summary>
    public required long ClubId { get; init; }

    /// <summary>The player's first name.</summary>
    public required string FirstName { get; init; }

    /// <summary>The player's last name.</summary>
    public required string LastName { get; init; }

    /// <summary>The player's full name.</summary>
    public string FullName => $"{FirstName} {LastName}";

    /// <summary>The player's date of birth.</summary>
    public required DateOnly DateOfBirth { get; init; }

    /// <summary>The player's expected graduation year.</summary>
    public required int GraduationYear { get; init; }

    /// <summary>The player's gender, if recorded.</summary>
    public Gender? Gender { get; init; }

    /// <summary>The player's jersey number, if assigned.</summary>
    public int? JerseyNumber { get; init; }

    /// <summary>The player's current lifecycle status.</summary>
    public required LifecycleStatus LifecycleStatus { get; init; }
}
