using System.Text.Json.Serialization;
using Nova.Shared.Enums;

namespace Nova.Shared.Features.Activity;

/// <summary>
/// The family-shaped, structured detail carried by one club activity row. The derived context
/// types are selected by the <see cref="ActivityEventKind"/> of the row so the client can render
/// family-specific copy without probing raw JSON.
/// </summary>
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(CampaignLifecycleContext), "campaignLifecycle")]
[JsonDerivedType(typeof(PlacementContext), "placement")]
[JsonDerivedType(typeof(JoinRequestContext), "joinRequest")]
[JsonDerivedType(typeof(MembershipContext), "membership")]
[JsonDerivedType(typeof(MemberRoleContext), "memberRole")]
public abstract record ClubActivityContext
{
}

/// <summary>
/// Context for campaign lifecycle (draft/open/close/reopen) activity rows.
/// </summary>
public sealed record CampaignLifecycleContext : ClubActivityContext
{
    /// <summary>
    /// Gets or sets the campaign identifier.
    /// </summary>
    public required long CampaignId { get; init; }

    /// <summary>
    /// Gets or sets the campaign display name.
    /// </summary>
    public required string CampaignName { get; init; }
}

/// <summary>
/// Context for placement activity rows (assignment, reassignment, withdrawal, outcome
/// replacement, and supersession).
/// </summary>
public sealed record PlacementContext : ClubActivityContext
{
    /// <summary>Gets the player identifier, independent of the owning campaign participation.</summary>
    public long? PlayerId { get; init; }

    /// <summary>Gets the season owning both sides of this transition.</summary>
    public long? SeasonId { get; init; }

    /// <summary>Gets the source campaign of the previous effective decision, if any.</summary>
    public long? PreviousCampaignId { get; init; }

    /// <summary>Gets the previous source campaign's display-name snapshot.</summary>
    public string? PreviousCampaignName { get; init; }

    /// <summary>Gets the source participation of the previous effective decision, if any.</summary>
    public long? PreviousPlayerCampaignAssignmentId { get; init; }

    /// <summary>Gets the prior team's stable identifier snapshot.</summary>
    public long? PreviousTeamId { get; init; }

    /// <summary>Gets the resulting team's stable identifier snapshot.</summary>
    public long? TeamId { get; init; }

    /// <summary>
    /// Gets or sets the campaign identifier.
    /// </summary>
    public required long CampaignId { get; init; }

    /// <summary>
    /// Gets or sets the campaign display name.
    /// </summary>
    public required string CampaignName { get; init; }

    /// <summary>
    /// Gets or sets the campaign assignment identifier.
    /// </summary>
    public required long PlayerCampaignAssignmentId { get; init; }

    /// <summary>
    /// Gets or sets the player display-name snapshot.
    /// </summary>
    public required string PlayerDisplayName { get; init; }

    /// <summary>
    /// Gets or sets the prior placement outcome, when the row records a change from a known outcome.
    /// </summary>
    public PlacementOutcome? PreviousOutcome { get; init; }

    /// <summary>
    /// Gets or sets the resulting placement outcome.
    /// </summary>
    public required PlacementOutcome Outcome { get; init; }

    /// <summary>
    /// Gets or sets the prior team display-name snapshot, when the row records a team change.
    /// </summary>
    public string? PreviousTeamName { get; init; }

    /// <summary>
    /// Gets or sets the resulting team display-name snapshot, when the row records a team change.
    /// </summary>
    public string? TeamName { get; init; }
}

/// <summary>
/// Context for join-request activity rows.
/// </summary>
public sealed record JoinRequestContext : ClubActivityContext
{
    /// <summary>
    /// Gets or sets the join request identifier.
    /// </summary>
    public required long JoinRequestId { get; init; }

    /// <summary>
    /// Gets or sets the requester display-name snapshot.
    /// </summary>
    public required string RequesterDisplayName { get; init; }
}

/// <summary>
/// Context for membership activity rows (a user joined, was removed, or left the club).
/// </summary>
public sealed record MembershipContext : ClubActivityContext
{
    /// <summary>
    /// Gets or sets the member user's stable identifier, so duplicate display names can be
    /// disambiguated and future member destinations can link to the subject.
    /// </summary>
    public required long MemberUserId { get; init; }

    /// <summary>
    /// Gets or sets the member display-name snapshot.
    /// </summary>
    public required string MemberDisplayName { get; init; }

    /// <summary>
    /// Gets or sets the name of the administrator who approved the membership, populated only for
    /// administrator viewers. Members see one shape ("Sam Doe joined the club"); administrators see
    /// the approval action ("Jordan Lee approved Sam Doe's membership").
    /// </summary>
    public string? ApprovedByActorName { get; init; }
}

/// <summary>
/// Context for member-role activity rows (a member was promoted or demoted).
/// </summary>
public sealed record MemberRoleContext : ClubActivityContext
{
    /// <summary>
    /// Gets or sets the member user's stable identifier.
    /// </summary>
    public required long MemberUserId { get; init; }

    /// <summary>
    /// Gets or sets the member display-name snapshot.
    /// </summary>
    public required string MemberDisplayName { get; init; }

    /// <summary>
    /// Gets or sets the resulting role.
    /// </summary>
    public required string Role { get; init; }
}
