namespace Nova.Shared.Enums;

/// <summary>
/// Defines the moderator actions that can be applied to a join request.
/// </summary>
public enum RequestAction
{
    /// <summary>
    /// Rejects the pending request.
    /// </summary>
    Reject,

    /// <summary>
    /// Approves the pending request.
    /// </summary>
    Approve
}
