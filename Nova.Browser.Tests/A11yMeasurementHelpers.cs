using System.Globalization;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Shared accessibility measurement helpers reused by browser scenarios: WCAG 2.5.8 minimum
/// touch-target sizing, WCAG AA text/background contrast-ratio computation, and the multi-target
/// checklist measurement used by the <c>NOVA_A11Y_SCREENSHOTS</c> evidence pass. Extracted from the
/// #69 measurement approach so scenarios never re-measure by hand.
/// </summary>
internal static class A11yMeasurementHelpers
{
    /// <summary>
    /// Asserts a control meets the WCAG 2.5.8 minimum target size (24×24 CSS px). The circuit can
    /// re-render right after visibility, briefly collapsing the control to a zero-size bounding box,
    /// so the measurement is retried until the layout settles.
    /// </summary>
    /// <param name="page">The page to wait for layout settlement.</param>
    /// <param name="locator">The control to measure.</param>
    /// <param name="name">The control's display name for the failure message.</param>
    /// <returns>A task that completes once the size assertion passes.</returns>
    public static async Task AssertTouchTargetAsync(IPage page, ILocator locator, string name)
    {
        await Expect(locator).ToBeVisibleAsync();

        double[] size = [];
        for (var attempt = 0; attempt < 20; attempt++)
        {
            size = await locator.EvaluateAsync<double[]>(
                "(el) => { const r = el.getBoundingClientRect(); return [r.width, r.height]; }");
            if (size[0] > 0 && size[1] > 0)
            {
                break;
            }

            await page.WaitForTimeoutAsync(200);
        }

        size[0].ShouldBeGreaterThanOrEqualTo(24, $"touch-target width for {name}");
        size[1].ShouldBeGreaterThanOrEqualTo(24, $"touch-target height for {name}");
    }

