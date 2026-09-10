using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Nova.UI.Features.Campaigns.Pages;

public partial class CampaignWorkspace
{
    private ElementReference _workspaceElement;
    private Task<bool>? _startupLocationCheck;
    private string? _startupLocationUri;
    private bool _startupLocationDelivered;
    private bool _startupLocationSettled;

    private void ObserveStartupLocationDelivery()
    {
        if (_startupLocationUri is not null
            && !string.Equals(new Uri(navigationManager.Uri).PathAndQuery,
                new Uri(_startupLocationUri).PathAndQuery, StringComparison.Ordinal))
        {
            // Remember delivery even when a subsequent Back navigation coalesces the renders.
            _startupLocationDelivered = true;
        }
    }

    private async Task<bool> ReconcileStartupLocationAsync(IJSObjectReference module)
    {
        if (_startupLocationSettled)
        {
            return false;
        }

        var navigationSequence = _navigationSequence;
        _startupLocationUri ??= navigationManager.Uri;
        _startupLocationCheck ??= module.InvokeAsync<bool>("reconcileWorkspaceLocation", ComponentCancellationToken,
            _workspaceElement, _jsOwner, _startupLocationUri, $"/campaigns/{CampaignId}").AsTask();
        var reconciled = await _startupLocationCheck;
        if (ComponentCancellationToken.IsCancellationRequested)
        {
            return true;
        }
        if (reconciled)
        {
            // A completed JS dispatch is not acknowledgment of the runtime's location update.
            // Unrelated renders must keep their effects blocked until parameters receive it.
            if (_startupLocationDelivered && !_startupLocationSettled)
            {
                _startupLocationSettled = true;
                StateHasChanged();
            }
            return true;
        }
        _startupLocationSettled = true;
        if (navigationSequence != _navigationSequence)
        {
            StateHasChanged();
            return true;
        }
        return false;
    }
}
