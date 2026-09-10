using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;

namespace Nova.UI.Features.Campaigns.Components;

public partial class CampaignEvaluationPanel
{
    private List<CampaignParticipantNoteDto> _notes = [];
    private List<CampaignParticipantTagApplicationDto> _applications = [];
    private IReadOnlyList<EvaluationTagChoice> _choices = [];
    private EvaluationHistoryCursor? _notesNext;
    private EvaluationHistoryCursor? _applicationsNext;
    private string? _notesError;
    private string? _applicationsError;
    private string? _choicesError;
    private bool _notesLoading;
    private bool _applicationsLoading;
    private bool _choicesLoading;
    private int _notesSequence;
    private int _applicationsSequence;
    private int _choicesSequence;
    private string _traitSearch = string.Empty;
    private bool _traitPicker;
    private long? _deleteNoteId;
    private long? _removeApplicationId;

    private GetEvaluationHistoryInput HistoryInput(EvaluationHistoryCursor? cursor) => new()
    {
        CampaignId = CampaignId,
        PlayerCampaignAssignmentId = State.ParticipantId!.Value,
        BeforeCreatedAt = cursor?.CreatedAt,
        BeforeId = cursor?.Id
    };

    private async Task LoadNotesAsync(bool append)
    {
        if (State.ParticipantId is null)
        {
            return;
        }

        var owner = Owner;
        var sequence = ++_notesSequence;
        _notesLoading = true;
        try
        {
            var result = await evidence.GetNotesAsync(HistoryInput(append ? _notesNext : null), ComponentCancellationToken);
            if (!Owns(owner) || sequence != _notesSequence)
            {
                return;
            }

            result.Switch(page =>
            {
                _notes = append ? [.. _notes, .. page.Items.Where(note => _notes.All(existing => existing.NoteId != note.NoteId))] : [.. page.Items];
                if (!append) { PersistedNotes = page; }
                _notesNext = page.Next;
                _notesError = null;
            }, problem => _notesError = problem.Detail ?? "Shared notes could not be loaded.");
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested && exception is HttpRequestException or OperationCanceledException)
        {
            if (Owns(owner) && sequence == _notesSequence)
            {
                _notesError = "Shared notes could not be loaded. Retry notes.";
            }
        }
        finally
        {
            if (Owns(owner) && sequence == _notesSequence)
            {
                _notesLoading = false;
            }
        }
    }

    private async Task LoadApplicationsAsync(bool append)
    {
        if (State.ParticipantId is null)
        {
            return;
        }

        var owner = Owner;
        var sequence = ++_applicationsSequence;
        _applicationsLoading = true;
        try
        {
            var result = await evidence.GetApplicationsAsync(HistoryInput(append ? _applicationsNext : null), ComponentCancellationToken);
            if (!Owns(owner) || sequence != _applicationsSequence)
            {
                return;
            }

            result.Switch(page =>
            {
                _applications = append ? [.. _applications, .. page.Items.Where(application => _applications.All(existing => existing.CampaignTagApplicationId != application.CampaignTagApplicationId))] : [.. page.Items];
                if (!append) { PersistedApplications = page; }
                _applicationsNext = page.Next;
                _applicationsError = null;
            }, problem => _applicationsError = problem.Detail ?? "Applied traits could not be loaded.");
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested && exception is HttpRequestException or OperationCanceledException)
        {
            if (Owns(owner) && sequence == _applicationsSequence)
            {
                _applicationsError = "Applied traits could not be loaded. Retry traits.";
            }
        }
        finally
        {
            if (Owns(owner) && sequence == _applicationsSequence)
            {
                _applicationsLoading = false;
            }
        }
    }

    private async Task LoadChoicesAsync()
    {
        if (State.ParticipantId is not { } id)
        {
            return;
        }

        var owner = Owner;
        var sequence = ++_choicesSequence;
        _choicesLoading = true;
        try
        {
            var result = await evidence.GetTagChoicesAsync(new() { CampaignId = CampaignId, PlayerCampaignAssignmentId = id }, ComponentCancellationToken);
            if (!Owns(owner) || sequence != _choicesSequence)
            {
                return;
            }

            result.Switch(value => { _choices = value; PersistedChoices = value; _choicesError = null; }, problem => _choicesError = problem.Detail ?? "Trait choices could not be loaded.");
        }
        catch (Exception exception) when (!ComponentCancellationToken.IsCancellationRequested && exception is HttpRequestException or OperationCanceledException)
        {
            if (Owns(owner) && sequence == _choicesSequence)
            {
                _choicesError = "Trait choices could not be loaded. Retry choices.";
            }
        }
        finally
        {
            if (Owns(owner) && sequence == _choicesSequence)
            {
                _choicesLoading = false;
            }
        }
    }

    private IEnumerable<EvaluationTagChoice> MatchingChoices => _choices.Where(choice =>
        choice.Name.Contains(_traitSearch.Trim(), StringComparison.OrdinalIgnoreCase));
}
