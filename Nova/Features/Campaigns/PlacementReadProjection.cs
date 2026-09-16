using System.Linq.Expressions;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.Features.Campaigns;

/// <summary>Scalar projections keep decision evidence bounded and separate from enrollment metadata.</summary>
internal static class PlacementReadProjection
{
    internal static Expression<Func<PlayerCampaignAssignmentEntity, ClosedCampaignRosterItem>> ClosedRow(long clubId)
        => a => new ClosedCampaignRosterItem(a.PlayerCampaignAssignmentId, a.PlayerId,
            a.Player.FirstName, a.Player.LastName, a.Player.GraduationYear, a.TryoutNumber,
            new PlacementDecisionSource(new CampaignSavedPlacementDecision(
                a.PlayerCampaignAssignmentId, a.PlayerId, a.CampaignId, a.Campaign.SeasonId,
                a.Campaign.SeasonOpeningSequence!.Value, a.PlacementOutcome,
                a.Team != null && a.Team.ClubId == clubId ? a.TeamId : null,
                a.DecisionRecordedAt!.Value, a.DecisionRecordedById!.Value, a.DecisionActorDisplayName!, a.ConcurrencyToken),
                a.Campaign.Name,
                a.Team != null && a.Team.ClubId == clubId
                    ? new CampaignParticipantTeamSummaryDto(a.Team.TeamId, a.Team.Name) : null))
        {
            PlayerLifecycleStatus = a.Player.LifecycleStatus,
            TeamLifecycleStatus = a.Team != null && a.Team.ClubId == clubId ? a.Team.LifecycleStatus : null,
        };

    internal static Expression<Func<PlayerCampaignAssignmentEntity, CurrentSeasonRosterItem>> RosterRow()
        => a => new CurrentSeasonRosterItem(a.PlayerId, a.Player.FirstName, a.Player.LastName,
            a.Player.GraduationYear,
            new PlacementDecisionSource(new CampaignSavedPlacementDecision(
                a.PlayerCampaignAssignmentId, a.PlayerId, a.CampaignId, a.Campaign.SeasonId,
                a.Campaign.SeasonOpeningSequence!.Value, a.PlacementOutcome, a.TeamId,
                a.DecisionRecordedAt!.Value, a.DecisionRecordedById!.Value, a.DecisionActorDisplayName!, a.ConcurrencyToken),
                a.Campaign.Name, new CampaignParticipantTeamSummaryDto(a.Team!.TeamId, a.Team.Name)));

    internal static Expression<Func<PlacementWorkingState, CampaignEffectivePlacementItem>> WorkingRow(long clubId)
        => row => new CampaignEffectivePlacementItem(
            row.Participation.PlayerCampaignAssignmentId, row.Participation.PlayerId,
            row.Participation.Player.FirstName, row.Participation.Player.LastName,
            row.Participation.Player.GraduationYear, row.Participation.TryoutNumber,
            row.Participation.Player.LifecycleStatus, row.Participation.ConcurrencyToken,
            row.Participation.PlacementOutcome == PlacementOutcome.Undecided ? null : new CampaignSavedPlacementDecision(
                row.Participation.PlayerCampaignAssignmentId, row.Participation.PlayerId,
                row.Participation.CampaignId, row.Participation.Campaign.SeasonId,
                row.Participation.Campaign.SeasonOpeningSequence!.Value, row.Participation.PlacementOutcome,
                row.Participation.Team != null && row.Participation.Team.ClubId == clubId ? row.Participation.TeamId : null,
                row.Participation.DecisionRecordedAt!.Value, row.Participation.DecisionRecordedById!.Value,
                row.Participation.DecisionActorDisplayName!, row.Participation.ConcurrencyToken),
            row.Decision == null ? null : new PlacementDecisionSource(new CampaignSavedPlacementDecision(
                row.Decision.PlayerCampaignAssignmentId, row.Decision.PlayerId,
                row.Decision.CampaignId, row.Decision.Campaign.SeasonId,
                row.Decision.Campaign.SeasonOpeningSequence!.Value, row.Decision.PlacementOutcome,
                row.Decision.Team != null && row.Decision.Team.ClubId == clubId ? row.Decision.TeamId : null,
                row.Decision.DecisionRecordedAt!.Value, row.Decision.DecisionRecordedById!.Value,
                row.Decision.DecisionActorDisplayName!, row.Decision.ConcurrencyToken), row.Decision.Campaign.Name,
                row.Decision.Team != null && row.Decision.Team.ClubId == clubId
                    ? new CampaignParticipantTeamSummaryDto(row.Decision.Team.TeamId, row.Decision.Team.Name) : null),
            row.Decision != null && row.Decision.PlacementOutcome == PlacementOutcome.Assigned
                && row.CorrectionReason == PlacementCorrectionReason.None
                && row.Participation.Player.LifecycleStatus == LifecycleStatus.Active
                ? new CampaignParticipantTeamSummaryDto(row.Decision.Team!.TeamId, row.Decision.Team.Name) : null,
            row.Eligibility, row.CorrectionReason)
        {
            LocalTeam = row.Participation.Team != null && row.Participation.Team.ClubId == clubId
                ? new CampaignParticipantTeamSummaryDto(row.Participation.Team.TeamId, row.Participation.Team.Name) : null,
        };
}
