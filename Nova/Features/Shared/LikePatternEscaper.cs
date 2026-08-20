namespace Nova.Features.Shared;

/// <summary>
/// Escapes user-supplied search text so it is matched as a literal substring in an <c>ILIKE</c> pattern.
/// </summary>
internal static class LikePatternEscaper
{
    /// <summary>
    /// Escapes <c>ILIKE</c> pattern metacharacters so that a user-supplied search term is treated
    /// as a literal substring. Backslash is escaped first to avoid double-escaping, then
    /// <c>%</c> and <c>_</c> are escaped with the backslash escape character.
    /// </summary>
    /// <param name="value">The raw user search term.</param>
    /// <returns>The term with <c>\</c>, <c>%</c>, and <c>_</c> escaped for use in an <c>ILIKE '%…%' ESCAPE '\'</c> pattern.</returns>
    internal static string EscapeLikePattern(string value)
        => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");
}
