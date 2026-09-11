using Microsoft.EntityFrameworkCore;
using Nova.Integration.Tests.Http;
using Nova.SharedKernel.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>Responsive, keyboard and scale evidence for the approved evaluation notebook.</summary>
/// <param name="fixture">The serialized Aspire browser fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class CampaignEvaluationResponsiveBrowserTests(BrowserSuiteFixture fixture)
{
    [Fact]
    public async Task ApprovedNotebookCapturesAuthenticSixtyParticipantCampaignAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        var participantId = await CampaignEvaluationNotebookSeed.PrepareAsync(fixture.AppHost, seed, TestContext.Current.CancellationToken);
        // The approved raster is 897×1752; a 1.5 DPR portrait keeps system text at CSS scale.
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password,
            new() { Width = 598, Height = 1168 }, deviceScaleFactor: 1.5f);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=evaluate&evaluation=true&evalSearch=42&evalParticipant={participantId}").ToString());
        await AssertNotebookAttachedAsync(page);
        await Expect(page.Locator("#evaluation-player-heading")).ToHaveTextAsync("#42 Jordan Lee");
        await Expect(page.Locator(".campaign-facts")).ToContainTextAsync("60 participants");
        await Expect(page.Locator(".evaluation-note-item")).ToHaveCountAsync(2);
        await Expect(page.Locator(".evaluation-note-item").First).ToContainTextAsync("Alex Morgan");
        await Expect(page.Locator(".evaluation-note-item").First).ToContainTextAsync("Keeps looking for passing options.");
        await Expect(page.Locator(".evaluation-note-item").First.Locator("time")).ToContainTextAsync("10:42 AM");
        await Expect(page.Locator(".evaluation-note-item").Nth(1)).ToContainTextAsync("Sam Chen");
        await Expect(page.Locator(".evaluation-note-item").Nth(1)).ToContainTextAsync("Confident receiving under pressure.");
        await Expect(page.Locator(".evaluation-note-item").Nth(1).Locator("time")).ToContainTextAsync("10:37 AM");
        await Expect(page.Locator(".evaluation-note-item button")).ToHaveCountAsync(0);
        await Expect(page.Locator(".evaluation-trait")).ToHaveCountAsync(2);
        await Expect(page.Locator(".evaluation-traits")).ToContainTextAsync("Strong");
        await Expect(page.Locator(".evaluation-traits")).ToContainTextAsync("Good awareness");
        await Expect(page.Locator(".participant-placement-context")).ToContainTextAsync("No decision");
        await Expect(page.Locator(".participant-placement-context")).ToContainTextAsync("North U16");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Show older notes", Exact = true })).ToBeVisibleAsync();
        await page.Locator("#evaluation-note").FillAsync("Quick to recover after losing possession.");
        await Expect(page.Locator("#evaluation-note")).ToHaveValueAsync("Quick to recover after losing possession.");
        await CaptureAsync(page, "hero", fullPage: false, seed.CampaignId, participants: 60);
        foreach (var viewport in new[] { (1440, 1000, "desktop"), (390, 844, "mobile"), (844, 390, "landscape") })
        {
            await page.SetViewportSizeAsync(viewport.Item1, viewport.Item2);
            (await page.EvaluateAsync<bool>("document.documentElement.scrollWidth <= innerWidth")).ShouldBeTrue();
            await CaptureAsync(page, viewport.Item3, fullPage: true, seed.CampaignId, participants: 60);
        }
        await page.Locator(".evaluation-save").ClickAsync();
        await Expect(page.Locator(".evaluation-status")).ToHaveTextAsync("Note saved.");
    }

    [Fact]
    public async Task ThousandParticipantLookupRemainsBoundedAndRecordsSeparateBenchmarkAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, token);
        var large = await SeedingHelpers.SeedCampaignWithParticipantsAsync(fixture.AppHost, seed.ClubId, seed.AdminEmail,
            "Scale", 1000, PlacementOutcome.NotSelected, token);
        await using var db = fixture.AppHost.CreateAdminContext();
        var selected = await db.PlayerCampaignAssignments.Include(item => item.Player)
            .SingleAsync(item => item.PlayerCampaignAssignmentId == large.AssignmentIds[41], token);
        selected.Player.FirstName = "Jordan";
        selected.Player.LastName = "Lee";
        await db.SaveChangesAsync(token);
        (await db.PlayerCampaignAssignments.CountAsync(item => item.CampaignId == large.CampaignId, token)).ShouldBe(1000);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password, new() { Width = 844, Height = 390 });
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{large.CampaignId}?tab=evaluate&evaluation=true&evalSearch=42&evalParticipant={selected.PlayerCampaignAssignmentId}").ToString());
        await AssertNotebookAttachedAsync(page);
        await page.Locator(".evaluation-back").ClickAsync();
        await Expect(page.Locator("#evaluation-player-heading")).ToHaveCountAsync(0);
        await Expect(page.Locator("a[data-eval-result]").First).ToContainTextAsync("#42");
        var lookupStarted = System.Diagnostics.Stopwatch.StartNew();
        await page.Locator("#evaluation-search").FillAsync("Player");
        await page.Locator("#evaluation-search").PressAsync("Enter");
        // This is a functional scale check with recorded timing, not a five-second SLO.
        // One bounded read may settle under concurrent provider load.
        // Retrieval errors end the wait and fail the result assertions.
        const int ReadSettlementMilliseconds = 15_000;
        await Expect(page.Locator(".evaluation-result-count").Filter(new() { HasText = "999 players match" })
            .Or(page.Locator(".evaluation-finder [role='alert']")))
            .ToBeVisibleAsync(new() { Timeout = ReadSettlementMilliseconds });
        await Expect(page.Locator(".evaluation-finder [role='alert']")).ToHaveCountAsync(0);
        page.Url.ShouldContain("evalSearch=Player");
        await Expect(page.Locator(".evaluation-result-count")).ToHaveTextAsync("999 players match “Player”");
        lookupStarted.Stop();
        var evidenceDirectory = Environment.GetEnvironmentVariable("NOVA_EVALUATION_EVIDENCE");
        if (!string.IsNullOrWhiteSpace(evidenceDirectory))
        {
            Directory.CreateDirectory(evidenceDirectory);
            await File.WriteAllTextAsync(Path.Combine(evidenceDirectory, "lookup-timing.json"),
                System.Text.Json.JsonSerializer.Serialize(new { large.CampaignId, Participants = 1000, Matches = 999, PageSize = 20, ReadSettlementMilliseconds, Milliseconds = lookupStarted.Elapsed.TotalMilliseconds, LatencyTargetSpecified = false }),
                TestContext.Current.CancellationToken);
        }
        await Expect(page.Locator("a[data-eval-result]")).ToHaveCountAsync(20);
        await page.GetByRole(AriaRole.Link, new() { Name = "Next page", Exact = true }).PressAsync("Enter");
        await Expect(page.Locator(".evaluation-paging")).ToContainTextAsync("Page 2 of 50");
        await CaptureAsync(page, "finder-landscape", fullPage: true, large.CampaignId, participants: 1000);
    }

    private static async Task AssertNotebookAttachedAsync(IPage page)
    {
        var note = page.Locator("#evaluation-note");
        var count = page.Locator("#evaluation-note-help");
        // Save is deliberately disabled during attachment and recovery. Prove a handled input
        // through the shared hydration policy before checking readiness or taking captures.
        await InteractionHelpers.ActUntilAsync(page, async () =>
        {
            if (await note.CountAsync() > 0 && await note.IsEditableAsync())
            {
                await note.FillAsync("Attach probe", new() { Timeout = 3000 });
            }
        }, async () =>
        {
            var alerts = await page.Locator(".evaluation-workspace [role='alert']").AllTextContentsAsync();
            if (alerts.Count > 0)
            {
                var message = string.Join(" | ", alerts);
                throw new InvalidOperationException($"Evaluation startup failed: {message[..Math.Min(message.Length, 4000)]}");
            }
            return await count.CountAsync() > 0 && (await count.InnerTextAsync()).Contains("12 / 4000", StringComparison.Ordinal);
        });
        await note.FillAsync(string.Empty);
        await Expect(count).ToContainTextAsync("0 / 4000");
        await Expect(page.Locator(".evaluation-save")).ToBeEnabledAsync();
    }

    private static async Task CaptureAsync(IPage page, string name, bool fullPage, long campaignId, int participants)
    {
        var directory = Environment.GetEnvironmentVariable("NOVA_EVALUATION_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name + ".png"), FullPage = fullPage });
        var geometry = await page.EvaluateAsync<string>("args => JSON.stringify({cssWidth:innerWidth,cssHeight:innerHeight,devicePixelRatio,documentHeight:document.documentElement.scrollHeight,campaignId:args.campaignId,participants:args.participants})", new { campaignId, participants });
        await File.WriteAllTextAsync(Path.Combine(directory, name + ".json"), geometry, TestContext.Current.CancellationToken);
    }
}
