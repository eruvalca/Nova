using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Features.Players;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level validation of the campaign closeout cross-slice scenarios: the administrator
/// evaluation-to-closeout happy path, blocked-close behavior, the stale blocked-close conflict, the
/// reopen confirmation flow, read-only rendering for non-administrators, direct URL/back-navigation
/// tab preservation, and keyboard/accessibility across viewports.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class CampaignCloseoutBrowserTests(BrowserSuiteFixture fixture)
{
    [Fact]
    public async Task AdminOverviewAndCloseoutHappyPathResolvesBlockersAndClosesIntoReadOnlyStateAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];

        // Overview shows the snapshot, the blocked readiness line, and the administrator closeout link.
        await OpenEvaluationAsync(page, seed.BlockedCampaignId);
        await Expect(page.Locator("#evaluation-finder-heading")).ToBeVisibleAsync();
        await Expect(page.Locator(".readiness-context")).ToContainTextAsync("Not ready to close");
        await OpenCloseoutFromEvaluationAsync(page);

        // Closeout shows authoritative counts and the three blocker rows with Count + Message.
        var blockerRows = page.Locator(".close-blocker");
        await Expect(blockerRows).ToHaveCountAsync(3);
        await Expect(blockerRows.Nth(0)).ToContainTextAsync("missing campaign outcomes");
        await Expect(blockerRows.Nth(0)).ToContainTextAsync("1");
        await Expect(blockerRows.Nth(1)).ToContainTextAsync("incompatible assignments");
        await Expect(blockerRows.Nth(2)).ToContainTextAsync("archived team");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review close" })).ToHaveCountAsync(0);

        // Resolve the outcomes blocker through the unresolved drill-down, then the eligibility and
        // archived-team blockers through their no-filter drill-downs.
        await ResolveBlockerAsync(page, "missing campaign outcomes", seed.BlockedAssignmentIds[0]);
        await ResolveBlockerAsync(page, "incompatible assignments", seed.BlockedAssignmentIds[1]);
        await ResolveBlockerAsync(page, "archived team", seed.BlockedAssignmentIds[2]);

        await Expect(page.Locator(".close-blocker")).ToHaveCountAsync(0);
        await Expect(page.Locator(".close-verdict")).ToHaveTextAsync("Ready to close");
        await ReviewCloseAsync(page);
        var closeButton = page.GetByRole(AriaRole.Button, new() { Name = "Close campaign" });
        await Expect(closeButton).ToBeEnabledAsync();
        await InteractionHelpers.ClickUntilAsync(page, closeButton, () => page.GetByText("Campaign closed.").IsVisibleAsync());

        // The panel switches to the closed read-only view and announces the close.
        await Expect(page.Locator(".lifecycle-checkpoint [role=status]")).ToContainTextAsync("Campaign closed.");
        await Expect(page.Locator(".close-review")).ToContainTextAsync("This campaign is closed and read-only.");
        await Expect(page.Locator("[aria-label='Final outcome summary']")).ToBeVisibleAsync();

        // The canonical club activity surface retains the closed transition.
        await AssertDashboardActivityAsync(page, seed.BlockedCampaignId, "closed");
    }

    [Fact]
    public async Task AdminBlockedCloseShowsBlockerDetailsWithoutActionAndNothingFrozenAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];

        await OpenCloseoutAsync(page, seed.BlockedCampaignId);

        var blockerRows = page.Locator(".close-blocker");
        await Expect(blockerRows).ToHaveCountAsync(3);
        // Blocker detail is text (Count + Message), not color-only.
        await Expect(blockerRows.Nth(0)).ToContainTextAsync("1 missing campaign outcomes");
        await Expect(blockerRows.Nth(1)).ToContainTextAsync("1 incompatible assignments");
        await Expect(blockerRows.Nth(2)).ToContainTextAsync("1 assignments use an archived team");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review close" })).ToHaveCountAsync(0);

        // Nothing is frozen: an administrator placement save still succeeds.
        await OpenPlacementsAsync(page, seed.BlockedCampaignId);
        await Expect(page.Locator("a.place-row").First).ToBeVisibleAsync();
        await SaveFirstPlacementOutcomeAsync(page, PlacementOutcome.NotSelected);
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Placement saved.");
    }

    /// <summary>Verifies automatic enrollment invalidates stale readiness without freezing campaign editing.</summary>
    /// <returns>A task representing the concurrent closeout browser scenario.</returns>
    [Fact]
    public async Task AdminStaleBlockedCloseShowsConflictAlertWithoutFreezingAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await CloseoutSeed.ActivateCampaignAsync(
            fixture.AppHost,
            seed.ClubId,
            seed.ReadyCampaignId,
            seed.AdminUserId,
            cancellationToken);
        await using var adminContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        await using var secondAdminContext = await fixture.NewSignedInContextAsync(seed.SecondAdminEmail, CloseoutSeed.Password);
        var adminPage = adminContext.Pages[0];
        var secondPage = secondAdminContext.Pages[0];

        // Admin A loads the ready campaign's closeout, which reports ready.
        await OpenCloseoutAsync(adminPage, seed.ReadyCampaignId);
        await ReviewCloseAsync(adminPage);
        var closeButton = adminPage.GetByRole(AriaRole.Button, new() { Name = "Close campaign" });
        await Expect(closeButton).ToBeEnabledAsync();

        // Admin B creates a player while Admin A holds stale readiness. The real creation service
        // automatically enrolls the player, introducing an unresolved participation without clearing a decision.
        using (fixture.AppHost.UseUser(seed.SecondAdminUserId, seed.ClubId, isClubAdmin: true))
        {
            var players = new PlayerManagementService(
                fixture.AppHost.CreateTenantContextFactory(),
                fixture.AppHost.CurrentUser,
                NullLogger<PlayerManagementService>.Instance);
            var created = await players.CreateAsync(new CreatePlayerInput
            {
                FirstName = "Late",
                LastName = $"Arrival {Guid.NewGuid():N}",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030
            }, cancellationToken);
            created.IsSuccess.ShouldBeTrue();
        }
        await OpenPlacementsAsync(secondPage, seed.ReadyCampaignId);
        // The late arrival joins the Needs-placement queue, which carries the written whole-campaign total.
        await Expect(secondPage.Locator("button.place-section.leads")).ToContainTextAsync("Needs placement");
        await Expect(secondPage.Locator("button.place-section.leads")).ToContainTextAsync("1");

        // Admin A's stale close is rejected with an actionable conflict and refetches the blockers.
        var conflictAlert = adminPage.Locator(".lifecycle-checkpoint [role=status]");
        await InteractionHelpers.ClickUntilAsync(adminPage, closeButton, () => conflictAlert.IsVisibleAsync());
        await Expect(conflictAlert).ToContainTextAsync("Resolve all campaign close blockers before closing this campaign.");
        await Expect(adminPage.Locator(".close-blocker")).ToHaveCountAsync(1);
        await Expect(adminPage.Locator(".close-blocker")).ToContainTextAsync("missing campaign outcomes");

        // The campaign is still active and editable for Admin B.
        await InteractionHelpers.ClickUntilAsync(
            secondPage,
            secondPage.Locator("a.place-row").First,
            () => OutcomeEnabledAsync(secondPage));
        await Expect(secondPage.Locator("#place-outcome")).ToBeEnabledAsync();
    }

    [Fact]
    public async Task AdminReopenConfirmRestoresEditingPreservingOutcomesAndHistoryAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await CloseoutSeed.CloseActiveCampaignAsync(
            fixture.AppHost,
            seed.ClubId,
            seed.AdminUserId,
            cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];

        await OpenCloseoutAsync(page, seed.ClosedCampaignId);
        await Expect(page.Locator(".close-review")).ToContainTextAsync("This campaign is closed and read-only.");

        // Reopen requires an inline confirmation; Cancel hides it without effect.
        var reopenButton = page.GetByRole(AriaRole.Button, new() { Name = "Review reopen" });
        var confirmGroup = page.Locator("[role=group][aria-label='Lifecycle confirmation']");
        await InteractionHelpers.ClickUntilAsync(page, reopenButton, () => confirmGroup.IsVisibleAsync());
        await Expect(confirmGroup).ToContainTextAsync("Existing outcomes remain in place.");
        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }), () => confirmGroup.IsHiddenAsync());

        await InteractionHelpers.ClickUntilAsync(page, reopenButton, () => confirmGroup.IsVisibleAsync());
        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Reopen campaign" }), () => page.GetByText("Campaign reopened.").IsVisibleAsync());
        await Expect(page.Locator(".lifecycle-checkpoint [role=status]")).ToContainTextAsync("Campaign reopened.");

        // The panel returns to the active checklist.
        await Expect(page.Locator("section[aria-labelledby='closeout-region-heading']")).ToContainTextAsync("3 local outcomes");

        // The canonical activity surface retains both lifecycle transitions.
        await AssertDashboardActivityAsync(page, seed.ClosedCampaignId, "closed", "reopened");

        // Editing is restored and previously decided outcomes are unchanged.
        await OpenPlacementsAsync(page, seed.ClosedCampaignId);
        // The three participants were decided Not selected in the closed campaign, so Place opens on an
        // empty Needs-placement queue; the admin reviews the resolved section to restore editing on one.
        await InteractionHelpers.ClickUntilAsync(
            page,
            page.Locator("button.place-section").Nth(2),
            () => Task.FromResult(page.Url.Contains("placementEligibility=Resolved", StringComparison.Ordinal)));
        // No seeded team is compatible with this participant's graduation year, so editing is proven with an
        // outcome that needs no team rather than by forcing an ineligible assignment.
        await SaveFirstPlacementOutcomeAsync(page, PlacementOutcome.Withdrawn);
        // Editing is restored, and the campaign's previously decided outcomes are unchanged: one participant
        // is now withdrawn while the two remaining Not-selected decisions still resolve their campaign.
        await Expect(page.Locator(".place-evidence")).ToContainTextAsync("Withdrawn");
        var sections = page.Locator("button.place-section");
        await Expect(sections.Nth(2)).ToContainTextAsync("2");
        await Expect(sections.Nth(3)).ToContainTextAsync("1");
    }

    [Fact]
    public async Task NonAdminClosedCampaignRendersReadOnlyWithoutCloseReopenControlsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, CloseoutSeed.Password);
        var page = context.Pages[0];

        await OpenCloseoutAsync(page, seed.ClosedCampaignId);
        await Expect(page.Locator(".close-review")).ToContainTextAsync("This campaign is closed and read-only.");
        await Expect(page.Locator("[aria-label='Final outcome summary']")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Close campaign" })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Reopen campaign" })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review reopen", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.Locator(".lifecycle-checkpoint")).ToContainTextAsync("A club administrator closes or reopens campaigns.");

        // Evaluation retains shared readiness orientation without administrator commands.
        await OpenEvaluationAsync(page, seed.ClosedCampaignId);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Review readiness", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Reopen campaign" })).ToHaveCountAsync(0);

        // Placements render read-only with no enabled save controls.
        await OpenPlacementsAsync(page, seed.ClosedCampaignId);
        await Expect(page.Locator("select[aria-label^='Outcome for']")).ToHaveCountAsync(0);
        await Expect(page.Locator("select[aria-label^='Team for']")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save" })).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task DirectCloseEvaluateUrlsAndBackNavigationPreserveRouteContextAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];

        // Direct closeout and overview URLs render their headings.
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}?tab=close").ToString());
        await Expect(page.Locator("#closeout-region-heading")).ToBeVisibleAsync();
        page.Url.ShouldContain("tab=close");

        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}?tab=evaluate").ToString());
        await Expect(page.Locator("#evaluation-finder-heading")).ToBeVisibleAsync();
        page.Url.ShouldContain("tab=evaluate");

        // Native anchors work before attachment. Repeated clicks while a panel loads
        // would create extra history entries and invalidate this Back-navigation check.
        await ActivatePlaceWithEvidenceAsync(page);
        page.Url.ShouldContain("tab=place");

        // Browser Back restores the overview tab (client-side history entry).
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("#evaluation-finder-heading")).ToBeVisibleAsync();

        // A blocker narrows Close to the exact affected set, then the participant link opens Place.
        await page.GetByRole(AriaRole.Link, new() { Name = "Close" }).ClickAsync();
        await Expect(page.Locator("#closeout-region-heading")).ToBeVisibleAsync();
        var outcomesRow = page.Locator(".close-blocker").Filter(new() { HasText = "missing campaign outcomes" });
        await outcomesRow.GetByRole(AriaRole.Link).ClickAsync();
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(1);
        page.Url.ShouldContain("closeBlocker=outcomes");
        await page.Locator(".close-roster tbody a").ClickAsync();
        await Expect(page.Locator("#placements-region-heading")).ToBeVisibleAsync();
        // The drill-down targets participants still missing a campaign-local outcome across every section,
        // which is deliberately not the Needs-placement queue: a zero queue never stands in for close
        // readiness.
        page.Url.ShouldContain("closeBlocker=outcomes");
        page.Url.ShouldContain("placementEligibility=all");

        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("#closeout-region-heading")).ToBeVisibleAsync();
        page.Url.ShouldContain("tab=close");
        page.Url.ShouldContain("closeBlocker=outcomes");
    }

    [Fact]
    public async Task StartupReconcilesBrowserLocationMissedBeforeInteractiveAttachmentAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, token);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];
        var requested = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/CampaignWorkspace*.razor.js", async route =>
        {
            requested.TrySetResult();
            await release.Task;
            await route.ContinueAsync();
        });
        try
        {
            var initial = new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}?tab=evaluate&sortDirection=desc").ToString();
            await page.GotoAsync(initial, new() { WaitUntil = WaitUntilState.DOMContentLoaded });
            await requested.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            await Expect(page.Locator("#evaluation-finder-heading")).ToBeVisibleAsync();
            var destination = new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}?tab=place&sortDirection=desc").ToString();
            // Model a browser URL update whose notification was missed before renderer attachment.
            await page.EvaluateAsync("url => history.replaceState(history.state, '', url)", destination);
            var historyLength = await page.EvaluateAsync<int>("history.length");
            release.TrySetResult();

            await Expect(page.Locator("#placements-region-heading")).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Place" })).ToHaveAttributeAsync("aria-current", "page");
            await Expect(page).ToHaveURLAsync(destination);
            (await page.EvaluateAsync<int>("history.length")).ShouldBe(historyLength);
            await AssertStartupReconciliationRejectsUnownedLocationsAsync(page);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    private static async Task AssertStartupReconciliationRejectsUnownedLocationsAsync(IPage page)
    {
        // Fragment-only movement has no workspace parameter delivery to acknowledge. Obsolete
        // elements, owners and campaign paths must never dispatch a replacement navigation.
        var rejectedDispatches = await page.EvaluateAsync<int>("""
            async () => {
                const module = await import('/_content/Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor.js');
                const element = document.querySelector('.campaign-field');
                const owner = element.dataset.workspaceOwner;
                const path = location.pathname;
                const fragmentOnly = new URL(location.href);
                fragmentOnly.hash = '#different-fragment';
                const stale = new URL(location.href);
                stale.search = '?tab=evaluate';
                let dispatches = 0;
                const navigate = Blazor.navigateTo;
                try {
                    Blazor.navigateTo = () => { dispatches++; };
                    const results = [
                        module.reconcileWorkspaceLocation(element, owner, fragmentOnly.href, path),
                        module.reconcileWorkspaceLocation(element.cloneNode(), owner, stale.href, path),
                        module.reconcileWorkspaceLocation(element, 'obsolete-owner', stale.href, path),
                        module.reconcileWorkspaceLocation(element, owner, stale.href, path + '/another-campaign')
                    ];
                    return dispatches + results.filter(Boolean).length;
                } finally {
                    Blazor.navigateTo = navigate;
                }
            }
            """);
        rejectedDispatches.ShouldBe(0);
    }

    private static async Task ActivatePlaceWithEvidenceAsync(IPage page)
    {
        var errors = new System.Collections.Concurrent.ConcurrentQueue<string>();
        void RecordError(object? sender, string error) => errors.Enqueue(error);
        page.PageError += RecordError;
        await page.EvaluateAsync("""
            () => {
                window.__novaRouteProbe = [];
                for (const type of ['pointerdown', 'pointerup', 'click']) {
                    document.addEventListener(type, event => {
                        const evidence = { type, target: event.target.outerHTML,
                            href: event.target.closest('a')?.href, url: location.href };
                        window.__novaRouteProbe.push(evidence);
                        queueMicrotask(() => evidence.prevented = event.defaultPrevented);
                    }, { capture: true, once: true });
                }
            }
            """);
        try
        {
            await page.GetByRole(AriaRole.Link, new() { Name = "Place" }).ClickAsync();
            await Expect(page.Locator("#placements-region-heading")).ToBeVisibleAsync();
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            var evidence = await page.EvaluateAsync<string>("""
                JSON.stringify({ url: location.href, events: window.__novaRouteProbe,
                    active: document.querySelector('.route-marker[aria-current="page"]')?.outerHTML,
                    workspace: document.querySelector('.campaign-field')?.dataset })
                """);
            throw new InvalidOperationException($"Place navigation failed: {evidence}; page errors: {string.Join(" | ", errors)}", exception);
        }
        finally
        {
            page.PageError -= RecordError;
        }
    }

    [Fact]
