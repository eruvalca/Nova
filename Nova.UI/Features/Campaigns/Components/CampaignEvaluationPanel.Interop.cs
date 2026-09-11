using Microsoft.JSInterop;

namespace Nova.UI.Features.Campaigns.Components;

public partial class CampaignEvaluationPanel
{
    private string? _initializingScope;
    private string? _failedInteropScope;
    private long _interopSequence;
    private string InteropScope => $"{Owner}:{FinderOwner}";

    private async Task<bool> EnsureEvaluationAttachmentAsync()
    {
        var owner = Owner;
        var finder = FinderOwner;
        var scope = InteropScope;
        if (string.Equals(_initializingScope, scope, StringComparison.Ordinal)
            || string.Equals(_failedInteropScope, scope, StringComparison.Ordinal)) { return false; }
        if (_module is not null && string.Equals(_attachedOwner, owner, StringComparison.Ordinal)
            && string.Equals(_attachedFinderOwner, finder, StringComparison.Ordinal)) { return true; }

        var sequence = ++_interopSequence;
        _initializingScope = scope;
        var captureScope = $"{CaptureScope ?? AuthorityScope}:{CampaignId}:{State.ParticipantId}";
        Task<IJSObjectReference>? load = null;
        bool current() => Owns(owner) && string.Equals(scope, InteropScope, StringComparison.Ordinal) && sequence == _interopSequence;
        try
        {
            load = _moduleLoad ??= js.InvokeAsync<IJSObjectReference>("import", "./_content/Nova.UI/Features/Campaigns/Components/CampaignEvaluationPanel.razor.js").AsTask();
            var module = await load;
            if (!current()) { return false; }
            _module = module;
            var captureChanged = !string.Equals(_attachedOwner, owner, StringComparison.Ordinal);
            _navigationReceiver ??= DotNetObjectReference.Create(this);
            await module.InvokeVoidAsync("attach", _root, owner, _lease, finder, captureScope, _navigationReceiver);
            if (!current()) { return false; }
            _attachedOwner = owner;
            _attachedFinderOwner = finder;
            _failedInteropScope = null;
            if (captureChanged) { await RestoreCaptureAsync(owner); }
            if (!current()) { return false; }
            StateHasChanged();
            return true;
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested && exception is JSException or OperationCanceledException)
        {
            if (load is not null && ReferenceEquals(load, _moduleLoad) && !load.IsCompletedSuccessfully) { _moduleLoad = null; }
            if (current())
            {
                FailEvaluationInterop(scope);
            }
            return false;
        }
        finally
        {
            if (sequence == _interopSequence) { _initializingScope = null; }
        }
    }

    private void FailEvaluationInterop(string scope)
    {
        _failedInteropScope = scope;
        _storageReady = false;
        _storageError = "Navigation protection is unavailable. Update your browser or retry before capturing evidence.";
        StateHasChanged();
    }
}
