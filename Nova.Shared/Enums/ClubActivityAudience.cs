namespace Nova.Shared.Enums;

/// <summary>
/// Identifies the minimum club role allowed to see an activity event.
/// </summary>
public enum ClubActivityAudience
{
    /// <summary>Every approved club member may see the event.</summary>
    AllMembers,
    /// <summary>Only club administrators may see the event.</summary>
    Administrators,
}
