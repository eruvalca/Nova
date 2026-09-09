using System.Globalization;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Builds query strings shared by placement routes; services validate input before using them.</summary>
internal static class PlacementQueryUrls
{
    internal static string WithPage(string path, PlacementPageInput input, long? teamId = null,
        int? graduationYear = null, string? search = null, string? eligibility = null)
    {
        var values = new List<string>
        {
            "page=" + (input.Page ?? 1).ToString(CultureInfo.InvariantCulture),
            "pageSize=" + (input.PageSize ?? PlacementPageInput.DefaultPageSize).ToString(CultureInfo.InvariantCulture),
        };
        if (teamId.HasValue)
        {
            values.Add("teamId=" + teamId.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (graduationYear.HasValue)
        {
            values.Add("graduationYear=" + graduationYear.Value.ToString(CultureInfo.InvariantCulture));
        }
        if (!string.IsNullOrWhiteSpace(search))
        {
            values.Add("search=" + Uri.EscapeDataString(search));
        }
        if (Enum.TryParse<EffectivePlacementEligibility>(eligibility, true, out var state) && Enum.IsDefined(state))
        {
            values.Add("eligibility=" + state);
        }
        return path + "?" + string.Join('&', values);
    }
}
