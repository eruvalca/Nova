using Nova.SharedKernel.Features.Campaigns;

namespace Nova.Client.Services.Campaigns;

/// <summary>Validates shared immutable receipt fields before client state can claim success.</summary>
internal static class EvaluationReceiptValidation
{
    /// <summary>Checks operation binding, a real subject, and a bounded recovery deadline.</summary>
    /// <param name="receipt">The wire receipt, potentially incomplete.</param>
    /// <param name="operationId">The exact dispatched operation.</param>
    /// <returns>Whether the receipt is structurally valid and belongs to the request.</returns>
    public static bool IsValid(EvaluationMutationReceipt? receipt, Guid operationId) =>
        receipt is not null && operationId != Guid.Empty && receipt.OperationId == operationId
        && receipt.PlayerCampaignAssignmentId > 0 && receipt.CommittedAt > DateTimeOffset.UnixEpoch
        && receipt.RecoveryExpiresAt > receipt.CommittedAt
        && receipt.RecoveryExpiresAt - receipt.CommittedAt <= TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1);
}
