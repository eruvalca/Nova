using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Playwright;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignPlaceBrowserTests
{
    [Fact]
    public async Task PlacementHistorySupportsBoundedEarlierPagesAndKeyboardReturnToLatestAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, token);
        await using var db = fixture.AppHost.CreateAdminContext();
        var participant = await db.PlayerCampaignAssignments.Where(row => row.CampaignId == seed.CampaignId)
            .OrderBy(row => row.PlayerCampaignAssignmentId).FirstAsync(token);
        await SeedPlacementHistoryAsync(seed, participant.PlayerCampaignAssignmentId, participant.ConcurrencyToken, token);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password,
            new ViewportSize { Width = 1440, Height = 900 });
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place&placementParticipant={participant.PlayerCampaignAssignmentId}").ToString());
        await Expect(page.Locator(".place-history-list li")).ToHaveCountAsync(20);
        var earlier = page.GetByRole(AriaRole.Button, new() { Name = "Earlier changes", Exact = true });
        var latest = page.GetByRole(AriaRole.Button, new() { Name = "Latest changes", Exact = true });
        await InteractionHelpers.ClickUntilAsync(page, earlier, () => latest.IsVisibleAsync());
        await Expect(page.Locator(".place-history-list li")).ToHaveCountAsync(20);
        await CaptureHistoryPagingAsync(page, latest);
        await earlier.ClickAsync();
        await Expect(page.Locator(".place-history-list li")).ToHaveCountAsync(1);
        await Expect(earlier).ToHaveCountAsync(0);
        await latest.FocusAsync();
        await page.Keyboard.PressAsync("Enter");
        await Expect(page.Locator(".place-history-list li")).ToHaveCountAsync(20);
        await Expect(latest).ToHaveCountAsync(0);
        await Expect(earlier).ToBeVisibleAsync();
    }

    private async Task SeedPlacementHistoryAsync(SeededPlacementWorkspace seed, long participantId, Guid concurrencyToken, CancellationToken token)
    {
        var user = fixture.AppHost.CurrentUser;
        var previous = (user.UserId, user.ClubId);
        try
        {
            user.UserId = seed.AdminUserId;
            user.ClubId = seed.ClubId;
            ICampaignPlacementService service = new CampaignPlacementService(fixture.AppHost.CreateTenantContextFactory(),
                user, NullLogger<CampaignPlacementService>.Instance);
            for (var index = 0; index < 41; index++)
            {
                var assigned = index % 2 == 0;
                var result = await service.UpdatePlacementAsync(new(participantId,
                    assigned ? PlacementOutcome.Assigned : PlacementOutcome.NotSelected,
                    assigned ? seed.EligibleTeamId : null, concurrencyToken, Guid.CreateVersion7()), token);
                result.IsSuccess.ShouldBeTrue();
                concurrencyToken = result.Value.ConcurrencyToken;
            }
        }
        finally
        {
            (user.UserId, user.ClubId) = previous;
        }
    }

    private static async Task CaptureHistoryPagingAsync(IPage page, ILocator latest)
    {
        var directory = Environment.GetEnvironmentVariable("NOVA_PLACE_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) { return; }
        Directory.CreateDirectory(directory);
        foreach (var (name, width, height) in new[] { ("desktop", 1440, 900), ("mobile", 390, 844) })
        {
            await page.SetViewportSizeAsync(width, height);
            await latest.ScrollIntoViewIfNeededAsync();
            await Expect(latest).ToBeInViewportAsync();
            await page.Mouse.MoveAsync(0, 0);
            await page.ScreenshotAsync(new() { Path = Path.Combine(directory, $"history-paging-{name}.png") });
        }
    }
}
