using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using Nova.SharedKernel.Results;

namespace Nova.SharedKernel.Features.Campaigns;

/// <summary>A stable creation-time/identity cursor for newest-first shared evidence.</summary>
/// <param name="CreatedAt">The last displayed creation timestamp.</param>
/// <param name="Id">The last displayed unique identity.</param>
public sealed record EvaluationHistoryCursor([property: JsonRequired] DateTimeOffset CreatedAt, [property: JsonRequired] long Id);

/// <summary>A bounded evidence region with explicit continuation.</summary>
/// <typeparam name="T">The evidence item.</typeparam>
/// <param name="Items">Up to twenty newest-first items.</param>
/// <param name="Next">The next cursor, present only when another item exists.</param>
public sealed record EvaluationHistoryPage<T>([property: JsonRequired] IReadOnlyList<T> Items, [property: JsonRequired] EvaluationHistoryCursor? Next);

/// <summary>Requests one participant's twenty-item evidence page.</summary>
public sealed record GetEvaluationHistoryInput : IValidatableObject
{
    /// <summary>The campaign identity.</summary>
    [Range(1, long.MaxValue)] public required long CampaignId { get; init; }
    /// <summary>The participant identity.</summary>
    [Range(1, long.MaxValue)] public required long PlayerCampaignAssignmentId { get; init; }
    /// <summary>The exclusive cursor timestamp, omitted for the first page.</summary>
    public DateTimeOffset? BeforeCreatedAt { get; init; }
    /// <summary>The exclusive cursor identity, omitted for the first page.</summary>
    [Range(1, long.MaxValue)] public long? BeforeId { get; init; }

    /// <inheritdoc />
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (BeforeId.HasValue != BeforeCreatedAt.HasValue || BeforeCreatedAt <= DateTimeOffset.UnixEpoch)
        {
            yield return new ValidationResult("A history cursor requires a timestamp and positive identity.", [nameof(BeforeCreatedAt), nameof(BeforeId)]);
        }
    }
}

/// <summary>One active catalog choice with authoritative selected-player application status.</summary>
/// <param name="PlayerTagId">The definition identity.</param>
/// <param name="Name">The first-created display label.</param>
/// <param name="Color">The shared color token.</param>
/// <param name="ApplicationId">The existing application, if any.</param>
public sealed record EvaluationTagChoice([property: JsonRequired] long PlayerTagId, [property: JsonRequired] string Name, [property: JsonRequired] string Color, [property: JsonRequired] long? ApplicationId);

/// <summary>Reads independently recoverable evidence regions without expanding participant identity payloads.</summary>
public interface ICampaignEvaluationQueryService
{
    /// <summary>Reads twenty shared notes in creation order.</summary>
    Task<ServiceResult<EvaluationHistoryPage<CampaignParticipantNoteDto>>> GetNotesAsync(GetEvaluationHistoryInput input, CancellationToken cancellationToken = default);
    /// <summary>Reads twenty applications, including archived definitions.</summary>
    Task<ServiceResult<EvaluationHistoryPage<CampaignParticipantTagApplicationDto>>> GetApplicationsAsync(GetEvaluationHistoryInput input, CancellationToken cancellationToken = default);
    /// <summary>Reads the complete active catalog and this player's applied status.</summary>
    Task<ServiceResult<IReadOnlyList<EvaluationTagChoice>>> GetTagChoicesAsync(GetCampaignParticipantDetailInput input, CancellationToken cancellationToken = default);
}
