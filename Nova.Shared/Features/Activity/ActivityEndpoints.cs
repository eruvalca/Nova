namespace Nova.Shared.Features.Activity;

/// <summary>
/// Defines the shared route constants for the club activity feed read endpoint so the server and
/// WebAssembly client agree on the route.
/// </summary>
public static class ActivityEndpoints
{
    /// <summary>
    /// The group prefix for club activity endpoints.
    /// </summary>
    public const string GroupPrefix = "/api/activity";

    /// <summary>
    /// Gets the club activity feed route (GET).
    /// </summary>
    public const string GetClubActivity = GroupPrefix;

    /// <summary>
    /// Gets the club activity feed route relative to the activity group (empty maps GET to the group root).
    /// </summary>
    public const string GetClubActivityRelative = "";

    /// <summary>
    /// Gets the route name assigned to the club activity feed query.
    /// </summary>
    public const string GetClubActivityRouteName = "GetClubActivity";

    /// <summary>
    /// Builds the club activity URL, omitting the optional cursor when it is not supplied or when
    /// it is invalid (a non-positive event identifier), so this builder only emits URLs the input
    /// contract (<c>[Range(1, long.MaxValue)]</c>) accepts.
    /// </summary>
    public static string GetClubActivityUrl(ClubActivityCursor? cursor)
        => cursor is not { ActivityEventId: > 0 }
            ? GetClubActivity
            : $"{GetClubActivity}?beforeActivityEventId={cursor.ActivityEventId}&beforeOccurredAt={Uri.EscapeDataString(cursor.OccurredAt.ToString("O"))}";
}
