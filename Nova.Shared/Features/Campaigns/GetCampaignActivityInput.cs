using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Request input for the bounded, deterministically ordered recent campaign activity query.
/// </summary>
public sealed record GetCampaignActivityInput
{
    /// <summary>
    /// The maximum number of activity events the server will return.
    /// </summary>
    public const int MaxEventCount = 50;

    /// <summary>
    /// The default number of activity events returned when no limit is supplied.
    /// </summary>
    public const int DefaultLimit = 50;

    /// <summary>
    /// The campaign identifier from the route.
    /// </summary>
    [Required]
    [Range(1, long.MaxValue)]
    public required long CampaignId { get; init; }

    /// <summary>
    /// The optional bound on returned activity events. Omission applies <see cref="DefaultLimit"/>,
    /// and validation rejects values above <see cref="MaxEventCount"/>.
    /// </summary>
    [Range(1, MaxEventCount)]
    public int? Limit { get; init; }
}
