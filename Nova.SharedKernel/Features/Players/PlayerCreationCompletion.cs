namespace Nova.SharedKernel.Features.Players;

/// <summary>Immutable evidence of the original creation, independent of subsequent profile and campaign changes.</summary>
public sealed record PlayerCreationCompletion
{
    /// <summary>The client's original logical operation.</summary>
    public required Guid OperationId { get; init; }
    /// <summary>The player profile as it was created, not a current-state read.</summary>
    public required PlayerDto Player { get; init; }
    /// <summary>The UTC completion time.</summary>
    public required DateTimeOffset CompletedAt { get; init; }
    /// <summary>The exclusive deadline for replaying this operation.</summary>
    public required DateTimeOffset RecoveryExpiresAt { get; init; }
    /// <summary>Original enrollment evidence; null means there was no Active campaign at commitment.</summary>
    public required PlayerCreationEnrollment? Enrollment { get; init; }
}

/// <summary>The technical participation created atomically with a player.</summary>
public sealed record PlayerCreationEnrollment
{
    /// <summary>The campaign that was Active at commitment.</summary>
    public required long CampaignId { get; init; }
    /// <summary>The campaign name at commitment.</summary>
    public required string CampaignName { get; init; }
    /// <summary>The created participation, without a saved placement decision.</summary>
    public required long PlayerCampaignAssignmentId { get; init; }
}
