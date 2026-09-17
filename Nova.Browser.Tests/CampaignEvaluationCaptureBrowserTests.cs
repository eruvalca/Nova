using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>Real Evaluate capture, independent navigation, authorship, and durable receipt recovery.</summary>
/// <param name="fixture">The shared Aspire and Chromium fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed partial class CampaignEvaluationCaptureBrowserTests(BrowserSuiteFixture fixture)
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UnreadableCaptureCanLeaveExplicitlyWithoutErasingRecoveryDataAsync(bool unavailable, bool wasm)
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenEvaluationAsync(page, seed.CampaignId, seed.AssignmentIds[1]);
        await EvaluationInteractionHelpers.AssertComposerAttachedAsync(page);
        var key = $"nova:evaluation:v1:{seed.EvaluatorUserId}:{seed.ClubId}:{seed.CampaignId}:{seed.AssignmentIds[1]}";
        const string Retained = "{unreadable original recovery bytes";
        await page.EvaluateAsync("([key, value]) => sessionStorage.setItem(key, value)", new[] { key, Retained });
        if (unavailable)
        {
            await page.AddInitScriptAsync($"const originalGet = Storage.prototype.getItem; Storage.prototype.getItem = function(key) {{ if (key === {JsonSerializer.Serialize(key)}) throw new Error('Storage blocked'); return originalGet.call(this, key); }};");
        }
        if (wasm) { await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, () => EvaluationInteractionHelpers.AssertRecoveryBlockedAttachedAsync(page)); }
        else
        {
            await page.ReloadAsync();
            await EvaluationInteractionHelpers.AssertRecoveryBlockedAttachedAsync(page);
        }
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry storage", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator(".evaluation-save")).ToBeDisabledAsync();
        var evaluationUrl = page.Url;
        var back = page.GetByRole(AriaRole.Link, new() { Name = "Back to campaigns", Exact = true });
        await page.Locator("#evaluation-search").FillAsync("60");
        await page.Locator("#evaluation-search").PressAsync("Enter");
        await page.GetByRole(AriaRole.Button, new() { Name = "Keep working", Exact = true }).ClickAsync();
        page.Url.ShouldBe(evaluationUrl);
        await back.ClickAsync();
        await Expect(page.Locator(".evaluation-protection")).ToContainTextAsync("outcome may be unknown");
        await page.GetByRole(AriaRole.Button, new() { Name = "Leave and keep recovery data", Exact = true }).ClickAsync();
        await Expect(page).ToHaveURLAsync(new Uri(fixture.BaseUri, "/campaigns").ToString());
        (await page.EvaluateAsync<string>("key => sessionStorage[key]", key)).ShouldBe(Retained);
        await page.GoBackAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry storage", Exact = true })).ToBeVisibleAsync();
        (await page.EvaluateAsync<string>("key => sessionStorage[key]", key)).ShouldBe(Retained);
        await using var database = fixture.AppHost.CreateAdminContext();
        (await database.Notes.CountAsync(note => note.PlayerCampaignAssignmentId == seed.AssignmentIds[1], TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task PhoneLookupRequiresSelectionAndRepeatedCaptureKeepsEachPlayerOpenAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password, new() { Width = 390, Height = 844 });
        var page = context.Pages[0];
        await OpenEvaluationAsync(page, seed.CampaignId);
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Find a player", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator(".evaluation-sheet")).ToHaveCountAsync(0);
        await SearchAsync(page, "60");
        await Expect(page.Locator("a[data-eval-result]")).ToHaveCountAsync(1);
        await Expect(page.Locator(".evaluation-sheet")).ToHaveCountAsync(0);
        await page.Locator("a[data-eval-result]").PressAsync("Enter");
        await Expect(page.Locator("#evaluation-player-heading")).ToContainTextAsync("#60 ");
        await Expect(page.Locator(".evaluation-save")).ToBeEnabledAsync();
        await Expect(page.Locator("#evaluation-player-heading")).ToBeFocusedAsync();
        await page.Locator("#evaluation-note").FillAsync("First player capture.");
        await Expect(page.Locator(".evaluation-note-item")).ToHaveCountAsync(0);
        await page.Locator(".evaluation-save").ClickAsync();
        await Expect(page.Locator(".evaluation-status")).ToHaveTextAsync("Note saved.");
        await Expect(page.Locator("#evaluation-player-heading")).ToContainTextAsync("#60 ");
        await Expect(page.Locator(".evaluation-note-item")).ToContainTextAsync("First player capture.");
        await VerifyAndCaptureOwnNoteControlsAsync(page);
        foreach (var selector in new[] { ".evaluation-save", ".evaluation-another", ".evaluation-place" })
        {
            var bounds = await page.Locator(selector).BoundingBoxAsync();
            bounds.ShouldNotBeNull();
            bounds.Height.ShouldBeGreaterThanOrEqualTo(44);
            bounds.Width.ShouldBeGreaterThanOrEqualTo(44);
        }
        await page.Locator(".evaluation-another").ClickAsync();
        await Expect(page.Locator("#evaluation-search")).ToHaveValueAsync(string.Empty);
        await Expect(page.Locator("#evaluation-search")).ToBeFocusedAsync();
        await SearchAsync(page, "59");
        await page.Locator($"a[data-eval-result][href*=\"evalParticipant={seed.AssignmentIds[58]}\"]").PressAsync("Enter");
        await Expect(page.Locator("#evaluation-player-heading")).ToContainTextAsync("#59 ");
        await Expect(page.Locator(".evaluation-save")).ToBeEnabledAsync();
        await page.Locator("#evaluation-note").FillAsync("Second player capture.");
        await page.Locator(".evaluation-save").ClickAsync();
        await Expect(page.Locator(".evaluation-note-item")).ToContainTextAsync("Second player capture.");
        await Expect(page.Locator(".evaluation-note-item")).Not.ToContainTextAsync("First player capture.");
        await page.SetViewportSizeAsync(844, 390);
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= window.innerWidth")).ShouldBeTrue();
    }

    [Fact]
    public async Task AdministratorCannotEditOrDeleteAnotherAuthorsSharedNoteAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var evaluator = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        await using var administrator = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password);
        var writer = evaluator.Pages[0];
        var admin = administrator.Pages[0];
        await OpenEvaluationAsync(writer, seed.CampaignId, seed.AssignmentIds[1]);
        await Expect(writer.Locator(".evaluation-save")).ToBeEnabledAsync();
        await writer.Locator("#evaluation-note").FillAsync("Member authored evidence.");
        await writer.Locator(".evaluation-save").ClickAsync();
        await Expect(writer.Locator(".evaluation-note-item")).ToContainTextAsync("Member authored evidence.");
        await Expect(writer.Locator(".evaluation-note-item").GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true })).ToBeVisibleAsync();
        await OpenEvaluationAsync(admin, seed.CampaignId, seed.AssignmentIds[1]);
        var memberNote = admin.Locator(".evaluation-note-item").Filter(new() { HasText = "Member authored evidence." });
        await Expect(memberNote).ToContainTextAsync("Bob Observer");
        await Expect(memberNote.Locator("button")).ToHaveCountAsync(0);
        await Expect(admin.Locator(".evaluation-save")).ToBeEnabledAsync();
        await admin.Locator("#evaluation-note").FillAsync("Administrator authored evidence.");
        await admin.Locator(".evaluation-save").ClickAsync();
        var ownNote = admin.Locator(".evaluation-note-item").Filter(new() { HasText = "Administrator authored evidence." });
        await Expect(ownNote.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true })).ToBeVisibleAsync();
        await Expect(ownNote.GetByRole(AriaRole.Button, new() { Name = "Delete", Exact = true })).ToBeVisibleAsync();
        await writer.ReloadAsync();
        var othersNote = writer.Locator(".evaluation-note-item").Filter(new() { HasText = "Administrator authored evidence." });
        await Expect(othersNote).ToContainTextAsync("Alice Author");
        await Expect(othersNote.Locator("button")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task LostHttpResponseReloadReplaysOriginalReceiptWithoutDuplicatingNoteAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenEvaluationAsync(page, seed.CampaignId, seed.AssignmentIds[1], search: "Player");
        await EvaluationInteractionHelpers.AssertComposerAttachedAsync(page);
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, () => EvaluationInteractionHelpers.AssertComposerAttachedAsync(page));
        await Expect(page.Locator(".evaluation-save")).ToBeEnabledAsync();
        var mutationUrl = new Uri(fixture.BaseUri, CampaignEndpoints.AddEvaluationNote).ToString();
        string? originalPayload = null;
        var intercepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(mutationUrl, async route =>
        {
            if (!string.Equals(route.Request.Method, "POST", StringComparison.Ordinal)) { await route.ContinueAsync(); return; }
            originalPayload = route.Request.PostData;
            var committed = await route.FetchAsync();
            committed.Status.ShouldBe(201);
            await route.AbortAsync("failed");
            intercepted.TrySetResult();
        });
        await page.Locator("#evaluation-note").FillAsync("Recover original transport receipt.");
        await page.Locator(".evaluation-save").ClickAsync();
        await intercepted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
        await Expect(page.Locator(".evaluation-capture-error")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry original operation", Exact = true })).ToBeEnabledAsync();
        await AssertPendingOperationBlocksPlayerLinkAsync(page, seed.AssignmentIds[0]);
        await page.UnrouteAsync(mutationUrl);
        page.Dialog += async (_, dialog) => await dialog.AcceptAsync();
        await ReloadRetainedCaptureAsync(page);
        await Expect(page.Locator(".evaluation-capture-error")).ToContainTextAsync("previous submission needs its receipt");
        await Expect(page.Locator("#evaluation-note")).ToHaveValueAsync("Recover original transport receipt.");
        string? replayPayload = null;
        await page.RouteAsync(mutationUrl, async route => { replayPayload = route.Request.PostData; await route.ContinueAsync(); });
        await page.GetByRole(AriaRole.Button, new() { Name = "Retry original operation", Exact = true }).ClickAsync();
        await Expect(page.Locator(".evaluation-status")).ToHaveTextAsync("Note saved.");
        AssertOriginalOperationReplayed(originalPayload, replayPayload);
        await using var database = fixture.AppHost.CreateAdminContext();
        (await database.Notes.CountAsync(note => note.PlayerCampaignAssignmentId == seed.AssignmentIds[1], cancellationToken)).ShouldBe(1);
    }

    [Fact]
    public async Task EvaluationPlaceReturnAndHistoryPreserveIndependentRosterContextAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        var navigationEvents = RecordNavigationDiagnostics(page);
        var route = $"/campaigns/{seed.CampaignId}?tab=evaluate&evaluation=true&evalSearch=60&evalParticipant={seed.AssignmentIds[59]}&search=Browser&page=2&rosterLanding=true";
        await page.GotoAsync(new Uri(fixture.BaseUri, route).ToString());
        await Expect(page.Locator(".evaluation-save")).ToBeEnabledAsync();
        await page.Locator(".evaluation-place").ClickAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Return to evaluation", Exact = true })).ToBeVisibleAsync();
        page.Url.ShouldContain($"placementParticipant={seed.AssignmentIds[59]}");
        page.Url.ShouldContain("search=Browser");
        page.Url.ShouldContain("page=2");
        await page.GetByRole(AriaRole.Link, new() { Name = "Return to evaluation", Exact = true }).ClickAsync();
        await Expect(page.Locator("#evaluation-player-heading")).ToContainTextAsync("#60 ");
        page.Url.ShouldContain("evalSearch=60");
        await page.Locator(".evaluation-back").ClickAsync();
        await Expect(page.Locator("#evaluation-player-heading")).ToHaveCountAsync(0);
        await Expect(page.Locator("#evaluation-search")).ToHaveValueAsync("60");
        await Expect(page.Locator("a[data-eval-result]")).ToHaveCountAsync(1);
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("#evaluation-player-heading")).ToContainTextAsync("#60 ");
        await page.GoForwardAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("#evaluation-player-heading")).ToHaveCountAsync(0);
        await Expect(page.Locator("#evaluation-search")).ToHaveValueAsync("60");
        await ReturnToCanonicalRosterAsync(page, seed.CampaignId, navigationEvents);
        page.Url.ShouldContain("search=Browser");
        page.Url.ShouldContain("page=2");
    }

    private static async Task ReturnToCanonicalRosterAsync(IPage page, long campaignId, IEnumerable<string> navigationEvents)
    {
        var roster = page.Locator(".route-marker-list a").Filter(new() { Has = page.Locator(".route-marker-label", new() { HasText = "Roster" }) });
        var rosterHref = await roster.GetAttributeAsync("href");
        rosterHref.ShouldNotBeNull();
        new Uri(new Uri(page.Url), rosterHref).AbsolutePath.ShouldBe($"/campaigns/{campaignId}/roster");
        await InstallFinalClickDiagnosticsAsync(page);
        await ClickAnchorWithDiagnosticsAsync(roster);
        try
        {
            await page.WaitForURLAsync(url => string.Equals(new Uri(url).AbsolutePath, $"/campaigns/{campaignId}/roster", StringComparison.Ordinal));
        }
        catch (TimeoutException exception)
        {
            var snapshot = await page.Locator("body").AriaSnapshotAsync();
            var pointerEvents = await page.EvaluateAsync<string>("JSON.stringify(window.__evaluationClickDiagnostics ?? [])");
            throw new TimeoutException($"Roster navigation did not complete. URL: {page.Url}; clicked href: {rosterHref}. Events: {string.Join('\n', navigationEvents)}. Pointer/history: {pointerEvents}. ARIA: {snapshot}", exception);
        }
        new Uri(page.Url).AbsolutePath.ShouldBe($"/campaigns/{campaignId}/roster");
    }

    [Fact]
    public async Task ClosingDuringCompositionKeepsDraftCopyableAndRemovesMutationControlsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await OpenEvaluationAsync(page, seed.CampaignId, seed.AssignmentIds[1]);
        await Expect(page.Locator(".evaluation-save")).ToBeEnabledAsync();
        await page.Locator("#evaluation-note").FillAsync("Copy this observation after closing.");
        await fixture.CloseCampaignAsAdminAsync(seed.CampaignId, seed.AdminUserId, seed.ClubId, cancellationToken);
        await page.Locator(".evaluation-save").ClickAsync();
        await Expect(page.Locator(".evaluation-readonly")).ToContainTextAsync("This campaign is read-only");
        await Expect(page.Locator(".evaluation-save")).ToHaveCountAsync(0);
        await Expect(page.Locator(".evaluation-place")).ToHaveCountAsync(0);
        await Expect(page.Locator("#evaluation-note")).ToHaveValueAsync("Copy this observation after closing.");
        await Expect(page.Locator("#evaluation-note")).ToHaveAttributeAsync("readonly", string.Empty);
        await page.SetViewportSizeAsync(1280, 960);
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await CaptureViewportAsync(page, "closed");
        await using var database = fixture.AppHost.CreateAdminContext();
        (await database.Notes.CountAsync(note => note.PlayerCampaignAssignmentId == seed.AssignmentIds[1], cancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task DraftProtectsNativeRosterLinkUntilExplicitDiscardAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        var events = RecordNavigationDiagnostics(page);
        await OpenEvaluationAsync(page, seed.CampaignId, seed.AssignmentIds[1]);
        await Expect(page.Locator(".evaluation-save")).ToBeEnabledAsync();
        await InstallFinalClickDiagnosticsAsync(page);
        var origin = page.Url;
        await page.Locator("#evaluation-note").FillAsync("Keep this draft during native navigation.");
        var roster = page.GetByRole(AriaRole.Navigation, new() { Name = "Campaign workspace routes" }).GetByRole(AriaRole.Link, new() { Name = "Roster" });
        await ClickAnchorWithDiagnosticsAsync(roster);
        await AssertGuardPromptAsync(page, page.Locator(".evaluation-protection"), events);
        page.Url.ShouldBe(origin);
        await page.GetByRole(AriaRole.Button, new() { Name = "Keep working", Exact = true }).ClickAsync();
        await Expect(page.Locator(".evaluation-protection")).ToHaveCountAsync(0);
        await Expect(page.Locator("#evaluation-note")).ToHaveValueAsync("Keep this draft during native navigation.");
        page.Url.ShouldBe(origin);
        await ClickAnchorWithDiagnosticsAsync(roster);
        await page.GetByRole(AriaRole.Button, new() { Name = "Discard and leave", Exact = true }).ClickAsync();
        try
        {
            var destination = new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}/roster").AbsoluteUri;
            await Expect(page).ToHaveURLAsync(new Regex($"^{Regex.Escape(destination)}(?:[?#].*)?$",
                RegexOptions.None, TimeSpan.FromSeconds(1)), new() { Timeout = 30000 });
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            var snapshot = await page.Locator("body").AriaSnapshotAsync();
            var pointerEvents = await page.EvaluateAsync<string>("JSON.stringify(window.__evaluationClickDiagnostics ?? [])");
            throw new TimeoutException($"Discard navigation did not complete. URL: {page.Url}; events: {string.Join('\n', events)}; pointer/history: {pointerEvents}; ARIA: {snapshot}", exception);
        }
        await Expect(page.Locator("#roster-search")).ToBeVisibleAsync();
        await using var database = fixture.AppHost.CreateAdminContext();
        (await database.Notes.CountAsync(note => note.PlayerCampaignAssignmentId == seed.AssignmentIds[1], TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    [Fact]
    public async Task ProtectedBackKeepsThenResumesExactHistoryEntriesAndForwardContextAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        var events = RecordNavigationDiagnostics(page);
        await OpenEvaluationAsync(page, seed.CampaignId);
        await SearchAsync(page, "60");
        var finderUrl = page.Url;
        var finderKey = await page.EvaluateAsync<string>("navigation.currentEntry.key");
        await page.Locator("a[data-eval-result]").ClickAsync();
        await Expect(page.Locator(".evaluation-save")).ToBeEnabledAsync();
        await InstallFinalClickDiagnosticsAsync(page);
        var selectedUrl = page.Url;
        var selectedKey = await page.EvaluateAsync<string>("navigation.currentEntry.key");
        await page.Locator("#evaluation-note").FillAsync("History-protected draft.");
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await AssertProtectedHistoryOriginAsync(page, selectedUrl, selectedKey, events);
        await page.GetByRole(AriaRole.Button, new() { Name = "Keep working", Exact = true }).ClickAsync();
        await Expect(page.Locator(".evaluation-protection")).ToHaveCountAsync(0);
        (await page.EvaluateAsync<string>("navigation.currentEntry.key")).ShouldBe(selectedKey);
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await AssertProtectedHistoryOriginAsync(page, selectedUrl, selectedKey, events);
        await page.GetByRole(AriaRole.Button, new() { Name = "Discard and leave", Exact = true }).ClickAsync();
        await AssertHistoryDestinationAsync(page, finderUrl, events);
        await Expect(page.Locator("#evaluation-player-heading")).ToHaveCountAsync(0);
        (await page.EvaluateAsync<string>("navigation.currentEntry.key")).ShouldBe(finderKey);
        await page.GoForwardAsync(new() { WaitUntil = WaitUntilState.Commit });
        await AssertHistoryDestinationAsync(page, selectedUrl, events);
        await Expect(page.Locator("#evaluation-player-heading")).ToContainTextAsync("#60 ");
        await Expect(page.Locator("a[data-eval-result][aria-current='page']")).ToHaveCountAsync(1);
        await Expect(page.Locator("#evaluation-search")).ToHaveValueAsync("60");
        await Expect(page.Locator("#evaluation-note")).ToHaveValueAsync(string.Empty);
        (await page.EvaluateAsync<string>("navigation.currentEntry.key")).ShouldBe(selectedKey);
    }

    [Fact]
    public async Task DrawerDraftProtectsNativeEvaluateLinkUntilExplicitDiscardAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        var events = RecordNavigationDiagnostics(page);
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}/roster?tab=roster&participant={seed.AssignmentIds[1]}").ToString());
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add note", Exact = true })).ToBeEnabledAsync();
        await InstallFinalClickDiagnosticsAsync(page);
        var origin = page.Url;
        await page.GetByRole(AriaRole.Button, new() { Name = "Add note", Exact = true }).ClickAsync();
        await page.Locator("#participant-drawer-note-content").FillAsync("Drawer draft remains protected.");
        var evaluate = page.GetByRole(AriaRole.Navigation, new() { Name = "Campaign workspace routes" }).GetByRole(AriaRole.Link, new() { Name = "Evaluate" });
        await ClickAnchorWithDiagnosticsAsync(evaluate);
        await AssertGuardPromptAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Keep working", Exact = true }), events);
        page.Url.ShouldBe(origin);
        await page.GetByRole(AriaRole.Button, new() { Name = "Keep working", Exact = true }).ClickAsync();
        await Expect(page.Locator("#participant-drawer-note-content")).ToHaveValueAsync("Drawer draft remains protected.");
        await ClickAnchorWithDiagnosticsAsync(evaluate);
        await page.GetByRole(AriaRole.Button, new() { Name = "Discard and leave", Exact = true }).ClickAsync();
        await Expect(page.Locator("#evaluation-search")).ToBeVisibleAsync();
        await Expect(page.Locator("aside.participant-drawer")).ToHaveCountAsync(0);
        page.Url.ShouldContain("tab=evaluate");
    }

    [Fact]
    public async Task ModifiedPlayerClickOpensNewTabWithoutChangingOriginalDraftAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        var events = RecordNavigationDiagnostics(page);
        await OpenEvaluationAsync(page, seed.CampaignId, seed.AssignmentIds[1], search: "Player");
        await Expect(page.Locator(".evaluation-save")).ToBeEnabledAsync();
        await InstallFinalClickDiagnosticsAsync(page);
        var origin = page.Url;
        await page.Locator("#evaluation-note").FillAsync("Keep the original tab draft.");
        var initialPageCount = context.Pages.Count;
        IPage popup;
        try
        {
            // A user-created tab need not retain an opener; observe the context's new-page event.
            popup = await context.RunAndWaitForPageAsync(() => page.Locator($"a[data-eval-result][href$=\"evalParticipant={seed.AssignmentIds[0]}\"], a[data-eval-result][href*=\"evalParticipant={seed.AssignmentIds[0]}&\"]")
                .ClickAsync(new() { Modifiers = [KeyboardModifier.Control] }));
        }
        catch (TimeoutException exception)
        {
            var draft = await page.Locator("#evaluation-note").InputValueAsync();
            var pages = string.Join(" | ", context.Pages.Select(item => item.Url));
            var pointerEvents = await page.EvaluateAsync<string>("JSON.stringify(window.__evaluationClickDiagnostics ?? [])");
            var snapshot = await page.Locator("body").AriaSnapshotAsync();
            throw new TimeoutException($"Ctrl-click did not produce a new context page. Original URL: {origin}; current URL: {page.Url}; original-tab draft: {draft}; context pages: {pages}; events: {string.Join('\n', events)}; pointer/history: {pointerEvents}; ARIA: {snapshot}", exception);
        }
        context.Pages.Count.ShouldBe(initialPageCount + 1);
        popup.ShouldNotBeSameAs(page);
        await Expect(popup.Locator("#evaluation-player-heading")).ToContainTextAsync("#1 ");
        popup.Url.ShouldContain($"evalParticipant={seed.AssignmentIds[0]}");
        page.Url.ShouldBe(origin);
        await Expect(page.Locator("#evaluation-note")).ToHaveValueAsync("Keep the original tab draft.");
        await Expect(page.Locator(".evaluation-protection")).ToHaveCountAsync(0);
        await popup.CloseAsync();
    }

    [Fact]
    public async Task MissingNavigationApiKeepsCaptureReadOnlyWithRecoveryGuidanceAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        await context.AddInitScriptAsync("Object.defineProperty(window, 'navigation', { configurable: true, value: undefined });");
        var page = context.Pages[0];
        await OpenEvaluationAsync(page, seed.CampaignId, seed.AssignmentIds[1]);
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Navigation protection is unavailable");
        await Expect(page.GetByRole(AriaRole.Alert)).ToContainTextAsync("Update your browser or retry");
        await Expect(page.Locator("#evaluation-player-heading")).ToBeVisibleAsync();
        await Expect(page.Locator("#evaluation-note")).ToHaveAttributeAsync("readonly", string.Empty);
        await Expect(page.Locator(".evaluation-save")).ToBeDisabledAsync();
        await using var database = fixture.AppHost.CreateAdminContext();
        (await database.Notes.CountAsync(note => note.PlayerCampaignAssignmentId == seed.AssignmentIds[1], TestContext.Current.CancellationToken)).ShouldBe(0);
    }

    private static async Task AssertHistoryDestinationAsync(IPage page, string url, IEnumerable<string> events)
    {
        try { await page.WaitForURLAsync(url, new() { WaitUntil = WaitUntilState.Commit }); }
        catch (TimeoutException exception)
        {
            var history = await page.EvaluateAsync<string>("""
                JSON.stringify({current: {key: navigation.currentEntry.key, url: location.href},
                    entries: navigation.entries().map(entry => ({key: entry.key, url: entry.url})),
                    events: window.__evaluationClickDiagnostics ?? []})
                """);
            var snapshot = await page.Locator("body").AriaSnapshotAsync();
            throw new TimeoutException($"History destination did not settle at {url}. Actual URL: {page.Url}; events: {string.Join('\n', events)}; history: {history}; ARIA: {snapshot}", exception);
        }
    }

    private static async Task AssertProtectedHistoryOriginAsync(IPage page, string url, string key, IEnumerable<string> events)
    {
        await AssertGuardPromptAsync(page, page.Locator(".evaluation-protection"), events);
        await page.WaitForURLAsync(url, new() { WaitUntil = WaitUntilState.Commit });
        (await page.EvaluateAsync<string>("navigation.currentEntry.key")).ShouldBe(key);
        await Expect(page.Locator("#evaluation-note")).ToHaveValueAsync("History-protected draft.");
    }

    private static async Task AssertGuardPromptAsync(IPage page, ILocator prompt, IEnumerable<string> events)
    {
        try { await Expect(prompt).ToBeVisibleAsync(); }
        catch (PlaywrightException exception)
        {
            var pointerEvents = await page.EvaluateAsync<string>("JSON.stringify(window.__evaluationClickDiagnostics ?? [])");
            var snapshot = await page.Locator("body").AriaSnapshotAsync();
            throw new InvalidOperationException($"Native guard prompt missing. URL: {page.Url}; events: {string.Join('\n', events)}; pointer/history: {pointerEvents}; ARIA: {snapshot}", exception);
        }
    }

    private static async Task ClickAnchorWithDiagnosticsAsync(ILocator anchor)
    {
        await RecordAnchorGeometryAsync(anchor);
        await anchor.ClickAsync();
    }

    private static Task<JsonElement?> RecordAnchorGeometryAsync(ILocator anchor) => anchor.EvaluateAsync<JsonElement?>("""
        element => {
            const box = element.getBoundingClientRect();
            const style = getComputedStyle(element);
            const hit = document.elementFromPoint(box.x + box.width / 2, box.y + box.height / 2);
            window.__evaluationClickDiagnostics?.push({kind:'anchor-geometry', time:performance.now(), scrollY,
                href:element.getAttribute('href'), box:box.toJSON(), hit:hit?.outerHTML,
                display:style.display, position:style.position, transform:style.transform,
                visibility:style.visibility, fonts:document.fonts.status, readyState:document.readyState});
        }
        """);

    private static async Task AssertPendingOperationBlocksPlayerLinkAsync(IPage page, long otherAssignmentId)
    {
        var origin = page.Url;
        await page.Locator($"a[data-eval-result][href$=\"evalParticipant={otherAssignmentId}\"], a[data-eval-result][href*=\"evalParticipant={otherAssignmentId}&\"]").ClickAsync();
        await Expect(page.Locator(".evaluation-protection")).ToContainTextAsync("Resolve the pending submission before changing players");
        page.Url.ShouldBe(origin);
        await Expect(page.Locator("#evaluation-note")).ToHaveValueAsync("Recover original transport receipt.");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Discard and leave", Exact = true })).ToHaveCountAsync(0);
    }

    private static void AssertOriginalOperationReplayed(string? originalPayload, string? replayPayload)
    {
        originalPayload.ShouldNotBeNull();
        replayPayload.ShouldNotBeNull();
        using var original = JsonDocument.Parse(originalPayload);
        using var replay = JsonDocument.Parse(replayPayload);
        replay.RootElement.GetProperty("operationId").GetGuid().ShouldBe(original.RootElement.GetProperty("operationId").GetGuid());
        replay.RootElement.GetProperty("content").GetString().ShouldBe("Recover original transport receipt.");
    }

    private static async Task VerifyAndCaptureOwnNoteControlsAsync(IPage page)
    {
        var note = page.Locator(".evaluation-note-item");
        foreach (var action in new[] { "Edit", "Delete" })
        {
            var button = note.GetByRole(AriaRole.Button, new() { Name = action, Exact = true });
            await Expect(button).ToBeEnabledAsync();
            var bounds = await button.BoundingBoxAsync();
            bounds.ShouldNotBeNull();
            bounds.Height.ShouldBeGreaterThanOrEqualTo(44);
            bounds.Width.ShouldBeGreaterThanOrEqualTo(44);
        }
        await note.ScrollIntoViewIfNeededAsync();
        await CaptureViewportAsync(page, "own-note");
    }

    private static async Task CaptureViewportAsync(IPage page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("NOVA_EVALUATION_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) { return; }
        Directory.CreateDirectory(directory);
        await page.Mouse.MoveAsync(0, 0);
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name + ".png"), FullPage = false });
        var geometry = await page.EvaluateAsync<string>("JSON.stringify({cssWidth:innerWidth,cssHeight:innerHeight,devicePixelRatio,scrollY,documentHeight:document.documentElement.scrollHeight})");
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"), geometry, TestContext.Current.CancellationToken);
    }

    private static ConcurrentQueue<string> RecordNavigationDiagnostics(IPage page)
    {
        var events = new ConcurrentQueue<string>();
        void Record(string detail)
        {
            if (events.Count < 100) { events.Enqueue($"{DateTimeOffset.UtcNow:O} {detail}"); }
        }
        page.PageError += (_, error) => Record($"Page error: {error}");
        page.Console += (_, message) =>
        {
            if (message.Type is "error" or "warning") { Record($"Console {message.Type}: {message.Text}"); }
        };
        page.FrameNavigated += (_, frame) =>
        {
            if (frame.ParentFrame is null) { Record($"Navigation: {frame.Url}"); }
        };
        page.Request += (_, request) =>
        {
            if (request.IsNavigationRequest) { Record($"Request: {request.Method} {request.Url}"); }
        };
        page.RequestFailed += (_, request) => Record($"Request failed: {request.Method} {request.Url} {request.Failure}");
        return events;
    }

    private static Task<JsonElement?> InstallFinalClickDiagnosticsAsync(IPage page) => page.EvaluateAsync("""
        () => {
            if (window.__evaluationClickDiagnostics) return;
            const entries = window.__evaluationClickDiagnostics = [];
            const record = (kind, detail) => {
                if (entries.length < 100) entries.push({kind, time: performance.now(), scrollY, url: location.href, ...detail});
            };
            for (const kind of ['pointerdown', 'pointerup', 'click']) {
                // Window capture precedes the document guard, including stopImmediatePropagation.
                window.addEventListener(kind, event => {
                    const target = event.target instanceof Element ? event.target : null;
                    const anchor = target?.closest('a');
                    const describe = node => node instanceof Element
                        ? {tag:node.tagName, id:node.id, classes:node.className, href:node.getAttribute('href')}
                        : {name:node?.nodeName ?? 'window'};
                    const detail = {
                        href: anchor?.getAttribute('href'), target: describe(target),
                        path: event.composedPath().map(describe),
                        hit: describe(document.elementFromPoint(event.clientX, event.clientY)),
                        anchorBox: anchor?.getBoundingClientRect().toJSON(),
                        x: event.clientX, y: event.clientY, prevented: event.defaultPrevented,
                        button: event.button, ctrl: event.ctrlKey, meta: event.metaKey, shift: event.shiftKey, alt: event.altKey
                    };
                    record(kind, detail);
                    // A later task observes cancellation after all capture/bubble handlers ran.
                    setTimeout(() => record(kind + '-completed', {
                        ...detail, prevented: event.defaultPrevented, anchorConnected: anchor?.isConnected,
                        finalAnchorBox: anchor?.getBoundingClientRect().toJSON()
                    }), 0);
                }, true);
            }
            for (const method of ['pushState', 'replaceState']) {
                const original = history[method];
                history[method] = function(...args) {
                    record(method, {destination: args[2]});
                    return original.apply(this, args);
                };
            }
            window.Blazor?.addEventListener('enhancedload', () => record('enhancedload', {fonts:document.fonts.status}));
            window.addEventListener('popstate', () => record('popstate', {key:navigation.currentEntry?.key}), true);
            record('installed', {});
        }
        """);

    private static async Task ReloadRetainedCaptureAsync(IPage page)
    {
        await page.ReloadAsync();
        await InteractionHelpers.ActUntilAsync(page, () => Task.CompletedTask, async () =>
        {
            // Observe restoration without typing into or clearing the retained operation. Any
            // terminal error or unexpectedly editable empty state ends the wait and fails below.
            var terminal = await page.EvaluateAsync<bool>("""
                () => !!document.querySelector('.evaluation-workspace [role="alert"], .evaluation-save:not([disabled])')
                    || !!document.querySelector('#evaluation-note:not([readonly])')
                """);
            return terminal;
        });
    }

    private async Task OpenEvaluationAsync(IPage page, long campaignId, long? participantId = null, string? search = null)
    {
        var path = $"/campaigns/{campaignId}?tab=evaluate&evaluation=true";
        if (participantId is not null) { path += $"&evalParticipant={participantId}"; }
        if (search is not null) { path += $"&evalSearch={Uri.EscapeDataString(search)}"; }
        await page.GotoAsync(new Uri(fixture.BaseUri, path).ToString());
        await Expect(page.Locator("[data-evaluation-workspace]")).ToBeVisibleAsync();
    }

    private static async Task SearchAsync(IPage page, string text)
    {
        await InteractionHelpers.ActUntilAsync(page, async () =>
        {
            await page.Locator("#evaluation-search").FillAsync(text);
            await page.Locator("#evaluation-search").PressAsync("Enter");
        }, async () => page.Url.Contains($"evalSearch={text}", StringComparison.Ordinal) && await page.Locator("a[data-eval-result]").CountAsync() > 0);
    }
}
