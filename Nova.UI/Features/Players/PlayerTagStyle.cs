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
    /// Builds a safe inline style string for a player tag badge, choosing black or white text
    /// by background luminance so the contrast ratio stays at or above the WCAG AA 4.5:1
    /// threshold for every valid color token.
    /// </summary>
    /// <param name="color">The incoming color token.</param>
    /// <returns>A sanitized inline style string.</returns>
    public static string BuildBadgeStyle(string? color)
    {
        var background = NormalizeColor(color);
        return $"background-color: {background}; color: {PickTextColor(background)};";
    }

    /// <summary>
    /// Picks <c>#FFFFFF</c> for dark backgrounds and <c>#000000</c> for light backgrounds. The
    /// luminance threshold sits inside the range where both text colors already meet 4.5:1,
    /// so every branch guarantees the required contrast.
    /// </summary>
    /// <param name="normalizedColor">An uppercase <c>#RRGGBB</c> color token.</param>
    /// <returns>The contrasting text color token.</returns>
    private static string PickTextColor(string normalizedColor)
    {
        var luminance =
            (0.2126 * LinearizeChannel(HexToByte(normalizedColor[1], normalizedColor[2])))
            + (0.7152 * LinearizeChannel(HexToByte(normalizedColor[3], normalizedColor[4])))
            + (0.0722 * LinearizeChannel(HexToByte(normalizedColor[5], normalizedColor[6])));
        return luminance <= 0.18 ? "#FFFFFF" : "#000000";
    }

    /// <summary>
    /// Converts an 8-bit color channel to its linearized luminance component per WCAG 2.x.
    /// </summary>
    /// <param name="channel">The raw channel value.</param>
    /// <returns>The linearized luminance component.</returns>
    private static double LinearizeChannel(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    /// <summary>
    /// Combines two hexadecimal characters into a byte value.
    /// </summary>
    /// <param name="high">The high hexadecimal character.</param>
    /// <param name="low">The low hexadecimal character.</param>
    /// <returns>The combined byte value.</returns>
    private static byte HexToByte(char high, char low) => (byte)((HexValue(high) << 4) | HexValue(low));

    /// <summary>
    /// Resolves a hexadecimal character to its numeric value.
    /// </summary>
    /// <param name="character">The candidate character.</param>
    /// <returns>The character's numeric value.</returns>
    private static int HexValue(char character) => character switch
    {
        >= '0' and <= '9' => character - '0',
        >= 'a' and <= 'f' => character - 'a' + 10,
        >= 'A' and <= 'F' => character - 'A' + 10,
        _ => 0
    };

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
