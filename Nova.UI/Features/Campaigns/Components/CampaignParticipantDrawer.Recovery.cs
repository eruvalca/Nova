using System.Text.Json;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Routing;
using Microsoft.JSInterop;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.UI.Features.Campaigns.Components;

public partial class CampaignParticipantDrawer
{
    [Inject] private NavigationManager DrawerNavigation { get; set; } = null!;
    private DrawerStoredOperation? _storedOperation;
    private string? _recoveryOwner;
    private bool _drawerStorageFailed;
    private bool _drawerStorageReady;
    private long _drawerRestoreSequence;
    private long _drawerStorageRetrySequence;
    private string? _drawerStorageRetryOwner;
    private DotNetObjectReference<CampaignParticipantDrawer>? _drawerNavigationReceiver;
    private readonly string _drawerNavigationLease = Guid.NewGuid().ToString("N");
    private Func<Task<bool>>? _drawerLeaveAction;
    private long _drawerDepartureSequence;
    private long? _drawerDepartureInFlight;
    private Guid _editExpectedVersion;
    private string _editNoteOriginal = string.Empty;
    private string DrawerStorageScope => $"{CaptureScope ?? AuthorityScope}:{CampaignId}:{ParticipantId}";
    private bool DrawerHasDraft => _addNoteContent.Length > 0 || (_editingNoteId is not null && !string.Equals(_editNoteContent, _editNoteOriginal, StringComparison.Ordinal));
    private bool DrawerProtected => _storedOperation is not null || DrawerHasDraft;
    private bool DrawerMutationBlocked => _isMutating || _storedOperation is not null || _drawerStorageFailed || !_drawerStorageReady;

    private async Task RestoreDrawerOperationAsync(IJSObjectReference module)
    {
        var owner = ParticipantOwner;
        var scope = DrawerStorageScope;
        if (string.Equals(_recoveryOwner, owner, StringComparison.Ordinal))
        {
            return;
        }

        _recoveryOwner = owner;
        var sequence = ++_drawerRestoreSequence;
        var draft = (_addNoteContent, _editingNoteId, _editNoteContent);
        _drawerStorageReady = false;
        try
        {
            _drawerNavigationReceiver ??= DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("protectNavigation", _dialog, owner, _drawerNavigationLease, _drawerNavigationReceiver);
            if (!OwnsDrawerRestore(owner, sequence)) { return; }
            var json = await module.InvokeAsync<string?>("readOperation", _dialog, scope);
            if (!OwnsDrawerRestore(owner, sequence))
            {
                return;
            }

            var stored = json is null ? null : JsonSerializer.Deserialize<DrawerStoredOperation>(json)
                ?? throw new JsonException("Invalid retained evaluation operation.");
            var input = stored is null ? null : ParseDrawerOperation(stored);
            _storedOperation = stored;
            if (draft == (_addNoteContent, _editingNoteId, _editNoteContent)) { RestoreDrawerNoteText(input); }
            _drawerStorageFailed = false;
            _drawerStorageReady = true;
            if (_storedOperation is not null) { _mutationError = "A submission needs its original receipt. Recover it before moving to another player."; }
            StateHasChanged();
        }
        catch (Exception exception) when (exception is JSException or JsonException)
        {
            if (OwnsDrawerRestore(owner, sequence))
            {
                _drawerStorageFailed = true;
                _mutationError = "Recovery storage or navigation protection is unavailable. Keep or copy your text; update your browser or retry.";
                StateHasChanged();
            }
        }
    }

    private bool OwnsDrawerRestore(string owner, long sequence) => sequence == _drawerRestoreSequence
        && string.Equals(owner, ParticipantOwner, StringComparison.Ordinal) && !ComponentCancellationToken.IsCancellationRequested;

    private async Task RetryDrawerStorageAsync()
    {
        var owner = ParticipantOwner;
        if (string.Equals(_drawerStorageRetryOwner, owner, StringComparison.Ordinal)) { return; }
        var sequence = ++_drawerStorageRetrySequence;
        _drawerStorageRetryOwner = owner;
        try
        {
            _drawerInteropFailedOwner = null;
            if (!await EnsureDrawerInteropAsync() || !string.Equals(owner, ParticipantOwner, StringComparison.Ordinal)
                || sequence != _drawerStorageRetrySequence) { return; }
            _recoveryOwner = null;
            await RestoreDrawerOperationAsync(await _moduleTask.Value);
        }
        finally
        {
            if (sequence == _drawerStorageRetrySequence) { _drawerStorageRetryOwner = null; }
        }
    }

