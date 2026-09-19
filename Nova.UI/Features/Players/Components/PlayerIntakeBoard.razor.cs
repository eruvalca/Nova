using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.JSInterop;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.UI.Components;
using Nova.UI.Features.Players.Services;

namespace Nova.UI.Features.Players.Components;

/// <summary>
/// The manual player-entry board for one authenticated member: required/optional profile entry,
/// the authoritative enrollment consequence, per-field server feedback, and durable ownership of
/// the exact pending creation command across reloads.
/// </summary>
public partial class PlayerIntakeBoard : NovaComponentBase
{
    private readonly IPlayerIntakeInterop _interop;
    private EditContext _editContext;
    private ElementReference _root;
    private DotNetObjectReference<PlayerIntakeBoard>? _departureReceiver;
    /// <summary>The lease every attempt this mounting made belongs to, so disposal releases it even when the
    /// answer to the attach was lost after the module installed the guard.</summary>
    private string? _guardLease;
    private bool _dirty;
    private bool _guardAttached;
    private bool _guardAttachInFlight;
    /// <summary>
    /// The attach this mounting started, kept so disposal can settle the lease it produced rather than
    /// reading a flag that a disposal in the middle of the attach would see as false.
    /// </summary>
    private Task? _guardAttach;
    private bool _departurePending;
    private string? _departureUrl;
    private bool _subscribed;
    private string? _focusRequest;
    private bool _dirtySyncRequested;
    private bool _setAsidePending;
    private bool _setAsideAcknowledged;
    /// <summary>
    /// Whether focus has already been moved for the feedback the board is currently showing. Feedback
    /// arriving where there was none is the transition the surface's contract asks focus to follow; the
    /// same feedback re-rendering, or another field holding it while one is corrected, is not.
    /// </summary>
    private bool _feedbackFocusMoved;

    /// <summary>The profile fields the board renders, in the order they are displayed.</summary>
    private static readonly string[] _profileFields =
    [
        nameof(PlayerFormState.FirstName),
        nameof(PlayerFormState.LastName),
        nameof(PlayerFormState.DateOfBirth),
        nameof(PlayerFormState.GraduationYear),
        nameof(PlayerFormState.Gender),
        nameof(PlayerFormState.JerseyNumber)
    ];

    /// <summary>The first field needing correction, which is the first invalid one in display order.</summary>
    private const string FirstInvalidFieldSelector = ".intake-fields .is-invalid";

    /// <summary>
    /// Initializes the board with the browser boundary used for owner-scoped creation recovery
    /// and the uncommitted-departure guard.
    /// </summary>
    /// <param name="interop">The injected browser boundary.</param>
    public PlayerIntakeBoard(IPlayerIntakeInterop interop)
    {
        _interop = interop;
        // Replaced in OnParametersSet once the host supplies the real profile state.
        _editContext = new EditContext(Model);
    }

    /// <summary>Gets or sets the authenticated member who owns any retained creation command.</summary>
    [Parameter, EditorRequired]
    public long OwnerUserId { get; set; }

    /// <summary>Gets or sets the club that owns any retained creation command.</summary>
    [Parameter, EditorRequired]
    public long ClubId { get; set; }

    /// <summary>Gets or sets the board heading.</summary>
    [Parameter]
    public string Heading { get; set; } = string.Empty;

    /// <summary>Gets or sets the commitment action's label.</summary>
    [Parameter]
    public string SubmitLabel { get; set; } = "Save";

    /// <summary>Gets or sets the mutable profile state bound to the permanent fields.</summary>
    [Parameter, EditorRequired]
    public PlayerFormState Model { get; set; } = PlayerFormState.CreateDefault();

    /// <summary>Gets or sets the club's enrollment consequence when it has been read.</summary>
    [Parameter]
    public PlayerIntakeContext? IntakeContext { get; set; }

    /// <summary>Gets or sets whether the enrollment consequence read is still in flight.</summary>
    [Parameter]
    public bool IntakeContextLoading { get; set; }

