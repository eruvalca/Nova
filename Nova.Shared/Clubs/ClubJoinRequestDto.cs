using Nova.Shared.Enums;

namespace Nova.Shared.Clubs;

/// <summary>
/// Represents a club join request returned from a service operation.
/// </summary>
/// <param name="ClubJoinRequestId">The unique identifier of the join request.</param>
/// <param name="ClubId">The id of the club being requested to join.</param>
/// <param name="ClubName">The display name of the club being requested to join.</param>
/// <param name="RequestingUserId">The id of the user who submitted the request.</param>
/// <param name="RequestingUserName">The full name of the user who submitted the request.</param>
/// <param name="Status">The current lifecycle status of the request.</param>
/// <param name="CreatedAt">The UTC timestamp when the request was submitted.</param>
public sealed record ClubJoinRequestDto(
    long ClubJoinRequestId,
    long ClubId,
    string ClubName,
    long RequestingUserId,
    string RequestingUserName,
    RequestStatus Status,
    DateTimeOffset CreatedAt);
