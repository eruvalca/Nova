using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Pages;

public partial class CampaignWorkspace
{
    private string? _authorityScope;
    private int _detailSequence;
    private int _choiceSequence;
    private int _navigationSequence;
    private readonly string _jsOwner = Guid.NewGuid().ToString("N");
    private bool _reconcilingLifecycle;
    private string? _teamSearch;
    private int _teamSearchSequence;
    private bool _teamChoicesTruncated;
    private bool _nonTeamChoicesFailed;
    private bool _teamChoicesFailed;
    private bool _replaceClosedEligibilityUrl;
    private string StateOwner(CampaignStatus? status) => $"{_authorityScope}:{CampaignId}:{status}:{_appliedQueryString}";
    private IReadOnlyList<CampaignEffectivePlacementItem> _workingRows = [];
    private string? _rosterOwner;

    private CampaignWorkspaceRosterState NormalizeRosterFilters(CampaignWorkspaceRosterState state)
    {
        if (_detail?.Status != CampaignStatus.Closed || state.Eligibility is null)
        {
            return state;
        }
        _replaceClosedEligibilityUrl = true;
        return state with { Eligibility = null, Page = 1 };
    }

    private bool NormalizeCurrentRosterFilters()
    {
        var normalized = NormalizeRosterFilters(_filters);
        if (ReferenceEquals(normalized, _filters))
        {
            return false;
        }
        _filters = normalized;
        _appliedQueryString = CampaignWorkspaceUrlState.BuildQueryString(normalized);
        _searchDraft = normalized.Search ?? string.Empty;
        _pendingBoundaryMove = null;
        ++_navigationSequence;
        _reloadRosterPending = true;
        _scrollToRosterTop = true;
        return true;
    }

    private void ReplaceClosedEligibilityUrl()
    {
        if (!_replaceClosedEligibilityUrl)
        {
            return;
        }
        _replaceClosedEligibilityUrl = false;
        if (_detail?.Status == CampaignStatus.Closed && !ComponentCancellationToken.IsCancellationRequested)
        {
            // Preserve the pathname, participant, destination and placement return parameters.
            navigationManager.NavigateTo(navigationManager.GetUriWithQueryParameters(new Dictionary<string, object?>(StringComparer.Ordinal)
            {
                ["eligibility"] = null,
                ["page"] = null,
            }), replace: true);
        }
    }

    private void PrepareRosterLoad()
    {
        NormalizeCurrentRosterFilters();
        _reloadRosterPending = false;
        ReplaceClosedEligibilityUrl();
    }

    private void DiscardUnownedRoster(string owner)
    {
        if (!string.Equals(_rosterOwner, owner, StringComparison.Ordinal))
        {
            _roster = null;
            _workingRows = [];
        }
    }
    private int? _campaignParticipantCount;
    private EffectivePlacementCounts? _eligibilityCounts;

    private async Task OnParticipantDataChangedAsync()
    {
        await LoadRosterAsync();
        if (!ComponentCancellationToken.IsCancellationRequested)
        {
            await LoadChoicesAsync();
            PersistStartupState();
        }
    }

    private async Task OnTeamSearchChangedAsync(string search)
    {
        _teamSearch = string.IsNullOrWhiteSpace(search) ? null : search.Trim();
        var request = _choiceSequence;
        var searchRequest = _teamSearchSequence + 1;
        var success = await LoadTeamChoicesAsync(request);
        if (request == _choiceSequence && searchRequest == _teamSearchSequence && !ComponentCancellationToken.IsCancellationRequested)
        {
            _choicesLoadFailed = _nonTeamChoicesFailed || !success;
        }
    }

    private Task OnEligibilityChangedAsync(string? eligibility)
        => ApplyFiltersAndNavigateAsync(_filters with { Eligibility = string.IsNullOrEmpty(eligibility) ? null : eligibility, Page = 1 });

    private Task OnOrderChangedAsync(string ordering)
    {
        var values = ordering.Split(':', 2);
        return values.Length == 2
            ? ApplyFiltersAndNavigateAsync(_filters with { SortBy = values[0], SortDirection = values[1], Page = 1 })
            : Task.CompletedTask;
    }

