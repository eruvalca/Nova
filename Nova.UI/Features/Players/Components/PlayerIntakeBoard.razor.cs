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
    private string? _guardLease;
    private bool _dirty;
    private bool _guardAttached;
    private bool _departurePending;
    private string? _departureUrl;
    private bool _subscribed;
    private string? _focusRequest;
    private bool _dirtySyncRequested;
    private bool _setAsidePending;
    private bool _setAsideAcknowledged;

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

    /// <summary>Gets or sets whether the enrollment consequence could not be read.</summary>
    [Parameter]
    public bool IntakeContextUnavailable { get; set; }

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

    /// <summary>Gets or sets structured graduation-year blockers for the current profile.</summary>
    [Parameter]
    public IReadOnlyList<GraduationYearBlockerItem> GraduationYearBlockers { get; set; } = [];

    /// <summary>Gets or sets the committed creation receipt, when the operation has settled as created.</summary>
    [Parameter]
    public PlayerCreationCompletion? Receipt { get; set; }

    /// <summary>Gets or sets a server-confirmed possible duplicate that staff must resolve elsewhere.</summary>
    [Parameter]
    public PlayerCreationDuplicate? Duplicate { get; set; }

    /// <summary>Gets or sets the parent-built duplicate destination, including the current return context.</summary>
    [Parameter]
    public Uri? DuplicateDetailUrl { get; set; }

    /// <summary>Gets or sets what the board knows about an unresolved or unreadable retained command.</summary>
    [Parameter]
    public PlayerCreationRecoveryState RecoveryState { get; set; }

    /// <summary>Gets or sets the exact unreadable retained bytes, when <see cref="RecoveryState"/> is unreadable.</summary>
    [Parameter]
    public string? InvalidRetainedValue { get; set; }

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
    protected bool IsEditable => !IsEntryBlocked && !ShowsReceipt && !IsFrozen && CanManage && !IsSubmitting;

    /// <summary>
    /// Gets whether the commit control is available. A retained command inside its window is
    /// replayed through the same control, so the member has exactly one way to settle it.
    /// </summary>
    protected bool CanCommit => !IsEntryBlocked && !ShowsReceipt && CanManage && !IsSubmitting
        && (RecoveryState == PlayerCreationRecoveryState.None
            || (RecoveryState == PlayerCreationRecoveryState.Unresolved && CanReplay));

    /// <summary>Gets the commit control's label, which names the action the current state performs.</summary>
    protected string CommitLabel => RecoveryState switch
    {
        PlayerCreationRecoveryState.Unresolved => "Replay the retained addition",
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
        => FieldErrors is not null && FieldErrors.TryGetValue(field, out var messages) ? messages : [];

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
            // The host replaced the profile state; the previous context and its listeners go with it.
            _editContext = new EditContext(Model);
            _subscribed = false;
        }

        if (!_subscribed)
        {
            _editContext.OnFieldChanged += OnFieldChanged;
            _subscribed = true;
        }


        // A receipt, a blocked entry state, or an unavailable member makes input lossless.
        if ((ShowsReceipt || IsEntryBlocked || !CanManage) && _dirty)
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
        if (firstRender && !_guardAttached)
        {
            _departureReceiver ??= DotNetObjectReference.Create(this);
            _guardLease = Guid.CreateVersion7().ToString("N");
            try
            {
                await _interop.AttachDepartureGuardAsync(_root, _departureReceiver, _guardLease, ComponentCancellationToken);
                _guardAttached = true;
                await SyncDirtyAsync();
            }
            catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested
                && exception is JSException or InvalidOperationException or OperationCanceledException)
            {
                // The guard is an enhancement. Both native document departures and the commit path
                // still protect the member's input, so the board stays usable without it.
            }
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
    /// Records that the member attempted to leave with uncommitted input and opens the departure
    /// confirmation. The attempt never departs: only the panel's confirm raises the departure.
    /// </summary>
    /// <param name="lease">The lease of the board mounting that raised the attempt.</param>
    /// <param name="url">The local destination the member chose.</param>
#pragma warning disable CA1054 // The collocated module passes a browser location, which is text at the interop boundary.
    [JSInvokable]
    public async Task OnBoardDepartureAttemptAsync(string lease, string url)
#pragma warning restore CA1054
    {
        if (!string.Equals(lease, _guardLease, StringComparison.Ordinal) || !_dirty) { return; }
        _departurePending = true;
        _departureUrl = url;
        await InvokeAsync(StateHasChanged);
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        if (_subscribed)
        {
            _editContext.OnFieldChanged -= OnFieldChanged;
        }

        if (_guardAttached)
        {
            try
            {
                await _interop.DetachDepartureGuardAsync(_guardLease!, CancellationToken.None);
            }
            catch (JSException)
            {
                // A torn-down browser context has already released the guard's listeners.
            }
        }

        _departureReceiver?.Dispose();
        await base.DisposeAsyncCore();
    }

    private void OnFieldChanged(object? sender, FieldChangedEventArgs args)
    {
        if (ShowsReceipt || IsEntryBlocked || !CanManage) { return; }
        if (_dirty) { return; }
        _dirty = true;
        _ = SyncDirtyAsync();
    }

    private async Task SyncDirtyAsync()
    {
        if (!_guardAttached) { return; }
        try
        {
            await _interop.MarkDirtyAsync(_guardLease!, _dirty, ComponentCancellationToken);
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested
            && exception is JSException or InvalidOperationException or ObjectDisposedException)
        {
            // Failing to record the dirty flag only weakens the warning; it never blocks a commit.
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
