

using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Services;

/// <summary>Close discovery is independent of Roster, Evaluate and Place filters.</summary>
public sealed record CampaignWorkspaceCloseState
{
    /// <summary>The literal participant search.</summary>
    public string? Search { get; init; }
    /// <summary>The selected authoritative blocker condition.</summary>
    public string? Blocker { get; init; }
    /// <summary>The Closed record's campaign-local terminal outcome filter.</summary>
    public string? Outcome { get; init; }
    /// <summary>The Closed participant whose inline history is selected.</summary>
    public long? ParticipantId { get; init; }
    /// <summary>The selected history's exclusive event cursor.</summary>
    public long? BeforeEventId { get; init; }
    /// <summary>The one-based 50-participant page.</summary>
    public int Page { get; init; } = 1;

    /// <summary>Normalizes a 50-row page to the shared discovery offset bound.</summary>
    public static int NormalizePage(int? page)
        => page is > 0 && (long)(page.Value - 1) * PlacementPageInput.DefaultPageSize <= int.MaxValue ? page.Value : 1;

    /// <summary>Normalizes bookmarked search text; invalid lengths show the unfiltered roster.</summary>
    public static string? NormalizeSearch(string? value)
    {
        var search = value?.Trim();
        return search is { Length: > 0 and <= CampaignRosterDiscoveryInput.MaximumSearchLength } ? search : null;
    }

    /// <summary>Normalizes bookmarked blocker tokens; unknown values show all participants.</summary>
    public static string? NormalizeBlocker(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "OUTCOMES" => CloseoutBlockerConditions.Outcomes,
        "ELIGIBILITY" => CloseoutBlockerConditions.Eligibility,
        "ARCHIVEDTEAMS" => CloseoutBlockerConditions.ArchivedTeams,
        _ => null,
    };

    /// <summary>Normalizes Closed discovery without exposing an Undecided final outcome.</summary>
    public static string? NormalizeOutcome(string? value) => value?.Trim().ToUpperInvariant() switch
    {
        "ASSIGNED" => "assigned",
        "NOTSELECTED" => "notselected",
        "WITHDRAWN" => "withdrawn",
        _ => null,
    };

    /// <summary>Preserves Close context through a correction journey.</summary>
    /// <param name="destination">The existing destination and other workspace state.</param>
    /// <param name="returnToClose">Whether to show the explicit correction return link.</param>
    /// <returns>The destination with independent Close state.</returns>
    public string Apply(string destination, bool returnToClose = false)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var parts = new List<string>();
        var page = NormalizePage(Page);
        if (page > 1) { parts.Add($"closePage={page.ToString(System.Globalization.CultureInfo.InvariantCulture)}"); }
        if (NormalizeSearch(Search) is { } search)
        {
            parts.Add($"closeSearch={Uri.EscapeDataString(search)}");
        }
        if (NormalizeBlocker(Blocker) is { } blocker)
        {
            parts.Add($"closeBlocker={Uri.EscapeDataString(blocker)}");
        }
        if (NormalizeOutcome(Outcome) is { } outcome) { parts.Add($"closeOutcome={outcome}"); }
        if (ParticipantId is > 0 and long participant)
        {
            parts.Add($"closeParticipant={participant.ToString(System.Globalization.CultureInfo.InvariantCulture)}");
            if (BeforeEventId is > 0 and long cursor) { parts.Add($"closeBeforeEventId={cursor.ToString(System.Globalization.CultureInfo.InvariantCulture)}"); }
        }
        if (returnToClose)
        {
            parts.Add("returnToClose=true");
        }
        if (parts.Count == 0) { return destination; }
        return destination + (destination.Contains('?', StringComparison.Ordinal) ? "&" : "?") + string.Join("&", parts);
    }
}
