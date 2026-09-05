using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Http;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>Exercises the saved Draft journey across interactive navigation and real lifecycle commands.</summary>
[Collection(BrowserSuiteCollection.Name)]
public sealed class CampaignDraftBrowserTests(BrowserSuiteFixture fixture)
{
    [Fact]
    public async Task Draft_OpensIntoRoster_AfterCreationAndCorrectionRoundTrips()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedingHelpers.SeedDraftClubAsync(fixture.AppHost, 24, 3, ct);
        await using var browser = await fixture.NewSignedInContextAsync(seed.AdminEmail, "Test#Passw0rd!");
        var page = browser.Pages[0];
        await page.SetViewportSizeAsync(1505, 1045);
        await CreateDraftAsync(page, "Fall evaluation");
        var draftUrl = page.Url;
        await page.GotoAsync(new Uri(fixture.BaseUri, "/campaigns").ToString());
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Prepare draft", Exact = true })).ToBeVisibleAsync();
        await CaptureAsync(page, "directory-desktop");
        await page.SetViewportSizeAsync(390, 844);
        await CaptureAsync(page, "directory-mobile");
        await Expect(page.GetByRole(AriaRole.Columnheader)).ToHaveCountAsync(5);
        foreach (var header in new[] { "Name", "Dates", "Status", "Participants", "Next action" })
        {
            await Expect(page.GetByRole(AriaRole.Columnheader, new() { Name = header, Exact = true })).ToHaveCountAsync(1);
        }
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
        await page.SetViewportSizeAsync(1505, 1045);
        await page.GetByRole(AriaRole.Link, new() { Name = "Prepare draft", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "View players", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Return to draft", Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Return to draft", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Roster preview" })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "View teams", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Return to draft", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Review opening", Exact = true }).ClickAsync();
        var commit = page.GetByRole(AriaRole.Button, new() { Name = "Open campaign and enroll 24 players", Exact = true });
        await Expect(commit).ToBeEnabledAsync();
        page.Url.ShouldContain("review=open");
        await CaptureAsync(page, "preparation-desktop");
        await page.SetViewportSizeAsync(390, 844);
        await Expect(commit).ToBeEnabledAsync();
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
        await CaptureAsync(page, "preparation-mobile");
        await commit.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Roster", Exact = true })).ToBeFocusedAsync();
        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Campaign opened and enrolled 24 players." })).ToBeVisibleAsync();
        page.Url.ShouldContain("/roster");
        await CaptureAsync(page, "roster-mobile");
        await page.SetViewportSizeAsync(1505, 1045);
        await CaptureAsync(page, "roster-desktop");
        var rosterPath = new Uri(page.Url).AbsolutePath;
        var drawer = page.Locator("aside.participant-drawer");
        await InteractionHelpers.ClickUntilAsync(page, page.Locator("tbody tr[id^='roster-row-']").First,
            () => drawer.IsVisibleAsync());
        await Expect(page.Locator("#participant-drawer-position")).ToHaveTextAsync("1 of 24");
        new Uri(page.Url).AbsolutePath.ShouldBe(rosterPath);
        await page.GetByRole(AriaRole.Button, new() { Name = "Next participant", Exact = true }).ClickAsync();
        await Expect(page.Locator("#participant-drawer-position")).ToHaveTextAsync("2 of 24");
        new Uri(page.Url).AbsolutePath.ShouldBe(rosterPath);
        await InteractionHelpers.ClickUntilAsync(page,
            page.GetByRole(AriaRole.Button, new() { Name = "Close participant details", Exact = true }),
            () => drawer.IsHiddenAsync());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Roster", Exact = true })).ToBeVisibleAsync();
        new Uri(page.Url).AbsolutePath.ShouldBe(rosterPath);
        await page.GotoAsync(new Uri(fixture.BaseUri, rosterPath + "?tab=closeout").ToString());
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Roster", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("#roster-search")).ToBeVisibleAsync();
        await Expect(page.Locator("tbody tr[id^='roster-row-']")).ToHaveCountAsync(24);
        await using var context = fixture.AppHost.CreateAdminContext();
        var campaign = await context.Campaigns.SingleAsync(item => item.ClubId == seed.ClubId, ct);
        campaign.Status.ShouldBe(CampaignStatus.Active);
        (await context.PlayerCampaignAssignments.CountAsync(item => item.CampaignId == campaign.CampaignId, ct)).ShouldBe(24);
        await page.GotoAsync(draftUrl);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Review opening", Exact = true })).ToHaveCountAsync(0);
        var receiptHandoff = await page.EvaluateAsync<bool>("""
            async () => {
                const workspace = await import('/_content/Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor.js');
                const recovery = await import('/_content/Nova.UI/Features/Campaigns/Pages/CampaignEntry.razor.js');
                const scope = 'receipt-regression:' + crypto.randomUUID();
                const campaignId = 987654321;
                const operationId = crypto.randomUUID();
                const receipt = { campaignId, operationId, enrolledPlayerCount: 24 };
                recovery.write(scope, 'receipt:' + campaignId, receipt);
                try {
                    let focusCount = 0;
                    const heading = { focus: () => focusCount++ };
                    const first = workspace.readOpeningReceipt(scope, campaignId, heading);
                    const second = workspace.readOpeningReceipt(scope, campaignId, heading);
                    if (focusCount !== 0) return false;
                    if (JSON.stringify(first) !== JSON.stringify(receipt) || JSON.stringify(second) !== JSON.stringify(receipt)) return false;
                    workspace.acknowledgeOpeningReceipt(scope, campaignId, crypto.randomUUID());
                    if (workspace.readOpeningReceipt(scope, campaignId)?.operationId !== operationId) return false;
                    workspace.acknowledgeOpeningReceipt(scope, campaignId, operationId);
                    return workspace.readOpeningReceipt(scope, campaignId) === null;
                } finally {
                    recovery.clear(scope);
                }
            }
            """);
        receiptHandoff.ShouldBeTrue("reading preserves the receipt until its own operation is acknowledged");
    }

    [Fact]
    public async Task Draft_IsUnavailableToOrdinaryMember_AndWarningDoesNotBlockAdministrator()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedingHelpers.SeedDraftClubAsync(fixture.AppHost, 1, 0, ct);
        await using var browser = await fixture.NewSignedInContextAsync(seed.AdminEmail, "Test#Passw0rd!");
        var page = browser.Pages[0];
        const string name = "North Shore autumn evaluation and placement preparation for returning and newly registered players";
        await CreateDraftAsync(page, name);
        var draftUrl = page.Url;
        await page.GetByRole(AriaRole.Link, new() { Name = "Review opening", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Open campaign and enroll 1 player" })).ToBeEnabledAsync();
        await Expect(page.GetByText("No active teams. Evaluation can begin; add teams before placement.", new() { Exact = true })).ToBeVisibleAsync();
        await CaptureAsync(page, "preparation-warning-long");
        await page.SetViewportSizeAsync(390, 844);
        (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
        await CaptureAsync(page, "preparation-warning-long-mobile");

        using var memberClient = fixture.AppHost.CreateNovaHttpClient();
        var memberEmail = SeedingHelpers.UniqueEmail("draft-member");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(memberClient, memberEmail, "Test#Passw0rd!", ct);
        await SeedingHelpers.UpdateUserAsync(fixture.AppHost, memberEmail, seed.ClubId, ct, firstName: "Sam", lastName: "Member");
        await using var member = await fixture.NewSignedInContextAsync(memberEmail, "Test#Passw0rd!");
        var memberPage = member.Pages[0];
        await memberPage.GotoAsync(draftUrl);
        await Expect(memberPage.GetByRole(AriaRole.Heading, new() { Name = "Campaign not found", Exact = true })).ToBeVisibleAsync();
        await Expect(memberPage.GetByText(name, new() { Exact = true })).ToHaveCountAsync(0);
        await memberPage.GotoAsync(new Uri(fixture.BaseUri, "/campaigns/9223372036854775807").ToString());
        await Expect(memberPage.GetByRole(AriaRole.Heading, new() { Name = "Campaign not found", Exact = true })).ToBeVisibleAsync();
        await memberPage.GotoAsync(new Uri(fixture.BaseUri, "/campaigns?view=draft&page=3").ToString());
        await Expect(memberPage.GetByLabel("Campaign view", new() { Exact = true })).ToHaveValueAsync("all");
        await Expect(memberPage.GetByRole(AriaRole.Heading, new() { Name = "No campaigns available", Exact = true })).ToBeVisibleAsync();
        await Expect(memberPage).ToHaveURLAsync(new System.Text.RegularExpressions.Regex(@"[?&]view=all(?:&|$)"));
        memberPage.Url.ShouldContain("page=1");
        await Expect(memberPage.GetByRole(AriaRole.Option, new() { Name = "Draft", Exact = true })).ToHaveCountAsync(0);
        await Expect(memberPage.GetByText(name, new() { Exact = true })).ToHaveCountAsync(0);
        await CaptureAsync(memberPage, "directory-member-empty");
    }

    [Fact]
    public async Task Draft_DeletesWithoutDeletingTeams_AfterInlineTeamCreation()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedingHelpers.SeedDraftClubAsync(fixture.AppHost, 0, 0, ct);
        await using var browser = await fixture.NewSignedInContextAsync(seed.AdminEmail, "Test#Passw0rd!");
        var page = browser.Pages[0];
        await CreateDraftAsync(page, "Winter preparation");
        await page.GetByRole(AriaRole.Link, new() { Name = "Review opening", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Open campaign and enroll 0 players" })).ToBeDisabledAsync();
        await CaptureAsync(page, "preparation-blocked");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create team", Exact = true }).ClickAsync();
        await page.GetByLabel("Team name").FillAsync("Winter blue");
        await page.GetByLabel("Graduation year").FillAsync("2030");
        await page.Locator("form").GetByRole(AriaRole.Button, new() { Name = "Create team", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Status).Filter(new() { HasText = "Winter blue created for your club." })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete draft", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Your club's teams will remain.", new() { Exact = false })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Delete draft permanently", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Campaigns", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByText("Draft deleted", new() { Exact = false })).ToBeVisibleAsync();
        await CaptureAsync(page, "directory-empty");
        await using var context = fixture.AppHost.CreateAdminContext();
        (await context.Campaigns.AnyAsync(item => item.ClubId == seed.ClubId, ct)).ShouldBeFalse();
        (await context.Teams.SingleAsync(item => item.ClubId == seed.ClubId, ct)).Name.ShouldBe("Winter blue");
    }

    private async Task CreateDraftAsync(IPage page, string name)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, "/campaigns/new").ToString());
        var submit = page.GetByRole(AriaRole.Button, new() { Name = "Create campaign", Exact = true });
        await Expect(submit).ToBeEnabledAsync();
        await page.GetByLabel("Campaign name", new() { Exact = true }).FillAsync(name);
        await page.Locator("#campaign-start-date").FillAsync("2026-09-12");
        await page.Locator("#campaign-planned-end-date").FillAsync("2026-09-26");
        await page.Locator("#inline-season-name").FillAsync("2026–27 Season");
        await page.Locator("#inline-season-start-date").FillAsync("2026-08-01");
        if (name == "Fall evaluation")
        {
            await CaptureAsync(page, "creation-desktop");
            await page.SetViewportSizeAsync(390, 844);
            await CaptureAsync(page, "creation-mobile");
            await page.SetViewportSizeAsync(1505, 1045);
        }

        await submit.ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = name, Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true })).ToBeEnabledAsync();
    }

    private static async Task CaptureAsync(IPage page, string name)
    {
        var directory = Path.Combine(Path.GetTempPath(), "nova-issue-196");
        Directory.CreateDirectory(directory);
        await page.Mouse.MoveAsync((page.ViewportSize?.Width ?? 1280) - 5, 5);
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"{name}.png"), FullPage = true });
        if (name == "preparation-desktop")
        {
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, "hero.png") });
            await page.SetViewportSizeAsync(1440, 1045);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, "desktop.png"), FullPage = true });
            await page.SetViewportSizeAsync(1505, 1045);
        }
    }
}
