using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Tags;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Nova.UI.Components;

namespace Nova.UI.Features.Campaigns.Pages;

/// <summary>
/// Renders the campaign workspace shell: header, tab bar, and the evaluate roster region.
/// </summary>
/// <param name="campaignQueryService">The campaign detail query service.</param>
/// <param name="participantQueryService">The campaign roster query service.</param>
/// <param name="tagDefinitionQueryService">The tag-definition choices service used by roster filters.</param>
/// <param name="teamRosterService">The team choices service used by roster filters.</param>
/// <param name="navigationManager">The navigation manager used for tab history and redirects.</param>
/// <param name="jsRuntime">The JavaScript runtime used for scroll and focus interactions.</param>
public partial class CampaignWorkspace(
    ICampaignQueryService campaignQueryService,
    ICampaignParticipantQueryService participantQueryService,
    ITagDefinitionQueryService tagDefinitionQueryService,
    ITeamRosterService teamRosterService,
    NavigationManager navigationManager,
    IJSRuntime jsRuntime) : NovaComponentBase
{
    /// <summary>
    /// The name of the evaluate tab, the only functional workspace section in this phase.
    /// </summary>
    private const string EvaluateTabName = "evaluate";

    /// <summary>
    /// The tag-definition choices service reserved for the roster filter components.
    /// </summary>
    private readonly ITagDefinitionQueryService _tagDefinitionQueryService = tagDefinitionQueryService;

    /// <summary>
    /// The team choices service reserved for the roster filter components.
    /// </summary>
    private readonly ITeamRosterService _teamRosterService = teamRosterService;

    /// <summary>
    /// The JavaScript runtime reserved for scroll and focus interactions.
    /// </summary>
    private readonly IJSRuntime _jsRuntime = jsRuntime;

    /// <summary>
    /// Gets or sets the campaign identifier from the route.
    /// </summary>
    [Parameter]
    public long CampaignId { get; set; }

    /// <summary>
    /// Gets or sets the incoming tab query parameter.
    /// </summary>
    [SupplyParameterFromQuery(Name = "tab")]
    private string? TabQuery { get; set; }

    /// <summary>
    /// Gets or sets the persisted campaign detail used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public CampaignDetailResult? PersistedDetail { get; set; }

    /// <summary>
    /// Gets or sets the persisted page error used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public string? PersistedPageError { get; set; }

    /// <summary>
    /// Gets or sets the persisted not-found flag used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public bool PersistedNotFound { get; set; }

    /// <summary>
    /// Gets or sets the persisted roster snapshot used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public PagedResult<CampaignParticipantRosterItem>? PersistedRoster { get; set; }

    /// <summary>
    /// Gets or sets the persisted roster error used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public string? PersistedRosterError { get; set; }

    /// <summary>
    /// Gets or sets whether startup initialization already completed during prerender.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <summary>
    /// The loaded campaign detail, or <see langword="null"/> when unavailable.
    /// </summary>
    private CampaignDetailResult? _detail;

    /// <summary>
    /// The current page-level error message.
    /// </summary>
    private string? _pageError;

    /// <summary>
    /// Indicates whether the requested campaign was not found.
    /// </summary>
    private bool _notFound;

    /// <summary>
    /// Indicates whether campaign detail is being loaded.
    /// </summary>
    private bool _isLoading;

    /// <summary>
    /// The loaded roster snapshot, or <see langword="null"/> when unavailable.
    /// </summary>
    private PagedResult<CampaignParticipantRosterItem>? _roster;

    /// <summary>
    /// The current roster-level error message.
    /// </summary>
    private string? _rosterError;

    /// <summary>
    /// Indicates whether the roster is being loaded.
    /// </summary>
    private bool _rosterLoading;

    /// <summary>
    /// The active workspace tab; only the evaluate tab is functional in this phase.
    /// </summary>
    private string _activeTab = EvaluateTabName;

    /// <summary>
    /// Indicates whether the tab query parameter has been applied to component state.
    /// </summary>
    private bool _tabQueryApplied;

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (_tabQueryApplied)
        {
            return;
        }

        _tabQueryApplied = true;
        // Only the evaluate tab is functional; any other tab value falls back to it.
        _activeTab = EvaluateTabName;
    }

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        if (Initialized)
        {
            _detail = PersistedDetail;
            _pageError = PersistedPageError;
            _notFound = PersistedNotFound;
            _roster = PersistedRoster;
            _rosterError = PersistedRosterError;
            _isLoading = false;
            return;
        }

        _isLoading = true;
        await LoadDetailAsync();
        PersistStartupState();
        Initialized = true;
    }

    /// <summary>
    /// Loads campaign detail and, on success, the initial roster page.
    /// </summary>
    /// <returns>A task that completes when both loads are finished.</returns>
    private async Task LoadDetailAsync()
    {
        _pageError = null;
        _notFound = false;

        var result = await campaignQueryService.GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = CampaignId },
            ComponentCancellationToken);

        var detailLoaded = false;
        result.Switch(
            detail =>
            {
                _detail = detail;
                detailLoaded = true;
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
                    _notFound = true;
                    return;
                }

                _pageError = FirstNonBlank(problem.Detail, "Failed to load campaign. Please retry.");
            });

        _isLoading = false;

        if (detailLoaded)
        {
            _rosterLoading = true;
            await LoadRosterAsync();
            _rosterLoading = false;
        }
    }

    /// <summary>
    /// Loads the initial roster page with default filters.
    /// </summary>
    /// <returns>A task that completes when the roster load is finished.</returns>
    private async Task LoadRosterAsync()
    {
        _rosterError = null;

        var result = await participantQueryService.GetParticipantRosterAsync(
            new GetCampaignParticipantRosterInput
            {
                CampaignId = CampaignId,
                Page = GetCampaignParticipantRosterInput.DefaultPage,
                PageSize = GetCampaignParticipantRosterInput.DefaultPageSize
            },
            ComponentCancellationToken);

        result.Switch(
            roster => _roster = roster,
            problem =>
            {
                if (problem.Kind == ServiceProblemKind.Forbidden)
                {
                    navigationManager.NavigateTo("/Account/AccessDenied", forceLoad: true);
                    return;
                }

                _rosterError = FirstNonBlank(problem.Detail, "Failed to load the roster. Please retry.");
                _roster = null;
            });
    }

    /// <summary>
    /// Retries the detail load after a recoverable error.
    /// </summary>
    /// <returns>A task that completes when the retried load is finished.</returns>
    private async Task RetryAsync()
    {
        _isLoading = true;
        await LoadDetailAsync();
        PersistStartupState();
    }

    /// <summary>
    /// Retries the roster load after a recoverable error.
    /// </summary>
    /// <returns>A task that completes when the retried load is finished.</returns>
    private async Task RetryRosterAsync()
    {
        _rosterLoading = true;
        await LoadRosterAsync();
        _rosterLoading = false;
        PersistStartupState();
    }

    /// <summary>
    /// Selects the evaluate tab, pushing the <c>tab</c> query parameter when absent.
    /// </summary>
    /// <returns>A task that completes when navigation is initiated.</returns>
    private Task SelectEvaluateTabAsync()
    {
        if (!string.Equals(TabQuery, EvaluateTabName, StringComparison.OrdinalIgnoreCase))
        {
            navigationManager.NavigateTo($"/campaigns/{CampaignId}?tab={EvaluateTabName}");
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Persists the current startup state for prerender-to-interactive restoration.
    /// </summary>
    private void PersistStartupState()
    {
        PersistedDetail = _detail;
        PersistedPageError = _pageError;
        PersistedNotFound = _notFound;
        PersistedRoster = _roster;
        PersistedRosterError = _rosterError;
    }

    /// <summary>
    /// Formats a campaign's date range for display.
    /// </summary>
    /// <param name="detail">The campaign detail payload.</param>
    /// <returns>The formatted date range.</returns>
    protected static string FormatCampaignDates(CampaignDetailResult detail)
        => detail.PlannedEndDate is null
            ? $"Starts {detail.StartDate:MMM d, yyyy}"
            : $"{detail.StartDate:MMM d, yyyy} – {detail.PlannedEndDate.Value:MMM d, yyyy}";

    /// <summary>
    /// Maps a campaign lifecycle status to its Bootstrap badge class.
    /// </summary>
    /// <param name="status">The campaign lifecycle status.</param>
    /// <returns>The badge background class.</returns>
    protected static string CampaignStatusBadgeClass(CampaignStatus status) => status switch
    {
        CampaignStatus.Active => "text-bg-success",
        _ => "text-bg-secondary"
    };

    /// <summary>
    /// Returns the first non-blank message from the supplied candidates.
    /// </summary>
    /// <param name="candidates">The candidate messages in preference order.</param>
    /// <returns>The first non-blank candidate.</returns>
    private static string FirstNonBlank(params string?[] candidates)
        => candidates.First(candidate => !string.IsNullOrWhiteSpace(candidate))!;
}
