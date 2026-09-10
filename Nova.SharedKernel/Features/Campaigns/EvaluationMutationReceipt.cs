namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>Immutable proof of the original evaluation mutation, independent of later evidence changes.</summary>
/// <param name="OperationId">The client-retained operation identity.</param>
/// <param name="PlayerCampaignAssignmentId">The verified participant affected.</param>
/// <param name="CommittedAt">The original completion time.</param>
/// <param name="RecoveryExpiresAt">The exclusive recovery deadline.</param>
public sealed record EvaluationMutationReceipt(
    Guid OperationId,
    long PlayerCampaignAssignmentId,
    DateTimeOffset CommittedAt,
    DateTimeOffset RecoveryExpiresAt);
