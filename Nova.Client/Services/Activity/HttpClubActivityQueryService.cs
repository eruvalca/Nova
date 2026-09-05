using Nova.Shared.Enums;
using Nova.Shared.Features.Activity;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Client.Services.Activity;

/// <summary>
/// WebAssembly HTTP implementation of <see cref="IClubActivityQueryService"/>.
/// </summary>
/// <param name="http">The HTTP client configured with the application base address.</param>
public sealed class HttpClubActivityQueryService(HttpClient http) : IClubActivityQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<ClubActivityResult>> GetClubActivityAsync(
        GetClubActivityInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        var cursor = input.BeforeActivityEventId is long id && input.BeforeOccurredAt is DateTimeOffset occurredAt
            ? new ClubActivityCursor(id, occurredAt)
            : null;

        using var response = await http.GetAsync(ActivityEndpoints.GetClubActivityUrl(cursor), cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<ClubActivityResult>(
            "The server returned an invalid club activity response.",
            IsValidActivityResult,
            cancellationToken);
    }

    /// <summary>
    /// Validates the structural invariants of a club activity success payload.
    /// </summary>
    /// <param name="result">The deserialized activity payload.</param>
    /// <returns><see langword="true"/> when the payload satisfies the client contract.</returns>
    private static bool IsValidActivityResult(ClubActivityResult result)
        => result is not null
            && result.Events is not null
            && result.Events.Count <= GetClubActivityInput.PageSize
            && result.Events.All(IsValidActivityItem)
            && IsOrdered(result.Events)
            && IsValidCursor(result);

    /// <summary>
    /// Validates one activity row, including the kind-to-family correspondence and family field
    /// presence so a malformed server payload fails loudly instead of rendering incomplete copy.
    /// </summary>
    /// <param name="item">The activity row to validate.</param>
    /// <returns><see langword="true"/> when the row is structurally valid for its kind.</returns>
    private static bool IsValidActivityItem(ClubActivityItemDto item)
    {
        if (item is null
            || item.ActivityEventId <= 0
            || item.OccurredAt == default
            || item.Context is null)
        {
            return false;
        }

        // Every non-membership kind carries a complete actor snapshot. MemberJoined is role-shaped:
        // the member view hides the actor and approval name, the administrator view carries a
        // complete actor plus the approval name, and any mixed shape is a malformed payload.
        if (item.Kind != ActivityEventKind.MemberJoined)
        {
            if (item.ActorUserId is not > 0 || string.IsNullOrWhiteSpace(item.ActorDisplayName))
            {
                return false;
            }
        }
        else if (item.Context is not MembershipContext membership)
        {
            return false;
        }
        else
        {
            var memberShape = item.ActorUserId is null
                && item.ActorDisplayName is null
                && membership.ApprovedByActorName is null;
            var adminShape = item.ActorUserId is > 0
                && !string.IsNullOrWhiteSpace(item.ActorDisplayName)
                && !string.IsNullOrWhiteSpace(membership.ApprovedByActorName);
            if (!memberShape && !adminShape)
            {
                return false;
            }
        }

        return item.Kind switch
        {
            ActivityEventKind.CampaignDraftCreated
            or ActivityEventKind.CampaignDraftDeleted
            or ActivityEventKind.CampaignOpened
            or ActivityEventKind.CampaignClosed
            or ActivityEventKind.CampaignReopened
                => item.Context is CampaignLifecycleContext context
                    && context.CampaignId > 0
                    && !string.IsNullOrWhiteSpace(context.CampaignName),
            ActivityEventKind.PlacementAssigned
            or ActivityEventKind.PlacementNotSelected
            or ActivityEventKind.PlacementWithdrawn
            or ActivityEventKind.PlacementReassigned
            or ActivityEventKind.PlacementOutcomeReplaced
            or ActivityEventKind.PlacementSuperseded
                => item.Context is PlacementContext placement
                    && IsValidPlacement(item.Kind, placement),
            ActivityEventKind.JoinRequestSubmitted
            or ActivityEventKind.JoinRequestCancelled
            or ActivityEventKind.JoinRequestRejected
                => item.Context is JoinRequestContext joinRequest
                    && joinRequest.JoinRequestId > 0
                    && !string.IsNullOrWhiteSpace(joinRequest.RequesterDisplayName),
            ActivityEventKind.MemberJoined
            or ActivityEventKind.MemberRemoved
            or ActivityEventKind.MemberLeft
                => item.Context is MembershipContext membership
                    && membership.MemberUserId > 0
                    && !string.IsNullOrWhiteSpace(membership.MemberDisplayName),
            ActivityEventKind.MemberPromoted
            or ActivityEventKind.MemberDemoted
                => item.Context is MemberRoleContext role
                    && role.MemberUserId > 0
                    && !string.IsNullOrWhiteSpace(role.MemberDisplayName)
                    && !string.IsNullOrWhiteSpace(role.Role),
            _ => false
        };
    }

    /// <summary>
    /// Validates the placement-family base fields plus the kind-specific resulting outcome and team
    /// snapshot consistency, so contradictory rows (for example, an assignment with a non-assigned
    /// outcome) surface as protocol failures instead of rendering misleading copy.
    /// </summary>
    /// <param name="kind">The placement event kind.</param>
    /// <param name="placement">The deserialized placement context.</param>
    /// <returns><see langword="true"/> when the placement context is consistent with its kind.</returns>
    private static bool IsValidPlacement(ActivityEventKind kind, PlacementContext placement)
    {
        if (placement.CampaignId <= 0
            || string.IsNullOrWhiteSpace(placement.CampaignName)
            || placement.PlayerCampaignAssignmentId <= 0
            || string.IsNullOrWhiteSpace(placement.PlayerDisplayName))
        {
            return false;
        }

        return kind switch
        {
            ActivityEventKind.PlacementAssigned
                => placement.Outcome == PlacementOutcome.Assigned
                    && !string.IsNullOrWhiteSpace(placement.TeamName),
            ActivityEventKind.PlacementReassigned
                => placement.Outcome == PlacementOutcome.Assigned
                    && !string.IsNullOrWhiteSpace(placement.PreviousTeamName)
                    && !string.IsNullOrWhiteSpace(placement.TeamName),
            ActivityEventKind.PlacementNotSelected
                => placement.Outcome == PlacementOutcome.NotSelected
                    && placement.TeamName is null,
            ActivityEventKind.PlacementWithdrawn
                => placement.Outcome == PlacementOutcome.Withdrawn
                    && placement.TeamName is null,
            ActivityEventKind.PlacementOutcomeReplaced
                => placement.Outcome is PlacementOutcome.Assigned or PlacementOutcome.Undecided
                    && (placement.Outcome == PlacementOutcome.Assigned
                        ? !string.IsNullOrWhiteSpace(placement.TeamName)
                        : placement.TeamName is null),
            ActivityEventKind.PlacementSuperseded
                => placement.PlayerId > 0
                    && placement.SeasonId > 0
                    && placement.PreviousCampaignId > 0
                    && placement.PreviousCampaignId != placement.CampaignId
                    && !string.IsNullOrWhiteSpace(placement.PreviousCampaignName)
                    && placement.PreviousPlayerCampaignAssignmentId > 0
                    && placement.PreviousPlayerCampaignAssignmentId != placement.PlayerCampaignAssignmentId
                    && IsSavedDecision(placement.Outcome, placement.TeamId, placement.TeamName)
                    && IsSavedDecision(placement.PreviousOutcome, placement.PreviousTeamId, placement.PreviousTeamName),
            _ => false,
        };
    }

    /// <summary>Checks a saved outcome and its paired team identifier/name snapshots.</summary>
    /// <param name="outcome">The saved decision outcome.</param>
    /// <param name="teamId">The assigned team's identifier, if assigned.</param>
    /// <param name="teamName">The assigned team's display-name snapshot, if assigned.</param>
    /// <returns>Whether the decision has a consistent saved outcome and team.</returns>
    private static bool IsSavedDecision(PlacementOutcome? outcome, long? teamId, string? teamName)
        => outcome switch
        {
            PlacementOutcome.Assigned => teamId > 0 && !string.IsNullOrWhiteSpace(teamName),
            PlacementOutcome.NotSelected or PlacementOutcome.Withdrawn => teamId is null && teamName is null,
            _ => false,
        };

    /// <summary>
    /// Verifies the portable ordering contract: <c>OccurredAt</c>, then <c>ActivityEventId</c> must
    /// be non-increasing across adjacent events.
    /// </summary>
    /// <param name="events">The activity rows to verify.</param>
    /// <returns><see langword="true"/> when every adjacent pair satisfies the ordering contract.</returns>
    private static bool IsOrdered(IReadOnlyList<ClubActivityItemDto> events)
    {
        for (var index = 1; index < events.Count; index++)
        {
            var previous = events[index - 1];
            var current = events[index];
            if (previous.OccurredAt < current.OccurredAt)
            {
                return false;
            }

            if (previous.OccurredAt == current.OccurredAt
                && previous.ActivityEventId < current.ActivityEventId)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>
    /// Validates the optional continuation cursor consistency: <c>NextCursor</c> is present exactly
    /// when <c>HasMore</c> is set and the feed is not exhausted.
    /// </summary>
    /// <param name="result">The decoded activity result.</param>
    /// <returns><see langword="true"/> when the cursor contract holds.</returns>
    private static bool IsValidCursor(ClubActivityResult result)
        => result.HasMore
            ? result.NextCursor is { ActivityEventId: > 0 } cursor
                && cursor.OccurredAt != default
            : result.NextCursor is null;
}
