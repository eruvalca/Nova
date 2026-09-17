
using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Components;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Validation;

namespace Nova.UI.Features.Players.Components;

/// <summary>
/// Renders the shared DataAnnotations-backed create/edit player form.
/// </summary>
public partial class PlayerForm
{
    /// <summary>
    /// Gets or sets the heading displayed above the form.
    /// </summary>
    [Parameter]
    public string Heading { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the mutable form state for create/edit operations.
    /// </summary>
    [Parameter, EditorRequired]
    public PlayerFormState Model { get; set; } = PlayerFormState.CreateDefault();

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

    /// <summary>Freezes profile fields while an original creation awaits a definitive outcome.</summary>
    [Parameter]
    public bool IsReadOnly { get; set; }

    /// <summary>A server-confirmed possible duplicate that staff can inspect.</summary>
    [Parameter]
    public PlayerCreationDuplicate? Duplicate { get; set; }

    /// <summary>The parent-built duplicate detail destination, including the current roster return context.</summary>
    [Parameter]
    public Uri? DuplicateDetailUrl { get; set; }

    /// <summary>
    /// Gets or sets a server-side error message to display.
    /// </summary>
    [Parameter]
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets structured graduation-year blockers returned from the server.
    /// </summary>
    [Parameter]
    public IReadOnlyList<GraduationYearBlockerItem> GraduationYearBlockers { get; set; } = [];

    /// <summary>
    /// Gets or sets the callback invoked when the form validates and submits.
    /// </summary>
    [Parameter]
    public EventCallback OnValidSubmit { get; set; }

    /// <summary>
    /// Gets or sets the callback invoked when the user cancels editing.
    /// </summary>
    [Parameter]
    public EventCallback OnCancel { get; set; }

    /// <summary>
    /// Gets all gender options for the form select.
    /// </summary>
    protected static IReadOnlyList<Gender> GenderOptions { get; } = Enum.GetValues<Gender>();
}

/// <summary>
/// Mutable player form state that reuses shared input-record validation rules.
/// </summary>
public sealed class PlayerFormState : IValidatableObject
{
    /// <summary>
    /// Gets or sets whether this state represents edit mode.
    /// </summary>
    public bool IsEdit { get; set; }

    /// <summary>
    /// Gets or sets the player identifier for edit mode.
    /// </summary>
    public long PlayerId { get; set; }

    /// <summary>
    /// Gets or sets the player's first name.
    /// </summary>
    public string FirstName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the player's last name.
    /// </summary>
    public string LastName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the player's date of birth.
    /// </summary>
    public DateOnly DateOfBirth { get; set; } = DateOnly.FromDateTime(DateTime.UtcNow.AddYears(-10));

    /// <summary>
    /// Gets or sets the player's expected graduation year.
    /// </summary>
    public int GraduationYear { get; set; } = DateTime.UtcNow.Year + 8;

    /// <summary>
    /// Gets or sets the player's optional gender value.
    /// </summary>
    public Gender? Gender { get; set; }

    /// <summary>
    /// Gets or sets the player's optional jersey number.
    /// </summary>
    public int? JerseyNumber { get; set; }

    /// <summary>
    /// Creates a default create-mode form state.
    /// </summary>
    /// <returns>A default create-mode form state.</returns>
    public static PlayerFormState CreateDefault() => new();

    /// <summary>
    /// Creates an edit-mode form state from player detail.
    /// </summary>
    /// <param name="detail">The player detail payload.</param>
    /// <returns>An edit-mode form state.</returns>
    public static PlayerFormState FromDetail(PlayerDetailDto detail)
    {
        ArgumentNullException.ThrowIfNull(detail);
        return new()
        {
            IsEdit = true,
            PlayerId = detail.PlayerId,
            FirstName = detail.FirstName,
            LastName = detail.LastName,
            DateOfBirth = detail.DateOfBirth,
            GraduationYear = detail.GraduationYear,
            Gender = detail.Gender,
            JerseyNumber = detail.JerseyNumber
        };
    }

    /// <summary>
    /// Converts this form state to the shared profile-validation payload without operation metadata.
    /// </summary>
    /// <returns>The profile fields used to validate a new player before allocating its operation identity.</returns>
    public PlayerProfileInput ToProfileInput() => new()
    {
        FirstName = FirstName,
        LastName = LastName,
        DateOfBirth = DateOfBirth,
        GraduationYear = GraduationYear,
        Gender = Gender,
        JerseyNumber = JerseyNumber
    };

    /// <summary>Freezes one logical manual creation; validation never generates its operation identity.</summary>
    public CreatePlayerInput ToCreateInput(Guid operationId, long clubId) => new()
    {
        OperationId = operationId,
        ClubId = clubId,
        FirstName = FirstName,
        LastName = LastName,
        DateOfBirth = DateOfBirth,
        GraduationYear = GraduationYear,
        Gender = Gender,
        JerseyNumber = JerseyNumber
    };

    /// <summary>
    /// Converts this form state to an update-player input payload.
    /// </summary>
    /// <returns>An update-player input payload.</returns>
    public UpdatePlayerInput ToUpdateInput() => new()
    {
        PlayerId = PlayerId,
        FirstName = FirstName,
        LastName = LastName,
        DateOfBirth = DateOfBirth,
        GraduationYear = GraduationYear,
        Gender = Gender,
        JerseyNumber = JerseyNumber
    };

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var errors = IsEdit
            ? InputValidator.Validate(ToUpdateInput())
            : InputValidator.Validate(ToProfileInput());

        foreach (var (field, messages) in errors)
        {
            foreach (var message in messages)
            {
                yield return new ValidationResult(message, [field]);
            }
        }
    }
}
