using Nova.Shared.Enums;

namespace Nova.Shared.Teams;

/// <summary>
/// Represents one team row in the tenant-scoped roster.
/// </summary>
public sealed record TeamRosterItem
{
    /// <summary>
    /// Gets the team's unique identifier.
    /// </summary>
    public required long TeamId { get; init; }

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

    /// <summary>
    /// Gets the number of assigned players in active campaigns.
    /// </summary>
    public required int ActivePlacementCount { get; init; }
}
