using System.Text.Json;
using Nova.Data;
using Nova.Entities;
using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;

namespace Nova.Features.Activity;

/// <summary>
/// The single boundary that appends durable activity events. Each <c>Append*</c> method validates
/// its facts through <see cref="ActivityEventPolicy"/>, stamps the stored visibility flag,
/// serializes the family-shaped payload, and adds the entity to the caller's open
/// <see cref="NovaDbContext"/> so the event commits atomically with the owning mutation. On
/// execution-strategy retries, callers re-run the whole mutation on a fresh context and the event
/// is naturally re-added.
/// </summary>
internal static class ActivityEventWriter
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    /// <summary>
    /// Appends a campaign lifecycle event.
    /// </summary>
    /// <param name="db">The caller's open context.</param>
    /// <param name="clubId">The club that owns the campaign.</param>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <param name="kind">The lifecycle event kind.</param>
    /// <param name="actorUserId">The acting user identifier.</param>
    /// <param name="actorDisplayName">The acting user display-name snapshot.</param>
    /// <param name="campaignName">The campaign display-name snapshot.</param>
    internal static void AppendCampaignLifecycle(
        NovaDbContext db,
        long clubId,
        long campaignId,
        ActivityEventKind kind,
        long actorUserId,
        string actorDisplayName,
        string campaignName)
    {
        if (ActivityEventPolicy.FamilyFor(kind) != ActivityEventFamily.CampaignLifecycle)
        {
            throw new ArgumentException($"Kind '{kind}' is not a campaign lifecycle event.", nameof(kind));
        }

        var context = new CampaignLifecycleContext
        {
            CampaignId = campaignId,
            CampaignName = campaignName,
        };
        Append(db, clubId, campaignId, kind, actorUserId, actorDisplayName, context);
    }

    /// <summary>
    /// Appends a placement event.
    /// </summary>
    /// <param name="db">The caller's open context.</param>
    /// <param name="clubId">The club that owns the campaign.</param>
    /// <param name="campaignId">The campaign identifier.</param>
    /// <param name="kind">The placement event kind.</param>
    /// <param name="actorUserId">The acting user identifier.</param>
    /// <param name="actorDisplayName">The acting user display-name snapshot.</param>
    /// <param name="context">The placement context payload.</param>
    internal static void AppendPlacement(
        NovaDbContext db,
        long clubId,
        long campaignId,
        ActivityEventKind kind,
        long actorUserId,
        string actorDisplayName,
        PlacementContext context)
    {
        if (ActivityEventPolicy.FamilyFor(kind) != ActivityEventFamily.Placement)
        {
            throw new ArgumentException($"Kind '{kind}' is not a placement event.", nameof(kind));
        }

        Append(db, clubId, campaignId, kind, actorUserId, actorDisplayName, context);
    }

    /// <summary>
    /// Appends a join request event.
    /// </summary>
    /// <param name="db">The caller's open context.</param>
    /// <param name="clubId">The club that owns the request.</param>
    /// <param name="kind">The join request event kind.</param>
    /// <param name="actorUserId">The acting user identifier.</param>
    /// <param name="actorDisplayName">The acting user display-name snapshot.</param>
    /// <param name="context">The join request context payload.</param>
    internal static void AppendJoinRequest(
        NovaDbContext db,
        long clubId,
        ActivityEventKind kind,
        long actorUserId,
        string actorDisplayName,
        JoinRequestContext context)
    {
        if (ActivityEventPolicy.FamilyFor(kind) != ActivityEventFamily.JoinRequest)
        {
            throw new ArgumentException($"Kind '{kind}' is not a join request event.", nameof(kind));
        }

        Append(db, clubId, campaignId: null, kind, actorUserId, actorDisplayName, context);
    }

    /// <summary>
    /// Appends a membership event.
    /// </summary>
    /// <param name="db">The caller's open context.</param>
    /// <param name="clubId">The club that owns the member.</param>
    /// <param name="kind">The membership event kind.</param>
    /// <param name="actorUserId">The acting user identifier.</param>
    /// <param name="actorDisplayName">The acting user display-name snapshot.</param>
    /// <param name="context">The membership context payload.</param>
    internal static void AppendMembership(
        NovaDbContext db,
        long clubId,
        ActivityEventKind kind,
        long actorUserId,
        string actorDisplayName,
        MembershipContext context)
    {
        if (ActivityEventPolicy.FamilyFor(kind) != ActivityEventFamily.Membership)
        {
            throw new ArgumentException($"Kind '{kind}' is not a membership event.", nameof(kind));
        }

        Append(db, clubId, campaignId: null, kind, actorUserId, actorDisplayName, context);
    }

    /// <summary>
    /// Appends a member role event.
    /// </summary>
    /// <param name="db">The caller's open context.</param>
    /// <param name="clubId">The club that owns the member.</param>
    /// <param name="kind">The member role event kind.</param>
    /// <param name="actorUserId">The acting user identifier.</param>
    /// <param name="actorDisplayName">The acting user display-name snapshot.</param>
    /// <param name="context">The member role context payload.</param>
    internal static void AppendMemberRole(
        NovaDbContext db,
        long clubId,
        ActivityEventKind kind,
        long actorUserId,
        string actorDisplayName,
        MemberRoleContext context)
    {
        if (ActivityEventPolicy.FamilyFor(kind) != ActivityEventFamily.MemberRole)
        {
            throw new ArgumentException($"Kind '{kind}' is not a member role event.", nameof(kind));
        }

        Append(db, clubId, campaignId: null, kind, actorUserId, actorDisplayName, context);
    }

    private static void Append(
        NovaDbContext db,
        long clubId,
        long? campaignId,
        ActivityEventKind kind,
        long actorUserId,
        string actorDisplayName,
        ClubActivityContext context)
    {
        // Serialize through the polymorphic base type so System.Text.Json emits the "type"
        // discriminator the feed's projection requires to read the row back.
        var payloadJson = JsonSerializer.Serialize(context, typeof(ClubActivityContext), JsonOptions);

        db.ActivityEvents.Add(new ActivityEventEntity
        {
            ClubId = clubId,
            CampaignId = campaignId,
            EventKind = kind,
            IsAdminOnly = ActivityEventPolicy.IsAdminOnly(kind),
            ActorUserId = actorUserId,
            ActorDisplayName = actorDisplayName,
            PayloadJson = payloadJson,
            CreatedById = actorUserId,
        });
    }
}
