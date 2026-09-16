using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Pages;

public partial class CampaignWorkspace
{
    [SupplyParameterFromQuery(Name = "closeSearch")] private string? CloseSearchQuery { get; set; }
    [SupplyParameterFromQuery(Name = "closePage")] private int? ClosePageQuery { get; set; }
    [SupplyParameterFromQuery(Name = "closeBlocker")] private string? CloseBlockerQuery { get; set; }
    [SupplyParameterFromQuery(Name = "returnToClose")] private bool? ReturnToCloseQuery { get; set; }
    private CampaignWorkspaceCloseState CloseState => new()
    {
        Search = CampaignWorkspaceCloseState.NormalizeSearch(CloseSearchQuery),
        Page = Math.Max(1, ClosePageQuery ?? 1),
        Blocker = CampaignWorkspaceCloseState.NormalizeBlocker(CloseBlockerQuery),
    };
    /// <summary>The authoritative snapshot persisted across prerender and interactive attachment.</summary>
    [PersistentState] public CampaignLifecycleEvidence? PersistedCloseEvidence { get; set; }
    private CampaignLifecycleEvidence? _closeEvidence;
    private string? _closeError;
    private bool _closeLoading;
    private bool _closeReturnPending;
    private long _closeGeneration;
    private string? _closeReadKey;
    private string CloseOwner => $"{_authorityScope}:{CampaignId}:{_authenticationVersion}";
    private string CurrentCloseReadKey => $"{CloseOwner}:{_detailSequence}:{_detail?.Status}";

    private string WithCloseContext(string url) => CloseState.Apply(url, ReturnToCloseQuery == true);
    private string BuildCloseUrl(CampaignWorkspaceCloseState state)
        => state.Apply(CampaignWorkspaceUrlState.WithEvaluationContext(
            CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(CampaignId, _placementState, _filters, _selectedParticipantId, PlacementParticipantQuery, ReturnToEvaluationQuery == true)
                .Replace("tab=place", "tab=close", StringComparison.Ordinal), EvaluationState));
    private string BuildCloseParticipantUrl(long participantId)
        => CloseState.Apply(CampaignWorkspaceUrlState.WithEvaluationContext(
            CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(CampaignId, new() { Eligibility = "all" }, _filters, _selectedParticipantId, participantId), EvaluationState), returnToClose: true);
    private Task OnCloseStateChangedAsync(CampaignWorkspaceCloseState state)
    {
        navigationManager.NavigateTo(BuildCloseUrl(state));
        return Task.CompletedTask;
    }
    private void InvalidateCloseEvidence()
    {
        ++_closeGeneration;
        _closeEvidence = null;
        PersistedCloseEvidence = null;
        _closeReadKey = null;
        _closeError = null;
        _closeLoading = false;
    }
    private Task RetryCloseReadinessAsync() => LoadDetailAsync(reloadRoster: false);

    private async Task<CampaignLifecycleEvidence?> RefreshCloseEvidenceAsync()
    {
        var owner = CloseOwner;
        await LoadDetailAsync();
        if (!string.Equals(owner, CloseOwner, StringComparison.Ordinal) || ComponentCancellationToken.IsCancellationRequested)
        {
            return null;
        }
        PersistStartupState();
        StateHasChanged();
        return _pageError is null && !_notFound ? _closeEvidence : null;
    }
    private async Task LoadCloseReadinessAsync()
    {
        var key = CurrentCloseReadKey;
        var detail = _detail;
        if (detail is null || detail.Status == CampaignStatus.Draft || string.Equals(_closeReadKey, key, StringComparison.Ordinal))
        {
            return;
        }
        if (_closeReadKey is null && PersistedCloseEvidence is { } persisted
            && string.Equals(persisted.Owner, CloseOwner, StringComparison.Ordinal) && persisted.Detail == detail)
        {
            _closeEvidence = persisted;
            _closeGeneration = persisted.Generation;
            _closeReadKey = key;
            return;
        }
        _closeReadKey = key;
        var generation = _closeGeneration;
        _closeEvidence = null;
        _closeLoading = true;
        _closeError = null;
        var result = await ReadSafelyAsync(() => closeoutQueryService.GetCloseoutReadinessAsync(new() { CampaignId = CampaignId }, ComponentCancellationToken));
        if (generation != _closeGeneration || !string.Equals(key, CurrentCloseReadKey, StringComparison.Ordinal) || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }
        _closeLoading = false;
        if (result.IsSuccess && result.Value.Status == detail.Status)
        {
            _closeEvidence = new(CloseOwner, generation, detail, result.Value);
            PersistedCloseEvidence = _closeEvidence;
        }
        else
        {
            _closeError = "Close readiness could not be verified. Refresh the campaign before taking an action.";
            if (result.IsSuccess && !_reconcilingLifecycle)
            {
                await OnCampaignReloadRequestedAsync();
            }
        }
    }
}
