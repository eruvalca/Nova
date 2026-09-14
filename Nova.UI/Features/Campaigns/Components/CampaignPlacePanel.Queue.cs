
using System.Globalization;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Services;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// The Place queue's authoritative page and unfiltered section totals.
/// </summary>
/// <param name="Rows">The bounded page of queue rows.</param>
/// <param name="Page">The one-based page returned.</param>
/// <param name="PageSize">The page size requested.</param>
/// <param name="TotalCount">The filtered match count before paging.</param>
/// <param name="Sections">The whole-campaign written sections, independent of filters and paging.</param>
public sealed record CampaignPlaceQueueData(
    IReadOnlyList<CampaignPlaceQueueRow> Rows,
    int Page,
    int PageSize,
    int TotalCount,
    IReadOnlyList<CampaignPlaceSection> Sections)
{
    /// <summary>
    /// Indicates the whole-campaign totals could not be read, so the section list is unknown rather than
    /// empty. The rows in this page are still authoritative.
    /// </summary>
    public bool TotalsUnavailable { get; init; }
}

/// <summary>
/// One written queue section: its filter token, its label, its whole-campaign total, and whether it leads.
/// </summary>
/// <param name="Token">The section's filter token.</param>
/// <param name="Label">The written section label.</param>
/// <param name="Count">The whole-campaign total, independent of filters and paging.</param>
/// <param name="Leads">Whether this section carries the destination's headline attention.</param>
public sealed record CampaignPlaceSection(string Token, string Label, int Count, bool Leads);

/// <summary>
/// One choice in the selected participant's compatible team select.
/// </summary>
/// <param name="TeamId">The team identifier.</param>
/// <param name="Name">The team display name.</param>
/// <param name="Unavailable">
/// Whether the team is the participant's saved team that is no longer active or compatible. An unavailable
/// choice is rendered disabled so the current decision stays legible and can never be re-selected.
/// </param>
public sealed record CampaignPlaceTeamChoice(long TeamId, string Name, bool Unavailable);

/// <summary>
/// The persisted Place queue snapshot carried across prerender and interactive attach.
/// </summary>
public sealed record CampaignPlacePersistedSnapshot
{
    /// <summary>The bounded page of queue rows.</summary>
    public IReadOnlyList<CampaignPlaceQueueRow> Rows { get; init; } = [];

    /// <summary>The one-based page returned.</summary>
    public int Page { get; init; } = 1;

    /// <summary>The page size requested.</summary>
    public int PageSize { get; init; } = GetCampaignEffectivePlacementsInput.DefaultPageSize;

    /// <summary>The filtered match count before paging.</summary>
    public int TotalCount { get; init; }

    /// <summary>The whole-campaign written sections.</summary>
    public IReadOnlyList<CampaignPlaceSection> Sections { get; init; } = [];

    /// <summary>The selected participant assignment, or <see langword="null"/> when nothing is selected.</summary>
    public long? SelectedParticipantId { get; init; }

    /// <summary>Indicates the retained totals are no longer known to be current.</summary>
    public bool Stale { get; init; }
}

