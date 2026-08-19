using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Client.Services.Campaigns;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="ICampaignCloseoutQueryService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpCampaignCloseoutQueryService(HttpClient http) : ICampaignCloseoutQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<CampaignCloseoutReadinessDto>> GetCloseoutReadinessAsync(
        GetCampaignCloseoutReadinessInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        using var response = await http.GetAsync(
            CampaignEndpoints.GetCampaignCloseoutReadinessUrl(input.CampaignId),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignCloseoutReadinessDto>(
            "The server returned an invalid campaign closeout readiness response.",
            IsValidReadiness,
            cancellationToken);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignActivityResult>> GetActivityAsync(
        GetCampaignActivityInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        var limit = input.Limit ?? GetCampaignActivityInput.DefaultLimit;

        using var response = await http.GetAsync(
            CampaignEndpoints.GetCampaignActivityUrl(input),
            cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<CampaignActivityResult>(
            "The server returned an invalid campaign activity response.",
            result => IsValidActivity(result, limit),
            cancellationToken);
    }

    /// <summary>
    /// Validates that a decoded closeout-readiness payload is structurally valid, internally
    /// consistent, and keyed by the shared condition constants.
    /// </summary>
    /// <param name="result">The decoded closeout readiness.</param>
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidReadiness(CampaignCloseoutReadinessDto result)
        => result is not null
            && result.CampaignId > 0
            && IsValidSummary(result.Summary)
            && result.Blockers is not null
            && result.Blockers.All(blocker => blocker is not null)
            && result.IsReady == (result.Blockers.Count == 0)
            && result.Blockers.Select(blocker => blocker.Condition).Distinct().Count() == result.Blockers.Count
            && result.Blockers.All(IsValidBlocker)
            && HasConsistentOutcomesCount(result);

    /// <summary>
    /// Validates one condition-keyed blocker row.
    /// </summary>
    /// <param name="blocker">The blocker row to validate.</param>
    /// <returns><see langword="true"/> when the row is structurally valid.</returns>
    private static bool IsValidBlocker(CampaignCloseoutBlockerDto blocker)
        => blocker.Count >= 0
            && blocker.AssignmentIds is not null
            && blocker.AssignmentIds.Count == blocker.Count
            && blocker.AssignmentIds.All(id => id > 0)
            && blocker.AssignmentIds.Distinct().Count() == blocker.AssignmentIds.Count
            && IsKnownCondition(blocker.Condition);

    /// <summary>
    /// Verifies the not-ready payload's outcomes blocker count matches the summary undecided count.
    /// </summary>
    /// <param name="result">The decoded closeout readiness.</param>
    /// <returns><see langword="true"/> when the outcomes count is consistent with the summary.</returns>
    private static bool HasConsistentOutcomesCount(CampaignCloseoutReadinessDto result)
    {
        if (result.IsReady)
        {
            return true;
        }

        var outcomesBlocker = result.Blockers.FirstOrDefault(
            blocker => blocker.Condition == CloseoutBlockerConditions.Outcomes);
        return outcomesBlocker is null || outcomesBlocker.Count == result.Summary.UndecidedCount;
    }

    /// <summary>
    /// Determines whether a condition key is one of the three shared foundation constants.
    /// </summary>
    /// <param name="condition">The condition key.</param>
    /// <returns><see langword="true"/> when the key is known.</returns>
    private static bool IsKnownCondition(string condition)
        => condition == CloseoutBlockerConditions.Outcomes
            || condition == CloseoutBlockerConditions.Eligibility
            || condition == CloseoutBlockerConditions.ArchivedTeams;

    /// <summary>
    /// Validates that a decoded placement summary carries internally consistent, non-negative counts.
    /// </summary>
    /// <param name="summary">The decoded summary.</param>
    /// <returns><see langword="true"/> when every count is non-negative and the total equals their sum.</returns>
    private static bool IsValidSummary(CampaignPlacementSummaryDto summary)
        => summary is not null
            && summary.AssignedCount >= 0
            && summary.NotSelectedCount >= 0
            && summary.WithdrawnCount >= 0
            && summary.UndecidedCount >= 0
            && summary.TotalCount >= 0
            && summary.TotalCount
                == summary.AssignedCount
                + summary.NotSelectedCount
                + summary.WithdrawnCount
                + summary.UndecidedCount;

    /// <summary>
    /// Validates that a decoded activity payload is bounded, populated, and ordered newest-first.
    /// </summary>
    /// <param name="result">The decoded activity result.</param>
    /// <param name="requestedLimit">The bound the client requested.</param>
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidActivity(CampaignActivityResult result, int requestedLimit)
        => result is not null
            && result.Events is not null
            && result.Events.Count <= requestedLimit
            && result.Events.All(IsValidActivityItem)
            && IsOrdered(result.Events);

    /// <summary>
    /// Validates one activity event row.
    /// </summary>
    /// <param name="item">The activity row to validate.</param>
    /// <returns><see langword="true"/> when the row is structurally valid.</returns>
    private static bool IsValidActivityItem(CampaignActivityItemDto item)
        => item is not null
            && item.CampaignLifecycleEventId > 0
            && item.EventType is CampaignLifecycleEventType.Closed or CampaignLifecycleEventType.Reopened
            && item.ActorUserId > 0
            && !string.IsNullOrWhiteSpace(item.ActorDisplayName);

    /// <summary>
    /// Verifies the portable activity ordering contract: timestamps must be non-increasing, and when
    /// two adjacent events share a timestamp their identifiers must be non-increasing.
    /// </summary>
    /// <param name="events">The activity rows to verify.</param>
    /// <returns><see langword="true"/> when every adjacent pair satisfies the ordering contract.</returns>
    private static bool IsOrdered(IReadOnlyList<CampaignActivityItemDto> events)
    {
        for (var index = 1; index < events.Count; index++)
        {
            var previous = events[index - 1];
            var current = events[index];
            if (previous.CreatedAt < current.CreatedAt)
            {
                return false;
            }

            if (previous.CreatedAt == current.CreatedAt
                && previous.CampaignLifecycleEventId < current.CampaignLifecycleEventId)
            {
                return false;
            }
        }

        return true;
    }
}
