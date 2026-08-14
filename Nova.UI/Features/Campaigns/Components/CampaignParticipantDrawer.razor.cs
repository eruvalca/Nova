using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.Shared.Validation;
using Nova.UI.Components;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Drawer presenting the selected campaign roster participant's details with loading, failure, and
/// loaded states, prev/next sequence navigation driven by the workspace page, and note/tag
/// evaluation mutations. Closed campaigns render read-only with all mutation controls hidden.
/// </summary>
/// <remarks>
/// <para>
/// Focus management: opening the drawer captures the activating roster row/card, installs a
/// document-level Tab trap that cycles focus through the dialog, and focuses the close button.
/// Closing removes the trap and restores focus to the selected participant's visible row/card,
/// falling back to the captured activating element, then to nothing. A participant-change render
/// that leaves focus outside the dialog (a boundary move renders the focused prev/next button
/// disabled, which drops focus to the body) pulls focus back to the close button so the trap and
/// Escape keep working. Disposal removes the trap through the restoring close path, so a browser
/// Back/Forward that drops the participant query parameter (unmounting the drawer without a close
/// click) still returns focus to the selected participant's visible row/card; on a real route
/// teardown every focus-return candidate is disconnected or invisible, so no focus change occurs
/// and no stale trap remains.
/// </para>
/// <para>
/// Arrow-key prev/next navigation is intentionally not implemented (scope decision); keyboard
/// users reach the header prev/next buttons through the Tab cycle, and Escape closes the drawer
/// along the same focus-return path.
/// </para>
/// </remarks>
/// <param name="participantQueryService">The query service used to load participant details.</param>
/// <param name="noteService">The evaluation-note service used by note create/edit/delete mutations.</param>
/// <param name="tagApplicationService">The tag-application service used by tag apply/remove mutations.</param>
/// <param name="tagDefinitionQueryService">The tag-definition query service used to load active tag choices.</param>
/// <param name="jsRuntime">The JavaScript runtime used to import the collocated drawer module.</param>
public partial class CampaignParticipantDrawer(
    ICampaignParticipantQueryService participantQueryService,
    ICampaignEvaluationNoteService noteService,
    ICampaignTagApplicationService tagApplicationService,
    ITagDefinitionQueryService tagDefinitionQueryService,
    IJSRuntime jsRuntime) : NovaComponentBase
{
    /// <summary>
    /// The dialog panel element, used as the focus-trap container and the boundary for focus checks.
    /// </summary>
    private ElementReference _dialog;

    /// <summary>
    /// The close button element, focused when the drawer opens and used as the anchor when focus
    /// must be pulled back into the dialog.
    /// </summary>
    private ElementReference _closeButton;

    /// <summary>
    /// The mutation error summary element, focused after a mutation failure so assistive technology
    /// announces the message.
    /// </summary>
    private ElementReference _errorSummary;

    /// <summary>
    /// The lazily imported collocated drawer module managing the focus trap and focus return.
    /// </summary>
    private readonly Lazy<Task<IJSObjectReference>> _moduleTask = new(() => jsRuntime
        .InvokeAsync<IJSObjectReference>(
            "import", "./_content/Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.js")
        .AsTask());

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
    /// Identifies which evaluation mutation is currently in flight so sibling controls disable and
    /// the active control can render pending state.
    /// </summary>
    private enum MutationKind
    {
        AddNote,
        EditNote,
        DeleteNote,
        ApplyTag,
        RemoveTag
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
    /// Gets or sets the active tag-definition choices persisted across prerender and interactive attach.
    /// </summary>
    [PersistentState]
    public IReadOnlyList<TagDefinitionDto>? PersistedTagChoices { get; set; }

    /// <summary>
    /// The loaded participant detail, or <see langword="null"/> when unavailable.
    /// </summary>
    private CampaignParticipantDetailDto? _detail;

    /// <summary>
    /// The current detail-load error message.
    /// </summary>
    private string? _detailError;

    /// <summary>
    /// The DOM identifier of the roster row corresponding to the selected participant, used as the
    /// primary focus-return target when the drawer closes.
    /// </summary>
    private string FallbackFocusId => $"roster-row-{ParticipantId}";

    /// <summary>
    /// Whether the focus trap was installed during the interactive first render, so the prerendered
    /// instance never issues JS interop and disposal only removes a trap that actually exists.
    /// </summary>
    private bool _focusTrapInstalled;

    /// <summary>
    /// The participant identifier rendered by the last completed render pass, used to detect
    /// participant changes after which focus may need to be pulled back into the dialog.
    /// </summary>
    private long? _lastRenderedParticipantId;

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
    /// The mutation error message shown in the drawer-level summary region after a failed mutation.
    /// </summary>
    private string? _mutationError;

    /// <summary>
    /// The success status message preserved across the post-mutation detail refresh.
    /// </summary>
    private string? _statusMessage;

    /// <summary>
    /// Indicates whether a mutation is currently in flight.
    /// </summary>
    private bool _isMutating;

    /// <summary>
    /// The in-flight mutation kind, used to disable sibling controls and render pending state.
    /// </summary>
    private MutationKind? _mutatingKind;

    /// <summary>
    /// Indicates that the error summary should receive focus after the next render.
    /// </summary>
    private bool _focusErrorSummary;

    /// <summary>
    /// The active tag-definition choices used by the apply picker, or <see langword="null"/> while
    /// loading or when the drawer never needs them.
    /// </summary>
    private IReadOnlyList<TagDefinitionDto>? _tagChoices;

    /// <summary>
    /// The tag-choices load error, shown inline in the apply section with its own retry.
    /// </summary>
    private string? _tagChoicesError;

    /// <summary>
    /// Indicates whether tag choices have already been loaded (or restored) to avoid refetching them.
    /// </summary>
    private bool _tagChoicesLoaded;

    /// <summary>
    /// Set when a conflict refresh reveals the campaign is Closed, forcing read-only mode even though
    /// the originally rendered detail was Active.
    /// </summary>
    private bool _enteredReadOnlyFromConflict;

    // ── Note mutation state ───────────────────────────────────────────────────

    /// <summary>
    /// Indicates whether the add-note form is visible.
    /// </summary>
    private bool _showAddNoteForm;

    /// <summary>
    /// The add-note draft content.
    /// </summary>
    private string _addNoteContent = string.Empty;

    /// <summary>
    /// Client-side validation errors for the add-note form keyed by member name.
    /// </summary>
    private Dictionary<string, string[]> _addNoteErrors = [];

    /// <summary>
    /// The note identifier currently being edited inline, or <see langword="null"/> when no edit is open.
    /// </summary>
    private long? _editingNoteId;

    /// <summary>
    /// The inline edit draft content.
    /// </summary>
    private string _editNoteContent = string.Empty;

    /// <summary>
    /// Client-side validation errors for the inline edit form keyed by member name.
    /// </summary>
    private Dictionary<string, string[]> _editNoteErrors = [];

    /// <summary>
    /// The note identifier whose delete confirmation is open, or <see langword="null"/>.
    /// </summary>
    private long? _deletingNoteId;

    /// <summary>
    /// Indicates whether the open delete confirmation checkbox is checked.
    /// </summary>
    private bool _deleteNoteConfirmed;

    // ── Tag mutation state ─────────────────────────────────────────────────────

    /// <summary>
    /// The tag-definition identifier selected in the apply picker, or <see langword="null"/>.
    /// </summary>
    private long? _selectedTagId;

    /// <summary>
    /// The campaign tag application whose remove confirmation is open, or <see langword="null"/>.
    /// </summary>
    private long? _removingTagApplicationId;

    /// <summary>
    /// Indicates whether the open remove confirmation checkbox is checked.
    /// </summary>
    private bool _removeTagConfirmed;

    /// <summary>
    /// Gets the drawer heading, preferring the loaded detail name and falling back to the roster item
    /// or a generic label.
    /// </summary>
    private string Heading => _detail?.DisplayName ?? RosterItem?.DisplayName ?? "Participant";

    /// <summary>
    /// Gets a value indicating whether the drawer is read-only because the campaign is Closed or a
    /// conflict refresh revealed a Closed campaign.
    /// </summary>
    private bool IsReadOnly => _detail is { CampaignStatus: CampaignStatus.Closed } || _enteredReadOnlyFromConflict;

    /// <summary>
    /// Gets the tag definitions that can still be applied: active choices minus already-applied
    /// definitions, ordered by name.
    /// </summary>
    private IReadOnlyList<TagDefinitionDto> RemainingTagChoices =>
        _tagChoices is null
            ? []
            : _tagChoices
                .Where(choice => _detail is null || _detail.AppliedTags.All(applied => applied.PlayerTagId != choice.PlayerTagId))
                .OrderBy(choice => choice.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

    /// <summary>
    /// Gets a value indicating whether the supplied mutation kind is the one currently in flight.
    /// </summary>
    /// <param name="kind">The mutation kind to test.</param>
    /// <returns><see langword="true"/> when that mutation is pending.</returns>
    private bool IsPending(MutationKind kind) => _isMutating && _mutatingKind == kind;

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
            // Navigating to another participant is an intentional user-action boundary: clear the
            // previous participant's mutation feedback and conflict-read-only flag before reloading.
            _statusMessage = null;
            _mutationError = null;
            await LoadDetailAsync();
        }
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var module = await _moduleTask.Value;

        if (firstRender)
        {
            await module.InvokeVoidAsync("open", _dialog, _closeButton);
            _focusTrapInstalled = true;
            _lastRenderedParticipantId = ParticipantId;
            return;
        }

        if (_focusErrorSummary && _detail is not null && _mutationError is not null)
        {
            _focusErrorSummary = false;
            await _errorSummary.FocusAsync();
        }

        if (!_focusTrapInstalled || ParticipantId == _lastRenderedParticipantId)
        {
            return;
        }

        _lastRenderedParticipantId = ParticipantId;

        // A boundary move renders the clicked prev/next button disabled, which drops focus to
        // <body>; re-focus inside the dialog so the trap and Escape keep working.
        await module.InvokeVoidAsync("restoreFocus", _dialog, _closeButton);
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
        _tagChoices = PersistedTagChoices;
        _tagChoicesLoaded = PersistedTagChoices is not null;
        _detailState = PersistedDetailError is not null
            ? DetailLoadState.Failed
            : PersistedDetail is not null
                ? DetailLoadState.Loaded
                : DetailLoadState.Loading;

        if (_detailState == DetailLoadState.Loading)
        {
            await LoadDetailAsync();
            return;
        }

        await LoadTagChoicesIfNeededAsync();
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
        _enteredReadOnlyFromConflict = false;

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

        if (_detailState == DetailLoadState.Loaded)
        {
            await LoadTagChoicesIfNeededAsync();
        }
    }

    /// <summary>
    /// Reloads the participant detail after a successful or conflicting mutation, preserving the
    /// status message so the refresh cannot clear it before it renders. When the refresh itself
    /// fails, the previously loaded detail and error are kept so the drawer does not flip to the
    /// load-failure state and lose the mutation feedback.
    /// </summary>
    /// <returns>A task that completes when the reload finishes.</returns>
    private async Task RefreshDetailAsync()
    {
        var previousDetail = _detail;
        var previousDetailError = _detailError;
        var previousState = _detailState;
        var previousParticipantId = _loadedParticipantId;

        await LoadDetailAsync();

        if (_detailState == DetailLoadState.Failed && previousDetail is not null)
        {
            _detail = previousDetail;
            _detailError = previousDetailError;
            _detailState = previousState;
            _loadedParticipantId = previousParticipantId;
            PersistedDetail = _detail;
            PersistedDetailError = _detailError;
        }
    }

    /// <summary>
    /// Loads active tag-definition choices when the loaded detail can apply tags and the choices
    /// have not been loaded or restored yet.
    /// </summary>
    /// <returns>A task that completes when the choice load finishes.</returns>
    private async Task LoadTagChoicesIfNeededAsync()
    {
        if (_tagChoicesLoaded || _detail is not { Capabilities.CanApplyTag: true } || IsReadOnly)
        {
            return;
        }

        await LoadTagChoicesAsync();
    }

    /// <summary>
    /// Loads active tag-definition choices for the apply picker, recording a failure inline without
    /// failing the drawer's detail render.
    /// </summary>
    /// <returns>A task that completes when the choice load finishes.</returns>
    private async Task LoadTagChoicesAsync()
    {
        _tagChoicesError = null;

        var result = await tagDefinitionQueryService.GetChoicesAsync(ComponentCancellationToken);

        result.Switch(
            choices =>
            {
                _tagChoices = choices;
                _tagChoicesLoaded = true;
                PersistedTagChoices = _tagChoices;
            },
            problem => _tagChoicesError = FirstNonBlank(problem.Detail, "Couldn't load tag choices."));
    }

    /// <summary>
    /// Retries the tag-choices load after an inline failure.
    /// </summary>
    /// <returns>A task that completes when the choice load finishes.</returns>
    private Task RetryTagChoicesAsync() => LoadTagChoicesAsync();

    /// <summary>
    /// Closes the drawer via the parent page, removing the focus trap and restoring focus to the
    /// selected participant's roster row before delivering the callback.
    /// </summary>
    /// <returns>A task that completes when the callback is delivered.</returns>
    private async Task CloseAsync()
    {
        if (_focusTrapInstalled)
        {
            _focusTrapInstalled = false;
            var module = await _moduleTask.Value;
            await module.InvokeVoidAsync("close", FallbackFocusId);
        }

        await OnClose.InvokeAsync();
    }

    /// <summary>
    /// Removes the focus trap when the component is disposed. Disposal also happens when browser
    /// Back/Forward removes the participant query parameter and the workspace stops rendering the
    /// drawer without a close click, so the restoring close path is used: focus returns to the
    /// selected participant's visible row/card when one is still on screen, and on a real route
    /// teardown every candidate is disconnected or invisible so no focus change occurs.
    /// </summary>
    /// <returns>A task that completes when the trap is removed.</returns>
    protected override async ValueTask DisposeAsyncCore()
    {
        if (!_moduleTask.IsValueCreated)
        {
            return;
        }

        try
        {
            var module = await _moduleTask.Value;

            if (_focusTrapInstalled)
            {
                _focusTrapInstalled = false;
                await module.InvokeVoidAsync("close", FallbackFocusId);
            }

            await module.DisposeAsync();
        }
        catch (JSDisconnectedException)
        {
            // The circuit is gone; the browser tore the trap and module down with the document.
        }
    }

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

    /// <summary>
    /// Flattens field-level validation messages when the problem carries no detail text.
    /// </summary>
    /// <param name="problem">The service problem.</param>
    /// <returns>The joined field messages, or <see langword="null"/> when the problem has no errors.</returns>
    private static string? FlattenValidationErrors(ServiceProblem problem)
        => problem.Errors is { Count: > 0 }
            ? string.Join(" ", problem.Errors.SelectMany(pair => pair.Value))
            : null;

    /// <summary>
    /// Maps a service problem to a human-readable mutation error message, preferring the server
    /// detail and falling back to a kind-appropriate message.
    /// </summary>
    /// <param name="problem">The service problem.</param>
    /// <returns>The mutation error message.</returns>
    private static string MutationErrorMessage(ServiceProblem problem) => problem.Kind switch
    {
        ServiceProblemKind.Conflict => FirstNonBlank(problem.Detail, "The change conflicts with the current state. Please try again."),
        ServiceProblemKind.Validation => FirstNonBlank(problem.Detail, FlattenValidationErrors(problem), "One or more fields are invalid."),
        ServiceProblemKind.Forbidden => FirstNonBlank(problem.Detail, "You're not authorized to make this change."),
        ServiceProblemKind.NotFound => FirstNonBlank(problem.Detail, "The item is no longer available."),
        ServiceProblemKind.BadRequest => FirstNonBlank(problem.Detail, "The request was invalid."),
        _ => FirstNonBlank(problem.Detail, "The change could not be completed. Please try again.")
    };

    /// <summary>
    /// Runs a mutation with the shared pending guard and feedback plumbing: sets the in-flight
    /// state, clears the previous error, runs the service call, and maps transport failures to the
    /// error summary.
    /// </summary>
    /// <param name="kind">The mutation kind used for pending state.</param>
    /// <param name="serviceCall">The service call plus result handling.</param>
    /// <returns>A task that completes when the mutation settles.</returns>
    private async Task RunMutationAsync(MutationKind kind, Func<Task> serviceCall)
    {
        if (_isMutating)
        {
            return;
        }

        _isMutating = true;
        _mutatingKind = kind;
        _mutationError = null;
        _statusMessage = null;

        try
        {
            await serviceCall();
        }
        catch (Exception ex) when (ex is HttpRequestException or OperationCanceledException)
        {
            if (ComponentCancellationToken.IsCancellationRequested)
            {
                return;
            }

            _mutationError = "Could not reach the server. Check your connection and retry.";
            FocusMutationError();
        }
        finally
        {
            _isMutating = false;
            _mutatingKind = null;
        }
    }

    /// <summary>
    /// Applies shared mutation result handling: on success runs the success action, sets the status
    /// message, and refreshes the detail; on any problem sets the error summary. A Conflict also
    /// refreshes the detail so stale screens heal, and enters read-only mode when the refreshed
    /// detail is Closed.
    /// </summary>
    /// <typeparam name="T">The mutation result payload type.</typeparam>
    /// <param name="result">The service result.</param>
    /// <param name="kind">The mutation kind for pending state.</param>
    /// <param name="successMessage">The status message shown on success.</param>
    /// <param name="onSuccess">The action to run on success before the detail refreshes.</param>
    /// <returns>A task that completes when result handling finishes.</returns>
    private async Task HandleMutationResultAsync<T>(
        ServiceResult<T> result,
        MutationKind kind,
        string successMessage,
        Action onSuccess)
    {
        var succeeded = false;
        var conflicted = false;
        result.Switch(
            _ =>
            {
                onSuccess();
                succeeded = true;
            },
            problem =>
            {
                _mutationError = MutationErrorMessage(problem);
                conflicted = problem.Kind == ServiceProblemKind.Conflict;
            });

        if (succeeded)
        {
            _statusMessage = successMessage;
            await RefreshDetailAsync();
            return;
        }

        if (conflicted)
        {
            await RefreshDetailAsync();
            if (_detail is { CampaignStatus: CampaignStatus.Closed })
            {
                _enteredReadOnlyFromConflict = true;
            }
        }

        FocusMutationError();
    }

    /// <summary>
    /// Requests focus on the mutation error summary after the next render.
    /// </summary>
    private void FocusMutationError()
    {
        _focusErrorSummary = true;
    }

    /// <summary>
    /// Opens the add-note form, closing any other open note interaction.
    /// </summary>
    private void ShowAddNoteForm()
    {
        _showAddNoteForm = true;
        _addNoteContent = string.Empty;
        _addNoteErrors = [];
        _editingNoteId = null;
        _editNoteContent = string.Empty;
        _editNoteErrors = [];
        _deletingNoteId = null;
        _deleteNoteConfirmed = false;
        _statusMessage = null;
    }

    /// <summary>
    /// Closes the add-note form and clears its draft.
    /// </summary>
    private void CancelAddNote()
    {
        _showAddNoteForm = false;
        _addNoteContent = string.Empty;
        _addNoteErrors = [];
    }

    /// <summary>
    /// Validates and submits the add-note form.
    /// </summary>
    /// <returns>A task that completes when the mutation settles.</returns>
    private async Task SaveAddNoteAsync()
    {
        if (_detail is not { } detail || _isMutating)
        {
            return;
        }

        var input = new AddEvaluationNoteInput
        {
            PlayerCampaignAssignmentId = detail.PlayerCampaignAssignmentId,
            Content = _addNoteContent
        };
        _addNoteErrors = InputValidator.Validate(input);
        if (_addNoteErrors.Count > 0)
        {
            return;
        }

        await RunMutationAsync(
            MutationKind.AddNote,
            async () =>
            {
                var result = await noteService.AddAsync(input, ComponentCancellationToken);
                await HandleMutationResultAsync(
                    result,
                    MutationKind.AddNote,
                    "Note added.",
                    () =>
                    {
                        _showAddNoteForm = false;
                        _addNoteContent = string.Empty;
                        _addNoteErrors = [];
                    });
            });
    }

    /// <summary>
    /// Opens the inline editor for the supplied note, closing any other open note interaction.
    /// </summary>
    /// <param name="note">The note to edit.</param>
    private void BeginEditNote(CampaignParticipantNoteDto note)
    {
        _editingNoteId = note.NoteId;
        _editNoteContent = note.Content;
        _editNoteErrors = [];
        _showAddNoteForm = false;
        _addNoteContent = string.Empty;
        _addNoteErrors = [];
        _deletingNoteId = null;
        _deleteNoteConfirmed = false;
        _statusMessage = null;
    }

    /// <summary>
    /// Cancels the open inline note edit, restoring the rendered note text.
    /// </summary>
    private void CancelEditNote()
    {
        _editingNoteId = null;
        _editNoteContent = string.Empty;
        _editNoteErrors = [];
    }

    /// <summary>
    /// Validates and submits the inline note edit.
    /// </summary>
    /// <param name="note">The note being edited.</param>
    /// <returns>A task that completes when the mutation settles.</returns>
    private async Task SaveEditNoteAsync(CampaignParticipantNoteDto note)
    {
        if (_isMutating)
        {
            return;
        }

        var input = new EditEvaluationNoteInput
        {
            NoteId = note.NoteId,
            Content = _editNoteContent
        };
        _editNoteErrors = InputValidator.Validate(input);
        if (_editNoteErrors.Count > 0)
        {
            return;
        }

        await RunMutationAsync(
            MutationKind.EditNote,
            async () =>
            {
                var result = await noteService.EditAsync(input, ComponentCancellationToken);
                await HandleMutationResultAsync(
                    result,
                    MutationKind.EditNote,
                    "Note updated.",
                    () =>
                    {
                        _editingNoteId = null;
                        _editNoteContent = string.Empty;
                        _editNoteErrors = [];
                    });
            });
    }

    /// <summary>
    /// Opens the delete confirmation for the supplied note.
    /// </summary>
    /// <param name="note">The note to delete.</param>
    private void BeginDeleteNote(CampaignParticipantNoteDto note)
    {
        _deletingNoteId = note.NoteId;
        _deleteNoteConfirmed = false;
        _showAddNoteForm = false;
        _editingNoteId = null;
        _statusMessage = null;
    }

    /// <summary>
    /// Closes the open note delete confirmation.
    /// </summary>
    private void CancelDeleteNote()
    {
        _deletingNoteId = null;
        _deleteNoteConfirmed = false;
    }

    /// <summary>
    /// Deletes the confirmed note and refreshes the detail on success.
    /// </summary>
    /// <param name="note">The note to delete.</param>
    /// <returns>A task that completes when the mutation settles.</returns>
    private async Task ConfirmDeleteNoteAsync(CampaignParticipantNoteDto note)
    {
        if (_isMutating)
        {
            return;
        }

        await RunMutationAsync(
            MutationKind.DeleteNote,
            async () =>
            {
                var result = await noteService.DeleteAsync(note.NoteId, ComponentCancellationToken);
                await HandleMutationResultAsync(
                    result,
                    MutationKind.DeleteNote,
                    "Note deleted.",
                    () =>
                    {
                        _deletingNoteId = null;
                        _deleteNoteConfirmed = false;
                    });
            });
    }

    /// <summary>
    /// Applies the currently selected tag definition and refreshes the detail on success.
    /// </summary>
    /// <returns>A task that completes when the mutation settles.</returns>
    private async Task ApplySelectedTagAsync()
    {
        if (_detail is not { } detail || _isMutating || _selectedTagId is null)
        {
            return;
        }

        var input = new ApplyCampaignTagApplicationInput
        {
            PlayerCampaignAssignmentId = detail.PlayerCampaignAssignmentId,
            PlayerTagId = _selectedTagId.Value
        };

        await RunMutationAsync(
            MutationKind.ApplyTag,
            async () =>
            {
                var result = await tagApplicationService.ApplyAsync(input, ComponentCancellationToken);
                await HandleMutationResultAsync(
                    result,
                    MutationKind.ApplyTag,
                    "Tag applied.",
                    () => _selectedTagId = null);
            });
    }

    /// <summary>
    /// Opens the remove confirmation for the supplied tag application.
    /// </summary>
    /// <param name="tag">The tag application to remove.</param>
    private void BeginRemoveTag(CampaignParticipantTagApplicationDto tag)
    {
        _removingTagApplicationId = tag.CampaignTagApplicationId;
        _removeTagConfirmed = false;
        _statusMessage = null;
    }

    /// <summary>
    /// Closes the open tag remove confirmation.
    /// </summary>
    private void CancelRemoveTag()
    {
        _removingTagApplicationId = null;
        _removeTagConfirmed = false;
    }

    /// <summary>
    /// Removes the confirmed tag application and refreshes the detail on success.
    /// </summary>
    /// <param name="tag">The tag application to remove.</param>
    /// <returns>A task that completes when the mutation settles.</returns>
    private async Task ConfirmRemoveTagAsync(CampaignParticipantTagApplicationDto tag)
    {
        if (_isMutating)
        {
            return;
        }

        await RunMutationAsync(
            MutationKind.RemoveTag,
            async () =>
            {
                var result = await tagApplicationService.RemoveAsync(
                    new RemoveCampaignTagApplicationInput { CampaignTagApplicationId = tag.CampaignTagApplicationId },
                    ComponentCancellationToken);
                await HandleMutationResultAsync(
                    result,
                    MutationKind.RemoveTag,
                    "Tag removed.",
                    () =>
                    {
                        _removingTagApplicationId = null;
                        _removeTagConfirmed = false;
                    });
            });
    }
}
