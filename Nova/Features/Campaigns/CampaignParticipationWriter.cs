using Nova.Data;
using Nova.Entities;
using Nova.Shared.Enums;

namespace Nova.Features.Campaigns;

/// <summary>
/// Stages technical campaign participation rows without manufacturing placement decisions or activity.
/// </summary>
internal static class CampaignParticipationWriter
{
    /// <summary>
    /// Stages one participation for each distinct player identifier.
    /// </summary>
    /// <param name="db">The caller's open transaction context.</param>
    /// <param name="clubId">The club that owns the campaign and players.</param>
    /// <param name="campaignId">The campaign receiving the participants.</param>
    /// <param name="playerIds">The player identifiers to enroll.</param>
    internal static void StageEnrollments(
        NovaDbContext db,
        long clubId,
        long campaignId,
        IEnumerable<long> playerIds)
    {
        foreach (var playerId in playerIds.Distinct())
        {
            db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
            {
                PlayerId = playerId,
                CampaignId = campaignId,
                ClubId = clubId,
                PlacementOutcome = PlacementOutcome.Undecided,
                CreatedById = default,
            });
        }
    }
}
