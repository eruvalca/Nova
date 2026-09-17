using Nova.Integration.Tests.Http;

namespace Nova.Browser.Tests;

/// <summary>Captures the Players surface on its actual authenticated shell for design comparison.</summary>
[Collection(BrowserSuiteCollection.Name)]
public sealed class PlayersDirectoryEvidenceTests(BrowserSuiteFixture fixture)
{
    [Fact]
    public async Task CaptureDirectoryOnItsOwnShellAsync()
    {
        var output = Environment.GetEnvironmentVariable("NOVA_PLAYERS_EVIDENCE");
        if (string.IsNullOrWhiteSpace(output)) { Assert.Skip("Set NOVA_PLAYERS_EVIDENCE to a capture directory."); }
        var seed = await SeedingHelpers.SeedDraftClubAsync(fixture.AppHost, 121, 0, TestContext.Current.CancellationToken);
        await using var browser = await fixture.NewSignedInContextAsync(seed.AdminEmail, "Test#Passw0rd!");
        var page = browser.Pages[0];
        Directory.CreateDirectory(output);
        await page.SetViewportSizeAsync(1440, 1000);
        await page.GotoAsync(new Uri(fixture.BaseUri, "/players").ToString());
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Player 01", Exact = true })).ToBeVisibleAsync();
        await page.Locator(".players-result-summary").ClickAsync();
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "desktop.png"), FullPage = true });
        await page.SetViewportSizeAsync(1280, 1000);
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "desktop-1280.png"), FullPage = true });
        await page.SetViewportSizeAsync(390, 844);
        await page.ScreenshotAsync(new() { Path = Path.Combine(output, "mobile.png"), FullPage = true });
    }
}