    /// <summary>Gets or sets whether the enrollment consequence could not be read.</summary>
    [Parameter]
    public bool IntakeContextUnavailable { get; set; }

    /// <summary>
    /// Gets or sets whether the board states the enrollment consequence of adding a player. Only the
    /// create host sets it: an edit changes an existing profile, enrolls nobody, and supplies no intake
    /// context, so it must not fall through to the default campaign copy as if it had been read.
    /// </summary>
    [Parameter]
    public bool ShowsEnrollmentConsequence { get; set; }

    /// <summary>Gets or sets whether the current member may commit profile mutations.</summary>
    [Parameter]
    public bool CanManage { get; set; }

    /// <summary>Gets or sets whether a mutation is in flight.</summary>
    [Parameter]
    public bool IsSubmitting { get; set; }

    /// <summary>Gets or sets a board-level failure message.</summary>
    [Parameter]
    public string? ErrorMessage { get; set; }

    /// <summary>Gets or sets field-keyed server validation messages bound to their owning controls.</summary>
    [Parameter]
    public IReadOnlyDictionary<string, string[]>? FieldErrors { get; set; }

    /// <summary>
    /// The server's per-field messages for the submission on screen, pruned as the member corrects them. The page
    /// hands the server's answer in through <see cref="FieldErrors"/>; a field edited after that answer no longer
    /// holds the value the server refused, so its message is dropped with the class and description that point at
    /// it rather than describing a value the member has already replaced.
    /// </summary>
    protected IReadOnlyDictionary<string, string[]>? FieldMessages => _fieldMessages;

    /// <summary>The messages <see cref="FieldMessages"/> reads, which the field-change handler prunes.</summary>
    private Dictionary<string, string[]>? _fieldMessages;

    /// <summary>The <see cref="FieldErrors"/> instance whose messages <see cref="FieldMessages"/> already holds.</summary>
    private IReadOnlyDictionary<string, string[]>? _appliedFieldErrors;

    /// <summary>Gets or sets structured graduation-year blockers for the current profile.</summary>
    [Parameter]
    public IReadOnlyList<GraduationYearBlockerItem> GraduationYearBlockers { get; set; } = [];

    /// <summary>Gets or sets the committed creation receipt, when the operation has settled as created.</summary>
    [Parameter]
    public PlayerCreationCompletion? Receipt { get; set; }

    /// <summary>Gets or sets a server-confirmed possible duplicate that staff must resolve elsewhere.</summary>
    [Parameter]
    public PlayerCreationDuplicate? Duplicate { get; set; }

    /// <summary>Gets or sets what the board knows about an unresolved or unreadable retained command.</summary>
    [Parameter]
    public PlayerCreationRecoveryState RecoveryState { get; set; }

    /// <summary>
    /// Gets or sets whether the owner's retained command has actually been checked. Until it has,
    /// the board refuses input rather than offering a pristine form that a landed recovery read
    /// would replace without the member's knowledge.
    /// </summary>
    [Parameter]
    public bool RecoveryChecked { get; set; } = true;

    /// <summary>Gets or sets whether owner-scoped recovery storage is currently unavailable.</summary>
    [Parameter]
    public bool StorageUnavailable { get; set; }

    /// <summary>Gets or sets whether the retained command can still be replayed unchanged.</summary>
    [Parameter]
    public bool CanReplay { get; set; }

    /// <summary>Gets or sets the parent-built player detail destination factory.</summary>
    [Parameter]
    public Func<long, Uri>? DetailUrlFactory { get; set; }

    /// <summary>Gets or sets the parent-built Players directory destination.</summary>
    [Parameter]
    public Uri? DirectoryUrl { get; set; }

    /// <summary>Gets or sets the parent-built directory destination that reviews the retained player's name.</summary>
    [Parameter]
    public Uri? ReconcileUrl { get; set; }

    /// <summary>Gets or sets the callback invoked when the member commits the form.</summary>
    [Parameter]
    public EventCallback OnSubmit { get; set; }

