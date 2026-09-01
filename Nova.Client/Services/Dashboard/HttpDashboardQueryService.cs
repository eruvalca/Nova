using Nova.Shared.Enums;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;

namespace Nova.Client.Services.Dashboard;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="IDashboardQueryService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpDashboardQueryService(HttpClient http) : IDashboardQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubDashboardResult>> GetDashboardAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(DashboardEndpoints.GetSummary, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<ClubDashboardResult>(
            "The server returned an invalid club dashboard response.",
            IsValidDashboard,
            cancellationToken);
    }

    /// <summary>
    /// Validates the structural invariants of a club dashboard summary success payload.
    /// </summary>
    /// <param name="result">The deserialized dashboard payload.</param>
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidDashboard(ClubDashboardResult result)
        => result is not null
            && result.ActiveCampaigns is not null
            && result.ActiveCampaigns.Count <= ClubDashboardResult.ActiveCampaignMaxCount
            && result.ActiveCampaigns.All(IsValidCard)
            && IsValidRosterCounts(result.Roster)
            && IsValidTeamCounts(result.Teams);

    /// <summary>
    /// Validates one active campaign card from a successful dashboard response.
    /// </summary>
    /// <param name="card">The active campaign card.</param>
    /// <returns><see langword="true"/> when all card invariants hold.</returns>
    private static bool IsValidCard(ActiveCampaignCardDto card)
        => card is not null
            && card.CampaignId > 0
            && !string.IsNullOrWhiteSpace(card.Name)
            && !string.IsNullOrWhiteSpace(card.SeasonName)
            && card.StartDate != default
            && (card.PlannedEndDate is null || card.PlannedEndDate >= card.StartDate)
            && card.ParticipantCount >= 0
            && card.UnresolvedCount >= 0
            && card.UnresolvedCount <= card.ParticipantCount
            && card.Status is CampaignStatus.Active or CampaignStatus.Closed
            && IsRelativePath(card.WorkspaceUrl);

    /// <summary>
    /// Validates the active/archived player counts.
    /// </summary>
    /// <param name="roster">The roster counts.</param>
    /// <returns><see langword="true"/> when all counts are non-negative.</returns>
    private static bool IsValidRosterCounts(RosterCountsDto roster)
        => roster is not null && roster.ActivePlayers >= 0 && roster.ArchivedPlayers >= 0;

    /// <summary>
    /// Validates the active/archived team counts.
    /// </summary>
    /// <param name="teams">The team counts.</param>
    /// <returns><see langword="true"/> when all counts are non-negative.</returns>
    private static bool IsValidTeamCounts(TeamCountsDto teams)
        => teams is not null && teams.ActiveTeams >= 0 && teams.ArchivedTeams >= 0;

    /// <summary>
    /// Determines whether a value is a relative, same-origin path (a leading slash without a URI scheme).
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <returns><see langword="true"/> when the URL is a relative path.</returns>
    private static bool IsRelativePath(string? url)
        => !string.IsNullOrWhiteSpace(url)
            && url.StartsWith('/')
            && !url.Contains("://");
}
