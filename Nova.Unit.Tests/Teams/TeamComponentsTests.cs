using System.Security.Claims;
using System.IO;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Shared.Teams;
using TeamsPage = Nova.UI.Features.Teams.Pages.Teams;
using OneOf.Types;
using Shouldly;

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
    public void Teams_ShowsMutationControls_ForAdmin()
    {
        RegisterServices(isClubAdmin: false, isAdmin: true);

        var cut = Render<TeamsPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading teams...", StringComparison.Ordinal));

        cut.Markup.ShouldContain("Add team");
        cut.Markup.ShouldContain("Edit");
        cut.Markup.ShouldContain("Archive");
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
                    new Dictionary<string, string[]>
                    {
                        ["blockers[0].assignmentId"] = ["77"],
                        ["blockers[0].campaignId"] = ["400"],
                        ["blockers[0].playerId"] = ["88"],
                        ["blockers[0].playerGraduationYear"] = ["2029"]
                    }))));

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
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(principal));
    }
}