    /// <summary>Gets or sets the callback invoked when the member abandons the form without committing.</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>Gets or sets the callback invoked when the member starts another addition after a receipt.</summary>
    [Parameter]
    public EventCallback OnAddAnother { get; set; }

    /// <summary>Gets or sets the callback invoked when the member deliberately sets an unresolved addition aside.</summary>
    [Parameter]
    public EventCallback OnSetAside { get; set; }

    /// <summary>Gets or sets the callback invoked when the member discards unreadable retained bytes.</summary>
    [Parameter]
    public EventCallback OnDiscardUnreadable { get; set; }

    /// <summary>Gets or sets the callback invoked when recovery storage must be retried.</summary>
    [Parameter]
    public EventCallback OnRetryStorage { get; set; }

    /// <summary>Gets or sets the callback invoked when the member chooses to leave with uncommitted input.</summary>
    [Parameter]
    public EventCallback<string> OnConfirmedDeparture { get; set; }

    /// <summary>Gets the gender choices offered by the optional field.</summary>
    protected static IReadOnlyList<Gender> GenderOptions { get; } = Enum.GetValues<Gender>();

    /// <summary>Gets whether the board shows a settled receipt instead of editable fields.</summary>
    protected bool ShowsReceipt => Receipt is not null;

    /// <summary>Gets whether the retained bytes are unusable, which replaces the fields entirely.</summary>
    protected bool IsEntryBlocked => RecoveryState == PlayerCreationRecoveryState.Unreadable;

    /// <summary>
    /// Gets whether the board holds a retained command whose outcome is unsettled. The fields stay
    /// visible so the member can see exactly what was sent, but frozen: the payload is never edited
    /// into a different command under the same operation identity.
    /// </summary>
    protected bool IsFrozen => RecoveryState is PlayerCreationRecoveryState.Unresolved or PlayerCreationRecoveryState.Expired;

    /// <summary>Gets whether the permanent fields accept input.</summary>
    protected bool IsEditable => !IsEntryBlocked && !ShowsReceipt && !IsFrozen && CanManage && !IsSubmitting
        && RecoveryChecked;

    /// <summary>
    /// Gets whether the board's state leaves nothing unsaved, so departing can lose nothing the member
    /// could still act on. A settled receipt and a blocked entry state hold only bytes the member
    /// cannot resend, and an unavailable member cannot enter anything; a frozen board's fields are the
    /// retained addition itself rather than unsaved input, so a frozen board holds something only while
    /// its own set-aside decision is open, which is the one control it has left.
    /// </summary>
    protected bool HasNothingUnsaved => !CanManage || ShowsReceipt || IsEntryBlocked || (IsFrozen && !_setAsidePending);

    /// <summary>
    /// Gets a profile field's class, marking the invalid state Bootstrap styles. The form components own
    /// <c>aria-invalid</c> and ignore one supplied to them, and they report only the validation their own
    /// edit context holds, so a message the server keyed to this field has to be shown as the class the
    /// surface's own invalid state uses.
    /// </summary>
    /// <param name="field">The profile field name.</param>
    /// <param name="baseClass">The class the field carries when it has nothing to correct.</param>
    /// <returns>The field class, with the invalid state while the field has feedback.</returns>
    protected string FieldClass(string field, string baseClass)
        => FieldHasError(field) ? $"{baseClass} is-invalid" : baseClass;

    /// <summary>
    /// Gets the id of the note that explains the field set's current refusal, or null when the fields
    /// accept input, so the frozen and withheld states stay named for assistive technology.
    /// </summary>
    protected string? FieldsDescription
    {
        get
        {
            if (IsEntryBlocked || IsFrozen) { return "intake-recovery-note"; }
            return RecoveryChecked ? null : "intake-checking-note";
        }
    }

    /// <summary>
    /// Gets whether the board offers a replay of the retained command. An open set-aside decision owns that
    /// command: a replay begun beside it would settle the very operation the member is still deciding to
    /// abandon, so the replay is offered only while that decision is not in front of them.
    /// </summary>
    protected bool OffersReplay => CanReplay && !_setAsidePending;

