using Nova.Shared.Enums;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Represents the minimal campaign-detail payload that feeds the workspace header.
/// </summary>
public sealed record CampaignDetailResult
{
    /// <summary>
    /// Gets the campaign identifier.
    /// </summary>
    public required long CampaignId { get; init; }

    /// <summary>
    /// Gets the campaign name.
    /// </summary>
    public required string Name { get; init; }

    /// <summary>
    /// Gets the campaign lifecycle status.
    /// </summary>
    public required CampaignStatus Status { get; init; }

    /// <summary>
    /// Gets the campaign start date.
    /// </summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>
    /// Gets the optional planned end date.
    /// </summary>
    public DateOnly? PlannedEndDate { get; init; }

    /// <summary>
    /// Gets the number of persisted campaign participants.
    /// </summary>
    public required int ParticipantCount { get; init; }

    /// <summary>
    /// Gets the season identifier.
    /// </summary>
    public required long SeasonId { get; init; }

    /// <summary>
    /// Gets the season name.
    /// </summary>
    public required string SeasonName { get; init; }

    /// <summary>
    /// Gets when the campaign was closed, or <see langword="null"/> while the campaign is active.
    /// </summary>
    public DateTimeOffset? ClosedAt { get; init; }

    /// <summary>
    /// Gets the identifier of the user who closed the campaign, or <see langword="null"/> while active.
    /// </summary>
    public long? ClosedByUserId { get; init; }

    /// <summary>
    /// Gets the resolved display name of the user who closed the campaign, or <see langword="null"/>
    /// while active.
    /// </summary>
    public string? ClosedByDisplayName { get; init; }
}
