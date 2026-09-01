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
            DashboardEndpoints.GetActivityUrl(input.ContinuationToken),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<DashboardActivityResult>(
            "The server returned an invalid club dashboard activity response.",
            IsValidActivity,
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
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidActivity(DashboardActivityResult result)
        => result is not null
            && result.Events is not null
            && result.Events.Count <= DashboardActivityResult.PageSize
            && result.Events.All(IsValidActivityItem)
            && (result.NextContinuationToken is null || !string.IsNullOrWhiteSpace(result.NextContinuationToken))
            && IsOrdered(result.Events);

    /// <summary>
    /// Validates one activity event row, including kind-specific field presence.
    /// </summary>
    /// <param name="item">The activity row to validate.</param>
    /// <returns><see langword="true"/> when the row is structurally valid for its kind.</returns>
    private static bool IsValidActivityItem(DashboardActivityItemDto item)
    {
        if (item is null || item.EventId <= 0 || item.EventAt == default || !Enum.IsDefined(item.Kind))
        {
            return false;
        }

        return item.Kind switch
        {
            DashboardActivityEventKind.CampaignDraftCreated
                or DashboardActivityEventKind.CampaignDraftDeleted
                or DashboardActivityEventKind.CampaignOpened
                or DashboardActivityEventKind.CampaignClosed
                or DashboardActivityEventKind.CampaignReopened
                => item.Context is CampaignActivityContextDto campaign && IsValid(campaign),
            DashboardActivityEventKind.PlacementAssigned
                or DashboardActivityEventKind.PlacementReassigned
                or DashboardActivityEventKind.PlacementOutcomeChanged
                => item.Context is PlacementActivityContextDto placement && IsValid(placement),
            DashboardActivityEventKind.JoinRequestSubmitted
                or DashboardActivityEventKind.JoinRequestCancelled
                or DashboardActivityEventKind.JoinRequestRejected
                or DashboardActivityEventKind.JoinRequestApproved
                => item.Context is JoinRequestActivityContextDto request && IsValid(request),
            DashboardActivityEventKind.MemberJoined
                or DashboardActivityEventKind.MemberPromoted
                or DashboardActivityEventKind.MemberDemoted
                or DashboardActivityEventKind.MemberRemoved
                or DashboardActivityEventKind.MemberLeft
                => item.Context is MembershipActivityContextDto membership && IsValid(membership),
            _ => false
        };
    }

    /// <summary>Validates campaign activity context.</summary>
    private static bool IsValid(CampaignActivityContextDto context)
        => !string.IsNullOrWhiteSpace(context.ActorDisplayName)
            && !string.IsNullOrWhiteSpace(context.CampaignName)
            && context.CampaignId is null or > 0;

    /// <summary>Validates placement activity context.</summary>
    private static bool IsValid(PlacementActivityContextDto context)
        => !string.IsNullOrWhiteSpace(context.ActorDisplayName)
            && !string.IsNullOrWhiteSpace(context.PlayerDisplayName)
            && !string.IsNullOrWhiteSpace(context.CampaignName)
            && context.PlayerId is null or > 0
            && context.PlayerCampaignAssignmentId is null or > 0
            && context.CampaignId is null or > 0
            && IsValid(context.Previous)
            && IsValid(context.Current);

    /// <summary>Validates one placement state snapshot.</summary>
    private static bool IsValid(PlacementSnapshotDto context)
        => Enum.IsDefined(context.Outcome) && context.TeamId is null or > 0;

    /// <summary>Validates administrator join-request activity context.</summary>
    private static bool IsValid(JoinRequestActivityContextDto context)
        => !string.IsNullOrWhiteSpace(context.ActorDisplayName)
            && !string.IsNullOrWhiteSpace(context.RequesterDisplayName)
            && context.RequesterUserId is null or > 0
            && context.ActionableRequestId is null or > 0;

    /// <summary>Validates membership activity context.</summary>
    private static bool IsValid(MembershipActivityContextDto context)
        => !string.IsNullOrWhiteSpace(context.MemberDisplayName)
            && context.MemberUserId is null or > 0;

    /// <summary>
    /// Verifies the portable activity ordering contract: <c>EventAt</c>, then event identifier must
    /// be non-increasing across adjacent events.
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
                && previous.EventId < current.EventId)
            {
                return false;
            }
        }

        return true;
    }
}
