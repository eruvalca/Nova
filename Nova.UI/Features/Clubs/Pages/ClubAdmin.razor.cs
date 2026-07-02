using Microsoft.AspNetCore.Components;
using Nova.Shared.Account;
using Nova.Shared.Clubs;
using Nova.Shared.Results;
using Nova.UI.Components;

namespace Nova.UI.Features.Clubs.Pages;

/// <summary>
/// Server-rendered club administration page for reviewing join requests and managing the club roster.
/// </summary>
/// <param name="clubAdminService">The service used to fetch club and roster data.</param>
/// <param name="clubMemberService">The service used to promote members to ClubAdmin.</param>
/// <param name="clubJoinRequestService">The service used to manage join requests.</param>
/// <param name="navigationManager">The navigation manager used for access-denied redirects.</param>
public partial class ClubAdmin(
    IClubAdminService clubAdminService,
    IClubMemberService clubMemberService,
    IClubJoinRequestService clubJoinRequestService,
    NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// Gets or sets the club identifier supplied by the route.
    /// </summary>
    [Parameter]
    public long ClubId { get; set; }

    /// <summary>
    /// Gets or sets the join request identifier supplied by a form POST.
    /// </summary>
    [SupplyParameterFromForm]
    private long? RequestId { get; set; }

    /// <summary>
    /// Gets or sets the target member identifier supplied by a form POST.
    /// </summary>
    [SupplyParameterFromForm]
    private long? MemberUserId { get; set; }

    /// <summary>
    /// Gets the current club summary used to render the overview card.
    /// </summary>
    private ClubAdminSummaryDto? _summary;

    /// <summary>
    /// Gets the current club roster used to render the members and admins card.
    /// </summary>
    private IReadOnlyList<ClubMemberDetailDto> _roster = [];

    /// <summary>
    /// Gets the current pending join requests used to render the join requests card.
    /// </summary>
    private IReadOnlyList<ClubJoinRequestDto> _requests = [];

    /// <summary>
    /// Gets or sets the current page error message.
    /// </summary>
    private string? _error;

    /// <summary>
    /// Gets or sets the current page status message.
    /// </summary>
    private string? _status;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadAsync();
    }

    /// <summary>
    /// Loads the club summary, roster, and pending join requests for the current club.
    /// </summary>
    /// <returns>A task that completes once the data has been refreshed.</returns>
    private async Task LoadAsync()
    {
        var summaryResult = await clubAdminService.GetClubAdminSummaryAsync(ClubId, ComponentCancellationToken);
        summaryResult.Switch(
            summary => _summary = summary,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    return;
                }

                _error = problem.Detail ?? "Failed to load the club summary.";
            });

        var rosterResult = await clubAdminService.GetClubRosterAsync(ClubId, ComponentCancellationToken);
        rosterResult.Switch(
            roster => _roster = roster,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    return;
                }

                _error = problem.Detail ?? "Failed to load the club roster.";
            });

        var requestsResult = await clubJoinRequestService.GetClubJoinRequestsAsync(ClubId, ComponentCancellationToken);
        requestsResult.Switch(
            requests => _requests = requests,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    return;
                }

                _error = problem.Detail ?? "Failed to load the join requests.";
            });
    }

    /// <summary>
    /// Approves the join request identified by the current form POST.
    /// </summary>
    /// <returns>A task that completes once the operation has finished and the data has reloaded.</returns>
    private async Task HandleApproveAsync()
    {
        if (RequestId is null)
        {
            return;
        }

        _error = null;
        _status = null;

        var shouldReturn = false;
        var result = await clubJoinRequestService.ApproveJoinRequestAsync(RequestId.Value, ComponentCancellationToken);
        result.Switch(
            _ => _status = "Join request approved.",
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    shouldReturn = true;
                    return;
                }

                _error = problem.Detail ?? "Failed to approve the request. Please try again.";
            });

        if (shouldReturn)
        {
            return;
        }

        await LoadAsync();
    }

    /// <summary>
    /// Rejects the join request identified by the current form POST.
    /// </summary>
    /// <returns>A task that completes once the operation has finished and the data has reloaded.</returns>
    private async Task HandleRejectAsync()
    {
        if (RequestId is null)
        {
            return;
        }

        _error = null;
        _status = null;

        var shouldReturn = false;
        var result = await clubJoinRequestService.RejectJoinRequestAsync(RequestId.Value, ComponentCancellationToken);
        result.Switch(
            _ => _status = "Join request rejected.",
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    shouldReturn = true;
                    return;
                }

                _error = problem.Detail ?? "Failed to reject the request. Please try again.";
            });

        if (shouldReturn)
        {
            return;
        }

        await LoadAsync();
    }

    /// <summary>
    /// Promotes the member identified by the current form POST to ClubAdmin.
    /// </summary>
    /// <returns>A task that completes once the operation has finished and the data has reloaded.</returns>
    private async Task HandlePromoteAsync()
    {
        if (MemberUserId is null)
        {
            return;
        }

        _error = null;
        _status = null;

        var shouldReturn = false;
        var result = await clubMemberService.AssignClubAdminAsync(
            new AssignAdminInput { TargetUserId = MemberUserId.Value },
            ComponentCancellationToken);
        result.Switch(
            _ => _status = "Member promoted to admin.",
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    shouldReturn = true;
                    return;
                }

                _error = problem.Detail ?? "Failed to promote the member to admin.";
            });

        if (shouldReturn)
        {
            return;
        }

        await LoadAsync();
    }

    /// <summary>
    /// Demotes the member identified by the current form POST from ClubAdmin.
    /// </summary>
    /// <returns>A task that completes once the operation has finished and the data has reloaded.</returns>
    private async Task HandleDemoteAsync()
    {
        if (MemberUserId is null)
        {
            return;
        }

        _error = null;
        _status = null;

        var shouldReturn = false;
        var result = await clubAdminService.DemoteClubAdminAsync(
            new DemoteAdminInput { TargetUserId = MemberUserId.Value },
            ComponentCancellationToken);
        result.Switch(
            _ => _status = "Member demoted from admin.",
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    shouldReturn = true;
                    return;
                }

                _error = problem.Detail ?? "Failed to demote the member from admin.";
            });

        if (shouldReturn)
        {
            return;
        }

        await LoadAsync();
    }

    /// <summary>
    /// Navigates to the access-denied page when authorization fails at the service boundary.
    /// </summary>
    private void NavigateToAccessDenied() => navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
}
