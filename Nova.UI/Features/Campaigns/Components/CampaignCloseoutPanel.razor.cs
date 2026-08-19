using Microsoft.AspNetCore.Components;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.UI.Components;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the campaign closeout workflow: the readiness checklist for Active campaigns with the
/// close action, and the closure metadata, outcome summary, and reopen action for Closed campaigns.
/// </summary>
/// <param name="closeoutQueryService">The closeout readiness query service.</param>
/// <param name="lifecycleService">The campaign close and reopen lifecycle service.</param>
public partial class CampaignCloseoutPanel(
    ICampaignCloseoutQueryService closeoutQueryService,
    ICampaignLifecycleService lifecycleService) : NovaComponentBase
{
    /// <summary>
    /// The fallback close-conflict warning shown when the server does not supply a detail message.
    /// </summary>
    private const string CloseConflictFallbackMessage = "Resolve all campaign close blockers before closing this campaign.";

    /// <summary>
    /// The fallback lifecycle failure message shown when the server does not supply a detail message.
    /// </summary>
    private const string LifecycleFailureFallbackMessage = "The campaign state changed. Reload and try again.";

    /// <summary>
    /// Gets or sets the campaign identifier from the route.
    /// </summary>
    [Parameter]
    public long CampaignId { get; set; }

    /// <summary>
    /// Gets or sets the loaded campaign detail used to detect the lifecycle status and closure metadata.
    /// </summary>
    [Parameter, EditorRequired]
    public CampaignDetailResult Detail { get; set; } = null!;

    /// <summary>
    /// Gets or sets whether the current user holds the club administrator role.
    /// </summary>
    [Parameter]
    public bool IsClubAdmin { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when a blocked row requests the unresolved-placement review,
    /// carrying the unresolved-only flag for the target placements URL.
    /// </summary>
    [Parameter]
    public EventCallback<bool> OnReviewUnresolved { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user cancels out of the closeout view.
    /// </summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked after a successful close or reopen so the page reloads detail.
    /// </summary>
    [Parameter]
    public EventCallback OnReloadRequested { get; set; }

    /// <summary>
    /// Gets or sets the persisted closeout readiness used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public CampaignCloseoutReadinessDto? PersistedReadiness { get; set; }

    /// <summary>
    /// Gets or sets the persisted load error used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public string? PersistedError { get; set; }

    /// <summary>
    /// Gets or sets whether startup initialization already completed during prerender.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <summary>
    /// The loaded closeout readiness, or <see langword="null"/> when unavailable.
    /// </summary>
    private CampaignCloseoutReadinessDto? _readiness;

    /// <summary>
    /// The current panel-level load error message.
    /// </summary>
    private string? _error;

    /// <summary>
    /// Indicates whether the panel data is loading.
    /// </summary>
    private bool _isLoading;

    /// <summary>
    /// Indicates whether a close mutation is in flight.
    /// </summary>
    private bool _isClosing;

    /// <summary>
    /// Indicates whether a reopen mutation is in flight.
    /// </summary>
    private bool _isReopening;

    /// <summary>
    /// Indicates whether the inline reopen confirmation is shown.
    /// </summary>
    private bool _showReopenConfirm;

    /// <summary>
    /// The current mutation error message, or <see langword="null"/> when none is active.
    /// </summary>
    private string? _mutationError;

    /// <summary>
    /// The current mutation success message, or <see langword="null"/> when none is active.
    /// </summary>
    private string? _successMessage;

    /// <summary>
    /// Indicates whether the active mutation ended in a lifecycle conflict, which warrants a warning
    /// alert and a readiness refresh.
    /// </summary>
    private bool _mutationConflict;

    /// <summary>
    /// The last applied campaign status, used to detect a close or reopen transition.
    /// </summary>
    private CampaignStatus _appliedStatus;

    /// <summary>
    /// Gets a value indicating whether the close action is currently enabled.
    /// </summary>
    private bool CanClose
        => _readiness?.IsReady == true
            && !_isClosing
            && IsClubAdmin
            && Detail.Status == CampaignStatus.Active;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        _appliedStatus = Detail.Status;

        if (Initialized)
        {
            _readiness = PersistedReadiness;
            _error = PersistedError;
            _isLoading = false;
            return;
        }

        _isLoading = true;
        await LoadReadinessAsync();
        PersistStartupState();
        Initialized = true;
        _isLoading = false;
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (Detail.Status != _appliedStatus)
        {
            _appliedStatus = Detail.Status;
            await LoadReadinessAsync();
            PersistStartupState();
        }
    }

    /// <summary>
    /// Loads the authoritative closeout readiness for the current campaign status.
    /// </summary>
    /// <returns>A task that completes when the load finishes and state is updated.</returns>
    private async Task LoadReadinessAsync()
    {
        _error = null;

        var result = await closeoutQueryService.GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = CampaignId },
            ComponentCancellationToken);

        result.Switch(
            readiness => _readiness = readiness,
            problem =>
            {
                _error = ProblemMessage(problem, "Failed to load closeout readiness. Please retry.");
                _readiness = null;
            });
    }

    /// <summary>
    /// Retries the readiness load after a recoverable error.
    /// </summary>
    /// <returns>A task that completes when the retried load finishes.</returns>
    private async Task RetryAsync()
    {
        _isLoading = true;
        await LoadReadinessAsync();
        PersistStartupState();
        _isLoading = false;
    }

    /// <summary>
    /// Closes the campaign after the readiness checklist passes.
    /// </summary>
    /// <returns>A task that completes when the close request and follow-up reload finish.</returns>
    private async Task CloseAsync()
    {
        if (!CanClose)
        {
            return;
        }

        _isClosing = true;
        _mutationError = null;
        _successMessage = null;
        _mutationConflict = false;

        var result = await lifecycleService.CloseAsync(CampaignId, ComponentCancellationToken);

        var succeeded = false;
        var refetchReadiness = false;
        result.Switch(
            _ =>
            {
                _mutationError = null;
                _successMessage = "Campaign closed.";
                succeeded = true;
            },
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Conflict)
                {
                    _mutationConflict = true;
                    _mutationError = ProblemMessage(problem, CloseConflictFallbackMessage);
                    refetchReadiness = true;
                }
                else
                {
                    _mutationConflict = false;
                    _mutationError = ProblemMessage(problem, LifecycleFailureFallbackMessage);
                }
            });

        if (succeeded)
        {
            await OnReloadRequested.InvokeAsync();
        }
        else if (refetchReadiness)
        {
            await LoadReadinessAsync();
            PersistStartupState();
        }

        _isClosing = false;
    }

    /// <summary>
    /// Shows the inline reopen confirmation.
    /// </summary>
    private void ConfirmReopen()
    {
        _mutationError = null;
        _mutationConflict = false;
        _showReopenConfirm = true;
    }

    /// <summary>
    /// Hides the inline reopen confirmation without performing any action.
    /// </summary>
    private void CancelReopen() => _showReopenConfirm = false;

    /// <summary>
    /// Reopens a closed campaign after the administrator confirms.
    /// </summary>
    /// <returns>A task that completes when the reopen request and follow-up reload finish.</returns>
    private async Task ReopenAsync()
    {
        if (_isReopening)
        {
            return;
        }

        _isReopening = true;
        _showReopenConfirm = false;
        _mutationError = null;
        _successMessage = null;
        _mutationConflict = false;

        var result = await lifecycleService.ReopenAsync(CampaignId, ComponentCancellationToken);

        var succeeded = false;
        var refetchReadiness = false;
        result.Switch(
            _ =>
            {
                _mutationError = null;
                _successMessage = "Campaign reopened.";
                succeeded = true;
            },
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Conflict)
                {
                    _mutationConflict = true;
                    _mutationError = ProblemMessage(problem, LifecycleFailureFallbackMessage);
                    refetchReadiness = true;
                }
                else
                {
                    _mutationConflict = false;
                    _mutationError = ProblemMessage(problem, LifecycleFailureFallbackMessage);
                }
            });

        if (succeeded)
        {
            await OnReloadRequested.InvokeAsync();
        }
        else if (refetchReadiness)
        {
            await LoadReadinessAsync();
            PersistStartupState();
        }

        _isReopening = false;
    }

    /// <summary>
    /// Raises a review-unresolved request back to the parent page with the unresolved-only flag.
    /// </summary>
    /// <param name="unresolvedOnly">Whether the target placements URL should filter to unresolved placements.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task ReviewUnresolvedAsync(bool unresolvedOnly) => OnReviewUnresolved.InvokeAsync(unresolvedOnly);

    /// <summary>
    /// Persists the startup readiness/error state for prerender-to-interactive restoration.
    /// </summary>
    private void PersistStartupState()
    {
        PersistedReadiness = _readiness;
        PersistedError = _error;
    }

    /// <summary>
    /// Finds the closeout blocker for a shared condition key.
    /// </summary>
    /// <param name="condition">The shared blocker condition key.</param>
    /// <returns>The matching blocker, or <see langword="null"/> when the condition is clear.</returns>
    private CampaignCloseoutBlockerDto? BlockerFor(string condition)
        => _readiness?.Blockers.FirstOrDefault(blocker => string.Equals(blocker.Condition, condition, StringComparison.Ordinal));

    /// <summary>
    /// Resolves a human-readable message from a service problem.
    /// </summary>
    /// <param name="problem">The service problem.</param>
    /// <param name="fallback">The fallback message when the problem has no detail.</param>
    /// <returns>The problem detail or the fallback message.</returns>
    private static string ProblemMessage(ServiceProblem problem, string fallback)
        => string.IsNullOrWhiteSpace(problem.Detail) ? fallback : problem.Detail;

    /// <summary>
    /// Formats the campaign closure timestamp for display.
    /// </summary>
    /// <param name="closedAt">The closure timestamp, or <see langword="null"/> when unavailable.</param>
    /// <returns>The formatted closure timestamp.</returns>
    private static string FormatClosedDate(DateTimeOffset? closedAt)
        => closedAt is null ? "unknown date" : closedAt.Value.ToString("MMM d, yyyy");
}
