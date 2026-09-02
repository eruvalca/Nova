using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Validation;

namespace Nova.UI.Features.Campaigns.Components;

/// <summary>
/// Renders the DataAnnotations-backed season metadata correction form.
/// </summary>
public partial class SeasonMetadataForm
{
    /// <summary>
    /// The local editable copy bound by this form.
    /// </summary>
    private SeasonMetadataFormState _localModel = SeasonMetadataFormState.CreateDefault();

    /// <summary>
    /// Tracks the last parent model reference copied into local state.
    /// </summary>
    private SeasonMetadataFormState? _lastModelReference;

    /// <summary>
    /// Gets or sets the heading displayed above the form.
    /// </summary>
    [Parameter]
    public string Heading { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mutable form state for the metadata correction.
    /// </summary>
    [Parameter, EditorRequired]
    public SeasonMetadataFormState Model { get; set; } = SeasonMetadataFormState.CreateDefault();

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
    public EventCallback<SeasonMetadataFormState> OnValidSubmit { get; set; }

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
/// Mutable season metadata form state that reuses shared input-record validation rules.
/// </summary>
public sealed class SeasonMetadataFormState : IValidatableObject
{
    /// <summary>
    /// Gets or sets the identifier of the season being corrected.
    /// </summary>
    public long SeasonId { get; set; }

    /// <summary>
    /// Gets or sets the season display name.
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the season start date.
    /// </summary>
    public DateOnly StartDate { get; set; }

    /// <summary>
    /// Gets or sets the optional season end date.
    /// </summary>
    public DateOnly? EndDate { get; set; }

    /// <summary>
    /// Gets or sets the concurrency token observed with the season metadata.
    /// </summary>
    public Guid ConcurrencyToken { get; set; }

    /// <summary>
    /// Creates a default form state.
    /// </summary>
    /// <returns>A default form state.</returns>
    public static SeasonMetadataFormState CreateDefault() => new();

    /// <summary>
    /// Creates a form state from a campaign list season group.
    /// </summary>
    /// <param name="season">The selected season group.</param>
    /// <returns>A form state initialized with the current metadata.</returns>
    public static SeasonMetadataFormState FromSeasonGroup(CampaignSeasonGroup season) => new()
    {
        SeasonId = season.SeasonId,
        Name = season.Name,
        StartDate = season.StartDate,
        EndDate = season.EndDate,
        ConcurrencyToken = season.ConcurrencyToken
    };

    /// <summary>
    /// Converts this state to an update-season-metadata input payload.
    /// </summary>
    /// <returns>An update-season-metadata input payload.</returns>
    public UpdateSeasonInput ToUpdateInput() => new()
    {
        ExpectedConcurrencyToken = ConcurrencyToken,
        Name = Name,
        StartDate = StartDate,
        EndDate = EndDate
    };

    /// <summary>
    /// Creates a deep copy of this form state.
    /// </summary>
    /// <returns>A copy of this state.</returns>
    public SeasonMetadataFormState Clone() => new()
    {
        SeasonId = SeasonId,
        Name = Name,
        StartDate = StartDate,
        EndDate = EndDate,
        ConcurrencyToken = ConcurrencyToken
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
