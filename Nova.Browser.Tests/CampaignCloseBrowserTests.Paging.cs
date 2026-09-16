using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignCloseBrowserTests
{
    [Fact]
    public async Task CorrectionThatRemovesLastBlockerPageOffersKeyboardRecoveryWithContextAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, token);
        await using (var db = fixture.AppHost.CreateAdminContext())
        {
            var rows = await db.PlayerCampaignAssignments.Where(row => row.CampaignId == seed.CampaignId)
                .OrderBy(row => row.PlayerCampaignAssignmentId).Take(51).ToListAsync(token);
            foreach (var row in rows)
            {
                row.PlacementOutcome = PlacementOutcome.Undecided;
                row.TeamId = null;
                row.DecisionRecordedAt = null;
                row.DecisionRecordedById = null;
                row.DecisionActorDisplayName = null;
            }
            await db.SaveChangesAsync(token);
        }
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=close&closeBlocker=outcomes&closePage=2&search=retained").ToString());
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(1);
        await page.Locator(".close-roster tbody a").ClickAsync();
        await Expect(page.Locator("#place-outcome")).ToBeEnabledAsync();
        var save = page.GetByRole(AriaRole.Button, new() { Name = "Save placement", Exact = true });
        await InteractionHelpers.ActUntilAsync(page, () => page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.NotSelected)),
            async () => await save.CountAsync() > 0 && await save.IsEnabledAsync());
        await save.ClickAsync();
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Placement saved");
        await page.GetByRole(AriaRole.Link, new() { Name = "Return to Close", Exact = true }).ClickAsync();
        await Expect(page.Locator(".close-roster")).ToContainTextAsync("0 of 50 matching participants");
        await Expect(page.Locator(".close-roster")).ToContainTextAsync("This page is beyond the current results.");
        await Expect(page.Locator(".close-roster")).Not.ToContainTextAsync("No participants match this review.");
        var recovery = page.GetByRole(AriaRole.Link, new() { Name = "Go to last available page", Exact = true });
        await page.SetViewportSizeAsync(390, 844);
        await AssertPhoneTargetAsync(recovery);
        await recovery.FocusAsync();
        await Expect(recovery).ToBeFocusedAsync();
        await CaptureAsync(page, "phone-stale-page");
        await recovery.PressAsync("Enter");
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(50);
        page.Url.ShouldContain("closeBlocker=outcomes");
        page.Url.ShouldContain("search=retained");
        page.Url.ShouldNotContain("closePage=");
    }
}
