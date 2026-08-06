using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Teams;
using Nova.Shared.Validation;

namespace Nova.UI.Features.Teams.Components;

/// <summary>
/// Renders the shared DataAnnotations-backed create/edit team form.
/// </summary>
public partial class TeamForm
{
    /// <summary>
    /// The local editable copy bound by this form.
    /// </summary>
    private TeamFormState _localModel = TeamFormState.CreateDefault();

    /// <summary>
    /// Tracks the last parent model reference copied into local state.
    /// </summary>
    private TeamFormState? _lastModelReference;

    /// <summary>
    /// Gets or sets the heading displayed above the form.
    /// </summary>
    [Parameter]
    public string Heading { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mutable form state for create/edit operations.
    /// </summary>
    [Parameter, EditorRequired]
    public TeamFormState Model { get; set; } = TeamFormState.CreateDefault();

    /// <summary>
    /// Gets or sets the submit button text.
    /// </summary>
    [Parameter]
    public string SubmitButtonText { get; set; } = "Save";

    /// <summary>
    /// Gets or sets whether a save operation is in progress.
    /// </summary>
    [Parameter]
    public bool IsSubmitting { get; set; }

    /// <summary>
    /// Gets or sets a server-side mutation error message.
    /// </summary>
    [Parameter]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets structured graduation-year cutoff blockers returned by the server.
    /// </summary>
    [Parameter]
    public IReadOnlyList<TeamGraduationYearBlockerItem> CutoffBlockers { get; set; } = [];

    /// <summary>
    /// Gets or sets the callback invoked when the form validates and submits.
    /// </summary>
    [Parameter]
    public EventCallback<TeamFormState> OnValidSubmit { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user cancels editing.
    /// </summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <inheritdoc />
    protected override void OnParametersSet()
    {
        if (!ReferenceEquals(_lastModelReference, Model))
        {
            _lastModelReference = Model;
            _localModel = Model.Clone();
        }
    }

    /// <summary>
    /// Submits a cloned local model to the parent callback.
    /// </summary>
    /// <returns>A task that completes when the parent callback finishes.</returns>
    private async Task HandleValidSubmit() => await OnValidSubmit.InvokeAsync(_localModel.Clone());
}

/// <summary>
/// Mutable team form state that reuses shared input-record validation rules.
/// </summary>
public sealed class TeamFormState : IValidatableObject
{
    /// <summary>
    /// Gets or sets whether this state represents edit mode.
    /// </summary>
    public bool IsEdit { get; set; }

    /// <summary>
    /// Gets or sets the team identifier in edit mode.
    /// </summary>
    public long TeamId { get; set; }

    /// <summary>
    /// Gets or sets the team's display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the team's graduation-year cutoff.
    /// </summary>
    public int GraduationYear { get; set; } = DateTime.UtcNow.Year + 8;

    /// <summary>
    /// Creates a default create-mode form state.
    /// </summary>
    /// <returns>A default create-mode form state.</returns>
    public static TeamFormState CreateDefault() => new();

    /// <summary>
    /// Creates an edit-mode form state from a roster item.
    /// </summary>
    /// <param name="team">The selected roster team.</param>
    /// <returns>An edit-mode form state.</returns>
    public static TeamFormState FromRosterItem(TeamRosterItem team) => new()
    {
        IsEdit = true,
        TeamId = team.TeamId,
        Name = team.Name,
        GraduationYear = team.GraduationYear
    };

    /// <summary>
    /// Creates an edit-mode form state from a loaded team detail payload.
    /// </summary>
    /// <param name="team">The loaded team detail.</param>
    /// <returns>An edit-mode form state.</returns>
    public static TeamFormState FromDetailDto(TeamDetailDto team) => new()
    {
        IsEdit = true,
        TeamId = team.TeamId,
        Name = team.Name,
        GraduationYear = team.GraduationYear
    };

    /// <summary>
    /// Converts this state to a create-team input payload.
    /// </summary>
    /// <returns>A create-team input payload.</returns>
    public CreateTeamInput ToCreateInput() => new()
    {
        Name = Name,
        GraduationYear = GraduationYear
    };

    /// <summary>
    /// Converts this state to an update-team input payload.
    /// </summary>
    /// <returns>An update-team input payload.</returns>
    public UpdateTeamInput ToUpdateInput() => new()
    {
        TeamId = TeamId,
        Name = Name,
        GraduationYear = GraduationYear
    };

    /// <summary>
    /// Creates a deep copy of this form state.
    /// </summary>
    /// <returns>A copy of this state.</returns>
    public TeamFormState Clone() => new()
    {
        IsEdit = IsEdit,
        TeamId = TeamId,
        Name = Name,
        GraduationYear = GraduationYear
    };

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var errors = IsEdit
            ? InputValidator.Validate(ToUpdateInput())
            : InputValidator.Validate(ToCreateInput());

        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                yield return new ValidationResult(message, [field]);
            }
        }
    }
}
