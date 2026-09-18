using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Features.Players;
using Nova.UI.Components;
using Nova.UI.Features.Players.Services;

namespace Nova.UI.Features.Players.Components;

/// <summary>
/// The one inline archive confirmation for a player, shared by the directory and Player detail.
/// </summary>
public partial class PlayerLifecycleConfirmation : NovaComponentBase
{
    private bool _acknowledged;
    private long _acknowledgedSubject;

    /// <summary>Gets or sets the subject whose archive this confirmation reviews.</summary>
    [Parameter]
    public long PlayerId { get; set; }

    /// <summary>Gets or sets the archived candidate's display name.</summary>
    [Parameter, EditorRequired]
    public string DisplayName { get; set; } = string.Empty;

    /// <summary>Gets or sets the structured blockers that currently prevent archiving.</summary>
    [Parameter]
    public IReadOnlyList<PlayerArchiveBlocker> Blockers { get; set; } = [];

    /// <summary>Gets or sets whether a mutation is in flight.</summary>
    [Parameter]
    public bool IsBusy { get; set; }

    /// <summary>Gets or sets the callback invoked when the member confirms the archive.</summary>
    [Parameter]
    public EventCallback OnConfirm { get; set; }

    /// <summary>Gets or sets the callback invoked when the member cancels the confirmation.</summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        // A different subject is a different decision; an acknowledgement never carries across one.
        if (_acknowledgedSubject != PlayerId)
        {
            _acknowledged = false;
            _acknowledgedSubject = PlayerId;
        }
    }

    private Task ConfirmAsync() => _acknowledged ? OnConfirm.InvokeAsync() : Task.CompletedTask;
}
