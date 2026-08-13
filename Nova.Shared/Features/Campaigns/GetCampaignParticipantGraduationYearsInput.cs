using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Request input for the distinct graduation-years choices available in a campaign roster.
/// </summary>
public sealed record GetCampaignParticipantGraduationYearsInput
{
    /// <summary>
    /// The maximum number of distinct graduation years the client accepts in a choices response.
    /// A roster spans a handful of class years, so this bound exists only for structural response
    /// validation on the client; the server never truncates the result.
    /// </summary>
    public const int MaxGraduationYears = 20;

    /// <summary>
    /// The campaign identifier from the route.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long CampaignId { get; init; }
}
