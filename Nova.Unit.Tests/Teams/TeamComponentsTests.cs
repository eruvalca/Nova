using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Enums;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Nova.Shared.Security;
using NSubstitute;
using OneOf.Types;
using Shouldly;
using TeamsPage = Nova.UI.Features.Teams.Pages.Teams;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Component-level tests for team roster state handling, role matrix, and mutation UX.
/// </summary>
public sealed class TeamComponentsTests : BunitContext
{
    [Fact]
    public void Teams_ShowsLoadingState_WhileRosterRequestIsPending()
    {
        var pending = new TaskCompletionSource<ServiceResult<IReadOnlyList<TeamRosterItem>>>();
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var cut = Render<TeamsPage>();
        cut.Markup.ShouldContain("Loading teams...");

        pending.SetResult(SuccessRosterResult(CreateRosterItems()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));
    }

    [Fact]
    public void Teams_ShowsEmptyState_WhenRosterHasNoRows()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult([])));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No teams found"));
    }

    [Fact]
    public void Teams_ShowsErrorAndRetries_WhenInitialLoadFails()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<IReadOnlyList<TeamRosterItem>>(ServiceProblem.ServerError("Transport failed."))),
                Task.FromResult(SuccessRosterResult(CreateRosterItems())));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Transport failed."));
        cut.Find("button.btn-outline-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));
    }

    [Fact]
    public void Teams_ShowsRetryableError_WhenRosterTransportFails()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ServiceResult<IReadOnlyList<TeamRosterItem>>>(new HttpRequestException("network")));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Failed to load teams. Please retry."));
    }

    [Fact]
    public void Teams_NavigatesToAccessDenied_WhenRosterResponseIsForbidden()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TeamRosterItem>>(ServiceProblem.Forbidden("Not authorized."))));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        var cut = Render<TeamsPage>();

        cut.WaitForAssertion(() =>
            navigationManager.Uri.ShouldContain("/Account/AccessDenied"),
            timeout: TimeSpan.FromSeconds(3));
    }

    [Fact]
    public void Teams_ShowsMutationControls_ForClubAdmin()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<TeamsPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading teams...", StringComparison.Ordinal));

        cut.Markup.ShouldContain("Add team");
        cut.Markup.ShouldContain("Edit");
        cut.Markup.ShouldContain("Archive");
    }

    [Fact]
    public void Teams_RowActions_IncludeTeamNameInAccessibleLabel()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<TeamsPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading teams...", StringComparison.Ordinal));

        cut.Find("button.btn-outline-primary").GetAttribute("aria-label").ShouldBe("Edit U16 Blue");
        cut.Find("button.btn-outline-warning").GetAttribute("aria-label").ShouldBe("Archive U16 Blue");
    }

    /// <summary>
    /// The global Admin role carries no club tenancy, so it must not surface club-scoped
    /// mutation controls.
    /// </summary>
    [Fact]
    public void Teams_HidesMutationControls_ForGlobalAdminWithoutClubAdmin()
    {
        RegisterServices(isClubAdmin: false, isAdmin: true);

        var cut = Render<TeamsPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading teams...", StringComparison.Ordinal));

        cut.Markup.ShouldNotContain("Add team");
        cut.Markup.ShouldNotContain("btn-outline-primary");
        cut.Markup.ShouldNotContain("btn-outline-warning");
    }

    [Fact]
    public void Teams_HidesMutationControls_ForEvaluator()
    {
        RegisterServices(isClubAdmin: false);

        var cut = Render<TeamsPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading teams...", StringComparison.Ordinal));

        cut.Markup.ShouldNotContain("Add team");
        cut.Markup.ShouldNotContain("btn-outline-primary");
        cut.Markup.ShouldNotContain("btn-outline-warning");
    }

    [Fact]
    public void Teams_ShowsMutationControls_WhenClubAdminRoleIsGrantedAfterLoad()
    {
        RegisterServices(isClubAdmin: false);
        var auth = new FakeAuthenticationStateProvider(CreatePrincipal(isClubAdmin: false, isAdmin: false));
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = Render<TeamsPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading teams...", StringComparison.Ordinal));
        cut.Markup.ShouldNotContain("Add team");

        auth.Change(CreatePrincipal(isClubAdmin: true, isAdmin: false));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Add team");
            cut.Markup.ShouldContain("Edit");
            cut.Markup.ShouldContain("Archive");
        });
    }

    [Fact]
    public void Teams_HidesMutationControls_WhenClubAdminRoleIsRevokedAfterLoad()
    {
        RegisterServices(isClubAdmin: true);
        var auth = new FakeAuthenticationStateProvider(CreatePrincipal(isClubAdmin: true, isAdmin: false));
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = Render<TeamsPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading teams...", StringComparison.Ordinal));
        cut.Markup.ShouldContain("Add team");

        auth.Change(CreatePrincipal(isClubAdmin: false, isAdmin: false));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldNotContain("Add team");
            cut.Markup.ShouldNotContain("btn-outline-primary");
            cut.Markup.ShouldNotContain("btn-outline-warning");
        });
    }

    [Fact]
    public void Teams_AppliesLifecycleAndGraduationFilters_WhenInputsChange()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems())));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);
        var navigationManager = Services.GetRequiredService<NavigationManager>();

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("#teams-view-filter").Change("archived");
        cut.WaitForAssertion(() =>
            rosterService.Received().GetRosterAsync(
                Arg.Is<GetTeamRosterInput>(input =>
                    input != null
                    && input.LifecycleStatus != null
                    && string.Equals(input.LifecycleStatus, "archived", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>()));
        navigationManager.Uri.ShouldContain("view=archived");

        cut.Find("#teams-grad-year").Change("2032");
        cut.WaitForAssertion(() =>
            rosterService.Received().GetRosterAsync(
                Arg.Is<GetTeamRosterInput>(input => input != null && input.GraduationYear == 2032),
                Arg.Any<CancellationToken>()));
        navigationManager.Uri.ShouldContain("graduationYear=2032");
    }

    [Fact]
    public void Teams_AppliesSearchFilter_AfterDebounce()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems())));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);
        var navigationManager = Services.GetRequiredService<NavigationManager>();

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("#teams-search").Input("Blue");
        cut.WaitForAssertion(() =>
            rosterService.Received().GetRosterAsync(
                Arg.Is<GetTeamRosterInput>(input =>
                    input != null
                    && input.Search != null
                    && string.Equals(input.Search, "Blue", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>()),
            timeout: TimeSpan.FromSeconds(2));
        navigationManager.Uri.ShouldContain("search=Blue");
    }

    [Fact]
    public void Teams_AppliesInitialQueryStringFilters_OnFirstRender()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems(LifecycleStatus.Archived))));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("https://localhost/teams?view=archived&search=Blue&graduationYear=2032");

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() =>
            rosterService.Received().GetRosterAsync(
                Arg.Is<GetTeamRosterInput>(input =>
                    input != null
                    && string.Equals(input.LifecycleStatus, "archived", StringComparison.Ordinal)
                    && string.Equals(input.Search, "Blue", StringComparison.Ordinal)
                    && input.GraduationYear == 2032),
                Arg.Any<CancellationToken>()));
        cut.WaitForAssertion(() => cut.Find("#teams-search").GetAttribute("value").ShouldBe("Blue"));
    }

    [Fact]
    public void Teams_IgnoresMalformedGraduationYearQuery_OnFirstRender()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems(LifecycleStatus.Archived))));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("https://localhost/teams?view=archived&search=Blue&graduationYear=abc");

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() =>
            rosterService.Received().GetRosterAsync(
                Arg.Is<GetTeamRosterInput>(input =>
                    input != null
                    && string.Equals(input.LifecycleStatus, "archived", StringComparison.Ordinal)
                    && string.Equals(input.Search, "Blue", StringComparison.Ordinal)
                    && input.GraduationYear == null),
                Arg.Any<CancellationToken>()));
        cut.WaitForAssertion(() => cut.Find("#teams-search").GetAttribute("value").ShouldBe("Blue"));
    }

    [Fact]
    public void Teams_AppliesUpdatedQueryStringFilters_OnSameRouteNavigation()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems(LifecycleStatus.Archived))));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);
        var navigationManager = Services.GetRequiredService<NavigationManager>();

        var cut = Render<TeamsPage>();
        navigationManager.NavigateTo("https://localhost/teams?view=archived&search=Blue&graduationYear=2032");
        cut.Render();

        cut.WaitForAssertion(() =>
            rosterService.Received().GetRosterAsync(
                Arg.Is<GetTeamRosterInput>(input =>
                    input != null
                    && string.Equals(input.LifecycleStatus, "archived", StringComparison.Ordinal)
                    && string.Equals(input.Search, "Blue", StringComparison.Ordinal)
                    && input.GraduationYear == 2032),
                Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Teams_PreservesSearchDraft_WhenLifecycleChangesBeforeDebounce()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems())));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);
        var navigationManager = Services.GetRequiredService<NavigationManager>();

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("#teams-search").Input("Blue");
        cut.Find("#teams-view-filter").Change("archived");

        cut.WaitForAssertion(() =>
            rosterService.Received().GetRosterAsync(
                Arg.Is<GetTeamRosterInput>(input =>
                    input != null
                    && string.Equals(input.Search, "Blue", StringComparison.Ordinal)
                    && string.Equals(input.LifecycleStatus, "archived", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>()));
        navigationManager.Uri.ShouldContain("search=Blue");
    }

    [Fact]
    public void Teams_PreservesSearchDraft_WhenGraduationYearChangesBeforeDebounce()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems())));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);
        var navigationManager = Services.GetRequiredService<NavigationManager>();

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("#teams-search").Input("Blue");
        cut.Find("#teams-grad-year").Change("2032");

        cut.WaitForAssertion(() =>
            rosterService.Received().GetRosterAsync(
                Arg.Is<GetTeamRosterInput>(input =>
                    input != null
                    && string.Equals(input.Search, "Blue", StringComparison.Ordinal)
                    && input.GraduationYear == 2032),
                Arg.Any<CancellationToken>()));
        navigationManager.Uri.ShouldContain("search=Blue");
    }

    [Fact]
    public void Teams_ShowsCreateSuccessMessage_AfterMutationReload()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems())));

        var managementService = Substitute.For<ITeamManagementService>();
        managementService.CreateAsync(Arg.Any<CreateTeamInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TeamDto>(new TeamDto
            {
                TeamId = 21,
                ClubId = 42,
                Name = "U14 White",
                GraduationYear = 2034,
                LifecycleStatus = LifecycleStatus.Active
            })));

        RegisterServices(
            rosterService: rosterService,
            managementService: managementService,
            isClubAdmin: true);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Add team"));
        cut.Find("#team-name").Change("U14 White");
        cut.Find("#team-grad-year").Change(2034);
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Team created successfully."));
    }

    [Fact]
    public void Teams_ShowsCutoffConflictBlockers_WhenUpdateReturnsConflict()
    {
        var managementService = Substitute.For<ITeamManagementService>();
        managementService.UpdateAsync(Arg.Any<UpdateTeamInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TeamDto>(
                ServiceProblem.Conflict(
                    "Update blocked.",
                    TeamLifecycleProblemExtensions.CreateGraduationYearBlockerExtensions(
                    [
                        new TeamGraduationYearBlockerItem
                        {
                            PlayerCampaignAssignmentId = 77,
                            CampaignId = 400,
                            PlayerId = 88,
                            PlayerGraduationYear = 2029
                        }
                    ])))));

        RegisterServices(isClubAdmin: true, managementService: managementService);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit team"));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Resolve ineligible active placements before raising this team's cutoff:");
            cut.Markup.ShouldContain("Campaign 400: Player 88 (graduation year 2029), assignment 77.");
        });
    }

    /// <summary>
    /// Verifies the form surfaces the server's conflict detail text. Guards against the parameter
    /// being bound to a literal string (for example <c>ErrorMessage="_formError"</c>) instead of the
    /// backing field, which silently renders the field name to the user.
    /// </summary>
    [Fact]
    public void Teams_ShowsServerErrorText_WhenUpdateReturnsConflict()
    {
        var managementService = Substitute.For<ITeamManagementService>();
        managementService.UpdateAsync(Arg.Any<UpdateTeamInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<TeamDto>(
                ServiceProblem.Conflict("A team with that name and graduation year already exists."))));

        RegisterServices(isClubAdmin: true, managementService: managementService);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit team"));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("A team with that name and graduation year already exists.");
            cut.Markup.ShouldNotContain("_formError");
        });
    }

    [Fact]
    public void Teams_ShowsArchiveBlockers_WhenArchiveReturnsConflict()
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
                            PlacementIds = [44]
                        }
                    ])))));

        RegisterServices(isClubAdmin: true, lifecycleService: lifecycleService);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-warning").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Archive U16 Blue?"));

        cut.Find("#archive-confirm-checkbox").Change(true);
        cut.Find("button.btn-warning").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Archive blockers:");
            cut.Markup.ShouldContain("Summer Tryouts (Campaign 15): placement IDs 44");
        });
    }

    [Fact]
    public void Teams_ShowsArchiveTransportError_AndKeepsArchiveWorkflowOpen()
    {
        var lifecycleService = Substitute.For<ITeamLifecycleService>();
        lifecycleService.ArchiveAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromException<ServiceResult<Success>>(new HttpRequestException("network")));

        RegisterServices(isClubAdmin: true, lifecycleService: lifecycleService);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-warning").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Archive U16 Blue?"));

        cut.Find("#archive-confirm-checkbox").Change(true);
        cut.Find("button.btn-warning").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Could not archive team. Please retry.");
            cut.Markup.ShouldContain("Archive U16 Blue?");
        });
    }

    [Fact]
    public void Teams_BeginArchive_ClosesEditWorkflow()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit team"));

        cut.Find("button.btn-outline-warning").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Archive U16 Blue?");
            cut.Markup.ShouldNotContain("Save changes");
        });
    }

    [Fact]
    public void Teams_ShowCreateForm_ClosesArchiveWorkflow()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-warning").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Archive U16 Blue?"));

        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Add team");
            cut.Markup.ShouldNotContain("Archive U16 Blue?");
        });
    }

    [Fact]
    public void Teams_BeginEdit_ClosesArchiveWorkflow()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-warning").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Archive U16 Blue?"));

        cut.Find("button.btn-outline-primary").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Edit team");
            cut.Markup.ShouldNotContain("Archive U16 Blue?");
        });
    }

    [Fact]
    public void Teams_ShowsLifecycleMutationError_InGlobalAlert()
    {
        var lifecycleService = Substitute.For<ITeamLifecycleService>();
        lifecycleService.RestoreAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(ServiceProblem.Conflict("Restore blocked."))));

        RegisterServices(isClubAdmin: true, lifecycleService: lifecycleService, rosterItems: CreateRosterItems(LifecycleStatus.Archived));

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-success").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Restore blocked."));
    }

    [Fact]
    public void Teams_ClearsRestoreSuccessMessage_WhenLaterRestoreFails()
    {
        var lifecycleService = Substitute.For<ITeamLifecycleService>();
        lifecycleService.RestoreAsync(7, Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<Success>(new Success())),
                Task.FromResult(new ServiceResult<Success>(ServiceProblem.Conflict("Restore blocked."))));

        RegisterServices(
            isClubAdmin: true,
            lifecycleService: lifecycleService,
            rosterItems: CreateRosterItems(LifecycleStatus.Archived));

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-success").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Team restored."));

        cut.Find("button.btn-outline-success").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Restore blocked.");
            cut.Markup.ShouldNotContain("Team restored.");
        });
    }

    [Fact]
    public void Teams_ShowsOnlyRestore_ForArchivedRows()
    {
        RegisterServices(isClubAdmin: true, rosterItems: CreateRosterItems(LifecycleStatus.Archived));

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Markup.ShouldNotContain("btn-outline-primary");
        cut.Markup.ShouldNotContain("btn-outline-warning");
        cut.Markup.ShouldContain("btn-outline-success");
        cut.Find("button.btn-outline-success").GetAttribute("aria-label").ShouldBe("Restore U16 Blue");
    }

    [Fact]
    public void TeamsRoute_DeclaresInteractiveAutoRenderMode()
    {
        var repoRoot = FindRepoRoot();
        var razorPath = Path.Combine(repoRoot, "Nova.UI", "Features", "Teams", "Pages", "Teams.razor");
        File.ReadAllText(razorPath).ShouldContain("@rendermode InteractiveAuto");
    }

    [Fact]
    public void TeamForm_ShowsValidationMessages_WhenSubmittedInvalid()
    {
        var model = new Nova.UI.Features.Teams.Components.TeamFormState
        {
            Name = string.Empty,
            GraduationYear = 2032
        };

        var cut = Render<Nova.UI.Features.Teams.Components.TeamForm>(parameters => parameters
            .Add(component => component.Heading, "Add team")
            .Add(component => component.Model, model)
            .Add(component => component.SubmitButtonText, "Create team"));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The Name field is required."));
    }

    private void RegisterServices(
        bool isClubAdmin,
        bool isAdmin = false,
        ITeamRosterService? rosterService = null,
        ITeamManagementService? managementService = null,
        ITeamLifecycleService? lifecycleService = null,
        IReadOnlyList<TeamRosterItem>? rosterItems = null)
    {
        rosterItems ??= CreateRosterItems();

        if (rosterService is null)
        {
            rosterService = Substitute.For<ITeamRosterService>();
            rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(SuccessRosterResult(rosterItems)));
        }

        managementService ??= Substitute.For<ITeamManagementService>();
        lifecycleService ??= Substitute.For<ITeamLifecycleService>();

        Services.AddSingleton(rosterService);
        Services.AddSingleton(managementService);
        Services.AddSingleton(lifecycleService);
        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuthenticationStateProvider(CreatePrincipal(isClubAdmin, isAdmin)));
    }

    private static ServiceResult<IReadOnlyList<TeamRosterItem>> SuccessRosterResult(IReadOnlyList<TeamRosterItem> items)
        => new(items.ToList().AsReadOnly());

    private static List<TeamRosterItem> CreateRosterItems(LifecycleStatus lifecycleStatus = LifecycleStatus.Active)
    {
        return
        [
            new TeamRosterItem
            {
                TeamId = 7,
                Name = "U16 Blue",
                GraduationYear = 2032,
                LifecycleStatus = lifecycleStatus,
                ActivePlacementCount = 1
            }
        ];
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var gitDirectoryPath = Path.Combine(directory.FullName, ".git");
            if (Directory.Exists(gitDirectoryPath) || File.Exists(gitDirectoryPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for Teams route assertion.");
    }

    private static ClaimsPrincipal CreatePrincipal(bool isClubAdmin, bool isAdmin)
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

        if (isAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, Roles.Admin));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>
    /// Provides a fixed authentication state for bUnit component tests.
    /// </summary>
    /// <param name="principal">The principal to return from <see cref="GetAuthenticationStateAsync"/>.</param>
    private sealed class FakeAuthenticationStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        private Task<AuthenticationState> _state = Task.FromResult(new AuthenticationState(principal));

        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state;

        /// <summary>
        /// Raises an authentication-state change notification with a new principal.
        /// </summary>
        /// <param name="newPrincipal">The principal to publish to subscribers.</param>
        public void Change(ClaimsPrincipal newPrincipal)
            => NotifyAuthenticationStateChanged(_state = Task.FromResult(new AuthenticationState(newPrincipal)));
    }
}
