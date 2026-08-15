using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level validation of the campaign evaluation workspace cross-slice scenarios:
/// the critical happy path, shared state across two users, restricted commands, the stale
/// close conflict, URL state, cross-page drawer navigation, the duplicate tag race, responsive
/// layouts, archived tag definitions, and keyboard/focus accessibility.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class CampaignEvaluationBrowserTests(BrowserSuiteFixture fixture)
{
    [Fact]
    public async Task Workspace_LoadsCampaignAndRoster_ForApprovedMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];

        await page.GotoAsync(new Uri(fixture.BaseUri, "/campaigns").ToString());
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = seed.CampaignName })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = seed.CampaignName }).ClickAsync();

        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = seed.CampaignName })).ToBeVisibleAsync();
        await Expect(page.Locator("p[aria-live=\"polite\"]")).ToContainTextAsync("60 participants");
        await Expect(page.Locator("tbody tr[id^='roster-row-']").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task Drawer_HappyPath_AddsNoteAndAppliesTag_WithActorMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenWorkspaceAsync(page, seed.CampaignId);

        var firstRow = page.Locator("tbody tr[id^='roster-row-']").First;
        var participantName = (await firstRow.Locator("td").Nth(1).TextContentAsync())!.Trim();
        await OpenParticipantAsync(page, firstRow);

        // Initial focus lands on the drawer close button.
        (await IsFocusedAsync(page, "participant-drawer-close")).ShouldBeTrue();
        await Expect(page.Locator("#participant-drawer-heading")).ToHaveTextAsync(participantName);
        await Expect(page.Locator("#participant-drawer-position")).ToHaveTextAsync("1 of 60");

        // Add a note through the real form.
        await page.GetByRole(AriaRole.Button, new() { Name = "Add note" }).ClickAsync();
        await page.Locator("#participant-drawer-note-content").FillAsync("Fast feet and good vision.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save note" }).ClickAsync();

        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Note added.");
        var noteItem = page.Locator("li.participant-drawer-note").First;
        await Expect(noteItem).ToContainTextAsync("Fast feet and good vision.");
        (await noteItem.Locator(".participant-drawer-note-meta").TextContentAsync())!
            .ShouldContain("Alice Author");

        // Apply a tag through the real form.
        var tagSelect = page.Locator("select[aria-label=\"Tag to apply\"]");
        await tagSelect.SelectOptionAsync(seed.ActiveTagName);
        var applyButton = page.GetByRole(AriaRole.Button, new() { Name = "Apply" });
        await Expect(applyButton).ToBeEnabledAsync();
        await applyButton.ClickAsync();

        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Tag applied.");
        var tagItem = page.Locator("li.participant-drawer-tag-item").First;
        await Expect(tagItem).ToContainTextAsync(seed.ActiveTagName);
        (await tagItem.Locator(".participant-drawer-tag-meta").TextContentAsync())!
            .ShouldContain("by Alice Author");

        // Drawer navigation advances the sequence without losing the drawer.
        await page.Locator("#participant-drawer-next").ClickAsync();
        await Expect(page.Locator("#participant-drawer-position")).ToHaveTextAsync("2 of 60");
    }

    [Fact]
    public async Task SharedState_RefreshesAcrossTwoUsers_WithActorMetadata()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var adminContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password);
        await using var evaluatorContext = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var adminPage = adminContext.Pages[0];
        var evaluatorPage = evaluatorContext.Pages[0];

        // The evaluator opens the participant first; the drawer shows no notes or tags yet.
        await OpenWorkspaceAsync(evaluatorPage, seed.CampaignId);
        var targetAssignmentId = seed.AssignmentIds[1];
        var targetRow = evaluatorPage.Locator($"#roster-row-{targetAssignmentId}");
        await OpenParticipantAsync(evaluatorPage, targetRow);
        await Expect(evaluatorPage.Locator("li.participant-drawer-note")).ToHaveCountAsync(0);
        await Expect(evaluatorPage.Locator("li.participant-drawer-tag-item")).ToHaveCountAsync(0);

        // The administrator adds a note and applies a tag from their own browser session.
        await OpenWorkspaceAsync(adminPage, seed.CampaignId);
        await OpenParticipantAsync(adminPage, adminPage.Locator($"#roster-row-{targetAssignmentId}"));
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Add note" }).ClickAsync();
        await adminPage.Locator("#participant-drawer-note-content").FillAsync("Watches the passing lanes.");
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Save note" }).ClickAsync();
        await Expect(adminPage.Locator("div.alert-success[role=status]")).ToContainTextAsync("Note added.");
        await adminPage.Locator("select[aria-label=\"Tag to apply\"]").SelectOptionAsync(seed.ActiveTagName);
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();
        await Expect(adminPage.Locator("div.alert-success[role=status]")).ToContainTextAsync("Tag applied.");

        // The evaluator refreshes the open workspace and observes the shared state with the
        // administrator's actor metadata.
        await evaluatorPage.ReloadAsync();
        await Expect(evaluatorPage.Locator("#participant-drawer-heading")).ToBeVisibleAsync();
        var noteItem = evaluatorPage.Locator("li.participant-drawer-note").First;
        await Expect(noteItem).ToContainTextAsync("Watches the passing lanes.");
        (await noteItem.Locator(".participant-drawer-note-meta").TextContentAsync())!
            .ShouldContain("Alice Author");
        var tagItem = evaluatorPage.Locator("li.participant-drawer-tag-item").First;
        await Expect(tagItem).ToContainTextAsync(seed.ActiveTagName);
        (await tagItem.Locator(".participant-drawer-tag-meta").TextContentAsync())!
            .ShouldContain("by Alice Author");
    }

    [Fact]
    public async Task RestrictedCommands_AreScopedToAuthorAndAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var adminContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password);
        await using var evaluatorContext = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var adminPage = adminContext.Pages[0];
        var evaluatorPage = evaluatorContext.Pages[0];

        // The administrator authors a note and applies a tag.
        await OpenWorkspaceAsync(adminPage, seed.CampaignId);
        var firstRow = adminPage.Locator("tbody tr[id^='roster-row-']").First;
        var assignmentId = await ReadAssignmentIdAsync(firstRow);
        await OpenParticipantAsync(adminPage, firstRow);
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Add note" }).ClickAsync();
        await adminPage.Locator("#participant-drawer-note-content").FillAsync("Authored observation.");
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Save note" }).ClickAsync();
        await Expect(adminPage.Locator("div.alert-success[role=status]")).ToContainTextAsync("Note added.");
        await adminPage.Locator("select[aria-label=\"Tag to apply\"]").SelectOptionAsync(seed.ActiveTagName);
        await adminPage.GetByRole(AriaRole.Button, new() { Name = "Apply" }).ClickAsync();
        await Expect(adminPage.Locator("div.alert-success[role=status]")).ToContainTextAsync("Tag applied.");

        // The author/admin sees the restricted mutation commands on their own items.
        var adminNoteItem = adminPage.Locator("li.participant-drawer-note").First;
        await Expect(adminNoteItem.GetByRole(AriaRole.Button, new() { Name = "Edit" })).ToBeVisibleAsync();
        await Expect(adminNoteItem.GetByRole(AriaRole.Button, new() { Name = "Delete" })).ToBeVisibleAsync();
        var adminTagItem = adminPage.Locator("li.participant-drawer-tag-item").First;
        await Expect(adminTagItem.GetByRole(AriaRole.Button, new() { Name = "Remove" })).ToBeVisibleAsync();

        // Another approved evaluator cannot invoke the restricted mutations, but retains the
        // add-note and apply-tag commands while the campaign is active.
        await OpenWorkspaceAsync(evaluatorPage, seed.CampaignId);
        await OpenParticipantAsync(evaluatorPage, evaluatorPage.Locator($"#roster-row-{assignmentId}"));
        var evaluatorNoteItem = evaluatorPage.Locator("li.participant-drawer-note").First;
        await Expect(evaluatorNoteItem).ToContainTextAsync("Authored observation.");
        await Expect(evaluatorNoteItem.Locator("button")).ToHaveCountAsync(0);
        var evaluatorTagItem = evaluatorPage.Locator("li.participant-drawer-tag-item").First;
        await Expect(evaluatorTagItem.Locator("button")).ToHaveCountAsync(0);
        await Expect(evaluatorPage.GetByRole(AriaRole.Button, new() { Name = "Add note" })).ToBeVisibleAsync();
        await Expect(evaluatorPage.Locator("select[aria-label=\"Tag to apply\"]")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task StaleClose_RejectsWrite_AndEntersReadOnly_PreservingContext()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var evaluatorContext = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = evaluatorContext.Pages[0];

        await OpenWorkspaceAsync(page, seed.CampaignId);
        var firstRow = page.Locator("tbody tr[id^='roster-row-']").First;
        var participantName = (await firstRow.Locator("td").Nth(1).TextContentAsync())!.Trim();
        await OpenParticipantAsync(page, firstRow);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add note" }).ClickAsync();
        await Expect(page.Locator("#participant-drawer-note-content")).ToBeVisibleAsync();

        // The campaign closes behind the evaluator's open session.
        await fixture.CloseCampaignAsAdminAsync(seed.CampaignId, seed.AdminUserId, seed.ClubId, cancellationToken);

        // The stale write is rejected and the drawer heals into read-only mode.
        await page.Locator("#participant-drawer-note-content").FillAsync("Stale write attempt.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save note" }).ClickAsync();
        await Expect(page.Locator(".participant-drawer-mutation-error")).ToBeVisibleAsync();
        await Expect(page.Locator(".participant-drawer-readonly-note")).ToContainTextAsync("Read-only — campaign is closed.");

        // Mutation controls are gone, but the roster/drawer context is preserved.
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add note" })).ToBeHiddenAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save note" })).ToBeHiddenAsync();
        await Expect(page.Locator("select[aria-label=\"Tag to apply\"]")).ToBeHiddenAsync();
        await Expect(page.Locator("#participant-drawer-heading")).ToHaveTextAsync(participantName);
        await Expect(page.Locator("#participant-drawer-position")).ToHaveTextAsync("1 of 60");
        await Expect(page.Locator("tbody tr[id^='roster-row-']").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task UrlState_SurvivesReload_AndBackForward_RestoresDrawer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenWorkspaceAsync(page, seed.CampaignId);

        // Open a participant first: a successful drawer click proves the interactive circuit is
        // attached, so the filter interactions below are never swallowed pre-hydration.
        await OpenParticipantAsync(page, page.Locator("tbody tr[id^='roster-row-']").First);
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("aside.participant-drawer")).ToBeHiddenAsync();

        // Apply a search filter and a sort through the UI; both land in the URL. Blazor performs
        // these navigations client-side (no document load), so wait for URL commit only.
        await page.Locator("#roster-search").FillAsync("Player 47");
        await page.WaitForURLAsync(
            url => url.Contains("search=", StringComparison.Ordinal),
            new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("p[aria-live=\"polite\"]")).ToContainTextAsync("1 participant");
        await page.Locator("button.roster-sort-header", new() { HasText = "Name" }).ClickAsync();
        await page.WaitForURLAsync(
            url => url.Contains("sortBy=", StringComparison.Ordinal),
            new() { WaitUntil = WaitUntilState.Commit });

        // Open the drawer; the participant lands in the URL.
        var row = page.Locator("tbody tr[id^='roster-row-']").First;
        await OpenParticipantAsync(page, row);
        await page.WaitForURLAsync(
            url => url.Contains("participant=", StringComparison.Ordinal),
            new() { WaitUntil = WaitUntilState.Commit });
        var heading = (await page.Locator("#participant-drawer-heading").TextContentAsync())!.Trim();

        // A full reload restores every piece of state.
        await page.ReloadAsync();
        await Expect(page.Locator("#participant-drawer-heading")).ToHaveTextAsync(heading);
        (await page.Locator("#roster-search").InputValueAsync()).ShouldBe("Player 47");
        page.Url.ShouldContain("search=");
        page.Url.ShouldContain("sortBy=");
        page.Url.ShouldContain("participant=");

        // Browser Back closes the drawer; Forward reopens it with the same participant. Blazor
        // handles history entries client-side (no document load fires), so wait for URL commit.
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("aside.participant-drawer")).ToBeHiddenAsync();
        await Expect(page.Locator("#roster-search")).ToHaveValueAsync("Player 47");
        await page.GoForwardAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("#participant-drawer-heading")).ToHaveTextAsync(heading);
    }

    [Fact]
    public async Task DrawerNavigation_CrossesPageBoundary_PreservingSequence()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenWorkspaceAsync(page, seed.CampaignId);

        // From the last participant of page 1, Next crosses onto page 2's first participant.
        var lastRow = page.Locator("tbody tr[id^='roster-row-']").Last;
        var lastRowName = (await lastRow.Locator("td").Nth(1).TextContentAsync())!.Trim();
        await OpenParticipantAsync(page, lastRow);
        await Expect(page.Locator("#participant-drawer-position")).ToHaveTextAsync("50 of 60");

        await page.Locator("#participant-drawer-next").ClickAsync();
        await Expect(page.Locator("#participant-drawer-position")).ToHaveTextAsync("51 of 60");
        await page.WaitForURLAsync(
            url => url.Contains("page=2", StringComparison.Ordinal),
            new() { WaitUntil = WaitUntilState.Commit });
        var firstRowOnPageTwoName = (await page.Locator("tbody tr[id^='roster-row-']").First.Locator("td").Nth(1).TextContentAsync())!.Trim();
        await Expect(page.Locator("#participant-drawer-heading")).ToHaveTextAsync(firstRowOnPageTwoName);

        // Previous crosses back to page 1's last participant. The URL builder omits the default
        // page, so wait for the page=2 parameter to disappear rather than for page=1 to appear.
        await page.Locator("#participant-drawer-previous").ClickAsync();
        await Expect(page.Locator("#participant-drawer-position")).ToHaveTextAsync("50 of 60");
        await page.WaitForURLAsync(
            url => !url.Contains("page=2", StringComparison.Ordinal),
            new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("#participant-drawer-heading")).ToHaveTextAsync(lastRowName);

        // At the true sequence ends the navigation controls disable correctly.
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?page=2&participant={seed.AssignmentIds[^1]}").ToString());
        await Expect(page.Locator("#participant-drawer-position")).ToHaveTextAsync("60 of 60");
        await Expect(page.Locator("#participant-drawer-next")).ToBeDisabledAsync();
        await Expect(page.Locator("#participant-drawer-previous")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task DuplicateTagRace_YieldsSingleChip_AfterRefresh()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var firstContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password);
        await using var secondContext = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var firstPage = firstContext.Pages[0];
        var secondPage = secondContext.Pages[0];

        var targetAssignmentId = seed.AssignmentIds[1];
        await OpenWorkspaceAsync(firstPage, seed.CampaignId);
        await OpenParticipantAsync(firstPage, firstPage.Locator($"#roster-row-{targetAssignmentId}"));
        await OpenWorkspaceAsync(secondPage, seed.CampaignId);
        await OpenParticipantAsync(secondPage, secondPage.Locator($"#roster-row-{targetAssignmentId}"));

        // Both users select the same tag and apply it concurrently.
        await firstPage.Locator("select[aria-label=\"Tag to apply\"]").SelectOptionAsync(seed.ActiveTagName);
        await secondPage.Locator("select[aria-label=\"Tag to apply\"]").SelectOptionAsync(seed.ActiveTagName);
        var firstApply = firstPage.GetByRole(AriaRole.Button, new() { Name = "Apply" });
        var secondApply = secondPage.GetByRole(AriaRole.Button, new() { Name = "Apply" });
        await Task.WhenAll(firstApply.ClickAsync(), secondApply.ClickAsync());
        await WaitForMutationSettlementAsync(firstPage);
        await WaitForMutationSettlementAsync(secondPage);

        // Exactly one session reports success and exactly one reports a clear conflict.
        var successCount = 0;
        var conflictCount = 0;
        foreach (var page in new[] { firstPage, secondPage })
        {
            if (await page.Locator("div.alert-success[role=status]").IsVisibleAsync())
            {
                successCount++;
            }

            if (await page.Locator(".participant-drawer-mutation-error").IsVisibleAsync())
            {
                conflictCount++;
            }
        }

        successCount.ShouldBe(1);
        conflictCount.ShouldBe(1);

        // After a refresh exactly one tag chip renders: no duplicate UI state.
        await firstPage.ReloadAsync();
        await Expect(firstPage.Locator("li.participant-drawer-tag-item")).ToHaveCountAsync(1);

        await using var db = fixture.AppHost.CreateAdminContext();
        var durableRows = await db.CampaignTagApplications
            .Where(candidate => candidate.PlayerCampaignAssignmentId == targetAssignmentId)
            .CountAsync(cancellationToken);
        durableRows.ShouldBe(1);
    }

    [Fact]
    public async Task ResponsiveLayouts_PreserveRosterAndDrawer_AcrossViewports()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);

        // Narrow viewport: the card list replaces the table and the drawer still opens/closes.
        await using var narrowContext = await fixture.NewSignedInContextAsync(
            seed.EvaluatorEmail, EvaluationSeed.Password, new ViewportSize { Width = 480, Height = 800 });
        var narrowPage = narrowContext.Pages[0];
        await OpenWorkspaceAsync(narrowPage, seed.CampaignId);
        await Expect(narrowPage.Locator(".table-responsive")).ToBeHiddenAsync();
        var firstCard = narrowPage.Locator("#roster-card-" + seed.AssignmentIds[0]);
        await Expect(firstCard).ToBeVisibleAsync();
        await OpenParticipantAsync(narrowPage, firstCard);
        await Expect(narrowPage.Locator("aside.participant-drawer")).ToBeVisibleAsync();
        await narrowPage.Keyboard.PressAsync("Escape");
        await Expect(narrowPage.Locator("aside.participant-drawer")).ToBeHiddenAsync();
        (await IsFocusedAsync(narrowPage, "roster-card-" + seed.AssignmentIds[0])).ShouldBeTrue();

        // Tablet viewport: the table is visible and the card list is not.
        await using var tabletContext = await fixture.NewSignedInContextAsync(
            seed.EvaluatorEmail, EvaluationSeed.Password, new ViewportSize { Width = 820, Height = 1024 });
        var tabletPage = tabletContext.Pages[0];
        await OpenWorkspaceAsync(tabletPage, seed.CampaignId);
        await Expect(tabletPage.Locator(".table-responsive")).ToBeVisibleAsync();
        await Expect(tabletPage.Locator("div.d-md-none[aria-label=\"Campaign roster participants\"]")).ToBeHiddenAsync();
        await Expect(tabletPage.Locator("tbody tr[id^='roster-row-']").First).ToBeVisibleAsync();
    }

    [Fact]
    public async Task ArchivedTagDefinition_StaysVisible_ButIsNotApplicableOrRemovable()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenWorkspaceAsync(page, seed.CampaignId);

        // The seeded archived application renders with its indicator and no removal command.
        await OpenParticipantAsync(page, page.Locator($"#roster-row-{seed.ArchivedTagApplicationAssignmentId}"));
        var archivedItem = page.Locator("li.participant-drawer-tag-item").First;
        await Expect(archivedItem).ToContainTextAsync(seed.ArchivedTagName);
        (await archivedItem.Locator(".participant-drawer-tag-meta").TextContentAsync())!.ShouldContain("archived");
        await Expect(archivedItem.Locator(".participant-drawer-tag-archived")).ToBeVisibleAsync();
        await Expect(archivedItem.Locator("button")).ToHaveCountAsync(0);

        // The chip text meets the WCAG AA 4.5:1 contrast threshold against its background.
        var chipContrast = await archivedItem.Locator(".participant-drawer-tag").EvaluateAsync<double>(@"(el) => {
            const parse = c => { const m = c.match(/rgba?\(([\d.]+),\s*([\d.]+),\s*([\d.]+)(?:,\s*([\d.]+))?\)/); return m ? { r: +m[1], g: +m[2], b: +m[3] } : null; };
            const lin = v => { v /= 255; return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4); };
            const lum = c => 0.2126 * lin(c.r) + 0.7152 * lin(c.g) + 0.0722 * lin(c.b);
            const style = getComputedStyle(el);
            const fg = parse(style.color), bg = parse(style.backgroundColor);
            const l1 = lum(fg), l2 = lum(bg);
            return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
        }");
        chipContrast.ShouldBeGreaterThanOrEqualTo(4.5);

        // The apply choices exclude the archived definition and keep the active ones.
        await page.Locator("#participant-drawer-close").ClickAsync();
        await Expect(page.Locator("aside.participant-drawer")).ToBeHiddenAsync();
        await OpenParticipantAsync(page, page.Locator($"#roster-row-{seed.AssignmentIds[1]}"));
        var options = await page.Locator("select[aria-label=\"Tag to apply\"] option").AllTextContentsAsync();
        options.ShouldContain(seed.ActiveTagName);
        options.ShouldContain(seed.SecondActiveTagName);
        options.ShouldNotContain(seed.ArchivedTagName);
    }

    [Fact]
    public async Task Drawer_IsKeyboardAccessible_TrapEscapeAndFocusReturn()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenWorkspaceAsync(page, seed.CampaignId);

        var firstRow = page.Locator("tbody tr[id^='roster-row-']").First;
        var assignmentId = await ReadAssignmentIdAsync(firstRow);
        await OpenParticipantAsync(page, firstRow);

        // Every drawer navigation control meets the WCAG 2.5.8 minimum target size (24×24 CSS px).
        foreach (var controlId in new[] { "participant-drawer-previous", "participant-drawer-next", "participant-drawer-close" })
        {
            var size = await page.Locator($"#{controlId}").EvaluateAsync<double[]>(
                "(el) => { const r = el.getBoundingClientRect(); return [r.width, r.height]; }");
            size[0].ShouldBeGreaterThanOrEqualTo(24, $"touch-target width for {controlId}");
            size[1].ShouldBeGreaterThanOrEqualTo(24, $"touch-target height for {controlId}");
        }

        // Focus starts on the close button and Tab cycles stay inside the dialog.
        (await IsFocusedAsync(page, "participant-drawer-close")).ShouldBeTrue();
        for (var i = 0; i < 12; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            (await IsFocusInsideDrawerAsync(page)).ShouldBeTrue();
        }

        // Escape closes the drawer and returns focus to the activating row.
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("aside.participant-drawer")).ToBeHiddenAsync();
        (await IsFocusedAsync(page, $"roster-row-{assignmentId}")).ShouldBeTrue();
    }

    [Fact]
    public async Task NoteFlow_IsKeyboardOperable_WithLabelsAndStatusAnnouncements()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenWorkspaceAsync(page, seed.CampaignId);

        // The search input carries an accessible label.
        await Expect(page.GetByLabel("Search name or tryout number")).ToBeVisibleAsync();

        var firstRow = page.Locator("tbody tr[id^='roster-row-']").First;
        await OpenParticipantAsync(page, firstRow);

        // Keyboard-only note flow: focus + Enter activation, typed content, Enter to save.
        var addNoteButton = page.GetByRole(AriaRole.Button, new() { Name = "Add note" });
        await addNoteButton.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        var noteContent = page.Locator("#participant-drawer-note-content");
        await Expect(noteContent).ToBeVisibleAsync();
        await noteContent.FillAsync("Typed without a pointer.");
        var saveButton = page.GetByRole(AriaRole.Button, new() { Name = "Save note" });
        await saveButton.FocusAsync();
        await page.Keyboard.PressAsync("Enter");

        // The polite status region announces the result.
        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Note added.");
        await Expect(page.Locator("li.participant-drawer-note").First).ToContainTextAsync("Typed without a pointer.");
    }

    [Fact]
    public async Task A11yManualChecklist_CapturesContrastAndTouchTargetEvidence()
    {
        if (Environment.GetEnvironmentVariable("NOVA_A11Y_SCREENSHOTS") != "1")
        {
            return;
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        var outputDirectory = Path.Combine(Path.GetTempPath(), "nova-a11y-screenshots");
        Directory.CreateDirectory(outputDirectory);
        var measurements = new List<string>();

        // Wide viewport: workspace with the drawer open on the first participant.
        await using (var wideContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password))
        {
            var page = wideContext.Pages[0];
            await OpenWorkspaceAsync(page, seed.CampaignId);
            await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "01-workspace-wide.png") });
            measurements.AddRange(await MeasureAsync(page, "wide-workspace"));
            await OpenParticipantAsync(page, page.Locator("tbody tr[id^='roster-row-']").First);
            await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "02-drawer-wide.png") });
            measurements.AddRange(await MeasureAsync(page, "wide-drawer"));
        }

        // Narrow viewport: card list and the full-screen drawer.
        await using (var narrowContext = await fixture.NewSignedInContextAsync(
            seed.EvaluatorEmail, EvaluationSeed.Password, new ViewportSize { Width = 480, Height = 800 }))
        {
            var page = narrowContext.Pages[0];
            await OpenWorkspaceAsync(page, seed.CampaignId);
            await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "03-workspace-narrow.png") });
            measurements.AddRange(await MeasureAsync(page, "narrow-workspace"));
            await OpenParticipantAsync(page, page.Locator("#roster-card-" + seed.AssignmentIds[0]));
            await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "04-drawer-narrow.png") });
            measurements.AddRange(await MeasureAsync(page, "narrow-drawer"));
        }

        await File.WriteAllLinesAsync(Path.Combine(outputDirectory, "measurements.txt"), measurements, cancellationToken);
    }

    /// <summary>
    /// Measures contrast ratios and touch-target sizes for the manual accessibility checklist
    /// items and returns one line per finding.
    /// </summary>
    private static async Task<IReadOnlyList<string>> MeasureAsync(IPage page, string scope)
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

    private async Task OpenWorkspaceAsync(IPage page, long campaignId)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{campaignId}").ToString());
        await Expect(page.Locator("#roster-region-heading")).ToBeVisibleAsync();
        await Expect(page.Locator("p[aria-live=\"polite\"]")).ToContainTextAsync("60 participants");
    }

    private static async Task OpenParticipantAsync(IPage page, ILocator row)
    {
        // Prerendered rows ignore clicks until the interactive circuit attaches, and the drawer
        // renders only after the Blazor server round-trip completes. Retry until the drawer is
        // actually open; never re-click once it is, or the backdrop intercepts the pointer.
        var drawer = page.Locator("aside.participant-drawer");
        for (var attempt = 0; attempt < 20; attempt++)
        {
            if (await drawer.IsVisibleAsync())
            {
                break;
            }

            try
            {
                await row.ClickAsync(new() { Timeout = 5000 });
            }
            catch (PlaywrightException)
            {
                // A re-render replaced the element mid-click or the backdrop appeared because a
                // previous click already opened the drawer; both settle below.
            }

            try
            {
                await drawer.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 });
                break;
            }
            catch (TimeoutException)
            {
                await page.WaitForTimeoutAsync(250);
            }
        }

        await Expect(drawer).ToBeVisibleAsync();
        await Expect(page.Locator(".participant-drawer-section-title").First).ToBeVisibleAsync();
    }

    private static async Task<long> ReadAssignmentIdAsync(ILocator row)
    {
        var id = await row.GetAttributeAsync("id");
        return long.Parse(id!["roster-row-".Length..]);
    }

    private static async Task<bool> IsFocusedAsync(IPage page, string elementId) =>
        await page.EvaluateAsync<bool>("(id) => document.activeElement?.id === id", elementId);

    private static async Task<bool> IsFocusInsideDrawerAsync(IPage page) =>
        await page.EvaluateAsync<bool>(
            "() => { const d = document.querySelector('aside.participant-drawer'); return d !== null && d.contains(document.activeElement); }");

    private static async Task WaitForMutationSettlementAsync(IPage page)
    {
        for (var attempt = 0; attempt < 20; attempt++)
        {
            var success = await page.Locator("div.alert-success[role=status]").IsVisibleAsync();
            var error = await page.Locator(".participant-drawer-mutation-error").IsVisibleAsync();
            if (success || error)
            {
                return;
            }

            await page.WaitForTimeoutAsync(250);
        }
    }
}
