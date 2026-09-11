using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.UI.Features.Campaigns.Components;

public partial class CampaignEvaluationPanel
{
    private string _draft = string.Empty;
    private string _editContent = string.Empty;
    private string _editOriginal = string.Empty;
    private long? _editingNoteId;
    private Guid _editVersion;
    private PendingCapture? _pending;
    private bool _dispatching;
    private bool _storageReady;
    private string? _captureError;
    private string? _storageError;
    private long _storageRevision;

    private void ResetCapture()
    {
        _draft = _editContent = _editOriginal = string.Empty;
        _editingNoteId = null;
        _editVersion = Guid.Empty;
        CancelDeleteNote();
        _pending = null;
        _dispatching = false;
        _storageReady = false;
        _storageRevision = 0;
        _captureError = _storageError = _statusMessage = _leaveTarget = null;
        _leaveHistoryKey = null;
        ++_departureSequence;
        _departureInFlight = null;
        _historyExpanded = false;
    }

    private async Task RestoreCaptureAsync(string owner)
    {
        try
        {
            var snapshot = await _module!.InvokeAsync<CaptureSnapshot?>("read", _root, owner, _lease);
            if (!Owns(owner))
            {
                return;
            }

            _storageReady = true;
            _storageError = null;
            if (snapshot is not null)
            {
                _storageRevision = snapshot.Revision;
                _draft = snapshot.Draft;
                _editingNoteId = snapshot.EditingNoteId;
                _editContent = snapshot.EditContent;
                _editOriginal = snapshot.EditOriginal;
                _editVersion = snapshot.EditVersion;
                _pending = snapshot.Pending;
                if (_pending is not null)
                {
                    _captureError = "A previous submission needs its receipt. Retry the original operation before moving to another player.";
                }
            }
        }
        catch (JSException)
        {
            if (Owns(owner))
            {
                _storageError = "Tab storage is unavailable. Nothing will be submitted until recovery storage is working. Your text remains copyable.";
            }
        }
    }

    private async Task<bool> PersistCaptureAsync()
    {
        var owner = Owner;
        if (!_storageReady || _module is null)
        {
            _storageError = "Recovery storage is not ready. Your text has not been submitted.";
            return false;
        }
        var snapshot = new CaptureSnapshot(++_storageRevision, _draft, _editingNoteId, _editContent, _editOriginal, _editVersion, _pending);
        try
        {
            var saved = await _module.InvokeAsync<bool>("write", _root, owner, _lease, snapshot);
            if (!Owns(owner))
            {
                return false;
            }

            _storageError = saved ? null : "Recovery storage changed before this action could be saved. Retry storage; nothing new was submitted.";
            return saved;
        }
        catch (JSException)
        {
            if (Owns(owner))
            {
                _storageError = "Tab storage could not save this action. Nothing new was submitted. Keep or copy your text and retry storage.";
            }

            return false;
        }
    }

    private async Task DraftChangedAsync(ChangeEventArgs args)
    {
        if (!_storageReady) { return; }
        _draft = args.Value?.ToString() ?? string.Empty;
        await PersistCaptureAsync();
    }

    private async Task EditChangedAsync(ChangeEventArgs args)
    {
        if (!_storageReady) { return; }
        _editContent = args.Value?.ToString() ?? string.Empty;
        await PersistCaptureAsync();
    }

    private async Task BeginEditAsync(CampaignParticipantNoteDto note)
    {
        if (!Writable || !_storageReady || !note.CanEdit || _pending is not null)
        {
            return;
        }

        if (_editingNoteId is not null && HasDraft) { _captureError = "Finish or cancel the current edit before editing another note."; return; }
        _editingNoteId = note.NoteId;
        CancelDeleteNote();
        _editVersion = note.Version;
        _editContent = _editOriginal = note.Content;
        _historyExpanded = true;
        await PersistCaptureAsync();
    }

    /// <summary>Retains the note revision reviewed when deletion is requested.</summary>
    /// <param name="note">The displayed note whose deletion is being confirmed.</param>
    private void BeginDeleteNote(CampaignParticipantNoteDto note)
    {
        if (!Writable || !_storageReady || !note.CanDelete || _pending is not null) { return; }
        _deleteNote = (note.NoteId, note.Version);
    }

