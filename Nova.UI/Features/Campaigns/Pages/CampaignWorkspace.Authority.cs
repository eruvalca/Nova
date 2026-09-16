using Microsoft.AspNetCore.Components.Authorization;
using Nova.SharedKernel.Security;

namespace Nova.UI.Features.Campaigns.Pages;

public partial class CampaignWorkspace
{
    private int _authenticationVersion;
    private int _openingReceiptGeneration;

    /// <summary>Invalidates mounted evidence and pending interactions when identity or authority changes.</summary>
    private void OnAuthenticationStateChanged(Task<AuthenticationState> stateTask)
        => _ = InvokeAsync(async () =>
        {
            var version = ++_authenticationVersion;
            var state = await stateTask;
            if (version != _authenticationVersion || ComponentCancellationToken.IsCancellationRequested) { return; }
            var captureScope = $"{state.User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value}:{state.User.FindFirst(NovaClaimTypes.ClubId)?.Value}";
            var isAdmin = state.User.IsInRole(Roles.ClubAdmin);
            var authorityScope = $"{captureScope}:{isAdmin}";
            if (string.Equals(authorityScope, _authorityScope, StringComparison.Ordinal)) { return; }
            _authorityScope = authorityScope;
            _captureScope = captureScope;
            _isClubAdmin = isAdmin;
            ResetAuthorityEvidence();
            StateHasChanged();
            await LoadDetailAsync();
            if (version != _authenticationVersion || ComponentCancellationToken.IsCancellationRequested) { return; }
            PersistStartupState();
            StateHasChanged();
        });

    private void ResetAuthorityEvidence()
    {
        InvalidateCloseEvidence();
        ++_detailSequence;
        ++_requestSequence;
        ++_choiceSequence;
        ++_navigationSequence;
        ++_teamSearchSequence;
        _searchDebounceSource?.Cancel();
        _detail = null;
        _roster = null;
        _workingRows = [];
        _campaignParticipantCount = null;
        _eligibilityCounts = null;
        _availableGraduationYears = [];
        _availableTags = [];
        _availableTeams = [];
        _pendingBoundaryMove = null;
        _openingReceiptMessage = null;
        _receiptChecked = false;
        ++_openingReceiptGeneration;
        _pageError = null;
        _rosterError = null;
        _isLoading = true;
        CancelMutationForm();
        PersistStartupState();
    }

    private bool OwnsOpeningReceipt(string scope, long campaignId, int generation)
        => generation == _openingReceiptGeneration && CampaignId == campaignId
            && string.Equals(scope, _authorityScope, StringComparison.Ordinal)
            && !ComponentCancellationToken.IsCancellationRequested;
}
