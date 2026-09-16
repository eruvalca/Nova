using System.Collections.Concurrent;
using Microsoft.EntityFrameworkCore;
using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class PlayerFormBrowserTests
{
    /// <summary>A browser-lost acknowledgement retries the exact command and recovers one committed player.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PlayerFormRetriesSameOperationAfterLostAcknowledgementAsync(bool malformedValidationOnRetry)
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(ct);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersAsync(page);
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, async () =>
        {
            await OpenCreationFormAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
            await Expect(page.Locator("#player-first-name")).ToHaveCountAsync(0);
        });
        var requests = new ConcurrentQueue<string>();
        await page.RouteAsync("**/api/players", async route =>
        {
            if (!string.Equals(route.Request.Method, "POST", StringComparison.Ordinal)) { await route.ContinueAsync(); return; }
            requests.Enqueue(route.Request.PostData ?? string.Empty);
            if (requests.Count == 1)
            {
                var committed = await route.FetchAsync();
                committed.Status.ShouldBe(201);
                await committed.DisposeAsync();
                await route.AbortAsync("failed");
            }
            else if (malformedValidationOnRetry && requests.Count == 2)
            {
                await route.FulfillAsync(new() { Status = 422, ContentType = "application/json", Body = "{not-json" });
            }
            else { await route.ContinueAsync(); }
        });
        await OpenCreationFormAsync(page);
        await FillCreationFormAsync(page, "Retry");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        await Expect(page.Locator("[role=alert]")).ToContainTextAsync("retry it unchanged");
        await Expect(page.Locator("#player-first-name")).ToBeDisabledAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        if (malformedValidationOnRetry)
        {
            await Expect(page.Locator("[role=alert]")).ToContainTextAsync("invalid player creation evidence");
            await Expect(page.Locator("#player-first-name")).ToBeDisabledAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        }
        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Player created successfully.");
        var sent = requests.ToArray();
        sent.Length.ShouldBe(malformedValidationOnRetry ? 3 : 2);
        sent.ShouldAllBe(request => string.Equals(request, sent[0], StringComparison.Ordinal));
        await using var db = fixture.AppHost.CreateAdminContext();
        (await db.Players.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(1);
        (await db.PlayerCreationReceipts.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(1);
    }

    /// <summary>Duplicate rejection exposes the existing record and permits a corrected new submission.</summary>
    [Fact]
    public async Task PlayerFormDuplicateCanBeCorrectedWithoutOverrideAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(ct);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersAsync(page);
        await OpenCreationFormAsync(page);
        await FillCreationFormAsync(page, "Original");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Player created successfully.");
        await OpenCreationFormAsync(page);
        await FillCreationFormAsync(page, " original ");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "View existing player", Exact = true })).ToBeVisibleAsync();
        await Expect(page.Locator("#player-first-name")).ToBeEnabledAsync();
        await page.Locator("#player-first-name").FillAsync("Corrected");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Corrected Recovery", new() { Exact = true })).ToBeVisibleAsync();
        await using var db = fixture.AppHost.CreateAdminContext();
        (await db.Players.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(2);
        (await db.PlayerCreationReceipts.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(3);
    }

    private static async Task OpenCreationFormAsync(IPage page)
        => await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Add player", Exact = true }),
            () => page.Locator("#player-first-name").IsVisibleAsync());

    private static async Task FillCreationFormAsync(IPage page, string firstName)
    {
        await page.Locator("#player-first-name").FillAsync(firstName);
        await page.Locator("#player-last-name").FillAsync("Recovery");
        await page.Locator("#player-dob").FillAsync("2012-01-01");
    }
}
