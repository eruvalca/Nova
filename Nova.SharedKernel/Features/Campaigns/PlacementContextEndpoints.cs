using System.Globalization;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Canonical placement-context read route shared by server and browser.</summary>
public static class PlacementContextEndpoints
{
    /// <summary>The context route relative to campaigns.</summary>
    public const string GetPlacementContextRelative = "{campaignId:long}/participants/{playerCampaignAssignmentId:long}/placement-context";

    /// <summary>The registered placement-context route name.</summary>
    public const string GetPlacementContextRouteName = "GetPlacementContext";

    /// <summary>Builds a bounded context read URL.</summary>
    public static Uri Url(GetPlacementContextInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var route = GetPlacementContextRelative.Replace("{campaignId:long}", input.CampaignId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal)
            .Replace("{playerCampaignAssignmentId:long}", input.PlayerCampaignAssignmentId.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
        var query = new List<string>();
        if (input.BeforeEventId is > 0 and long cursor) { query.Add($"beforeEventId={cursor.ToString(CultureInfo.InvariantCulture)}"); }
        if (input.RequireClosed is true) { query.Add("requireClosed=true"); }
        return new Uri($"{CampaignEndpoints.GroupPrefix}/{route}"
            + (query.Count > 0 ? "?" + string.Join("&", query) : string.Empty), UriKind.Relative);
    }
}
