using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Nova.SharedKernel.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignPlaceBrowserTests
{
    [Fact]
    public async Task InvalidRecoveryDataCanBeDiscardedExplicitlyBeforeANewDeliberateSaveAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, token);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await OpenFirstPlacementAsync(page, seed.CampaignId);
        var assignmentId = SelectedAssignmentId(page);
        var key = $"nova:placement:{seed.AdminUserId}:{seed.ClubId}:{seed.CampaignId}";
        await page.EvaluateAsync("key => sessionStorage.setItem(key, '{invalid')", key);
        await page.ReloadAsync();
        var discard = page.GetByRole(AriaRole.Button, new() { Name = "Discard invalid data and refresh", Exact = true });
        await Expect(discard).ToBeVisibleAsync();
        await Expect(page.Locator(".place-recovery")).ToContainTextAsync("does not undo a save or prove it failed");
        await Expect(SaveButton(page)).ToHaveCountAsync(0);
        (await page.EvaluateAsync<string>("key => sessionStorage.getItem(key)", key)).ShouldBe("{invalid");
        var directory = Environment.GetEnvironmentVariable("NOVA_PLACE_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(directory)) { await CaptureInvalidStorageAsync(page, directory, seed.CampaignId); }
        await discard.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator("#place-outcome")).ToBeEnabledAsync();
        await Expect(page.Locator(".alert-danger")).ToContainTextAsync("earlier save result remains unknown");
        (await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", key)).ShouldBeNull();
        await using var verify = fixture.AppHost.CreateAdminContext();
        (await verify.PlacementMutationReceipts.CountAsync(row => row.ClubId == seed.ClubId, token)).ShouldBe(0);
        await page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.NotSelected));
        await SaveButton(page).ClickAsync();
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Placement saved.");
        var saved = await verify.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, token);
        saved.PlacementOutcome.ShouldBe(PlacementOutcome.NotSelected);
        (await verify.PlacementMutationReceipts.CountAsync(row => row.ClubId == seed.ClubId, token)).ShouldBe(1);
    }

    [Fact]
    public async Task ClosedInvalidRecoveryDataCanBeDiscardedWithoutChangingFinalPlacementsAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, token);
        await using var db = fixture.AppHost.CreateAdminContext();
        var before = await db.PlayerCampaignAssignments.AsNoTracking().Where(row => row.CampaignId == seed.ClosedCampaignId)
            .OrderBy(row => row.PlayerCampaignAssignmentId)
            .Select(row => new { row.PlayerCampaignAssignmentId, row.ConcurrencyToken, row.PlacementOutcome, row.TeamId, row.DecisionRecordedById, row.DecisionRecordedAt })
            .ToListAsync(token);
        before.ShouldNotBeEmpty();
        var receipts = await db.PlacementMutationReceipts.CountAsync(row => row.ClubId == seed.ClubId, token);
        var activity = await db.ActivityEvents.CountAsync(row => row.ClubId == seed.ClubId, token);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        var key = $"nova:placement:{seed.AdminUserId}:{seed.ClubId}:{seed.ClosedCampaignId}";
        await page.EvaluateAsync("key => sessionStorage.setItem(key, '{invalid')", key);
        await page.GotoAsync(new Uri(fixture.BaseUri,
            $"/campaigns/{seed.ClosedCampaignId}?tab=place&placementParticipant={before[0].PlayerCampaignAssignmentId}").ToString());
        var discard = page.GetByRole(AriaRole.Button, new() { Name = "Discard invalid data and refresh", Exact = true });
        await Expect(discard).ToBeEnabledAsync();
        await AssertPlaceSearchAttachedAsync(page);
        await Expect(page.Locator("#place-invalid-recovery-warning")).ToContainTextAsync("this campaign remains read-only");
        await Expect(page.Locator(".place-readonly")).ToContainTextAsync("Read-only — campaign is closed.");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Retry storage", Exact = true })).ToBeEnabledAsync();
        await Expect(page.Locator("#place-outcome, #place-team")).ToHaveCountAsync(0);
        var directory = Environment.GetEnvironmentVariable("NOVA_PLACE_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(directory)) { await CaptureInvalidStorageAsync(page, directory, seed.ClosedCampaignId, "closed-invalid-storage"); }
        await discard.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator(".alert-danger")).ToContainTextAsync("earlier save result remains unknown");
        await Expect(discard).ToHaveCountAsync(0);
        (await page.EvaluateAsync<string?>("key => sessionStorage.getItem(key)", key)).ShouldBeNull();
        await Expect(page.Locator("#place-outcome, #place-team")).ToHaveCountAsync(0);
        await Expect(SaveButton(page)).ToHaveCountAsync(0);
        var after = await db.PlayerCampaignAssignments.AsNoTracking().Where(row => row.CampaignId == seed.ClosedCampaignId)
            .OrderBy(row => row.PlayerCampaignAssignmentId)
            .Select(row => new { row.PlayerCampaignAssignmentId, row.ConcurrencyToken, row.PlacementOutcome, row.TeamId, row.DecisionRecordedById, row.DecisionRecordedAt })
            .ToListAsync(token);
        after.ShouldBe(before);
        (await db.PlacementMutationReceipts.CountAsync(row => row.ClubId == seed.ClubId, token)).ShouldBe(receipts);
        (await db.ActivityEvents.CountAsync(row => row.ClubId == seed.ClubId, token)).ShouldBe(activity);
    }

    private static async Task CaptureInvalidStorageAsync(IPage page, string directory, long campaignId, string state = "invalid-storage")
    {
        await CapturePlacementStateViewportsAsync(page, state, directory, campaignId);
        await page.Locator(".place-recovery").EvaluateAsync("element => element.scrollIntoView({block: 'center'})");
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, state + "-mobile-actions.png"), FullPage = false });
        var geometry = await page.Locator(".place-recovery").EvaluateAsync<string>(
            "element => JSON.stringify({cssWidth:innerWidth,cssHeight:innerHeight,scrollY,actions:[...element.querySelectorAll('button')].map(button=>({text:button.textContent.trim(),rect:button.getBoundingClientRect().toJSON()}))})");
        await File.WriteAllTextAsync(Path.Combine(directory, state + "-mobile-actions.json"), geometry, TestContext.Current.CancellationToken);
    }

    [Fact]
    public async Task PlayerDetailReturnContextFollowsInteractiveQueryNavigationAndBrowserHistoryAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, token);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await OpenFirstPlacementAsync(page, seed.CampaignId);
        var first = new Uri(page.Url).PathAndQuery;
        await using var db = fixture.AppHost.CreateAdminContext();
        var assignmentId = SelectedAssignmentId(page);
        var playerId = await db.PlayerCampaignAssignments.Where(row => row.PlayerCampaignAssignmentId == assignmentId).Select(row => row.PlayerId).SingleAsync(token);
        var detail = $"/players/{playerId}?returnUrl=";
        await page.GotoAsync(new Uri(fixture.BaseUri, detail + Uri.EscapeDataString(first)).ToString());
        var back = page.GetByRole(AriaRole.Link, new() { Name = "← Back to roster", Exact = true });
        await Expect(back).ToHaveAttributeAsync("href", first);
        var cancel = page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true });
        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }), () => cancel.IsVisibleAsync());
        await cancel.ClickAsync();
        const string Second = "/players?search=Changed";
        await page.EvaluateAsync("url => Blazor.navigateTo(url)", detail + Uri.EscapeDataString(Second));
        await page.WaitForURLAsync(url => url.Contains("Changed", StringComparison.Ordinal), new() { WaitUntil = WaitUntilState.Commit });
        await Expect(back).ToHaveAttributeAsync("href", Second);
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(back).ToHaveAttributeAsync("href", first);
        await page.GoForwardAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(back).ToHaveAttributeAsync("href", Second);
    }
}
