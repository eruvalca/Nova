namespace Nova.UI.Features.Players;

/// <summary>
/// Provides safe badge-style helpers for player tag color rendering.
/// </summary>
internal static class PlayerTagStyle
{
    /// <summary>
    /// The fallback badge background color used when an incoming color token is invalid.
    /// </summary>
    private const string DefaultTagColor = "#6C757D";

    /// <summary>
    /// Builds a safe inline style string for a player tag badge.
    /// </summary>
    /// <param name="color">The incoming color token.</param>
    /// <returns>A sanitized inline style string.</returns>
    public static string BuildBadgeStyle(string? color)
        => $"background-color: {NormalizeColor(color)}; color: #ffffff;";

    /// <summary>
    /// Normalizes a raw color token to an uppercase <c>#RRGGBB</c> value or a safe fallback.
    /// </summary>
    /// <param name="color">The incoming color token.</param>
    /// <returns>A normalized color token safe for inline style output.</returns>
    public static string NormalizeColor(string? color)
    {
        if (string.IsNullOrWhiteSpace(color))
        {
            return DefaultTagColor;
        }

        var trimmed = color.Trim();
        if (trimmed.Length != 7 || trimmed[0] != '#')
        {
            return DefaultTagColor;
        }

        for (var index = 1; index < trimmed.Length; index++)
        {
            if (!IsHexCharacter(trimmed[index]))
            {
                return DefaultTagColor;
            }
        }

        return trimmed.ToUpperInvariant();
    }

    /// <summary>
    /// Determines whether a character is a valid hexadecimal digit.
    /// </summary>
    /// <param name="character">The candidate character.</param>
    /// <returns><see langword="true"/> when the character is hexadecimal; otherwise <see langword="false"/>.</returns>
    private static bool IsHexCharacter(char character)
        => character is >= '0' and <= '9'
            or >= 'a' and <= 'f'
            or >= 'A' and <= 'F';
}