    /// <summary>Clears deletion confirmation without changing a submitted recovery operation.</summary>
    private void CancelDeleteNote() => _deleteNote = null;

    /// <summary>Submits the reviewed revision even if history has refreshed since confirmation opened.</summary>
    /// <returns>The submission task, or a completed task when no confirmation is open.</returns>
    private Task ConfirmDeleteNoteAsync() => _deleteNote is { } reviewed
        ? SubmitAsync("delete", reviewed.NoteId, reviewed.Version)
        : Task.CompletedTask;

    private async Task CancelEditAsync()
    {
        _editingNoteId = null;
        _editContent = _editOriginal = string.Empty;
        await PersistCaptureAsync();
    }

    private async Task ReviewVersionAsync(CampaignParticipantNoteDto note)
    {
        _editVersion = note.Version;
        _editOriginal = note.Content;
        await PersistCaptureAsync();
    }

    private async Task RetryStorageAsync()
    {
        if (!_storageReady)
        {
            var draft = _draft;
            var owner = Owner;
            await RestoreCaptureAsync(owner);
            if (!Owns(owner))
            {
                return;
            }

            if (draft.Length > 0)
            {
                _draft = draft;
            }
        }
        await PersistCaptureAsync();
    }

    private async Task SubmitAsync(string kind, long? subjectId = null, Guid version = default, string? label = null)
    {
        if (!Writable || _pending is not null || State.ParticipantId is not { } id)
        {
            return;
        }

        var pending = new PendingCapture(kind, Guid.CreateVersion7(), id, subjectId,
            kind is "edit" ? _editVersion : version, kind switch { "add" => _draft, "edit" => _editContent, _ => label });
        var validation = InputValidator.Validate(pending.ToInput());
        if (validation.Count > 0)
        {
            _captureError = string.Join(" ", validation.Values.SelectMany(messages => messages));
            return;
        }
        _pending = pending;
        if (!await PersistCaptureAsync())
        {
            if (ReferenceEquals(_pending, pending))
            {
                _pending = null;
            }
            return;
        }
        await DispatchPendingAsync();
    }

    private async Task DispatchPendingAsync()
    {
        if (_pending is not { } pending || _dispatching || !_storageReady)
        {
            return;
        }
        var owner = Owner;
        if (pending.AssignmentId != State.ParticipantId)
        {
            _captureError = "This recovery belongs to a different player. Open that player to recover it.";
            return;
        }
        _dispatching = true;
        _captureError = null;
        try
        {
            if (!await PersistCaptureAsync() || !Owns(owner) || !ReferenceEquals(pending, _pending))
            {
                return;
            }
            var result = await DispatchAsync(pending);
            if (!Owns(owner) || !ReferenceEquals(pending, _pending))
            {
                return;
            }
            if (result.IsProblem)
            {
                await HandleRejectedCaptureAsync(pending, owner, result.Problem);
            }
            else
            {
                await HandleCommittedCaptureAsync(pending, owner, result.Value);
            }
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested && exception is HttpRequestException or JSException or OperationCanceledException)
        {
            if (Owns(owner))
            {
                _captureError = "The submission outcome is not yet known. Retry the original operation to recover its receipt.";
            }
        }
        finally
        {
            if (Owns(owner))
            {
                _dispatching = false;
            }
        }
    }

    private async Task HandleRejectedCaptureAsync(PendingCapture pending, string owner, ServiceProblem problem)
    {
        _captureError = problem.Detail ?? "The result is not yet known. Retry the original operation.";
        if (problem.Kind is not (ServiceProblemKind.Validation or ServiceProblemKind.Forbidden or ServiceProblemKind.NotFound or ServiceProblemKind.Conflict))
        {
            return;
        }
        _pending = null;
        if (pending.Kind is "delete") { CancelDeleteNote(); }
        var stored = await PersistCaptureAsync();
        if (!Owns(owner))
        {
            return;
        }
        if (!stored)
        {
            _pending = pending;
        }
        await Task.WhenAll(LoadIdentityAsync(), LoadNotesAsync(false), LoadApplicationsAsync(false), LoadChoicesAsync());
        if (Owns(owner))
        {
            await OnLifecycleChanged.InvokeAsync();
        }
    }