    /// <summary>
    /// Gets whether the board offers the actions that resolve the retained record — setting it aside, or
    /// discarding bytes that cannot be read. A submission in flight owns that record: it is the only recovery
    /// copy of the operation being dispatched, so resolving it before the answer arrives would leave a
    /// committed response with no receipt to show and an unknown response with nothing to replay after a
    /// reload. Reloading remains the way out of a submission that never answers, because the record outlives
    /// the page.
    /// </summary>
    protected bool CanResolveRetained => !IsSubmitting;

    /// <summary>
    /// Gets whether the commit control is available. A retained command inside its window is
    /// replayed through the same control, so the member has exactly one way to settle it. A new
    /// addition also waits for the enrollment consequence: dispatching while the board still names
    /// it as unread would commit the member under a campaign nobody has read, while a read that
    /// failed is settled — the board names that and leaves the decision to the member.
    /// </summary>
    protected bool CanCommit => !IsEntryBlocked && !ShowsReceipt && CanManage && !IsSubmitting && RecoveryChecked
        && (RecoveryState == PlayerCreationRecoveryState.None
            ? !ShowsEnrollmentConsequence || !IntakeContextLoading
            : RecoveryState == PlayerCreationRecoveryState.Unresolved && OffersReplay);

    /// <summary>
    /// Gets the sentence that names why the board is withholding input. A refused check is not a check
    /// in progress, and it is the state that persists until storage answers again, so it says which
    /// action the member has.
    /// </summary>
    protected string RetainedCheckNote => StorageUnavailable
        ? "This browser's retained addition could not be checked. Retry storage to continue."
        : "Checking this browser for a retained addition…";

    /// <summary>
    /// Gets the commit control's label. The replay label appears only where a replay is actually
    /// offered, so a retained command that can no longer be sent is not named as an action.
    /// </summary>
    protected string CommitLabel => RecoveryState switch
    {
        PlayerCreationRecoveryState.Unresolved when OffersReplay => "Replay the retained addition",
        _ => SubmitLabel
    };