#pragma warning disable MA0051 // Keep this complete browser scenario or DOM measurement together so the setup and asserted behavior remain reviewable.
    public async Task CloseoutKeyboardAndA11yAcrossWideAndNarrowViewportsAsync()
#pragma warning restore MA0051
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await CloseoutSeed.ActivateCampaignAsync(
            fixture.AppHost,
            seed.ClubId,
            seed.ReadyCampaignId,
            seed.AdminUserId,
            cancellationToken);
        await using var wideContext = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = wideContext.Pages[0];

        await OpenEvaluationAsync(page, seed.ReadyCampaignId);
        var openCloseout = page.GetByRole(AriaRole.Link, new() { Name = "Review readiness", Exact = true });
        await InteractionHelpers.ActUntilAsync(
            page,
            async () =>
            {
                await openCloseout.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            },
            () => page.Locator("#closeout-region-heading").IsVisibleAsync());

        var closeButton = page.GetByRole(AriaRole.Button, new() { Name = "Close campaign" });
        await ReviewCloseAsync(page);
        await Expect(closeButton).ToBeEnabledAsync();
        await InteractionHelpers.ActUntilAsync(
            page,
            async () =>
            {
                await closeButton.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            },
            () => page.GetByText("Campaign closed.").IsVisibleAsync());
        await Expect(page.Locator(".lifecycle-checkpoint [role=status]")).ToContainTextAsync("Campaign closed.");

        await OpenCloseoutAsync(page, seed.ClosedCampaignId);
        var reopenButton = page.GetByRole(AriaRole.Button, new() { Name = "Review reopen" });
        var confirmGroup = page.Locator("[role=group][aria-label='Lifecycle confirmation']");
        await InteractionHelpers.ActUntilAsync(
            page,
            async () =>
            {
                await reopenButton.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            },
            () => confirmGroup.IsVisibleAsync());
        await Expect(confirmGroup).ToBeVisibleAsync();

        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel" }).ClickAsync();
        await Expect(confirmGroup).ToBeHiddenAsync();

        await InteractionHelpers.ActUntilAsync(
            page,
            async () =>
            {
                await reopenButton.FocusAsync();
                await page.Keyboard.PressAsync("Enter");
            },
            () => confirmGroup.IsVisibleAsync());
        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Reopen campaign" }), () => page.GetByText("Campaign reopened.").IsVisibleAsync());
        await Expect(page.Locator(".lifecycle-checkpoint [role=status]")).ToContainTextAsync("Campaign reopened.");

        await CloseoutSeed.ActivateCampaignAsync(
            fixture.AppHost,
            seed.ClubId,
            seed.BlockedCampaignId,
            seed.AdminUserId,
            cancellationToken);
        await using var narrowContext = await fixture.NewSignedInContextAsync(
                    seed.AdminEmail, CloseoutSeed.Password, new ViewportSize { Width = 480, Height = 800 });
        var narrowPage = narrowContext.Pages[0];

        await OpenCloseoutAsync(narrowPage, seed.BlockedCampaignId);
        var blockerRows = narrowPage.Locator(".close-blocker");
        await Expect(blockerRows).ToHaveCountAsync(3);
        await Expect(blockerRows.Nth(0)).ToContainTextAsync("missing campaign outcomes");
        await Expect(blockerRows.Nth(1)).ToContainTextAsync("incompatible assignments");
        await Expect(blockerRows.Nth(2)).ToContainTextAsync("archived team");
        await A11yMeasurementHelpers.AssertTouchTargetAsync(narrowPage, blockerRows.First.GetByRole(AriaRole.Link), "Review players");
        await A11yMeasurementHelpers.AssertTouchTargetAsync(narrowPage, narrowPage.Locator("#close-search"), "Close search");

        // A later opened campaign prevents reopening this earlier Closed campaign.
        await OpenCloseoutAsync(narrowPage, seed.ReadyCampaignId);
        await Expect(narrowPage.Locator(".lifecycle-checkpoint")).ToContainTextAsync("A later campaign has opened");
        await Expect(narrowPage.GetByRole(AriaRole.Button, new() { Name = "Review reopen" })).ToHaveCountAsync(0);
        await CloseoutSeed.CloseActiveCampaignAsync(fixture.AppHost, seed.ClubId, seed.AdminUserId, cancellationToken);
        await OpenCloseoutAsync(narrowPage, seed.ClosedCampaignId);
        await AssertNarrowLifecycleTargetsAsync(narrowPage);
    }

    private static async Task AssertNarrowLifecycleTargetsAsync(IPage page)
    {
        var reviewReopen = page.GetByRole(AriaRole.Button, new() { Name = "Review reopen", Exact = true });
        await A11yMeasurementHelpers.AssertTouchTargetAsync(page, reviewReopen, "Review reopen");
        await InteractionHelpers.ClickUntilAsync(page, reviewReopen, () => page.Locator(".confirmation").IsVisibleAsync());
        var reopen = page.GetByRole(AriaRole.Button, new() { Name = "Reopen campaign", Exact = true });
        await A11yMeasurementHelpers.AssertTouchTargetAsync(page, reopen, "Reopen campaign");
        await A11yMeasurementHelpers.AssertTouchTargetAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }), "Cancel");
        await reopen.ClickAsync();
        await Expect(page.Locator(".lifecycle-checkpoint")).ToContainTextAsync("Campaign reopened.");
        await A11yMeasurementHelpers.AssertTouchTargetAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true }), "Review close");
        await ReviewCloseAsync(page);
        await A11yMeasurementHelpers.AssertTouchTargetAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Close campaign", Exact = true }), "Close campaign");
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
    }

    [Fact]
    public async Task RouteMarkersRemainScrollableAndKeyboardOperableAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);

        await using var narrowContext = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            CloseoutSeed.Password,
            new ViewportSize { Width = 480, Height = 800 });
        var narrowPage = narrowContext.Pages[0];
        await narrowPage.EmulateMediaAsync(new() { ReducedMotion = ReducedMotion.Reduce });

        await narrowPage.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}?tab=roster").ToString());
        await Expect(narrowPage.Locator("#roster-region-heading")).ToBeVisibleAsync();
        await Expect(narrowPage.GetByRole(AriaRole.Link, new() { Name = "Roster" }))
            .ToHaveAttributeAsync("aria-current", "page");
        var route = narrowPage.Locator("nav.campaign-route").Last;
        await Expect(route).ToBeVisibleAsync();
        await AssertRouteOverflowAsync(narrowPage, route);

        await InteractionHelpers.ActUntilAsync(
            narrowPage,
            async () =>
            {
                var evaluate = narrowPage.GetByRole(AriaRole.Link, new() { Name = "Evaluate" });
                await evaluate.FocusAsync();
                await evaluate.PressAsync("Enter");
            },
            () => Task.FromResult(narrowPage.Url.Contains("tab=evaluate", StringComparison.OrdinalIgnoreCase)));
        await narrowPage.WaitForURLAsync(
            url => url.Contains("tab=evaluate", StringComparison.OrdinalIgnoreCase),
            new() { WaitUntil = WaitUntilState.Commit });
        await Expect(narrowPage.Locator("#evaluation-finder-heading")).ToBeVisibleAsync();
        await Expect(narrowPage.GetByRole(AriaRole.Link, new() { Name = "Evaluate" }))
            .ToHaveAttributeAsync("aria-current", "page");

        await narrowPage.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}?tab=close").ToString());
        var closeMarker = narrowPage.GetByRole(AriaRole.Link, new() { Name = "Close" });
        await Expect(closeMarker).ToHaveAttributeAsync("aria-current", "page");
        await AssertMarkerFullyVisibleAsync(narrowPage, closeMarker);
        (await narrowPage.EvaluateAsync<double>("document.querySelector('nav.campaign-route').scrollLeft")).ShouldBeGreaterThan(0);

        await AssertRouteRevealPreservesDocumentScrollAsync(narrowPage, closeMarker);
    }

    private static async Task AssertRouteOverflowAsync(IPage page, ILocator route)
    {
        // A locator's resolved handle can detach during hydration before Evaluate runs. Resolve
        // and measure the current strip atomically, preserving the strict overflow requirement.
        await Expect(route.Locator(".route-marker-list")).ToHaveCSSAsync("min-width", "576px");
        var geometry = await page.EvaluateAsync<System.Text.Json.JsonElement>("""
            () => {
                const element = document.querySelector('nav.campaign-route');
                if (!element) throw new Error('Campaign route is missing');
                const list = element.querySelector('.route-marker-list');
                const orientation = element.closest('.workspace-orientation');
                return {
                    overflow: element.scrollWidth > element.clientWidth,
                    connected: element.isConnected, viewport: innerWidth,
                    rootFont: getComputedStyle(document.documentElement).fontSize,
                    scriptingNone: matchMedia('(scripting: none)').matches,
                    clientWidth: element.clientWidth, scrollWidth: element.scrollWidth,
                    width: element.getBoundingClientRect().width,
                    listWidth: list?.getBoundingClientRect().width,
                    listMinimum: list ? getComputedStyle(list).minWidth : null,
                    gridColumns: orientation ? getComputedStyle(orientation).gridTemplateColumns : null,
                    styles: Array.from(document.querySelectorAll('link[rel="stylesheet"]'),
                        link => ({ href: link.href, loaded: Boolean(link.sheet) }))
                };
            }
            """);
        geometry.GetProperty("overflow").GetBoolean().ShouldBeTrue(geometry.ToString());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(320)]
    [InlineData(480)]
    [InlineData(600)]
    [InlineData(768)]
    public async Task RouteMarkersFitDirectCloseLoadAndNavigateWithoutJavaScriptAsync(int width)
    {
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var noScriptContext = await fixture.NewSignedInContextAsync(
            seed.AdminEmail,
            CloseoutSeed.Password,
            new ViewportSize { Width = width, Height = 800 },
            javaScriptEnabled: false);
        var noScriptPage = noScriptContext.Pages[0];
        await noScriptPage.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}?tab=close").ToString());
        await Expect(noScriptPage.Locator("#closeout-region-heading")).ToBeVisibleAsync();
        await Expect(noScriptPage.GetByRole(AriaRole.Link, new() { Name = "Close" }))
            .ToHaveAttributeAsync("aria-current", "page");
        foreach (var name in new[] { "Roster", "Evaluate", "Place", "Close" })
        {
            var marker = noScriptPage.GetByRole(AriaRole.Link, new() { Name = name });
            await AssertMarkerFullyVisibleAsync(noScriptPage, marker);
            (await marker.EvaluateAsync<bool>("element => element.scrollWidth <= element.clientWidth"))
                .ShouldBeTrue($"{name} must retain its full label without overflow at {width}px");
        }

        await noScriptPage.GetByRole(AriaRole.Link, new() { Name = "Place" }).ClickAsync();
        await noScriptPage.WaitForURLAsync(url => url.Contains("tab=place", StringComparison.OrdinalIgnoreCase));
        await Expect(noScriptPage.Locator("#placements-region-heading")).ToBeVisibleAsync();
    }

    private static async Task AssertRouteRevealPreservesDocumentScrollAsync(IPage page, ILocator closeMarker)
    {
        // Invoke the same interop operation on the real rendered strip below the fold.
        // A later workspace render must reveal horizontally without moving the document.
        var scrollTop = await page.EvaluateAsync<double>("""
            async () => {
                const module = await import('/_content/Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor.js');
                const container = document.querySelector('nav.campaign-route');
                if (!container) throw new Error('Campaign route is missing');
                window.scrollTo({ top: document.documentElement.scrollHeight, behavior: 'instant' });
                const before = window.scrollY;
                container.scrollLeft = 0;
                module.revealActiveRouteMarker(container);
                if (window.scrollY !== before) throw new Error('Route reveal moved the document vertically');
                return before;
            }
            """);
        scrollTop.ShouldBeGreaterThan(0);
        await AssertMarkerFullyVisibleAsync(page, closeMarker);
    }

    private static async Task AssertMarkerFullyVisibleAsync(IPage page, ILocator marker)
    {
        await InteractionHelpers.ActUntilAsync(
            page,
            () => Task.CompletedTask,
            () => marker.EvaluateAsync<bool>("""
                element => {
                    const marker = element.getBoundingClientRect();
                    const container = element.closest('.campaign-route');
                    const left = container.getBoundingClientRect().left + container.clientLeft;
                    return marker.width > 0 && marker.left >= left && marker.right <= left + container.clientWidth;
                }
                """));
    }

    [Fact]
    public async Task CloseoutA11yEvidenceCapturesScreenshotsAsync()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NOVA_A11Y_SCREENSHOTS"), "1", StringComparison.Ordinal))
        {
            Assert.Skip("Set NOVA_A11Y_SCREENSHOTS=1 to capture closeout accessibility evidence.");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];
        var outputDirectory = Path.Combine(Path.GetTempPath(), "nova-a11y-screenshots");
        Directory.CreateDirectory(outputDirectory);

        await OpenCloseoutAsync(page, seed.BlockedCampaignId);
        await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "closeout-blocked.png") });
        await OpenCloseoutAsync(page, seed.ClosedCampaignId);
        await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "closeout-closed.png") });
    }

    [Fact]
    public async Task CloseoutLoadingShowsIndicatorThenRendersChecklistAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}").ToString());
        await Expect(page.Locator("#roster-region-heading")).ToBeVisibleAsync();
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, () => AssertCampaignMenuAttachedAsync(page));
        await Expect(page.Locator("#roster-region-heading")).ToBeVisibleAsync();

        // Hold the closeout-readiness fetch open while the loading state is asserted, then release it.
        var release = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var intercepted = new TaskCompletionSource<object?>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(
            IsCloseoutReadinessUrl,
            async route =>
            {
                intercepted.TrySetResult(null);
                await release.Task;
                await route.ContinueAsync();
            });

        try
        {
            await page.GetByRole(AriaRole.Link, new() { Name = "Close" }).ClickAsync();
            await Expect(page.Locator("#closeout-region-heading")).ToBeVisibleAsync();
            await intercepted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            await Expect(page.GetByText("Checking Close readiness…")).ToBeVisibleAsync();

            release.TrySetResult(null);
            await Expect(page.Locator(".close-blocker")).ToHaveCountAsync(3);
        }
        finally
        {
            release.TrySetResult(null);
            await page.UnrouteAsync(IsCloseoutReadinessUrl);
        }
    }

    [Fact]
    public async Task CloseoutFailureShowsRetryAndRetryRecoversAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await CloseoutSeed.SeedAsync(fixture.AppHost, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, CloseoutSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.BlockedCampaignId}").ToString());
        await Expect(page.Locator("#roster-region-heading")).ToBeVisibleAsync();
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, () => AssertCampaignMenuAttachedAsync(page));
        await Expect(page.Locator("#roster-region-heading")).ToBeVisibleAsync();

        var intercepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(IsCloseoutReadinessUrl, async route =>
        {
            await route.FulfillAsync(new() { Status = 500 });
            intercepted.TrySetResult();
        });

        var errorAlert = page.Locator(".close-review [role=alert]");
        var retry = errorAlert.GetByRole(AriaRole.Button, new() { Name = "Retry readiness" });
        try
        {
            await page.GetByRole(AriaRole.Link, new() { Name = "Close" }).ClickAsync();
            await Expect(page.Locator("#closeout-region-heading")).ToBeVisibleAsync();
            await intercepted.Task.WaitAsync(TimeSpan.FromSeconds(30), cancellationToken);
            await Expect(errorAlert).ToContainTextAsync("Close readiness could not be verified");
            await Expect(retry).ToBeVisibleAsync();
        }
        finally
        {
            await page.UnrouteAsync(IsCloseoutReadinessUrl);
        }
        await retry.ClickAsync();

        await Expect(page.Locator(".close-blocker")).ToHaveCountAsync(3);
        await Expect(page.Locator(".close-review [role=alert]")).ToHaveCountAsync(0);
    }

    private static async Task AssertCampaignMenuAttachedAsync(IPage page)
    {
        var toggle = page.GetByRole(AriaRole.Button, new() { Name = "Campaign menu", Exact = true });
        var menu = page.GetByRole(AriaRole.Menu);
        await InteractionHelpers.ClickUntilAsync(page, toggle, () => menu.IsVisibleAsync());
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "true");
        await toggle.ClickAsync();
        await Expect(menu).ToBeHiddenAsync();
        await Expect(toggle).ToHaveAttributeAsync("aria-expanded", "false");
    }

    /// <summary>Matches the closeout-readiness fetch.</summary>
    private static bool IsCloseoutReadinessUrl(string url) =>
        url.Contains("/api/campaigns/", StringComparison.Ordinal)
        && url.Contains("/closeout-readiness", StringComparison.Ordinal);

    private async Task AssertDashboardActivityAsync(IPage page, long campaignId, params string[] verbs)
    {
        await using var db = fixture.AppHost.CreateAdminContext();
        var campaignName = await db.Campaigns.Where(campaign => campaign.CampaignId == campaignId)
            .Select(campaign => campaign.Name).SingleAsync(TestContext.Current.CancellationToken);
        await page.GotoAsync(new Uri(fixture.BaseUri, "/dashboard").ToString());
        foreach (var verb in verbs)
        {
            await Expect(page.Locator(".activity-list li").Filter(new() { HasText = $"{verb} {campaignName}" })).ToBeVisibleAsync();
        }
    }
    /// <summary>Navigates to the closeout tab and waits for its heading.</summary>
    private async Task OpenCloseoutAsync(IPage page, long campaignId)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{campaignId}?tab=close").ToString());
        await Expect(page.Locator("#closeout-region-heading")).ToBeVisibleAsync();
    }

    /// <summary>Navigates to Evaluate and waits for its heading.</summary>
    private async Task OpenEvaluationAsync(IPage page, long campaignId)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{campaignId}?tab=evaluate").ToString());
        await Expect(page.Locator("#evaluation-finder-heading")).ToBeVisibleAsync();
    }

    /// <summary>Navigates to the placements tab and waits for its heading.</summary>
    private async Task OpenPlacementsAsync(IPage page, long campaignId)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{campaignId}?tab=place").ToString());
        await Expect(page.Locator("#placements-region-heading")).ToBeVisibleAsync();
        // The queue region leads with the written whole-campaign section totals.
        await Expect(page.Locator(".place-sections")).ToBeVisibleAsync();
    }

    /// <summary>Follows the shared "Review readiness" link, retrying through SSR hydration.</summary>
    private static async Task OpenCloseoutFromEvaluationAsync(IPage page)
    {
        await InteractionHelpers.ClickUntilAsync(
            page,
            page.GetByRole(AriaRole.Link, new() { Name = "Review readiness", Exact = true }),
            () => page.Locator("#closeout-region-heading").IsVisibleAsync());
    }

    /// <summary>
    /// Resolves a closeout blocker by following its "Review unresolved" drill-down, recording
    /// <see cref="PlacementOutcome.NotSelected"/> against the named assignment, and returning to the
    /// closeout tab.
    /// </summary>
    private static async Task ResolveBlockerAsync(IPage page, string rowLabel, long assignmentId)
    {
        var row = page.Locator(".close-blocker").Filter(new() { HasText = rowLabel });
        await row.GetByRole(AriaRole.Link).ClickAsync();
        await page.Locator($".close-roster a[href*='placementParticipant={assignmentId}']").ClickAsync();
        await Expect(page.Locator("#place-outcome")).ToBeEnabledAsync();
        await SaveSelectedPlacementOutcomeAsync(page, PlacementOutcome.NotSelected);
        await page.GetByRole(AriaRole.Link, new() { Name = "Return to Close", Exact = true }).ClickAsync();
        await Expect(page.Locator("#closeout-region-heading")).ToBeVisibleAsync();
    }

    private static Task ReviewCloseAsync(IPage page)
        => InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true }),
            () => page.GetByRole(AriaRole.Group, new() { Name = "Lifecycle confirmation" }).IsVisibleAsync());

    /// <summary>Records an outcome for whichever queue row is currently selected.</summary>
    /// <param name="page">The page to drive.</param>
    /// <param name="outcome">The outcome to record.</param>
    /// <param name="teamId">The team to assign, required for <see cref="PlacementOutcome.Assigned"/>.</param>
    /// <returns>A task that completes once the save is announced.</returns>
    private static async Task SaveFirstPlacementOutcomeAsync(IPage page, PlacementOutcome outcome, long? teamId = null)
    {
        await InteractionHelpers.ClickUntilAsync(
            page,
            page.Locator("a.place-row").First,
            () => OutcomeEnabledAsync(page));
        await SaveSelectedPlacementOutcomeAsync(page, outcome, teamId);
    }

    /// <summary>
    /// Reports whether the Place decision control exists and is enabled. The presence check must come first
    /// and must not wait: Playwright's <c>IsEnabledAsync</c> waits for a missing element and then throws,
    /// which would abort the settle loop before the first click ever lands.
    /// </summary>
    /// <param name="page">The page to inspect.</param>
    /// <returns><see langword="true"/> when the decision control is present and enabled.</returns>
    private static async Task<bool> OutcomeEnabledAsync(IPage page)
    {
        var outcomeControl = page.Locator("#place-outcome");
        return await outcomeControl.CountAsync() > 0 && await outcomeControl.IsEnabledAsync();
    }

    /// <summary>
    /// Applies an outcome to the selected participant and submits it. The select swallows change events
    /// until the circuit attaches, so the choice is re-applied until the submit becomes enabled.
    /// </summary>
    /// <param name="page">The page to drive.</param>
    /// <param name="outcome">The outcome to record.</param>
    /// <param name="teamId">The team to assign, required for <see cref="PlacementOutcome.Assigned"/>.</param>
    /// <returns>A task that completes once the save is announced.</returns>
    private static async Task SaveSelectedPlacementOutcomeAsync(IPage page, PlacementOutcome outcome, long? teamId = null)
    {
        await Expect(page.Locator(".place-name")).ToBeVisibleAsync();
        var save = page.GetByRole(AriaRole.Button, new() { Name = "Save placement", Exact = true });

        await InteractionHelpers.ActUntilAsync(
            page,
            async () =>
            {
                await page.Locator("#place-outcome").SelectOptionAsync(outcome.ToString());
                if (outcome == PlacementOutcome.Assigned && teamId is { } selectedTeam)
                {
                    await Expect(page.Locator("#place-team")).ToBeEnabledAsync(new() { Timeout = 1500 });
                    await page.Locator("#place-team").SelectOptionAsync(
                        selectedTeam.ToString(System.Globalization.CultureInfo.InvariantCulture));
                }
            },
            async () => await save.CountAsync() > 0 && await save.IsEnabledAsync());

        var confirm = page.GetByRole(AriaRole.Button, new() { Name = "Confirm change", Exact = true });
        await InteractionHelpers.ActUntilAsync(page, () => save.ClickAsync(new() { Timeout = 3000 }),
            async () => await page.GetByText("Placement saved.").IsVisibleAsync() || await confirm.IsVisibleAsync());
        if (await confirm.IsVisibleAsync())
        {
            await confirm.ClickAsync();
            await Expect(page.GetByText("Placement saved.")).ToBeVisibleAsync();
        }
    }
}
