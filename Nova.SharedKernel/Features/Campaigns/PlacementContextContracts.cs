using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Results;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>A bounded placement history request in the visible campaign's participant context.</summary>
public sealed record GetPlacementContextInput
{
    /// <summary>The visible campaign.</summary>
    [Range(1, long.MaxValue)] public required long CampaignId { get; init; }
    /// <summary>The participant whose placement evidence is requested.</summary>
    [Range(1, long.MaxValue)] public required long PlayerCampaignAssignmentId { get; init; }
    /// <summary>The exclusive event-identity cursor for the next twenty changes.</summary>
    [Range(1, long.MaxValue)] public long? BeforeEventId { get; init; }
    /// <summary>When true, reject a reopened campaign instead of returning Active history scope.</summary>
    public bool? RequireClosed { get; init; }
}

/// <summary>A factual previous-season assignment, separate from current-season membership.</summary>
public sealed record PreviousSeasonPlacement(PlacementSeasonIdentity Season, PlacementDecisionSource Source,
    [property: JsonRequired] bool CanKeep);

/// <summary>A durable placement transition with original names and attribution.</summary>
public sealed record PlacementHistoryItem(long EventId, long CampaignId, string CampaignName,
    [property: JsonRequired] PlacementOutcome? PreviousOutcome, string? PreviousTeamName,
    [property: JsonRequired] PlacementOutcome Outcome, string? TeamName, string ActorDisplayName, DateTimeOffset OccurredAt);

/// <summary>Bounded history and previous-season context, never an effective-roster substitute.</summary>
public sealed record PlacementContextResult(long PlayerCampaignAssignmentId,
    [property: JsonRequired] PreviousSeasonPlacement? PreviousPlacement,
    [property: JsonRequired] IReadOnlyList<PlacementHistoryItem> History,
    [property: JsonRequired] long? NextEventId,
    [property: JsonRequired] bool CanSupersedeWithdrawal);

/// <summary>Reads bounded placement context for one visible campaign participant.</summary>
public interface IPlacementContextQueryService
{
    /// <summary>Reads previous-season context and up to twenty durable placement changes.</summary>
    Task<ServiceResult<PlacementContextResult>> GetContextAsync(GetPlacementContextInput input,
        CancellationToken cancellationToken = default);
}
