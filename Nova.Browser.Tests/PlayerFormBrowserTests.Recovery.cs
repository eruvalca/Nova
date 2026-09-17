using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Features.Players;
using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class PlayerFormBrowserTests
{
    /// <summary>A browser-lost acknowledgement retries the exact command and recovers one committed player.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData("validation")]
    [InlineData("conflict")]
    public async Task PlayerFormRetriesSameOperationAfterLostAcknowledgementAsync(string? retryProblem)
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(ct);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersInWebAssemblyAsync(page);
        var requests = new ConcurrentQueue<string>();
        long committedPlayerId = 0;
        await page.RouteAsync("**/api/players", async route =>
        {
            if (!string.Equals(route.Request.Method, "POST", StringComparison.Ordinal)) { await route.ContinueAsync(); return; }
            requests.Enqueue(route.Request.PostData ?? string.Empty);
            if (requests.Count == 1)
            {
                var committed = await route.FetchAsync();
                committed.Status.ShouldBe(201);
                committedPlayerId = JsonSerializer.Deserialize<PlayerCreationCompletion>(await committed.TextAsync(), JsonSerializerOptions.Web)
                    .ShouldNotBeNull().Player.PlayerId;
                await committed.DisposeAsync();
                await route.AbortAsync("failed");
            }
            else if (retryProblem is not null && requests.Count == 2)
            {
                var validation = string.Equals(retryProblem, "validation", StringComparison.Ordinal);
                var body = validation ? "{not-json" : ContradictoryDuplicateBody(route.Request.PostData!, committedPlayerId);
                await route.FulfillAsync(new() { Status = validation ? 422 : 409, ContentType = "application/json", Body = body });
            }
            else { await route.ContinueAsync(); }
        });
        await OpenCreationFormAsync(page);
        await FillCreationFormAsync(page, "Retry");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        await Expect(page.Locator("[role=alert]")).ToContainTextAsync("retry it unchanged");
        await Expect(page.Locator("#player-first-name")).ToBeDisabledAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        if (retryProblem is not null)
        {
            await Expect(page.Locator("[role=alert]")).ToContainTextAsync("invalid player creation evidence");
            await Expect(page.Locator("#player-first-name")).ToBeDisabledAsync();
            await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        }
        await Expect(page.Locator("div.alert-success[role=status]")).ToContainTextAsync("Player created successfully.");
        var sent = requests.ToArray();
        sent.Length.ShouldBe(retryProblem is not null ? 3 : 2);
        sent.ShouldAllBe(request => string.Equals(request, sent[0], StringComparison.Ordinal));
        await using var db = fixture.AppHost.CreateAdminContext();
        (await db.Players.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(1);
        (await db.PlayerCreationReceipts.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(1);
    }

    private static string ContradictoryDuplicateBody(string request, long playerId)
    {
        var input = JsonSerializer.Deserialize<CreatePlayerInput>(request, JsonSerializerOptions.Web).ShouldNotBeNull();
        var duplicate = PlayerCreationProblems.Duplicate(input.OperationId, playerId, Nova.SharedKernel.Enums.LifecycleStatus.Active);
        return JsonSerializer.Serialize(new Dictionary<string, object?>(duplicate.Extensions!, StringComparer.Ordinal)
        {
            ["status"] = 409,
            ["errors"] = new Dictionary<string, string[]>(StringComparer.Ordinal) { ["FirstName"] = ["Invalid value"] }
        });
    }

    /// <summary>Expiry explains directory reconciliation while the original uncertain command stays retained.</summary>
    [Fact]
    public async Task PlayerFormExpiryRetainsCommandWithoutRetryGuidanceAsync()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAdminAsync(ct);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, Password);
        var page = context.Pages[0];
        await OpenPlayersInWebAssemblyAsync(page);
        var requests = new ConcurrentQueue<string>();
        var expiry = PlayerCreationProblems.Expired();
        var expiryBody = JsonSerializer.Serialize(new Dictionary<string, object?>(expiry.Extensions!, StringComparer.Ordinal)
        {
            ["status"] = 409,
            ["title"] = "Conflict",
            ["detail"] = expiry.Detail
        });
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
            else
            {
                await route.FulfillAsync(new() { Status = 409, ContentType = "application/problem+json", Body = expiryBody });
            }
        });
        await OpenCreationFormAsync(page);
        await FillCreationFormAsync(page, "Expired");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        await Expect(page.Locator("[role=alert]")).ToContainTextAsync("retry it unchanged");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        await Expect(page.Locator("[role=alert]")).ToContainTextAsync("Review the Players directory");
        await Expect(page.Locator("[role=alert]")).Not.ToContainTextAsync("retry it unchanged");
        await Expect(page.Locator("[role=alert]")).ToContainTextAsync("The original addition is still retained");
        await Expect(page.Locator("#player-first-name")).ToBeDisabledAsync();
        await CancelCreationFormAsync(page);
        await OpenCreationFormAsync(page);
        await Expect(page.Locator("#player-first-name")).ToBeDisabledAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        await Expect(page.Locator("[role=alert]")).ToContainTextAsync("has expired");
        await Expect(page.Locator("[role=alert]")).Not.ToContainTextAsync("retry it unchanged");
        var sent = requests.ToArray();
        sent.Length.ShouldBe(3);
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
        await CancelCreationFormAsync(page);
        await OpenCreationFormAsync(page);
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "View existing player", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.Locator("#player-first-name")).ToBeEnabledAsync();
        await page.Locator("#player-first-name").FillAsync("Corrected");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        await Expect(page.GetByText("Corrected Recovery", new() { Exact = true })).ToBeVisibleAsync();
        await using var db = fixture.AppHost.CreateAdminContext();
        (await db.Players.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(2);
        (await db.PlayerCreationReceipts.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(3);
    }

    /// <summary>The duplicate detail round trip restores the originating roster filters.</summary>
    [Fact]
    public async Task PlayerFormDuplicateDetailPreservesRosterReturnContextAsync()
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

        const string RosterUrl = "/players?view=archived&search=Original";
        await page.GotoAsync(new Uri(fixture.BaseUri, RosterUrl).ToString());
        await OpenCreationFormAsync(page);
        await FillCreationFormAsync(page, "Original");
        await page.GetByRole(AriaRole.Button, new() { Name = "Create player", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "View existing player", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Original Recovery", Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "← Back to roster", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Players", Exact = true })).ToBeVisibleAsync();
        await OpenCreationFormAsync(page);
        new Uri(page.Url).PathAndQuery.ShouldBe(RosterUrl);
        await Expect(page.Locator("#players-search")).ToHaveValueAsync("Original");
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "View existing player", Exact = true })).ToHaveCountAsync(0);
    }

    private async Task OpenPlayersInWebAssemblyAsync(IPage page)
    {
        await OpenPlayersAsync(page);
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, async () =>
        {
            await OpenCreationFormAsync(page);
            await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
            await Expect(page.Locator("#player-first-name")).ToHaveCountAsync(0);
        });
    }

    private static async Task CancelCreationFormAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        // The open helper's initial visibility probe must not accept the form being closed.
        await Expect(page.Locator("#player-first-name")).ToHaveCountAsync(0);
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
