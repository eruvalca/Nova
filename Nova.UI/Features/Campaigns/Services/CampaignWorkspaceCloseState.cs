

namespace Nova.UI.Features.Campaigns.Services;

/// <summary>Close discovery is independent of Roster, Evaluate and Place filters.</summary>
public sealed record CampaignWorkspaceCloseState
{
    /// <summary>The literal participant search.</summary>
    public string? Search { get; init; }
    /// <summary>The selected authoritative blocker condition.</summary>
    public string? Blocker { get; init; }
    /// <summary>The one-based 50-participant page.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Preserves Close context through a correction journey.</summary>
    /// <param name="destination">The existing destination and other workspace state.</param>
    /// <param name="returnToClose">Whether to show the explicit correction return link.</param>
    /// <returns>The destination with independent Close state.</returns>
    public string Apply(string destination, bool returnToClose = false)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var parts = new List<string>();
        if (Page > 1) { parts.Add($"closePage={Page.ToString(System.Globalization.CultureInfo.InvariantCulture)}"); }
        if (Search is not null)
        {
            parts.Add($"closeSearch={Uri.EscapeDataString(Search)}");
        }
        if (Blocker is not null)
        {
            parts.Add($"closeBlocker={Uri.EscapeDataString(Blocker)}");
        }
        if (returnToClose)
        {
            parts.Add("returnToClose=true");
        }
        if (parts.Count == 0) { return destination; }
        return destination + (destination.Contains('?', StringComparison.Ordinal) ? "&" : "?") + string.Join("&", parts);
    }
}
