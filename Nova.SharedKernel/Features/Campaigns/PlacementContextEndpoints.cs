using System.Globalization;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Canonical placement-context read route shared by server and browser.</summary>
public static class PlacementContextEndpoints
{
    /// <summary>The context route relative to campaigns.</summary>
    public const string Relative = "{campaignId:long}/participants/{playerCampaignAssignmentId:long}/placement-context";

    /// <summary>Builds a bounded context read URL.</summary>
    public static Uri Url(GetPlacementContextInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        return new Uri(string.Create(CultureInfo.InvariantCulture,
        $"{CampaignEndpoints.GroupPrefix}/{input.CampaignId}/participants/{input.PlayerCampaignAssignmentId}/placement-context")
        + (input.BeforeEventId is long cursor ? $"?beforeEventId={cursor.ToString(CultureInfo.InvariantCulture)}" : string.Empty), UriKind.Relative);
    }
}
