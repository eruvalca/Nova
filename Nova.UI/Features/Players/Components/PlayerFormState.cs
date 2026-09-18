using System.ComponentModel.DataAnnotations;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Validation;

namespace Nova.UI.Features.Players.Components;

/// <summary>
/// Mutable player-entry state that reuses the shared input-record validation rules, so the board
/// never invents a second policy beside <see cref="PlayerProfileInput"/>.
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
    /// Clears only the player-specific input, keeping the mode and identity of the current board.
    /// </summary>
    public void ResetForNextAddition()
    {
        FirstName = string.Empty;
        LastName = string.Empty;
        DateOfBirth = CreateDefault().DateOfBirth;
        GraduationYear = CreateDefault().GraduationYear;
        Gender = null;
        JerseyNumber = null;
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
    /// <param name="operationId">The retained UUIDv7 identity for this logical creation.</param>
    /// <param name="clubId">The original club scope for the command.</param>
    /// <returns>The exact command payload.</returns>
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

    /// <summary>Projects this state back onto the exact command payload it was frozen from.</summary>
    /// <param name="command">The retained command whose profile fields were corrected.</param>
    /// <returns>A command with the retained identity and the corrected profile fields.</returns>
    public CreatePlayerInput ToCorrectedCreateInput(CreatePlayerInput command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return command with
        {
            FirstName = FirstName,
            LastName = LastName,
            DateOfBirth = DateOfBirth,
            GraduationYear = GraduationYear,
            Gender = Gender,
            JerseyNumber = JerseyNumber
        };
    }

    /// <summary>Projects the retained command back into editable state so a member can correct it.</summary>
    /// <param name="command">The retained command.</param>
    /// <returns>A create-mode state holding the retained payload.</returns>
    public static PlayerFormState FromPendingCommand(CreatePlayerInput command)
    {
        ArgumentNullException.ThrowIfNull(command);
        return new()
        {
            FirstName = command.FirstName,
            LastName = command.LastName,
            DateOfBirth = command.DateOfBirth,
            GraduationYear = command.GraduationYear,
            Gender = command.Gender,
            JerseyNumber = command.JerseyNumber
        };
    }

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
