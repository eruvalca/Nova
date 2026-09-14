using System.ComponentModel.DataAnnotations;
using Nova.SharedKernel.Validation;

namespace Nova.SharedKernel.Features.Teams;

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

    /// <summary>
    /// Gets the optional inclusive maximum graduation year, for callers whose rule is a cutoff rather than a
    /// single cohort.
    /// </summary>
    /// <remarks>
    /// A team's graduation year is the earliest year it accepts: placement policy refuses a player whose year
    /// precedes the team's, so a player may be placed with any team at or below their own year. This is the
    /// range companion to the exact <see cref="GraduationYear"/> filter, which stays the default for
    /// team-management screens that ask for one cohort. Both filters combine when both are supplied.
    /// </remarks>
    [Range(2000, 2100)]
    public int? MaxGraduationYear { get; init; }

    /// <summary>
    /// Gets the optional maximum number of teams to return.
    /// </summary>
    /// <remarks>
    /// Omission keeps the existing unbounded behavior for the team management UI. Callers that
    /// render bounded team-choice selects (for example the campaign placements panel) must pass a
    /// documented cap and show a truncation notice when the returned count equals it.
    /// </remarks>
    [Range(1, 200)]
    public int? Limit { get; init; }
}
