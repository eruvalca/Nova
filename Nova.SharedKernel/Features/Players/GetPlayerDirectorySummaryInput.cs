using System.ComponentModel.DataAnnotations;

namespace Nova.SharedKernel.Features.Players;

/// <summary>Identifies the club whose unfiltered player directory summary is requested.</summary>
public sealed record GetPlayerDirectorySummaryInput
{
    /// <summary>Gets the route club, which must match the authenticated membership.</summary>
    [Range(1, long.MaxValue)]
    public required long ClubId { get; init; }
}
