using Microsoft.AspNetCore.Components;
using Nova.Shared.Clubs;

namespace Nova.UI.Features.Clubs.Components;

/// <summary>
/// Displays a pending club join request with a cancel button.
/// </summary>
/// <param name="clubJoinRequestService">The service for club join request operations.</param>
public partial class PendingJoinRequestCard(IClubJoinRequestService clubJoinRequestService)
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
    /// Whether a cancellation is currently in progress.
    /// </summary>
    private bool _cancelling;

    /// <summary>
    /// An error message to display, or <see langword="null"/> when no error.
    /// </summary>
    private string? _error;

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
            success =>
            {
                _ = OnRequestCancelled.InvokeAsync();
            },
            problem =>
            {
                _error = problem.Detail ?? "Failed to cancel the request. Please try again.";
            });

        _cancelling = false;
    }
}