    /// <summary>Reads the owner's retained command without dispatching it.</summary>
    /// <param name="cancellationToken">A token that cancels the read.</param>
    /// <returns>The retained state, or null when recovery storage is unavailable.</returns>
    internal async Task<PlayerCreationRecoveryRead?> ReadRecoveryAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _interop.ReadAsync(OwnerUserId, ClubId, cancellationToken);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or OperationCanceledException)
        {
            return null;
        }
    }

    /// <summary>Persists the exact command before it is dispatched; a failed write blocks dispatch.</summary>
    /// <param name="command">The exact command to retain.</param>
    /// <param name="recoveryExpiresAt">The exclusive deadline the server honours for it.</param>
    /// <param name="cancellationToken">A token that cancels the write.</param>
    /// <returns><see langword="true"/> when the command is durably retained.</returns>
    internal async Task<bool> PersistAsync(CreatePlayerInput command, DateTimeOffset recoveryExpiresAt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);
        try
        {
            await _interop.WriteAsync(
                new PendingPlayerCreation
                {
                    ActorUserId = OwnerUserId,
                    RecoveryExpiresAt = recoveryExpiresAt,
                    Payload = command
                },
                cancellationToken);
            return true;
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Clears storage only when it still holds the settled operation.</summary>
    /// <param name="operationId">The settled operation identity.</param>
    /// <param name="cancellationToken">A token that cancels the clear.</param>
    /// <returns><see langword="true"/> when the matching record was removed.</returns>
    internal async Task<bool> ClearAsync(Guid operationId, CancellationToken cancellationToken)
    {
        try
        {
            return await _interop.ClearAsync(OwnerUserId, ClubId, operationId, cancellationToken);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Discards unreadable bytes only when they still match the value the member reviewed.</summary>
    /// <param name="expectedValue">The exact reviewed bytes.</param>
    /// <param name="cancellationToken">A token that cancels the discard.</param>
    /// <returns><see langword="true"/> when the reviewed bytes were removed.</returns>
    internal async Task<bool> DiscardUnreadableAsync(string expectedValue, CancellationToken cancellationToken)
    {
        try
        {
            return await _interop.DiscardInvalidAsync(OwnerUserId, ClubId, expectedValue, cancellationToken);
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or OperationCanceledException)
        {
            return false;
        }
    }

    /// <summary>Matches the bound profile state's validation rules to the shared input contract.</summary>
    /// <param name="field">The profile field name.</param>
    /// <returns>The server messages reported for that field, when any.</returns>
    protected IReadOnlyList<string> FieldErrorsFor(string field)
        => FieldMessages is not null && FieldMessages.TryGetValue(field, out var messages) ? messages : [];

    /// <summary>
    /// Gets whether a profile field has feedback to describe. The bound form's own validation message and
    /// the server's per-field messages both render inside the field's error region, so the control names
    /// that region whenever either has something to say: assistive technology hears what to correct
    /// rather than only that the field is invalid.
    /// </summary>
    /// <param name="field">The profile field name.</param>
    /// <returns><see langword="true"/> when the field has a message to describe.</returns>
    protected bool FieldHasError(string field)
        => FieldErrorsFor(field).Count > 0
            || _editContext.GetValidationMessages(new FieldIdentifier(Model, field)).Any();

    /// <summary>
    /// Moves focus to the first field needing correction once a refusal has left feedback, which the intake
    /// surface's contract asks focus to follow: the message the control names is what the member has to
    /// read. The module reaches the message's own region when the control cannot take that focus, which is
    /// how a frozen recovery response still lands the member on the feedback. Feedback that is merely
    /// re-rendered is not that transition, so focus is moved once per refusal rather than chasing each
    /// message the member clears while correcting the fields.
    /// </summary>
    private void MoveFocusToFeedback()
    {
        if (Array.Find(_profileFields, FieldHasError) is null)
        {
            // Nothing left to correct, so the next refusal is a fresh transition to follow.
            _feedbackFocusMoved = false;
            return;
        }

        if (_feedbackFocusMoved) { return; }
        _feedbackFocusMoved = true;
        RequestFocusOnRegion(FirstInvalidFieldSelector);
    }

    /// <summary>Projects the duplicate record into its destination link.</summary>
    /// <returns>The duplicate destination, or null when there is nothing to inspect.</returns>
    protected Uri? DuplicateUrl => Duplicate is { } duplicate && DetailUrlFactory is not null
        ? DetailUrlFactory(duplicate.PlayerId)
        : null;

    /// <summary>Projects the created player into its destination link.</summary>
    /// <returns>The created player's detail destination, or null when it cannot be built.</returns>
    protected Uri? ReceiptDetailUrl => Receipt is { } receipt && DetailUrlFactory is not null
        ? DetailUrlFactory(receipt.Player.PlayerId)
        : null;

    /// <summary>Describes the committed enrollment exactly as the immutable receipt recorded it.</summary>
    /// <returns>Written enrollment feedback naming the campaign or the next-campaign case.</returns>
    protected string ReceiptEnrollmentText => Receipt?.Enrollment is { } enrollment
        ? $"Enrolled in {enrollment.CampaignName}."
        : "No campaign was Active, so this player is ready for the next campaign opening.";

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_editContext.Model, Model))
        {
            // The host replaced the profile state; the previous context and its listeners go with it, or the
            // replaced model stays rooted through this board's own handlers until it is disposed. The board's own
            // view of what is unsaved is deliberately untouched here: a replacement can carry the same values
            // back (a refusal's corrected form) and the host that replaces a form wholesale releases the guard.
            _editContext.OnFieldChanged -= OnFieldChanged;
            _editContext.OnValidationStateChanged -= OnValidationStateChanged;
            _editContext = new EditContext(Model);
            _subscribed = false;
        }

        if (!_subscribed)
        {
            _editContext.OnFieldChanged += OnFieldChanged;
            _editContext.OnValidationStateChanged += OnValidationStateChanged;
            // The flag belongs to the subscription itself: setting it anywhere else would let every later render
            // add another pair of handlers, each running every callback and only one of them removed on disposal.
            _subscribed = true;
        }

        // A new answer replaces the last one; the same instance has already been pruned for the fields the member
        // corrected since it arrived, so it must not be adopted again.
        if (!ReferenceEquals(_appliedFieldErrors, FieldErrors))
        {
            _appliedFieldErrors = FieldErrors;
            _fieldMessages = FieldErrors is { Count: > 0 }
                ? new Dictionary<string, string[]>(FieldErrors, StringComparer.Ordinal)
                : null;
        }

        // Messages the server keyed to a field arrive as parameters rather than through the edit context,
        // so focus follows them here exactly as the validation-state handler follows the form's own.
        MoveFocusToFeedback();


        // A lossless transition — including the freeze a failed acknowledgement leaves behind — makes
        // input unsaveable, so it can no longer be what the departure guard speaks for.
        if (HasNothingUnsaved && _dirty)
        {
            _dirty = false;
            // The guard's own state must follow, or a later departure still prompts for input that
            // is no longer at risk. Interop cannot run here, so the next render performs it.
            _dirtySyncRequested = true;
        }
    }

    /// <summary>Requests focus on the board's first editable control after the next render.</summary>
    public void RequestFocusOnFirstField() => _focusRequest = ":first";

    /// <summary>Requests focus on a named region after the next render.</summary>
    /// <param name="selector">The region selector to focus.</param>
    public void RequestFocusOnRegion(string selector)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selector);
        _focusRequest = selector;
    }

    /// <summary>Clears the uncommitted flag and releases the departure guard's protection.</summary>
    public void MarkCommittedOrClosed()
    {
        _dirty = false;
        _dirtySyncRequested = true;
        _departurePending = false;
        _departureUrl = null;
    }

    /// <inheritdoc />
    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        // Attached here and retried on every later render until it succeeds, because a transient
        // import or attach failure must not leave typed input unprotected for the rest of the mount.
        // A failed attempt schedules no render of its own, so retries follow real interaction rather
        // than a hot loop, and the in-flight gate keeps concurrent renders from stacking attempts.
        if (!_guardAttached && !_guardAttachInFlight)
        {
            await TryAttachDepartureGuardAsync();
        }

        if (_dirtySyncRequested)
        {
            _dirtySyncRequested = false;
            await SyncDirtyAsync();
        }

        if (_focusRequest is { } request)
        {
            _focusRequest = null;
            try
            {
                if (string.Equals(request, ":first", StringComparison.Ordinal))
                {
                    await _interop.FocusFirstFieldAsync(_root, ComponentCancellationToken);
                }
                else
                {
                    await _interop.FocusRegionAsync(_root, request, ComponentCancellationToken);
                }
            }
            catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested
                && exception is JSException or InvalidOperationException or OperationCanceledException)
            {
                // Focus is an enhancement; the board stays usable when the browser refuses it.
            }
        }
    }

    /// <summary>
    /// Attaches the uncommitted-departure guard under a fresh lease. The module replaces whatever
    /// guard is active on attach, so a later attempt supersedes the previous listeners instead of
    /// duplicating them, and the current dirty flag is pushed again to match the new lease.
    /// </summary>
    private async Task TryAttachDepartureGuardAsync()
    {
        _departureReceiver ??= DotNetObjectReference.Create(this);
        _guardLease = Guid.CreateVersion7().ToString("N");
        _guardAttachInFlight = true;
        try
        {
            // Recorded before the await: a disposal that lands while this call is in flight must be able
            // to wait for it, because cancellation cuts off the answer without undoing what the browser
            // did with the attach.
            var attach = _interop.AttachDepartureGuardAsync(_root, _departureReceiver, _guardLease, ComponentCancellationToken);
            _guardAttach = attach;
            await attach;
            _guardAttached = true;
            await SyncDirtyAsync();
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested
            && exception is JSException or InvalidOperationException or OperationCanceledException)
        {
            // The guard is an enhancement. Both native document departures and the commit path
            // still protect the member's input, so the board stays usable without it.
        }
        finally
        {
            _guardAttachInFlight = false;
        }
    }

    /// <summary>
    /// Leaves the form. A board whose member has typed something makes this a departure like any other, so the
    /// member decides about their typing in the panel the guard opens rather than losing it to one click; a
    /// board with nothing typed leaves immediately, because cancelling it can lose nothing.
    /// </summary>
    /// <returns>A task that completes when the departure has been requested.</returns>
    private async Task CancelFormAsync()
    {
        // The acknowledgement lives outside the EditContext, so it is the board's own signal that the member has
        // an uncommitted decision here: without it, Cancel would discard what the link path asks about.
        if ((!_dirty && !_setAsideAcknowledged) || DirectoryUrl is not { } directoryUrl)
        {
            await OnCancel.InvokeAsync();
            return;
        }

        _departurePending = true;
        _departureUrl = directoryUrl.ToString();
        // The panel is the destination now, for the same reason the guard's own attempt focuses it: a
        // keyboard or screen-reader member must land in the question rather than on the control they pressed.
        _focusRequest = "#intake-departure-heading";
        await InvokeAsync(StateHasChanged);
    }

    /// <summary>
    /// Records that the member attempted to leave with uncommitted input and opens the departure
    /// confirmation. A board that holds nothing that could be lost departs instead, because the
    /// module has already cancelled the click this attempt represents.
    /// </summary>
    /// <param name="lease">The lease of the board mounting that raised the attempt.</param>
    /// <param name="url">The local destination the member chose.</param>
