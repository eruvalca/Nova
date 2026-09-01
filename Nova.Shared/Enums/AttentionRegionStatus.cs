namespace Nova.Shared.Enums;

/// <summary>
/// The availability state of one administrator attention region. A region reports
/// <see cref="Unavailable"/> when its underlying query fails; the other region remains independent.
/// </summary>
public enum AttentionRegionStatus
{
    /// <summary>
    /// The region's projection loaded successfully.
    /// </summary>
    Loaded = 0,

    /// <summary>
    /// The region's underlying query failed and its count is unknown.
    /// </summary>
    Unavailable = 1,
}
