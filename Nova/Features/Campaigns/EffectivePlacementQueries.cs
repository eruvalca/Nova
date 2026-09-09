using Nova.Data;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.Features.Campaigns;

/// <summary>Composable tenant-safe season selection. Validity filters must follow latest-decision selection.</summary>
internal static class EffectivePlacementQueries
{
    internal static IQueryable<PlayerCampaignAssignmentEntity> LatestDecisions(NovaReadDbContext db, long clubId)
    {
        var saved = db.PlayerCampaignAssignments.Where(a => a.ClubId == clubId
            && a.Player.ClubId == clubId && a.Campaign.ClubId == clubId
            && a.Campaign.Season.ClubId == clubId
            && a.Campaign.SeasonId == a.Campaign.Club.CurrentSeasonId
            && (a.Campaign.Status == CampaignStatus.Active || a.Campaign.Status == CampaignStatus.Closed)
            && a.Campaign.SeasonOpeningSequence != null
            && a.PlacementOutcome != PlacementOutcome.Undecided);

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
    internal static IQueryable<PlacementWorkingState> WorkingSet(NovaReadDbContext db, long clubId)
    {
        var participants = db.PlayerCampaignAssignments.Where(a => a.ClubId == clubId
            && a.Player.ClubId == clubId && a.Campaign.ClubId == clubId
            && a.Campaign.Season.ClubId == clubId
            && a.Campaign.Status == CampaignStatus.Active
            && a.Campaign.SeasonId == a.Campaign.Club.CurrentSeasonId);
        var latest = LatestDecisions(db, clubId);
        return from local in participants
               join decision in latest on local.PlayerId equals decision.PlayerId into decisions
               from decision in decisions.DefaultIfEmpty()
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
