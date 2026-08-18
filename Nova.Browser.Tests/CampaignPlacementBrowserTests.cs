using Microsoft.Playwright;
using Nova.Shared.Enums;
using Shouldly;
using static Microsoft.Playwright.Assertions;

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
        await AssignOutcomeAsync(page, outcomeSelect, teamSelect);
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
    public async Task Workspace_AppliesGraduationYearFilter_AndComposesWithUnresolvedOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await OpenPlacementsAsync(page, seed.CampaignId);

        await SelectGraduationYearAsync(page, "2028");
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("60 undecided");
        var years = await page.Locator("tbody tr td:nth-child(2)").AllTextContentsAsync();
        years.ShouldNotBeEmpty();
        years.ShouldAllBe(year => year.Trim() == "2028");
        page.Url.ShouldContain("placementGraduationYear=2028");

        await CheckUnresolvedOnlyAsync(page);
        page.Url.ShouldContain("placementGraduationYear=2028");
        page.Url.ShouldContain("unresolvedOnly=true");
        var filteredRows = page.Locator("tbody tr[id^='placement-row-']");
        await Expect(filteredRows).ToHaveCountAsync(50);
        var filteredYears = await filteredRows.Locator("td:nth-child(2)").AllTextContentsAsync();
        filteredYears.ShouldAllBe(year => year.Trim() == "2028");
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("60 undecided");
    }

    [Fact]
    public async Task Workspace_ChangesEverySupportedOutcome_AndUpdatesSummary()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await OpenPlacementsAsync(page, seed.CampaignId);

        await SaveFirstRowAsync(page, PlacementOutcome.Assigned, seed.EligibleTeamId);
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("1 assigned");

        await SaveFirstRowAsync(page, PlacementOutcome.NotSelected, teamId: null, rowIndex: 1);
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("1 not selected");

        await SaveFirstRowAsync(page, PlacementOutcome.Withdrawn, teamId: null, rowIndex: 2);
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("1 withdrawn");
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("57 undecided");
    }

    [Fact]
    public async Task SecondEdit_ReusesReplacementToken_WithoutReload()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await OpenPlacementsAsync(page, seed.CampaignId);

        await SaveFirstRowAsync(page, PlacementOutcome.Assigned, seed.EligibleTeamId);
        await SaveFirstRowAsync(page, PlacementOutcome.Withdrawn, teamId: null);
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("0 assigned");
        await Expect(page.Locator("div.placement-summary[role=status]")).ToContainTextAsync("1 withdrawn");
    }

    [Fact]
    public async Task ConcurrentUpdate_ShowsConflictRecovery_AndReloadShowsWinner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var firstContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        await using var secondContext = await fixture.NewSignedInContextAsync(seed.SecondAdminEmail, PlacementSeed.Password);
        var firstPage = firstContext.Pages[0];
        var secondPage = secondContext.Pages[0];
        await OpenPlacementsAsync(firstPage, seed.CampaignId);
        await OpenPlacementsAsync(secondPage, seed.CampaignId);

        await SaveFirstRowAsync(firstPage, PlacementOutcome.Assigned, seed.EligibleTeamId);

        var secondRow = secondPage.Locator("tbody tr[id^='placement-row-']").First;
        var secondOutcome = secondRow.Locator("select[aria-label^='Outcome for']");
        await secondOutcome.SelectOptionAsync(((int)PlacementOutcome.Withdrawn).ToString());
        var secondSave = secondRow.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        await Expect(secondSave).ToBeVisibleAsync();
        await secondSave.ClickAsync();

        var conflict = secondPage.GetByRole(AriaRole.Alert).Filter(new() { HasText = "placement" }).First;
        await Expect(conflict).ToContainTextAsync("changed by another user");
        await Expect(conflict).ToBeFocusedAsync();
        await Expect(secondRow.Locator("select")).ToHaveCountAsync(2);
        await Expect(secondRow.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true })).ToBeDisabledAsync();

        await secondPage.GetByRole(AriaRole.Button, new() { Name = "Close and reload", Exact = true }).ClickAsync();
        await Expect(secondPage.Locator("tbody tr[id^='placement-row-']").First).ToBeVisibleAsync();
        var winnerSelect = secondPage.Locator("tbody tr[id^='placement-row-']").First.Locator("select[aria-label^='Outcome for']");
        await Expect(winnerSelect).ToHaveValueAsync(((int)PlacementOutcome.Assigned).ToString());
    }

    [Fact]
    public async Task ParticipantNavigation_AndBack_RestoreTabAndFilters()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await OpenPlacementsAsync(page, seed.CampaignId);
        await SelectGraduationYearAsync(page, "2028");
        await CheckUnresolvedOnlyAsync(page);
        await page.GetByRole(AriaRole.Button, new() { Name = "Next", Exact = true }).ClickAsync();
        await Expect(page.Locator("text=Page 2 of 2")).ToBeVisibleAsync();

        var participantLink = page.Locator("tbody tr[id^='placement-row-']").First.GetByRole(AriaRole.Link).First;
        await participantLink.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "← Back to roster", Exact = true })).ToBeVisibleAsync();
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("#placements-region-heading")).ToBeVisibleAsync();
        await Expect(page.Locator("text=Page 2 of 2")).ToBeVisibleAsync();
        page.Url.ShouldContain("tab=placements");
        page.Url.ShouldContain("placementGraduationYear=2028");
        page.Url.ShouldContain("unresolvedOnly=true");
        page.Url.ShouldContain("placementPage=2");
        await Expect(page.Locator("tbody tr[id^='placement-row-']")).ToHaveCountAsync(5);
    }

    [Fact]
    public async Task NarrowViewport_CardsRemainKeyboardOperable_WithLabelsAndAnnouncements()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            PlacementSeed.Password,
            new ViewportSize { Width = 480, Height = 800 });
        var page = context.Pages[0];
        await OpenPlacementsAsync(page, seed.CampaignId);

        await Expect(page.Locator(".campaign-placements-panel .table-responsive")).ToBeHiddenAsync();
        var card = page.Locator("li[id^='placement-card-']").First;
        await Expect(card).ToBeVisibleAsync();
        var playerName = (await card.Locator("a").InnerTextAsync()).Trim();
        var outcome = page.GetByRole(AriaRole.Combobox, new() { Name = $"Outcome for {playerName}" });
        var team = page.GetByRole(AriaRole.Combobox, new() { Name = $"Team for {playerName}" });
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await outcome.FocusAsync();
            await Expect(outcome).ToBeFocusedAsync();
            // Reset to Undecided first so a single ArrowDown deterministically reaches Assigned.
            // Without the reset, a queued change from a previous attempt can move the select past
            // Assigned while the team-enabled check still observes the stale enabled state, and the
            // subsequent change disables the team select again. Retry until the team actually
            // enables so a swallowed prerender change event recovers once the circuit attaches.
            await outcome.SelectOptionAsync("0");
            await page.Keyboard.PressAsync("ArrowDown");
            await page.Keyboard.PressAsync("Enter");
            if (await outcome.InputValueAsync() == "1" && await team.IsEnabledAsync())
            {
                break;
            }

            await page.WaitForTimeoutAsync(250);
        }

        await Expect(outcome).ToHaveValueAsync("1");
        await Expect(team).ToBeEnabledAsync();
        await team.FocusAsync();
        await Expect(team).ToBeFocusedAsync();
        for (var attempt = 0; attempt < 20; attempt++)
        {
            await page.Keyboard.PressAsync("ArrowDown");
            await page.Keyboard.PressAsync("Tab");
            if (!string.IsNullOrEmpty(await team.InputValueAsync()))
            {
                break;
            }

            await page.WaitForTimeoutAsync(250);
            await team.FocusAsync();
        }
        (await team.InputValueAsync()).ShouldNotBeNullOrEmpty();

        var save = card.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        await Expect(save).ToBeVisibleAsync();
        await save.FocusAsync();
        await Expect(save).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Placement saved.");

        var controlCount = await card.Locator(".placement-row-control").CountAsync();
        for (var index = 0; index < controlCount; index++)
        {
            var size = await card.Locator(".placement-row-control").Nth(index).EvaluateAsync<double[]>(
                "(el) => { const r = el.getBoundingClientRect(); return [r.width, r.height]; }");
            size[0].ShouldBeGreaterThanOrEqualTo(24);
            size[1].ShouldBeGreaterThanOrEqualTo(24);
        }

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

    private static async Task SelectGraduationYearAsync(IPage page, string year)
    {
        var select = page.Locator("#placement-graduation-year");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                if (year != "2031")
                {
                    await select.SelectOptionAsync("2031");
                }

                await select.SelectOptionAsync(year);
                if (page.Url.Contains($"placementGraduationYear={year}", StringComparison.Ordinal))
                {
                    return;
                }

                await page.WaitForTimeoutAsync(500);
                if (page.Url.Contains($"placementGraduationYear={year}", StringComparison.Ordinal))
                {
                    return;
                }
            }
            catch (PlaywrightException)
            {
                await page.WaitForTimeoutAsync(250);
            }
        }

        throw new TimeoutException($"The graduation-year filter did not hydrate for {year}.");
    }

    private static async Task SaveFirstRowAsync(
        IPage page,
        PlacementOutcome outcome,
        long? teamId,
        int rowIndex = 0)
    {
        var row = page.Locator("tbody tr[id^='placement-row-']").Nth(rowIndex);
        await Expect(row).ToBeVisibleAsync();
        var outcomeSelect = row.Locator("select[aria-label^='Outcome for']");
        var outcomeValue = ((int)outcome).ToString();
        var teamSelect = row.Locator("select[aria-label^='Team for']");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            try
            {
                await outcomeSelect.SelectOptionAsync("0");
                await outcomeSelect.SelectOptionAsync(outcomeValue);
                await Expect(outcomeSelect).ToHaveValueAsync(outcomeValue, new() { Timeout = 1500 });
                if (outcome != PlacementOutcome.Assigned)
                {
                    break;
                }

                await Expect(teamSelect).ToBeEnabledAsync(new() { Timeout = 1500 });
                break;
            }
            catch (PlaywrightException)
            {
                await page.WaitForTimeoutAsync(250);
            }
        }

        if (outcome == PlacementOutcome.Assigned)
        {
            await teamSelect.SelectOptionAsync(teamId!.Value.ToString());
        }

        var save = row.GetByRole(AriaRole.Button, new() { Name = "Save", Exact = true });
        await Expect(save).ToBeVisibleAsync();
        await save.ClickAsync();
        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Placement saved.");
    }

    private static async Task AssignOutcomeAsync(IPage page, ILocator outcomeSelect, ILocator teamSelect)
    {
        // Prerendered row controls swallow change events until the interactive circuit re-attaches
        // them after the roster reload. Select a distinct outcome (Assigned) and retry until the
        // team select actually becomes enabled, never assuming a single select landed.
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await teamSelect.IsEnabledAsync())
            {
                return;
            }

            try
            {
                // Force a value change on every attempt so the change event always fires, even if a
                // previous swallowed select left the DOM value already at "1".
                await outcomeSelect.SelectOptionAsync("0");
                await outcomeSelect.SelectOptionAsync("1");
            }
            catch (PlaywrightException)
            {
                // The select was replaced mid-interaction or the row is still hydrating; retry.
            }

            try
            {
                await Expect(teamSelect).ToBeEnabledAsync(new() { Timeout = 1500 });
                return;
            }
            catch (PlaywrightException)
            {
                await page.WaitForTimeoutAsync(250);
            }
        }

        throw new TimeoutException("The team select did not become enabled after assigning the outcome.");
    }
}
