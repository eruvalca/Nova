namespace Nova.Shared.Enums;

/// <summary>
/// Identifies lifecycle transitions that are durably recorded for a campaign.
/// </summary>
public enum CampaignLifecycleEventType
{
    /// <summary>
    /// Indicates that the campaign was closed.
    /// </summary>
    Closed = 0,

    /// <summary>
    /// Indicates that the campaign was reopened.
    /// </summary>
    Reopened = 1,
}
