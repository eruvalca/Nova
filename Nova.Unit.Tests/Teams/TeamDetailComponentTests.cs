using System.Net.Http;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Shared.Teams;
using Nova.UI.Features.Teams.Pages;
using OneOf.Types;
using Shouldly;
using TeamDetailPage = Nova.UI.Features.Teams.Pages.TeamDetail;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Component-level tests for the <see cref="TeamDetailPage"/> covering profile display, placement history,
/// role matrix, admin mutations with refresh, and all error/empty/state behaviors.
/// </summary>
public sealed class TeamDetailComponentTests : BunitContext
{
    // ── Loading state ─────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies a loading spinner appears while the detail request is pending.
    /// </summary>
    [Fact]
    public void TeamDetail_ShowsLoadingState_WhileDetailRequestIsPending()
    {
        var pending = new TaskCompletionSource<ServiceResult<TeamDetailDto>>();
        var detailService = Substitute.For<ITeamDetailService>();
        detailService.GetTeamDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(detailService: detailService);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.Markup.ShouldContain("Loading team details...");

        pending.SetResult(new ServiceResult<TeamDetailDto>(CreateTeamDetail()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));
    }

    // ── Not-found state ───────────────────────────────────────────────────────

    /// <summary>
    /// Verifies a not-found card renders when the service returns a 404.
    /// </summary>
    [Fact]
    public void TeamDetail_ShowsNotFoundState_WhenServiceReturnsNotFound()
    {
        var detailService = Substitute.For<ITeamDetailService>();
        detailService.GetTeamDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TeamDetailDto>(
                ServiceProblem.NotFound("Team not found."))));

        RegisterServices(detailService: detailService);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 99));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Team not found"));
        cut.Markup.ShouldNotContain("Loading team details...");
    }

    // ── Forbidden state ───────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the page redirects to access-denied when the service returns forbidden.
    /// </summary>
    [Fact]
    public void TeamDetail_RedirectsToAccessDenied_WhenServiceReturnsForbidden()
    {
        var detailService = Substitute.For<ITeamDetailService>();
        detailService.GetTeamDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TeamDetailDto>(
                ServiceProblem.Forbidden("Access denied."))));

        RegisterServices(detailService: detailService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldEndWith("/Account/AccessDenied"));
    }

    // ── Transport error with retry ────────────────────────────────────────────

    /// <summary>
    /// Verifies an error message and Retry button appear on transport failure, and detail loads on retry.
    /// </summary>
    [Fact]
    public void TeamDetail_ShowsErrorAndRetry_WhenTransportFails()
    {
        var detailService = Substitute.For<ITeamDetailService>();
        detailService.GetTeamDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromException<ServiceResult<TeamDetailDto>>(new HttpRequestException("Connection refused")),
                Task.FromResult(new ServiceResult<TeamDetailDto>(CreateTeamDetail())));

        RegisterServices(detailService: detailService);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Failed to load team details. Please retry."));
        cut.Find("button.btn-outline-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));
    }

    // ── Profile fields ────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the team profile fields (name, graduation year, lifecycle status) render correctly.
    /// </summary>
    [Fact]
    public void TeamDetail_DisplaysProfileFields()
    {
        RegisterServices();

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("U16 Blue");
            cut.Markup.ShouldContain("2028");
            cut.Markup.ShouldContain("Active");
        });
    }

    /// <summary>
    /// Verifies the archived lifecycle badge renders correctly for an archived team.
    /// </summary>
    [Fact]
    public void TeamDetail_ShowsArchivedLifecycleBadge_WhenTeamIsArchived()
    {
        var detailService = Substitute.For<ITeamDetailService>();
        detailService.GetTeamDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TeamDetailDto>(
                CreateTeamDetail(lifecycleStatus: LifecycleStatus.Archived))));

        RegisterServices(detailService: detailService);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() =>
            cut.Find("span.badge.text-bg-secondary").TextContent.Trim().ShouldBe("Archived"));
    }

    // ── Placement history grouping and ordering ───────────────────────────────

    /// <summary>
    /// Verifies campaign groups are rendered newest first.
    /// </summary>
    [Fact]
    public void TeamDetail_RendersPlacementGroupsNewestFirst()
    {
        var history = new List<TeamPlacementImpactDto>
        {
            BuildPlacement(1, "Early Campaign", new DateOnly(2024, 1, 1), CampaignStatus.Closed),
            BuildPlacement(2, "Recent Campaign", new DateOnly(2025, 6, 1), CampaignStatus.Active)
        };

        var detailService = Substitute.For<ITeamDetailService>();
        detailService.GetTeamDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TeamDetailDto>(
                CreateTeamDetail(placementHistory: history))));

        RegisterServices(detailService: detailService);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() =>
        {
            var articles = cut.FindAll("article");
            articles.Count.ShouldBe(2);
            articles[0].TextContent.ShouldContain("Recent Campaign");
            articles[1].TextContent.ShouldContain("Early Campaign");
        });
    }

    /// <summary>
    /// Verifies multiple placements for the same campaign collapse into one group.
    /// </summary>
    [Fact]
    public void TeamDetail_GroupsMultiplePlacementsUnderSameCampaign()
    {
        var history = new List<TeamPlacementImpactDto>
        {
            BuildPlacement(1, "Fall Tryouts", new DateOnly(2025, 9, 1), CampaignStatus.Active, playerDisplayName: "Alex Adams", campaignId: 10),
            BuildPlacement(2, "Fall Tryouts", new DateOnly(2025, 9, 1), CampaignStatus.Active, playerDisplayName: "Sam Lee", campaignId: 10)
        };

        var detailService = Substitute.For<ITeamDetailService>();
        detailService.GetTeamDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TeamDetailDto>(
                CreateTeamDetail(placementHistory: history))));

        RegisterServices(detailService: detailService);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.FindAll("article").Count.ShouldBe(1);
            cut.Markup.ShouldContain("Alex Adams");
            cut.Markup.ShouldContain("Sam Lee");
        });
    }

    // ── Empty placement history ───────────────────────────────────────────────

    /// <summary>
    /// Verifies an empty-history message renders when the placement list is empty.
    /// </summary>
    [Fact]
    public void TeamDetail_ShowsEmptyHistoryMessage_WhenNoPlacements()
    {
        RegisterServices();

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No placement history yet."));
    }

    // ── Active placement impact section ──────────────────────────────────────

    /// <summary>
    /// Verifies active placement impacts render in a dedicated section.
    /// </summary>
    [Fact]
    public void TeamDetail_ShowsActivePlacementImpacts_WhenPresent()
    {
        var active = BuildPlacement(1, "Fall Tryouts", new DateOnly(2025, 9, 1), CampaignStatus.Active);
        var detail = CreateTeamDetail(
            activePlacementImpacts: [active],
            placementHistory: [active]);

        var detailService = Substitute.For<ITeamDetailService>();
        detailService.GetTeamDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TeamDetailDto>(detail)));

        RegisterServices(detailService: detailService);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Active campaign placements"));
    }

    /// <summary>
    /// Verifies the active impact section is hidden when there are no active placements.
    /// </summary>
    [Fact]
    public void TeamDetail_HidesActivePlacementSection_WhenNoActivePlacements()
    {
        RegisterServices();

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Active campaign placements"));
    }

    // ── Role matrix: admin sees actions ──────────────────────────────────────

    /// <summary>
    /// Verifies Edit and Archive buttons appear for administrators on an active team.
    /// </summary>
    [Fact]
    public void TeamDetail_ShowsAdminActions_ForClubAdmin()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("btn-outline-primary");
            cut.Markup.ShouldContain("btn-outline-warning");
        });
    }

    /// <summary>
    /// Verifies Restore button appears for administrators on an archived team.
    /// </summary>
    [Fact]
    public void TeamDetail_ShowsRestoreButton_ForArchivedTeamAndAdmin()
    {
        var detailService = Substitute.For<ITeamDetailService>();
        detailService.GetTeamDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TeamDetailDto>(
                CreateTeamDetail(lifecycleStatus: LifecycleStatus.Archived))));

        RegisterServices(detailService: detailService, isClubAdmin: true);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Restore");
            cut.Markup.ShouldNotContain("btn-outline-warning");
        });
    }

    // ── Role matrix: evaluator is read-only ───────────────────────────────────

    /// <summary>
    /// Verifies action buttons are hidden for evaluators.
    /// </summary>
    [Fact]
    public void TeamDetail_HidesAdminActions_ForEvaluator()
    {
        RegisterServices(isClubAdmin: false);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldNotContain("btn-outline-primary");
            cut.Markup.ShouldNotContain("btn-outline-warning");
            cut.Markup.ShouldNotContain("btn-outline-success");
        });
    }

    // ── Edit mutation with refresh ────────────────────────────────────────────

    /// <summary>
    /// Verifies the edit form opens, detail refreshes, and a success message appears after a successful edit.
    /// </summary>
    [Fact]
    public void TeamDetail_RefreshesDetail_AfterSuccessfulEdit()
    {
        var managementService = Substitute.For<ITeamManagementService>();
        managementService.UpdateAsync(Arg.Any<UpdateTeamInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TeamDto>(new TeamDto
            {
                TeamId = 7,
                ClubId = 42,
                Name = "U16 Blue",
                GraduationYear = 2028,
                LifecycleStatus = LifecycleStatus.Active
            })));

        RegisterServices(isClubAdmin: true, managementService: managementService);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit team"));
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Team updated successfully."));
    }

    // ── Archive mutation with refresh ─────────────────────────────────────────

    /// <summary>
    /// Verifies the archive confirmation panel opens, team archives on confirm, and a success message appears.
    /// </summary>
    [Fact]
    public void TeamDetail_RefreshesDetail_AfterSuccessfulArchive()
    {
        var lifecycleService = Substitute.For<ITeamLifecycleService>();
        lifecycleService.ArchiveAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));

        RegisterServices(isClubAdmin: true, lifecycleService: lifecycleService);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-warning").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Archive U16 Blue?"));

        cut.Find("#archive-confirm-checkbox").Change(true);
        cut.Find("button.btn-warning").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Team archived."));
    }

    // ── Archive blockers displayed ────────────────────────────────────────────

    /// <summary>
    /// Verifies archive blockers are displayed when the archive service returns a conflict.
    /// </summary>
    [Fact]
    public void TeamDetail_ShowsArchiveBlockers_WhenArchiveReturnsConflict()
    {
        var lifecycleService = Substitute.For<ITeamLifecycleService>();
        lifecycleService.ArchiveAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(
                ServiceProblem.Conflict(
                    "Archive blocked.",
                    TeamLifecycleProblemExtensions.CreateArchiveBlockerExtensions(
                    [
                        new TeamArchiveBlocker
                        {
                            CampaignId = 15,
                            CampaignName = "Summer Tryouts",
                            PlacementIds = [44, 45]
                        }
                    ])))));

        RegisterServices(isClubAdmin: true, lifecycleService: lifecycleService);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-warning").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Archive U16 Blue?"));

        cut.Find("#archive-confirm-checkbox").Change(true);
        cut.Find("button.btn-warning").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Archive blockers:");
            cut.Markup.ShouldContain("Summer Tryouts (Campaign 15): placement IDs 44, 45");
        });
    }

    // ── Restore mutation ──────────────────────────────────────────────────────

    /// <summary>
    /// Verifies detail refreshes and a success message appears after a successful restore.
    /// </summary>
    [Fact]
    public void TeamDetail_RefreshesDetail_AfterSuccessfulRestore()
    {
        var lifecycleService = Substitute.For<ITeamLifecycleService>();
        lifecycleService.RestoreAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));

        var detailService = Substitute.For<ITeamDetailService>();
        detailService.GetTeamDetailAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TeamDetailDto>(
                CreateTeamDetail(lifecycleStatus: LifecycleStatus.Archived))));

        RegisterServices(isClubAdmin: true, detailService: detailService, lifecycleService: lifecycleService);

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Restore"));

        cut.Find("button.btn-outline-success").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Team restored."));
    }

    // ── Return URL ────────────────────────────────────────────────────────────

    /// <summary>
    /// Verifies the back link falls back to <c>/teams</c> when the return URL is external.
    /// </summary>
    [Fact]
    public void TeamDetail_UsesFallbackReturnUrl_WhenReturnUrlIsExternal()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/teams/7?returnUrl=https%3A%2F%2Fevil.example%2Fphish");

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() =>
            cut.Find("a.btn-outline-secondary").GetAttribute("href").ShouldBe("/teams"));
    }

    /// <summary>
    /// Verifies a safe relative return URL is preserved in the back link.
    /// </summary>
    [Fact]
    public void TeamDetail_PreservesSafeRelativeReturnUrl_InBackLink()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/teams/7?returnUrl=%2Fteams%3Fview%3Darchived%26graduationYear%3D2028");

        var cut = Render<TeamDetailPage>(p => p.Add(c => c.TeamId, 7));
        cut.WaitForAssertion(() =>
            cut.Find("a.btn-outline-secondary").GetAttribute("href")
                .ShouldBe("/teams?view=archived&graduationYear=2028"));
    }

    // ── GroupPlacementsByCampaign helper ──────────────────────────────────────

    /// <summary>
    /// Verifies <see cref="TeamDetail.GroupPlacementsByCampaign"/> orders groups newest first
    /// and rows alphabetically within each group.
    /// </summary>
    [Fact]
    public void GroupPlacementsByCampaign_OrdersGroupsNewestFirstAndRowsAlphabetically()
    {
        var placements = new List<TeamPlacementImpactDto>
        {
            BuildPlacement(1, "Fall 2025", new DateOnly(2025, 9, 1), CampaignStatus.Active, playerDisplayName: "Zara", campaignId: 10),
            BuildPlacement(2, "Fall 2025", new DateOnly(2025, 9, 1), CampaignStatus.Active, playerDisplayName: "Alex", campaignId: 10),
            BuildPlacement(3, "Spring 2024", new DateOnly(2024, 3, 1), CampaignStatus.Closed)
        };

        var groups = TeamDetail.GroupPlacementsByCampaign(placements);

        groups.Count.ShouldBe(2);
        groups[0].CampaignName.ShouldBe("Fall 2025");
        groups[0].Placements[0].PlayerDisplayName.ShouldBe("Alex");
        groups[0].Placements[1].PlayerDisplayName.ShouldBe("Zara");
        groups[1].CampaignName.ShouldBe("Spring 2024");
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Registers required component services in the bUnit DI container.
    /// </summary>
    /// <param name="isClubAdmin">Whether the fake user is a club admin.</param>
    /// <param name="detailService">Optional substitute for <see cref="ITeamDetailService"/>.</param>
    /// <param name="managementService">Optional substitute for <see cref="ITeamManagementService"/>.</param>
    /// <param name="lifecycleService">Optional substitute for <see cref="ITeamLifecycleService"/>.</param>
    private void RegisterServices(
        bool isClubAdmin = false,
        ITeamDetailService? detailService = null,
        ITeamManagementService? managementService = null,
        ITeamLifecycleService? lifecycleService = null)
    {
        if (detailService is null)
        {
            detailService = Substitute.For<ITeamDetailService>();
            detailService.GetTeamDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<TeamDetailDto>(CreateTeamDetail())));
        }

        managementService ??= Substitute.For<ITeamManagementService>();
        lifecycleService ??= Substitute.For<ITeamLifecycleService>();

        Services.AddSingleton(detailService);
        Services.AddSingleton(managementService);
        Services.AddSingleton(lifecycleService);
        Services.AddSingleton<AuthenticationStateProvider>(
            new FakeAuthenticationStateProvider(CreatePrincipal(isClubAdmin)));
    }

    /// <summary>
    /// Creates a default <see cref="TeamDetailDto"/> for use in tests.
    /// </summary>
    /// <param name="lifecycleStatus">Optional lifecycle status override.</param>
    /// <param name="activePlacementImpacts">Optional active placement impact override.</param>
    /// <param name="placementHistory">Optional placement history override.</param>
    /// <returns>A populated <see cref="TeamDetailDto"/>.</returns>
    private static TeamDetailDto CreateTeamDetail(
        LifecycleStatus lifecycleStatus = LifecycleStatus.Active,
        IReadOnlyList<TeamPlacementImpactDto>? activePlacementImpacts = null,
        IReadOnlyList<TeamPlacementImpactDto>? placementHistory = null)
        => new(
            TeamId: 7,
            ClubId: 42,
            Name: "U16 Blue",
            GraduationYear: 2028,
            LifecycleStatus: lifecycleStatus,
            ActivePlacementImpacts: activePlacementImpacts ?? [],
            PlacementHistory: placementHistory ?? []);

    /// <summary>
    /// Builds a minimal <see cref="TeamPlacementImpactDto"/> for placement history tests.
    /// </summary>
    /// <param name="assignmentId">The assignment identifier.</param>
    /// <param name="campaignName">The campaign display name.</param>
    /// <param name="startDate">The campaign start date.</param>
    /// <param name="campaignStatus">The campaign lifecycle status.</param>
    /// <param name="playerDisplayName">Optional player display name.</param>
    /// <returns>A populated <see cref="TeamPlacementImpactDto"/>.</returns>
    private static TeamPlacementImpactDto BuildPlacement(
        long assignmentId,
        string campaignName,
        DateOnly startDate,
        CampaignStatus campaignStatus = CampaignStatus.Active,
        string playerDisplayName = "Avery Athlete",
        long? campaignId = null)
        => new(
            PlayerCampaignAssignmentId: assignmentId,
            CampaignId: campaignId ?? assignmentId * 100,
            CampaignName: campaignName,
            CampaignStatus: campaignStatus,
            CampaignStartDate: startDate,
            PlayerId: assignmentId * 10,
            PlayerDisplayName: playerDisplayName,
            PlayerGraduationYear: 2028,
            TryoutNumber: (int)assignmentId,
            PlacementOutcome: PlacementOutcome.Assigned);

    /// <summary>
    /// Builds a <see cref="ClaimsPrincipal"/> with optional club-admin role for test authentication.
    /// </summary>
    /// <param name="isClubAdmin">Whether to add the club-admin role claim.</param>
    /// <returns>A populated claims principal.</returns>
    private static ClaimsPrincipal CreatePrincipal(bool isClubAdmin)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "101"),
            new(NovaClaimTypes.ClubId, "42")
        };

        if (isClubAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, Roles.ClubAdmin));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>
    /// Provides a fixed authentication state for bUnit component tests.
    /// </summary>
    /// <param name="principal">The principal to return from <see cref="GetAuthenticationStateAsync"/>.</param>
    private sealed class FakeAuthenticationStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(principal));
    }
}
