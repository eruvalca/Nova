using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Nova.SharedKernel.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>
/// Browser-level validation of the player add/edit form and the player-detail status badge:
/// validation, successful creation, failure with retry, responsive rendering, keyboard operability,
/// and the active-campaign status badge contrast.
/// </summary>
/// <param name="fixture">The Aspire-hosted browser suite fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed partial class PlayerFormBrowserTests(BrowserSuiteFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    [Fact]
    public async Task PlayerFormValidationRejectsWhitespaceFirstNameAndStaysOnFormAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersAsync(page);

        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Link, new() { Name = "Add player" }), () =>
        {
            return page.Locator("#player-first-name").IsVisibleAsync();
        });

        await page.Locator("#player-first-name").FillAsync("   ");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();

        await Expect(page.Locator("div.intake-field-error").First).ToBeVisibleAsync();
        await Expect(page.Locator("div.alert-success[role=status]")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task IntakeBoardModuleRetainsAndReadsOwnerScopedBytesAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());

        // Exercise the collocated module's own contract in a real browser, independently of Blazor.
        var result = await page.EvaluateAsync<string>(@"async () => {
            const m = await import('/_content/Nova.UI/Features/Players/Components/PlayerIntakeBoard.razor.js');
            const payload = { operationId: '0198f0a1-7b2c-7def-8abc-0123456789ab', clubId: 42, firstName: 'Module',
                lastName: 'Probe', dateOfBirth: '2012-04-01', graduationYear: 2031, gender: null, jerseyNumber: null };
            const created = Number.parseInt(payload.operationId.replaceAll('-', '').slice(0, 12), 16);
            const json = JSON.stringify({ actorUserId: 101,
                recoveryExpiresAt: new Date(created + 86400000).toISOString(), payload });
            // The reservation and the removals take the cross-tab lock, so they answer with a promise.
            await m.writePending(101, 42, json);
            const read = m.readRecovery(101, 42);
            // The read is lock-free by design — only the read-then-write reservation and the removals take the
            // cross-tab lock — so it answers with the record itself rather than with a promise to await.
            const direct = typeof read?.then === 'undefined';
            const stored = Object.keys(localStorage).filter(k => k.startsWith('nova:player-creation')).length;
            // One operation identity carries one exact command: a same-id write with different bytes is
            // refused, and the retained bytes stay the ones the member's dispatch is accounted for by.
            let refused = false;
            try { await m.writePending(101, 42, json.replace('""firstName"":""Module""', '""firstName"":""Altered""')); }
            catch { refused = true; }
            const preserved = m.readRecovery(101, 42).json === json;
            const cleared = await m.clearPending(101, 42, payload.operationId);
            const after = Object.keys(localStorage).filter(k => k.startsWith('nova:player-creation')).length;
            // An in-board anchor is a departure like any other while the board is dirty: the guard asks
            // instead of letting the link navigate with the member's typing.
            const guardRoot = document.createElement('div');
            const inside = document.createElement('a');
            inside.href = '/players?view=archived';
            inside.textContent = 'Review players';
            guardRoot.append(inside);
            document.body.append(guardRoot);
            const asked = [];
            await m.attachDepartureGuard(guardRoot, {
                invokeMethodAsync: (name, lease, url) => { asked.push(name + ' ' + url); return Promise.resolve(); }
            }, 'probe-lease');
            m.markDirty('probe-lease', true);
            const attempt = new MouseEvent('click', { bubbles: true, cancelable: true });
            inside.dispatchEvent(attempt);
            m.detachDepartureGuard('probe-lease');
            guardRoot.remove();
            return [read.json === null ? 'unreadable' : 'readable', stored, cleared, after,
                m.readRecovery(101, 43).json === null ? 'otherowner-empty' : 'otherowner-leaked',
                refused, preserved, direct, asked.length, asked[0] ?? '', attempt.defaultPrevented].join('|');
        }");

        result.ShouldBe("readable|1|true|0|otherowner-empty|true|true|true|1|OnBoardDepartureAttemptAsync /players?view=archived|true");
    }

    /// <summary>Focus moves to the field to correct without removing it from the tab order.</summary>
    [Fact]
    public async Task IntakeBoardFocusMovesToTheFieldNeedingCorrectionAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, "/").ToString());

        // Exercise the collocated module's focus contract in a real browser, on the shape the board renders:
        // the first control marked invalid inside the field set, and a note region beside it.
        var result = await page.EvaluateAsync<string>(@"async () => {
            const m = await import('/_content/Nova.UI/Features/Players/Components/PlayerIntakeBoard.razor.js');
            const root = document.createElement('div');
            const fields = document.createElement('fieldset');
            fields.className = 'intake-fields';
            const control = document.createElement('input');
            control.id = 'probe-control';
            control.className = 'form-control is-invalid';
            fields.appendChild(control);
            root.appendChild(fields);
            document.body.appendChild(root);
            m.focusRegion(root, '.intake-fields .is-invalid');
            const controlFocused = document.activeElement === control;
            const tabOrderKept = !control.hasAttribute('tabindex');
            const noteRoot = document.createElement('div');
            const note = document.createElement('p');
            note.id = 'probe-note';
            note.textContent = 'Note';
            noteRoot.appendChild(note);
            document.body.appendChild(noteRoot);
            m.focusRegion(noteRoot, '#probe-note');
            const regionFocused = document.activeElement === note;
            const regionMadeFocusable = note.getAttribute('tabindex') === '-1';
            root.remove();
            noteRoot.remove();
            return [controlFocused, tabOrderKept, regionFocused, regionMadeFocusable].join('|');
        }");

        result.ShouldBe("true|true|true|true");
    }

    [Fact]
    public async Task PlayerFormSuccessCreatesPlayerAndReflectsInRosterAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersAsync(page);

        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Link, new() { Name = "Add player" }), () =>
        {
            return page.Locator("#player-first-name").IsVisibleAsync();
        });

        var suffix = Guid.NewGuid().ToString("N");
        var firstName = "Form";
        var lastName = $"Player {suffix}";
        await page.Locator("#player-first-name").FillAsync(firstName);
        await page.Locator("#player-last-name").FillAsync(lastName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();

        await Expect(page.Locator("#intake-receipt-heading")).ToContainTextAsync("Player added");
        await Expect(page.GetByText($"{firstName} {lastName}")).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PlayerFormResponsivePreservesInputsAcrossViewportsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersAsync(page);

        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Link, new() { Name = "Add player" }), () =>
        {
            return page.Locator("#player-first-name").IsVisibleAsync();
        });

        var firstName = $"Narrow {Guid.NewGuid():N}";
        await page.Locator("#player-first-name").FillAsync(firstName);
        await page.SetViewportSizeAsync(480, 800);

        await Expect(page.Locator("#player-first-name")).ToHaveValueAsync(firstName);
        await Expect(page.GetByLabel("First name")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true })).ToBeVisibleAsync();
    }

    [Fact]
    public async Task PlayerFormKeyboardTabAndEnterSubmitsAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersAsync(page);

        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Link, new() { Name = "Add player" }), () =>
        {
            return page.Locator("#player-first-name").IsVisibleAsync();
        });

        // The board withholds input until it has checked the owner's retained addition, so a
        // keyboard entry can only land once the fields are enabled.
        await Expect(page.Locator("#player-first-name")).ToBeEnabledAsync();

        var suffix = Guid.NewGuid().ToString("N");
        await page.Locator("#player-first-name").FocusAsync();
        await page.Keyboard.TypeAsync("Form");
        await page.Keyboard.PressAsync("Tab");
        await page.Keyboard.TypeAsync($"Player {suffix}");

        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true });
        await InteractionHelpers.TabUntilFocusedAsync(page, submit);
        await page.Keyboard.PressAsync("Enter");

        await Expect(page.Locator("#intake-receipt-heading")).ToContainTextAsync("Player added");
        await Expect(page.GetByText($"Form Player {suffix}")).ToBeVisibleAsync();
    }

    /// <summary>
    /// The uncommitted-departure guard asks before discarding typed input: an attempt only opens the
    /// confirmation, <c>Keep editing</c> preserves the typed value, and only the confirmed departure leaves.
    /// </summary>
    [Fact]
    public async Task PlayerFormDepartureGuardAsksBeforeDiscardingTypedInputAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersInWebAssemblyAsync(page);
        var departure = page.GetByRole(AriaRole.Link, new() { Name = "Players", Exact = true });
        var firstName = $"Guardian {Guid.NewGuid():N}";

        // The guard attaches after the board's first render, so an early click can still escape; each
        // attempt re-enters the board and retypes until the click is intercepted.
        await InteractionHelpers.ActUntilAsync(page,
            async () =>
            {
                if (!string.Equals(new Uri(page.Url).AbsolutePath, "/players/new", StringComparison.Ordinal))
                {
                    await OpenCreationFormAsync(page);
                }

                await page.Locator("#player-first-name").FillAsync(firstName);
                await departure.ClickAsync(new() { Timeout = 3000 });
            },
            () => page.Locator("#intake-departure").IsVisibleAsync());

        // The attempt opened the confirmation instead of performing the departure.
        new Uri(page.Url).AbsolutePath.ShouldBe("/players/new");
        await Expect(page.Locator("#intake-departure")).ToContainTextAsync("Leave with uncommitted player details?");

        await page.GetByRole(AriaRole.Button, new() { Name = "Keep editing", Exact = true }).ClickAsync();
        await Expect(page.Locator("#intake-departure")).ToHaveCountAsync(0);
        await Expect(page.Locator("#player-first-name")).ToHaveValueAsync(firstName);

        // Cancel is a departure like any other: it asks about the same typed value rather than discarding it,
        // and keeping editing leaves the member where they were with their value intact.
        await page.Locator("#intake-cancel").ClickAsync();
        await Expect(page.Locator("#intake-departure")).ToBeVisibleAsync();
        await Expect(page.Locator("#player-first-name")).ToHaveValueAsync(firstName);
        await page.GetByRole(AriaRole.Button, new() { Name = "Keep editing", Exact = true }).ClickAsync();
        await Expect(page.Locator("#intake-departure")).ToHaveCountAsync(0);

        await InteractionHelpers.ClickUntilAsync(page, departure, () => page.Locator("#intake-departure").IsVisibleAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Leave and discard", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Players", Exact = true })).ToBeVisibleAsync();
        new Uri(page.Url).AbsolutePath.ShouldBe("/players");

        // A board holding no uncommitted input leaves without asking.
        await OpenCreationFormAsync(page);
        await departure.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Players", Exact = true })).ToBeVisibleAsync();
        new Uri(page.Url).AbsolutePath.ShouldBe("/players");
        await Expect(page.Locator("#intake-departure")).ToHaveCountAsync(0);
    }

    /// <summary>
    /// Back/Forward is the board's documented unguarded departure path, and this pins the limitation
    /// rather than its desirability: the router owns a traversal of the board's own entry and leaves the
    /// route before any listener the board installs can run, so the typed value goes with it. The
    /// guard's contract covers document unload and same-origin link departure, which the scenario above
    /// and this one's own first half prove are live for the same typed value.
    /// </summary>
    [Fact]
    public async Task PlayerFormHistoryTraversalIsTheDocumentedUnguardedDepartureAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersInWebAssemblyAsync(page);
        var departure = page.GetByRole(AriaRole.Link, new() { Name = "Players", Exact = true });
        var firstName = $"History {Guid.NewGuid():N}";

        // The guard is attached and speaks for this value: the link departure asks before discarding.
        await InteractionHelpers.ActUntilAsync(page,
            async () =>
            {
                if (!string.Equals(new Uri(page.Url).AbsolutePath, "/players/new", StringComparison.Ordinal))
                {
                    await OpenCreationFormAsync(page);
                }

                await page.Locator("#player-first-name").FillAsync(firstName);
                await departure.ClickAsync(new() { Timeout = 3000 });
            },
            () => page.Locator("#intake-departure").IsVisibleAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Keep editing", Exact = true }).ClickAsync();
        await Expect(page.Locator("#intake-departure")).ToHaveCountAsync(0);
        await Expect(page.Locator("#player-first-name")).ToHaveValueAsync(firstName);

        // The same board, still dirty, cannot intercept the traversal: it leaves without a prompt.
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Players", Exact = true })).ToBeVisibleAsync();
        new Uri(page.Url).AbsolutePath.ShouldBe("/players");
        await Expect(page.Locator("#intake-departure")).ToHaveCountAsync(0);
    }

    [Fact]
    public async Task PlayerDetailActiveCampaignBadgeMeetsContrastThresholdAsync()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        var playerId = await SeedPlayerInActiveCampaignAsync(seed, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/players/{playerId}").ToString());
        await Expect(page.Locator("#campaign-history-heading")).ToBeVisibleAsync();

        // The campaign-history entry renders the active campaign's status badge (text-bg-success).
        var badge = page.Locator("article span.badge.text-bg-success").First;
        await Expect(badge).ToHaveTextAsync("Active");
        await A11yMeasurementHelpers.AssertContrastRatioAsync(badge, 4.5, "player detail active-campaign status badge");
    }

    /// <summary>
    /// Captures player-detail accessibility evidence (screenshot + status-badge measurement) when
    /// <c>NOVA_A11Y_SCREENSHOTS=1</c>; otherwise skips so a green run always means the assertions executed.
    /// </summary>
    [Fact]
    public async Task PlayerDetailA11yEvidenceCapturesScreenshotsAsync()
    {
        if (!string.Equals(Environment.GetEnvironmentVariable("NOVA_A11Y_SCREENSHOTS"), "1", StringComparison.Ordinal))
        {
            Assert.Skip("Set NOVA_A11Y_SCREENSHOTS=1 to capture player detail accessibility evidence.");
        }

        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(cancellationToken);
        var playerId = await SeedPlayerInActiveCampaignAsync(seed, cancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        var outputDirectory = Path.Combine(Path.GetTempPath(), "nova-a11y-screenshots");
        Directory.CreateDirectory(outputDirectory);

        await page.GotoAsync(new Uri(fixture.BaseUri, $"/players/{playerId}").ToString());
        await Expect(page.Locator("#campaign-history-heading")).ToBeVisibleAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(outputDirectory, "player-detail.png") });

        var badge = page.Locator("article span.badge.text-bg-success").First;
        var ratio = await A11yMeasurementHelpers.MeasureContrastRatioAsync(badge);
        await File.AppendAllTextAsync(
            Path.Combine(outputDirectory, "measurements.txt"),
            $"player-detail-active-badge contrast={ratio:F2}{Environment.NewLine}",
            cancellationToken);
    }

    /// <summary>Navigates to the players roster page and waits for it to render.</summary>
    private async Task OpenPlayersAsync(IPage page)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, "/players").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Players", Exact = true })).ToBeVisibleAsync();
    }

    /// <summary>Seeds a club with a single administrator and returns the login credentials and identifiers.</summary>
    private async Task<(long ClubId, string AdminEmail, long AdminUserId)> SeedAdminAsync(CancellationToken cancellationToken)
    {
        using var adminClient = fixture.AppHost.CreateNovaHttpClient();
        var adminEmail = SeedingHelpers.UniqueEmail("player-form-admin");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(adminClient, adminEmail, Password, cancellationToken);
        await SeedingHelpers.UpdateUserAsync(fixture.AppHost, adminEmail, clubId: null, cancellationToken, firstName: "Alice", lastName: "Author");
        var club = await SeedingHelpers.CreateClubAsync(adminClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(adminClient, cancellationToken);

        long adminUserId;
#pragma warning disable MA0004 // Await disposal in this original variable scope while retaining the test runner context.
        await using (var context = fixture.AppHost.CreateAdminContext())
#pragma warning restore MA0004
        {
#pragma warning disable CA1862 // This normalized Identity lookup is translated to SQL; StringComparison overloads are not translatable.
            adminUserId = (await context.Users.SingleAsync(user => user.NormalizedEmail == adminEmail.ToUpperInvariant(), cancellationToken)).Id;
#pragma warning restore CA1862
        }

        return (club.ClubId, adminEmail, adminUserId);
    }

    /// <summary>Seeds a player enrolled in an active campaign and returns the player identifier.</summary>
    private async Task<long> SeedPlayerInActiveCampaignAsync(
        (long ClubId, string AdminEmail, long AdminUserId) seed,
        CancellationToken cancellationToken)
    {
        var campaign = await SeedingHelpers.SeedCampaignWithParticipantsAsync(
            fixture.AppHost, seed.ClubId, seed.AdminEmail, "Player Badge", 1, PlacementOutcome.Undecided, cancellationToken);
        await using var context = fixture.AppHost.CreateAdminContext();
        return await context.PlayerCampaignAssignments
            .Where(assignment => assignment.PlayerCampaignAssignmentId == campaign.AssignmentIds[0])
            .Select(assignment => assignment.PlayerId)
            .SingleAsync(cancellationToken);
    }
}
