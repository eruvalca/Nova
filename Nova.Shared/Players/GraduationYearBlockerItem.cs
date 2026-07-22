namespace Nova.Shared.Players;

/// <summary>
/// Identifies a single campaign placement that would become ineligible if the player's graduation
/// year were changed to the proposed value.
/// </summary>
public sealed record GraduationYearBlockerItem
{
    /// <summary>The player-campaign participation row that would become ineligible.</summary>
    public required long PlayerCampaignAssignmentId { get; init; }

    /// <summary>The campaign the placement belongs to.</summary>
    public required long CampaignId { get; init; }

    /// <summary>The team the player is currently assigned to.</summary>
    public required long TeamId { get; init; }

    /// <summary>The team's graduation year requirement (must be &lt;= player's graduation year).</summary>
    public required int TeamGraduationYear { get; init; }
}
