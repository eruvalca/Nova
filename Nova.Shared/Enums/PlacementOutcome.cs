namespace Nova.Shared.Enums;

/// <summary>
/// Identifies the placement decision for a player's participation in a campaign.
/// </summary>
public enum PlacementOutcome
{
    /// <summary>
    /// Indicates that no final placement decision has been made.
    /// </summary>
    Undecided = 0,

    /// <summary>
    /// Indicates that the player has been assigned to a team.
    /// </summary>
    Assigned = 1,

    /// <summary>
    /// Indicates that the player was not selected for a team.
    /// </summary>
    NotSelected = 2,

    /// <summary>
    /// Indicates that the player withdrew from the campaign.
    /// </summary>
    Withdrawn = 3,
}