/// <summary>
/// Loads the Place queue page and its unfiltered section totals from the authoritative read for the lifecycle.
/// </summary>
public partial class CampaignPlacePanel
{
    /// <summary>
    /// Loads the queue page for the supplied discovery state and adopts it as the applied state.
    /// </summary>
    /// <param name="state">The Place discovery state to load.</param>
    /// <returns>A task that completes when the load finishes.</returns>
    private async Task LoadQueueAsync(CampaignWorkspacePlacementState state)
    {
        var request = ++_queueRequestSequence;
        var status = CampaignStatus;
        _queueLoading = true;
        _queueError = null;

        var loaded = IsClosedContext(status)
            ? await ReadClosedQueueAsync(state, request)
            : await ReadActiveQueueAsync(state, request);

        if (request != _queueRequestSequence || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        // A read that lands past the end of the result set would render an empty queue, hide the pager, and
        // claim the campaign has no participants. Clamp to the last real page and correct the URL instead of
        // publishing a snapshot that misrepresents the campaign. The loading posture is kept until the
        // corrected state arrives.
        if (loaded is not null)
        {
            var lastPage = Math.Max(1, (int)Math.Ceiling(loaded.TotalCount / (double)Math.Max(1, loaded.PageSize)));
            if (state.Page > lastPage)
            {
                await OnStateChanged.InvokeAsync(state with { Page = lastPage });
                return;
            }
        }

        _queueLoading = false;
        if (loaded is null)
        {
            // A failed load keeps whatever was already on screen but marks it stale, so an
            // exact-looking total is never presented beside a read that did not answer. The applied
            // lifecycle marker still advances: the posture was decided, and repeating the same failing
            // read on every parameter pass would not make it succeed.
            _appliedState = state;
            _appliedStatus = status;
            _queueError = QueueFailureMessage;
            _queueStale = _queue is not null;
            return;
        }

        _appliedState = state;
        _appliedStatus = status;
        _appliedQueryString = QueryKey(state);
        _searchDraft = state.Search ?? string.Empty;
        _queue = loaded;

        // A Closed campaign whose campaign-local totals could not be read keeps its rows but must say the
        // totals are unavailable rather than presenting an empty section list as fact.
        _queueStale = loaded.TotalsUnavailable;
        PersistQueue();
    }

    /// <summary>
    /// Re-reads the queue, the unfiltered totals, and the selected participant after a committed decision.
    /// </summary>
    /// <returns>A task that completes when reconciliation finishes.</returns>
    /// <summary>
    /// Re-reads the queue, the unfiltered totals, and the selected participant after a committed decision.
    /// </summary>
    /// <returns>
    /// <see langword="true"/> when an authoritative snapshot was adopted; <see langword="false"/> when the
    /// read failed or corrected the page, so settlement must wait for the corrected read.
    /// </returns>
    private async Task<bool> ReconcileAsync()
    {
        var request = ++_queueRequestSequence;
        var status = CampaignStatus;
        var state = _appliedState;

        var loaded = IsClosedContext(status)
            ? await ReadClosedQueueAsync(state, request)
            : await ReadActiveQueueAsync(state, request);

        if (request != _queueRequestSequence || ComponentCancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (loaded is not null)
        {
            // The same last-page correction a normal load applies. Committing the final row on the last page
            // can shrink the result set, and adopting that empty page would hide the pager and claim the
            // campaign has no participants.
            var lastPage = Math.Max(1, (int)Math.Ceiling(loaded.TotalCount / (double)Math.Max(1, loaded.PageSize)));
            if (state.Page > lastPage)
            {
                await OnStateChanged.InvokeAsync(state with { Page = lastPage });
                return false;
            }

            _queue = loaded;
            _queueError = null;
            _queueStale = loaded.TotalsUnavailable;
        }
        else
        {
            _queueStale = _queue is not null;
            _queueError = _queue is null ? QueueFailureMessage : null;
        }

        await RefreshSelectionAsync(force: true);
        PersistQueue();
        return true;
    }

    /// <summary>
    /// Determines whether the supplied lifecycle uses the immutable Closed read.
    /// </summary>
    /// <param name="status">The lifecycle to test.</param>
    /// <returns><see langword="true"/> when the Closed read applies.</returns>
    private static bool IsClosedContext(CampaignStatus status) => status != CampaignStatus.Active;

    /// <summary>
    /// Reads one Active queue page and its unfiltered eligibility totals.
    /// </summary>
    /// <param name="state">The Place discovery state to apply.</param>
    /// <param name="request">The request identifier used to discard obsolete responses.</param>
    /// <returns>The loaded page, or <see langword="null"/> when the read failed.</returns>
    private async Task<CampaignPlaceQueueData?> ReadActiveQueueAsync(CampaignWorkspacePlacementState state, int request)
    {
        var result = await ReadSafelyAsync(() => placementQueries.GetCampaignEffectivePlacementsAsync(
            new GetCampaignEffectivePlacementsInput
            {
                CampaignId = CampaignId,
                Search = state.Search,
                GraduationYears = state.GraduationYears.Count > 0 ? [.. state.GraduationYears] : null,
                TagDefinitionIds = state.TagDefinitionIds.Count > 0 ? [.. state.TagDefinitionIds] : null,
                LocalOutcome = state.Outcome,
                LocalTeamId = state.TeamId,
                SortBy = state.SortBy,
                SortDirection = state.SortDirection,
                Eligibility = CampaignWorkspaceUrlState.ResolvePlacementEligibility(state),
                Page = state.Page,
                PageSize = GetCampaignEffectivePlacementsInput.DefaultPageSize
            },
            ComponentCancellationToken));

        if (request != _queueRequestSequence || ComponentCancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return result.Match<CampaignPlaceQueueData?>(
            success => new CampaignPlaceQueueData(
                [.. success.Participants.Items.Select(CampaignPlaceQueueRow.FromActive)],
                success.Participants.Page,
                success.Participants.PageSize,
                success.Participants.TotalCount,
                [
                    new CampaignPlaceSection("NeedsPlacement", "Needs placement", success.Counts.NeedsPlacement, Leads: true),
                    new CampaignPlaceSection("OptionalReassignment", "Assigned this season", success.Counts.OptionalReassignment, Leads: false),
                    new CampaignPlaceSection("Resolved", "Not selected", success.Counts.Resolved, Leads: false),
                    new CampaignPlaceSection("Unavailable", "Unavailable", success.Counts.Unavailable, Leads: false)
                ]),
            _ => null);
    }

    /// <summary>
    /// Reads one Closed queue page and the campaign-local outcome totals the replaced surface also showed.
    /// </summary>
    /// <param name="state">The Place discovery state to apply.</param>
    /// <param name="request">The request identifier used to discard obsolete responses.</param>
    /// <returns>The loaded page, or <see langword="null"/> when the read failed.</returns>
    private async Task<CampaignPlaceQueueData?> ReadClosedQueueAsync(CampaignWorkspacePlacementState state, int request)
    {
        var result = await ReadSafelyAsync(() => placementQueries.GetClosedCampaignRosterAsync(
            new GetClosedCampaignRosterInput
            {
                CampaignId = CampaignId,
                Search = state.Search,
                GraduationYears = state.GraduationYears.Count > 0 ? [.. state.GraduationYears] : null,
                TagDefinitionIds = state.TagDefinitionIds.Count > 0 ? [.. state.TagDefinitionIds] : null,
                LocalOutcome = state.Outcome,
                LocalTeamId = state.TeamId,
                SortBy = state.SortBy,
                SortDirection = state.SortDirection,
                Page = state.Page,
                PageSize = GetClosedCampaignRosterInput.DefaultPageSize
            },
            ComponentCancellationToken));

        if (request != _queueRequestSequence || ComponentCancellationToken.IsCancellationRequested || !result.IsSuccess)
        {
            return null;
        }

        var closed = result.Value;

        var summary = await ReadSafelyAsync(() => campaignPlacementQueries.GetPlacementSummaryAsync(
            new GetCampaignPlacementSummaryInput { CampaignId = CampaignId }, ComponentCancellationToken));
        if (request != _queueRequestSequence || ComponentCancellationToken.IsCancellationRequested)
        {
            return null;
        }

        return new CampaignPlaceQueueData(
            [.. closed.Participants.Items.Select(CampaignPlaceQueueRow.FromClosed)],
            closed.Participants.Page,
            closed.Participants.PageSize,
            closed.Participants.TotalCount,
            BuildClosedSections(summary))
        {
            // The rows are authoritative; only the campaign-local totals are unknown.
            TotalsUnavailable = !summary.IsSuccess
        };
    }

    /// <summary>
    /// Builds the Closed posture's written sections from the campaign-local outcome summary.
    /// </summary>
    /// <param name="summary">The campaign-local outcome summary read, or <see langword="null"/> when it failed.</param>
    /// <returns>The written sections; unknown totals are omitted rather than shown as zero.</returns>
    private static IReadOnlyList<CampaignPlaceSection> BuildClosedSections(ServiceResult<CampaignPlacementSummaryDto> summary)
    {
        if (!summary.IsSuccess)
        {
            return [];
        }

        return
        [
            new CampaignPlaceSection("undecided", "No campaign decision", summary.Value.UndecidedCount, Leads: false),
            new CampaignPlaceSection("assigned", "Assigned", summary.Value.AssignedCount, Leads: false),
            new CampaignPlaceSection("notselected", "Not selected", summary.Value.NotSelectedCount, Leads: false),
            new CampaignPlaceSection("withdrawn", "Withdrawn", summary.Value.WithdrawnCount, Leads: false)
        ];
    }

    /// <summary>
    /// Awaits a service read, converting a transport failure into an unavailable service problem and
    /// re-throwing genuine cancellation of the component's own token.
    /// </summary>
    /// <typeparam name="T">The read's success type.</typeparam>
    /// <param name="read">The read to await.</param>
    /// <returns>The service result, or a server-error problem when the transport failed.</returns>
    private async Task<ServiceResult<T>> ReadSafelyAsync<T>(Func<Task<ServiceResult<T>>> read)
    {
        try
        {
            return await read();
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException
            && !ComponentCancellationToken.IsCancellationRequested)
        {
            // A regional read that cannot reach the server reports its own unavailability; it is never
            // presented as an exact but empty result, and neighboring regions keep their data.
            return ServiceProblem.ServerError("Could not load this information. Check your connection and retry.");
        }
    }

    /// <summary>
    /// Determines whether the supplied written section is the applied one.
    /// </summary>
    /// <param name="section">The written section to test.</param>
    /// <returns><see langword="true"/> when the section is applied.</returns>
    private bool IsSectionSelected(CampaignPlaceSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (IsClosedContext(CampaignStatus))
        {
            return string.Equals(_appliedState.Outcome, section.Token, StringComparison.Ordinal);
        }

        if (string.Equals(_appliedState.Eligibility, CampaignWorkspaceUrlState.AllPlacementSections, StringComparison.Ordinal))
        {
            return false;
        }

        return string.Equals(
            CampaignWorkspaceUrlState.ResolvePlacementEligibility(_appliedState), section.Token, StringComparison.Ordinal);
    }

    /// <summary>
    /// Applies a written section as the queue's scope.
    /// </summary>
    /// <param name="section">The written section to apply.</param>
    /// <returns>A task that completes when the change is raised.</returns>
    private Task OnSectionSelectedAsync(CampaignPlaceSection section)
    {
        ArgumentNullException.ThrowIfNull(section);

        return IsClosedContext(CampaignStatus)
            ? ApplyStateAsync(_appliedState with { Outcome = section.Token, Page = 1 })
            : ApplyStateAsync(_appliedState with { Eligibility = section.Token, Page = 1 });
    }

    /// <summary>
    /// Gets the section value the shared discovery control should display.
    /// </summary>
    private string SectionFilterValue => IsClosedContext(CampaignStatus)
        ? string.Empty
        : CampaignWorkspaceUrlState.ResolvePlacementEligibility(_appliedState) ?? string.Empty;

    /// <summary>
    /// Gets the unfiltered eligibility totals the shared section control annotates, or <see langword="null"/>
    /// for the Closed posture, whose sections are campaign-local outcomes rather than eligibility.
    /// </summary>
    private EffectivePlacementCounts? ActiveSectionCounts => IsClosedContext(CampaignStatus) || _queue is null
        ? null
        : new EffectivePlacementCounts(
            SectionCount("NeedsPlacement"),
            SectionCount("OptionalReassignment"),
            SectionCount("Resolved"),
            SectionCount("Unavailable"));

    /// <summary>
    /// Reads a written section's unfiltered total from the loaded queue.
    /// </summary>
    /// <param name="token">The section token to read.</param>
    /// <returns>The written total, or zero when the section is not reported.</returns>
    private int SectionCount(string token)
        => _queue?.Sections.FirstOrDefault(section => string.Equals(section.Token, token, StringComparison.Ordinal))?.Count ?? 0;

    /// <summary>
    /// Projects a drafted team identifier as a select-binding value.
    /// </summary>
    /// <param name="teamId">The drafted team identifier.</param>
    /// <returns>The invariant string value, or an empty string when no team is drafted.</returns>
    private static string TeamValue(long? teamId) => teamId?.ToString(CultureInfo.InvariantCulture) ?? string.Empty;

    /// <summary>
    /// Gets the written queue failure statement.
    /// </summary>
    private static string QueueFailureMessage => "The placement queue could not be loaded.";
}
