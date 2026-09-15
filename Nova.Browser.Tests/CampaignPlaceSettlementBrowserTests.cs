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
    public async Task SavingTheLastParticipantOnPageTwoAdoptsPageOneBeforeEnablingEditingAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, token);
        await using var db = fixture.AppHost.CreateAdminContext();
        var participants = await db.PlayerCampaignAssignments.Where(row => row.CampaignId == seed.CampaignId)
            .OrderBy(row => row.PlayerCampaignAssignmentId).Take(9).ToListAsync(token);
        var user = fixture.AppHost.CurrentUser;
        var previous = (user.UserId, user.ClubId);
        try
        {
            user.UserId = seed.AdminUserId;
            user.ClubId = seed.ClubId;
            ICampaignPlacementService service = new CampaignPlacementService(fixture.AppHost.CreateTenantContextFactory(),
                user, NullLogger<CampaignPlacementService>.Instance);
            foreach (var participant in participants)
            {
                (await service.UpdatePlacementAsync(new(participant.PlayerCampaignAssignmentId, PlacementOutcome.NotSelected,
                    null, participant.ConcurrencyToken, Guid.CreateVersion7()), token)).IsSuccess.ShouldBeTrue();
            }
        }
        finally { (user.UserId, user.ClubId) = previous; }

        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place&placementPage=2").ToString());
        await Expect(page.Locator("a.place-row")).ToHaveCountAsync(1);
        await InteractionHelpers.ClickUntilAsync(page, page.Locator("a.place-row"), () => Task.FromResult(Selected(page)));
        await Expect(page.Locator("#place-outcome")).ToBeEnabledAsync();
        await page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.NotSelected));
        await InteractionHelpers.ClickUntilAsync(page, SaveButton(page),
            () => HasTextAsync(page.Locator(".alert-success"), "Placement saved."));
        await page.WaitForURLAsync(url => !url.Contains("placementPage=2", StringComparison.Ordinal), new() { WaitUntil = WaitUntilState.Commit });
        await Expect(page.Locator("a.place-row")).ToHaveCountAsync(50);
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("50 need placement.");
        await Expect(page.Locator(".alert-success")).Not.ToContainTextAsync("A later decision");
        await Expect(page.Locator("#place-outcome")).ToBeEnabledAsync();
        await Expect(page.Locator(".place-evidence")).ToContainTextAsync("Not selected");
        await Expect(page.Locator("button.place-section.leads")).ToContainTextAsync("50");
    }
}
