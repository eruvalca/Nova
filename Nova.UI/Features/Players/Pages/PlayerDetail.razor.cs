
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
    /// The club identifier from the current principal's claims, used to detect club-membership changes
    /// while the page is mounted and rebind club-scoped state accordingly.
    /// </summary>
    private long? _clubId;

    /// <summary>
    /// The club scope generation. Detail reads and lifecycle mutations re-check it before applying
    /// state, so a response that belongs to a scope this page has left cannot repopulate the view.
    /// </summary>
    private int _clubScopeVersion;

    /// <summary>
    /// The authentication generation. Every applied state re-checks it, so a startup read that
    /// resolves after a notification cannot overwrite the newer principal.
    /// </summary>
    private int _authenticationVersion;

    /// <summary>
    /// Indicates whether the archive confirmation panel is open.
    /// </summary>
    private bool _showArchiveConfirm;

    /// <summary>
    /// The subject the open archive confirmation reviews. Captured when the panel opens so a later
    /// route or refresh change cannot show one player while the confirm archives another.
    /// </summary>
    private long _archiveSubjectId;

    /// <summary>
    /// The reviewed subject's display name, captured with <see cref="_archiveSubjectId"/>.
    /// </summary>
    private string _archiveSubjectName = string.Empty;

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
        authenticationStateProvider.AuthenticationStateChanged += OnAuthenticationStateChanged;
        var version = _authenticationVersion;
        var authState = await authenticationStateProvider.GetAuthenticationStateAsync();
        _returnUrl = NormalizeReturnUrl(ReturnUrl);
        if (version != _authenticationVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            // A notification overtook this read: it already bound the page to its club and started its
            // own load, so this read applies neither its principal nor its detail — the two loads share
            // the club-scope generation, so the stale one would otherwise win the race to apply.
            return;
        }

        ApplyAuthority(authState.User);
        await LoadDetailAsync();
    }

    /// <summary>
    /// Recomputes the club scope and the management permission from one principal. Membership, not the
    /// admin role, is what the server's mutation gate requires — the same authority the Players
    /// directory derives, so both hosts offer the same lifecycle control.
    /// </summary>
    /// <param name="principal">The authenticated principal to read.</param>
    /// <returns><see langword="true"/> when the principal may manage this club's players.</returns>
    private bool ApplyAuthority(ClaimsPrincipal principal)
    {
        var club = ReadClubIdClaim(principal);
        var user = principal.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        _clubId = club;
        _canManagePlayers = principal.Identity?.IsAuthenticated == true && club is > 0 && !string.IsNullOrEmpty(user);
        return _canManagePlayers;
    }

    /// <summary>Recomputes this page's club-scoped authority when the authentication state changes.</summary>
    /// <param name="stateTask">The authentication state task produced by the change event.</param>
    private void OnAuthenticationStateChanged(Task<AuthenticationState> stateTask)
        => _ = InvokeAsync(() => ApplyAuthenticationStateAsync(stateTask));

    /// <summary>
    /// Applies an authentication-state change: closes management state the change may have revoked and
    /// rebinds the club-scoped detail, so a page that stays mounted cannot keep showing the previous
    /// scope's player or offer its lifecycle controls.
    /// </summary>
    /// <param name="stateTask">The authentication state task produced by the change event.</param>
    private async Task ApplyAuthenticationStateAsync(Task<AuthenticationState> stateTask)
    {
        var version = ++_authenticationVersion;
        var authState = await stateTask;
        if (version != _authenticationVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            // A newer notification already applied its state; this one is stale.
            return;
        }

        var previousClub = _clubId;
        var canManage = ApplyAuthority(authState.User);

        if (!canManage || _clubId != previousClub)
        {
            // A reviewed panel belongs to the authority that opened it: neither a revoked membership
            // nor another club's claim may leave it actionable.
            CancelArchive();
            _archiveSubjectId = 0;
            _archiveSubjectName = string.Empty;
            _mutationError = null;
            _statusMessage = null;
        }

        if (_clubId != previousClub)
        {
            // Rebind to the newly claimed club: invalidate every in-flight scope-bound response,
            // drop the stale detail and reload against the new scope, which re-authorizes
            // server-side and redirects when the claim no longer allows it.
            ++_clubScopeVersion;
            _isMutating = false;
            _detail = null;
            _error = null;
            _isNotFound = false;
            _isLoading = true;
            await InvokeAsync(StateHasChanged);
            await LoadDetailAsync();
        }

        await InvokeAsync(StateHasChanged);
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
        var version = _clubScopeVersion;
        _isLoading = true;
        _error = null;
        _isNotFound = false;

        var result = await playerDetailService.GetPlayerDetailAsync(PlayerId, ComponentCancellationToken);
        if (version != _clubScopeVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            // This read belongs to a club scope the page has left.
            return;
        }

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
        // Snapshot the reviewed subject: the panel reviews one player, and only that player may be
        // archived, however the route or the loaded detail changes while the panel is open.
        _archiveSubjectId = PlayerId;
        _archiveSubjectName = _detail is { } detail ? $"{detail.FirstName} {detail.LastName}" : string.Empty;
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
        var version = _clubScopeVersion;
        _isMutating = true;
        _mutationError = null;
        _archiveBlockers = [];

        var result = await playerLifecycleService.ArchiveAsync(_archiveSubjectId, ComponentCancellationToken);
        if (version != _clubScopeVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            // The outcome belongs to a club scope the page has left.
            return;
        }

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
        var version = _clubScopeVersion;
        _isMutating = true;
        _mutationError = null;

        var result = await playerLifecycleService.RestoreAsync(PlayerId, ComponentCancellationToken);
        if (version != _clubScopeVersion || ComponentCancellationToken.IsCancellationRequested)
        {
            // The outcome belongs to a club scope the page has left.
            return;
        }

        result.Switch(
            _ => _statusMessage = PlayerLifecycleCopy.RestoredResult,
            problem => _mutationError = problem.Detail ?? "Could not restore player.");

        _isMutating = false;
        if (result.IsSuccess)
        {
            await LoadDetailAsync();
        }
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        authenticationStateProvider.AuthenticationStateChanged -= OnAuthenticationStateChanged;
        await base.DisposeAsyncCore();
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
