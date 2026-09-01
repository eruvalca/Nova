using Nova.Data;
using Nova.Entities;
using Nova.Shared.Enums;

namespace Nova.Features.ClubActivity;

/// <summary>Actor display evidence captured at the time of an activity mutation.</summary>
public sealed record ActivityActorEvidence(long UserId, string DisplayName);

/// <summary>Campaign evidence for a lifecycle transition.</summary>
public sealed record CampaignActivityEvidence(
    ClubActivityEventKind Kind,
    long ClubId,
    ActivityActorEvidence Actor,
    long CampaignId,
    string CampaignName,
    string? SeasonName);

/// <summary>Placement evidence for a transition in an Active campaign.</summary>
public sealed record PlacementActivityEvidence(
    long ClubId,
    ActivityActorEvidence Actor,
    long CampaignId,
    string CampaignName,
    long AssignmentId,
    long PlayerId,
    string PlayerDisplayName,
    PlacementActivityState Previous,
    PlacementActivityState Current);

/// <summary>Join-request evidence captured before a request is changed or deleted.</summary>
public sealed record JoinRequestActivityEvidence(
    ClubActivityEventKind Kind,
    long ClubId,
    ActivityActorEvidence Actor,
    long RequestId,
    long RequesterUserId,
    string RequesterDisplayName);

/// <summary>Membership or role evidence captured before membership changes.</summary>
public sealed record MembershipActivityEvidence(
    ClubActivityEventKind Kind,
    long ClubId,
    ActivityActorEvidence Actor,
    long SubjectUserId,
    string SubjectDisplayName);

/// <summary>
/// Adds immutable activity evidence to a caller-owned EF context. The caller remains responsible
/// for saving and committing the owning mutation and event atomically.
/// </summary>
public interface IClubActivityEventWriter
{
    /// <summary>Stages campaign lifecycle evidence.</summary>
    void AppendCampaign(ApplicationDbContext db, CampaignActivityEvidence evidence);
    /// <summary>Stages placement evidence when the state actually changed.</summary>
    bool AppendPlacement(ApplicationDbContext db, PlacementActivityEvidence evidence);
    /// <summary>Stages join-request evidence.</summary>
    void AppendJoinRequest(ApplicationDbContext db, JoinRequestActivityEvidence evidence);
    /// <summary>Stages membership or role evidence.</summary>
    void AppendMembership(ApplicationDbContext db, MembershipActivityEvidence evidence);
}

/// <summary>Default application implementation of the transaction-friendly activity writer.</summary>
public sealed class ClubActivityEventWriter : IClubActivityEventWriter
{
    /// <inheritdoc />
    public void AppendCampaign(ApplicationDbContext db, CampaignActivityEvidence evidence)
    {
        Add(db, new ClubActivityEventEntity
        {
            ClubId = evidence.ClubId,
            EventKind = evidence.Kind,
            Audience = ClubActivityEventPolicy.AudienceFor(evidence.Kind),
            CreatedById = evidence.Actor.UserId,
            ActorDisplayName = evidence.Actor.DisplayName,
            CampaignId = evidence.CampaignId,
            CampaignName = evidence.CampaignName,
            SeasonName = evidence.SeasonName
        });
    }

    /// <inheritdoc />
    public bool AppendPlacement(ApplicationDbContext db, PlacementActivityEvidence evidence)
    {
        var kind = ClubActivityEventPolicy.ClassifyPlacement(evidence.Previous, evidence.Current);
        if (kind is null)
        {
            return false;
        }

        Add(db, new ClubActivityEventEntity
        {
            ClubId = evidence.ClubId,
            EventKind = kind.Value,
            Audience = ClubActivityAudience.AllMembers,
            CreatedById = evidence.Actor.UserId,
            ActorDisplayName = evidence.Actor.DisplayName,
            CampaignId = evidence.CampaignId,
            CampaignName = evidence.CampaignName,
            PlayerCampaignAssignmentId = evidence.AssignmentId,
            PlayerId = evidence.PlayerId,
            PlayerDisplayName = evidence.PlayerDisplayName,
            PreviousPlacementOutcome = evidence.Previous.Outcome,
            PreviousTeamId = evidence.Previous.TeamId,
            PreviousTeamName = evidence.Previous.TeamName,
            PreviousSourceCampaignName = evidence.Previous.SourceCampaignName,
            CurrentPlacementOutcome = evidence.Current.Outcome,
            CurrentTeamId = evidence.Current.TeamId,
            CurrentTeamName = evidence.Current.TeamName,
            CurrentSourceCampaignName = evidence.Current.SourceCampaignName
        });
        return true;
    }

    /// <inheritdoc />
    public void AppendJoinRequest(ApplicationDbContext db, JoinRequestActivityEvidence evidence)
    {
        Add(db, new ClubActivityEventEntity
        {
            ClubId = evidence.ClubId,
            EventKind = evidence.Kind,
            Audience = ClubActivityEventPolicy.AudienceFor(evidence.Kind),
            CreatedById = evidence.Actor.UserId,
            ActorDisplayName = evidence.Actor.DisplayName,
            JoinRequestId = evidence.RequestId,
            SubjectUserId = evidence.RequesterUserId,
            SubjectDisplayName = evidence.RequesterDisplayName
        });
    }

    /// <inheritdoc />
    public void AppendMembership(ApplicationDbContext db, MembershipActivityEvidence evidence)
    {
        Add(db, new ClubActivityEventEntity
        {
            ClubId = evidence.ClubId,
            EventKind = evidence.Kind,
            Audience = ClubActivityEventPolicy.AudienceFor(evidence.Kind),
            CreatedById = evidence.Actor.UserId,
            ActorDisplayName = evidence.Actor.DisplayName,
            SubjectUserId = evidence.SubjectUserId,
            SubjectDisplayName = evidence.SubjectDisplayName
        });
    }

    private static void Add(ApplicationDbContext db, ClubActivityEventEntity entity)
        => db.ClubActivityEvents.Add(entity);
}
