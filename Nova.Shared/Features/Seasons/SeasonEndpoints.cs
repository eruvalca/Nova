namespace Nova.Shared.Features.Seasons;

/// <summary>Provides stable routes for season HTTP APIs.</summary>
public static class SeasonEndpoints
{
    /// <summary>Gets the seasons route group prefix.</summary>
    public const string GroupPrefix = "/api/seasons";

    /// <summary>Gets the relative collection route.</summary>
    public const string CollectionRelative = "";

    /// <summary>Gets the relative season detail route.</summary>
    public const string DetailRelative = "{seasonId:long}";

    /// <summary>Gets the relative season advancement route.</summary>
    public const string StartNextRelative = "start-next";

    /// <summary>Gets the named route for season detail.</summary>
    public const string GetDetailRouteName = "GetSeasonDetail";

    /// <summary>Builds the detail route for a season identifier.</summary>
    /// <param name="seasonId">The season identifier.</param>
    /// <returns>The season detail route.</returns>
    public static string Detail(long seasonId) => $"{GroupPrefix}/{seasonId}";
}
