namespace Nova.Shared.Enums;

/// <summary>
/// Identifies the lifecycle stage of a campaign.
/// </summary>
public enum CampaignStatus
{
    /// <summary>
    /// Indicates that the campaign remains open for active workflow mutations.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Indicates that the campaign is closed and retained for historical read-only access.
    /// </summary>
    Closed = 1,

    /// <summary>
    /// Indicates that the campaign is administrator-only preparation data and has not enrolled players.
    /// </summary>
    Draft = 2,
}
