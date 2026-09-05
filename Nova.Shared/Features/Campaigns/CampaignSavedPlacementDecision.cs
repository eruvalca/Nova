using System.Text.Json.Serialization;
using Nova.Shared.Enums;

namespace Nova.Shared.Features.Campaigns;

/// <summary>A saved campaign decision, distinct from technical enrollment and from effective-roster projections.</summary>
/// <param name="PlayerCampaignAssignmentId">The source participation identifier.</param>
/// <param name="PlayerId">The player identifier.</param>
/// <param name="CampaignId">The source campaign identifier.</param>
/// <param name="SeasonId">The source season identifier.</param>
/// <param name="SeasonOpeningSequence">The source campaign's authoritative opening order.</param>
/// <param name="Outcome">The explicit outcome; never Undecided.</param>
/// <param name="TeamId">The assigned team identifier, if assigned.</param>
/// <param name="RecordedAt">When the decision was recorded.</param>
/// <param name="RecordedById">The deciding member identifier.</param>
/// <param name="ActorDisplayName">The deciding member's name snapshot.</param>
/// <param name="ConcurrencyToken">The source participation version.</param>
public sealed record CampaignSavedPlacementDecision(
    long PlayerCampaignAssignmentId,
    long PlayerId,
    long CampaignId,
    long SeasonId,
    long SeasonOpeningSequence,
    PlacementOutcome Outcome,
    long? TeamId,
    [property: JsonRequired] DateTimeOffset RecordedAt,
    [property: JsonRequired] long RecordedById,
    [property: JsonRequired] string ActorDisplayName,
    Guid ConcurrencyToken);
