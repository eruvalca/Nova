namespace Nova.Shared.Features.Players;

/// <summary>Describes the final disposition of one reviewed CSV row.</summary>
public enum PlayerImportCommitRowStatus
{
    /// <summary>The reviewed player was created.</summary>
    Created,
    /// <summary>The row was invalid when reviewed and was not eligible for commitment.</summary>
    SkippedInvalidAtPreview,
    /// <summary>The row was a duplicate when reviewed and was not eligible for commitment.</summary>
    SkippedDuplicateAtPreview,
    /// <summary>The previously eligible row failed final validation.</summary>
    BlockedAtCommit
}

/// <summary>Records one immutable row outcome without retaining original CSV values.</summary>
/// <param name="SourceRowNumber">The logical source row, including the header.</param>
/// <param name="Status">The final row disposition.</param>
/// <param name="PlayerId">The created player, only for a created row.</param>
/// <param name="Errors">Field errors discovered during final validation.</param>
/// <param name="Duplicate">A duplicate discovered during final validation.</param>
public sealed record PlayerImportCommitRow(
    int SourceRowNumber,
    PlayerImportCommitRowStatus Status,
    long? PlayerId,
    IReadOnlyList<PlayerImportFieldError> Errors,
    PlayerImportDuplicate? Duplicate);

/// <summary>Contains the original, immutable reconciliation of a completed import.</summary>
/// <param name="OperationId">The confirmed preview operation.</param>
/// <param name="CompletedAt">The UTC completion timestamp.</param>
/// <param name="RecoveryExpiresAt">The exclusive end of exact-request recovery.</param>
/// <param name="TotalRows">The total number of source data rows.</param>
/// <param name="CreatedRows">The number of created players.</param>
/// <param name="SkippedInvalidRows">Rows excluded as invalid at preview.</param>
/// <param name="SkippedDuplicateRows">Rows excluded as duplicates at preview.</param>
/// <param name="BlockedRows">Previously eligible rows blocked at commit.</param>
/// <param name="EnrolledPlayers">Created players enrolled in the Active campaign.</param>
/// <param name="WaitingPlayers">Created players waiting for a future campaign opening.</param>
/// <param name="CampaignId">The Active campaign at commitment, if present.</param>
/// <param name="CampaignName">The original name of that campaign.</param>
/// <param name="Rows">One ordered result for every source row.</param>
public sealed record PlayerImportCompletion(
    Guid OperationId,
    DateTimeOffset CompletedAt,
    DateTimeOffset RecoveryExpiresAt,
    int TotalRows,
    int CreatedRows,
    int SkippedInvalidRows,
    int SkippedDuplicateRows,
    int BlockedRows,
    int EnrolledPlayers,
    int WaitingPlayers,
    long? CampaignId,
    string? CampaignName,
    IReadOnlyList<PlayerImportCommitRow> Rows);
