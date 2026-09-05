using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Players;

/// <summary>Confirms the exact file and eligible rows of one server-issued preview.</summary>
public sealed record PlayerImportCommitInput : IValidatableObject
{
    /// <summary>Gets the original CSV and its upload metadata.</summary>
    [Required]
    public required PlayerImportUploadInput Upload { get; init; }

    /// <summary>Gets the UUIDv7 identity issued by preview.</summary>
    public required Guid OperationId { get; init; }

    /// <summary>Gets the opaque preview confirmation, retained unchanged for recovery.</summary>
    [Required, NotWhitespace, MaxLength(PlayerImportConstraints.MaxConfirmationTokenCharacters)]
    public required string ConfirmationToken { get; init; }

    /// <summary>Validates the server-issued operation identity.</summary>
    /// <param name="validationContext">The validation context.</param>
    /// <returns>Errors for an invalid operation identity.</returns>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (OperationId == Guid.Empty || OperationId.Version != 7)
        {
            yield return new ValidationResult("A preview operation ID is required.", [nameof(OperationId)]);
        }
    }
}
