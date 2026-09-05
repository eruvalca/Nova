using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Http;
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
        await Expect(page.Locator("#participant-drawer-close")).ToBeFocusedAsync();
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
        await CloseDrawerAsync(page);

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
        await CloseDrawerAsync(narrowPage);
        await Expect(narrowPage.Locator("#roster-card-" + seed.AssignmentIds[0])).ToBeFocusedAsync();

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
        await A11yMeasurementHelpers.AssertContrastRatioAsync(
            archivedItem.Locator(".participant-drawer-tag"),
            4.5,
            "archived tag chip text");

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
        await Expect(page.Locator("#participant-drawer-close")).ToBeFocusedAsync();
        for (var i = 0; i < 12; i++)
        {
            await page.Keyboard.PressAsync("Tab");
            (await IsFocusInsideDrawerAsync(page)).ShouldBeTrue();
        }

        // Escape closes the drawer and returns focus to the activating row.
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator("aside.participant-drawer")).ToBeHiddenAsync();
        await Expect(page.Locator($"#roster-row-{assignmentId}")).ToBeFocusedAsync();
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
            Assert.Skip("Set NOVA_A11Y_SCREENSHOTS=1 to capture accessibility evidence.");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        var outputDirectory = BrowserTestArtifacts.ForCurrentTest("screenshots");
        Directory.CreateDirectory(outputDirectory);
        var measurements = new List<string>();

        // Wide viewport: workspace with the drawer open on the first participant.
        await using (var wideContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password))
        {
            var page = wideContext.Pages[0];
            await OpenWorkspaceAsync(page, seed.CampaignId);
            await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "01-workspace-wide.png") });
            measurements.AddRange(await A11yMeasurementHelpers.MeasureChecklistAsync(page, "wide-workspace"));
            await OpenParticipantAsync(page, page.Locator("tbody tr[id^='roster-row-']").First);
            await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "02-drawer-wide.png") });
            measurements.AddRange(await A11yMeasurementHelpers.MeasureChecklistAsync(page, "wide-drawer"));
        }

        // Narrow viewport: card list and the full-screen drawer.
        await using (var narrowContext = await fixture.NewSignedInContextAsync(
            seed.EvaluatorEmail, EvaluationSeed.Password, new ViewportSize { Width = 480, Height = 800 }))
        {
            var page = narrowContext.Pages[0];
            await OpenWorkspaceAsync(page, seed.CampaignId);
            await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "03-workspace-narrow.png") });
            measurements.AddRange(await A11yMeasurementHelpers.MeasureChecklistAsync(page, "narrow-workspace"));
            await OpenParticipantAsync(page, page.Locator("#roster-card-" + seed.AssignmentIds[0]));
            await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "04-drawer-narrow.png") });
            measurements.AddRange(await A11yMeasurementHelpers.MeasureChecklistAsync(page, "narrow-drawer"));
        }

        await File.WriteAllLinesAsync(Path.Combine(outputDirectory, "measurements.txt"), measurements, cancellationToken);
    }

    [Fact]
    public async Task Roster_EmptySearch_ShowsNoResults_WithZeroCountAnnouncement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenWorkspaceAsync(page, seed.CampaignId);

        await OpenParticipantAsync(page, page.Locator("tbody tr[id^='roster-row-']").First);
        await CloseDrawerAsync(page);

        await page.Locator("#roster-search").FillAsync("Nobody McMissing");

        await Expect(page.Locator("p[aria-live=\"polite\"]")).ToContainTextAsync("0 participants");
        await Expect(page.GetByText("No participants match the current filters.")).ToBeVisibleAsync();
        await Expect(page.Locator("tbody tr[id^='roster-row-']")).ToHaveCountAsync(0);
        await Expect(page.Locator("[id^='roster-card-']")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Drawer_NoteValidation_RejectsWhitespaceContent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenWorkspaceAsync(page, seed.CampaignId);

        await OpenParticipantAsync(page, page.Locator("tbody tr[id^='roster-row-']").First);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add note" }).ClickAsync();
        await Expect(page.Locator("#participant-drawer-note-content")).ToBeVisibleAsync();
        await page.Locator("#participant-drawer-note-content").FillAsync("   ");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save note" }).ClickAsync();

        // The inline validation error renders, no success alert appears, and the note list is unchanged.
        await Expect(page.Locator("#participant-drawer-note-content")).ToHaveClassAsync(new Regex("is-invalid"));
        await Expect(page.Locator("div.alert-success[role=status]")).ToHaveCountAsync(0);
        await Expect(page.Locator("li.participant-drawer-note")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Roster_AssignedOutcomeBadge_MeetsContrastThreshold()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);

        // Assign the first participant to a team so their roster outcome badge renders "Assigned"
        // (the text-bg-success surface). The roster query does not re-validate eligibility.
        var suffix = Guid.NewGuid().ToString("N");
        var teamId = await SeedingHelpers.InsertTeamAsync(
            fixture.AppHost, seed.ClubId, seed.AdminEmail, $"Assigned Team {suffix}", 2030, cancellationToken);
        await SeedingHelpers.AssignPlacementAsync(fixture.AppHost, seed.AssignmentIds[0], teamId, cancellationToken);

        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenWorkspaceAsync(page, seed.CampaignId);

        var badge = page.Locator($"#roster-row-{seed.AssignmentIds[0]} span.badge.text-bg-success");
        await Expect(badge).ToHaveTextAsync("Assigned");
        await A11yMeasurementHelpers.AssertContrastRatioAsync(badge, 4.5, "roster assigned outcome badge");
    }

    [Fact]
    public async Task Roster_Loading_ShowsIndicator_ThenRendersRows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenWorkspaceAsync(page, seed.CampaignId);

        // Switch the page to WebAssembly so the roster list load becomes a browser /api/... fetch.
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page);
        await OpenWorkspaceAsync(page, seed.CampaignId);

        // Prove hydration before driving the filter, so the search below is never swallowed.
        await OpenParticipantAsync(page, page.Locator("tbody tr[id^='roster-row-']").First);
        await CloseDrawerAsync(page);

        // Hold the roster list fetch open while the loading state is asserted, then release it.
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var intercepted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(
            IsRosterListUrl,
            async route =>
            {
                intercepted.TrySetResult(null);
                await release.Task;
                await route.ContinueAsync();
            });

        await page.Locator("#roster-search").FillAsync("Player 47");
        await intercepted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        await Expect(page.GetByText("Loading roster...")).ToBeVisibleAsync();

        release.TrySetResult(null);
        await Expect(page.Locator("p[aria-live=\"polite\"]")).ToContainTextAsync("1 participant");
        await Expect(page.Locator("tbody tr[id^='roster-row-']")).ToHaveCountAsync(1);
        await page.UnrouteAsync(IsRosterListUrl);
    }

    [Fact]
    public async Task Roster_Failure_ShowsRetry_AndRetryRecovers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenWorkspaceAsync(page, seed.CampaignId);
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page);
        await OpenWorkspaceAsync(page, seed.CampaignId);

        // Prove hydration before driving the filter.
        await OpenParticipantAsync(page, page.Locator("tbody tr[id^='roster-row-']").First);
        await CloseDrawerAsync(page);

        await page.RouteAsync(IsRosterListUrl, route => route.FulfillAsync(new() { Status = 500 }));

        await page.Locator("#roster-search").FillAsync("Player 47");
        var errorAlert = page.Locator("div.alert-danger[role=alert]");
        await Expect(errorAlert).ToContainTextAsync("Failed to load the roster");
        var retry = errorAlert.GetByRole(AriaRole.Button, new() { Name = "Retry" });
        await Expect(retry).ToBeVisibleAsync();

        await page.UnrouteAsync(IsRosterListUrl);
        await retry.ClickAsync();

        await Expect(page.Locator("p[aria-live=\"polite\"]")).ToContainTextAsync("1 participant");
        await Expect(page.Locator("tbody tr[id^='roster-row-']")).ToHaveCountAsync(1);
        await Expect(page.Locator("div.alert-danger[role=alert]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task Drawer_DetailFailure_ShowsRetry_AndRetryRecovers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenWorkspaceAsync(page, seed.CampaignId);
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page);
        await OpenWorkspaceAsync(page, seed.CampaignId);

        // Intercept only the drawer's participant-detail fetch.
        await page.RouteAsync(IsParticipantDetailUrl, route => route.FulfillAsync(new() { Status = 500 }));

        await OpenDrawerAsync(page, page.Locator("tbody tr[id^='roster-row-']").First);
        await Expect(page.Locator("div.alert-danger[role=alert]")).ToContainTextAsync("Failed to load participant details");
        var retry = page.Locator("#participant-drawer-retry");
        await Expect(retry).ToBeVisibleAsync();

        await page.UnrouteAsync(IsParticipantDetailUrl);
        await retry.ClickAsync();

        await Expect(page.Locator(".participant-drawer-section-title").First).ToBeVisibleAsync();
        await Expect(page.Locator("div.alert-danger[role=alert]")).ToHaveCountAsync(0);
    }

    /// <summary>Matches the campaign roster list fetch, excluding detail and graduation-years fetches.</summary>
    private static bool IsRosterListUrl(string url) =>
        url.Contains("/api/campaigns/", StringComparison.Ordinal)
        && url.Contains("/participants", StringComparison.Ordinal)
        && !url.Contains("/participants/", StringComparison.Ordinal);

    /// <summary>Matches the campaign participant-detail fetch, excluding the graduation-years fetch.</summary>
    private static bool IsParticipantDetailUrl(string url) =>
        url.Contains("/api/campaigns/", StringComparison.Ordinal)
        && url.Contains("/participants/", StringComparison.Ordinal)
        && !url.Contains("/graduation-years", StringComparison.Ordinal);

    private async Task OpenWorkspaceAsync(IPage page, long campaignId)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{campaignId}").ToString());
        await Expect(page.Locator("#roster-region-heading")).ToBeVisibleAsync();
        await Expect(page.Locator("p[aria-live=\"polite\"]")).ToContainTextAsync("60 participants");
    }

    private static async Task OpenParticipantAsync(IPage page, ILocator row)
    {
        await OpenDrawerAsync(page, row);
        await Expect(page.Locator(".participant-drawer-section-title").First).ToBeVisibleAsync();
    }

    /// <summary>
    /// Opens the participant drawer by clicking the row, retrying through the SSR hydration window,
    /// and waits for the drawer shell to be visible (without requiring a successful detail load).
    /// </summary>
    private static async Task OpenDrawerAsync(IPage page, ILocator row)
    {
        // Prerendered rows ignore clicks until the interactive circuit attaches, and the drawer
        // renders only after the Blazor server round-trip completes. Retry until the drawer is
        // actually open; never re-click once it is, or the backdrop intercepts the pointer.
        var drawer = page.Locator("aside.participant-drawer");
        for (var attempt = 0; attempt < BrowserRetryPolicy.MaxAttempts; attempt++)
        {
            if (await drawer.IsVisibleAsync())
            {
                break;
            }

            try
            {
                await row.ClickAsync(new() { Timeout = 5000 });
            }
            catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
            {
                // A re-render replaced the element mid-click, the backdrop appeared because a
                // previous click already opened the drawer, or the click actionability timeout
                // surfaced as System.TimeoutException; both settle below.
            }

            try
            {
                await drawer.WaitForAsync(new() { State = WaitForSelectorState.Visible, Timeout = 3000 });
                break;
            }
            catch (TimeoutException)
            {
                await page.WaitForTimeoutAsync(BrowserRetryPolicy.Delay);
            }
        }

        await Expect(drawer).ToBeVisibleAsync();
    }

    /// <summary>
    /// Closes the participant drawer by pressing Escape, retrying through the SSR hydration window until
    /// the drawer is actually hidden. Symmetric with <see cref="OpenDrawerAsync"/>: the drawer's Escape
    /// handler may not be attached for a moment after the drawer opens, so a single early Escape can be
    /// swallowed; retry until the drawer closes rather than assuming one press is enough.
    /// </summary>
    /// <param name="page">The page whose drawer should be closed.</param>
    /// <returns>A task that completes once the drawer is hidden.</returns>
    private static async Task CloseDrawerAsync(IPage page)
    {
        var drawer = page.Locator("aside.participant-drawer");
        for (var attempt = 0; attempt < BrowserRetryPolicy.MaxAttempts; attempt++)
        {
            if (await drawer.IsHiddenAsync())
            {
                return;
            }

            await page.Keyboard.PressAsync("Escape");

            try
            {
                await drawer.WaitForAsync(new() { State = WaitForSelectorState.Hidden, Timeout = 3000 });
                return;
            }
            catch (TimeoutException)
            {
                await page.WaitForTimeoutAsync(BrowserRetryPolicy.Delay);
            }
        }

        await Expect(drawer).ToBeHiddenAsync();
    }

    private static async Task<long> ReadAssignmentIdAsync(ILocator row)
    {
        var id = await row.GetAttributeAsync("id");
        return long.Parse(id!["roster-row-".Length..]);
    }

    private static async Task<bool> IsFocusInsideDrawerAsync(IPage page) =>
        await page.EvaluateAsync<bool>(
            "() => { const d = document.querySelector('aside.participant-drawer'); return d !== null && d.contains(document.activeElement); }");

    private static async Task WaitForMutationSettlementAsync(IPage page)
    {
        for (var attempt = 0; attempt < BrowserRetryPolicy.MaxAttempts; attempt++)
        {
            var success = await page.Locator("div.alert-success[role=status]").IsVisibleAsync();
            var error = await page.Locator(".participant-drawer-mutation-error").IsVisibleAsync();
            if (success || error)
            {
                return;
            }

            await page.WaitForTimeoutAsync(BrowserRetryPolicy.Delay);
        }

        throw new TimeoutException(
            "Tag-apply race did not settle within the retry window: neither alert-success nor mutation-error appeared.");
    }
}
