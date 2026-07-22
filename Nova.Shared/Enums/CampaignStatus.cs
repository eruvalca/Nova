namespace Nova.Shared.Enums;

/// <summary>
/// Identifies whether a campaign is open for current mutations or closed for historical read-only access.
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
}
