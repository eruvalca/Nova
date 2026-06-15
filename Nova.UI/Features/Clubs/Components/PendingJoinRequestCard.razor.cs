using Microsoft.AspNetCore.Components;
using Nova.Shared.Clubs;
using Nova.Shared.Enums;

namespace Nova.UI.Features.Clubs.Components;

/// <summary>
/// Displays a pending club join request with a cancel button, and polls every 15 seconds for a
/// terminal (Approved/Rejected) status. On approval it offers a CTA to complete onboarding; on
/// rejection it offers a "search again" action.
/// </summary>
/// <param name="clubJoinRequestService">The service for club join request operations.</param>
/// <param name="navigationManager">The navigation manager for the full-document completion redirect.</param>
public partial class PendingJoinRequestCard(
    IClubJoinRequestService clubJoinRequestService,
    NavigationManager navigationManager)
{
    /// <summary>
    /// The pending join request to display. Required.
    /// </summary>
    [Parameter, System.Diagnostics.CodeAnalysis.NotNull]
    public ClubJoinRequestDto? Request { get; set; }

    /// <summary>
    /// Invoked when the user successfully cancels the join request.
    /// </summary>
    [Parameter]
    public EventCallback OnRequestCancelled { get; set; }

    /// <summary>
    /// Invoked when, after a rejection, the user asks to search for another club.
    /// </summary>
    [Parameter]
    public EventCallback OnSearchAgainRequested { get; set; }

    /// <summary>
    /// Whether a cancellation is currently in progress.
    /// </summary>
    private bool _cancelling;

    /// <summary>
    /// Whether polling has detected that the request was approved.
    /// </summary>
    private bool _isApproved;

    /// <summary>
    /// Whether polling has detected that the request was rejected.
    /// </summary>
    private bool _isRejected;

    /// <summary>
    /// The club name captured from the approved request, for the approval message.
    /// </summary>
    private string? _approvedClubName;

    /// <summary>
    /// The 15-second status polling timer, or <see langword="null"/> before initialization.
    /// </summary>
    private PeriodicTimer? _pollingTimer;

    /// <summary>
    /// An error message to display, or <see langword="null"/> when no error.
    /// </summary>
    private string? _error;

    /// <inheritdoc />
    protected override void OnInitialized()
    {
        // Reflect the initial status passed in via the Request parameter.
        if (Request is { Status: RequestStatus.Approved })
        {
            _isApproved = true;
            _approvedClubName = Request.ClubName;
            return;
        }

        if (Request is { Status: RequestStatus.Rejected })
        {
            _isRejected = true;
            return;
        }

        // Only poll while the request is still pending. Fire-and-forget; cancellation is cooperative
        // via ComponentCancellationToken and the timer is disposed in DisposeAsyncCore.
        _ = PollStatusAsync();
    }

    /// <summary>
    /// Polls the current user's request status every 15 seconds until a terminal status is detected
    /// or the component is disposed.
    /// </summary>
    private async Task PollStatusAsync()
    {
        _pollingTimer = new PeriodicTimer(TimeSpan.FromSeconds(15));
        try
        {
            while (await _pollingTimer.WaitForNextTickAsync(ComponentCancellationToken))
            {
                var result = await clubJoinRequestService.GetCurrentUserPendingRequestAsync(ComponentCancellationToken);

                var terminal = false;
                result.Switch(
                    dto =>
                    {
                        if (dto.Status == RequestStatus.Approved)
                        {
                            _isApproved = true;
                            _approvedClubName = dto.ClubName;
                            terminal = true;
                        }
                        else if (dto.Status == RequestStatus.Rejected)
                        {
                            _isRejected = true;
                            terminal = true;
                        }
                    },
                    _ => { /* NotFound or transient error: keep polling */ });

                if (terminal)
                {
                    _pollingTimer.Dispose();
                    await InvokeAsync(StateHasChanged);
                    break;
                }
            }
        }
        catch (OperationCanceledException)
        {
            // Component disposed — stop polling silently.
        }
    }

    /// <summary>
    /// Handles the cancel button click: calls the service to delete the request and invokes
    /// <see cref="OnRequestCancelled"/> on success.
    /// </summary>
    private async Task HandleCancelAsync()
    {
        _cancelling = true;
        _error = null;

        var result = await clubJoinRequestService.CancelJoinRequestAsync(Request!.ClubJoinRequestId, ComponentCancellationToken);
        result.Switch(
            success => _ = OnRequestCancelled.InvokeAsync(),
            problem => _error = problem.Detail ?? "Failed to cancel the request. Please try again.");

        _cancelling = false;
    }

    /// <summary>
    /// Navigates (full document) to the onboarding completion endpoint so the new ClubId claim takes effect.
    /// </summary>
    private void HandleCompleteOnboarding() => navigationManager.NavigateTo(ClubEndpoints.Complete + "?returnUrl=/", forceLoad: true);

    /// <summary>
    /// Raises <see cref="OnSearchAgainRequested"/> so the parent returns to the search/create view.
    /// </summary>
    private async Task HandleSearchAgainAsync() => await OnSearchAgainRequested.InvokeAsync();

    /// <inheritdoc />
    protected override ValueTask DisposeAsyncCore()
    {
        _pollingTimer?.Dispose();
        _pollingTimer = null;
        return ValueTask.CompletedTask;
    }
}
