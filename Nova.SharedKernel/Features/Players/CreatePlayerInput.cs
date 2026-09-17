using System.ComponentModel.DataAnnotations;

namespace Nova.SharedKernel.Features.Players;

/// <summary>One logical player creation. Retain the exact input and identity until its outcome is settled.</summary>
/// <remarks>Cancellation, denial, expiry and transport failures do not prove rollback. Recovery uses this same input.</remarks>
public sealed record CreatePlayerInput : PlayerProfileInput
{
    /// <summary>The UUIDv7 generated once before dispatch; executable and recoverable for 24 hours.</summary>
    [Required, CustomValidation(typeof(CreatePlayerInput), nameof(ValidateOperationId))]
    public required Guid OperationId { get; init; }

    /// <summary>The original club scope, compared with authenticated membership before any execution or disclosure.</summary>
    [Required, Range(1, long.MaxValue)]
    public required long ClubId { get; init; }

    /// <summary>Validates the immutable UUIDv7 shape; the service evaluates its window using the current clock.</summary>
    /// <param name="operationId">The identity supplied on the creation command.</param>
    /// <returns>A field error for an invalid UUIDv7 or unrepresentable recovery deadline.</returns>
    public static ValidationResult? ValidateOperationId(Guid operationId)
        => PlayerCreationOperation.TryGetCreatedAt(operationId, out _)
            ? ValidationResult.Success
            : new ValidationResult("Use a valid UUIDv7 player creation operation ID.", [nameof(OperationId)]);
}
