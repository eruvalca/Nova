namespace Nova.Shared.Campaigns;

/// <summary>
/// Defines shared route constants for campaign command endpoints.
/// </summary>
public static class CampaignEndpoints
{
    /// <summary>
    /// The group prefix for campaign endpoints.
    /// </summary>
    public const string GroupPrefix = "/api/campaigns";

    /// <summary>
    /// Creates an Active campaign and its initial participation snapshot (POST).
    /// </summary>
    public const string Create = GroupPrefix;

    /// <summary>
    /// The relative creation path within <see cref="GroupPrefix"/>.
    /// </summary>
    public const string CreateRelative = "";

    /// <summary>
    /// The route name assigned to campaign creation.
    /// </summary>
    public const string CreateRouteName = "CreateCampaign";
}
