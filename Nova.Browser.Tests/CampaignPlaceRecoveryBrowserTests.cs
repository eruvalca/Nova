using System.Globalization;
using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignPlaceBrowserTests
{
    [Fact]
    public async Task ReassignmentRequiresConfirmationAndCancelPreservesTheSavedTeamAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, token);
        long destinationTeamId;
        await using (var setup = fixture.AppHost.CreateAdminContext())
        {
            var team = await setup.Teams.SingleAsync(row => row.ClubId == seed.ClubId && row.Name == seed.IneligibleTeamName, token);
            team.GraduationYear = 2028;
            destinationTeamId = team.TeamId;
            await setup.SaveChangesAsync(token);
        }
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await OpenFirstPlacementAsync(page, seed.CampaignId);
        var assignmentId = SelectedAssignmentId(page);
        await page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.Assigned));
        await page.Locator("#place-team").SelectOptionAsync(seed.EligibleTeamId.ToString(CultureInfo.InvariantCulture));
        await SaveButton(page).ClickAsync();
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Placement saved.");
        var reassign = page.GetByRole(AriaRole.Button, new() { Name = "Reassign player", Exact = true });
        await Expect(reassign).ToBeVisibleAsync();
        await Expect(page.Locator("#place-outcome")).ToHaveCountAsync(0);
        await reassign.ClickAsync();
        await page.Locator("#place-team").SelectOptionAsync(destinationTeamId.ToString(CultureInfo.InvariantCulture));
        await SaveButton(page).ClickAsync();
        var confirm = page.GetByRole(AriaRole.Button, new() { Name = "Confirm change", Exact = true });
        await Expect(page.Locator(".place-confirmation > p")).ToBeFocusedAsync();
        await Expect(page.Locator(".place-confirmation")).ToContainTextAsync(seed.EligibleTeamName);
        await Expect(page.Locator(".place-confirmation")).ToContainTextAsync(seed.IneligibleTeamName);

        await page.GetByRole(AriaRole.Button, new() { Name = "Keep current placement", Exact = true }).ClickAsync();

        await Expect(confirm).ToHaveCountAsync(0);
        await AssertPlacementTeamAndEventCountAsync(seed.ClubId, assignmentId, seed.EligibleTeamId, 1, token);
        if (await reassign.IsVisibleAsync()) { await reassign.ClickAsync(); }
        await page.Locator("#place-team").SelectOptionAsync(destinationTeamId.ToString(CultureInfo.InvariantCulture));
        await SaveButton(page).ClickAsync();
        await Expect(page.Locator(".place-confirmation > p")).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Tab");
        await Expect(confirm).ToBeFocusedAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator(".place-evidence")).ToContainTextAsync(seed.IneligibleTeamName);
        await AssertPlacementTeamAndEventCountAsync(seed.ClubId, assignmentId, destinationTeamId, 2, token);
    }

    [Fact]
    public async Task PlayerAndTeamsCorrectionReturnsPreserveSelectedPlaceAndDiscoveryAsync()
    {
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await OpenFirstPlacementAsync(page, seed.CampaignId);
        await page.Locator("#roster-search").FillAsync("Player 01");
        await page.WaitForURLAsync(url => url.Contains("placementSearch=Player%2001", StringComparison.Ordinal), new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("a.place-row")).ToHaveCountAsync(1);
        var placeUrl = page.Url;
        var playerName = await page.Locator(".place-name").InnerTextAsync();

        await page.GetByRole(AriaRole.Link, new() { Name = "Open player record", Exact = true }).ClickAsync();
        var backToPlace = page.GetByRole(AriaRole.Link, new() { Name = "← Back to roster", Exact = true });
        await Expect(backToPlace).ToBeVisibleAsync();
        await page.ReloadAsync();
        await backToPlace.ClickAsync();
        await Expect(page.Locator(".place-name")).ToHaveTextAsync(playerName);
        page.Url.ShouldBe(placeUrl);

        await page.GetByRole(AriaRole.Link, new() { Name = "Manage teams", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Teams", Exact = true })).ToBeVisibleAsync();
        var returnLink = page.GetByRole(AriaRole.Link, new() { Name = "Return to placement", Exact = true });
        await Expect(returnLink).ToBeVisibleAsync();
        await page.ReloadAsync();
        await returnLink.ClickAsync();
        await Expect(page.Locator(".place-name")).ToHaveTextAsync(playerName);
        await Expect(page.Locator("#roster-search")).ToHaveValueAsync("Player 01");
        page.Url.ShouldBe(placeUrl);
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(returnLink).ToBeVisibleAsync();
        await page.GoForwardAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator(".place-name")).ToHaveTextAsync(playerName);
        page.Url.ShouldBe(placeUrl);
    }

    [Fact]
    public async Task LostPlacementAcknowledgementReloadRecoversExactCommandWithoutDuplicateActivityAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, token);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await OpenFirstPlacementAsync(page, seed.CampaignId);
        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, () => InteractionHelpers.ActUntilAsync(page,
            () => page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.NotSelected)),
            () => IsEnabledAsync(SaveButton(page))));
        var assignmentId = SelectedAssignmentId(page);
        var mutationUrl = new Uri(fixture.BaseUri, CampaignEndpoints.UpdateCampaignPlacementUrl(assignmentId)).ToString();
        var intercepted = await InterceptCommittedPlacementAsync(page, mutationUrl);
        string original;
        try
        {
            await SaveButton(page).ClickAsync();
            var committed = await intercepted.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            committed.Status.ShouldBe(200);
            original = committed.Payload;
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Recover save", Exact = true })).ToBeEnabledAsync();
            await Expect(page.Locator("#place-outcome")).ToHaveCountAsync(0);
        }
        finally { await page.UnrouteAsync(mutationUrl); }
        await page.ReloadAsync();
        var recover = page.GetByRole(AriaRole.Button, new() { Name = "Recover save", Exact = true });
        await Expect(recover).ToBeEnabledAsync();
        var replayed = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(mutationUrl, async route => { replayed.TrySetResult(route.Request.PostData!); await route.ContinueAsync(); });
        try
        {
            await recover.ClickAsync();
            var replay = await replayed.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            original.ShouldBe(replay);
            await Expect(page.Locator(".alert-success")).ToContainTextAsync("Placement saved.");
            await Expect(recover).ToHaveCountAsync(0);
        }
        finally { await page.UnrouteAsync(mutationUrl); }
        var input = JsonSerializer.Deserialize<UpdateCampaignPlacementInput>(original, JsonSerializerOptions.Web)!;
        input.OperationId.Version.ShouldBe(7);
        await using var db = fixture.AppHost.CreateAdminContext();
        (await db.PlacementMutationReceipts.CountAsync(row => row.ClubId == seed.ClubId && row.OperationId == input.OperationId, token)).ShouldBe(1);
        var assignment = await db.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, token);
        assignment.PlacementOutcome.ShouldBe(PlacementOutcome.NotSelected);
        (await db.ActivityEvents.CountAsync(row => row.ClubId == seed.ClubId && row.PlayerId == assignment.PlayerId, token)).ShouldBe(1);
    }

    private async Task CapturePlacementConfirmationAndRecoveryAsync(IPage page, string directory, long campaignId)
    {
        await InteractionHelpers.ClickUntilAsync(page, SaveButton(page),
            () => HasTextAsync(page.Locator(".alert-success"), "Placement saved."));
        await page.GetByRole(AriaRole.Button, new() { Name = "Reassign player", Exact = true }).ClickAsync();
        await page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.Withdrawn));
        await SaveButton(page).ClickAsync();
        await Expect(page.Locator(".place-confirmation")).ToContainTextAsync("This decision is final in this campaign.");
        await CapturePlacementStateViewportsAsync(page, "confirmation", directory, campaignId);
        await page.GetByRole(AriaRole.Button, new() { Name = "Keep current placement", Exact = true }).ClickAsync();
        await Expect(page.Locator(".place-confirmation")).ToHaveCountAsync(0);

        await WasmWarmupHelper.ReloadAsWebAssemblyAsync(page, () => InteractionHelpers.ClickUntilAsync(page,
            page.GetByRole(AriaRole.Button, new() { Name = "Reassign player", Exact = true }),
            () => IsEnabledAsync(page.Locator("#place-outcome"))));
        await page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.NotSelected));
        await SaveButton(page).ClickAsync();
        await Expect(page.Locator(".place-confirmation")).ToBeVisibleAsync();
        var mutationUrl = new Uri(fixture.BaseUri, CampaignEndpoints.UpdateCampaignPlacementUrl(SelectedAssignmentId(page))).ToString();
        var intercepted = await InterceptCommittedPlacementAsync(page, mutationUrl);
        try
        {
            await page.GetByRole(AriaRole.Button, new() { Name = "Confirm change", Exact = true }).ClickAsync();
            var committed = await intercepted.Task.WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
            committed.Status.ShouldBe(200);
            await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Recover save", Exact = true })).ToBeEnabledAsync();
            await CapturePlacementStateViewportsAsync(page, "outcome-unknown", directory, campaignId);
        }
        finally { await page.UnrouteAsync(mutationUrl); }
        await page.GetByRole(AriaRole.Button, new() { Name = "Recover save", Exact = true }).ClickAsync();
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Placement saved.");
    }

    private static async Task CapturePlacementStateViewportsAsync(IPage page, string state, string directory, long campaignId)
    {
        await page.SetViewportSizeAsync(1440, 900);
        await Expect(page.Locator(".place-name")).ToBeVisibleAsync();
        await CaptureAsync(page, state + "-desktop", directory, fullPage: true, campaignId);
        await page.SetViewportSizeAsync(390, 844);
        await Expect(page.Locator(".place-name")).ToBeVisibleAsync();
        await CaptureAsync(page, state + "-mobile", directory, fullPage: true, campaignId);
    }

    private static async Task<TaskCompletionSource<(string Payload, int Status)>> InterceptCommittedPlacementAsync(IPage page, string mutationUrl)
    {
        var intercepted = new TaskCompletionSource<(string, int)>(TaskCreationOptions.RunContinuationsAsynchronously);
        await page.RouteAsync(mutationUrl, async route =>
        {
            try
            {
                var committed = await route.FetchAsync();
                await route.AbortAsync("failed");
                intercepted.TrySetResult((route.Request.PostData!, committed.Status));
            }
            catch (PlaywrightException exception) { intercepted.TrySetException(exception); }
        });
        return intercepted;
    }

    private async Task OpenFirstPlacementAsync(IPage page, long campaignId)
    {
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{campaignId}?tab=place").ToString());
        await InteractionHelpers.ClickUntilAsync(page, page.Locator("a.place-row").First,
            () => IsEnabledAsync(page.Locator("#place-outcome")));
    }

    private static long SelectedAssignmentId(IPage page) => long.Parse(
        QueryHelpers.ParseQuery(new Uri(page.Url).Query)["placementParticipant"].ToString(), CultureInfo.InvariantCulture);

    private async Task AssertPlacementTeamAndEventCountAsync(long clubId, long assignmentId, long teamId, int events, CancellationToken token)
    {
        await using var db = fixture.AppHost.CreateAdminContext();
        var assignment = await db.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == assignmentId, token);
        assignment.TeamId.ShouldBe(teamId);
        assignment.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        (await db.ActivityEvents.CountAsync(row => row.ClubId == clubId && row.PlayerId == assignment.PlayerId, token)).ShouldBe(events);
    }
}
