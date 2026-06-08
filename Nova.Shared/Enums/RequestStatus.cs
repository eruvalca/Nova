namespace Nova.Shared.Enums;

/// <summary>
/// Represents the lifecycle state of a club join request.
/// </summary>
public enum RequestStatus
{
    /// <summary>
    /// The request has been submitted and is awaiting review.
    /// </summary>
    Pending,

    /// <summary>
    /// The request has been approved.
    /// </summary>
    Approved,

    /// <summary>
    /// The request has been denied.
    /// </summary>
    Rejected
}