    private async Task<ServiceResult<T>> ReadSafelyAsync<T>(Func<Task<ServiceResult<T>>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException && !ComponentCancellationToken.IsCancellationRequested)
        {
            return ServiceProblem.ServerError("Could not load this information. Check your connection and retry.");
        }
    }

    /// <summary>Preserves placement evidence alongside the roster page across interactive attachment.</summary>
    [PersistentState]
    public IReadOnlyList<CampaignEffectivePlacementItem>? PersistedWorkingRows { get; set; }

    /// <summary>Preserves the authoritative whole-campaign scale across interactive attachment.</summary>
    [PersistentState]
    public int? PersistedParticipantCount { get; set; }

    /// <summary>Preserves whole-campaign eligibility counts independently of discovery.</summary>
    [PersistentState]
    public EffectivePlacementCounts? PersistedEligibilityCounts { get; set; }

    /// <summary>Identifies the authority, campaign, lifecycle and query owning persisted workspace data.</summary>
    [PersistentState]
    public string? PersistedOwner { get; set; }

    /// <summary>Retains the bounded team-choice notice across interactive attachment.</summary>
    [PersistentState] public bool PersistedTeamChoicesTruncated { get; set; }
    /// <summary>Retains a regional choice failure without discarding successful neighboring data.</summary>
    [PersistentState] public bool PersistedChoicesLoadFailed { get; set; }

    private bool ClampRosterPage()
    {
        if (_roster is null)
        {
            return false;
        }
        var maximum = CampaignWorkspaceUrlState.CalculatePageCount(_roster.TotalCount, _roster.PageSize);
        if (_roster.Page <= maximum)
        {
            return false;
        }
        _pendingBoundaryMove = null;
        _filters = _filters with { Page = maximum };
        _appliedQueryString = CampaignWorkspaceUrlState.BuildQueryString(_filters);
        _reloadRosterPending = true;
        navigationManager.NavigateTo(BuildRosterUrl(_filters, _activeTab, _selectedParticipantId), replace: true);
        return true;
    }

    private void AcceptRosterSnapshot(RosterSnapshot snapshot, string owner)
    {
        _roster = snapshot.Roster;
        _rosterOwner = owner;
        _workingRows = snapshot.WorkingRows;
        _campaignParticipantCount = snapshot.ParticipantCount;
        _eligibilityCounts = snapshot.Counts;
    }

    private GetCampaignEffectivePlacementsInput BuildRosterInput() => new()
    {
        CampaignId = CampaignId,
        Search = _filters.Search,
        GraduationYears = _filters.GraduationYears.Count > 0 ? [.. _filters.GraduationYears] : null,
        TagDefinitionIds = _filters.TagDefinitionIds.Count > 0 ? [.. _filters.TagDefinitionIds] : null,
        LocalOutcome = _filters.Outcome,
        Eligibility = _filters.Eligibility,
        LocalTeamId = _filters.TeamId,
        SortBy = _filters.SortBy ?? "displayName",
        SortDirection = _filters.SortDirection ?? "asc",
        Page = _filters.Page,
        PageSize = RosterPageSize
    };
    private async Task<ServiceResult<RosterSnapshot>> ReadRosterSnapshotAsync(GetCampaignEffectivePlacementsInput input)
    {
        if (_detail?.Status == CampaignStatus.Closed)
        {
            var closed = await effectivePlacementQueryService.GetClosedCampaignRosterAsync(new()
            {
                CampaignId = input.CampaignId,
                Search = input.Search,
                GraduationYears = input.GraduationYears,
                TagDefinitionIds = input.TagDefinitionIds,
                LocalOutcome = input.LocalOutcome,
                LocalTeamId = input.LocalTeamId,
                SortBy = input.SortBy,
                SortDirection = input.SortDirection,
                Page = input.Page,
                PageSize = input.PageSize,
                ParticipantId = input.ParticipantId,
            }, ComponentCancellationToken);
            return closed.Match<ServiceResult<RosterSnapshot>>(result => new RosterSnapshot(
                new(result.Participants.Items.Select(row => new CampaignParticipantRosterItem(row.PlayerCampaignAssignmentId,
                    row.PlayerId, row.FirstName + " " + row.LastName, row.GraduationYear, row.TryoutNumber,
                    row.Source.Decision.Outcome, row.Source.Team, row.AppliedTags)).ToList().AsReadOnly(),
                    result.Participants.Page, result.Participants.PageSize, result.Participants.TotalCount),
                [], result.ParticipantCount, null), problem => problem);
        }

        var working = await effectivePlacementQueryService.GetCampaignEffectivePlacementsAsync(input, ComponentCancellationToken);
        return working.Match<ServiceResult<RosterSnapshot>>(result => new RosterSnapshot(
            new(result.Participants.Items.Select(row => new CampaignParticipantRosterItem(row.PlayerCampaignAssignmentId,
                row.PlayerId, row.FirstName + " " + row.LastName, row.GraduationYear, row.TryoutNumber,
                row.LocalDecision?.Outcome ?? PlacementOutcome.Undecided, row.LocalTeam, row.AppliedTags)).ToList().AsReadOnly(),
                result.Participants.Page, result.Participants.PageSize, result.Participants.TotalCount),
            result.Participants.Items, result.Counts.NeedsPlacement + result.Counts.OptionalReassignment + result.Counts.Resolved + result.Counts.Unavailable,
            result.Counts), problem => problem);
    }

    private sealed record RosterSnapshot(PagedResult<CampaignParticipantRosterItem> Roster,
        IReadOnlyList<CampaignEffectivePlacementItem> WorkingRows, int ParticipantCount, EffectivePlacementCounts? Counts);
}
