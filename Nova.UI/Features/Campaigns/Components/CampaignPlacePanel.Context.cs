using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Results;

namespace Nova.UI.Features.Campaigns.Components;

public partial class CampaignPlacePanel
{
    private PlacementContextResult? _context;
    private string? _contextError;
    private bool _contextLoading;
    private int _contextRequest;
    private long? _contextParticipant;
    private long? _contextBeforeEventId;

    private async Task LoadOptionalContextAsync()
    {
        try
        {
            await LoadContextAsync();
            if (!ComponentCancellationToken.IsCancellationRequested) { StateHasChanged(); }
        }
        catch (OperationCanceledException) when (ComponentCancellationToken.IsCancellationRequested)
        {
            // The region was disposed while its independent read was pending.
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested)
        {
            await DispatchExceptionAsync(exception);
        }
    }

    private async Task LoadContextAsync(bool more = false)
    {
        if (_selected is not { } selected) { return; }
        var request = ++_contextRequest;
        var owner = EffectiveOwner;
        _contextLoading = true;
        _contextError = null;
        var cursor = more ? _context?.NextEventId : null;
        if (!more) { _context = null; }
        _contextParticipant = selected.PlayerCampaignAssignmentId;
        var result = await ReadSafelyAsync(() => contextQueries.GetContextAsync(new GetPlacementContextInput
        {
            CampaignId = CampaignId,
            PlayerCampaignAssignmentId = selected.PlayerCampaignAssignmentId,
            BeforeEventId = cursor
        }, ComponentCancellationToken));
        if (request != _contextRequest || !string.Equals(owner, EffectiveOwner, StringComparison.Ordinal)
            || _selected?.PlayerCampaignAssignmentId != selected.PlayerCampaignAssignmentId
            || ComponentCancellationToken.IsCancellationRequested) { return; }
        _contextLoading = false;
        result.Switch(value =>
        {
            _context = value;
            _contextBeforeEventId = cursor;
        }, _ => _contextError = "Placement history could not be loaded.");
    }

    private async Task KeepPreviousTeamAsync()
    {
        if (_context?.PreviousPlacement is not { CanKeep: true, Source.Team: { } team }
            || _selected is null || _selected.EffectiveDecision is not null || _selected.LocalDecision is not null
            || !CanRecordDecision || !_storageReady || _saving) { return; }
        _draftOutcome = PlacementOutcome.Assigned;
        _draftTeamId = team.TeamId;
        var input = new UpdateCampaignPlacementInput(_selected.PlayerCampaignAssignmentId,
            PlacementOutcome.Assigned, team.TeamId, _selected.ConcurrencyToken!.Value, Guid.CreateVersion7());
        _keepOperationId = string.Equals(Nova.UI.Features.Campaigns.Services.CampaignWorkspaceUrlState.ResolvePlacementEligibility(State),
            "NeedsPlacement", StringComparison.Ordinal) ? input.OperationId : null;
        await DispatchAsync(input);
    }

    private string TeamsCorrectionUrl => $"{ClubRoutes.Teams}?returnUrl={Uri.EscapeDataString(ComposePlaceUrl?.Invoke(SelectedParticipantId)
        ?? Nova.UI.Features.Campaigns.Services.CampaignWorkspaceUrlState.BuildPlaceWorkspaceUrl(CampaignId, State, placementParticipantId: SelectedParticipantId))}";
}
