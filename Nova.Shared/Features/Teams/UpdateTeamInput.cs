using System.ComponentModel.DataAnnotations;
using Nova.Shared.Validation;

namespace Nova.Shared.Features.Teams;

/// <summary>
/// Describes permanent profile changes for an active team.
/// </summary>
public sealed record UpdateTeamInput
{
    /// <summary>
    /// Gets the identifier of the team to update.
    /// </summary>
    [Required, Range(1, long.MaxValue)]
    public required long TeamId { get; init; }

    /// <summary>
    /// Gets the team's replacement display name.
    /// </summary>
    [Required, NotWhitespace, MaxLength(100)]
    public required string Name { get; init; }

    /// <summary>
    /// Gets the replacement minimum eligible player graduation year.
    /// </summary>
    [Required, Range(2000, 2100)]
    public required int GraduationYear { get; init; }
}
