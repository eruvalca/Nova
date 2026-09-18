
using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Nova.UI.Components;
using Nova.UI.Features.Players.Services;

namespace Nova.UI.Features.Players.Pages;

/// <summary>
/// Displays one player's permanent profile, lifecycle state, current traits, and expandable campaign history.
/// Members reach the shared manual edit route; existing lifecycle and history presentation is retained.
/// </summary>
/// <param name="playerDetailService">The player-detail query service.</param>
/// <param name="playerLifecycleService">The player archive/restore service.</param>
/// <param name="authenticationStateProvider">The authentication state provider.</param>
/// <param name="navigationManager">The navigation manager used for access-denied redirects.</param>
public partial class PlayerDetail(
    IPlayerDetailService playerDetailService,
    IPlayerLifecycleService playerLifecycleService,
    AuthenticationStateProvider authenticationStateProvider,
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
    /// The page-level error message shown when loading fails.
    /// </summary>
    private string? _error;

    /// <summary>
    /// The mutation-level error message shown when an admin action fails.
    /// </summary>
    private string? _mutationError;

    /// <summary>
    /// The success status message shown after a successful mutation.
    /// </summary>
    private string? _statusMessage;

    /// <summary>
    /// Indicates whether the detail is currently loading.
    /// </summary>
    private bool _isLoading;

    /// <summary>
    /// Indicates whether the player was not found.
    /// </summary>
    private bool _isNotFound;

    /// <summary>
    /// Indicates whether a mutation is in progress.
    /// </summary>
    private bool _isMutating;

    /// <summary>
    /// Indicates whether the current user may commit player lifecycle mutations. The server's gate is
    /// club membership, so the record offers what an approved member may actually do.
    /// </summary>
    private bool _canManagePlayers;

    /// <summary>
    /// Indicates whether the archive confirmation panel is open.
    /// </summary>
    private bool _showArchiveConfirm;

    /// <summary>
    /// Structured archive blockers returned from a failed archive attempt.
    /// </summary>
    private IReadOnlyList<PlayerArchiveBlocker> _archiveBlockers = [];

    /// <summary>
    /// The normalized return URL used by the back link.
    /// </summary>
    private string? _returnUrl;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        var principal = authState.User;
        // Membership, not the admin role, is what the server's mutation gate requires — the same
        // authority the Players directory derives, so both hosts offer the same lifecycle control.
        var club = ReadClubIdClaim(principal);
        var user = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _canManagePlayers = principal.Identity?.IsAuthenticated == true && club is > 0 && !string.IsNullOrEmpty(user);

        _returnUrl = NormalizeReturnUrl(ReturnUrl);
        await LoadDetailAsync();
    }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        var normalized = NormalizeReturnUrl(ReturnUrl);
        if (!string.Equals(_returnUrl, normalized, StringComparison.Ordinal)) { _returnUrl = normalized; }
    }

    /// <summary>
    /// Gets the Bootstrap badge CSS class for the current lifecycle status.
    /// </summary>
    protected string LifecycleBadgeClass => _detail?.LifecycleStatus switch
    {
        LifecycleStatus.Archived => "badge text-bg-secondary",
        _ => "badge text-bg-success"
    };

    /// <summary>
    /// Gets the Bootstrap badge CSS class for a given campaign status.
    /// </summary>
    /// <param name="status">The campaign status.</param>
    /// <returns>A Bootstrap badge class string.</returns>
    protected static string CampaignStatusBadgeClass(CampaignStatus status) => status switch
    {
        CampaignStatus.Active => "text-bg-success",
        CampaignStatus.Closed => "text-bg-secondary",
        _ => "text-bg-secondary"
    };

    /// <summary>
    /// Loads or reloads the player detail payload from the service.
    /// </summary>
    /// <returns>A task that completes when loading and state updates are finished.</returns>
    private async Task LoadDetailAsync()
    {
        _isLoading = true;
        _error = null;
        _isNotFound = false;

        var result = await playerDetailService.GetPlayerDetailAsync(PlayerId, ComponentCancellationToken);
        result.Switch(
            detail =>
            {
                var sortedHistory = detail.CampaignHistory
                    .OrderByDescending(h => h.CampaignStartDate)
                    .ToList();
                _detail = detail with { CampaignHistory = sortedHistory };
            },
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                if (problem.Kind == ServiceProblemKind.NotFound)
                {
                    _isNotFound = true;
                    _detail = null;
                    return;
                }

                _error = problem.Detail ?? "Could not load player details.";
            });

        _isLoading = false;
    }

    /// <summary>
    /// Reloads detail data after a user-initiated retry.
    /// </summary>
    /// <returns>A task that completes when loading is finished.</returns>
    private async Task RetryLoadAsync() => await LoadDetailAsync();

    private string EditUrl => PlayersUrlState.FromReturnDestination(_returnUrl).ToFormUrl(PlayerId);

    /// <summary>
    /// Opens the archive confirmation panel.
    /// </summary>
    private void BeginArchive()
    {
        _showArchiveConfirm = true;
        _archiveBlockers = [];
        _mutationError = null;
        _statusMessage = null;
    }

    /// <summary>
    /// Closes archive confirmation without mutating data.
    /// </summary>
    private void CancelArchive()
    {
        _showArchiveConfirm = false;
        _archiveBlockers = [];
    }

    /// <summary>
    /// Archives the player after explicit user confirmation, then refreshes detail.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task ConfirmArchiveAsync()
    {
        _isMutating = true;
        _mutationError = null;
        _archiveBlockers = [];

        var result = await playerLifecycleService.ArchiveAsync(PlayerId, ComponentCancellationToken);
        result.Switch(
            _ =>
            {
                _statusMessage = PlayerLifecycleCopy.ArchivedResult;
                CancelArchive();
            },
            problem =>
            {
                _mutationError = problem.Detail ?? "Could not archive player.";
                if (problem.Kind == ServiceProblemKind.Conflict
                    && problem.TryGetArchiveBlockers(out var blockers))
                {
                    _archiveBlockers = blockers;
                }
            });

        _isMutating = false;
        if (result.IsSuccess)
        {
            await LoadDetailAsync();
        }
    }

    /// <summary>
    /// Restores an archived player, then refreshes detail.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task RestorePlayerAsync()
    {
        _isMutating = true;
        _mutationError = null;

        var result = await playerLifecycleService.RestoreAsync(PlayerId, ComponentCancellationToken);
        result.Switch(
            _ => _statusMessage = PlayerLifecycleCopy.RestoredResult,
            problem => _mutationError = problem.Detail ?? "Could not restore player.");

        _isMutating = false;
        if (result.IsSuccess)
        {
            await LoadDetailAsync();
        }
    }

    /// <summary>
    /// Reads the authenticated club id from the principal, as the Players directory does.
    /// </summary>
    /// <param name="principal">The authenticated principal.</param>
    /// <returns>The club identifier, or <see langword="null"/> when the claim is absent or unusable.</returns>
    private static long? ReadClubIdClaim(ClaimsPrincipal principal)
        => long.TryParse(principal.FindFirst(NovaClaimTypes.ClubId)?.Value, NumberStyles.Integer,
            CultureInfo.InvariantCulture, out var id) && id > 0 ? id : null;

    /// <summary>
    /// Normalizes the inbound return URL to a safe local path within this application.
    /// </summary>
    /// <param name="returnUrl">The incoming return URL query value.</param>
    /// <returns>A safe local path for the roster back link.</returns>
    private static string NormalizeReturnUrl(string? returnUrl)
        => Nova.UI.Common.CorrectionReturnContext.Normalize(returnUrl) ?? "/players";

    /// <summary>
    /// Builds a safe inline CSS style string for one current-trait badge.
    /// </summary>
    /// <param name="trait">The trait to style.</param>
    /// <returns>A sanitized inline style string.</returns>
    private static string BuildTraitStyle(PlayerCurrentTraitDto trait)
        => PlayerTagStyle.BuildBadgeStyle(trait.Color);

    /// <summary>
    /// Builds a safe inline CSS style string for one history tag application badge.
    /// </summary>
    /// <param name="tag">The tag application to style.</param>
    /// <returns>A sanitized inline style string.</returns>
    private static string BuildTagApplicationStyle(PlayerTagApplicationDto tag)
        => PlayerTagStyle.BuildBadgeStyle(tag.TagColor);

}
