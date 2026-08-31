using Nova.Shared.Enums;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;
using Nova.Shared.Validation;

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

    /// <inheritdoc />
    public async Task<ServiceResult<DashboardActivityResult>> GetActivityAsync(
        GetDashboardActivityInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        using var response = await http.GetAsync(
            input.ContinuationToken is null && input.Limit is not null
                ? $"{DashboardEndpoints.GetActivity}?limit={input.Limit.Value}"
                : DashboardEndpoints.GetActivityUrl(input.ContinuationToken),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var requestedLimit = input.Limit ?? DashboardActivityResult.PageSize;
        return await response.Content.ReadRequiredJsonAsync<DashboardActivityResult>(
            "The server returned an invalid club dashboard activity response.",
            result => IsValidActivity(result, requestedLimit),
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
            && IsValidTeamCounts(result.Teams)
            && IsValidAdminAttention(result.AdminAttention);

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
    /// Validates the optional administrator attention counts.
    /// </summary>
    /// <param name="attention">The administrator attention counts, or <see langword="null"/> for non-administrators.</param>
    /// <returns><see langword="true"/> when present fields are non-negative and the optional identifier is positive.</returns>
    private static bool IsValidAdminAttention(AdminAttentionDto? attention)
        => attention is null
            || (attention.PendingJoinRequestCount >= 0
                && attention.UnresolvedPlacementCount >= 0
                && (attention.FirstUnresolvedCampaignId is null || attention.FirstUnresolvedCampaignId > 0));

    /// <summary>
    /// Determines whether a value is a relative, same-origin path (a leading slash without a URI scheme).
    /// </summary>
    /// <param name="url">The URL to check.</param>
    /// <returns><see langword="true"/> when the URL is a relative path.</returns>
    private static bool IsRelativePath(string? url)
        => !string.IsNullOrWhiteSpace(url)
            && url.StartsWith('/')
            && !url.Contains("://");

    /// <summary>
    /// Validates that a decoded activity payload is bounded, populated, ordered newest-first, and
    /// kind-specific fields are present for each event.
    /// </summary>
    /// <param name="result">The decoded activity result.</param>
    /// <param name="requestedLimit">The bound the client requested.</param>
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidActivity(DashboardActivityResult result, int requestedLimit)
        => result is not null
            && result.Events is not null
            && result.Events.Count <= requestedLimit
            && result.Events.All(IsValidActivityItem)
            && IsOrdered(result.Events);

    /// <summary>
    /// Validates one activity event row, including kind-specific field presence.
    /// </summary>
    /// <param name="item">The activity row to validate.</param>
    /// <returns><see langword="true"/> when the row is structurally valid for its kind.</returns>
    private static bool IsValidActivityItem(DashboardActivityItemDto item)
    {
        if (item is null || item.EventId <= 0 || item.EventAt == default)
        {
            return false;
        }
        if (item.Context is null)
        {
            if (!Enum.IsDefined(item.Kind) || item.ActorUserId <= 0 || string.IsNullOrWhiteSpace(item.ActorDisplayName) || item.CampaignId <= 0 || string.IsNullOrWhiteSpace(item.CampaignName))
            {
                return false;
            }

            return item.Kind switch
            {
                DashboardActivityEventKind.NoteAdded => item.PlayerCampaignAssignmentId is > 0 && !string.IsNullOrWhiteSpace(item.PlayerDisplayName),
                DashboardActivityEventKind.TagApplied => item.PlayerCampaignAssignmentId is > 0 && !string.IsNullOrWhiteSpace(item.PlayerDisplayName) && !string.IsNullOrWhiteSpace(item.TagName),
                DashboardActivityEventKind.PlacementSet => item.PlayerCampaignAssignmentId is > 0 && !string.IsNullOrWhiteSpace(item.PlayerDisplayName) && item.PlacementOutcome is not null,
                DashboardActivityEventKind.CampaignClosed => item.LifecycleEventType == CampaignLifecycleEventType.Closed,
                DashboardActivityEventKind.CampaignReopened => item.LifecycleEventType == CampaignLifecycleEventType.Reopened,
                _ => false
            };
        }
        return item.Context switch
        {
            CampaignActivityContextDto c => !string.IsNullOrWhiteSpace(c.ActorDisplayName) && !string.IsNullOrWhiteSpace(c.CampaignName),
            PlacementActivityContextDto p => !string.IsNullOrWhiteSpace(p.ActorDisplayName) && !string.IsNullOrWhiteSpace(p.PlayerDisplayName),
            JoinRequestActivityContextDto j => !string.IsNullOrWhiteSpace(j.ActorDisplayName) && !string.IsNullOrWhiteSpace(j.RequesterDisplayName),
            MembershipActivityContextDto m => !string.IsNullOrWhiteSpace(m.MemberDisplayName),
            _ => false
        };
    }

    /// <summary>
    /// Verifies the portable activity ordering contract: <c>EventAt</c>, then kind rank, then event
    /// identifier must all be non-increasing across adjacent events.
    /// </summary>
    /// <param name="events">The activity rows to verify.</param>
    /// <returns><see langword="true"/> when every adjacent pair satisfies the ordering contract.</returns>
    private static bool IsOrdered(IReadOnlyList<DashboardActivityItemDto> events)
    {
        for (var index = 1; index < events.Count; index++)
        {
            var previous = events[index - 1];
            var current = events[index];
            if (previous.EventAt < current.EventAt)
            {
                return false;
            }

            if (previous.EventAt == current.EventAt
                && (int)previous.Kind < (int)current.Kind)
            {
                return false;
            }

            if (previous.EventAt == current.EventAt
                && previous.Kind == current.Kind
                && previous.EventId < current.EventId)
            {
                return false;
            }
        }

        return true;
    }
}
