using System.ComponentModel.DataAnnotations;
using Nova.SharedKernel.Validation;

namespace Nova.SharedKernel.Features.Players;

/// <summary>One logical player creation. Retain the exact input and identity until its outcome is settled.</summary>
/// <remarks>Cancellation, denial, expiry and transport failures do not prove rollback. Recovery uses this same input.</remarks>
public sealed record CreatePlayerInput : PlayerProfileInput
{
    /// <summary>The UUIDv7 generated once before dispatch; executable and recoverable for 24 hours.</summary>
    [Required, NotEmptyGuid]
    public required Guid OperationId { get; init; }

    /// <summary>The original club scope, compared with authenticated membership before any execution or disclosure.</summary>
    [Required, Range(1, long.MaxValue)]
    public required long ClubId { get; init; }
}
