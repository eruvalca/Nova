using Nova.Shared.Features.Photos;

namespace Nova.Shared.Features.Clubs;

/// <summary>
/// Defines the route constants for club crest endpoints so the client and server agree on routes.
/// </summary>
public static class ClubCrestEndpoints
{
    /// <summary>
    /// The route template for retrieving a club's crest (GET), with a <c>size</c> query
    /// parameter. Mapped outside the clubs group at its absolute path and requires
    /// authorization; the small variant is a 64px square while the medium and large
    /// variants preserve the source aspect ratio.
    /// </summary>
    public const string GetTemplate = "/api/clubs/{clubId:long}/crest";

    /// <summary>
    /// The relative route template for managing a specific club's crest within a
    /// club-specific sub-group (POST changes the crest; DELETE removes it, ClubAdmin only).
    /// </summary>
    public const string ManageRelative = "{clubId:long}/crest";

    /// <summary>
    /// The absolute route template for changing a club's crest (POST, ClubAdmin only).
    /// Use <see cref="ChangeCrestUrl"/> to build the URL.
    /// </summary>
    public const string Change = "/api/clubs/{clubId:long}/crest";

    /// <summary>
    /// The absolute route template for removing a club's crest (DELETE, ClubAdmin only).
    /// Use <see cref="RemoveCrestUrl"/> to build the URL.
    /// </summary>
    public const string Remove = "/api/clubs/{clubId:long}/crest";

    /// <summary>
    /// Builds the URL for retrieving a club's crest at the requested size.
    /// </summary>
    /// <param name="clubId">The id of the club whose crest to retrieve.</param>
    /// <param name="size">The crest variant to retrieve.</param>
    /// <returns>The relative URL of the crest endpoint.</returns>
    public static string GetCrestUrl(long clubId, ProfilePhotoSize size) =>
        $"/api/clubs/{clubId}/crest?size={size.ToString().ToLowerInvariant()}";

    /// <summary>
    /// Builds the URL for changing a club's crest.
    /// </summary>
    /// <param name="clubId">The id of the club whose crest to change.</param>
    /// <returns>The relative URL of the change crest endpoint.</returns>
    public static string ChangeCrestUrl(long clubId) => $"/api/clubs/{clubId}/crest";

    /// <summary>
    /// Builds the URL for removing a club's crest.
    /// </summary>
    /// <param name="clubId">The id of the club whose crest to remove.</param>
    /// <returns>The relative URL of the remove crest endpoint.</returns>
    public static string RemoveCrestUrl(long clubId) => $"/api/clubs/{clubId}/crest";
}
