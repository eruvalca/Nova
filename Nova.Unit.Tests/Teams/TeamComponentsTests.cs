using System.Globalization;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Enums;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Unit.Tests.Components;
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
    /// <summary>Verifies Draft return context is discarded on club or role changes while directory filters survive.</summary>
    /// <param name="clubChange">Whether authority changes by switching clubs instead of revoking the administrator role.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Teams_DiscardsDraftReturnContext_WhenScopeChanges(bool clubChange)
    {
        RegisterServices(isClubAdmin: true);
        var authentication = new ControlledAuthenticationStateProvider(CreatePrincipal(true, false));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/club/teams?returnToDraft=10&view=archived&search=Blue");
        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Return to draft"));

        await cut.InvokeAsync(() => authentication.Publish(CreatePrincipal(clubChange, false, clubChange ? 43 : 42)));

        cut.WaitForAssertion(() => navigation.Uri.ShouldNotContain("returnToDraft"));
        navigation.Uri.ShouldContain("view=archived");
        navigation.Uri.ShouldContain("search=Blue");
        cut.Instance.ReturnToDraft.ShouldBeNull();
        cut.Markup.ShouldNotContain("Return to draft");
        cut.FindAll("a").ShouldNotContain(link => (link.GetAttribute("href") ?? string.Empty).Contains("returnToDraft", StringComparison.OrdinalIgnoreCase));
        if (!clubChange)
        {
            await cut.InvokeAsync(() => authentication.Publish(CreatePrincipal(true, false)));
            cut.Markup.ShouldNotContain("Return to draft");
            // Role-only changes keep the current club's roster and its independent request ownership.
            cut.Markup.ShouldContain("U16 Blue");
            await Services.GetRequiredService<ITeamRosterService>().Received(1)
                .GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>());
        }
    }

    /// <summary>Verifies pending and unchanged authentication retain a team edit under the existing club-scoped ownership policy.</summary>
    [Fact]
    public async Task Teams_PreservesEdit_DuringPendingAndUnchangedAuthentication()
    {
        RegisterServices(isClubAdmin: true);
        var authentication = new ControlledAuthenticationStateProvider(CreatePrincipal(true, false));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));
        cut.Find("button.btn-outline-primary").Click();
        cut.Find("#team-name").Change("Unsaved current team");
        var pending = new TaskCompletionSource<AuthenticationState>();

        await cut.InvokeAsync(() => authentication.Publish(pending.Task));

        cut.Find("#team-name").GetAttribute("value").ShouldBe("Unsaved current team");
        await cut.InvokeAsync(() => pending.SetResult(new AuthenticationState(CreatePrincipal(true, false))));
        cut.Find("#team-name").GetAttribute("value").ShouldBe("Unsaved current team");
        await Services.GetRequiredService<ITeamRosterService>().Received(1)
            .GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies an empty identity overtaking startup reaches the club-required state without a team query.</summary>
    [Fact]
    public async Task Teams_AppliesEmptyIdentity_WhenItOvertakesStartup()
    {
        RegisterServices(isClubAdmin: true);
        var pending = new TaskCompletionSource<AuthenticationState>();
        var authentication = new ControlledAuthenticationStateProvider(pending.Task);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<TeamsPage>();
        var roster = Services.GetRequiredService<ITeamRosterService>();

        await cut.InvokeAsync(() => authentication.Publish(Task.FromResult(new AuthenticationState(new ClaimsPrincipal()))));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("You must join a club before viewing the team roster."));
        cut.Instance.Initialized.ShouldBeTrue();
        await cut.InvokeAsync(() => pending.SetResult(new AuthenticationState(CreatePrincipal(true, false))));
        cut.Markup.ShouldContain("You must join a club before viewing the team roster.");
        cut.FindAll("tbody tr").ShouldBeEmpty();
        roster.ReceivedCalls().ShouldBeEmpty();
    }

    /// <summary>Verifies late administrator authentication cannot overwrite a newer revocation.</summary>
    /// <param name="startup">Whether the older task is the initial authentication read.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Teams_IgnoresOvertakenAdministratorAuthentication(bool startup)
    {
        RegisterServices(isClubAdmin: true);
        var older = new TaskCompletionSource<AuthenticationState>();
        var administrator = new AuthenticationState(CreatePrincipal(isClubAdmin: true, isAdmin: false));
        var authentication = new ControlledAuthenticationStateProvider(startup ? older.Task : Task.FromResult(administrator));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/club/teams?returnToDraft=10");
        var cut = Render<TeamsPage>();
        if (!startup)
        {
            cut.WaitForAssertion(() => cut.Markup.ShouldContain("Add team"));
            await cut.InvokeAsync(() => authentication.Publish(older.Task));
        }
        await cut.InvokeAsync(() => authentication.Publish(Task.FromResult(
            new AuthenticationState(CreatePrincipal(isClubAdmin: false, isAdmin: false)))));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));
        cut.Markup.ShouldNotContain("Add team");
        cut.Markup.ShouldNotContain("Return to draft");

        await cut.InvokeAsync(() => older.SetResult(administrator));

        cut.Instance.Initialized.ShouldBeTrue();
        cut.Markup.ShouldNotContain("Add team");
        cut.Markup.ShouldNotContain("Archive team");
        cut.Markup.ShouldNotContain("Return to draft");
    }

    /// <summary>Verifies a pending identity completion cannot reload the roster after disposal.</summary>
    [Fact]
    public async Task Teams_IgnoresAuthenticationCompletion_AfterDisposal()
    {
        RegisterServices(isClubAdmin: true);
        var authentication = new ControlledAuthenticationStateProvider(Task.FromResult(
            new AuthenticationState(CreatePrincipal(isClubAdmin: true, isAdmin: false))));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));
        var roster = Services.GetRequiredService<ITeamRosterService>();
        var callsBeforeDisposal = roster.ReceivedCalls().Count();
        var pending = new TaskCompletionSource<AuthenticationState>();
        await cut.InvokeAsync(() => authentication.Publish(pending.Task));
        await cut.Instance.DisposeAsync();
        cut.Dispose();

        await cut.InvokeAsync(() => pending.SetResult(new AuthenticationState(
            CreatePrincipal(isClubAdmin: true, isAdmin: false, clubId: 43))));

        roster.ReceivedCalls().Count().ShouldBe(callsBeforeDisposal);
    }

    /// <summary>Verifies crafted Draft return context is hidden from members and disappears when administrator access is revoked.</summary>
    /// <param name="startsAsAdmin">Whether administrator access is initially granted before being revoked.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void Teams_ReturnToDraft_RequiresCurrentAdministratorRole(bool startsAsAdmin)
    {
        RegisterServices(isClubAdmin: startsAsAdmin);
        var authentication = new ControlledAuthenticationStateProvider(CreatePrincipal(startsAsAdmin, isAdmin: false));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/club/teams?returnToDraft=10");
        var cut = Render<TeamsPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading teams...", StringComparison.Ordinal));

        if (startsAsAdmin)
        {
            cut.FindAll("a").Single(link => link.TextContent.Trim() == "Return to draft")
                .GetAttribute("href").ShouldBe("/campaigns/10");
            authentication.Publish(CreatePrincipal(isClubAdmin: false, isAdmin: false));
        }

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Return to draft"));
    }

    /// <summary>Configures the shell's browser-only focus restoration while component tests exercise team workflows.</summary>
    public TeamComponentsTests()
    {
        JSInterop.SetupModule("./_content/Nova.UI/Features/Clubs/Components/ClubShell.razor.js")
            .Setup<bool>("restoreHeadingFocusAfterAttach", _ => true).SetResult(true);
    }

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
        var auth = new ControlledAuthenticationStateProvider(CreatePrincipal(isClubAdmin: false, isAdmin: false));
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = Render<TeamsPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading teams...", StringComparison.Ordinal));
        cut.Markup.ShouldNotContain("Add team");

        auth.Publish(CreatePrincipal(isClubAdmin: true, isAdmin: false));

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
        var auth = new ControlledAuthenticationStateProvider(CreatePrincipal(isClubAdmin: true, isAdmin: false));
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = Render<TeamsPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading teams...", StringComparison.Ordinal));
        cut.Markup.ShouldContain("Add team");

        auth.Publish(CreatePrincipal(isClubAdmin: false, isAdmin: false));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldNotContain("Add team");
            cut.Markup.ShouldNotContain("btn-outline-primary");
            cut.Markup.ShouldNotContain("btn-outline-warning");
        });
    }

    [Fact]
    public void Teams_RebindsRoster_WhenClubMembershipChangesAfterLoad()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(SuccessRosterResult(
                [
                    new TeamRosterItem
                    {
                        TeamId = 7,
                        Name = "U16 Orange",
                        GraduationYear = 2032,
                        LifecycleStatus = LifecycleStatus.Active,
                        ActivePlacementCount = 1
                    }
                ])),
                Task.FromResult(SuccessRosterResult(
                [
                    new TeamRosterItem
                    {
                        TeamId = 9,
                        Name = "U18 Crimson",
                        GraduationYear = 2034,
                        LifecycleStatus = LifecycleStatus.Active,
                        ActivePlacementCount = 2
                    }
                ])));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);
        var auth = new ControlledAuthenticationStateProvider(CreatePrincipal(isClubAdmin: true, isAdmin: false, clubId: 42));
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Orange"));

        auth.Publish(CreatePrincipal(isClubAdmin: true, isAdmin: false, clubId: 43));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("U18 Crimson");
            cut.Markup.ShouldNotContain("U16 Orange");
        });
        rosterService.Received(2).GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When club membership changes, the page must render the loading state before the new
    /// club's roster request completes instead of leaving the previous club's roster visible.
    /// </summary>
    [Fact]
    public void Teams_ShowsLoadingState_WhenClubMembershipChangesBeforeReloadCompletes()
    {
        var pending = new TaskCompletionSource<ServiceResult<IReadOnlyList<TeamRosterItem>>>();
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(SuccessRosterResult(CreateRosterItems(name: "U16 Orange"))),
                pending.Task);

        RegisterServices(rosterService: rosterService, isClubAdmin: true);
        var auth = new ControlledAuthenticationStateProvider(CreatePrincipal(isClubAdmin: true, isAdmin: false, clubId: 42));
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Orange"));

        auth.Publish(CreatePrincipal(isClubAdmin: true, isAdmin: false, clubId: 43));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Loading teams...");
            cut.Markup.ShouldNotContain("U16 Orange");
        });

        pending.SetResult(SuccessRosterResult(CreateRosterItems(name: "U18 Crimson", teamId: 9)));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("U18 Crimson");
            cut.Markup.ShouldNotContain("U16 Orange");
        });
    }

    /// <summary>
    /// A club switch cancels the previous club's in-flight mutation and must release mutation
    /// ownership so the new club's management controls are not disabled by a stalled operation.
    /// A stale mutation completing later must not clear the flag of a newer mutation.
    /// </summary>
    [Fact]
    public void Teams_ReenablesMutationControls_WhenClubChangesDuringInFlightMutation()
    {
        var pendingCreate1 = new TaskCompletionSource<ServiceResult<TeamDto>>();
        var pendingCreate2 = new TaskCompletionSource<ServiceResult<TeamDto>>();
        var managementService = Substitute.For<ITeamManagementService>();
        managementService.CreateAsync(Arg.Any<CreateTeamInput>(), Arg.Any<CancellationToken>())
            .Returns(pendingCreate1.Task, pendingCreate2.Task);

        RegisterServices(managementService: managementService, isClubAdmin: true);
        var auth = new ControlledAuthenticationStateProvider(CreatePrincipal(isClubAdmin: true, isAdmin: false, clubId: 42));
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        // Start a create mutation that stalls on the pending task.
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Add team"));
        cut.Find("#team-name").Change("U14 White");
        cut.Find("#team-grad-year").Change(2034);
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Find("button[type='submit']").HasAttribute("disabled"));

        // Switch club while the mutation is still in flight.
        auth.Publish(CreatePrincipal(isClubAdmin: true, isAdmin: false, clubId: 43));

        // The new club's controls must be usable again immediately, even though the old
        // mutation never completed.
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("U16 Blue");
            cut.Markup.ShouldNotContain("Loading teams...");
        });
        cut.Find("button.btn-primary").HasAttribute("disabled").ShouldBeFalse();

        // Start a second mutation for the new club, then complete the stale one. Its finally
        // must not clear the newer mutation's flag.
        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Add team"));
        cut.Find("#team-name").Change("U18 Crimson");
        cut.Find("#team-grad-year").Change(2032);
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Find("button[type='submit']").HasAttribute("disabled"));

        pendingCreate1.SetResult(new ServiceResult<TeamDto>(new TeamDto
        {
            TeamId = 22,
            ClubId = 43,
            Name = "U16 Blue",
            GraduationYear = 2032,
            LifecycleStatus = LifecycleStatus.Active
        }));
        cut.Render();
        cut.Find("button[type='submit']").HasAttribute("disabled").ShouldBeTrue();

        pendingCreate2.SetResult(new ServiceResult<TeamDto>(new TeamDto
        {
            TeamId = 23,
            ClubId = 43,
            Name = "U18 Crimson",
            GraduationYear = 2032,
            LifecycleStatus = LifecycleStatus.Active
        }));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Team created successfully."));
    }

    /// <summary>
    /// When club membership changes, graduation-year filter options must be derived from the
    /// new club's roster only — years from the previous club must not leak into the dropdown.
    /// </summary>
    [Fact]
    public void Teams_ClearsGraduationYearOptions_WhenClubMembershipChanges()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(SuccessRosterResult(
                [
                    new TeamRosterItem
                    {
                        TeamId = 7,
                        Name = "U16 Orange",
                        GraduationYear = 2032,
                        LifecycleStatus = LifecycleStatus.Active,
                        ActivePlacementCount = 1
                    }
                ])),
                Task.FromResult(SuccessRosterResult(
                [
                    new TeamRosterItem
                    {
                        TeamId = 9,
                        Name = "U18 Crimson",
                        GraduationYear = 2034,
                        LifecycleStatus = LifecycleStatus.Active,
                        ActivePlacementCount = 2
                    }
                ])));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);
        var auth = new ControlledAuthenticationStateProvider(CreatePrincipal(isClubAdmin: true, isAdmin: false, clubId: 42));
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Orange"));

        var firstFilter = cut.Find("#teams-grad-year");
        firstFilter.TextContent.ShouldContain("2032");

        auth.Publish(CreatePrincipal(isClubAdmin: true, isAdmin: false, clubId: 43));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U18 Crimson"));
        var secondFilter = cut.Find("#teams-grad-year");
        secondFilter.TextContent.ShouldContain("2034");
        secondFilter.TextContent.ShouldNotContain("2032");
    }

    /// <summary>
    /// On an interactive attach after a club change, the prerendered roster snapshot from the
    /// previous club must not be restored; the page must reload against the new club scope instead.
    /// </summary>
    [Fact]
    public void Teams_ReloadsRoster_WhenPersistedSnapshotBelongsToDifferentClub()
    {
        var rosterService = Substitute.For<ITeamRosterService>();
        rosterService.GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(
            [
                new TeamRosterItem
                {
                    TeamId = 9,
                    Name = "U18 Crimson",
                    GraduationYear = 2034,
                    LifecycleStatus = LifecycleStatus.Active,
                    ActivePlacementCount = 2
                }
            ])));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);
        Services.AddSingleton<AuthenticationStateProvider>(
            new ControlledAuthenticationStateProvider(CreatePrincipal(isClubAdmin: true, isAdmin: false, clubId: 43)));

        var cut = Render<PersistedClubIdTeams>(parameters => parameters
            .Add(component => component.StartInitialized, true));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U18 Crimson"));
        cut.Markup.ShouldNotContain("U14 Emerald");
        rosterService.Received(1).GetRosterAsync(Arg.Any<GetTeamRosterInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Teams_ClosesManagementPanels_WhenClubAdminRoleIsRevokedAfterLoad()
    {
        RegisterServices(isClubAdmin: true);
        var auth = new ControlledAuthenticationStateProvider(CreatePrincipal(isClubAdmin: true, isAdmin: false));
        Services.AddSingleton<AuthenticationStateProvider>(auth);

        var cut = Render<TeamsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("U16 Blue"));

        cut.Find("button.btn-outline-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit team"));

        auth.Publish(CreatePrincipal(isClubAdmin: false, isAdmin: false));

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldNotContain("Edit team");
            cut.Markup.ShouldNotContain("Add team");
            cut.Markup.ShouldNotContain("Save changes");
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
        Services.AddSingleton<AuthenticationStateProvider>(new ControlledAuthenticationStateProvider(CreatePrincipal(isClubAdmin, isAdmin)));
    }

    private static ServiceResult<IReadOnlyList<TeamRosterItem>> SuccessRosterResult(IReadOnlyList<TeamRosterItem> items)
        => new(items.ToList().AsReadOnly());

    private static List<TeamRosterItem> CreateRosterItems(
        LifecycleStatus lifecycleStatus = LifecycleStatus.Active,
        string name = "U16 Blue",
        long teamId = 7)
    {
        return
        [
            new TeamRosterItem
            {
                TeamId = teamId,
                Name = name,
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

    private static ClaimsPrincipal CreatePrincipal(bool isClubAdmin, bool isAdmin, long clubId = 42)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "101"),
            new(NovaClaimTypes.ClubId, clubId.ToString(CultureInfo.InvariantCulture))
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
    /// Starts with a prerendered state already restored from the previous club (club 42), so tests can
    /// exercise the interactive-attach path where the persisted snapshot's club differs from the current one.
    /// </summary>
    private sealed class PersistedClubIdTeams(
        ITeamRosterService rosterService,
        ITeamManagementService managementService,
        ITeamLifecycleService lifecycleService,
        AuthenticationStateProvider authenticationStateProvider,
        NavigationManager navigationManager)
        : TeamsPage(rosterService, managementService, lifecycleService, authenticationStateProvider, navigationManager)
    {
        [Parameter] public bool StartInitialized { get; set; }

        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                PersistedClubId = 42;
                PersistedRoster =
                [
                    new TeamRosterItem
                    {
                        TeamId = 5,
                        Name = "U14 Emerald",
                        GraduationYear = 2031,
                        LifecycleStatus = LifecycleStatus.Active,
                        ActivePlacementCount = 1
                    }
                ];
                PersistedPageError = null;
            }

            return base.OnInitializedAsync();
        }
    }
}