#pragma warning disable CA1054 // The collocated module passes a browser location, which is text at the interop boundary.
    [JSInvokable]
    public async Task OnBoardDepartureAttemptAsync(string lease, string url)
#pragma warning restore CA1054
    {
        if (!string.Equals(lease, _guardLease, StringComparison.Ordinal)) { return; }
        // The module observes DOM input, including controls outside the EditForm, so it decides
        // whether a prompt is due; the board only refuses when nothing could be lost.
        if (HasNothingUnsaved)
        {
            // The module already cancelled this click, so refusing here would leave it inert.
            _dirty = false;
            await OnConfirmedDeparture.InvokeAsync(url);
            return;
        }
        _dirty = true;
        _departurePending = true;
        _departureUrl = url;
        // The panel is the destination now: a keyboard or screen-reader member must land in it rather
        // than stay on the link whose navigation was cancelled.
        _focusRequest = "#intake-departure-heading";
        await InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        if (_subscribed)
        {
            _editContext.OnFieldChanged -= OnFieldChanged;
            _editContext.OnValidationStateChanged -= OnValidationStateChanged;
        }

        // An attach this mounting started and never saw the answer to is still its own to settle:
        // disposal cancels the token, which cuts off the await but not the browser, so the boundary can
        // hold a guard that no live board will ever release. Waiting for the attach lets that be decided
        // on what the boundary actually did, and releasing a lease that is not the active guard is a
        // no-op in the module, so this can never take another mounting's guard away.
        var attachWasInFlight = _guardAttach is { IsCompleted: false };
        if (attachWasInFlight)
        {
            try
            {
                await _guardAttach!;
            }
            catch (Exception exception) when (exception is JSException or InvalidOperationException
                or OperationCanceledException or ObjectDisposedException)
            {
                // The attach already reported its own failure where it happened; disposal needs only
                // the outcome that decides whether this lease has to be released.
            }
        }

        if (_guardLease is not null)
        {
            try
            {
                await _interop.DetachDepartureGuardAsync(_guardLease, CancellationToken.None);
            }
            catch (JSDisconnectedException)
            {
                // A torn-down browser context has already released the guard's listeners. Any other
                // failure is real: swallowing it would leave the document listeners and the module's
                // active guard installed with a receiver that no longer exists.
            }
        }

        _departureReceiver?.Dispose();
        await base.DisposeAsyncCore();
    }

    private void OnFieldChanged(object? sender, FieldChangedEventArgs args)
    {
        // The server answered about the value that was sent, so a correction drops the message it left instead of
        // describing a value the member has already replaced.
        if (_fieldMessages?.Remove(args.FieldIdentifier.FieldName) == true)
        {
            _ = InvokeAsync(StateHasChanged);
        }

        if (HasNothingUnsaved) { return; }
        if (_dirty) { return; }
        _dirty = true;
        _ = SyncDirtyAsync();
    }

    /// <summary>
    /// Re-renders when field feedback changes, because the controls describe their field's message and carry
    /// the invalid class. A form that refused a submission before any request was made produces its messages
    /// without re-rendering this component, so without this the attributes would keep describing the field as
    /// validation left it before, and that same transition is the one focus follows.
    /// </summary>
    /// <param name="sender">The edit context that raised the change.</param>
    /// <param name="args">The validation state that changed.</param>
    private void OnValidationStateChanged(object? sender, ValidationStateChangedEventArgs args)
        => _ = InvokeAsync(() =>
        {
            MoveFocusToFeedback();
            StateHasChanged();
        });

    /// <summary>
    /// Records the board's uncommitted state with the boundary. Called detached from the field-change handler, so
    /// a teardown that cancels it must be handled here rather than faulting a task nobody observes.
    /// </summary>
    /// <returns>A task that completes when the flag has been recorded, or when the attempt was abandoned.</returns>
    internal async Task SyncDirtyAsync()
    {
        if (!_guardAttached) { return; }
        try
        {
            await _interop.MarkDirtyAsync(_guardLease!, _dirty, ComponentCancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Teardown cancelled the sync: the module keeps the last state it was told, and there is no board
            // left to report a failure to.
        }
        catch (Exception exception) when (exception is JSException or InvalidOperationException or ObjectDisposedException)
        {
            // Failing to record the dirty flag only weakens the warning; it never blocks a commit. A failure
            // that raced teardown is the same news: the guard it spoke for is going away with this mounting.
        }
    }

    private Task ConfirmDepartureAsync()
    {
        var url = _departureUrl;
        _departurePending = false;
        _departureUrl = null;
        _dirty = false;
        return url is null ? Task.CompletedTask : OnConfirmedDeparture.InvokeAsync(url);
    }

    private Task CancelDepartureAsync()
    {
        _departurePending = false;
        _departureUrl = null;
        return Task.CompletedTask;
    }

    private void BeginSetAside()
    {
        _setAsidePending = true;
        _setAsideAcknowledged = false;
        // The confirmation's own controls are the next step, so focus moves into it.
        _focusRequest = "#intake-set-aside-heading";
    }

    private void CancelSetAside()
    {
        _setAsidePending = false;
        _setAsideAcknowledged = false;
    }

    private async Task ConfirmSetAsideAsync()
    {
        if (!_setAsideAcknowledged)
        {
            return;
        }

        _setAsidePending = false;
        _setAsideAcknowledged = false;
        await OnSetAside.InvokeAsync();
    }
}

/// <summary>
/// What the mounted board knows about the owner's retained creation command. Only
/// <see cref="None"/> permits a new addition without an explicit resolution.
/// </summary>
public enum PlayerCreationRecoveryState
{
    /// <summary>No command is retained for this owner.</summary>
    None,

    /// <summary>A command is retained and still inside its replay window.</summary>
    Unresolved,

    /// <summary>A command is retained but the server's window has closed; its outcome stays unknown.</summary>
    Expired,

    /// <summary>
    /// Retained bytes cannot be dispatched by this owner — either they are corrupt, or they describe a
    /// different member or club. They are preserved for an explicit discard and are never replayed.
    /// </summary>
    Unreadable
}
