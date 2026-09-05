using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Validation;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the DataAnnotations-backed Active campaign metadata correction form.
/// </summary>
public partial class CampaignMetadataForm
{
    private EditContext _editContext = new(CampaignMetadataFormState.CreateDefault());
    private ValidationMessageStore? _serverMessages;
    private IReadOnlyDictionary<string, string[]>? _lastServerErrors;

    /// <summary>Gets or sets field-level validation from the metadata command.</summary>
    [Parameter] public IReadOnlyDictionary<string, string[]>? ServerErrors { get; set; }
    /// <summary>
    /// The local editable copy bound by this form.
    /// </summary>
    private CampaignMetadataFormState _localModel = CampaignMetadataFormState.CreateDefault();

    /// <summary>
    /// Tracks the last parent model reference copied into local state.
    /// </summary>
    private CampaignMetadataFormState? _lastModelReference;

    /// <summary>
    /// Gets or sets the heading displayed above the form.
    /// </summary>
    [Parameter]
    public string Heading { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mutable form state for the metadata correction.
    /// </summary>
    [Parameter, EditorRequired]
    public CampaignMetadataFormState Model { get; set; } = CampaignMetadataFormState.CreateDefault();

    /// <summary>
    /// Gets or sets the seasons available for reassignment.
    /// </summary>
    [Parameter]
    public IReadOnlyList<CampaignSeasonChoice> Seasons { get; set; } = [];

    /// <summary>
    /// Gets or sets the total number of tenant seasons before the choice bound. When greater than
    /// <see cref="Seasons"/>.Count, a truncation note is shown so administrators know older seasons
    /// are not selectable.
    /// </summary>
    [Parameter]
    public int TotalSeasonCount { get; set; }

    /// <summary>
    /// Gets or sets the submit button text.
    /// </summary>
    [Parameter]
    public string SubmitButtonText { get; set; } = "Save changes";

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
    public EventCallback<CampaignMetadataFormState> OnValidSubmit { get; set; }

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
            _editContext.OnFieldChanged -= ClearServerErrors;
            _lastModelReference = Model;
            _localModel = Model.Clone();
            _editContext = new EditContext(_localModel);
            _serverMessages = new ValidationMessageStore(_editContext);
            _editContext.OnFieldChanged += ClearServerErrors;
        }
        if (!ReferenceEquals(_lastServerErrors, ServerErrors))
        {
            _lastServerErrors = ServerErrors;
            _serverMessages?.Clear();
            if (ServerErrors is not null)
            {
                foreach (var error in ServerErrors)
                {
                    _serverMessages?.Add(new FieldIdentifier(_localModel, error.Key), error.Value);
                }
            }
            _editContext.NotifyValidationStateChanged();
        }
    }

    private void ClearServerErrors(object? sender, FieldChangedEventArgs args)
    {
        // Contextual failures can span fields (for example, the season and campaign dates).
        // The unchanged parent error snapshot must not reinstall them after an edit.
        _serverMessages?.Clear();
        _editContext.NotifyValidationStateChanged();
    }

    /// <inheritdoc />
    protected override async ValueTask DisposeAsyncCore()
    {
        _editContext.OnFieldChanged -= ClearServerErrors;
        await base.DisposeAsyncCore();
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
/// Mutable campaign metadata form state that reuses shared input-record validation rules.
/// </summary>
public sealed class CampaignMetadataFormState : IValidatableObject
{
    /// <summary>
    /// Gets or sets the identifier of the campaign being corrected.
    /// </summary>
    public long CampaignId { get; set; }

    /// <summary>
    /// Gets or sets the campaign display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the identifier of the season the campaign belongs to.
    /// </summary>
    public long SeasonId { get; set; }

    /// <summary>
    /// Gets or sets the campaign start date.
    /// </summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// Gets or sets the optional planned campaign end date.
    /// </summary>
    public DateOnly? PlannedEndDate { get; set; }

    /// <summary>
    /// Creates a default form state.
    /// </summary>
    /// <returns>A default form state.</returns>
    public static CampaignMetadataFormState CreateDefault() => new();

    /// <summary>
    /// Creates a form state from a campaign list row.
    /// </summary>
    /// <param name="campaign">The selected campaign row.</param>
    /// <param name="seasonId">The identifier of the season group containing the campaign.</param>
    /// <returns>A form state initialized with the current metadata.</returns>
    public static CampaignMetadataFormState FromListItem(CampaignListItem campaign, long seasonId) => new()
    {
        CampaignId = campaign.CampaignId,
        Name = campaign.Name,
        SeasonId = seasonId,
        StartDate = campaign.StartDate,
        PlannedEndDate = campaign.PlannedEndDate
    };

    /// <summary>
    /// Creates a form state from a loaded campaign detail payload.
    /// </summary>
    /// <param name="detail">The loaded campaign detail.</param>
    /// <returns>A form state initialized with the current metadata.</returns>
    public static CampaignMetadataFormState FromDetail(CampaignDetailResult detail) => new()
    {
        CampaignId = detail.CampaignId,
        Name = detail.Name,
        SeasonId = detail.SeasonId,
        StartDate = detail.StartDate,
        PlannedEndDate = detail.PlannedEndDate
    };

    /// <summary>
    /// Converts this state to an update-campaign-metadata input payload.
    /// </summary>
    /// <returns>An update-campaign-metadata input payload.</returns>
    public UpdateCampaignMetadataInput ToUpdateInput() => new()
    {
        CampaignId = CampaignId,
        Name = Name,
        SeasonId = SeasonId,
        StartDate = StartDate,
        PlannedEndDate = PlannedEndDate
    };

    /// <summary>
    /// Creates a deep copy of this form state.
    /// </summary>
    /// <returns>A copy of this state.</returns>
    public CampaignMetadataFormState Clone() => new()
    {
        CampaignId = CampaignId,
        Name = Name,
        SeasonId = SeasonId,
        StartDate = StartDate,
        PlannedEndDate = PlannedEndDate
    };

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var errors = InputValidator.Validate(ToUpdateInput());

        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                yield return new ValidationResult(message, [field]);
            }
        }
    }
}
