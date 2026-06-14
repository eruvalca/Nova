using Microsoft.AspNetCore.Components;
using Nova.Shared.Clubs;

namespace Nova.UI.Features.Clubs.Pages;

/// <summary>
/// ClubAdmin page that lists the pending join requests for a club and lets the admin
/// approve or reject each one.
/// </summary>
/// <param name="clubJoinRequestService">The service for club join request operations.</param>
public partial class ClubAdminJoinRequests(IClubJoinRequestService clubJoinRequestService)
{
    /// <summary>
    /// The id of the club whose pending requests are shown. Bound from the route.
    /// </summary>
    [Parameter]
    public long ClubId { get; set; }

    /// <summary>
    /// The current pending requests, or <see langword="null"/> before the first load.
    /// </summary>
    private IReadOnlyList<ClubJoinRequestDto>? _requests;

    /// <summary>
    /// Whether the page is currently loading the request list.
    /// </summary>
    private bool _loading = true;

    /// <summary>
    /// A page-level error message, or <see langword="null"/> when no error.
    /// </summary>
    private string? _error;

    /// <summary>
    /// The id of the request currently being approved/rejected, or <see langword="null"/> when idle.
    /// </summary>
    private long? _processingRequestId;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadRequestsAsync();
    }

    /// <summary>
    /// Loads (or reloads) the pending join requests for <see cref="ClubId"/>.
    /// </summary>
    private async Task LoadRequestsAsync()
    {
        _loading = true;
        _error = null;

        var result = await clubJoinRequestService.GetClubJoinRequestsAsync(ClubId, ComponentCancellationToken);
        result.Switch(
            requests => _requests = requests,
            problem => _error = problem.Detail ?? "Failed to load join requests. Please try again.");

        _loading = false;
    }

    /// <summary>
    /// Approves the specified request, then reloads the list.
    /// </summary>
    /// <param name="requestId">The id of the request to approve.</param>
    private async Task HandleApproveAsync(long requestId)
    {
        _processingRequestId = requestId;
        _error = null;

        var succeeded = false;
        var result = await clubJoinRequestService.ApproveJoinRequestAsync(requestId, ComponentCancellationToken);
        result.Switch(
            _ => succeeded = true,
            problem => _error = problem.Detail ?? "Failed to approve the request. Please try again.");

        _processingRequestId = null;

        if (succeeded)
        {
            await LoadRequestsAsync();
        }
    }

    /// <summary>
    /// Rejects the specified request, then reloads the list.
    /// </summary>
    /// <param name="requestId">The id of the request to reject.</param>
    private async Task HandleRejectAsync(long requestId)
    {
        _processingRequestId = requestId;
        _error = null;

        var succeeded = false;
        var result = await clubJoinRequestService.RejectJoinRequestAsync(requestId, ComponentCancellationToken);
        result.Switch(
            _ => succeeded = true,
            problem => _error = problem.Detail ?? "Failed to reject the request. Please try again.");

        _processingRequestId = null;

        if (succeeded)
        {
            await LoadRequestsAsync();
        }
    }
}
