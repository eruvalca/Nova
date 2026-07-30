using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Nova.Shared.Enums;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Components;
using Nova.UI.Features.Players.Components;

namespace Nova.UI.Features.Players.Pages;

/// <summary>
/// Displays one player's permanent profile, lifecycle state, current traits, and expandable campaign history.
/// Administrators can launch edit, archive, and restore interactions inline; evaluators have read-only access.
/// </summary>
/// <param name="playerDetailService">The player-detail query service.</param>
/// <param name="playerManagementService">The player create/update service.</param>
/// <param name="playerLifecycleService">The player archive/restore service.</param>
/// <param name="authenticationStateProvider">The authentication state provider.</param>
/// <param name="navigationManager">The navigation manager used for access-denied redirects.</param>
public partial class PlayerDetail(
    IPlayerDetailService playerDetailService,
    IPlayerManagementService playerManagementService,
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
    /// The form-level error message forwarded into the edit form.
    /// </summary>
    private string? _formError;

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
    /// Indicates whether the current user can manage (edit/archive/restore) players.
    /// </summary>
    private bool _canManagePlayers;

    /// <summary>
    /// Indicates whether the edit form is currently visible.
    /// </summary>
    private bool _showEditForm;

    /// <summary>
    /// The edit-mode form state when edit is active.
    /// </summary>
    private PlayerFormState? _editForm;

    /// <summary>
    /// Structured graduation-year blockers returned from an update conflict.
    /// </summary>
    private IReadOnlyList<GraduationYearBlockerItem> _graduationYearBlockers = [];

    /// <summary>
    /// Indicates whether the archive confirmation panel is open.
    /// </summary>
    private bool _showArchiveConfirm;

    /// <summary>
    /// Indicates whether the archive confirmation checkbox is checked.
    /// </summary>
    private bool _archiveConfirmed;

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
        _canManagePlayers = principal.IsInRole(Roles.Admin) || principal.IsInRole(Roles.ClubAdmin);

        _returnUrl = NormalizeReturnUrl(ReturnUrl);
        await LoadDetailAsync();
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

    /// <summary>
    /// Opens the edit form by populating state from the currently loaded detail.
    /// </summary>
    /// <returns>A task that completes when form state is ready.</returns>
    private async Task BeginEditAsync()
    {
        _showArchiveConfirm = false;
        _formError = null;
        _mutationError = null;
        _graduationYearBlockers = [];
        _isMutating = true;

        var result = await playerDetailService.GetPlayerDetailAsync(PlayerId, ComponentCancellationToken);
        result.Switch(
            detail =>
            {
                _editForm = PlayerFormState.FromDetail(detail);
                _showEditForm = true;
            },
            problem => _mutationError = problem.Detail ?? "Could not load player details for editing.");

        _isMutating = false;
    }

    /// <summary>
    /// Saves edits for the player and refreshes detail.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task UpdatePlayerAsync()
    {
        if (_editForm is null)
        {
            return;
        }

        _isMutating = true;
        _formError = null;
        _graduationYearBlockers = [];

        var result = await playerManagementService.UpdateAsync(_editForm.ToUpdateInput(), ComponentCancellationToken);
        result.Switch(
            _ =>
            {
                _showEditForm = false;
                _editForm = null;
                _statusMessage = "Player updated successfully.";
            },
            problem =>
            {
                _formError = problem.Detail ?? "Could not update player.";
                if (problem.Kind == ServiceProblemKind.Conflict)
                {
                    _graduationYearBlockers = ExtractGraduationYearBlockers(problem.Errors);
                }
            });

        _isMutating = false;
        if (result.IsSuccess)
        {
            await LoadDetailAsync();
        }
    }

    /// <summary>
    /// Cancels edit mode and clears form state.
    /// </summary>
    private void CancelEdit()
    {
        _showEditForm = false;
        _editForm = null;
        _formError = null;
        _graduationYearBlockers = [];
    }

    /// <summary>
    /// Opens the archive confirmation panel.
    /// </summary>
    private void BeginArchive()
    {
        _showEditForm = false;
        _showArchiveConfirm = true;
        _archiveConfirmed = false;
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
        _archiveConfirmed = false;
        _archiveBlockers = [];
    }

    /// <summary>
    /// Archives the player after explicit user confirmation, then refreshes detail.
    /// </summary>
    /// <returns>A task that completes when the mutation finishes.</returns>
    private async Task ConfirmArchiveAsync()
    {
        if (!_archiveConfirmed)
        {
            return;
        }

        _isMutating = true;
        _mutationError = null;
        _archiveBlockers = [];

        var result = await playerLifecycleService.ArchiveAsync(PlayerId, ComponentCancellationToken);
        result.Switch(
            _ =>
            {
                _statusMessage = "Player archived.";
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
            _ => _statusMessage = "Player restored. Missed campaign enrollment is not backfilled automatically.",
            problem => _mutationError = problem.Detail ?? "Could not restore player.");

        _isMutating = false;
        if (result.IsSuccess)
        {
            await LoadDetailAsync();
        }
    }

    /// <summary>
    /// Normalizes the inbound return URL to a safe local path within this application.
    /// </summary>
    /// <param name="returnUrl">The incoming return URL query value.</param>
    /// <returns>A safe local path for the roster back link.</returns>
    private static string NormalizeReturnUrl(string? returnUrl)
    {
        if (string.IsNullOrWhiteSpace(returnUrl))
        {
            return "/players";
        }

        var candidate = returnUrl.Trim();
        if (!Uri.IsWellFormedUriString(candidate, UriKind.Relative)
            || candidate.StartsWith("//", StringComparison.Ordinal)
            || candidate.StartsWith("\\\\", StringComparison.Ordinal))
        {
            return "/players";
        }

        return candidate.StartsWith('/') ? candidate : $"/{candidate}";
    }

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

    /// <summary>
    /// Extracts structured graduation-year blockers from a conflict error payload.
    /// </summary>
    /// <param name="errors">The service-problem errors dictionary.</param>
    /// <returns>A parsed list of blocker items, or an empty list when unavailable.</returns>
    private static IReadOnlyList<GraduationYearBlockerItem> ExtractGraduationYearBlockers(
        IReadOnlyDictionary<string, string[]>? errors)
    {
        if (errors is null || errors.Count == 0)
        {
            return [];
        }

        var blockers = new Dictionary<int, GraduationYearBlockerBuilder>();
        foreach (var (key, values) in errors)
        {
            if (values.Length == 0 || !TryParseBlockerKey(key, out var index, out var fieldName))
            {
                continue;
            }

            if (!blockers.TryGetValue(index, out var builder))
            {
                builder = new GraduationYearBlockerBuilder();
                blockers[index] = builder;
            }

            var value = values[0];
            switch (fieldName)
            {
                case "assignmentId":
                    builder.PlayerCampaignAssignmentId = TryParseLong(value);
                    break;
                case "campaignId":
                    builder.CampaignId = TryParseLong(value);
                    break;
                case "teamId":
                    builder.TeamId = TryParseLong(value);
                    break;
                case "teamGraduationYear":
                    builder.TeamGraduationYear = TryParseInt(value);
                    break;
            }
        }

        return blockers
            .OrderBy(pair => pair.Key)
            .Select(pair => pair.Value)
            .Where(builder =>
                builder.PlayerCampaignAssignmentId is not null
                && builder.CampaignId is not null
                && builder.TeamId is not null
                && builder.TeamGraduationYear is not null)
            .Select(builder => new GraduationYearBlockerItem
            {
                PlayerCampaignAssignmentId = builder.PlayerCampaignAssignmentId!.Value,
                CampaignId = builder.CampaignId!.Value,
                TeamId = builder.TeamId!.Value,
                TeamGraduationYear = builder.TeamGraduationYear!.Value
            })
            .ToList()
            .AsReadOnly();
    }

    /// <summary>
    /// Attempts to parse a blocker error key of the form <c>blockers[N].fieldName</c>.
    /// </summary>
    /// <param name="key">The error dictionary key.</param>
    /// <param name="index">The extracted blocker index.</param>
    /// <param name="fieldName">The extracted field name.</param>
    /// <returns><see langword="true"/> when the key matches the expected pattern.</returns>
    private static bool TryParseBlockerKey(string key, out int index, out string fieldName)
    {
        index = default;
        fieldName = string.Empty;

        if (!key.StartsWith("blockers[", StringComparison.Ordinal))
        {
            return false;
        }

        var closeBracketIndex = key.IndexOf(']');
        var dotIndex = key.IndexOf('.', closeBracketIndex + 1);
        if (closeBracketIndex <= "blockers[".Length || dotIndex < 0)
        {
            return false;
        }

        var indexText = key["blockers[".Length..closeBracketIndex];
        if (!int.TryParse(indexText, NumberStyles.Integer, CultureInfo.InvariantCulture, out index))
        {
            return false;
        }

        fieldName = key[(dotIndex + 1)..];
        return fieldName.Length > 0;
    }

    /// <summary>
    /// Parses a <see langword="long"/> from a string, returning <see langword="null"/> on failure.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <returns>The parsed value, or <see langword="null"/>.</returns>
    private static long? TryParseLong(string value)
        => long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    /// <summary>
    /// Parses an <see langword="int"/> from a string, returning <see langword="null"/> on failure.
    /// </summary>
    /// <param name="value">The string value to parse.</param>
    /// <returns>The parsed value, or <see langword="null"/>.</returns>
    private static int? TryParseInt(string value)
        => int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : null;

    /// <summary>
    /// Mutable accumulator for one graduation-year blocker while parsing error keys.
    /// </summary>
    private sealed class GraduationYearBlockerBuilder
    {
        /// <summary>
        /// Gets or sets the assignment identifier.
        /// </summary>
        public long? PlayerCampaignAssignmentId { get; set; }

        /// <summary>
        /// Gets or sets the campaign identifier.
        /// </summary>
        public long? CampaignId { get; set; }

        /// <summary>
        /// Gets or sets the team identifier.
        /// </summary>
        public long? TeamId { get; set; }

        /// <summary>
        /// Gets or sets the team graduation-year requirement.
        /// </summary>
        public int? TeamGraduationYear { get; set; }
    }
}
