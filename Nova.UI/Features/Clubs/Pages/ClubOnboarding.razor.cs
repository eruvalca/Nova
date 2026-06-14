using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Clubs;
using Nova.Shared.Results;
using Nova.Shared.Security;

namespace Nova.UI.Features.Clubs.Pages;

/// <summary>
/// The club onboarding page. Shown to authenticated users who have a profile photo
/// but have not yet joined or created a club. Presents options to create a new club
/// or search for and request to join an existing one.
/// </summary>
/// <param name="clubJoinRequestService">The service for club join request operations.</param>
/// <param name="navigationManager">The navigation manager for full-document redirects.</param>
/// <param name="authenticationStateProvider">The authentication state provider used to check current user claims.</param>
public partial class ClubOnboarding(
    IClubJoinRequestService clubJoinRequestService,
    NavigationManager navigationManager,
    AuthenticationStateProvider authenticationStateProvider)
{
    /// <summary>
    /// Whether the page is currently loading initial data.
    /// </summary>
    private bool _loading = true;

    /// <summary>
    /// The current user's pending join request, or <see langword="null"/> if none exists.
    /// </summary>
    private ClubJoinRequestDto? _pendingRequest;

    /// <summary>
    /// An error message to display at the page level, or <see langword="null"/> when no error.
    /// </summary>
    private string? _error;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        // Club members must not access the onboarding page — redirect them to the home page.
        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        if (authState.User.HasClaim(c => c.Type == NovaClaimTypes.ClubId))
        {
            navigationManager.NavigateTo("/", replace: true);
            return;
        }

        _loading = true;
        var result = await clubJoinRequestService.GetCurrentUserPendingRequestAsync(ComponentCancellationToken);
        result.Switch(
            dto => _pendingRequest = dto,
            problem =>
            {
                // NotFound means no pending request — this is the expected empty state.
                if (problem.Kind != ServiceProblemKind.NotFound)
                {
                    _error = problem.Detail ?? "Failed to load your request status. Please refresh and try again.";
                }
                _pendingRequest = null;
            });
        _loading = false;
    }

    /// <summary>
    /// Handles a successful club creation. Performs a full-document navigation to the
    /// cookie-refresh endpoint so the new <c>nova:club_id</c> claim takes effect.
    /// </summary>
    /// <param name="club">The newly created club.</param>
    private void HandleClubCreated(ClubDto club)
    {
        navigationManager.NavigateTo(ClubEndpoints.Complete + "?returnUrl=/", forceLoad: true);
    }

    /// <summary>
    /// Handles a successfully submitted join request. Updates page state to show the
    /// pending request card.
    /// </summary>
    /// <param name="dto">The created join request.</param>
    private void HandleJoinRequested(ClubJoinRequestDto dto)
    {
        _pendingRequest = dto;
        _error = null;
    }

    /// <summary>
    /// Handles a cancelled join request. Resets page state to show the create/search form.
    /// </summary>
    private void HandleRequestCancelled()
    {
        _pendingRequest = null;
        _error = null;
    }

    /// <summary>
    /// Handles a "search again" request raised after a rejection. Clears the pending request state
    /// so the create/search view is shown again.
    /// </summary>
    private void HandleSearchAgain()
    {
        _pendingRequest = null;
        _error = null;
    }
}
