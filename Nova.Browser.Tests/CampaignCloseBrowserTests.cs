using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>Real Close review, correction, deliberate lifecycle confirmation and keyboard focus.</summary>
[Collection(BrowserSuiteCollection.Name)]
public sealed partial class CampaignCloseBrowserTests(BrowserSuiteFixture fixture)
{
    [Fact]
    public async Task MemberReviewsGroupedLocalOutcomesAndReturnsFromInheritedCorrectionAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await PrepareReviewAsync(seed);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password, new() { Width = 1536, Height = 1024 });
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=close&search=unrelated").ToString());
        await Expect(page.Locator(".close-review")).ToContainTextAsync("No campaign decision 4");
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(50);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true })).ToHaveCountAsync(0);
        await AssertSearchAndHistoryAsync(page);
        await InteractionHelpers.ActUntilAsync(page, () => page.Locator("#close-blocker").SelectOptionAsync("outcomes"),
            () => Task.FromResult(page.Url.Contains("closeBlocker=outcomes", StringComparison.Ordinal)));
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(4);
        await page.Locator("#close-blocker").SelectOptionAsync("");
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(50);
        await CaptureAsync(page, "hero-repro", fullPage: false);
        await page.SetViewportSizeAsync(1440, 960);
        await CaptureAsync(page, "desktop");
        await page.SetViewportSizeAsync(390, 844);
        await CaptureAsync(page, "mobile");
        await page.SetViewportSizeAsync(1536, 1024);
        await page.Locator(".close-blocker a").First.ClickAsync();
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(4);
        await Expect(page.Locator(".close-roster")).ToContainTextAsync("Inherited assigned");
        await page.Locator(".close-roster tbody a").First.ClickAsync();
        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Reassign player", Exact = true }),
            () => page.Locator("#place-outcome").IsVisibleAsync());
        await Expect(page.Locator("#place-outcome")).ToBeEnabledAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Return to Close", Exact = true })).ToBeVisibleAsync();
        await page.SetViewportSizeAsync(390, 844);
        await AssertPhoneTargetAsync(page.GetByRole(AriaRole.Link, new() { Name = "Return to Close", Exact = true }));
        await CaptureAsync(page, "phone-return");
        await InteractionHelpers.ActUntilAsync(page, () => page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.NotSelected)),
            async () => await page.Locator("#place-team").CountAsync() == 0);
        await page.GetByRole(AriaRole.Button, new() { Name = "Save placement", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirm change", Exact = true }).ClickAsync();
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Placement saved");
        await page.GetByRole(AriaRole.Link, new() { Name = "Return to Close", Exact = true }).ClickAsync();
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(3);
        await Expect(page.Locator(".close-review")).ToContainTextAsync("No campaign decision 3");
        page.Url.ShouldContain("closeBlocker=outcomes");
        page.Url.ShouldContain("search=unrelated");
    }

    [Fact]
    public async Task AdministratorCancelsThenClosesAndReopensWithFocusAndRetainedOutcomesAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await using (var db = fixture.AppHost.CreateAdminContext())
        {
            var campaign = await db.Campaigns.SingleAsync(row => row.CampaignId == seed.CampaignId, TestContext.Current.CancellationToken);
            (await db.Clubs.SingleAsync(row => row.ClubId == seed.ClubId, TestContext.Current.CancellationToken)).CurrentSeasonId = campaign.SeasonId;
            await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        }
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=close").ToString());
        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true }),
            () => page.GetByRole(AriaRole.Group, new() { Name = "Lifecycle confirmation" }).IsVisibleAsync());
        await page.GetByRole(AriaRole.Button, new() { Name = "Cancel", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true })).ToBeVisibleAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Review close", Exact = true }).ClickAsync();
        await Expect(page.Locator(".confirmation")).ToBeVisibleAsync();
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(50);
        await CaptureAsync(page, "admin-confirmation");
        await page.GetByRole(AriaRole.Button, new() { Name = "Close campaign", Exact = true }).PressAsync("Enter");
        await Expect(page.Locator("#lifecycle-heading")).ToHaveTextAsync("Closed campaign");
        await Expect(page.Locator("#lifecycle-heading")).ToBeFocusedAsync();
        await Expect(page.Locator(".lifecycle-checkpoint")).ToContainTextAsync("Campaign closed.");
        await Expect(page.Locator(".close-roster")).ToHaveCountAsync(0);
        await CaptureAsync(page, "closed");
        await page.GetByRole(AriaRole.Button, new() { Name = "Review reopen", Exact = true }).ClickAsync();
        await page.GetByRole(AriaRole.Button, new() { Name = "Reopen campaign", Exact = true }).ClickAsync();
        await Expect(page.Locator(".lifecycle-checkpoint")).ToContainTextAsync("Campaign reopened.");
        await Expect(page.Locator(".close-review")).ToContainTextAsync("Not selected 60");
        await Expect(page.Locator("#lifecycle-heading")).ToBeFocusedAsync();
    }

    private async Task PrepareReviewAsync(SeededEvaluationWorkspace seed)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = fixture.AppHost.CreateAdminContext();
        var campaign = await db.Campaigns.Include(row => row.Season).SingleAsync(row => row.CampaignId == seed.CampaignId, token);
        campaign.Name = "Fall tryouts";
        campaign.Season.Name = "2026 season";
        campaign.SeasonOpeningSequence = 2;
        var club = await db.Clubs.SingleAsync(row => row.ClubId == seed.ClubId, token);
        club.CurrentSeasonId = campaign.SeasonId;
        var team = new TeamEntity { CreationOperationId = Guid.NewGuid(), ClubId = seed.ClubId, CreatedById = seed.AdminUserId, Name = "U16 North", GraduationYear = 2028 };
        var prior = new CampaignEntity { CreationOperationId = Guid.NewGuid(), ClubId = seed.ClubId, CreatedById = seed.AdminUserId, SeasonId = campaign.SeasonId, Name = "Spring placement", StartDate = new(2026, 3, 1), Status = CampaignStatus.Closed, SeasonOpeningSequence = 1, ClosedAt = DateTimeOffset.UtcNow, ClosedById = seed.AdminUserId };
        db.Teams.Add(team);
        db.Campaigns.Add(prior);
        await db.SaveChangesAsync(token);
        var rows = await db.PlayerCampaignAssignments.Include(row => row.Player).Where(row => row.CampaignId == campaign.CampaignId).OrderBy(row => row.PlayerCampaignAssignmentId).ToListAsync(token);
        for (var index = 0; index < rows.Count; index++)
        {
            var row = rows[index];
            row.Player.GraduationYear = 2028;
            row.PlacementOutcome = index switch { < 48 => PlacementOutcome.Assigned, < 54 => PlacementOutcome.NotSelected, < 56 => PlacementOutcome.Withdrawn, _ => PlacementOutcome.Undecided };
            row.TeamId = index < 48 ? team.TeamId : null;
            row.DecisionRecordedAt = index < 56 ? DateTimeOffset.UtcNow : null;
            row.DecisionRecordedById = index < 56 ? seed.AdminUserId : null;
            row.DecisionActorDisplayName = index < 56 ? "Alice Author" : null;
            if (index >= 56)
            {
                db.PlayerCampaignAssignments.Add(new() { ClubId = seed.ClubId, CreatedById = seed.AdminUserId, CampaignId = prior.CampaignId, PlayerId = row.PlayerId, PlacementOutcome = PlacementOutcome.Assigned, TeamId = team.TeamId, ConcurrencyToken = Guid.NewGuid(), DecisionRecordedAt = DateTimeOffset.UtcNow, DecisionRecordedById = seed.AdminUserId, DecisionActorDisplayName = "Alice Author" });
            }
        }
        rows[0].Player.FirstName = "Maya"; rows[0].Player.LastName = "Patel";
        rows[1].Player.FirstName = "Jordan"; rows[1].Player.LastName = "Lee";
        rows[2].Player.FirstName = "Avery"; rows[2].Player.LastName = "Chen";
        await db.SaveChangesAsync(token);
    }

    private static async Task AssertSearchAndHistoryAsync(IPage page)
    {
        await page.GetByRole(AriaRole.Link, new() { Name = "Next page", Exact = true }).ClickAsync();
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(10);
        await page.Locator("#close-search").FillAsync("Nobody matches this search");
        await page.GetByRole(AriaRole.Button, new() { Name = "Search", Exact = true }).ClickAsync();
        await Expect(page.Locator(".close-roster")).ToContainTextAsync("No participants match this review.");
        await page.SetViewportSizeAsync(390, 844);
        await AssertPhoneTargetAsync(page.GetByRole(AriaRole.Link, new() { Name = "Show all campaign participants", Exact = true }));
        await CaptureAsync(page, "phone-empty");
        await page.SetViewportSizeAsync(1536, 1024);
        page.Url.ShouldNotContain("closePage=");
        await page.GoBackAsync(new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(10);
        page.Url.ShouldContain("closePage=2");
        await page.GetByRole(AriaRole.Link, new() { Name = "Previous page", Exact = true }).ClickAsync();
        await Expect(page.Locator(".close-roster tbody a")).ToHaveCountAsync(50);
    }

    private static async Task CaptureAsync(IPage page, string name, bool fullPage = true)
    {
        var directory = Environment.GetEnvironmentVariable("NOVA_CLOSE_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) { return; }
        Directory.CreateDirectory(directory);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name + ".png"), FullPage = fullPage });
    }

    private static async Task AssertPhoneTargetAsync(ILocator control)
    {
        await Expect(control).ToBeVisibleAsync();
        var box = await control.BoundingBoxAsync();
        box.ShouldNotBeNull();
        box.Height.ShouldBeGreaterThanOrEqualTo(44);
        box.Width.ShouldBeGreaterThanOrEqualTo(44);
    }
}
