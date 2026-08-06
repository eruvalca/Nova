namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Defines shared route constants for campaign command and query endpoints.
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

    /// <summary>
    /// Gets the campaign-list route.
    /// </summary>
    public const string GetCampaignList = GroupPrefix;

    /// <summary>
    /// Gets the campaign-list route relative to the campaign group.
    /// </summary>
    public const string GetCampaignListRelative = "";

    /// <summary>
    /// Gets the route name assigned to the campaign list.
    /// </summary>
    public const string GetCampaignListRouteName = "GetCampaignList";

    /// <summary>
    /// Gets the campaign creation-setup route.
    /// </summary>
    public const string GetCreationSetup = $"{GroupPrefix}/creation-setup";

    /// <summary>
    /// Gets the creation-setup route relative to the campaign group.
    /// </summary>
    public const string GetCreationSetupRelative = "creation-setup";

    /// <summary>
    /// Gets the route name assigned to campaign creation setup.
    /// </summary>
    public const string GetCreationSetupRouteName = "GetCampaignCreationSetup";

    /// <summary>
    /// Builds a campaign-list URL from the accepted optional filters.
    /// </summary>
    /// <param name="status">The optional campaign status filter.</param>
    /// <param name="limit">The optional bounded result limit.</param>
    /// <returns>The campaign-list URL.</returns>
    public static string GetCampaignListUrl(string? status = null, int? limit = null)
    {
        var querySegments = new List<string>();
        var normalizedStatus = status?.Trim().ToLowerInvariant() switch
        {
            "active" => "active",
            "closed" => "closed",
            _ => null
        };

        if (normalizedStatus is not null)
        {
            querySegments.Add($"status={Uri.EscapeDataString(normalizedStatus)}");
        }

        if (limit is >= GetCampaignListInput.MinLimit and <= GetCampaignListInput.MaxLimit)
        {
            querySegments.Add($"limit={limit.Value}");
        }

        return querySegments.Count == 0
            ? GetCampaignList
            : $"{GetCampaignList}?{string.Join('&', querySegments)}";
    }
}
