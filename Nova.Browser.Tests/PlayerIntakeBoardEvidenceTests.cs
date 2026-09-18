using Nova.Integration.Tests.Http;

namespace Nova.Browser.Tests;

/// <summary>
/// Captures the manual intake board on its actual authenticated shell so a design reference and the
/// curated finish evidence come from the surface's own composition rather than another page's.
/// </summary>
[Collection(BrowserSuiteCollection.Name)]
public sealed class PlayerIntakeBoardEvidenceTests(BrowserSuiteFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>Captures the pristine create board, its committed receipt, and the unsettled state.</summary>
    [Fact]
    public async Task CaptureIntakeBoardStatesAsync()
    {
        var output = Environment.GetEnvironmentVariable("NOVA_PLAYERS_EVIDENCE");
        if (string.IsNullOrWhiteSpace(output)) { Assert.Skip("Set NOVA_PLAYERS_EVIDENCE to a capture directory."); }

        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedingHelpers.SeedDraftClubAsync(fixture.AppHost, 0, 0, ct);
        await SeedingHelpers.SeedSeasonAndCampaignAsync(fixture.AppHost, seed.ClubId, seed.AdminEmail, "Intake", ct);
        Directory.CreateDirectory(output);

        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync(new Uri(fixture.BaseUri, "/players/new").ToString());
        await Expect(page.Locator("#player-first-name")).ToBeVisibleAsync();
        await Expect(page.Locator(".intake-consequence")).ToContainTextAsync("joins");
        await page.Locator(".intake-heading").ClickAsync();
        // The comp frame is the viewport, so the reference is captured without full-page growth.
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "intake-board-desktop.png") });
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "intake-board-desktop-full.png"), FullPage = true });

        await page.SetViewportSizeAsync(390, 844);
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "intake-board-mobile.png") });
        await page.SetViewportSizeAsync(1440, 1000);

        await page.Locator("#player-first-name").FillAsync("Avery");
        await page.Locator("#player-last-name").FillAsync("Evidence");
        await page.Locator("#player-dob").FillAsync("2012-04-01");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        await Expect(page.Locator("#intake-receipt-heading")).ToContainTextAsync("Player added");
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "intake-board-receipt.png"), FullPage = true });
    }
}
