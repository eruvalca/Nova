using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.Features.Campaigns;

/// <summary>Composable tenant-safe season selection. Validity filters must follow latest-decision selection.</summary>
internal static class EffectivePlacementQueries
{
    private static IQueryable<PlayerCampaignAssignmentEntity> SavedDecisions(NovaReadDbContext db, long clubId)
        => db.PlayerCampaignAssignments.Where(a => a.ClubId == clubId
            && a.Player.ClubId == clubId && a.Campaign.ClubId == clubId
            && a.Campaign.Season.ClubId == clubId
            && a.Campaign.SeasonId == a.Campaign.Club.CurrentSeasonId
            && (a.Campaign.Status == CampaignStatus.Active || a.Campaign.Status == CampaignStatus.Closed)
            && a.Campaign.SeasonOpeningSequence != null
            && a.PlacementOutcome != PlacementOutcome.Undecided);

    internal static IQueryable<PlayerCampaignAssignmentEntity> LatestDecisions(NovaReadDbContext db, long clubId)
    {
        var saved = SavedDecisions(db, clubId);
        return saved.Where(a => !saved.Any(newer => newer.PlayerId == a.PlayerId
            && newer.Campaign.SeasonId == a.Campaign.SeasonId
            && (newer.Campaign.SeasonOpeningSequence > a.Campaign.SeasonOpeningSequence
                || newer.Campaign.SeasonOpeningSequence == a.Campaign.SeasonOpeningSequence
                    && newer.PlayerCampaignAssignmentId > a.PlayerCampaignAssignmentId)));
    }

    internal static IQueryable<PlayerCampaignAssignmentEntity> Roster(NovaReadDbContext db, long clubId)
        => LatestDecisions(db, clubId).Where(a => a.Player.LifecycleStatus == LifecycleStatus.Active
            && a.PlacementOutcome == PlacementOutcome.Assigned
            && a.Team != null && a.Team.ClubId == clubId
            && a.Team.LifecycleStatus == LifecycleStatus.Active
            && a.Player.GraduationYear >= a.Team.GraduationYear);

    /// <summary>SQL counterpart of CampaignPlacementPolicy.GetEligibility; direct matrix tests enforce parity.</summary>
    internal static IQueryable<PlacementWorkingState> WorkingSet(NovaReadDbContext db, long clubId,
        IQueryable<PlayerCampaignAssignmentEntity>? participantFilter = null)
    {
        var participants = (participantFilter ?? db.PlayerCampaignAssignments).Where(a => a.ClubId == clubId
            && a.Player.ClubId == clubId && a.Campaign.ClubId == clubId
            && a.Campaign.Season.ClubId == clubId
            && a.Campaign.Status == CampaignStatus.Active
            && a.Campaign.SeasonId == a.Campaign.Club.CurrentSeasonId);
        var saved = SavedDecisions(db, clubId);
        // PostgreSQL can select the latest visible decision directly through a lateral join.
        // SQLite needs the nullable scalar key shape because it does not support APPLY.
        var pairs = db.Database.IsNpgsql()
            ? from local in participants
              from decision in saved.Where(candidate => candidate.PlayerId == local.PlayerId)
                  .OrderByDescending(candidate => candidate.Campaign.SeasonOpeningSequence)
                  .ThenByDescending(candidate => candidate.PlayerCampaignAssignmentId).Take(1).DefaultIfEmpty()
              select new PlacementWorkingState { Participation = local, Decision = decision }
            : from local in participants
              let latestId = saved.Where(candidate => candidate.PlayerId == local.PlayerId)
                  .OrderByDescending(candidate => candidate.Campaign.SeasonOpeningSequence)
                  .ThenByDescending(candidate => candidate.PlayerCampaignAssignmentId)
                  .Select(candidate => (long?)candidate.PlayerCampaignAssignmentId).FirstOrDefault()
              join decision in saved on latestId equals (long?)decision.PlayerCampaignAssignmentId into decisions
              from decision in decisions.DefaultIfEmpty()
              select new PlacementWorkingState { Participation = local, Decision = decision };
        return from pair in pairs
               let local = pair.Participation
               let decision = pair.Decision
               let validTeam = decision != null && decision.PlacementOutcome == PlacementOutcome.Assigned
                   && decision.Team != null && decision.Team.ClubId == clubId
                   && decision.Team.LifecycleStatus == LifecycleStatus.Active
                   && local.Player.GraduationYear >= decision.Team.GraduationYear
               select new PlacementWorkingState
               {
                   Participation = local,
                   Decision = decision,
                   Eligibility = local.Player.LifecycleStatus != LifecycleStatus.Active
                       || decision != null && decision.PlacementOutcome == PlacementOutcome.Withdrawn
                       ? EffectivePlacementEligibility.Unavailable
                       : validTeam ? EffectivePlacementEligibility.OptionalReassignment
                       : decision != null && decision.PlacementOutcome == PlacementOutcome.NotSelected
                           && decision.CampaignId == local.CampaignId
                           ? EffectivePlacementEligibility.Resolved : EffectivePlacementEligibility.NeedsPlacement,
                   CorrectionReason = decision == null || decision.PlacementOutcome != PlacementOutcome.Assigned
                       ? PlacementCorrectionReason.None
                       : decision.Team == null || decision.Team.ClubId != clubId ? PlacementCorrectionReason.TeamUnavailable
                       : decision.Team.LifecycleStatus != LifecycleStatus.Active ? PlacementCorrectionReason.TeamArchived
                       : local.Player.GraduationYear < decision.Team.GraduationYear ? PlacementCorrectionReason.TeamIncompatible
                       : PlacementCorrectionReason.None,
               };
    }

    internal static IQueryable<PlayerCampaignAssignmentEntity> NeedsPlacement(NovaReadDbContext db, long clubId)
        => WorkingSet(db, clubId).Where(row => row.Eligibility == EffectivePlacementEligibility.NeedsPlacement)
            .Select(row => row.Participation);
}

/// <summary>Relational intermediate used only before bounded scalar projection; never materialized as an entity graph.</summary>
internal sealed class PlacementWorkingState
{
    public required PlayerCampaignAssignmentEntity Participation { get; init; }
    public PlayerCampaignAssignmentEntity? Decision { get; init; }
    public EffectivePlacementEligibility Eligibility { get; init; }
    public PlacementCorrectionReason CorrectionReason { get; init; }
}
