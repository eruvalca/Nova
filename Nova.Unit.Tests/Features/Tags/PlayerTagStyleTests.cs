using Nova.UI.Features.Players;
using Shouldly;

namespace Nova.Unit.Tests.Features.Tags;

/// <summary>
/// Decision-matrix coverage for the player tag badge style helper: every valid color token must
/// produce black or white text whose WCAG contrast ratio against the badge background is at
/// least 4.5:1, and invalid tokens must fall back to the default gray with safe text.
/// </summary>
public sealed class PlayerTagStyleTests
{
    /// <summary>
    /// Verifies the text color flips to black on light badge backgrounds.
    /// </summary>
    [Theory]
    [InlineData("#00CC00")] // Bright green: white text would be ~2.9:1.
    [InlineData("#999999")] // Mid gray.
    [InlineData("#FFFFFF")] // Pure white.
    [InlineData("#00cc00")] // Lowercase tokens are normalized first.
    public void BuildBadgeStyle_UsesBlackText_ForLightBackgrounds(string color)
    {
        var style = PlayerTagStyle.BuildBadgeStyle(color);

        style.ShouldContain("color: #000000;");
        ContrastRatio(style).ShouldBeGreaterThanOrEqualTo(4.5);
    }

    /// <summary>
    /// Verifies the text color stays white on dark badge backgrounds.
    /// </summary>
    [Theory]
    [InlineData("#CC0000")] // Deep red.
    [InlineData("#000000")] // Pure black.
    [InlineData("#1D3557")] // Dark navy.
    public void BuildBadgeStyle_UsesWhiteText_ForDarkBackgrounds(string color)
    {
        var style = PlayerTagStyle.BuildBadgeStyle(color);

        style.ShouldContain("color: #FFFFFF;");
        ContrastRatio(style).ShouldBeGreaterThanOrEqualTo(4.5);
    }

    /// <summary>
    /// Verifies invalid or missing color tokens fall back to the default gray background with
    /// white text at an acceptable contrast ratio.
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("red")]
    [InlineData("#12")]
    [InlineData("#GGGGGG")]
    public void BuildBadgeStyle_FallsBackToDefaultGray_ForInvalidTokens(string? color)
    {
        var style = PlayerTagStyle.BuildBadgeStyle(color);

        style.ShouldContain("background-color: #6C757D;");
        style.ShouldContain("color: #FFFFFF;");
        ContrastRatio(style).ShouldBeGreaterThanOrEqualTo(4.5);
    }

    /// <summary>
    /// Verifies the whole seeded club palette (representative dark and light colors) always
    /// yields at least 4.5:1 contrast.
    /// </summary>
    [Fact]
    public void BuildBadgeStyle_MeetsContrastThreshold_ForRepresentativePalette()
    {
        string[] palette = ["#FF5733", "#33FF57", "#3357FF", "#F1C40F", "#9B59B6", "#1ABC9C", "#E67E22", "#34495E", "#7F8C8D", "#2ECC71"];

        foreach (var color in palette)
        {
            ContrastRatio(PlayerTagStyle.BuildBadgeStyle(color)).ShouldBeGreaterThanOrEqualTo(4.5, $"contrast for {color}");
        }
    }

    /// <summary>
    /// Computes the WCAG contrast ratio between the text color and background color encoded in
    /// a badge style string of the form <c>background-color: #RRGGBB; color: #RRGGBB;</c>.
    /// </summary>
    /// <param name="style">The badge style string.</param>
    /// <returns>The contrast ratio, or zero when the style cannot be parsed.</returns>
    private static double ContrastRatio(string style)
    {
        var parts = style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var background = parts[0]["background-color: ".Length..].Trim();
        var foreground = parts[1]["color: ".Length..].Trim();
        var backgroundLuminance = RelativeLuminance(background);
        var foregroundLuminance = RelativeLuminance(foreground);
        var lighter = Math.Max(backgroundLuminance, foregroundLuminance);
        var darker = Math.Min(backgroundLuminance, foregroundLuminance);
        return (lighter + 0.05) / (darker + 0.05);
    }

    /// <summary>
    /// Computes the WCAG relative luminance of an uppercase <c>#RRGGBB</c> color token.
    /// </summary>
    /// <param name="color">The color token.</param>
    /// <returns>The relative luminance.</returns>
    private static double RelativeLuminance(string color)
    {
        var red = ParseChannel(color[1], color[2]);
        var green = ParseChannel(color[3], color[4]);
        var blue = ParseChannel(color[5], color[6]);
        return (0.2126 * red) + (0.7152 * green) + (0.0722 * blue);
    }

    /// <summary>
    /// Converts two hexadecimal characters to a linearized luminance channel value.
    /// </summary>
    /// <param name="high">The high hexadecimal character.</param>
    /// <param name="low">The low hexadecimal character.</param>
    /// <returns>The linearized channel value.</returns>
    private static double ParseChannel(char high, char low)
    {
        var value = ((HexValue(high) << 4) | HexValue(low)) / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

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
}
