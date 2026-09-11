using Microsoft.JSInterop;

namespace Nova.UI.Features.Campaigns.Components;

public partial class CampaignParticipantDrawer
{
    private string? _drawerInitializingOwner;
    private string? _drawerInteropFailedOwner;
    private long _drawerInteropSequence;

    private async Task<bool> EnsureDrawerInteropAsync()
    {
        var owner = ParticipantOwner;
        if (string.Equals(_drawerInitializingOwner, owner, StringComparison.Ordinal)
            || string.Equals(_drawerInteropFailedOwner, owner, StringComparison.Ordinal)) { return false; }
        var sequence = ++_drawerInteropSequence;
        _drawerInitializingOwner = owner;
        var lazy = _moduleTask;
        bool current() => string.Equals(owner, ParticipantOwner, StringComparison.Ordinal)
            && !ComponentCancellationToken.IsCancellationRequested && sequence == _drawerInteropSequence;
        try
        {
            var module = await lazy.Value;
            if (!current()) { return false; }
            if (!_focusTrapInstalled)
            {
                await module.InvokeVoidAsync("open", _dialog, _closeButton);
                if (!current()) { return false; }
                _focusTrapInstalled = true;
                _lastRenderedParticipantId = ParticipantId;
            }
            _drawerInteropFailedOwner = null;
            return true;
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested && exception is JSException or OperationCanceledException)
        {
            if (ReferenceEquals(lazy, _moduleTask) && (!lazy.IsValueCreated || !lazy.Value.IsCompletedSuccessfully))
            {
                _moduleTask = new(() => jsRuntime.InvokeAsync<IJSObjectReference>("import", "./_content/Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.js").AsTask());
            }
            if (current())
            {
                _drawerInteropFailedOwner = owner;
                _drawerStorageFailed = true;
                _drawerStorageReady = false;
                _mutationError = "Recovery storage or navigation protection is unavailable. Keep or copy your text; update your browser or retry.";
                StateHasChanged();
            }
            return false;
        }
        finally
        {
            if (sequence == _drawerInteropSequence) { _drawerInitializingOwner = null; }
        }
    }
}
