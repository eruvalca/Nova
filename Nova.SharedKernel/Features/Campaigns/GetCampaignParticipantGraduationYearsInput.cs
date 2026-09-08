using System.ComponentModel.DataAnnotations;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>
/// Request input for the distinct graduation-years choices available in a campaign roster.
/// </summary>
public sealed record GetCampaignParticipantGraduationYearsInput
{
    /// <summary>
    /// The campaign identifier from the route.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long CampaignId { get; init; }
}
