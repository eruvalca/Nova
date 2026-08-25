using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level acceptance of the Sass-compiled kelp-forest Bootstrap theme (issue #132). The
/// scenario signs in, loads the campaign list, and asserts the compiled theme is actually applied:
/// the <c>.btn-primary</c> surface is the kelp teal (not the default Bootstrap blue), no computed
/// style anywhere yields the default Bootstrap blue, the focused form control's ring is the kelp
/// teal, and the primary button, status badge, and a navigation link each meet WCAG AA 4.5:1.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class BootstrapThemeBrowserTests(BrowserSuiteFixture fixture)
{
    private const string ExpectedPrimaryRgb = "rgb(14, 124, 123)";
    private const string DefaultBootstrapBlueRgb = "rgb(13, 110, 253)";

    /// <summary>
    /// BT1: the kelp theme is compiled in and applied — the primary button is the kelp teal, the
    /// focused form control's ring uses the teal, and no computed style anywhere on the page uses
    /// the default Bootstrap blue.
    /// </summary>
    [Fact]
    public async Task Theme_PrimaryButtonAndFocusRing_AreKelpTeal_WithNoBootstrapBlue()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/campaigns").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns" })).ToBeVisibleAsync();

        // The "Create campaign" control is the page's `.btn-primary`. The compiled theme stylesheet
        // and the Bootstrap transition settle asynchronously, so a single synchronous computed-style
        // read under parallel load observes the transparent/empty start value. Poll through
        // BrowserRetryPolicy until the kelp-teal background appears.
        var primaryButton = page.GetByRole(AriaRole.Link, new() { Name = "Create campaign" });
        await Expect(primaryButton).ToBeVisibleAsync();
        var backgroundColor = string.Empty;
        for (var attempt = 0; attempt < BrowserRetryPolicy.MaxAttempts; attempt++)
        {
            backgroundColor = await primaryButton.EvaluateAsync<string>(
                "(el) => getComputedStyle(el).backgroundColor");
            if (backgroundColor == ExpectedPrimaryRgb)
            {
                break;
            }

            await page.WaitForTimeoutAsync(BrowserRetryPolicy.Delay);
        }

        backgroundColor.ShouldBe(ExpectedPrimaryRgb);

        // The focused form control (the campaign-view select) shows the teal focus ring. Bootstrap
        // transitions box-shadow over ~0.15s, so a single synchronous read right after focus()
        // always sees the transparent start value; poll through the transition window via
        // BrowserRetryPolicy until the teal value appears.
        var select = page.Locator("#campaigns-view-filter");
        var focusRing = string.Empty;
        for (var attempt = 0; attempt < BrowserRetryPolicy.MaxAttempts; attempt++)
        {
            focusRing = await select.EvaluateAsync<string>(
                "(el) => { el.focus(); return getComputedStyle(el).boxShadow; }");
            if (focusRing.Contains("rgba(14, 124, 123"))
            {
                break;
            }

            await page.WaitForTimeoutAsync(BrowserRetryPolicy.Delay);
        }

        focusRing.ShouldContain("rgba(14, 124, 123");

        // No computed style anywhere on the page resolves to the default Bootstrap blue.
        var anyBlue = await page.EvaluateAsync<bool>(
            @"() => {
                const blue = 'rgb(13, 110, 253)';
                for (const el of document.querySelectorAll('*')) {
                    for (const prop of ['color', 'backgroundColor', 'borderColor']) {
                        if (getComputedStyle(el)[prop] === blue) return true;
                    }
                }
                return false;
            }");
        anyBlue.ShouldBeFalse();
    }

    /// <summary>
    /// BT2: the kelp theme controls meet WCAG AA contrast — the primary button, the campaign
    /// status badge, and a navigation link each measure at least 4.5:1 against their background.
    /// </summary>
    [Fact]
    public async Task Theme_PrimaryButtonBadgeAndNavLink_MeetContrastThreshold()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/campaigns").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns" })).ToBeVisibleAsync();

        await A11yMeasurementHelpers.AssertContrastRatioAsync(
            page.GetByRole(AriaRole.Link, new() { Name = "Create campaign" }), 4.5, "primary button");

        var badge = page.Locator("span.badge.text-bg-success").First;
        await Expect(badge).ToHaveTextAsync("Active");
        await A11yMeasurementHelpers.AssertContrastRatioAsync(badge, 4.5, "campaign status badge");

        // The "Campaigns" navigation link sits on the `.navbar.bg-light` surface.
        await A11yMeasurementHelpers.AssertContrastRatioAsync(
            page.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true }), 4.5, "navigation link");
    }
}
