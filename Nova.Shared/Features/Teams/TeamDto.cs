using Nova.Shared.Enums;

namespace Nova.Shared.Features.Teams;

/// <summary>
/// Represents the permanent profile and lifecycle state of a team.
/// </summary>
public sealed record TeamDto
{
    /// <summary>
    /// Gets the team's unique identifier.
    /// </summary>
    public required long TeamId { get; init; }

    /// <summary>
    /// Gets the identifier of the club that owns the team.
    /// </summary>
    public required long ClubId { get; init; }

    /// <summary>
    /// Gets the team's display name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the minimum player graduation year eligible for the team.
    /// </summary>
    public required int GraduationYear { get; init; }

    /// <summary>
    /// Gets the team's lifecycle state.
    /// </summary>
    public required LifecycleStatus LifecycleStatus { get; init; }
}
