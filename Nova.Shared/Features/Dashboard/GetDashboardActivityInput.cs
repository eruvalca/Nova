using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Dashboard;

/// <summary>
/// Request input for the bounded, deterministically ordered club dashboard recent-activity query.
/// </summary>
public sealed record GetDashboardActivityInput
{
    /// <summary>
    /// The opaque token identifying the last event from which the next older page should continue.
    /// </summary>
    [MaxLength(512)]
    public string? ContinuationToken { get; init; }
}
