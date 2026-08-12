namespace Nova.Shared.Features.Tags;

/// <summary>
/// Shared limits for the tag-definition feature so the server, WebAssembly clients, and URL
/// builders stay in agreement without duplicating magic numbers.
/// </summary>
public static class TagDefinitionLimits
{
    /// <summary>
    /// The maximum number of tag definitions returned by the management-list and choices queries.
    /// </summary>
    public const int MaxTagDefinitions = 100;

    /// <summary>
    /// The maximum length of a tag-name search term accepted by the management-list endpoint.
    /// </summary>
    public const int MaxSearchLength = 100;
}
