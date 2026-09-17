using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Data;
using Nova.Integration.Tests.Http;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>Exercises directory discovery, native destinations and manual member workflows on the real host.</summary>
[Collection(BrowserSuiteCollection.Name)]
public sealed partial class PlayersDirectoryBrowserTests(BrowserSuiteFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    [Fact]
    public async Task NativeDiscoveryWorksWithoutJavaScriptAsync()
    {
        var seed = await SeedingHelpers.SeedDraftClubAsync(fixture.AppHost, 24, 0, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password, javaScriptEnabled: false);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, "/players?returnToDraft=42").ToString());
        await page.GetByLabel("Search name or tryout number").FillAsync("Player 01");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply", Exact = true }).ClickAsync();
        await Expect(page.Locator("tbody .player-record-link")).ToHaveCountAsync(1);
        new Uri(page.Url).Query.ShouldContain("returnToDraft=42");
        await page.GetByRole(AriaRole.Link, new() { Name = "Clear filters", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Next", Exact = true }).ClickAsync();
        await Expect(page.Locator(".players-paging")).ToContainTextAsync("Page 2 of 2");
    }

    [Fact]
    public async Task ModifiedAddLinkOpensANewTabWithoutMovingTheDirectoryAsync()
    {
        var seed = await SeedingHelpers.SeedDraftClubAsync(fixture.AppHost, 1, 0, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, "/players").ToString());
        await AssertDirectoryAttachedAsync(page);
        await page.GetByLabel("Search name or tryout number").FillAsync("Player");
        await page.WaitForURLAsync(url => string.Equals(new Uri(url).PathAndQuery, "/players?search=Player", StringComparison.Ordinal));
        var original = page.Url;
        var popup = await context.RunAndWaitForPageAsync(() => page.GetByRole(AriaRole.Link, new() { Name = "Add player", Exact = true })
            .ClickAsync(new() { Modifiers = [KeyboardModifier.Control] }));
        await popup.WaitForLoadStateAsync(LoadState.DOMContentLoaded);
        await Expect(popup.Locator("#player-first-name")).ToBeVisibleAsync();
        new Uri(popup.Url).PathAndQuery.ShouldBe("/players/new?search=Player");
        page.Url.ShouldBe(original);
    }

    [Fact]
    public async Task DirectoryPagesBeyondOneHundredAndRestoresHistoryAsync()
    {
        var seed = await SeedingHelpers.SeedDraftClubAsync(fixture.AppHost, 121, 0, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await InteractionHelpers.WithNavigationDiagnosticsAsync(page, async () =>
        {
            await page.SetViewportSizeAsync(1440, 1000);
            await page.GotoAsync(new Uri(fixture.BaseUri, "/players").ToString());
            await AssertDirectoryAttachedAsync(page);
            var identities = new HashSet<string>(StringComparer.Ordinal);
            for (var number = 1; number <= 7; number++)
            {
                await Expect(page.Locator(".players-paging")).ToContainTextAsync($"Page {number} of 7");
                var links = page.Locator("tbody .player-record-link");
                await Expect(links).ToHaveCountAsync(number == 7 ? 1 : 20);
                foreach (var href in await links.EvaluateAllAsync<string[]>("nodes => nodes.map(n => n.getAttribute('href'))"))
                {
                    identities.Add(new Uri(fixture.BaseUri, href).AbsolutePath).ShouldBeTrue();
                }
                if (number < 7)
                {
                    await InteractionHelpers.NavigateEnhancedAsync(page,
                        () => page.GetByRole(AriaRole.Link, new() { Name = "Next", Exact = true }).PressAsync("Enter"));
                }
            }
            identities.Count.ShouldBe(121);
            await page.ReloadAsync();
            await Expect(page.Locator(".players-paging")).ToContainTextAsync("Page 7 of 7");
            await AssertDirectoryAttachedAsync(page);
            await InteractionHelpers.NavigateEnhancedAsync(page, () => page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit }));
            await Expect(page.Locator(".players-paging")).ToContainTextAsync("Page 6 of 7");
            await InteractionHelpers.NavigateEnhancedAsync(page, () => page.GoForwardAsync(new() { WaitUntil = WaitUntilState.Commit }));
            await Expect(page.Locator(".players-paging")).ToContainTextAsync("Page 7 of 7");
            await AssertDirectoryAttachedAsync(page);
            await page.GetByLabel("Search name or tryout number").FillAsync("Player 01");
            await page.WaitForURLAsync(url => string.Equals(new Uri(url).PathAndQuery, "/players?search=Player%2001", StringComparison.Ordinal));
            await Expect(page.Locator("tbody .player-record-link")).ToHaveCountAsync(1);
            await Expect(page.Locator("tbody .player-record-link")).ToHaveTextAsync("Player 01");
            new Uri(page.Url).Query.ShouldNotContain("page=");
            await page.ReloadAsync();
            await Expect(page.GetByLabel("Search name or tryout number")).ToHaveValueAsync("Player 01");
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Import player list", Exact = true })).ToHaveCountAsync(0);
        });
    }

    [Fact]
    public async Task MobileDirectoryKeepsLongContentAndKeyboardTargetsWithinItsRegionAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await SeedingHelpers.SeedDraftClubAsync(fixture.AppHost, 121, 0, token);
        const string LongName = "AveryAlexandriaAlexandriaAlexandria";
        await using (var db = fixture.AppHost.CreateAdminContext())
        {
            var player = await db.Players.Where(row => row.ClubId == seed.ClubId).OrderBy(row => row.PlayerId).FirstAsync(token);
            player.FirstName = LongName;
            player.LastName = "JohnsonWithALongUnbrokenFamilyName";
            player.GraduationYear = 2100;
            await db.SaveChangesAsync(token);
        }
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await page.SetViewportSizeAsync(390, 844);
        await page.GotoAsync(new Uri(fixture.BaseUri, "/players").ToString());
        await AssertDirectoryAttachedAsync(page);
        await Expect(page.Locator("#players-grad-year option[value='2100']")).ToHaveCountAsync(1);
        await Expect(page.Locator("tbody .player-record-link").First).ToContainTextAsync(LongName);
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
        var region = page.GetByRole(AriaRole.Region, new() { Name = "Player directory", Exact = true });
        await Expect(region).ToHaveCSSAsync("overflow-x", "auto");
        (await region.EvaluateAsync<bool>("element => element.scrollWidth > element.clientWidth")).ShouldBeTrue();
        await region.FocusAsync();
        await page.Keyboard.PressAsync("ArrowRight");
        await page.WaitForFunctionAsync("document.querySelector('.players-table-region').scrollLeft > 0");
        (await region.EvaluateAsync<int>("element => element.scrollLeft")).ShouldBeGreaterThan(0);
        await AssertTargetsAsync(page);
        await page.GetByLabel("Search name or tryout number").FocusAsync();
        await page.Keyboard.TypeAsync("no matching player");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "No matching players" })).ToBeVisibleAsync();
        await InteractionHelpers.NavigateEnhancedAsync(page,
            () => page.GetByRole(AriaRole.Link, new() { Name = "Clear filters", Exact = true }).PressAsync("Enter"));
        await Expect(page.Locator("tbody .player-record-link")).ToHaveCountAsync(20);
    }

    [Fact]
    public async Task OrdinaryMemberCreatesEditsArchivesAndRestoresThroughRoutedFormAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await SeedingHelpers.SeedDraftClubAsync(fixture.AppHost, 0, 0, token);
        var member = await CreateMemberAsync(seed.ClubId, token);
        await using var context = await fixture.NewSignedInContextAsync(member, Password);
        var page = context.Pages[0];
        await InteractionHelpers.WithNavigationDiagnosticsAsync(page, async () =>
        {
            await page.GotoAsync(new Uri(fixture.BaseUri, "/players?returnToDraft=42").ToString());
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Return to draft", Exact = true })).ToHaveCountAsync(0);
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Import player list", Exact = true })).ToHaveCountAsync(0);
            await InteractionHelpers.NavigateEnhancedAsync(page,
                () => page.GetByRole(AriaRole.Link, new() { Name = "Add player", Exact = true }).ClickAsync());
            await Expect(page.Locator("#player-first-name")).ToBeVisibleAsync();
            new Uri(page.Url).AbsolutePath.ShouldBe("/players/new");
            await InteractionHelpers.NavigateEnhancedAsync(page, () => InteractionHelpers.ClickUntilAsync(page,
                page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }),
                () => page.Locator("#players-search").IsVisibleAsync()));
            await InteractionHelpers.NavigateEnhancedAsync(page,
                () => page.GetByRole(AriaRole.Link, new() { Name = "Add player", Exact = true }).ClickAsync());
            await page.Locator("#player-first-name").FillAsync("Directory");
            await page.Locator("#player-last-name").FillAsync("Member");
            await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
            await Expect(page.Locator(".alert-success")).ToContainTextAsync("Player created successfully.");
            await InteractionHelpers.NavigateEnhancedAsync(page,
                () => page.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true }).ClickAsync());
            await Expect(page.Locator("#player-first-name")).ToHaveValueAsync("Directory");
            new Uri(page.Url).AbsolutePath.ShouldEndWith("/edit");
            await page.Locator("#player-first-name").FillAsync("Corrected");
            await page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Corrected Member", Exact = true })).ToBeVisibleAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Archive", Exact = true }).ClickAsync();
            await page.Locator("#archive-confirm-checkbox").CheckAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Archive player", Exact = true }).ClickAsync();
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "All players are archived" })).ToBeVisibleAsync();
            await InteractionHelpers.NavigateEnhancedAsync(page,
                () => page.GetByRole(AriaRole.Link, new() { Name = "Archived 1", Exact = true }).ClickAsync());
            await page.GetByRole(AriaRole.Button, new() { Name = "Restore", Exact = true }).ClickAsync();
            await Expect(page.Locator(".alert-success")).ToContainTextAsync("Player restored.");
            await using var db = fixture.AppHost.CreateAdminContext();
            var saved = await db.Players.SingleAsync(row => row.ClubId == seed.ClubId, token);
            saved.FirstName.ShouldBe("Corrected");
            saved.LifecycleStatus.ShouldBe(Nova.SharedKernel.Enums.LifecycleStatus.Active);
        });
    }

    [Fact]
    public async Task DirectoryRecordAndFormPreserveCompleteDraftAndPlaceCorrectionReturnAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        var draft = await CreateCorrectionDraftAsync(seed);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await InteractionHelpers.WithNavigationDiagnosticsAsync(page, async () =>
        {
            await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place&placementPage=2").ToString());
            await InteractionHelpers.NavigateEnhancedAsync(page, () => page.Locator("a.place-row").First.ClickAsync());
            await Expect(page.Locator(".place-name")).ToBeVisibleAsync();
            var place = new Uri(page.Url).PathAndQuery;
            var draftDestination = $"/campaigns/{draft}?returnUrl=" + Uri.EscapeDataString(place);
            await page.GotoAsync(new Uri(fixture.BaseUri, draftDestination).ToString());
            var directory = await OpenDraftCorrectionDirectoryAsync(page, draftDestination);
            directory.ShouldContain($"returnToDraft={draft}");
            directory.ShouldContain("returnUrl=");
            await page.GotoAsync(new Uri(fixture.BaseUri, directory).ToString());
            await InteractionHelpers.NavigateEnhancedAsync(page, () => page.Locator("tbody .player-record-link").First.ClickAsync());
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "← Back to roster", Exact = true })).ToHaveAttributeAsync("href", directory);
            await InteractionHelpers.NavigateEnhancedAsync(page, () => page.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true }).ClickAsync());
            await Expect(page.Locator("#player-first-name")).ToBeVisibleAsync();
            await page.ReloadAsync();
            await Expect(page.Locator("#player-first-name")).ToBeVisibleAsync();
            await InteractionHelpers.NavigateEnhancedAsync(page, () => page.GetByRole(AriaRole.Link, new() { Name = "Return to players", Exact = true }).ClickAsync());
            await Expect(page.Locator(".players-paging")).ToContainTextAsync("Page 2 of 4");
            new Uri(page.Url).PathAndQuery.ShouldBe(directory);
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Return to draft", Exact = true }))
                .ToHaveAttributeAsync("href", draftDestination);
            await InteractionHelpers.NavigateEnhancedAsync(page, () => page.GetByRole(AriaRole.Link, new() { Name = "Return to draft", Exact = true }).ClickAsync());
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Roster preview", Exact = true })).ToBeVisibleAsync();
            await InteractionHelpers.NavigateEnhancedAsync(page, () => page.GetByRole(AriaRole.Link, new() { Name = "Return to placement", Exact = true }).ClickAsync());
            await Expect(page.Locator(".place-name")).ToBeVisibleAsync();
            new Uri(page.Url).PathAndQuery.ShouldBe(place);
            await InteractionHelpers.NavigateEnhancedAsync(page, () => page.GetByRole(AriaRole.Link, new() { Name = "Open player record", Exact = true }).ClickAsync());
            await InteractionHelpers.NavigateEnhancedAsync(page, () => page.GetByRole(AriaRole.Link, new() { Name = "Edit", Exact = true }).ClickAsync());
            await Expect(page.Locator("#player-first-name")).ToBeVisibleAsync();
            await InteractionHelpers.NavigateEnhancedAsync(page, () => page.GetByRole(AriaRole.Link, new() { Name = "Return to players", Exact = true }).ClickAsync());
            await InteractionHelpers.NavigateEnhancedAsync(page, () => page.GetByRole(AriaRole.Link, new() { Name = "Return to placement", Exact = true }).ClickAsync());
            await Expect(page.Locator(".place-name")).ToBeVisibleAsync();
            new Uri(page.Url).PathAndQuery.ShouldBe(place);
        });
    }

    private static async Task<string> OpenDraftCorrectionDirectoryAsync(IPage page, string draftDestination)
    {
        await InteractionHelpers.NavigateEnhancedAsync(page, () => page.GetByRole(AriaRole.Link, new() { Name = "View players", Exact = true }).ClickAsync());
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Return to draft", Exact = true }))
            .ToHaveAttributeAsync("href", draftDestination);
        await InteractionHelpers.NavigateEnhancedAsync(page, () => page.GetByRole(AriaRole.Link, new() { Name = "Next", Exact = true }).ClickAsync());
        await Expect(page.Locator(".players-paging")).ToContainTextAsync("Page 2 of 4");
        var directory = new Uri(page.Url).PathAndQuery;
        directory.ShouldContain("page=2");
        return directory;
    }

    private async Task<long> CreateCorrectionDraftAsync(SeededPlacementWorkspace seed)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = fixture.AppHost.CreateAdminContext();
        var active = await db.Campaigns.SingleAsync(row => row.CampaignId == seed.CampaignId, token);
        var draft = new Nova.Entities.CampaignEntity
        {
            CreationOperationId = Guid.CreateVersion7(),
            ClubId = seed.ClubId,
            SeasonId = active.SeasonId,
            CreatedById = seed.AdminUserId,
            Name = "Directory correction draft",
            StartDate = active.StartDate
        };
        db.Campaigns.Add(draft);
        await db.SaveChangesAsync(token);
        return draft.CampaignId;
    }

    private static async Task AssertDirectoryAttachedAsync(IPage page)
    {
        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Archive", Exact = true }).First,
            () => page.Locator("#archive-confirm-checkbox").IsVisibleAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(page.Locator("#archive-confirm-checkbox")).ToHaveCountAsync(0);
    }

    private async Task<string> CreateMemberAsync(long clubId, CancellationToken token)
    {
        using var client = fixture.AppHost.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail("directory-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, token);
        await SeedingHelpers.UpdateUserAsync(fixture.AppHost, email, clubId, token, firstName: "Club", lastName: "Member");
        return email;
    }

    private static async Task AssertTargetsAsync(IPage page)
    {
        var targets = page.Locator(".players-directory a, .players-directory button, .players-directory select, .players-directory input:not([type=hidden])");
        var tooSmall = await targets.EvaluateAllAsync<string[]>("""
            nodes => nodes.filter(n => !n.disabled && n.getBoundingClientRect().width > 0)
              .filter(n => { const r = n.getBoundingClientRect(); return r.width < 44 || r.height < 44; })
              .map(n => n.textContent || n.id)
            """);
        tooSmall.ShouldBeEmpty();
    }
}
