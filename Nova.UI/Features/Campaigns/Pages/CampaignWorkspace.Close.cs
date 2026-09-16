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
    [SupplyParameterFromQuery(Name = "closeOutcome")] private string? CloseOutcomeQuery { get; set; }
    [SupplyParameterFromQuery(Name = "closeParticipant")] private long? CloseParticipantQuery { get; set; }
    [SupplyParameterFromQuery(Name = "closeBeforeEventId")] private long? CloseBeforeEventQuery { get; set; }
    [SupplyParameterFromQuery(Name = "returnToClose")] private bool? ReturnToCloseQuery { get; set; }
    private CampaignWorkspaceCloseState CloseState => new()
    {
        Search = CampaignWorkspaceCloseState.NormalizeSearch(CloseSearchQuery),
        Page = CampaignWorkspaceCloseState.NormalizePage(ClosePageQuery),
        Blocker = CampaignWorkspaceCloseState.NormalizeBlocker(CloseBlockerQuery),
        Outcome = CampaignWorkspaceCloseState.NormalizeOutcome(CloseOutcomeQuery),
        ParticipantId = CloseParticipantQuery is > 0 ? CloseParticipantQuery : null,
        BeforeEventId = CloseParticipantQuery is > 0 && CloseBeforeEventQuery is > 0 ? CloseBeforeEventQuery : null,
    };
    /// <summary>The authoritative snapshot persisted across prerender and interactive attachment.</summary>
    [PersistentState] public CampaignLifecycleEvidence? PersistedCloseEvidence { get; set; }
    /// <summary>A completed failed startup read, owned independently of its absent readiness payload.</summary>
    [PersistentState] public CampaignCloseReadFailure? PersistedCloseFailure { get; set; }
    private CampaignLifecycleEvidence? _closeEvidence;
    private string? _closeError;
    private bool _closeLoading;
    private bool _closeReturnPending;
    private long _closeGeneration;
    private string? _closeReadKey;
    private string CloseOwner => $"{_authorityScope}:{CampaignId}:{_authenticationVersion}";
    private string CurrentCloseReadKey => $"{CloseOwner}:{_detailSequence}:{_detail?.Status}";

    private string WithCloseContext(string url) => CloseState.Apply(url, ReturnToCloseQuery == true);
    private void ClearReopenedCloseState(CampaignStatus? previous, CampaignStatus current)
    {
        if (previous != CampaignStatus.Closed || current != CampaignStatus.Active) { return; }
        CloseOutcomeQuery = null;
        CloseParticipantQuery = null;
        CloseBeforeEventQuery = null;
        ClosePageQuery = 1;
        navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameters(new Dictionary<string, object?>(StringComparer.Ordinal)
        {
            ["closeOutcome"] = null,
            ["closeParticipant"] = null,
            ["closeBeforeEventId"] = null,
            ["closePage"] = null,
        }), replace: true);
    }
    private string BuildCloseUrl(CampaignWorkspaceCloseState state)
        => state.Apply(CampaignWorkspaceUrlState.WithEvaluationContext(
            CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(CampaignId, _placementState, _filters, _selectedParticipantId, PlacementParticipantQuery, ReturnToEvaluationQuery == true)
                .Replace("tab=place", "tab=close", StringComparison.Ordinal), EvaluationState));
    private string BuildCloseParticipantUrl(long participantId)
        => CloseState.Apply(CampaignWorkspaceUrlState.WithEvaluationContext(
            CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(CampaignId, new() { Eligibility = "all" }, _filters, _selectedParticipantId, participantId), EvaluationState), returnToClose: true);
    private string BuildClosedEvaluationUrl(long participantId)
        => CloseState.Apply(CampaignWorkspaceUrlState.BuildEvaluationLookupUrl(CampaignId,
            EvaluationState with { ParticipantId = participantId }, _filters, _selectedParticipantId), returnToClose: true);
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
        PersistedCloseFailure = null;
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
        if (_closeReadKey is null && PersistedCloseFailure is { } failure
            && string.Equals(failure.Owner, CloseOwner, StringComparison.Ordinal) && failure.Detail == detail)
        {
            _closeError = failure.Message;
            _closeGeneration = failure.Generation;
            _closeReadKey = key;
            return;
        }
        _closeReadKey = key;
        var generation = _closeGeneration;
        _closeEvidence = null;
        PersistedCloseEvidence = null;
        PersistedCloseFailure = null;
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
            PersistedCloseFailure = new(CloseOwner, generation, detail, _closeError);
            if (result.IsSuccess && !_reconcilingLifecycle)
            {
                await OnCampaignReloadRequestedAsync();
            }
        }
    }
}
