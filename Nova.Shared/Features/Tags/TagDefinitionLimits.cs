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
    /// The maximum number of simultaneously-active tag definitions a club may own. Enforced during
    /// creation and restore so the active-only choices query never silently truncates the selectable
    /// set: a club cannot exceed this many active definitions, so the bounded choices read always
    /// returns the complete active set, and a restore that would push a club over the limit is
    /// rejected with a conflict.
    /// </summary>
    public const int MaxActiveTagDefinitions = 100;

    /// <summary>
    /// The maximum length of a tag-name search term accepted by the management-list endpoint.
    /// </summary>
    public const int MaxSearchLength = 100;
}
