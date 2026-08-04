using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Campaigns;

/// <summary>
/// Defines the optional filters and bound for the tenant-scoped campaign list.
/// </summary>
public sealed record GetCampaignListInput
{
    /// <summary>
    /// The default number of campaigns returned when no limit is supplied.
    /// </summary>
    public const int DefaultLimit = 50;

    /// <summary>
    /// The minimum number of campaigns accepted by one request.
    /// </summary>
    public const int MinLimit = 1;

    /// <summary>
    /// The maximum number of campaigns returned by one request.
    /// </summary>
    public const int MaxLimit = 100;

    /// <summary>
    /// Gets the optional status filter, accepting <c>active</c> or <c>closed</c>.
    /// </summary>
    [RegularExpression("(?i)^(active|closed)$")]
    public string? Status { get; init; }

    /// <summary>
    /// Gets the optional maximum number of campaign rows.
    /// </summary>
    [Range(MinLimit, MaxLimit)]
    public int? Limit { get; init; }
}
