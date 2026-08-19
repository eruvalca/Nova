using Microsoft.AspNetCore.Components;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.UI.Components;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the campaign overview: a campaign snapshot, the authoritative outcome summary, the
/// closeout-readiness line, and a bounded recent-lifecycle activity feed.
/// </summary>
/// <param name="closeoutQueryService">The closeout readiness and recent-activity query service.</param>
public partial class CampaignOverviewPanel(
    ICampaignCloseoutQueryService closeoutQueryService) : NovaComponentBase
{
    /// <summary>
    /// Gets or sets the campaign identifier from the route.
    /// </summary>
    [Parameter]
    public long CampaignId { get; set; }

    /// <summary>
    /// Gets or sets the loaded campaign detail used for the snapshot fields.
    /// </summary>
    [Parameter, EditorRequired]
    public CampaignDetailResult Detail { get; set; } = null!;

    /// <summary>
    /// Gets or sets whether the current user holds the club administrator role.
    /// </summary>
    [Parameter]
    public bool IsClubAdmin { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the administrator opens the closeout tab.
    /// </summary>
    [Parameter]
    public EventCallback OnOpenCloseout { get; set; }

    /// <summary>
    /// Gets or sets the persisted closeout readiness used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public CampaignCloseoutReadinessDto? PersistedReadiness { get; set; }

    /// <summary>
    /// Gets or sets the persisted recent activity used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public CampaignActivityResult? PersistedActivity { get; set; }

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
    /// The loaded recent activity, or <see langword="null"/> when unavailable.
    /// </summary>
    private CampaignActivityResult? _activity;

    /// <summary>
    /// The current panel-level load error message.
    /// </summary>
    private string? _error;

    /// <summary>
    /// Indicates whether the panel data is loading.
    /// </summary>
    private bool _isLoading;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        if (Initialized)
        {
            _readiness = PersistedReadiness;
            _activity = PersistedActivity;
            _error = PersistedError;
            _isLoading = false;
            return;
        }

        _isLoading = true;
        await LoadInitialAsync();
        PersistStartupState();
        Initialized = true;
        _isLoading = false;
    }

    /// <summary>
    /// Loads closeout readiness and recent activity in parallel, surfacing the first failure as a
    /// panel-level error.
    /// </summary>
    /// <returns>A task that completes when both loads finish and state is updated.</returns>
    private async Task LoadInitialAsync()
    {
        _error = null;

        var readinessTask = closeoutQueryService.GetCloseoutReadinessAsync(
            new GetCampaignCloseoutReadinessInput { CampaignId = CampaignId },
            ComponentCancellationToken);
        var activityTask = closeoutQueryService.GetActivityAsync(
            new GetCampaignActivityInput { CampaignId = CampaignId, Limit = GetCampaignActivityInput.DefaultLimit },
            ComponentCancellationToken);

        await Task.WhenAll(readinessTask, activityTask);

        var readinessResult = await readinessTask;
        var activityResult = await activityTask;

        string? error = null;
        readinessResult.Switch(
            readiness => _readiness = readiness,
            problem => error = ProblemMessage(problem, "Failed to load the campaign overview. Please retry."));
        activityResult.Switch(
            activity => _activity = activity,
            problem => error ??= ProblemMessage(problem, "Failed to load the campaign activity. Please retry."));

        _error = error;
    }

    /// <summary>
    /// Retries the initial load after a recoverable error.
    /// </summary>
    /// <returns>A task that completes when the retried load finishes.</returns>
    private async Task RetryAsync()
    {
        _isLoading = true;
        await LoadInitialAsync();
        PersistStartupState();
        _isLoading = false;
    }

    /// <summary>
    /// Persists the startup readiness/activity/error state for prerender-to-interactive restoration.
    /// </summary>
    private void PersistStartupState()
    {
        PersistedReadiness = _readiness;
        PersistedActivity = _activity;
        PersistedError = _error;
    }

    /// <summary>
    /// Resolves a human-readable message from a service problem.
    /// </summary>
    /// <param name="problem">The service problem.</param>
    /// <param name="fallback">The fallback message when the problem has no detail.</param>
    /// <returns>The problem detail or the fallback message.</returns>
    private static string ProblemMessage(ServiceProblem problem, string fallback)
        => string.IsNullOrWhiteSpace(problem.Detail) ? fallback : problem.Detail;

    /// <summary>
    /// Formats a campaign's date range for display.
    /// </summary>
    /// <param name="detail">The campaign detail payload.</param>
    /// <returns>The formatted date range.</returns>
    private static string FormatCampaignDates(CampaignDetailResult detail)
        => detail.PlannedEndDate is null
            ? $"Starts {detail.StartDate:MMM d, yyyy}"
            : $"{detail.StartDate:MMM d, yyyy} – {detail.PlannedEndDate.Value:MMM d, yyyy}";

    /// <summary>
    /// Formats a lifecycle event timestamp for display.
    /// </summary>
    /// <param name="createdAt">The event timestamp.</param>
    /// <returns>The formatted timestamp.</returns>
    private static string FormatActivityDate(DateTimeOffset createdAt)
        => createdAt.ToString("MMM d, yyyy");

    /// <summary>
    /// Maps a lifecycle event type to its display verb phrase.
    /// </summary>
    /// <param name="eventType">The lifecycle event type.</param>
    /// <returns>The display verb phrase.</returns>
    private static string ActivityVerb(CampaignLifecycleEventType eventType) => eventType switch
    {
        CampaignLifecycleEventType.Closed => "closed the campaign",
        CampaignLifecycleEventType.Reopened => "reopened the campaign",
        _ => "changed the campaign"
    };
}
