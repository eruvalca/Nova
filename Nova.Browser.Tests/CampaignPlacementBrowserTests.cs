using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level validation of the campaign placements workspace: the primary administrator
/// assignment workflow, read-only views for approved non-administrators and closed campaigns, and
/// the URL/history round-trip plus touch-target accessibility of the row controls.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class CampaignPlacementBrowserTests(BrowserSuiteFixture fixture)
{
    [Fact]
    public async Task Workspace_AssignsEligibleTeam_SavesRefreshesSummary_AndRemovesRowFromUnresolved()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await OpenPlacementsAsync(page, seed.CampaignId);

        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("0 assigned");
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("60 undecided");

        // Toggling the unresolved-only filter drives a Blazor URL navigation, proving hydration.
        await CheckUnresolvedOnlyAsync(page);
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("60 undecided");

        var firstRow = page.Locator("tbody tr[id^='placement-row-']").First;
        await Expect(firstRow).ToBeVisibleAsync();
        var firstRowId = await firstRow.GetAttributeAsync("id");

        var outcomeSelect = firstRow.Locator("select[aria-label^='Outcome for']");
        var teamSelect = firstRow.Locator("select[aria-label^='Team for']");
        await outcomeSelect.SelectOptionAsync("1");
        await Expect(teamSelect).ToBeEnabledAsync();
        await teamSelect.SelectOptionAsync(seed.EligibleTeamId.ToString());

        await firstRow.GetByRole(AriaRole.Button, new() { Name = "Save" }).ClickAsync();

        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Placement saved.");
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("1 assigned");
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("59 undecided");
        await Expect(page.Locator($"#{firstRowId}")).ToHaveCountAsync(0);

        // The exercised row controls meet the WCAG 2.5.8 minimum target size (24×24 CSS px).
        foreach (var selector in new[] { "select[aria-label^='Outcome for']", "select[aria-label^='Team for']" })
        {
            var size = await page.Locator(selector).First.EvaluateAsync<double[]>(
                "(el) => { const r = el.getBoundingClientRect(); return [r.width, r.height]; }");
            size[0].ShouldBeGreaterThanOrEqualTo(24, $"touch-target width for {selector}");
            size[1].ShouldBeGreaterThanOrEqualTo(24, $"touch-target height for {selector}");
        }

        // A full reload restores the tab and the unresolved-only filter.
        await page.ReloadAsync();
        await Expect(page.Locator("#placements-region-heading")).ToBeVisibleAsync();
        await Expect(page.Locator("#placement-unresolved-only")).ToBeCheckedAsync();
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("1 assigned");
    }

    [Fact]
    public async Task PlacementsTab_RendersReadOnly_ForApprovedNonAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await OpenPlacementsAsync(page, seed.CampaignId);

        await Expect(page.Locator(".campaign-placements-panel")).ToContainTextAsync("Read-only");
        await Expect(page.Locator("select[aria-label^='Outcome for']")).ToHaveCountAsync(0);
        await Expect(page.Locator("select[aria-label^='Team for']")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task ClosedCampaign_ShowsFrozenBanner_AndStaticRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await OpenPlacementsAsync(page, seed.ClosedCampaignId);

        await Expect(page.Locator(".campaign-placements-panel")).ToContainTextAsync("Placements are frozen.");
        await Expect(page.Locator("select[aria-label^='Outcome for']")).ToHaveCountAsync(0);
        await Expect(page.Locator("select[aria-label^='Team for']")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save" })).ToHaveCountAsync(0);
    }

    private async Task OpenPlacementsAsync(IPage page, long campaignId)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{campaignId}?tab=placements").ToString());
        await Expect(page.Locator("#placements-region-heading")).ToBeVisibleAsync();
        await Expect(page.Locator("div.placement-summary[role=status]")).ToBeVisibleAsync();
    }

    private static async Task CheckUnresolvedOnlyAsync(IPage page)
    {
        var checkbox = page.Locator("#placement-unresolved-only");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (page.Url.Contains("unresolvedOnly=true", StringComparison.Ordinal))
            {
                return;
            }

            try
            {
                await checkbox.ClickAsync(new() { Timeout = 3000 });
            }
            catch (PlaywrightException)
            {
                // The checkbox was replaced mid-interaction or the circuit re-rendered; retry.
            }

            try
            {
                await page.WaitForURLAsync(
                    url => url.Contains("unresolvedOnly=true", StringComparison.Ordinal),
                    new() { WaitUntil = WaitUntilState.Commit, Timeout = 3000 });
                return;
            }
            catch (TimeoutException)
            {
                await page.WaitForTimeoutAsync(250);
            }
        }

        throw new TimeoutException("The unresolved-only checkbox did not hydrate within 5s.");
    }
}
