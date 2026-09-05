using System.ComponentModel.DataAnnotations;

namespace Nova.Shared.Features.Campaigns;

/// <summary>
/// Identifies a condition that prevents a Draft campaign from opening.
/// </summary>
public enum CampaignOpeningBlocker
{
    /// <summary>
    /// The club has no active players to enroll.
    /// </summary>
    NoActivePlayers = 0,

    /// <summary>
    /// Another campaign in the club is already Active.
    /// </summary>
    AnotherCampaignActive = 1,
}

/// <summary>
/// Identifies a non-blocking condition surfaced during campaign opening.
/// </summary>
public enum CampaignOpeningWarning
{
    /// <summary>
    /// The club has no active teams, but evaluation work may still begin.
    /// </summary>
    NoActiveTeams = 0,
}

/// <summary>
/// Summarizes the Active campaign that currently blocks another Draft from opening.
/// </summary>
/// <param name="CampaignId">The blocking campaign identifier.</param>
/// <param name="CampaignName">The blocking campaign display name.</param>
public sealed record BlockingActiveCampaign(long CampaignId, string CampaignName);

/// <summary>
/// Reports the advisory, administrator-only opening readiness of one Draft campaign.
/// </summary>
/// <param name="CampaignId">The Draft campaign identifier.</param>
/// <param name="ActivePlayerCount">The current number of active club players.</param>
/// <param name="ActiveTeamCount">The current number of active club teams.</param>
/// <param name="CanOpen">Whether the current advisory snapshot has no blockers.</param>
/// <param name="Blockers">Every blocker present in the current snapshot.</param>
/// <param name="Warnings">Every non-blocking warning present in the current snapshot.</param>
/// <param name="BlockingCampaign">The other Active campaign, when one blocks opening.</param>
public sealed record CampaignOpeningReadinessResult(
    long CampaignId,
    int ActivePlayerCount,
    int ActiveTeamCount,
    bool CanOpen,
    IReadOnlyList<CampaignOpeningBlocker> Blockers,
    IReadOnlyList<CampaignOpeningWarning> Warnings,
    BlockingActiveCampaign? BlockingCampaign)
{
    /// <summary>Gets at most five active teams, ordered by name then identifier.</summary>
    public IReadOnlyList<CampaignOpeningTeam> ActiveTeams { get; init; } = [];
}

/// <summary>Identifies a durable team in a Draft preparation preview.</summary>
/// <param name="TeamId">The durable club team identifier.</param>
/// <param name="Name">The current team name.</param>
public sealed record CampaignOpeningTeam(long TeamId, string Name);

/// <summary>
/// Reports the immutable snapshot committed when a Draft campaign opened.
/// </summary>
/// <param name="OperationId">The caller-generated opening operation identifier.</param>
/// <param name="CampaignId">The opened campaign identifier.</param>
/// <param name="OpenedAt">When the campaign originally opened.</param>
/// <param name="OpenedByUserId">The administrator who originally opened the campaign.</param>
/// <param name="EnrolledPlayerCount">The exact active-player count enrolled at opening.</param>
/// <param name="ActiveTeamCount">The exact active-team count observed at opening.</param>
/// <param name="Warnings">The immutable non-blocking warning snapshot from opening.</param>
public sealed record OpenCampaignResult(
    Guid OperationId,
    long CampaignId,
    DateTimeOffset OpenedAt,
    long OpenedByUserId,
    int EnrolledPlayerCount,
    int ActiveTeamCount,
    IReadOnlyList<CampaignOpeningWarning> Warnings);

/// <summary>
/// Defines stable structured-error keys returned when fresh opening readiness blocks commitment.
/// </summary>
public static class CampaignOpeningProblemKeys
{
    /// <summary>
    /// Identifies the zero-active-player blocker.
    /// </summary>
    public const string NoActivePlayers = "blockers.noActivePlayers";

    /// <summary>
    /// Identifies the other-Active-campaign blocker.
    /// </summary>
    public const string AnotherCampaignActive = "blockers.anotherCampaignActive";

    /// <summary>
    /// Carries the identifier of the other Active campaign.
    /// </summary>
    public const string BlockingCampaignId = "blockingCampaign.id";

    /// <summary>
    /// Carries the display name of the other Active campaign.
    /// </summary>
    public const string BlockingCampaignName = "blockingCampaign.name";
}
