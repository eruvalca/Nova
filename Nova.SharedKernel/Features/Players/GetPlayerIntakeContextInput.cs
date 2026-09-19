using System.ComponentModel.DataAnnotations;

namespace Nova.SharedKernel.Features.Players;

/// <summary>Identifies the club whose manual-intake enrollment consequence is requested.</summary>
public sealed record GetPlayerIntakeContextInput
{
    /// <summary>Gets the club whose Active campaign governs manual intake.</summary>
    [Range(1, long.MaxValue)]
    public required long ClubId { get; init; }
}
