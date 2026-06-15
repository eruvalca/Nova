using Microsoft.AspNetCore.Components;
using Nova.Shared.Clubs;
using Nova.Shared.Results;

namespace Nova.UI.Features.Clubs.Pages;

/// <summary>
/// Displays the signed-in member's club details and roster.
/// </summary>
/// <param name="clubDetailService">The service that loads club detail data.</param>
/// <param name="navigationManager">The navigation manager used for access-denied redirects.</param>
public partial class ClubDetail(
    IClubDetailService clubDetailService,
    NavigationManager navigationManager)
{
    /// <summary>
    /// The id of the club to display. Bound from the route.
    /// </summary>
    [Parameter]
    public long ClubId { get; set; }

    /// <summary>
    /// The loaded club details, or <see langword="null"/> when unavailable.
    /// </summary>
    private ClubDetailDto? _club;

    /// <summary>
    /// A page-level error message, or <see langword="null"/> when no error occurred.
    /// </summary>
    private string? _error;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        await LoadClubAsync();
    }

    /// <summary>
    /// Loads the club detail payload for the current route.
    /// </summary>
    /// <returns>A task that completes once the load and state update are finished.</returns>
    private async Task LoadClubAsync()
    {
        _error = null;

        var result = await clubDetailService.GetClubDetailAsync(ClubId, ComponentCancellationToken);
        result.Switch(
            detail => _club = detail,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    NavigateToAccessDenied();
                    return;
                }

                _error = problem.Detail ?? "Failed to load club details. Please refresh and try again.";
            });
    }

    /// <summary>
    /// Navigates to the access-denied page when authorization fails at the service boundary.
    /// </summary>
    private void NavigateToAccessDenied()
    {
        navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
    }
}
