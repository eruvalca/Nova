using System.Text.RegularExpressions;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level acceptance of the public Nova landing page (issue #145): the anonymous root renders
/// the public marketing content without the authenticated navbar, each section carries the approved
/// truthful copy, the CTA and section-navigation destinations resolve, an onboarded signed-in visitor
/// is redirected to /dashboard, the onboarding gates stay ahead of the dashboard, and the page meets
/// the metadata, keyboard/focus, touch-target, contrast, and responsive constraints.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class LandingPageBrowserTests(BrowserSuiteFixture fixture)
{
    private static readonly Regex RegisterHrefPattern = new(@".*Account/Register\?returnUrl=(%2F|/)dashboard", RegexOptions.Compiled);

    /// <summary>
    /// LP1: an anonymous visit to / renders the public landing page — the approved hero headline, the
    /// section CTA/nav links, and no authenticated bottom navbar.
    /// </summary>
    [Fact]
    public async Task Landing_Anonymous_RendersPublicContent_WithoutAuthenticatedNavbar()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());

        // The public landing page renders (not a redirect to login).
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Run better tryouts. Build stronger teams.", Exact = true })).ToBeVisibleAsync();

        // The public header carries the Nova identity and the sign-in / create actions.
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Sign in", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Create your club", Exact = true })).ToHaveCountAsync(3);

        // No authenticated bottom navbar leaks to anonymous users.
        await Expect(page.Locator("nav[aria-label='Primary']")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Dashboard", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Campaigns", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Players", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Teams", Exact = true })).ToHaveCountAsync(0);
    }

    /// <summary>
    /// LP2: every landing section carries its approved, truthful content — the product preview is
    /// clearly illustrative, the how-it-works sequence uses Nova terminology, the admin/coach role-fit
    /// copy is present, and the trust section is limited to verifiable role-based access and club data
    /// isolation (no fabricated certifications, customer counts, or pricing).
    /// </summary>
    [Fact]
    public async Task Landing_Sections_CarryTruthfulApprovedContent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());

        // Product preview: illustrative content is explicitly labelled.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Registration to roster, without losing the thread.", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("[aria-label='Illustrative Nova campaign workspace']")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Build the player pool", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Capture observations", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Resolve team assignments", Exact = true })).ToBeVisibleAsync();

        // How it works: Nova's own workflow terminology.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Three stops. One source of truth.", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Establish the context", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Collaborate on evaluations", Exact = true })).ToHaveCountAsync(1);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Place and close", Exact = true })).ToBeVisibleAsync();

        // Admin/coach role fit.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "The whole staff. The right access.", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("Nova brings administrators, coaches, and evaluators", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(page.GetByText("Record evaluations, apply tags, and collaborate", new() { Exact = false })).ToBeVisibleAsync();
        await Expect(page.GetByText("permissions enforced by their role", new() { Exact = false })).ToBeVisibleAsync();

        // Trust: limited to verifiable behavior, no fabricated claims.
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Trust is enforced, not advertised.", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Role-based access", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club data isolation", Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>
    /// LP3: the primary <c>Create your club</c> action targets the registration page with the safe
    /// /dashboard continuation, and the secondary <c>Follow the campaign route</c> action targets the
    /// anchored how-it-works section.
    /// </summary>
    [Fact]
    public async Task Landing_CtaDestinations_ResolveToRegisterAndSection()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());

        var createClub = page.GetByRole(AriaRole.Link, new() { Name = "Create your club", Exact = true }).First;
        await Expect(createClub).ToHaveAttributeAsync("href", RegisterHrefPattern);

        var seeHow = page.GetByRole(AriaRole.Link, new() { Name = "Follow the campaign route", Exact = true });
        await Expect(seeHow).ToHaveAttributeAsync("href", "#how-it-works");
    }

    /// <summary>
    /// LP4: the public header and footer anchor navigation leap to the corresponding landing sections.
    /// </summary>
    [Fact]
    public async Task Landing_AnchorNavigation_LeapsToSections()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());

        await page.GetByRole(AriaRole.Link, new() { Name = "Product", Exact = true }).First.ClickAsync();
        await page.WaitForURLAsync(url => url.EndsWith("/#product", StringComparison.OrdinalIgnoreCase));

        await page.GetByRole(AriaRole.Link, new() { Name = "How it works", Exact = true }).First.ClickAsync();
        await page.WaitForURLAsync(url => url.EndsWith("/#how-it-works", StringComparison.OrdinalIgnoreCase));
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Three stops. One source of truth.", Exact = true })).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "For clubs", Exact = true }).ClickAsync();
        await page.WaitForURLAsync(url => url.EndsWith("/#collaboration", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// LP5: a fully onboarded signed-in visitor at / is redirected to the authenticated /dashboard
    /// home (history-replace), so the public page is not reachable while authenticated.
    /// </summary>
    [Fact]
    public async Task Landing_AuthenticatedAdmin_RedirectsToDashboard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, DashboardSeed.Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());
        await page.WaitForURLAsync(url => url.Contains("/dashboard", StringComparison.OrdinalIgnoreCase));

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Club operations" })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Run better tryouts. Build stronger teams.", Exact = true })).ToHaveCountAsync(0);
    }

    /// <summary>
    /// LP6: the onboarding gates still run ahead of the dashboard — a photo-less user lands on
    /// /Account/ProfilePhoto and a photo-complete club-less user lands on /Clubs/Onboarding.
    /// </summary>
    [Fact]
    public async Task Landing_OnboardingGates_ArePreservedBeforeDashboard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await DashboardSeed.SeedAsync(fixture.AppHost, cancellationToken);

        await using (var photoLessContext = await fixture.NewSignedInContextAsync(seed.PhotoLessEmail, DashboardSeed.Password))
        {
            var page = photoLessContext.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());
            await page.WaitForURLAsync(url => url.Contains("/Account/ProfilePhoto", StringComparison.OrdinalIgnoreCase));
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Profile photo" })).ToBeVisibleAsync();
        }

        await using (var clubLessContext = await fixture.NewSignedInContextAsync(seed.ClubLessEmail, DashboardSeed.Password))
        {
            var page = clubLessContext.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());
            await page.WaitForURLAsync(url => url.Contains("/Clubs/Onboarding", StringComparison.OrdinalIgnoreCase));
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Welcome to Nova" })).ToBeVisibleAsync();
        }
    }

    /// <summary>
    /// LP7: the document head carries the page title, meta description, canonical URL, and Open Graph +
    /// Twitter metadata for the landing page.
    /// </summary>
    [Fact]
    public async Task Landing_Metadata_SetInDocumentHead()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());

        (await page.TitleAsync()).ShouldBe("Nova — Run better tryouts. Build stronger teams.");

        var description = await page.EvaluateAsync<string>(
            "() => document.querySelector('meta[name=\"description\"]')?.content ?? null");
        var canonical = await page.EvaluateAsync<string>(
            "() => document.querySelector('link[rel=\"canonical\"]')?.href ?? null");
        var ogType = await page.EvaluateAsync<string>(
            "() => document.querySelector('meta[property=\"og:type\"]')?.content ?? null");
        var ogSiteName = await page.EvaluateAsync<string>(
            "() => document.querySelector('meta[property=\"og:site_name\"]')?.content ?? null");
        var ogTitle = await page.EvaluateAsync<string>(
            "() => document.querySelector('meta[property=\"og:title\"]')?.content ?? null");
        var ogUrl = await page.EvaluateAsync<string>(
            "() => document.querySelector('meta[property=\"og:url\"]')?.content ?? null");
        var twitterCard = await page.EvaluateAsync<string>(
            "() => document.querySelector('meta[name=\"twitter:card\"]')?.content ?? null");

        description.ShouldBe(
            "Nova is the tryout platform for club administrators and coaches. Run better tryouts, collaborate on player evaluations, and place players onto teams.");
        canonical.ShouldBe(new Uri(fixture.BaseUri, "/").ToString());
        ogType.ShouldBe("website");
        ogSiteName.ShouldBe("Nova");
        ogTitle.ShouldBe("Nova — Run better tryouts. Build stronger teams.");
        ogUrl.ShouldBe(new Uri(fixture.BaseUri, "/").ToString());
        twitterCard.ShouldBe("summary");
    }

    /// <summary>
    /// LP8: keyboard Tab order reaches the primary actions with visible focus, and the section CTA
    /// controls meet the 24×24 px touch-target minimum.
    /// </summary>
    [Fact]
    public async Task Landing_KeyboardOrderAndTouchTargets_AreAccessible()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());

        var seeHow = page.GetByRole(AriaRole.Link, new() { Name = "Follow the campaign route", Exact = true });
        await InteractionHelpers.TabUntilFocusedAsync(page, seeHow);
        await Expect(seeHow).ToBeFocusedAsync();

        var createClub = page.GetByRole(AriaRole.Link, new() { Name = "Create your club", Exact = true }).First;
        await A11yMeasurementHelpers.AssertTouchTargetAsync(page, seeHow, "Follow the campaign route");
        await A11yMeasurementHelpers.AssertTouchTargetAsync(page, createClub, "Create your club");
    }

    /// <summary>
    /// LP9: the public-header action labels are vertically centered within their 44px touch
    /// targets instead of leaving visibly more space below the text than above it.
    /// </summary>
    [Fact]
    public async Task Landing_HeaderActions_CenterLabelsVertically()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());

        var header = page.Locator(".public-header");
        foreach (var actionName in new[] { "Sign in", "Create your club" })
        {
            var action = header.GetByRole(AriaRole.Link, new() { Name = actionName, Exact = true });
            var verticalInsetDifference = await action.EvaluateAsync<double>(
                """
                (element) => {
                    const elementBounds = element.getBoundingClientRect();
                    const textRange = document.createRange();
                    textRange.selectNodeContents(element);
                    const textBounds = textRange.getBoundingClientRect();
                    const topInset = textBounds.top - elementBounds.top;
                    const bottomInset = elementBounds.bottom - textBounds.bottom;
                    return Math.abs(topInset - bottomInset);
                }
                """);
            verticalInsetDifference.ShouldBeLessThanOrEqualTo(
                1.0,
                $"the {actionName} label must be vertically centered in its button");
        }
    }

    /// <summary>
    /// LP10: the hero copy and primary actions meet the WCAG AA 4.5:1 contrast threshold.
    /// </summary>
    [Fact]
    public async Task Landing_HeroCopyAndActions_MeetContrastThreshold()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await using var context = await fixture.NewAnonymousContextAsync();
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());

        await A11yMeasurementHelpers.AssertContrastRatioAsync(
            page.GetByRole(AriaRole.Heading, new() { Name = "Run better tryouts. Build stronger teams.", Exact = true }), 4.5, "hero heading");
        await A11yMeasurementHelpers.AssertContrastRatioAsync(
            page.GetByRole(AriaRole.Link, new() { Name = "Create your club", Exact = true }).First, 4.5, "Create your club");
        await A11yMeasurementHelpers.AssertContrastRatioAsync(
            page.GetByRole(AriaRole.Link, new() { Name = "Follow the campaign route", Exact = true }), 4.5, "Follow the campaign route");
    }

    /// <summary>
    /// LP11: at wide and narrow viewports the landing page has no horizontal overflow and keeps its
    /// substantive content accessible.
    /// </summary>
    [Fact]
    public async Task Landing_Responsive_NoHorizontalOverflowAcrossViewports()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using (var wideContext = await fixture.NewAnonymousContextAsync(new ViewportSize { Width = 1280, Height = 800 }))
        {
            var page = wideContext.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());
            await AssertNoHorizontalOverflowAsync(page);
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Run better tryouts. Build stronger teams.", Exact = true })).ToBeVisibleAsync();
        }

        await using (var narrowContext = await fixture.NewAnonymousContextAsync(new ViewportSize { Width = 480, Height = 800 }))
        {
            var page = narrowContext.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());
            await AssertNoHorizontalOverflowAsync(page);
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Run better tryouts. Build stronger teams.", Exact = true })).ToBeVisibleAsync();
            // At narrow widths the public header collapses into a toggler, so the hero and final CTA
            // still expose the primary action (2 of 3 visible controls).
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Create your club", Exact = true })).ToHaveCountAsync(2);
        }
    }

    /// <summary>
    /// Captures landing-page accessibility evidence (screenshots) when <c>NOVA_A11Y_SCREENSHOTS=1</c>;
    /// otherwise skips so a green run always means the assertions executed.
    /// </summary>
    [Fact]
    public async Task Landing_A11yEvidence_CapturesScreenshots()
    {
        if (Environment.GetEnvironmentVariable("NOVA_A11Y_SCREENSHOTS") != "1")
        {
            Assert.Skip("Set NOVA_A11Y_SCREENSHOTS=1 to capture landing-page accessibility evidence.");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var outputDirectory = Path.Combine(Path.GetTempPath(), "nova-a11y-screenshots");
        Directory.CreateDirectory(outputDirectory);

        await using (var wideContext = await fixture.NewAnonymousContextAsync(new ViewportSize { Width = 1280, Height = 800 }))
        {
            var page = wideContext.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Run better tryouts. Build stronger teams.", Exact = true })).ToBeVisibleAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "landing-anonymous-wide.png") });
        }

        await using (var narrowContext = await fixture.NewAnonymousContextAsync(new ViewportSize { Width = 480, Height = 800 }))
        {
            var page = narrowContext.Pages[0];
            await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Run better tryouts. Build stronger teams.", Exact = true })).ToBeVisibleAsync();
            await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "landing-anonymous-narrow.png") });
        }
    }

    /// <summary>
    /// Asserts the page body does not overflow horizontally at the current viewport.
    /// </summary>
    /// <param name="page">The page to inspect.</param>
    /// <returns>A task that completes once the overflow assertion passes.</returns>
    private static async Task AssertNoHorizontalOverflowAsync(IPage page)
    {
        var hasOverflow = await page.EvaluateAsync<bool>(
            "() => document.documentElement.scrollWidth > document.documentElement.clientWidth");
        hasOverflow.ShouldBeFalse("the landing page should not overflow horizontally at the current viewport");
    }
}
