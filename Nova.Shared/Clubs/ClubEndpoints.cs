namespace Nova.Shared.Clubs;

/// <summary>
/// Defines the route constants for club and club join request endpoints so the client and server agree on routes.
/// </summary>
public static class ClubEndpoints
{
    /// <summary>
    /// The group prefix for club endpoints.
    /// </summary>
    public const string GroupPrefix = "/api/clubs";

    /// <summary>
    /// Creates a new club for the current user (POST). The current user becomes the club admin.
    /// </summary>
    public const string Create = "/api/clubs";

    /// <summary>
    /// The relative path for club creation within the group (empty string maps POST to the group root).
    /// </summary>
    public const string CreateRelative = "";

    /// <summary>
    /// Searches clubs by name, city, or state (GET). Accepts an optional <c>q</c> query parameter.
    /// </summary>
    public const string Search = "/api/clubs/search";

    /// <summary>
    /// The relative path for club search within the group.
    /// </summary>
    public const string SearchRelative = "search";

    /// <summary>
    /// Gets the current user's pending join request, if any (GET). Returns 404 when there is no pending request.
    /// </summary>
    public const string PendingRequest = "/api/clubs/join-requests/pending";

    /// <summary>
    /// The relative path for the pending join request within the group.
    /// </summary>
    public const string PendingRequestRelative = "join-requests/pending";

    /// <summary>
    /// The route template for creating a join request for a specific club (POST). Use <see cref="CreateJoinRequestUrl"/> to build the URL.
    /// </summary>
    public const string CreateJoinRequestTemplate = "/api/clubs/{clubId:long}/join-requests";

    /// <summary>
    /// The relative route template for creating a join request within a club-specific sub-group.
    /// </summary>
    public const string CreateJoinRequestRelative = "{clubId:long}/join-requests";

    /// <summary>
    /// The route template for cancelling a pending join request (DELETE). Use <see cref="CancelJoinRequestUrl"/> to build the URL.
    /// </summary>
    public const string CancelJoinRequestTemplate = "/api/clubs/join-requests/{requestId:long}";

    /// <summary>
    /// The relative path for cancelling a join request within the group.
    /// </summary>
    public const string CancelJoinRequestRelative = "join-requests/{requestId:long}";

    /// <summary>
    /// The route template for listing a specific club's pending join requests (GET, ClubAdmin only).
    /// Use <see cref="AdminJoinRequestsUrl"/> to build the URL.
    /// </summary>
    public const string AdminJoinRequests = "/api/clubs/{clubId:long}/admin/join-requests";

    /// <summary>
    /// The relative path for listing a club's pending join requests within the group.
    /// </summary>
    public const string AdminJoinRequestsRelative = "{clubId:long}/admin/join-requests";

    /// <summary>
    /// The route template for approving a pending join request (POST, ClubAdmin only).
    /// Use <see cref="ApproveJoinRequestUrl"/> to build the URL.
    /// </summary>
    public const string ApproveJoinRequest = "/api/clubs/join-requests/{requestId:long}/approve";

    /// <summary>
    /// The relative path for approving a join request within the group.
    /// </summary>
    public const string ApproveJoinRequestRelative = "join-requests/{requestId:long}/approve";

    /// <summary>
    /// The route template for rejecting a pending join request (POST, ClubAdmin only).
    /// Use <see cref="RejectJoinRequestUrl"/> to build the URL.
    /// </summary>
    public const string RejectJoinRequest = "/api/clubs/join-requests/{requestId:long}/reject";

    /// <summary>
    /// The relative path for rejecting a join request within the group.
    /// </summary>
    public const string RejectJoinRequestRelative = "join-requests/{requestId:long}/reject";

    /// <summary>
    /// Relative route (within the clubs group) for listing the current club's members.
    /// </summary>
    public const string GetMembersRelative = "members";

    /// <summary>
    /// Absolute route for listing the current club's members.
    /// </summary>
    public const string GetMembers = "/api/clubs/members";

    /// <summary>
    /// Relative route (within the clubs group) for assigning ClubAdmin to a member.
    /// </summary>
    public const string AssignAdminRelative = "assign-admin";

    /// <summary>
    /// Absolute route for assigning ClubAdmin to a member.
    /// </summary>
    public const string AssignAdmin = "/api/clubs/assign-admin";

    /// <summary>
    /// The full-document navigation endpoint that refreshes the auth cookie after club creation
    /// and redirects to the supplied return URL.
    /// </summary>
    public const string Complete = "/Clubs/Onboarding/Complete";

    /// <summary>
    /// Builds the URL for creating a join request for the specified club.
    /// </summary>
    /// <param name="clubId">The id of the club to request joining.</param>
    /// <returns>The relative URL of the create join request endpoint.</returns>
    public static string CreateJoinRequestUrl(long clubId) => $"/api/clubs/{clubId}/join-requests";

    /// <summary>
    /// Builds the URL for cancelling a specific join request.
    /// </summary>
    /// <param name="requestId">The id of the join request to cancel.</param>
    /// <returns>The relative URL of the cancel join request endpoint.</returns>
    public static string CancelJoinRequestUrl(long requestId) => $"/api/clubs/join-requests/{requestId}";

    /// <summary>
    /// Builds the URL for searching clubs with an optional query string.
    /// </summary>
    /// <param name="query">The search term, or <see langword="null"/> to return all clubs.</param>
    /// <returns>The relative URL of the search endpoint.</returns>
    public static string SearchUrl(string? query) =>
        string.IsNullOrWhiteSpace(query) ? Search : $"{Search}?q={Uri.EscapeDataString(query)}";

    /// <summary>
    /// Builds the URL for listing a club's pending join requests.
    /// </summary>
    /// <param name="clubId">The id of the club.</param>
    /// <returns>The relative URL of the admin join-requests endpoint.</returns>
    public static string AdminJoinRequestsUrl(long clubId) => $"/api/clubs/{clubId}/admin/join-requests";

    /// <summary>
    /// Builds the URL for approving a specific join request.
    /// </summary>
    /// <param name="requestId">The id of the join request.</param>
    /// <returns>The relative URL of the approve endpoint.</returns>
    public static string ApproveJoinRequestUrl(long requestId) => $"/api/clubs/join-requests/{requestId}/approve";

    /// <summary>
    /// Builds the URL for rejecting a specific join request.
    /// </summary>
    /// <param name="requestId">The id of the join request.</param>
    /// <returns>The relative URL of the reject endpoint.</returns>
    public static string RejectJoinRequestUrl(long requestId) => $"/api/clubs/join-requests/{requestId}/reject";
}
