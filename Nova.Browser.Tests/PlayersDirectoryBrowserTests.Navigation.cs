using Nova.Integration.Tests.Http;
using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class PlayersDirectoryBrowserTests
{
    [Fact]
    public async Task EnhancedFormResponsePreservesTheMountedFormAndItsDraftAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await SeedingHelpers.SeedDraftClubAsync(fixture.AppHost, 1, 0, token);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, "/players").ToString());
        await AssertDirectoryAttachedAsync(page);
        await page.EvaluateAsync("""
            () => {
                window.__playersEnhancedLoads = 0;
                Blazor.addEventListener('enhancednavigationend', () => window.__playersEnhancedLoads++);
            }
            """);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var completed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync("**/players/new", async route =>
        {
            try
            {
                var response = await route.FetchAsync();
                response.Status.ShouldBe(200);
                held.TrySetResult();
                await release.Task.WaitAsync(token);
                await route.FulfillAsync(new() { Response = response });
                await response.DisposeAsync();
            }
            finally { completed.TrySetResult(); }
        });
        try
        {
            await page.GetByRole(AriaRole.Link, new() { Name = "Add player", Exact = true }).ClickAsync();
            await held.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            await Expect(page.Locator("#player-first-name")).ToBeVisibleAsync();
            await page.Locator("#player-first-name").FillAsync("Held draft");
            await page.Locator("#player-last-name").FocusAsync();
            release.TrySetResult();
            await page.WaitForFunctionAsync("window.__playersEnhancedLoads > 0");
            await Expect(page.Locator("#player-first-name")).ToHaveValueAsync("Held draft");
            new Uri(page.Url).AbsolutePath.ShouldBe("/players/new");
        }
        finally
        {
            release.TrySetResult();
            if (held.Task.IsCompleted) { await completed.Task.WaitAsync(TimeSpan.FromSeconds(30), token); }
            await page.UnrouteAsync("**/players/new");
        }
    }
}
