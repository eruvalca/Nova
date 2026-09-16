using Microsoft.AspNetCore.Components;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Pages;

public partial class CampaignWorkspace
{
    private Task LoadRosterRegionsAsync() => string.Equals(_activeTab, EvaluateTabName, StringComparison.Ordinal)
        ? Task.CompletedTask : Task.WhenAll(LoadChoicesAsync(), LoadRosterAsync());

    private void ApplyWorkspaceTab(string previousTab)
    {
        _activeTab = IsRosterLanding ? RosterTabName : CampaignWorkspaceUrlState.NormalizeTab(TabQuery);
        if (string.Equals(previousTab, EvaluateTabName, StringComparison.Ordinal)
            && !string.Equals(_activeTab, EvaluateTabName, StringComparison.Ordinal))
        {
            _reloadRosterPending = true;
        }
    }

    private string? _captureScope;
    [SupplyParameterFromQuery(Name = "placementParticipant")] private long? PlacementParticipantQuery { get; set; }
    [SupplyParameterFromQuery(Name = "returnToEvaluation")] private bool? ReturnToEvaluationQuery { get; set; }
    [SupplyParameterFromQuery(Name = "evaluation")] private bool? EvaluationQuery { get; set; }
    [SupplyParameterFromQuery(Name = "evalSearch")] private string? EvaluationSearchQuery { get; set; }
    [SupplyParameterFromQuery(Name = "evalPage")] private int? EvaluationPageQuery { get; set; }
    [SupplyParameterFromQuery(Name = "evalParticipant")] private long? EvaluationParticipantQuery { get; set; }
    [SupplyParameterFromQuery(Name = "rosterLanding")] private bool? RosterLandingQuery { get; set; }

    private CampaignWorkspaceEvaluationState EvaluationState => new()
    {
        Search = string.IsNullOrWhiteSpace(EvaluationSearchQuery) ? null : EvaluationSearchQuery.Trim(),
        Page = Math.Max(1, EvaluationPageQuery ?? 1),
        ParticipantId = EvaluationParticipantQuery is > 0 ? EvaluationParticipantQuery
            : LegacyEvaluationParticipant,
        RosterLanding = IsRosterLanding || RosterLandingQuery == true
    };

    private long? LegacyEvaluationParticipant => EvaluationQuery != true && string.Equals(_activeTab, EvaluateTabName, StringComparison.Ordinal) ? _selectedParticipantId : null;

    private string EvaluationUrl => WithCloseContext(WithPlacementContext(CampaignWorkspaceUrlState.BuildEvaluationLookupUrl(CampaignId,
        string.Equals(_activeTab, RosterTabName, StringComparison.Ordinal) && _selectedParticipantId is not null
            ? EvaluationState with { ParticipantId = _selectedParticipantId } : EvaluationState, _filters, _selectedParticipantId)));

    private string WithPlacementContext(string url)
    {
        var query = CampaignWorkspaceUrlState.BuildPlacementQueryString(_placementState);
        if (PlacementParticipantQuery is > 0) { query += $"&placementParticipant={PlacementParticipantQuery}"; }
        if (ReturnToEvaluationQuery == true) { query += "&returnToEvaluation=true"; }
        if (string.IsNullOrEmpty(query)) { return url; }
        return url + (url.Contains('?', StringComparison.Ordinal) ? "&" : "?") + query.TrimStart('&');
    }

    private string CloseUrl => BuildCloseUrl(CloseState);
}
