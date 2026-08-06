using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Teams;

/// <summary>
/// Describes a new team to create in the current club.
/// </summary>
public sealed record CreateTeamInput
{
    /// <summary>
    /// Gets the team's display name.
    /// </summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the minimum player graduation year eligible for the team.
    /// </summary>
    [Required, Range(2000, 2100)]
    public required int GraduationYear { get; init; }
}
