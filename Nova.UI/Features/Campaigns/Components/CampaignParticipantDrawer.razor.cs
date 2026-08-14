using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.UI.Components;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Drawer presenting the selected campaign roster participant's details with loading, failure, and
/// loaded states, plus prev/next sequence navigation driven by the workspace page.
/// </summary>
/// <param name="participantQueryService">The query service used to load participant details.</param>
/// <param name="jsRuntime">The JavaScript runtime used to focus the close button when the drawer opens.</param>
public partial class CampaignParticipantDrawer(
    ICampaignParticipantQueryService participantQueryService,
    IJSRuntime jsRuntime) : NovaComponentBase
{
    /// <summary>
    /// The DOM identifier of the close button, focused when the drawer opens.
    /// </summary>
    private const string CloseButtonId = "participant-drawer-close";

    /// <summary>
    /// Represents the detail-load state rendered by the drawer body.
    /// </summary>
    private enum DetailLoadState
    {
        Loading,
        Loaded,
        Failed
    }

    /// <summary>
    /// Gets or sets the campaign identifier used to scope the detail query.
    /// </summary>
    [Parameter, EditorRequired]
    public long CampaignId { get; set; }

    /// <summary>
    /// Gets or sets the selected participant assignment identifier.
    /// </summary>
    [Parameter, EditorRequired]
    public long ParticipantId { get; set; }

    /// <summary>
    /// Gets or sets the selected roster item, or <see langword="null"/> when it is not on the loaded page.
    /// </summary>
    [Parameter]
    public CampaignParticipantRosterItem? RosterItem { get; set; }

    /// <summary>
    /// Gets or sets the 1-based position of the participant within the roster sequence,
    /// or <see langword="null"/> when the participant is off the loaded page.
    /// </summary>
    [Parameter]
    public int? Position { get; set; }

    /// <summary>
    /// Gets or sets the total number of participants in the roster sequence.
    /// </summary>
    [Parameter]
    public int TotalCount { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the participant has a predecessor in the roster sequence.
    /// </summary>
    [Parameter]
    public bool HasPrevious { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the participant has a successor in the roster sequence.
    /// </summary>
    [Parameter]
    public bool HasNext { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the previous participant is requested.
    /// </summary>
    [Parameter]
    public EventCallback OnPrevious { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the next participant is requested.
    /// </summary>
    [Parameter]
    public EventCallback OnNext { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the drawer is closed.
    /// </summary>
    [Parameter]
    public EventCallback OnClose { get; set; }

    /// <summary>
    /// Gets or sets the persisted detail payload used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public CampaignParticipantDetailDto? PersistedDetail { get; set; }

    /// <summary>
    /// Gets or sets the persisted detail error used across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public string? PersistedDetailError { get; set; }

    /// <summary>
    /// Gets or sets whether startup initialization already completed during prerender.
    /// </summary>
    [PersistentState]
    public bool Initialized { get; set; }

    /// <summary>
    /// The loaded participant detail, or <see langword="null"/> when unavailable.
    /// </summary>
    private CampaignParticipantDetailDto? _detail;

    /// <summary>
    /// The current detail-load error message.
    /// </summary>
    private string? _detailError;

    /// <summary>
    /// The current detail-load state.
    /// </summary>
    private DetailLoadState _detailState;

    /// <summary>
    /// Monotonic guard used to discard stale detail responses.
    /// </summary>
    private int _detailRequestSequence;

    /// <summary>
    /// The participant identifier the current state was loaded for, or <see langword="null"/> before the first load.
    /// </summary>
    private long? _loadedParticipantId;

    /// <summary>
    /// Gets the drawer heading, preferring the loaded detail name and falling back to the roster item
    /// or a generic label.
    /// </summary>
    private string Heading => _detail?.DisplayName ?? RosterItem?.DisplayName ?? "Participant";

    /// <inheritdoc />
    protected override async Task OnInitializedAsync()
    {
        if (Initialized)
        {
            await RestorePersistedStateAsync();
            return;
        }

        await LoadDetailAsync();
    }

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (ParticipantId != _loadedParticipantId)
        {
            await LoadDetailAsync();
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender)
        {
            await jsRuntime.InvokeVoidAsync("novaCampaignWorkspaceFocus", CloseButtonId);
        }
    }

    /// <summary>
    /// Restores the detail state persisted during prerender, reloading defensively when neither a
    /// detail nor an error was persisted.
    /// </summary>
    /// <returns>A task that completes when restoration is finished.</returns>
    private async Task RestorePersistedStateAsync()
    {
        _detail = PersistedDetail;
        _detailError = PersistedDetailError;
        _loadedParticipantId = ParticipantId;
        _detailState = PersistedDetailError is not null
            ? DetailLoadState.Failed
            : PersistedDetail is not null
                ? DetailLoadState.Loaded
                : DetailLoadState.Loading;

        if (_detailState == DetailLoadState.Loading)
        {
            await LoadDetailAsync();
        }
    }

    /// <summary>
    /// Loads the participant detail for the current selection, discarding stale responses.
    /// </summary>
    /// <returns>A task that completes when the load is finished.</returns>
    private async Task LoadDetailAsync()
    {
        _detail = null;
        _detailError = null;
        _detailState = DetailLoadState.Loading;
        _loadedParticipantId = ParticipantId;

        var requestId = ++_detailRequestSequence;
        var input = new GetCampaignParticipantDetailInput
        {
            CampaignId = CampaignId,
            PlayerCampaignAssignmentId = ParticipantId
        };

        var result = await participantQueryService.GetParticipantDetailAsync(input, ComponentCancellationToken);

        if (requestId != _detailRequestSequence)
        {
            return;
        }

        result.Switch(
            detail =>
            {
                if (detail is null)
                {
                    _detailState = DetailLoadState.Failed;
                    _detailError = "Participant details are unavailable. Please retry.";
                    return;
                }

                _detail = detail;
                _detailState = DetailLoadState.Loaded;
            },
            problem =>
            {
                _detailState = DetailLoadState.Failed;
                _detailError = FirstNonBlank(problem.Detail, "Failed to load participant details. Please retry.");
            });

        PersistedDetail = _detail;
        PersistedDetailError = _detailError;
        Initialized = true;
    }

    /// <summary>
    /// Closes the drawer via the parent page.
    /// </summary>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task CloseAsync() => OnClose.InvokeAsync();

    /// <summary>
    /// Closes the drawer when Escape is pressed inside the panel.
    /// </summary>
    /// <param name="args">The keyboard event.</param>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private Task OnKeyDownAsync(KeyboardEventArgs args)
        => string.Equals(args.Key, "Escape", StringComparison.OrdinalIgnoreCase)
            ? CloseAsync()
            : Task.CompletedTask;

    /// <summary>
    /// Reloads the participant detail after a load failure.
    /// </summary>
    /// <returns>A task that completes when the reload is finished.</returns>
    private Task RetryAsync() => LoadDetailAsync();

    /// <summary>
    /// Maps a campaign lifecycle status to its Bootstrap badge class.
    /// </summary>
    /// <param name="status">The campaign lifecycle status.</param>
    /// <returns>The badge background class.</returns>
    private static string CampaignStatusBadgeClass(CampaignStatus status) => status switch
    {
        CampaignStatus.Active => "text-bg-success",
        _ => "text-bg-secondary"
    };

    /// <summary>
    /// Formats a timestamp for drawer metadata.
    /// </summary>
    /// <param name="value">The timestamp to format.</param>
    /// <returns>The display timestamp.</returns>
    private static string FormatTimestamp(DateTimeOffset value) => value.ToString("MMM d, yyyy h:mm tt");

    /// <summary>
    /// Returns the first non-blank message from the supplied candidates.
    /// </summary>
    /// <param name="candidates">The candidate messages in preference order.</param>
    /// <returns>The first non-blank candidate.</returns>
    private static string FirstNonBlank(params string?[] candidates)
        => candidates.First(candidate => !string.IsNullOrWhiteSpace(candidate))!;
}
