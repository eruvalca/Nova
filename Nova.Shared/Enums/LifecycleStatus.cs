namespace Nova.Shared.Enums;

/// <summary>
/// Identifies whether a persistent club record is available for current workflows or retained only for history.
/// </summary>
public enum LifecycleStatus
{
    /// <summary>
    /// Indicates that the record is available for current workflows.
    /// </summary>
    Active = 0,

    /// <summary>
    /// Indicates that the record is retained for history but unavailable for new workflow associations.
    /// </summary>
    Archived = 1,
}
