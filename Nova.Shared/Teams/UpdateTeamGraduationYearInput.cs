using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Teams;

/// <summary>
/// Describes an administrator request to change a team's graduation-year eligibility cutoff.
/// </summary>
public sealed record UpdateTeamGraduationYearInput
{
    /// <summary>
    /// Gets the identifier of the team to update.
    /// </summary>
    [Required, Range(1, long.MaxValue)]
    public required long TeamId { get; init; }

    /// <summary>
    /// Gets the proposed minimum eligible player graduation year.
    /// </summary>
    [Required, Range(2000, 2100)]
    public required int GraduationYear { get; init; }
}
