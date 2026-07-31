namespace Nova.Shared.Teams;

/// <summary>
/// Identifies an active assigned placement that would become ineligible for a proposed team
/// graduation-year change.
/// </summary>
public sealed record TeamGraduationYearBlockerItem
{
    /// <summary>
    /// Gets the player-campaign assignment that would become ineligible.
    /// </summary>
    public required long PlayerCampaignAssignmentId { get; init; }

    /// <summary>
    /// Gets the campaign containing the blocked placement.
    /// </summary>
    public required long CampaignId { get; init; }

    /// <summary>
    /// Gets the player whose graduation year is below the proposed team cutoff.
    /// </summary>
    public required long PlayerId { get; init; }

    /// <summary>
    /// Gets the player's graduation year.
    /// </summary>
    public required int PlayerGraduationYear { get; init; }
}