    private async Task HandleCommittedCaptureAsync(PendingCapture pending, string owner, string message)
    {
        _pending = null;
        if (pending.Kind is "add" && string.Equals(_draft, pending.Text, StringComparison.Ordinal))
        {
            _draft = string.Empty;
        }
        if (pending.Kind is "edit")
        {
            _editingNoteId = null;
            _editContent = _editOriginal = string.Empty;
        }
        CancelDeleteNote();
        _removeApplicationId = null;
        _statusMessage = message;
        var cleared = await PersistCaptureAsync();
        if (!Owns(owner))
        {
            return;
        }
        if (!cleared)
        {
            _pending = pending;
            _captureError = "The operation committed, but its recovery record could not be cleared. Retry to recover the same receipt.";
        }
        if (pending.Kind is "add" or "edit" or "delete")
        {
            await LoadNotesAsync(false);
        }
        else
        {
            await Task.WhenAll(LoadApplicationsAsync(false), LoadChoicesAsync());
        }
    }
    private async Task<ServiceResult<string>> DispatchAsync(PendingCapture pending)
    {
        switch (pending.ToInput())
        {
            case AddEvaluationNoteInput input:
                return (await notes.AddAsync(input, ComponentCancellationToken)).Match<ServiceResult<string>>(_ => "Note saved.", problem => problem);
            case EditEvaluationNoteInput input:
                return (await notes.EditAsync(input, ComponentCancellationToken)).Match<ServiceResult<string>>(_ => "Note updated.", problem => problem);
            case DeleteEvaluationNoteInput input:
                return (await notes.DeleteAsync(input, ComponentCancellationToken)).Match<ServiceResult<string>>(_ => "Note deleted.", problem => problem);
            case ApplyCampaignTagApplicationInput input:
                return (await tags.ApplyAsync(input, ComponentCancellationToken)).Match<ServiceResult<string>>(result => result.AlreadyApplied ? "Trait was already applied; its original attribution is preserved." : "Trait applied.", problem => problem);
            case CreateAndApplyCampaignTagInput input:
                return (await tags.CreateAndApplyAsync(input, ComponentCancellationToken)).Match<ServiceResult<string>>(result => result.AlreadyApplied ? "Trait was already applied; its original attribution is preserved." : "Trait applied.", problem => problem);
            case RemoveCampaignTagApplicationInput input:
                return (await tags.RemoveAsync(input, ComponentCancellationToken)).Match<ServiceResult<string>>(_ => "Trait removed.", problem => problem);
            default: return ServiceProblem.Conflict("The stored operation is not recognized. Keep a copy of the draft.");
        }
    }

    private sealed record CaptureSnapshot(long Revision, string Draft, long? EditingNoteId, string EditContent,
        string EditOriginal, Guid EditVersion, PendingCapture? Pending);

    private sealed record PendingCapture(string Kind, Guid OperationId, long AssignmentId, long? SubjectId, Guid Version, string? Text)
    {
        public EvaluationOperationInput ToInput() => Kind switch
        {
            "add" => new AddEvaluationNoteInput { OperationId = OperationId, PlayerCampaignAssignmentId = AssignmentId, Content = Text ?? string.Empty },
            "edit" => new EditEvaluationNoteInput { OperationId = OperationId, NoteId = SubjectId ?? 0, ExpectedVersion = Version, Content = Text ?? string.Empty },
            "delete" => new DeleteEvaluationNoteInput { OperationId = OperationId, NoteId = SubjectId ?? 0, ExpectedVersion = Version },
            "apply" => new ApplyCampaignTagApplicationInput { OperationId = OperationId, PlayerCampaignAssignmentId = AssignmentId, PlayerTagId = SubjectId ?? 0 },
            "create" => new CreateAndApplyCampaignTagInput { OperationId = OperationId, PlayerCampaignAssignmentId = AssignmentId, Label = Text ?? string.Empty },
            "remove" => new RemoveCampaignTagApplicationInput { OperationId = OperationId, CampaignTagApplicationId = SubjectId ?? 0 },
            _ => throw new InvalidOperationException("The stored evaluation operation kind is invalid.")
        };
    }
}
