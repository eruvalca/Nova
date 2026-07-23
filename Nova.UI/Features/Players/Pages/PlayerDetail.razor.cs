using Microsoft.AspNetCore.Components;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Nova.UI.Components;

namespace Nova.UI.Features.Players.Pages;

/// <summary>
/// Displays one player's permanent profile and campaign history.
/// </summary>
/// <param name="playerDetailService">The player-detail query service.</param>
/// <param name="navigationManager">The navigation manager used for access-denied redirects.</param>
public partial class PlayerDetail(
    IPlayerDetailService playerDetailService,
    NavigationManager navigationManager) : NovaComponentBase
{
    /// <summary>
    /// Gets or sets the target player identifier from the route.
    /// </summary>
    [Parameter]
    public long PlayerId { get; set; }

    /// <summary>
    /// Gets or sets the optional return URL query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "returnUrl")]
    private string? ReturnUrl { get; set; }

    /// <summary>
    /// The loaded player detail payload.
    /// </summary>
    private PlayerDetailDto? _detail;

    /// <summary>
    /// The page-level error message.
    /// </summary>
    private string? _error;

    /// <summary>
    /// The normalized return URL used by the back link.
    /// </summary>
    private string? _returnUrl;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        _returnUrl = string.IsNullOrWhiteSpace(ReturnUrl) ? "/players" : ReturnUrl;
        var result = await playerDetailService.GetPlayerDetailAsync(PlayerId, ComponentCancellationToken);
        result.Switch(
            detail => _detail = detail,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                _error = problem.Detail ?? "Could not load player details.";
            });
    }
}
