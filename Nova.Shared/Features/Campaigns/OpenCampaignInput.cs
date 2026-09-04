using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Describes an idempotent request to open one Draft campaign.
/// </summary>
public sealed record OpenCampaignInput
{
    /// <summary>
    /// Gets the caller-generated identifier for the logical opening operation.
    /// </summary>
    [Required, NotEmptyGuid(ErrorMessage = "The operation identifier must not be empty.")]
    public required Guid OperationId { get; init; }
}
