using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Shouldly;

namespace Nova.Browser.Tests;

/// <summary>Acceptance for shared campaign orientation and placement-aware roster context.</summary>
/// <param name="fixture">The serial Aspire browser fixture.</param>
[Collection(BrowserSuiteCollection.Name)]
public sealed class CampaignWorkspaceBrowserTests(BrowserSuiteFixture fixture)
{
    private static readonly (string First, string Last)[] _captureNames =
    [("Avery", "Morgan"), ("Casey", "Nolan"), ("Jordan", "Patel"), ("Riley", "Rivera"), ("Sam", "Stone"), ("Taylor", "Vega")];
    [Fact]
    public async Task InheritedPlacementContextStaysBesideDiscoveryAndBecomesMobileDialogAsync()
    {
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, TestContext.Current.CancellationToken);
        await PrepareRepresentativeWorkspaceAsync(seed);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password,
            new ViewportSize { Width = 1536, Height = 1024 });
        var page = context.Pages[0];
        await page.GotoAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}/roster?participant={seed.AssignmentIds[0]}").ToString());
        await Expect(page.Locator("#participant-drawer-close")).ToBeFocusedAsync();
        await Expect(page.Locator(".readiness-context")).ToContainTextAsync("Not ready to close");
        await Expect(page.Locator(".readiness-context")).ToContainTextAsync("0 need placement");
        await Expect(page.Locator(".readiness-blocker")).ToBeVisibleAsync();
        await Expect(page.Locator(".participant-placement-context")).ToContainTextAsync("No campaign decision");
        await Expect(page.Locator(".participant-placement-context")).ToContainTextAsync("U16 North");
        await Expect(page.Locator(".participant-placement-context")).ToContainTextAsync("Spring placement");
        await Expect(page.Locator(".participant-placement-context")).ToContainTextAsync("Optional reassignment");
        await Expect(page.Locator("aside.participant-drawer")).ToHaveAttributeAsync("role", "region");
        var roster = (await page.Locator(".workspace-board").BoundingBoxAsync())!;
        var participant = (await page.Locator("aside.participant-drawer").BoundingBoxAsync())!;
        participant.X.ShouldBeGreaterThanOrEqualTo(roster.X + roster.Width);
        await page.GetByRole(AriaRole.Button, new() { Name = "Add note", Exact = true }).ClickAsync();
        await page.Locator("#participant-drawer-note-content").FillAsync("Quick acceleration out of turns. Developing consistency in defensive shape.");
        await page.GetByRole(AriaRole.Button, new() { Name = "Save note", Exact = true }).ClickAsync();
        await Expect(page.Locator(".participant-drawer-note")).ToContainTextAsync("Quick acceleration");
        // Note persistence precedes the independent roster/tag refresh. Capture the settled
        // workspace, not the intermediate mutation state that happens to render the note first.
        await Expect(page.Locator("tbody tr[id^='roster-row-']")).ToHaveCountAsync(50);
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add note", Exact = true })).ToBeEnabledAsync();
        await CaptureAsync(page, "hero-repro");
        await page.SetViewportSizeAsync(1440, 1024);
        await CaptureAsync(page, "desktop");
        await page.SetViewportSizeAsync(390, 844);
        await Expect(page.Locator("aside.participant-drawer")).ToHaveAttributeAsync("role", "dialog");
        await Expect(page.Locator("aside.participant-drawer")).ToHaveAttributeAsync("aria-modal", "true");
        await page.Locator("#participant-drawer-close").FocusAsync();
        for (var index = 0; index < 10; index++)
        {
            await page.Keyboard.PressAsync("Tab");
            (await page.Locator("aside.participant-drawer").EvaluateAsync<bool>("element => element.contains(document.activeElement)")).ShouldBeTrue();
        }
        await CaptureAsync(page, "mobile-context");
        await page.Keyboard.PressAsync("Escape");
        await Expect(page.Locator($"#roster-card-{seed.AssignmentIds[0]}")).ToBeFocusedAsync();
        await CaptureAsync(page, "mobile");
        await page.Locator("#roster-eligibility").SelectOptionAsync("NeedsPlacement");
        await Expect(page.GetByText("No participants match the current filters.")).ToBeVisibleAsync();
        await Expect(page.Locator(".campaign-facts")).ToContainTextAsync("60");
    }

    private async Task PrepareRepresentativeWorkspaceAsync(SeededEvaluationWorkspace seed)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = fixture.AppHost.CreateAdminContext();
        var campaign = await db.Campaigns.Include(c => c.Season).SingleAsync(c => c.CampaignId == seed.CampaignId, token);
        campaign.Name = "Fall evaluation";
        campaign.StartDate = new DateOnly(2026, 9, 12);
        campaign.EndDate = new DateOnly(2026, 9, 26);
        campaign.Season.Name = "2026–27 Season";
        campaign.SeasonOpeningSequence = 2;
        var assignments = await db.PlayerCampaignAssignments.Include(a => a.Player)
            .Where(a => a.CampaignId == seed.CampaignId).OrderBy(a => a.PlayerCampaignAssignmentId).ToListAsync(token);
        NameCapturePlayers(assignments);
        assignments[0].PlacementOutcome = PlacementOutcome.Undecided;
        assignments[0].DecisionRecordedAt = null;
        assignments[0].DecisionRecordedById = null;
        assignments[0].DecisionActorDisplayName = null;
        await db.SaveChangesAsync(token);
        var team = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            ClubId = seed.ClubId,
            CreatedById = seed.AdminUserId,
            Name = "U16 North",
            GraduationYear = assignments[0].Player.GraduationYear
        };
        var prior = new CampaignEntity
        {
            CreationOperationId = Guid.NewGuid(),
            ClubId = seed.ClubId,
            CreatedById = seed.AdminUserId,
            SeasonId = campaign.SeasonId,
            Name = "Spring placement",
            StartDate = new DateOnly(2026, 3, 1),
            Status = CampaignStatus.Closed,
            SeasonOpeningSequence = 1,
            ClosedAt = DateTimeOffset.UtcNow,
            ClosedById = seed.AdminUserId
        };
        db.Teams.Add(team);
        db.Campaigns.Add(prior);
        await db.SaveChangesAsync(token);
        db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
        {
            ClubId = seed.ClubId,
            CreatedById = seed.AdminUserId,
            CampaignId = prior.CampaignId,
            PlayerId = assignments[0].PlayerId,
            PlacementOutcome = PlacementOutcome.Assigned,
            TeamId = team.TeamId,
            ConcurrencyToken = Guid.NewGuid(),
            DecisionRecordedAt = DateTimeOffset.UtcNow,
            DecisionRecordedById = seed.AdminUserId,
            DecisionActorDisplayName = "Alice Author",
        });
        await db.SaveChangesAsync(token);
        await NameCaptureTagsAsync(seed);
    }

    private async Task NameCaptureTagsAsync(SeededEvaluationWorkspace seed)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = fixture.AppHost.CreateAdminContext();
        var tags = await db.PlayerTags.Where(tag => tag.ClubId == seed.ClubId).OrderBy(tag => tag.PlayerTagId).ToListAsync(token);
        for (var index = 0; index < tags.Count; index++)
        {
            tags[index].Name = index switch { 0 => "Quick feet", 1 => "Keeper", _ => "Prior evaluation" };
        }
        await db.SaveChangesAsync(token);
    }

    private static void NameCapturePlayers(List<PlayerCampaignAssignmentEntity> assignments)
    {
        foreach (var assignment in assignments)
        {
            assignment.Player.LastName = "Zimmerman";
        }
        for (var index = 0; index < _captureNames.Length; index++)
        {
            assignments[index].Player.FirstName = _captureNames[index].First;
            assignments[index].Player.LastName = _captureNames[index].Last;
        }
    }

    [Fact]
    public async Task ClosedRosterKeepsArchivedLocalEvidenceAndReportsIncompleteHistoryAsync()
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await EvaluationSeed.SeedAsync(fixture.AppHost, token);
        await PrepareClosedRecordAsync(seed);
        await using var context = await fixture.NewSignedInContextAsync(seed.AdminEmail, EvaluationSeed.Password,
            new ViewportSize { Width = 1440, Height = 1024 });
        var page = context.Pages[0];
        var path = $"/campaigns/{seed.CampaignId}/roster?participant={seed.AssignmentIds[0]}&eligibility=NeedsPlacement&page=2";
        await page.GotoAsync(new Uri(fixture.BaseUri, path).ToString());
        await Expect(page.Locator("#participant-drawer-close")).ToBeFocusedAsync();
        await Expect(page).ToHaveURLAsync(new Uri(fixture.BaseUri, $"/campaigns/{seed.CampaignId}/roster?participant={seed.AssignmentIds[0]}").ToString());
        await Expect(page.Locator("#roster-eligibility")).ToHaveCountAsync(0);
        await Expect(page.Locator("tbody tr[id^='roster-row-']")).ToHaveCountAsync(50);
        await page.ReloadAsync();
        await Expect(page.Locator("#participant-drawer-close")).ToBeFocusedAsync();
        await Expect(page.Locator("tbody tr[id^='roster-row-']")).ToHaveCountAsync(50);
        await Expect(page.Locator(".participant-placement-context")).ToContainTextAsync("U16 archived local team");
        await Expect(page.Locator(".participant-drawer-readonly-note")).ToBeVisibleAsync();
        await Expect(page.GetByRole(AriaRole.Button, new() { Name = "Add note", Exact = true })).ToHaveCountAsync(0);
        await CaptureAsync(page, "closed-desktop");
        await page.Locator("#roster-search").FillAsync("no such participant");
        await Expect(page.GetByText("No participants match the current filters.")).ToBeVisibleAsync();
        await Expect(page.Locator(".campaign-facts")).ToContainTextAsync("60");
        await CaptureAsync(page, "closed-no-matches");
        await using (var db = fixture.AppHost.CreateAdminContext())
        {
            var other = await db.PlayerCampaignAssignments.SingleAsync(a => a.PlayerCampaignAssignmentId == seed.AssignmentIds[1], token);
            other.PlacementOutcome = PlacementOutcome.Undecided;
            other.DecisionRecordedAt = null;
            other.DecisionRecordedById = null;
            other.DecisionActorDisplayName = null;
            await db.SaveChangesAsync(token);
        }
        await page.ReloadAsync();
        await Expect(page.Locator(".workspace-board .alert-danger")).ToBeVisibleAsync();
        await Expect(page.Locator(".campaign-facts")).ToContainTextAsync("60");
        await CaptureAsync(page, "closed-integrity-error");
    }

    private async Task PrepareClosedRecordAsync(SeededEvaluationWorkspace seed)
    {
        var token = TestContext.Current.CancellationToken;
        await using var db = fixture.AppHost.CreateAdminContext();
        var campaign = await db.Campaigns.Include(c => c.Season).SingleAsync(c => c.CampaignId == seed.CampaignId, token);
        campaign.Name = "Spring placement";
        campaign.Season.Name = "2026–27 Season";
        campaign.Status = CampaignStatus.Closed;
        campaign.ClosedAt = DateTimeOffset.UtcNow;
        campaign.ClosedById = seed.AdminUserId;
        var assignment = await db.PlayerCampaignAssignments.Include(a => a.Player)
            .SingleAsync(a => a.PlayerCampaignAssignmentId == seed.AssignmentIds[0], token);
        assignment.Player.FirstName = "Avery";
        assignment.Player.LastName = "Morgan";
        assignment.Player.LifecycleStatus = LifecycleStatus.Archived;
        assignment.Player.ArchivedAt = DateTimeOffset.UtcNow;
        assignment.Player.ArchivedById = seed.AdminUserId;
        var team = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            ClubId = seed.ClubId,
            CreatedById = seed.AdminUserId,
            Name = "U16 archived local team",
            GraduationYear = assignment.Player.GraduationYear,
            LifecycleStatus = LifecycleStatus.Archived,
            ArchivedAt = DateTimeOffset.UtcNow,
            ArchivedById = seed.AdminUserId
        };
        db.Teams.Add(team);
        await db.SaveChangesAsync(token);
        assignment.PlacementOutcome = PlacementOutcome.Assigned;
        assignment.TeamId = team.TeamId;
        await db.SaveChangesAsync(token);
    }

    private static async Task CaptureAsync(IPage page, string name)
    {
        var directory = Environment.GetEnvironmentVariable("NOVA_WORKSPACE_EVIDENCE");
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }
        Directory.CreateDirectory(directory);
        await page.Mouse.MoveAsync(0, 0);
        await page.EvaluateAsync("window.scrollTo(0, 0)");
        await page.Locator(".participant-drawer-body").EvaluateAllAsync<object>("elements => elements.forEach(element => element.scrollTop = 0)");
        await page.ScreenshotAsync(new()
        {
            Path = Path.Combine(directory, name + ".png"),
            FullPage = !string.Equals(name, "hero-repro", StringComparison.Ordinal) && !string.Equals(name, "mobile-context", StringComparison.Ordinal)
        });
    }
}
