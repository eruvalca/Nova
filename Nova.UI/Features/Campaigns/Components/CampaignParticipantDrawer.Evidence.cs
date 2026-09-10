using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

public partial class CampaignParticipantDrawer
{
    /// <summary>The first independently bounded note page from prerender.</summary>
    [PersistentState] public EvaluationHistoryPage<CampaignParticipantNoteDto>? PersistedNotes { get; set; }
    /// <summary>The first independently bounded application page from prerender.</summary>
    [PersistentState] public EvaluationHistoryPage<CampaignParticipantTagApplicationDto>? PersistedApplications { get; set; }
    private List<CampaignParticipantNoteDto> _notes = [];
    private List<CampaignParticipantTagApplicationDto> _applications = [];
    private EvaluationHistoryCursor? _notesNext;
    private EvaluationHistoryCursor? _applicationsNext;
    private string? _notesError;
    private string? _applicationsError;
    private int _notesSequence;
    private int _applicationsSequence;

    private void ResetEvidenceOwner()
    {
        ++_notesSequence;
        ++_applicationsSequence;
        _notes = [];
        _applications = [];
        _notesNext = _applicationsNext = null;
        _notesError = _applicationsError = null;
        PersistedNotes = null;
        PersistedApplications = null;
    }

    private GetEvaluationHistoryInput HistoryInput(EvaluationHistoryCursor? cursor) => new()
    {
        CampaignId = CampaignId,
        PlayerCampaignAssignmentId = ParticipantId,
        BeforeCreatedAt = cursor?.CreatedAt,
        BeforeId = cursor?.Id
    };

    private async Task LoadNotesAsync(bool append)
    {
        var owner = ContextOwner;
        var sequence = ++_notesSequence;
        var result = await evaluationQueryService.GetNotesAsync(HistoryInput(append ? _notesNext : null), ComponentCancellationToken);
        if (sequence != _notesSequence || !string.Equals(owner, ContextOwner, StringComparison.Ordinal) || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        result.Switch(page =>
        {
            if (!append)
            {
                PersistedNotes = page;
            }
            _notes = append ? [.. _notes, .. page.Items.Where(note => _notes.All(existing => existing.NoteId != note.NoteId))] : [.. page.Items];
            _notesNext = page.Next;
            _notesError = null;
        }, problem => _notesError = problem.Detail ?? "Notes could not be loaded.");
    }

    private async Task LoadApplicationsAsync(bool append)
    {
        var owner = ContextOwner;
        var sequence = ++_applicationsSequence;
        var result = await evaluationQueryService.GetApplicationsAsync(HistoryInput(append ? _applicationsNext : null), ComponentCancellationToken);
        if (sequence != _applicationsSequence || !string.Equals(owner, ContextOwner, StringComparison.Ordinal) || ComponentCancellationToken.IsCancellationRequested)
        {
            return;
        }

        result.Switch(page =>
        {
            if (!append)
            {
                PersistedApplications = page;
            }
            _applications = append ? [.. _applications, .. page.Items.Where(application => _applications.All(existing => existing.CampaignTagApplicationId != application.CampaignTagApplicationId))] : [.. page.Items];
            _applicationsNext = page.Next;
            _applicationsError = null;
        }, problem => _applicationsError = problem.Detail ?? "Applications could not be loaded.");
    }
}
