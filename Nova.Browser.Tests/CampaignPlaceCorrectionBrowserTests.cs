using System.Globalization;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

public sealed partial class CampaignPlaceBrowserTests
{
    [Fact]
    public async Task InvalidTeamCorrectionReturnsThroughTeamDetailWithCompleteWorkspaceContextAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, token);
        var target = await SeedInvalidPlacementAsync(seed, token);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        var route = $"/campaigns/{seed.CampaignId}?tab=place&placementParticipant={target.AssignmentId}&placementSearch=Player&placementEligibility=NeedsPlacement&placementPage=2&placementSortBy=tryoutNumber&placementSortDirection=asc&search=Roster&graduationYears=2028&sortBy=tryoutNumber&sortDirection=desc&evalSearch=Player&evalPage=2&evalParticipant={target.AssignmentId}&evaluation=true&returnToEvaluation=true";
        await page.GotoAsync(new Uri(fixture.BaseUri, route).ToString());
        await Expect(page.Locator(".place-correction")).ToBeVisibleAsync();
        await Expect(page.Locator(".place-evidence")).ToContainTextAsync(seed.IneligibleTeamName);
        var originalUrl = page.Url;
        await OpenTeamsCorrectionFromReadyPlaceAsync(page, seed.IneligibleTeamName);
        await page.GetByRole(AriaRole.Link, new() { Name = seed.IneligibleTeamName, Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Heading, new() { Name = seed.IneligibleTeamName, Exact = true })).ToBeVisibleAsync();
        await page.ReloadAsync();
        await InteractionHelpers.ClickUntilAsync(page, page.GetByRole(AriaRole.Button, new() { Name = "Edit", Exact = true }),
            () => page.Locator("#team-grad-year").IsVisibleAsync());
        await page.Locator("#team-grad-year").FillAsync("2028");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save changes", Exact = true }).ClickAsync();
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Team updated successfully.");
        await page.GetByRole(AriaRole.Link, new() { Name = "← Back to teams", Exact = true }).ClickAsync();
        await Expect(page.GetByRole(AriaRole.Link, new() { Name = "Return to placement", Exact = true })).ToBeVisibleAsync();
        await page.ReloadAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Return to placement", Exact = true }).ClickAsync();
        await Expect(page.Locator(".place-correction")).ToHaveCountAsync(0);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Reassign player", Exact = true })).ToBeVisibleAsync();
        AssertEquivalentWorkspaceUrl(page.Url, originalUrl);
        await using var db = fixture.AppHost.CreateAdminContext();
        var assignment = await db.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == target.AssignmentId, token);
        assignment.TeamId.ShouldBe(target.TeamId);
        assignment.ConcurrencyToken.ShouldBe(target.Token);
        (await db.Teams.SingleAsync(row => row.TeamId == target.TeamId, token)).GraduationYear.ShouldBe(2028);
    }

    [Fact]
    public async Task KeepPreviousSeasonTeamSavesOnceAndAdvancesWithinFilteredNeedsPlacementAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, token);
        var target = await SeedHistoricalPlacementAsync(seed, previousSeason: true, PlacementOutcome.Assigned, token);
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri,
            $"/campaigns/{seed.CampaignId}?tab=place&placementSearch=Player%200&placementEligibility=NeedsPlacement&placementParticipant={target.AssignmentId}").ToString());
        var keep = page.GetByRole(AriaRole.Button, new() { Name = "Keep on " + seed.EligibleTeamName, Exact = true });
        await Expect(keep).ToBeEnabledAsync();
        await Expect(page.Locator(".place-evidence")).ToContainTextAsync("No effective season placement");
        var nextRow = page.Locator("a.place-row").Nth(1);
        var nextName = (await nextRow.Locator(".place-row-name").InnerTextAsync()).Trim();
        var nextHref = (await nextRow.GetAttributeAsync("href")).ShouldNotBeNull();
        await InteractionHelpers.ClickUntilAsync(page, keep,
            () => Task.FromResult(page.Url.Contains("placementParticipant=", StringComparison.Ordinal) && SelectedAssignmentId(page) != target.AssignmentId));
        await Expect(page.Locator(".place-name")).ToHaveTextAsync(nextName);
        page.Url.ShouldContain("placementSearch=Player%200");
        nextHref.ShouldContain("placementParticipant=" + SelectedAssignmentId(page).ToString(CultureInfo.InvariantCulture));
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Confirm change", Exact = true })).ToHaveCountAsync(0);
        await AssertPlacementTeamAndEventCountAsync(seed.ClubId, target.AssignmentId, seed.EligibleTeamId, 1, token);
        await using var db = fixture.AppHost.CreateAdminContext();
        (await db.PlacementMutationReceipts.CountAsync(row => row.ClubId == seed.ClubId, token)).ShouldBe(1);
        var previous = await db.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == target.PriorAssignmentId, token);
        previous.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        previous.TeamId.ShouldBe(seed.EligibleTeamId);
    }

    [Fact]
    public async Task PriorCampaignWithdrawalRequiresAdministratorSupersessionAndLeavesClosedDecisionImmutableAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await PlacementSeed.SeedAsync(fixture.AppHost, token);
        var target = await SeedHistoricalPlacementAsync(seed, previousSeason: false, PlacementOutcome.Withdrawn, token);
        var route = new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}?tab=place&placementParticipant={target.AssignmentId}").ToString();
        await AssertMemberCannotSupersedeAsync(seed, route);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(route);
        var supersede = page.GetByRole(AriaRole.Button, new() { Name = "Supersede prior withdrawal", Exact = true });
        await Expect(supersede).ToBeVisibleAsync();
        await Expect(page.Locator("#place-outcome")).ToHaveCountAsync(0);
        await Expect(page.Locator("button.place-section.leads")).ToContainTextAsync(TotalText(-1));
        await InteractionHelpers.ClickUntilAsync(page, supersede, () => IsEnabledAsync(page.Locator("#place-outcome")));
        await page.Locator("#place-outcome").SelectOptionAsync(nameof(PlacementOutcome.NotSelected));
        await SaveButton(page).ClickAsync();
        await Expect(page.Locator(".place-confirmation")).ToContainTextAsync("available again");
        await page.GetByRole(AriaRole.Button, new() { Name = "Confirm change", Exact = true }).ClickAsync();
        await Expect(page.Locator(".alert-success")).ToContainTextAsync("Placement saved.");
        await Expect(page.Locator(".place-evidence")).ToContainTextAsync("Not selected");
        await using var db = fixture.AppHost.CreateAdminContext();
        var current = await db.PlayerCampaignAssignments.SingleAsync(row => row.PlayerCampaignAssignmentId == target.AssignmentId, token);
        current.PlacementOutcome.ShouldBe(PlacementOutcome.NotSelected);
        current.DecisionRecordedById.ShouldBe(seed.AdminUserId);
        var previous = await db.PlayerCampaignAssignments.Include(row => row.Campaign)
            .SingleAsync(row => row.PlayerCampaignAssignmentId == target.PriorAssignmentId, token);
        previous.Campaign.Status.ShouldBe(CampaignStatus.Closed);
        previous.PlacementOutcome.ShouldBe(PlacementOutcome.Withdrawn);
        previous.ConcurrencyToken.ShouldBe(target.PriorToken);
        (await db.ActivityEvents.CountAsync(row => row.ClubId == seed.ClubId && row.PlayerId == current.PlayerId, token)).ShouldBe(1);
    }

    private static async Task OpenTeamsCorrectionFromReadyPlaceAsync(IPage page, string teamName)
    {
        // A direct Place URL prerenders its correction evidence before handlers attach. Exercise
        // local UI state first, like the existing queue-search test, so startup reconciliation has
        // attached before the native correction handoff leaves this surface.
        var filters = page.GetByRole(AriaRole.Button, new() { Name = "Filters", Exact = true });
        await InteractionHelpers.ClickUntilAsync(page, filters, () => page.Locator("#roster-filter-shelf").IsVisibleAsync());
        await filters.ClickAsync();
        await Expect(page.Locator("#roster-filter-shelf")).ToBeHiddenAsync();
        await page.GetByRole(AriaRole.Link, new() { Name = "Manage teams", Exact = true }).ClickAsync();
        try
        {
            await page.WaitForURLAsync(url => string.Equals(new Uri(url).AbsolutePath, "/club/teams", StringComparison.Ordinal));
            await Expect(page.GetByRole(AriaRole.Heading, new() { Name = "Teams", Exact = true })).ToBeVisibleAsync();
            await Expect(page.GetByRole(AriaRole.Link, new() { Name = teamName, Exact = true })).ToBeVisibleAsync();
        }
        catch (Exception exception) when (exception is PlaywrightException or TimeoutException)
        {
            var content = await page.Locator("main").InnerTextAsync();
            throw new InvalidOperationException($"The Teams correction handoff did not settle. URL: {page.Url}. Main: {content}", exception);
        }
    }

    private static void AssertEquivalentWorkspaceUrl(string actual, string expected)
    {
        var actualUri = new Uri(actual);
        var expectedUri = new Uri(expected);
        actualUri.GetLeftPart(UriPartial.Path).ShouldBe(expectedUri.GetLeftPart(UriPartial.Path));
        actualUri.Fragment.ShouldBe(expectedUri.Fragment);
        var actualQuery = QueryHelpers.ParseQuery(actualUri.Query);
        var expectedQuery = QueryHelpers.ParseQuery(expectedUri.Query);
        actualQuery.Keys.Order(StringComparer.Ordinal).ShouldBe(expectedQuery.Keys.Order(StringComparer.Ordinal));
        foreach (var (key, values) in expectedQuery)
        {
            actualQuery[key].ToArray().ShouldBe(values.ToArray());
        }
    }

    private async Task AssertMemberCannotSupersedeAsync(SeededPlacementWorkspace seed, string route)
    {
        await using var context = await fixture.NewSignedInContextAsync(seed.EvaluatorEmail, PlacementSeed.Password);
        var page = context.Pages[0];
        await page.GotoAsync(route);
        await Expect(page.Locator(".place-evidence")).ToContainTextAsync("Withdrawn");
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Supersede prior withdrawal", Exact = true })).ToHaveCountAsync(0);
        await Expect(page.Locator("#place-outcome")).ToHaveCountAsync(0);
        await Expect(page.Locator("button.place-section.leads")).ToContainTextAsync(TotalText(-1));
    }

    private async Task<(long AssignmentId, long TeamId, Guid Token)> SeedInvalidPlacementAsync(SeededPlacementWorkspace seed, CancellationToken token)
    {
        await using var db = fixture.AppHost.CreateAdminContext();
        var assignment = await db.PlayerCampaignAssignments.Where(row => row.CampaignId == seed.CampaignId)
            .OrderBy(row => row.TryoutNumber).FirstAsync(token);
        var team = await db.Teams.SingleAsync(row => row.ClubId == seed.ClubId && row.Name == seed.IneligibleTeamName, token);
        assignment.PlacementOutcome = PlacementOutcome.Assigned;
        assignment.TeamId = team.TeamId;
        assignment.DecisionRecordedAt = DateTimeOffset.UtcNow.AddDays(-1);
        assignment.DecisionRecordedById = seed.AdminUserId;
        assignment.DecisionActorDisplayName = "Alice Author";
        await db.SaveChangesAsync(token);
        return (assignment.PlayerCampaignAssignmentId, team.TeamId, assignment.ConcurrencyToken);
    }

    private async Task<(long AssignmentId, long PriorAssignmentId, Guid PriorToken)> SeedHistoricalPlacementAsync(
        SeededPlacementWorkspace seed, bool previousSeason, PlacementOutcome outcome, CancellationToken token)
    {
        await using var db = fixture.AppHost.CreateAdminContext();
        var current = await db.Campaigns.Include(row => row.Season).SingleAsync(row => row.CampaignId == seed.CampaignId, token);
        current.SeasonOpeningSequence = 10;
        var assignment = await db.PlayerCampaignAssignments.Where(row => row.CampaignId == seed.CampaignId)
            .OrderBy(row => row.TryoutNumber).FirstAsync(token);
        var seasonId = current.SeasonId;
        if (previousSeason)
        {
            var priorSeason = new SeasonEntity
            {
                CreationOperationId = Guid.NewGuid(),
                Name = "Previous placement season",
                ClubId = seed.ClubId,
                StartDate = current.Season.StartDate.AddYears(-1),
                EndDate = current.Season.StartDate.AddDays(-1),
                CreatedById = seed.AdminUserId
            };
            db.Seasons.Add(priorSeason);
            await db.SaveChangesAsync(token);
            current.Season.CreationPreviousSeasonId = priorSeason.SeasonId;
            seasonId = priorSeason.SeasonId;
        }
        var prior = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = "Prior saved placement",
            ClubId = seed.ClubId,
            SeasonId = seasonId,
            SeasonOpeningSequence = 9,
            OpeningOperationId = Guid.NewGuid(),
            OpenedAt = DateTimeOffset.UtcNow.AddDays(-2),
            OpenedById = seed.AdminUserId,
            InitialEnrolledPlayerCount = 1,
            InitialActiveTeamCount = 4,
            Status = CampaignStatus.Closed,
            ClosedAt = DateTimeOffset.UtcNow.AddDays(-1),
            ClosedById = seed.AdminUserId,
            CreatedById = seed.AdminUserId
        };
        db.Campaigns.Add(prior);
        await db.SaveChangesAsync(token);
        var saved = new PlayerCampaignAssignmentEntity
        {
            CampaignId = prior.CampaignId,
            PlayerId = assignment.PlayerId,
            ClubId = seed.ClubId,
            PlacementOutcome = outcome,
            TeamId = outcome == PlacementOutcome.Assigned ? seed.EligibleTeamId : null,
            DecisionRecordedAt = DateTimeOffset.UtcNow.AddDays(-1),
            DecisionRecordedById = seed.AdminUserId,
            DecisionActorDisplayName = "Alice Author",
            CreatedById = seed.AdminUserId
        };
        db.PlayerCampaignAssignments.Add(saved);
        await db.SaveChangesAsync(token);
        return (assignment.PlayerCampaignAssignmentId, saved.PlayerCampaignAssignmentId, saved.ConcurrencyToken);
    }
}
