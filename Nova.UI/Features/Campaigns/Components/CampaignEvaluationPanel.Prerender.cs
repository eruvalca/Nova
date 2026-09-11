using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

public partial class CampaignEvaluationPanel
{
    /// <summary>The authority, campaign, player and lifecycle owning prerendered evidence.</summary>
    [PersistentState] public string? PersistedEvidenceScope { get; set; }
    /// <summary>The independently loaded identity.</summary>
    [PersistentState] public CampaignParticipantDetailDto? PersistedIdentity { get; set; }
    /// <summary>The first bounded note page.</summary>
    [PersistentState] public EvaluationHistoryPage<CampaignParticipantNoteDto>? PersistedNotes { get; set; }
    /// <summary>The first bounded application page.</summary>
    [PersistentState] public EvaluationHistoryPage<CampaignParticipantTagApplicationDto>? PersistedApplications { get; set; }
    /// <summary>The complete active choice catalog.</summary>
    [PersistentState] public IReadOnlyList<EvaluationTagChoice>? PersistedChoices { get; set; }
    /// <summary>The authority and query owning prerendered search results.</summary>
    [PersistentState] public string? PersistedFinderScope { get; set; }
    /// <summary>The bounded lookup results.</summary>
    [PersistentState] public IReadOnlyList<EvaluationFinderRow>? PersistedFinderRows { get; set; }
    /// <summary>The explicit match count.</summary>
    [PersistentState] public int? PersistedFinderCount { get; set; }

    private string EvidencePersistenceScope => $"{Owner}:{Status}";

    private bool RestoreEvidence()
    {
        if (!string.Equals(PersistedEvidenceScope, EvidencePersistenceScope, StringComparison.Ordinal))
        {
            PersistedEvidenceScope = EvidencePersistenceScope;
            PersistedIdentity = null;
            PersistedNotes = null;
            PersistedApplications = null;
            PersistedChoices = null;
            return false;
        }
        _identity = PersistedIdentity;
        _notes = PersistedNotes?.Items.ToList() ?? [];
        _notesNext = PersistedNotes?.Next;
        _applications = PersistedApplications?.Items.ToList() ?? [];
        _applicationsNext = PersistedApplications?.Next;
        _choices = PersistedChoices ?? [];
        return true;
    }
}
