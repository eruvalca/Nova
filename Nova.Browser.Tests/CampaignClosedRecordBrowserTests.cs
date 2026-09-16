using System.Text.Json;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Http;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Activity;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>The bounded final record on the real campaign shell, including native navigation.</summary>
[Collection(BrowserSuiteCollection.Name)]
public sealed partial class CampaignClosedRecordBrowserTests(BrowserSuiteFixture fixture)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task EvaluationRoundTripPreservesPlaceContextThroughSearchPagingAndSelectionAsync(bool javaScriptEnabled)
    {
        var seed = await SeedClosedAsync();
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password,
            new() { Width = 1440, Height = 1000 }, javaScriptEnabled: javaScriptEnabled);
        var page = context.Pages[0];
        var carried = "placementSearch=Goalie&placementPage=2&placementParticipant=25&placementYears=2028,2029"
            + "&placementTags=7,9&placementOutcome=assigned&placementTeamId=60&placementSortBy=tryoutNumber"
            + "&placementSortDirection=desc&returnToEvaluation=true&search=preserved&closeOutcome=assigned";
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=close&{carried}&closeParticipant={seed.AssignmentIds[0]}").ToString());
        await Expect(page.Locator(".participant-history li")).ToHaveCountAsync(20);
        await page.GetByRole(AriaRole.Link, new() { Name = "Read evaluation", Exact = true }).ClickAsync();
        await Expect(page.Locator(".evaluation-sheet")).ToBeVisibleAsync();
        AssertCarriedContext(page, carried, seed.AssignmentIds[0]);
        await page.Locator("#evaluation-search").FillAsync("Player");
        await page.GetByRole(AriaRole.Button, new() { Name = "Find", Exact = true }).ClickAsync();
        await Expect(page.Locator("[data-eval-result]")).ToHaveCountAsync(20);
        AssertCarriedContext(page, carried, seed.AssignmentIds[0]);
        await page.GetByRole(AriaRole.Link, new() { Name = "Next page", Exact = true }).ClickAsync();
        await Expect(page.Locator(".evaluation-paging")).ToContainTextAsync("Page 2 of 3");
        AssertCarriedContext(page, carried, seed.AssignmentIds[0]);
        await page.Locator("[data-eval-result]").First.ClickAsync();
        await Expect(page.Locator(".evaluation-sheet")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save note", Exact = true })).ToHaveCountAsync(0);
        AssertCarriedContext(page, carried, seed.AssignmentIds[0]);
        await page.GetByRole(AriaRole.Link, new() { Name = "Return to Close", Exact = true }).ClickAsync();
        await Expect(page.Locator(".participant-history li")).ToHaveCountAsync(20);
        AssertCarriedContext(page, carried, seed.AssignmentIds[0]);
        var finalQuery = QueryHelpers.ParseQuery(new Uri(page.Url).Query);
        finalQuery["tab"].ToString().ShouldBe("close");
        finalQuery["evalSearch"].ToString().ShouldBe("Player");
        finalQuery["evalPage"].ToString().ShouldBe("2");
    }

    private static void AssertCarriedContext(IPage page, string carried, long closeParticipant)
    {
        var actual = QueryHelpers.ParseQuery(new Uri(page.Url).Query);
        foreach (var pair in QueryHelpers.ParseQuery(carried))
        {
            actual[pair.Key].Count.ShouldBe(1, $"{pair.Key} must be present exactly once");
            actual[pair.Key].ToString().ShouldBe(pair.Value.ToString(), pair.Key);
        }
        actual["closeParticipant"].ToString().ShouldBe(closeParticipant.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    [Fact]
    public async Task MemberReadsPagedFinalRecordAndInlineHistoryWithReadOnlyEvaluationReturnAsync()
    {
        var seed = await SeedClosedAsync();
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password, new() { Width = 1440, Height = 1000 });
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=close&search=preserved").ToString());
        await Expect(page.Locator(".closed-record tbody a")).ToHaveCountAsync(50);
        await Expect(page.Locator(".record-summary")).ToContainTextAsync("Assigned 1 · Not selected 58 · Withdrawn 1");
        await Expect(page.Locator(".closed-record")).ToContainTextAsync("Archived team");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Review reopen", Exact = true })).ToHaveCountAsync(0);
        await CaptureAsync(page, "desktop");
        await page.GetByRole(AriaRole.Link, new() { Name = "Next page", Exact = true }).ClickAsync();
        await Expect(page.Locator(".closed-record tbody a")).ToHaveCountAsync(10);
        await page.GetByRole(AriaRole.Link, new() { Name = "Previous page", Exact = true }).ClickAsync();
        await page.Locator(".closed-record tbody a").First.ClickAsync();
        await Expect(page.Locator(".participant-history li")).ToHaveCountAsync(20);
        await Expect(page.Locator(".participant-history")).ToContainTextAsync("Original decision maker");
        await page.GetByRole(AriaRole.Link, new() { Name = "Earlier changes", Exact = true }).ClickAsync();
        await Expect(page.Locator(".participant-history li")).ToHaveCountAsync(3);
        await page.GetByRole(AriaRole.Link, new() { Name = "Latest changes", Exact = true }).ClickAsync();
        await Expect(page.Locator(".participant-history li")).ToHaveCountAsync(20);
        await page.GetByRole(AriaRole.Link, new() { Name = "Read evaluation", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Return to Close", Exact = true })).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Save note", Exact = true })).ToHaveCountAsync(0);
        await page.GetByRole(AriaRole.Link, new() { Name = "Return to Close", Exact = true }).ClickAsync();
        await Expect(page.Locator(".participant-history li")).ToHaveCountAsync(20);
        page.Url.ShouldContain("search=preserved");
        await page.SetViewportSizeAsync(390, 844);
        await CaptureAsync(page, "phone-history");
        await page.Locator("#closed-outcome").ScrollIntoViewIfNeededAsync();
        await page.Locator("#closed-outcome").SelectOptionAsync("withdrawn");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply filters", Exact = true }).ClickAsync();
        await Expect(page.Locator(".closed-record tbody a")).ToHaveCountAsync(1);
        await Expect(page.Locator(".participant-history")).ToHaveCountAsync(0);
        await Expect(page.Locator(".record-summary")).ToContainTextAsync("60 participants");
        await CaptureAsync(page, "phone-filtered");
    }

    [Fact]
    public async Task ScriptDisabledFinalFiltersAndHistoryKeepAppliedStateOnBackAsync()
    {
        var seed = await SeedClosedAsync();
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, EvaluationSeed.Password,
            new() { Width = 390, Height = 844 }, javaScriptEnabled: false);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=close&closePage=2&search=preserved").ToString());
        await Expect(page.Locator(".closed-record tbody a")).ToHaveCountAsync(10);
        await page.Locator("#closed-outcome").ScrollIntoViewIfNeededAsync();
        await page.Locator("#closed-outcome").SelectOptionAsync("assigned");
        var apply = page.GetByRole(AriaRole.Button, new() { Name = "Apply filters", Exact = true });
        await apply.FocusAsync();
        await Expect(apply).ToBeFocusedAsync();
        (await apply.BoundingBoxAsync()).ShouldNotBeNull().Height.ShouldBeGreaterThanOrEqualTo(44);
        await apply.PressAsync("Enter");
        await Expect(page.Locator(".closed-record tbody a")).ToHaveCountAsync(1);
        page.Url.ShouldNotContain("closePage=");
        page.Url.ShouldContain("search=preserved");
        await page.Locator(".closed-record tbody a").ClickAsync();
        await Expect(page.Locator(".participant-history li")).ToHaveCountAsync(20);
        await page.GetByRole(AriaRole.Link, new() { Name = "Earlier changes", Exact = true }).ClickAsync();
        await Expect(page.Locator(".participant-history li")).ToHaveCountAsync(3);
        await page.GoBackAsync();
        await Expect(page.Locator(".participant-history li")).ToHaveCountAsync(20);
        await Expect(page.Locator("#closed-outcome")).ToHaveValueAsync("assigned");
        await page.Locator("#closed-search").ScrollIntoViewIfNeededAsync();
        await page.Locator("#closed-search").FillAsync("Nobody matches");
        await page.GetByRole(AriaRole.Button, new() { Name = "Apply filters", Exact = true }).ClickAsync();
        await Expect(page.Locator(".closed-record")).ToContainTextAsync("No participants match these filters");
        await CaptureAsync(page, "phone-native-empty");
        await page.GoBackAsync();
        await Expect(page.Locator("#closed-search")).ToHaveValueAsync("");
        await Expect(page.Locator("#closed-outcome")).ToHaveValueAsync("assigned");
    }

    private async Task<SeededEvaluationWorkspace> SeedClosedAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, token);
        await PrepareOutcomesAsync(seed);
        await SeedingHelpers.CloseCampaignThroughServiceAsync(fixture.AppHost, seed.ClubId, seed.AdminUserId, seed.CampaignId, token);
        await using var db = fixture.AppHost.CreateAdminContext();
        var assigned = await db.PlayerCampaignAssignments.Include(row => row.Player).Include(row => row.Team)
            .SingleAsync(row => row.CampaignId == seed.CampaignId && row.PlacementOutcome == PlacementOutcome.Assigned, token);
        assigned.Player.LifecycleStatus = LifecycleStatus.Archived;
        assigned.Player.ArchivedAt = DateTimeOffset.UtcNow;
        assigned.Player.ArchivedById = seed.AdminUserId;
        assigned.Team!.LifecycleStatus = LifecycleStatus.Archived;
        assigned.Team.ArchivedAt = DateTimeOffset.UtcNow;
        assigned.Team.ArchivedById = seed.AdminUserId;
        await db.SaveChangesAsync(token);
        return seed;
    }

    private async Task PrepareOutcomesAsync(SeededEvaluationWorkspace seed)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = fixture.AppHost.CreateAdminContext();
        var campaign = await db.Campaigns.SingleAsync(row => row.CampaignId == seed.CampaignId, token);
        (await db.Clubs.SingleAsync(row => row.ClubId == seed.ClubId, token)).CurrentSeasonId = campaign.SeasonId;
        campaign.Name = "Autumn campaign";
        var rows = await db.PlayerCampaignAssignments.Include(row => row.Player).Where(row => row.CampaignId == seed.CampaignId)
            .OrderBy(row => row.PlayerCampaignAssignmentId).ToListAsync(token);
        var first = rows[0];
        first.Player.FirstName = "Alexandra";
        first.Player.LastName = "Montgomery-Washington";
        var team = new TeamEntity { ClubId = seed.ClubId, CreatedById = seed.AdminUserId, CreationOperationId = Guid.NewGuid(), Name = "North regional development squad", GraduationYear = first.Player.GraduationYear };
        first.Team = team;
        first.PlacementOutcome = PlacementOutcome.Assigned;
        first.DecisionActorDisplayName = "Original decision maker";
        rows[1].PlacementOutcome = PlacementOutcome.Withdrawn;
        for (var index = 0; index < 23; index++)
        {
            var assigned = index % 2 == 0;
            db.ActivityEvents.Add(new ActivityEventEntity
            {
                ClubId = seed.ClubId,
                CampaignId = seed.CampaignId,
                PlayerId = first.PlayerId,
                ActorUserId = seed.AdminUserId,
                ActorDisplayName = "Original decision maker",
                CreatedById = seed.AdminUserId,
                EventKind = ActivityEventKind.PlacementOutcomeReplaced,
                PayloadJson = JsonSerializer.Serialize<ClubActivityContext>(new PlacementContext
                {
                    CampaignId = seed.CampaignId,
                    CampaignName = campaign.Name,
                    PlayerCampaignAssignmentId = first.PlayerCampaignAssignmentId,
                    PlayerDisplayName = first.Player.FirstName + " " + first.Player.LastName,
                    PlayerId = first.PlayerId,
                    PreviousOutcome = assigned ? PlacementOutcome.NotSelected : PlacementOutcome.Assigned,
                    PreviousTeamName = assigned ? null : team.Name,
                    Outcome = assigned ? PlacementOutcome.Assigned : PlacementOutcome.NotSelected,
                    TeamName = assigned ? team.Name : null,
                }),
            });
        }
        await db.SaveChangesAsync(token);
    }

    private static async Task CaptureAsync(IPage page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("NOVA_CLOSED_RECORD_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory)) { return; }
        Directory.CreateDirectory(directory);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await page.ScreenshotAsync(new() { Path = Path.Combine(directory, name + ".png"), FullPage = true });
    }
}