    private void RestoreDrawerNoteText(EvaluationOperationInput? input)
    {
        if (input is AddEvaluationNoteInput add)
        {
            _addNoteContent = add.Content;
            _showAddNoteForm = true;
        }
        else if (input is EditEvaluationNoteInput edit)
        {
            _editingNoteId = edit.NoteId;
            _editNoteContent = edit.Content;
            _editNoteOriginal = string.Empty;
            _editExpectedVersion = edit.ExpectedVersion;
        }
    }

    private EvaluationOperationInput ParseDrawerOperation(DrawerStoredOperation stored)
    {
        if (stored.Payload is null) { throw new JsonException("Missing retained operation payload."); }
        EvaluationOperationInput? input = stored.Kind switch
        {
            nameof(AddEvaluationNoteInput) => JsonSerializer.Deserialize<AddEvaluationNoteInput>(stored.Payload),
            nameof(EditEvaluationNoteInput) => JsonSerializer.Deserialize<EditEvaluationNoteInput>(stored.Payload),
            nameof(DeleteEvaluationNoteInput) => JsonSerializer.Deserialize<DeleteEvaluationNoteInput>(stored.Payload),
            nameof(ApplyCampaignTagApplicationInput) => JsonSerializer.Deserialize<ApplyCampaignTagApplicationInput>(stored.Payload),
            nameof(RemoveCampaignTagApplicationInput) => JsonSerializer.Deserialize<RemoveCampaignTagApplicationInput>(stored.Payload),
            _ => throw new JsonException("Unknown retained operation kind.")
        };
        if (input is null || InputValidator.Validate(input).Count != 0
            || (input is AddEvaluationNoteInput add && add.PlayerCampaignAssignmentId != ParticipantId)
            || (input is ApplyCampaignTagApplicationInput apply && apply.PlayerCampaignAssignmentId != ParticipantId))
        {
            throw new JsonException("Invalid retained operation payload.");
        }
        return input;
    }

    private async Task<ServiceResult<T>> CallStoredAsync<T>(EvaluationOperationInput input,
        Func<CancellationToken, Task<ServiceResult<T>>> submit, MutationOwner lease)
    {
        var stored = new DrawerStoredOperation(input.GetType().Name, JsonSerializer.Serialize(input, input.GetType()));
        var scope = DrawerStorageScope;
        var module = await _moduleTask.Value;
        if (!OwnsMutation(lease))
        {
            throw new OperationCanceledException("The participant changed before dispatch.");
        }

        _storedOperation = stored;
        try
        {
            await module.InvokeVoidAsync("writeOperation", _dialog, scope, JsonSerializer.Serialize(stored));
        }
        catch (JSException)
        {
            if (OwnsMutation(lease))
            {
                _storedOperation = null;
            }

            throw;
        }
        if (!OwnsMutation(lease))
        {
            throw new OperationCanceledException("The participant changed before dispatch.");
        }

        var result = await submit(ComponentCancellationToken);
        if (OwnsMutation(lease) && (result.IsSuccess || result.Problem.Kind is ServiceProblemKind.Validation or ServiceProblemKind.Forbidden or ServiceProblemKind.NotFound or ServiceProblemKind.Conflict))
        {
            await module.InvokeVoidAsync("clearOperation", _dialog, scope, JsonSerializer.Serialize(stored));
            if (OwnsMutation(lease))
            {
                _storedOperation = null;
                if (input is DeleteEvaluationNoteInput) { CancelDeleteNote(); }
            }
        }
        return result;
    }

    private async Task ReplayDrawerOperationAsync()
    {
        if (_storedOperation is not { } stored || _isMutating)
        {
            return;
        }

        _isMutating = true;
        var lease = new MutationOwner(++_mutationSequence, ContextOwner);
        try
        {
            switch (ParseDrawerOperation(stored))
            {
                case AddEvaluationNoteInput add:
                    await ReplayDrawerAsync(add, token => noteService.AddAsync(add, token), lease, "Note saved.");
                    break;
                case EditEvaluationNoteInput edit:
                    await ReplayDrawerAsync(edit, token => noteService.EditAsync(edit, token), lease, "Note updated.");
                    break;
                case DeleteEvaluationNoteInput delete:
                    await ReplayDrawerAsync(delete, token => noteService.DeleteAsync(delete, token), lease, "Note deleted.");
                    break;
                case ApplyCampaignTagApplicationInput apply:
                    await ReplayDrawerAsync(apply, token => tagApplicationService.ApplyAsync(apply, token), lease, "Tag applied.");
                    break;
                case RemoveCampaignTagApplicationInput remove:
                    await ReplayDrawerAsync(remove, token => tagApplicationService.RemoveAsync(remove, token), lease, "Tag removed.");
                    break;
                default: _mutationError = "This retained operation is not recognized. Its text remains available in this tab."; break;
            }
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested && exception is JSException or JsonException or HttpRequestException or OperationCanceledException)
        {
            if (OwnsMutation(lease))
            {
                _mutationError = "The original operation could not be recovered yet. Retry without changing its payload.";
            }
        }
        finally
        {
            if (OwnsMutation(lease))
            {
                _isMutating = false;
            }
        }
    }

