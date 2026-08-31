using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Dashboard;

/// <summary>
/// Request input for the bounded, deterministically ordered club dashboard recent-activity query.
/// </summary>
public sealed record GetDashboardActivityInput
{
    /// <summary>The fixed number of events returned per page.</summary>
    public const int PageSize = 20;

    // Retained for source compatibility with older callers. New callers should omit this and
    // receive the fixed page size above.
    public const int MaxEventCount = 50;
    public const int DefaultLimit = MaxEventCount;

    [Range(1, MaxEventCount)]
    public int? Limit { get; init; }

    /// <summary>
    /// The opaque token identifying the last event from which the next older page should continue.
    /// </summary>
    [MaxLength(512)]
    public string? ContinuationToken { get; init; }
}