    /// <summary>
    /// Measures the WCAG AA text/background contrast ratio of a single element using its computed
    /// <c>color</c> and <c>background-color</c>. Semi-transparent colors are composited over the
    /// white page surface before the luminance calculation.
    /// </summary>
    /// <param name="locator">The element to measure.</param>
    /// <returns>The computed contrast ratio.</returns>
    public static async Task<double> MeasureContrastRatioAsync(ILocator locator)
    {
        // The circuit can re-render right after the element appears, briefly leaving its computed
        // colors unparseable. Retry the measurement until the layout settles, mirroring the
        // touch-target retry loop.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var raw = await locator.EvaluateAsync<string?>(@"(el) => {
                const parse = color => {
                    const m = color.match(/rgba?\(([\d.]+),\s*([\d.]+),\s*([\d.]+)(?:,\s*([\d.]+))?\)/);
                    return m ? { r: +m[1], g: +m[2], b: +m[3], a: m[4] === undefined ? 1 : +m[4] } : null;
                };
                const overWhite = c => {
                    if (c === null || c.a === 1) return c;
                    return { r: c.r * c.a + 255 * (1 - c.a), g: c.g * c.a + 255 * (1 - c.a), b: c.b * c.a + 255 * (1 - c.a) };
                };
                const luminance = (r, g, b) => {
                    const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
                    return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
                };
                const contrast = (fg, bg) => {
                    const f = overWhite(fg), b = overWhite(bg) ?? { r: 255, g: 255, b: 255 };
                    const l1 = luminance(f.r, f.g, f.b), l2 = luminance(b.r, b.g, b.b);
                    const [hi, lo] = l1 >= l2 ? [l1, l2] : [l2, l1];
                    return (hi + 0.05) / (lo + 0.05);
                };
                const style = getComputedStyle(el);
                const fg = parse(style.color), bg = parse(style.backgroundColor);
                if (!fg || !bg) return null;
                return String(contrast(fg, bg));
            }");
            if (raw is not null && double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var ratio))
            {
                return ratio;
            }

            await Task.Delay(200);
        }

        throw new TimeoutException("The element's computed contrast colors never settled.");
    }

    /// <summary>
    /// Asserts a single element's text/background contrast ratio meets or exceeds the supplied
    /// WCAG threshold (4.5:1 for AA normal text).
    /// </summary>
    /// <param name="locator">The element to measure.</param>
    /// <param name="minimum">The minimum acceptable contrast ratio.</param>
    /// <param name="name">The element's display name for the failure message.</param>
    /// <returns>A task that completes once the contrast assertion passes.</returns>
    public static async Task AssertContrastRatioAsync(ILocator locator, double minimum, string name)
    {
        var ratio = await MeasureContrastRatioAsync(locator);
        ratio.ShouldBeGreaterThanOrEqualTo(minimum, $"text/background contrast ratio for {name}");
    }

    /// <summary>
    /// Measures contrast ratios and touch-target sizes for the manual accessibility checklist and
    /// returns one line per finding, matching the #69 evidence-pass format.
    /// </summary>
    /// <param name="page">The page whose visible controls should be measured.</param>
    /// <param name="scope">The label prefixing each returned measurement line.</param>
    /// <returns>One measurement string per visible, non-empty target.</returns>
    public static async Task<IReadOnlyList<string>> MeasureChecklistAsync(IPage page, string scope)
    {
        var result = await page.EvaluateAsync<string>(@"() => {
            const results = [];
            const targets = [
                { name: 'status-badge', selector: '.badge', limit: 3 },
                { name: 'note-meta', selector: '.participant-drawer-note-meta', limit: 1 },
                { name: 'tag-meta', selector: '.participant-drawer-tag-meta', limit: 1 },
                { name: 'readonly-note', selector: '.participant-drawer-readonly-note', limit: 1 },
                { name: 'primary-button', selector: '.participant-drawer button.btn-primary', limit: 1 },
                { name: 'secondary-button', selector: '.participant-drawer button.btn-outline-secondary', limit: 2 },
                { name: 'drawer-close', selector: '#participant-drawer-close', limit: 1 },
                { name: 'drawer-prev', selector: '#participant-drawer-previous', limit: 1 },
                { name: 'drawer-next', selector: '#participant-drawer-next', limit: 1 },
                { name: 'roster-row', selector: 'tbody tr[id^=\'roster-row-\']', limit: 1 },
                { name: 'roster-card', selector: '[id^=\'roster-card-\']', limit: 1 },
                { name: 'pager-button', selector: 'nav[aria-label=\'Roster pagination\'] button', limit: 2 },
                { name: 'search-input', selector: '#roster-search', limit: 1 }
            ];
            const luminance = (r, g, b) => {
                const f = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
                return 0.2126 * f(r) + 0.7152 * f(g) + 0.0722 * f(b);
            };
            const parse = color => {
                const m = color.match(/rgba?\(([\d.]+),\s*([\d.]+),\s*([\d.]+)(?:,\s*([\d.]+))?\)/);
                return m ? { r: +m[1], g: +m[2], b: +m[3], a: m[4] === undefined ? 1 : +m[4] } : null;
            };
            // Composite a semi-transparent color over white (the page surface).
            const overWhite = c => {
                if (c === null || c.a === 1) return c;
                return {
                    r: c.r * c.a + 255 * (1 - c.a),
                    g: c.g * c.a + 255 * (1 - c.a),
                    b: c.b * c.a + 255 * (1 - c.a)
                };
            };
            const contrast = (fg, bg) => {
                const f = overWhite(fg), b = overWhite(bg) ?? { r: 255, g: 255, b: 255 };
                const l1 = luminance(f.r, f.g, f.b);
                const l2 = luminance(b.r, b.g, b.b);
                const [hi, lo] = l1 >= l2 ? [l1, l2] : [l2, l1];
                return ((hi + 0.05) / (lo + 0.05)).toFixed(2);
            };
            for (const t of targets) {
                let count = 0;
                for (const el of document.querySelectorAll(t.selector)) {
                    const rect = el.getBoundingClientRect();
                    if (rect.width === 0 || rect.height === 0) continue;
                    const style = getComputedStyle(el);
                    const fg = parse(style.color);
                    const bg = parse(style.backgroundColor);
                    const item = [];
                    item.push(t.name);
                    if (fg && bg) item.push('contrast=' + contrast(fg, bg));
                    item.push(rect.width.toFixed(0) + 'x' + rect.height.toFixed(0));
                    results.push(item.join(' '));
                    if (++count >= t.limit) break;
                }
            }
            return results.join('\n');
        }");
        return result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }
}
