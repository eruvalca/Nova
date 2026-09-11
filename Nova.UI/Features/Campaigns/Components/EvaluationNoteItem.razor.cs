using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>One attributed shared note with author-only, version-aware inline actions.</summary>
public partial class EvaluationNoteItem
{
    /// <summary>The authoritative note version and capabilities.</summary>
    [Parameter, EditorRequired] public CampaignParticipantNoteDto Note { get; set; } = null!;
    /// <summary>Whether current campaign authority permits writes.</summary>
    [Parameter] public bool Writable { get; set; }
    /// <summary>Whether an operation outcome is unresolved.</summary>
    [Parameter] public bool Pending { get; set; }
    /// <summary>Whether the note editor is open.</summary>
    [Parameter] public bool Editing { get; set; }
    /// <summary>The separately retained edit draft.</summary>
    [Parameter] public string Draft { get; set; } = string.Empty;
    /// <summary>The version originally reviewed for the edit.</summary>
    [Parameter] public Guid ExpectedVersion { get; set; }
    /// <summary>Whether inline deletion confirmation is open.</summary>
    [Parameter] public bool ConfirmingDelete { get; set; }
    /// <summary>Starts editing.</summary>
    [Parameter] public EventCallback OnEdit { get; set; }
    /// <summary>Retains a changed draft.</summary>
    [Parameter] public EventCallback<ChangeEventArgs> OnDraftChanged { get; set; }
    /// <summary>Saves the explicit edit.</summary>
    [Parameter] public EventCallback OnSave { get; set; }
    /// <summary>Cancels the inline editor.</summary>
    [Parameter] public EventCallback OnCancel { get; set; }
    /// <summary>Opens deletion confirmation.</summary>
    [Parameter] public EventCallback OnDelete { get; set; }
    /// <summary>Keeps the shared note.</summary>
    [Parameter] public EventCallback OnCancelDelete { get; set; }
    /// <summary>Deletes the reviewed version.</summary>
    [Parameter] public EventCallback OnConfirmDelete { get; set; }
    /// <summary>Acknowledges the refreshed version while retaining draft content.</summary>
    [Parameter] public EventCallback OnReviewVersion { get; set; }
}