    private async Task ReplayDrawerAsync<T>(EvaluationOperationInput input, Func<CancellationToken, Task<ServiceResult<T>>> submit,
        MutationOwner lease, string message)
    {
        var result = await CallStoredAsync(input, submit, lease);
        await HandleMutationResultAsync(lease, result, message, () =>
        {
            if (input is AddEvaluationNoteInput add && string.Equals(_addNoteContent, add.Content, StringComparison.Ordinal))
            {
                _addNoteContent = string.Empty;
            }

            if (input is EditEvaluationNoteInput) { _editingNoteId = null; _editNoteContent = string.Empty; }
        });
    }

    private Task GuardDrawerMoveAsync(Func<Task> move)
    {
        if (!DrawerProtected)
        {
            return move();
        }

        ++_drawerDepartureSequence;
        _drawerLeaveAction = async () => { await move(); return true; };
        if (_storedOperation is not null)
        {
            _mutationError = "Recover the pending submission before changing players.";
        }

        return Task.CompletedTask;
    }

    private Task GuardDrawerNavigationAsync(LocationChangingContext context)
    {
        if (!DrawerProtected)
        {
            return Task.CompletedTask;
        }

        context.PreventNavigation();
        return GuardDrawerMoveAsync(() => { DrawerNavigation.NavigateTo(context.TargetLocation); return Task.CompletedTask; });
    }

    private async Task DiscardDrawerAndLeaveAsync()
    {
        if (DrawerMutationBlocked || _drawerLeaveAction is not { } move || _drawerDepartureInFlight is not null)
        {
            return;
        }

        var owner = ParticipantOwner;
        var context = ContextOwner;
        var request = _drawerDepartureSequence;
        var draft = (_addNoteContent, _editNoteContent, _editingNoteId);
        bool current() => !ComponentCancellationToken.IsCancellationRequested && request == _drawerDepartureSequence
            && string.Equals(context, ContextOwner, StringComparison.Ordinal) && _drawerLeaveAction == move;
        _drawerDepartureInFlight = request;
        var departed = false;
        try
        {
            var module = await _moduleTask.Value;
            if (!current() || DrawerMutationBlocked || draft != (_addNoteContent, _editNoteContent, _editingNoteId))
            {
                return;
            }

            _addNoteContent = _editNoteContent = string.Empty;
            _editingNoteId = null;
            await module.InvokeVoidAsync("releaseNavigation", _dialog, owner, _drawerNavigationLease);
            if (!current() || DrawerProtected)
            {
                return;
            }

            var resumed = await move();
            departed = resumed;
            if (current() && !DrawerProtected)
            {
                if (resumed)
                {
                    _drawerLeaveAction = null;
                }
                else
                {
                    _mutationError = "Navigation was interrupted. Keep working or try leaving again.";
                }
            }
        }
        catch (JSException)
        {
            if (current())
            {
                _mutationError = "Navigation could not continue. Keep working or try leaving again.";
            }
        }
        finally
        {
            await FinishDrawerDepartureAsync(owner, request, departed, current);
        }
    }

    private async Task FinishDrawerDepartureAsync(string owner, long request, bool departed, Func<bool> current)
    {
        if (_drawerDepartureInFlight == request)
        {
            if (!departed)
            {
                try { await (await _moduleTask.Value).InvokeVoidAsync("cancelNavigation", _dialog, owner, _drawerNavigationLease); }
                catch (JSException)
                {
                    if (current())
                    {
                        _mutationError = "Navigation protection could not be restored. Reload before continuing.";
                    }
                }
            }
            if (_drawerDepartureInFlight == request)
            {
                _drawerDepartureInFlight = null;
            }
        }
    }

    /// <summary>Protects owned drafts from native enhanced links and history traversal.</summary>
    [JSInvokable]
    public Task ProtectNativeNavigationAsync(string owner, string lease, string target, string? historyKey)
    {
        if (string.Equals(owner, ParticipantOwner, StringComparison.Ordinal)
            && string.Equals(lease, _drawerNavigationLease, StringComparison.Ordinal)
            && !ComponentCancellationToken.IsCancellationRequested)
        {
            ++_drawerDepartureSequence;
            _drawerLeaveAction = historyKey is null
                ? () => { DrawerNavigation.NavigateTo(target); return Task.FromResult(true); }
            : async () => await (await _moduleTask.Value).InvokeAsync<bool>("resumeHistory", _dialog, owner, lease, historyKey);
            StateHasChanged();
        }
        return Task.CompletedTask;
    }

    private sealed record DrawerStoredOperation(string Kind, string Payload);
}
