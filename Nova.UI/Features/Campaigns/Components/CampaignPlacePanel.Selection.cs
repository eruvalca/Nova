
using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Results;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Loads the selected participant's authoritative evidence and the bounded teams compatible with them.
/// </summary>
public partial class CampaignPlacePanel
{
    /// <summary>
    /// Resolves the selected participant's evidence, reusing the loaded page row when it already carries it.
    /// </summary>
    /// <param name="force">
    /// When <see langword="true"/>, always re-reads from the server instead of reusing the loaded page row.
    /// Reconciliation after a committed decision uses this so the evidence is authoritative, never patched.
    /// </param>
    /// <returns>A task that completes when the selection is resolved.</returns>
    private async Task RefreshSelectionAsync(bool force = false)
    {
        var participantId = SelectedParticipantId;
        var previousParticipantId = _appliedParticipantId;
        _appliedParticipantId = participantId;

        // Focus follows the stage that replaced the one the member was in.
        if (previousParticipantId is null && participantId is not null)
        {
            _stageFocusTarget = true;
        }
        else if (previousParticipantId is not null && participantId is null)
        {
            _stageFocusTarget = false;
        }

        // Every entry invalidates any in-flight selection read, including the branches that resolve without
        // a server round trip. Otherwise a read started for a previous participant can land after the user
        // selected someone else and quietly adopt the wrong row, which would record a decision against the
        // participant the sheet no longer shows.
        var request = ++_selectedRequestSequence;
        _selectedLoading = false;

        if (participantId is null)
        {
            _selected = null;
            _selectedError = null;
            _compatibleTeams = [];
            _teamChoicesError = null;
            ApplyDraftFromSelection();
            return;
        }

        if (!force && _queueError is null && !_queueStale
            && _queue?.Rows.FirstOrDefault(row => row.PlayerCampaignAssignmentId == participantId.Value) is { } onPage)
        {
            await AdoptSelectionAsync(onPage, request);
            return;
        }

        // The sheet must never keep showing, and drafting against, the previous participant while a new one
        // is resolving: the decision controls submit against what the sheet shows, so a stale sheet here
        // would let a save land on the participant the member has already moved past.
        _selected = null;
        ApplyDraftFromSelection();
        _selectedLoading = true;
        _selectedError = null;
        var row = await ReadSelectionAsync(participantId.Value, request);
        if (request != _selectedRequestSequence || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        _selectedLoading = false;
        if (row is null)
        {
            _selectedError = "The selected participant's placement evidence could not be loaded.";
            return;
        }

        await AdoptSelectionAsync(row, request);
    }

    /// <summary>
    /// Reads one participant's evidence from the authoritative read for the current lifecycle.
    /// </summary>
    /// <param name="participantId">The participant assignment to read.</param>
    /// <param name="request">The request identifier used to discard obsolete responses.</param>
    /// <returns>The participant's row, or <see langword="null"/> when the read failed.</returns>
    private async Task<CampaignPlaceQueueRow?> ReadSelectionAsync(long participantId, int request)
    {
        if (IsClosedContext(CampaignStatus))
        {
            var closed = await ReadSafelyAsync(() => placementQueries.GetClosedCampaignRosterAsync(
                new GetClosedCampaignRosterInput { CampaignId = CampaignId, ParticipantId = participantId, Page = 1, PageSize = 1 },
                ComponentCancellationToken));
            if (request != _selectedRequestSequence || ComponentCancellationToken.IsCancellationRequested || closed is null)
            {
                return null;
            }

            return closed.Match(
                success => success.Participants.Items.Count > 0
                    ? CampaignPlaceQueueRow.FromClosed(success.Participants.Items[0])
                    : null,
                _ => null);
        }

        var active = await ReadSafelyAsync(() => placementQueries.GetCampaignEffectivePlacementsAsync(
            new GetCampaignEffectivePlacementsInput { CampaignId = CampaignId, ParticipantId = participantId, Page = 1, PageSize = 1 },
            ComponentCancellationToken));
        if (request != _selectedRequestSequence || ComponentCancellationToken.IsCancellationRequested || active is null)
        {
            return null;
        }

        return active.Match(
            success => success.Participants.Items.Count > 0
                ? CampaignPlaceQueueRow.FromActive(success.Participants.Items[0])
                : null,
            _ => null);
    }

    /// <summary>
    /// Adopts a resolved participant as the selection and loads the teams compatible with them.
    /// </summary>
    /// <param name="row">The resolved participant row.</param>
    /// <param name="request">The selection request identifier that resolved the row.</param>
    /// <returns>A task that completes when the compatible team choices are loaded.</returns>
    private async Task AdoptSelectionAsync(CampaignPlaceQueueRow row, int request)
    {
        // Adopt only a row that still owns the selection: an obsolete read, a superseded request, or a row
        // for a different participant must never replace what the sheet and the URL agree on.
        if (request != _selectedRequestSequence
            || ComponentCancellationToken.IsCancellationRequested
            || row.PlayerCampaignAssignmentId != SelectedParticipantId)
        {
            return;
        }

        _selected = row;
        _selectedError = null;

        // A search belongs to the participant it was typed for, not to whoever is selected next.
        _teamChoicesSearch = string.Empty;
        _teamChoicesTruncated = false;
        ApplyDraftFromSelection();
        PersistQueue();

        if (row.ConcurrencyToken is null)
        {
            // A Closed immutable record carries no decision controls and therefore no team choices.
            _compatibleTeams = [];
            _teamChoicesError = null;
            return;
        }

        await LoadCompatibleTeamsAsync(row.GraduationYear);
    }

    /// <summary>
    /// Loads the bounded active teams whose graduation year matches the selected participant exactly.
    /// </summary>
    /// <param name="graduationYear">The selected participant's graduation year.</param>
    /// <returns>A task that completes when the choices are loaded.</returns>
    private async Task LoadCompatibleTeamsAsync(int graduationYear)
    {
        var request = ++_teamChoicesRequestSequence;

        // Clear the previous participant's choices before awaiting the new ones. The select renders
        // whenever the list is non-empty and the submit does not block on the loading flag, so a stale
        // list would offer teams filtered for a different graduation year.
        _compatibleTeams = [];
        _teamChoicesLoading = true;
        _teamChoicesError = null;

        var result = await ReadSafelyAsync(() => teamRosterService.GetRosterAsync(
            new GetTeamRosterInput
            {
                LifecycleStatus = "active",
                GraduationYear = graduationYear,
                Search = _teamChoicesSearch.Length > 0 ? _teamChoicesSearch : null,
                Limit = TeamChoiceLimit
            },
            ComponentCancellationToken));

        if (request != _teamChoicesRequestSequence || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        _teamChoicesLoading = false;
        var choices = result.Match<IReadOnlyList<TeamRosterItem>?>(teams => teams, _ => null);

        if (choices is null)
        {
            _teamChoicesError = "Compatible teams could not be loaded.";
            return;
        }

        _compatibleTeams = choices;

        // The read is capped, so the caller must say so when the cap was reached; a valid team beyond it is
        // reachable only through a narrower search.
        _teamChoicesTruncated = choices.Count >= TeamChoiceLimit;
    }

    /// <summary>
    /// Applies a narrower compatible-team search so a team beyond the documented cap stays reachable.
    /// </summary>
    /// <param name="args">The change event carrying the search text.</param>
    /// <returns>A task that completes when the narrowed choices are loaded.</returns>
    private async Task OnTeamSearchChangedAsync(ChangeEventArgs args)
    {
        _teamChoicesSearch = args.Value?.ToString()?.Trim() ?? string.Empty;
        if (_selected is { } selected && selected.ConcurrencyToken is not null)
        {
            await LoadCompatibleTeamsAsync(selected.GraduationYear);
        }
    }

    /// <summary>
    /// Reloads the selected participant's evidence on demand after a regional read failure.
    /// </summary>
    /// <returns>A task that completes when the reload finishes.</returns>
    private Task RetrySelectionAsync() => RefreshSelectionAsync(force: true);

    /// <summary>
    /// Reloads the compatible team choices on demand after a regional read failure.
    /// </summary>
    /// <returns>A task that completes when the reload finishes.</returns>
    private Task RetryCompatibleTeamsAsync() => _selected is null
        ? Task.CompletedTask
        : LoadCompatibleTeamsAsync(_selected.GraduationYear);

    /// <summary>
    /// Determines whether the selected participant's saved team is absent from the compatible choices.
    /// </summary>
    /// <param name="row">The selected participant's row.</param>
    /// <returns><see langword="true"/> when the saved team needs a disabled current-team option.</returns>
    private bool IsSavedTeamMissingFromChoices(CampaignPlaceQueueRow row)
        => row.LocalTeam is { } team && _compatibleTeams.All(choice => choice.TeamId != team.TeamId);

    /// <summary>
    /// Gets the compatible team choices to render, including a disabled current-team entry when the saved
    /// team is no longer active or compatible. An unavailable saved team is never substituted.
    /// </summary>
    private IReadOnlyList<CampaignPlaceTeamChoice> VisibleTeamChoices
    {
        get
        {
            var choices = _compatibleTeams
                .Select(team => new CampaignPlaceTeamChoice(team.TeamId, team.Name, Unavailable: false))
                .ToList();

            if (_selected is not null && IsSavedTeamMissingFromChoices(_selected) && _selected.LocalTeam is { } current)
            {
                // The saved team is shown so the current decision stays legible, but it is disabled: an
                // archived or incompatible team can never receive a new decision, and no substitute appears.
                choices.Insert(0, new CampaignPlaceTeamChoice(current.TeamId, current.TeamName, Unavailable: true));
            }

            return choices;
        }
    }
}

