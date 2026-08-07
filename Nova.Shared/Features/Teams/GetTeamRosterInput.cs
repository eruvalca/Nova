using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Teams;

/// <summary>
/// Defines the optional filters for the current club's team roster.
/// </summary>
public sealed record GetTeamRosterInput
{
    /// <summary>
    /// Gets the optional case-insensitive team-name search term.
    /// </summary>
    [MaxLength(200)]
    public string? Search { get; init; }

    /// <summary>
    /// Gets the optional lifecycle view, which accepts <c>active</c> or <c>archived</c>.
    /// </summary>
    [NotWhitespace, RegularExpression("(?i)^(active|archived)$")]
    public string? LifecycleStatus { get; init; }

    /// <summary>
    /// Gets the optional exact graduation-year filter.
    /// </summary>
    [Range(2000, 2100)]
    public int? GraduationYear { get; init; }
}
