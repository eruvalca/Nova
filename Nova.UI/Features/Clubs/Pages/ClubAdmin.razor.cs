using Microsoft.AspNetCore.Components;
using Nova.Shared.Clubs;
using Nova.Shared.Results;

namespace Nova.UI.Features.Clubs.Pages;

/// <summary>
/// Club admin page that lists pending join requests for a club and lets admins approve
/// or reject each one.
/// </summary>
/// <param name="clubJoinRequestService">The service for club join request operations.</param>
/// <param name="navigationManager">The navigation manager used for access-denied redirects.</param>
public partial class ClubAdmin(
    IClubJoinRequestService clubJoinRequestService,
    NavigationManager navigationManager)
{
    /// <summary>
    /// The id of the club whose pending requests are shown. Bound from the route.
    /// </summary>
    [Parameter]
    public long ClubId { get; set; }

    /// <summary>
    /// The current pending requests.
    /// Persisted across the prerender → interactive handoff so initial results are reused.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<ClubJoinRequestDto>? Requests { get; set; }

    /// <summary>
    /// Whether the page is currently loading the request list.
    /// </summary>
    private bool _loading = true;

    /// <summary>
    /// A page-level error message, or <see langword="null"/> when no error.
    /// Persisted across the prerender → interactive handoff.
    /// </summary>
    [PersistentState]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Whether initial data has already been loaded during prerendering.
    /// Persisted to prevent a duplicate API call when the interactive runtime attaches.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <summary>
    /// A non-null view over <see cref="Requests"/> for safe rendering.
    /// </summary>
    private IReadOnlyList<ClubJoinRequestDto> RequestList => Requests ?? [];

    /// <summary>
    /// The id of the request currently being approved/rejected, or <see langword="null"/> when idle.
    /// </summary>
    private long? _processingRequestId;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        // Skip the data fetch on the interactive pass — state was already loaded during prerender.
        if (Initialized)
        {
            _loading = false;
            return;
        }

        _loading = true;
        ErrorMessage = null;

        var result = await clubJoinRequestService.GetClubJoinRequestsAsync(ClubId, ComponentCancellationToken);
        var shouldReturn = false;
        result.Switch(
            requests => Requests = requests,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    shouldReturn = true;
                    return;
                }

                ErrorMessage = problem.Detail ?? "Failed to load join requests. Please try again.";
            });

        if (shouldReturn)
        {
            return;
        }

        Initialized = true;
        _loading = false;
    }

    /// <summary>
    /// Loads (or reloads) the pending join requests for <see cref="ClubId"/>.
    /// </summary>
    /// <returns>A task that completes once loading has finished.</returns>
    private async Task LoadRequestsAsync()
    {
        _loading = true;
        ErrorMessage = null;

        var result = await clubJoinRequestService.GetClubJoinRequestsAsync(ClubId, ComponentCancellationToken);
        var shouldReturn = false;
        result.Switch(
            requests => Requests = requests,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    shouldReturn = true;
                    return;
                }

                ErrorMessage = problem.Detail ?? "Failed to load join requests. Please try again.";
            });

        if (shouldReturn)
        {
            return;
        }

        _loading = false;
    }

    /// <summary>
    /// Approves the specified request, then reloads the list.
    /// </summary>
    /// <param name="requestId">The id of the request to approve.</param>
    /// <returns>A task that completes once processing and optional reload are finished.</returns>
    private async Task HandleApproveAsync(long requestId)
    {
        _processingRequestId = requestId;
        ErrorMessage = null;

        var succeeded = false;
        var shouldReturn = false;
        var result = await clubJoinRequestService.ApproveJoinRequestAsync(requestId, ComponentCancellationToken);
        result.Switch(
            _ => succeeded = true,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    shouldReturn = true;
                    return;
                }

                ErrorMessage = problem.Detail ?? "Failed to approve the request. Please try again.";
            });

        _processingRequestId = null;

        if (shouldReturn)
        {
            return;
        }

        if (succeeded)
        {
            await LoadRequestsAsync();
        }
    }

    /// <summary>
    /// Rejects the specified request, then reloads the list.
    /// </summary>
    /// <param name="requestId">The id of the request to reject.</param>
    /// <returns>A task that completes once processing and optional reload are finished.</returns>
    private async Task HandleRejectAsync(long requestId)
    {
        _processingRequestId = requestId;
        ErrorMessage = null;

        var succeeded = false;
        var shouldReturn = false;
        var result = await clubJoinRequestService.RejectJoinRequestAsync(requestId, ComponentCancellationToken);
        result.Switch(
            _ => succeeded = true,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    shouldReturn = true;
                    return;
                }

                ErrorMessage = problem.Detail ?? "Failed to reject the request. Please try again.";
            });

        _processingRequestId = null;

        if (shouldReturn)
        {
            return;
        }

        if (succeeded)
        {
            await LoadRequestsAsync();
        }
    }

    /// <summary>
    /// Navigates to the access-denied page when authorization fails at the service boundary.
    /// </summary>
    private void NavigateToAccessDenied() => navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
}
