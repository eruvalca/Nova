using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Enums;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Pages;

/// <summary>
/// Owns the Place destination's URL-backed discovery state and its canonical workspace route.
/// </summary>
public partial class CampaignWorkspace
{
    /// <summary>
    /// Gets or sets the incoming Place search query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "placementSearch")]
    private string? PlacementSearchQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming Place section query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "placementEligibility")]
    private string? PlacementEligibilityQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming comma-separated Place graduation-years query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "placementYears")]
    private string? PlacementYearsQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming comma-separated Place tag-identifier query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "placementTags")]
    private string? PlacementTagsQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming Place campaign-local outcome query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "placementOutcome")]
    private string? PlacementOutcomeQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming Place campaign-local team query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "placementTeamId")]
    private long? PlacementTeamIdQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming Place sort-field query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "placementSortBy")]
    private string? PlacementSortByQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming Place sort-direction query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "placementSortDirection")]
    private string? PlacementSortDirectionQuery { get; set; }

    /// <summary>
    /// Gets or sets the incoming Place page-number query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "placementPage")]
    private int? PlacementPageQuery { get; set; }

    /// <summary>
    /// The applied Place discovery state reflected in the workspace URL. Place keeps its own namespaced
    /// query keys so its section, search, and filters never leak into the Roster destination's state.
    /// </summary>
    private CampaignWorkspacePlacementState _placementState = new();

    /// <summary>
    /// Re-derives the applied Place discovery state from the incoming query parameters.
    /// </summary>
    private void ApplyPlacementQueryState()
        => _placementState = CampaignWorkspaceUrlState.ParsePlacement(
            PlacementSearchQuery,
            PlacementEligibilityQuery,
            PlacementYearsQuery,
            PlacementTagsQuery,
            PlacementOutcomeQuery,
            PlacementTeamIdQuery,
            PlacementSortByQuery,
            PlacementSortDirectionQuery,
            PlacementPageQuery);

    /// <summary>
    /// Gets the canonical Place workspace URL, carrying evaluation context and the current Roster context so a
    /// switch between destinations preserves both.
    /// </summary>
    private string PlaceUrl => CampaignWorkspaceUrlState.WithEvaluationContext(
        BuildPlaceUrl(_placementState, PlacementParticipantQuery), EvaluationState);

    /// <summary>
    /// Builds the canonical Place URL for the supplied discovery state, preserving the Roster context, the
    /// selected participant, and the Evaluate return affordance.
    /// </summary>
    /// <param name="state">The Place discovery state to serialize.</param>
    /// <param name="placementParticipantId">The participant Place has selected.</param>
    /// <returns>The relative Place workspace URL with evaluation context applied.</returns>
    private string BuildPlaceUrl(CampaignWorkspacePlacementState state, long? placementParticipantId)
        => CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(
            CampaignId, state, _filters, _selectedParticipantId, placementParticipantId, ReturnToEvaluationQuery == true);

    /// <summary>
    /// Applies a Place discovery or page change raised by the Place surface and pushes the matching canonical URL.
    /// </summary>
    /// <param name="next">The Place state to apply.</param>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnPlacementStateChangedAsync(CampaignWorkspacePlacementState next)
    {
        _placementState = next;
        return NavigateToPlaceAsync(BuildPlaceUrl(next, PlacementParticipantQuery));
    }

    /// <summary>
    /// Pushes a canonical Place URL when it differs from the current location.
    /// </summary>
    /// <param name="targetUrl">The relative Place URL to navigate to.</param>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task NavigateToPlaceAsync(string targetUrl)
    {
        var currentPathAndQuery = new Uri(navigationManager.Uri).PathAndQuery;
        if (!string.Equals(targetUrl, currentPathAndQuery, StringComparison.Ordinal))
        {
            navigationManager.NavigateTo(CampaignWorkspaceUrlState.WithEvaluationContext(targetUrl, EvaluationState));
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Navigates to the Place route in response to a closeout blocker drill-down, optionally filtered to
    /// participants still missing a campaign-local decision.
    /// </summary>
    /// <param name="unresolvedOnly">Whether the target Place URL should filter to participants without a campaign-local decision.</param>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task OnReviewUnresolvedAsync(bool unresolvedOnly)
    {
        var url = unresolvedOnly
            ? CampaignWorkspaceUrlState.BuildReviewUnresolvedUrl(CampaignId, _filters, _selectedParticipantId)
            : CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(CampaignId, new(), _filters, _selectedParticipantId);
        navigationManager.NavigateTo(url);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Composes the Place return URL a player-detail round trip should come back to, including the Roster
    /// context and evaluation lookup the panel does not own.
    /// </summary>
    /// <param name="placementParticipantId">The participant Place has selected, or <see langword="null"/> to clear it.</param>
    /// <returns>The relative Place workspace URL with Roster and evaluation context applied.</returns>
    private string BuildPlaceReturnUrl(long? placementParticipantId)
        => CampaignWorkspaceUrlState.WithEvaluationContext(
            BuildPlaceUrl(_placementState, placementParticipantId), EvaluationState);

    /// <summary>
    /// Drops the Place section and page for a Closed campaign, which has no placement-eligibility axis and
    /// whose Place read ignores the section, and flags the URL so a link carrying either is repaired.
    /// </summary>
    /// <param name="state">The applied Place discovery state.</param>
    /// <returns>The normalized state, or the supplied state when nothing applies.</returns>
    private CampaignWorkspacePlacementState NormalizePlacementFilters(CampaignWorkspacePlacementState state)
    {
        if (_detail?.Status != CampaignStatus.Closed || state.Eligibility is null)
        {
            return state;
        }

        _dropClosedPlaceEligibility = true;
        return state with { Eligibility = null, Page = 1 };
    }
}
