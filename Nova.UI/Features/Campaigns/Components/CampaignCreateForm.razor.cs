using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Validation;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the DataAnnotations-backed campaign creation form with existing or inline season selection.
/// </summary>
public partial class CampaignCreateForm
{
    /// <summary>
    /// The local editable copy bound by this form.
    /// </summary>
    private CampaignCreateFormState _localModel = CampaignCreateFormState.CreateDefault();

    /// <summary>
    /// Tracks the last parent model reference copied into local state.
    /// </summary>
    private CampaignCreateFormState? _lastModelReference;

    /// <summary>
    /// Gets or sets the heading displayed above the form.
    /// </summary>
    [Parameter]
    public string Heading { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mutable form state for campaign creation.
    /// </summary>
    [Parameter, EditorRequired]
    public CampaignCreateFormState Model { get; set; } = CampaignCreateFormState.CreateDefault();

    /// <summary>
    /// Gets or sets the seasons available for existing-season selection.
    /// </summary>
    [Parameter]
    public IReadOnlyList<CampaignSeasonChoice> Seasons { get; set; } = [];

    /// <summary>
    /// Gets or sets whether the no-current-season workflow may offer inline season creation.
    /// </summary>
    [Parameter]
    public bool AllowInlineSeasonCreation { get; set; } = true;

    /// <summary>
    /// Gets or sets the submit button text.
    /// </summary>
    [Parameter]
    public string SubmitButtonText { get; set; } = "Create campaign";

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
    /// Gets or sets the callback invoked when the form validates and submits.
    /// </summary>
    [Parameter]
    public EventCallback<CampaignCreateFormState> OnValidSubmit { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user cancels creation.
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

        if (!AllowInlineSeasonCreation)
        {
            _localModel.UseInlineSeason = false;
        }
    }

    /// <summary>
    /// Submits a cloned local model to the parent callback.
    /// </summary>
    /// <returns>A task that completes when the parent callback finishes.</returns>
    private async Task HandleValidSubmit() => await OnValidSubmit.InvokeAsync(_localModel.Clone());

    /// <summary>
    /// Formats a season choice's date range for display in the season dropdown.
    /// </summary>
    /// <param name="season">The season choice.</param>
    /// <returns>The formatted date range.</returns>
    private static string FormatSeasonChoiceDates(CampaignSeasonChoice season)
        => season.EndDate is null
            ? $"starts {season.StartDate:MMM d, yyyy}"
            : $"{season.StartDate:MMM d, yyyy} – {season.EndDate.Value:MMM d, yyyy}";
}

/// <summary>
/// Mutable campaign creation form state that reuses shared input-record validation rules.
/// </summary>
public sealed class CampaignCreateFormState : IValidatableObject
{
    /// <summary>
    /// Gets or sets the caller-generated identifier making repeated submissions idempotent.
    /// The owning page assigns this once per form session and reuses it across retries.
    /// </summary>
    public Guid OperationId { get; set; }

    /// <summary>
    /// Gets or sets the campaign display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the campaign start date.
    /// </summary>
    public DateOnly StartDate { get; set; } = DateOnly.FromDateTime(DateTime.Today);

    /// <summary>
    /// Gets or sets the optional planned campaign end date.
    /// </summary>
    public DateOnly? PlannedEndDate { get; set; }

    /// <summary>
    /// Gets or sets whether the campaign creates a new season inline instead of using an existing one.
    /// </summary>
    public bool UseInlineSeason { get; set; }

    /// <summary>
    /// Gets or sets the selected existing season identifier.
    /// </summary>
    public long? ExistingSeasonId { get; set; }

    /// <summary>
    /// Gets or sets the inline season display name.
    /// </summary>
    public string InlineSeasonName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the inline season start date.
    /// </summary>
    public DateOnly InlineSeasonStartDate { get; set; }

    /// <summary>
    /// Gets or sets the optional inline season end date.
    /// </summary>
    public DateOnly? InlineSeasonEndDate { get; set; }

    /// <summary>
    /// Creates a default form state.
    /// </summary>
    /// <returns>A default form state.</returns>
    public static CampaignCreateFormState CreateDefault() => new();

    /// <summary>
    /// Converts this state to a create-campaign input payload.
    /// </summary>
    /// <returns>A create-campaign input payload.</returns>
    public CreateCampaignInput ToCreateInput() => new()
    {
        OperationId = OperationId,
        Name = Name,
        StartDate = StartDate,
        PlannedEndDate = PlannedEndDate,
        ExistingSeasonId = UseInlineSeason ? null : ExistingSeasonId,
        InlineSeason = UseInlineSeason
            ? new InlineSeasonInput
            {
                Name = InlineSeasonName,
                StartDate = InlineSeasonStartDate,
                EndDate = InlineSeasonEndDate
            }
            : null
    };

    /// <summary>
    /// Creates a deep copy of this form state.
    /// </summary>
    /// <returns>A copy of this state.</returns>
    public CampaignCreateFormState Clone() => new()
    {
        OperationId = OperationId,
        Name = Name,
        StartDate = StartDate,
        PlannedEndDate = PlannedEndDate,
        UseInlineSeason = UseInlineSeason,
        ExistingSeasonId = ExistingSeasonId,
        InlineSeasonName = InlineSeasonName,
        InlineSeasonStartDate = InlineSeasonStartDate,
        InlineSeasonEndDate = InlineSeasonEndDate
    };

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var errors = InputValidator.Validate(ToCreateInput());

        foreach (var (field, messages) in errors)
        {
            var targetField = MapInputFieldToFormField(field);
            foreach (var message in messages)
            {
                yield return new ValidationResult(message, [targetField]);
            }
        }
    }

    /// <summary>
    /// Maps shared input-record member names (including inline-season children) to form property names.
    /// </summary>
    /// <param name="inputField">The member name reported by the shared input validation.</param>
    /// <returns>The form property that displays the error.</returns>
    private static string MapInputFieldToFormField(string inputField) => inputField switch
    {
        nameof(CreateCampaignInput.InlineSeason) => nameof(InlineSeasonName),
        $"{nameof(CreateCampaignInput.InlineSeason)}.{nameof(InlineSeasonInput.Name)}" => nameof(InlineSeasonName),
        $"{nameof(CreateCampaignInput.InlineSeason)}.{nameof(InlineSeasonInput.StartDate)}" => nameof(InlineSeasonStartDate),
        $"{nameof(CreateCampaignInput.InlineSeason)}.{nameof(InlineSeasonInput.EndDate)}" => nameof(InlineSeasonEndDate),
        _ => inputField
    };
}
