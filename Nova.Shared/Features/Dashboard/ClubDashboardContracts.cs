using Nova.Shared.Enums;

namespace Nova.Shared.Features.Dashboard;

/// <summary>
/// The club dashboard summary response: active campaign cards, active/archived roster and team
/// counts, and the administrator-only attention counts (absent for non-administrators).
/// </summary>
public sealed record ClubDashboardResult
{
    /// <summary>
    /// The maximum number of active campaign cards returned by the dashboard summary.
    /// </summary>
    public const int ActiveCampaignMaxCount = 20;

    /// <summary>
    /// Gets the bounded active campaign cards, ordered newest-first by the campaign list surface.
    /// </summary>
    public required IReadOnlyList<ActiveCampaignCardDto> ActiveCampaigns { get; init; }

    /// <summary>
    /// Gets the active and archived player counts for the caller's club.
    /// </summary>
    public required RosterCountsDto Roster { get; init; }

    /// <summary>
    /// Gets the active and archived team counts for the caller's club.
    /// </summary>
    public required TeamCountsDto Teams { get; init; }

    /// <summary>
    /// Gets the administrator attention counts, or <see langword="null"/> for non-administrators.
    /// </summary>
    public AdminAttentionDto? AdminAttention { get; init; }
}

/// <summary>
/// One active campaign card on the club dashboard, carrying the campaign list surface projection and
/// a prebuilt workspace link.
/// </summary>
public sealed record ActiveCampaignCardDto
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
    /// Gets the campaign's season name.
    /// </summary>
    public required string SeasonName { get; init; }

    /// <summary>
    /// Gets the campaign start date.
    /// </summary>
    public required DateOnly StartDate { get; init; }

    /// <summary>
    /// Gets the optional planned campaign end date.
    /// </summary>
    public DateOnly? PlannedEndDate { get; init; }

    /// <summary>
    /// Gets the campaign lifecycle status.
    /// </summary>
    public required CampaignStatus Status { get; init; }

    /// <summary>
    /// Gets the number of persisted campaign participants.
    /// </summary>
    public required int ParticipantCount { get; init; }

    /// <summary>
    /// Gets the number of participants whose placement remains undecided, from the campaign list surface.
    /// </summary>
    public required int UnresolvedCount { get; init; }

    /// <summary>
    /// Gets the prebuilt relative workspace URL for the campaign.
    /// </summary>
    public required string WorkspaceUrl { get; init; }
}

/// <summary>
/// The active and archived player counts for the caller's club.
/// </summary>
public sealed record RosterCountsDto
{
    /// <summary>
    /// Gets the number of active players.
    /// </summary>
    public required int ActivePlayers { get; init; }

    /// <summary>
    /// Gets the number of archived players.
    /// </summary>
    public required int ArchivedPlayers { get; init; }
}

/// <summary>
/// The active and archived team counts for the caller's club.
/// </summary>
public sealed record TeamCountsDto
{
    /// <summary>
    /// Gets the number of active teams.
    /// </summary>
    public required int ActiveTeams { get; init; }

    /// <summary>
    /// Gets the number of archived teams.
    /// </summary>
    public required int ArchivedTeams { get; init; }
}

/// <summary>
/// The administrator-only attention counts: pending join requests and unresolved placements, plus the
/// first active campaign with an undecided participant for the review link target.
/// </summary>
public sealed record AdminAttentionDto
{
    /// <summary>
    /// Gets the number of pending join requests for the club.
    /// </summary>
    public required int PendingJoinRequestCount { get; init; }

    /// <summary>
    /// Gets the total number of unresolved placements across active campaigns, read directly from the
    /// tenant-filtered read context (authoritative across all active campaigns, independent of the card cap).
    /// </summary>
    public required int UnresolvedPlacementCount { get; init; }

    /// <summary>
    /// Gets the first active campaign (in card order) with an undecided participant, or
    /// <see langword="null"/> when no active campaign has unresolved placements.
    /// </summary>
    public long? FirstUnresolvedCampaignId { get; init; }
}
