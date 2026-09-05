using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Shared.Security;
using NSubstitute;
using OneOf.Types;
using Shouldly;
using PlayerDetailPage = Nova.UI.Features.Players.Pages.PlayerDetail;
using PlayersPage = Nova.UI.Features.Players.Pages.Players;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Component-level tests for player roster state handling, role matrix, and mutation UX.
/// </summary>
public sealed class PlayerComponentsTests : BunitContext
{
    /// <summary>Verifies an empty identity overtaking startup reaches the club-required state without loading another user's roster.</summary>
    [Fact]
    public async Task Players_AppliesEmptyIdentity_WhenItOvertakesStartup()
    {
        RegisterServices(isClubAdmin: true);
        var pending = new TaskCompletionSource<AuthenticationState>();
        var authentication = new DeferredAuthentication(pending.Task);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<PlayersPage>();

        await cut.InvokeAsync(() => authentication.Publish(Task.FromResult(new AuthenticationState(new ClaimsPrincipal()))));

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("You must join a club before viewing the player roster."));
        cut.Instance.Initialized.ShouldBeTrue();
        await cut.InvokeAsync(() => pending.SetResult(new AuthenticationState(CreatePrincipal(true))));
        cut.Markup.ShouldContain("You must join a club before viewing the player roster.");
        cut.Markup.ShouldNotContain("Avery Johnson");
        await Services.GetRequiredService<IPlayerService>().DidNotReceive().GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies role loss discards a checked archive confirmation and restoring access requires fresh consent.</summary>
    [Fact]
    public async Task Players_DiscardsArchiveConfirmation_WhenAdministratorRoleIsLost()
    {
        var lifecycle = Substitute.For<IPlayerLifecycleService>();
        RegisterServices(isClubAdmin: true, lifecycleService: lifecycle);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        cut.Find("button.btn-outline-warning").Click();
        cut.Find("#archive-confirm-checkbox").Change(true);

        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(false)));

        cut.WaitForAssertion(() => cut.FindAll("#archive-confirm-checkbox").ShouldBeEmpty());
        cut.Markup.ShouldNotContain("Archive Avery Johnson?");
        await lifecycle.DidNotReceive().ArchiveAsync(Arg.Any<long>(), Arg.Any<CancellationToken>());
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true)));
        cut.WaitForAssertion(() => cut.FindAll("#archive-confirm-checkbox").ShouldBeEmpty());
        cut.Find("button.btn-outline-warning").Click();
        cut.Find("#archive-confirm-checkbox").HasAttribute("checked").ShouldBeFalse();
        cut.Find("button.btn-warning").HasAttribute("disabled").ShouldBeTrue();
    }

    /// <summary>Verifies a new club immediately discards roster-derived filters, edit state, snapshots, and old URL context.</summary>
    [Fact]
    public async Task Players_ClearsPreviousClubState_BeforeNewRosterCompletes()
    {
        var pending = new TaskCompletionSource<ServiceResult<PagedResult<PlayerListItem>>>();
        var roster = Substitute.For<IPlayerService>();
        roster.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<GetPlayerRosterInput>().ClubId == 42
                ? Task.FromResult(SuccessRosterResult(CreateRosterItems())) : pending.Task);
        RegisterServices(isClubAdmin: true, rosterService: roster);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/players?returnToDraft=10&tag=11");
        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        cut.Find("button.btn-outline-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit player"));
        cut.Instance.PersistedPageError = "Previous club error";

        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true, clubId: 43)));

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Avery Johnson"));
        cut.Markup.ShouldNotContain("Edit player");
        cut.Markup.ShouldNotContain("Defender");
        cut.FindAll("#players-grad-year option[value='2032']").ShouldBeEmpty();
        cut.Instance.PersistedRoster.ShouldBeNull();
        cut.Instance.PersistedPageError.ShouldBeNull();
        navigation.Uri.ShouldBe("http://localhost/players");
        await roster.Received().GetPlayerRosterAsync(Arg.Is<GetPlayerRosterInput>(input => input.ClubId == 43), Arg.Any<CancellationToken>());
        await cut.InvokeAsync(() => pending.SetResult(SuccessRosterResult([])));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No players found"));
        cut.Instance.SnapshotScope.ShouldBe("101:43:True");
    }

    /// <summary>Verifies obsolete roster success and authorization failures cannot replace the new club or redirect it.</summary>
    /// <param name="forbidden">Whether the old request completes with an authorization failure.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Players_IgnoresPreviousClubRosterCompletion(bool forbidden)
    {
        var pending = new TaskCompletionSource<ServiceResult<PagedResult<PlayerListItem>>>();
        var roster = Substitute.For<IPlayerService>();
        roster.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<GetPlayerRosterInput>().ClubId == 42
                ? pending.Task : Task.FromResult(SuccessRosterResult([])));
        RegisterServices(isClubAdmin: true, rosterService: roster);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<PlayersPage>();

        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true, clubId: 43)));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No players found"));
        await cut.InvokeAsync(() => pending.SetResult(forbidden
            ? new ServiceResult<PagedResult<PlayerListItem>>(ServiceProblem.Forbidden("Previous club forbidden"))
            : SuccessRosterResult(CreateRosterItems())));

        cut.WaitForAssertion(() => cut.Instance.SnapshotScope.ShouldBe("101:43:True"));
        cut.Markup.ShouldNotContain("Avery Johnson");
        cut.Markup.ShouldNotContain("Previous club forbidden");
        cut.Instance.PersistedRoster.ShouldNotBeNull().Items.ShouldBeEmpty();
        Services.GetRequiredService<NavigationManager>().Uri.ShouldBe("http://localhost/players");
    }

    /// <summary>Verifies a completed old-club edit request cannot reopen the old player's form.</summary>
    [Fact]
    public async Task Players_IgnoresPreviousClubEditCompletion()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlayerDetailDto>>();
        var details = Substitute.For<IPlayerDetailService>();
        details.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        RegisterServices(isClubAdmin: true, detailService: details);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        var edit = cut.Find("button.btn-outline-primary").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true, clubId: 43)));
        await cut.InvokeAsync(() => pending.SetResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail())));
        await edit;

        cut.Markup.ShouldNotContain("Edit player");
        cut.FindAll("button[type='submit']").ShouldBeEmpty();
    }

    /// <summary>Verifies old-club archive completion cannot publish feedback or refresh the new club's roster.</summary>
    /// <param name="success">Whether the old mutation eventually succeeds.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Players_IgnoresPreviousClubArchiveCompletion(bool success)
    {
        var pending = new TaskCompletionSource<ServiceResult<Success>>();
        var lifecycle = Substitute.For<IPlayerLifecycleService>();
        lifecycle.ArchiveAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        RegisterServices(isClubAdmin: true, lifecycleService: lifecycle);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
        cut.Find("button.btn-outline-warning").Click();
        cut.Find("#archive-confirm-checkbox").Change(true);
        var archive = cut.Find("button.btn-warning").ClickAsync(new Microsoft.AspNetCore.Components.Web.MouseEventArgs());

        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true, clubId: 43)));
        await cut.InvokeAsync(() => pending.SetResult(success ? new ServiceResult<Success>(new Success())
            : new ServiceResult<Success>(ServiceProblem.ServerError("Old archive failure"))));
        await archive;

        cut.Markup.ShouldNotContain("Old archive failure");
        cut.Markup.ShouldNotContain("Player archived.");
        cut.FindAll("#archive-confirm-checkbox").ShouldBeEmpty();
        await Services.GetRequiredService<IPlayerService>().Received(1).GetPlayerRosterAsync(
            Arg.Is<GetPlayerRosterInput>(input => input.ClubId == 43), Arg.Any<CancellationToken>());
    }

    /// <summary>Verifies prerender snapshots are reused only for an exact authenticated scope.</summary>
    /// <param name="scope">The scope associated with the saved roster.</param>
    /// <param name="reuse">Whether the saved roster belongs to the current club and user.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, false)]
    [InlineData("101:42:True", false)]
    [InlineData("101:43:True", true)]
    public async Task Players_RestoresOnlyMatchingPrerenderSnapshot(string? scope, bool reuse)
    {
        var roster = Substitute.For<IPlayerService>();
        roster.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult([])));
        RegisterServices(isClubAdmin: true, rosterService: roster);
        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuthenticationStateProvider(CreatePrincipal(true, clubId: 43)));

        var cut = Render<SnapshotPlayers>(parameters => parameters.Add(component => component.RestoredScope, scope));

        if (reuse)
        {
            cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
            await roster.DidNotReceive().GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>());
        }
        else
        {
            cut.WaitForAssertion(() => cut.Markup.ShouldContain("No players found"));
            cut.Markup.ShouldNotContain("Avery Johnson");
            await roster.Received(1).GetPlayerRosterAsync(Arg.Is<GetPlayerRosterInput>(input => input.ClubId == 43), Arg.Any<CancellationToken>());
        }
        cut.Instance.SnapshotScope.ShouldBe("101:43:True");
    }

    /// <summary>Verifies an obsolete transport exception cannot replace the new club's successful roster.</summary>
    [Fact]
    public async Task Players_IgnoresPreviousClubTransportFailure()
    {
        var pending = new TaskCompletionSource<ServiceResult<PagedResult<PlayerListItem>>>();
        var roster = Substitute.For<IPlayerService>();
        roster.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<GetPlayerRosterInput>().ClubId == 42
                ? pending.Task : Task.FromResult(SuccessRosterResult([])));
        RegisterServices(isClubAdmin: true, rosterService: roster);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var cut = Render<PlayersPage>();
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true, clubId: 43)));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No players found"));

        await cut.InvokeAsync(() => pending.SetException(new HttpRequestException("Previous club transport failed")));

        cut.WaitForAssertion(() => cut.Instance.SnapshotScope.ShouldBe("101:43:True"));
        cut.Instance.PersistedPageError.ShouldBeNull();
        cut.Markup.ShouldNotContain("Previous club transport failed");
        cut.Markup.ShouldContain("No players found");
    }

    /// <summary>Verifies members cannot return to Drafts and role loss resets the URL together with roster filters.</summary>
    /// <param name="startsAsAdmin">Whether administrator access is initially granted before being revoked.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void Players_ReturnToDraft_RequiresCurrentAdministratorRole(bool startsAsAdmin)
    {
        RegisterServices(isClubAdmin: startsAsAdmin);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(startsAsAdmin));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/players?view=archived&search=Avery&graduationYear=2032&tag=11&returnToDraft=10");
        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        if (startsAsAdmin)
        {
            cut.FindAll("a").Single(link => link.TextContent.Trim() == "Return to draft")
                .GetAttribute("href").ShouldBe("/campaigns/10");
            authentication.Change(CreatePrincipal(false));
            cut.WaitForAssertion(() => navigation.Uri.ShouldBe("http://localhost/players"));
            cut.Find("#players-view-filter").GetAttribute("value").ShouldBe("active");
            var latestRequest = Services.GetRequiredService<IPlayerService>().ReceivedCalls()
                .Last(call => call.GetMethodInfo().Name == nameof(IPlayerService.GetPlayerRosterAsync))
                .GetArguments()[0].ShouldBeOfType<GetPlayerRosterInput>();
            latestRequest.LifecycleStatus.ShouldBe("active");
            latestRequest.Search.ShouldBe(string.Empty);
            latestRequest.GraduationYear.ShouldBeNull();
            latestRequest.PlayerTagId.ShouldBeNull();
        }

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Return to draft"));
    }

    [Fact]
    public void Players_ShowsLoadingState_WhileRosterRequestIsPending()
    {
        var pending = new TaskCompletionSource<ServiceResult<PagedResult<PlayerListItem>>>();
        var rosterService = Substitute.For<IPlayerService>();
        rosterService.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var cut = Render<PlayersPage>();
        cut.Markup.ShouldContain("Loading players...");

        pending.SetResult(SuccessRosterResult(CreateRosterItems()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
    }

    [Fact]
    public void Players_ShowsEmptyState_WhenRosterHasNoRows()
    {
        var rosterService = Substitute.For<IPlayerService>();
        rosterService.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult([])));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No players found"));
    }

    [Fact]
    public void Players_ShowsErrorAndRetries_WhenInitialLoadFails()
    {
        var rosterService = Substitute.For<IPlayerService>();
        rosterService.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PagedResult<PlayerListItem>>(ServiceProblem.ServerError("Transport failed."))),
                Task.FromResult(SuccessRosterResult(CreateRosterItems())));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Transport failed."));
        cut.Find("button.btn-outline-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
    }

    [Fact]
    public void Players_ShowsMutationControls_ForClubAdmin()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<PlayersPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading players...", StringComparison.Ordinal));

        cut.Markup.ShouldContain("Add player");
        cut.Markup.ShouldContain("Edit");
        cut.Markup.ShouldContain("Archive");
    }

    [Fact]
    public void Players_HidesMutationControls_ForEvaluator()
    {
        RegisterServices(isClubAdmin: false);

        var cut = Render<PlayersPage>();
        cut.WaitForState(() => !cut.Markup.Contains("Loading players...", StringComparison.Ordinal));

        cut.Markup.ShouldNotContain("Add player");
        cut.Markup.ShouldNotContain("btn-outline-primary");
        cut.Markup.ShouldNotContain("btn-outline-warning");
    }

    [Fact]
    public void Players_AppliesLifecycleGraduationAndTagFilters_WhenInputsChange()
    {
        var rosterService = Substitute.For<IPlayerService>();
        rosterService.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems())));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("#players-view-filter").Change("archived");
        cut.WaitForAssertion(() =>
            rosterService.Received().GetPlayerRosterAsync(
                Arg.Is<GetPlayerRosterInput>(input =>
                    input != null
                    && input.LifecycleStatus != null
                    && string.Equals(input.LifecycleStatus, "archived", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>()));

        cut.Find("#players-grad-year").Change("2032");
        cut.WaitForAssertion(() =>
            rosterService.Received().GetPlayerRosterAsync(
                Arg.Is<GetPlayerRosterInput>(input => input != null && input.GraduationYear == 2032),
                Arg.Any<CancellationToken>()));

        cut.Find("#players-tag-filter").Change("11");
        cut.WaitForAssertion(() =>
            rosterService.Received().GetPlayerRosterAsync(
                Arg.Is<GetPlayerRosterInput>(input => input != null && input.PlayerTagId == 11),
                Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Players_AppliesSearchFilter_AfterDebounce()
    {
        var rosterService = Substitute.For<IPlayerService>();
        rosterService.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems())));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("#players-search").Input("12");
        cut.WaitForAssertion(() =>
            rosterService.Received().GetPlayerRosterAsync(
                Arg.Is<GetPlayerRosterInput>(input =>
                    input != null
                    && input.Search != null
                    && string.Equals(input.Search, "12", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>()),
            timeout: TimeSpan.FromSeconds(2));
    }

    [Fact]
    public void Players_RequestsMaxPageSize_OnInitialRosterLoad()
    {
        var rosterService = Substitute.For<IPlayerService>();
        rosterService.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems())));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        rosterService.Received().GetPlayerRosterAsync(
            Arg.Is<GetPlayerRosterInput>(input =>
                input != null
                && input.Page == GetPlayerRosterInput.DefaultPage
                && input.PageSize == GetPlayerRosterInput.MaxPageSize),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Players_ShowsTruncationMessage_WhenRosterIsLargerThanLoadedItems()
    {
        var rosterService = Substitute.For<IPlayerService>();
        rosterService.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(
                CreateRosterItems(),
                totalCount: 120,
                pageSize: GetPlayerRosterInput.MaxPageSize)));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() =>
            cut.Markup.ShouldContain("Showing first 1 of 120 players. Refine filters to narrow the roster."));
    }

    [Fact]
    public void Players_ShowsCreateSuccessMessage_AfterMutationReload()
    {
        var rosterService = Substitute.For<IPlayerService>();
        rosterService.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems())));

        var managementService = Substitute.For<IPlayerManagementService>();
        managementService.CreateAsync(Arg.Any<CreatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDto>(new PlayerDto
            {
                PlayerId = 21,
                ClubId = 42,
                FirstName = "Taylor",
                LastName = "Lane",
                DateOfBirth = new DateOnly(2012, 5, 1),
                GraduationYear = 2031,
                LifecycleStatus = LifecycleStatus.Active
            })));

        RegisterServices(
            rosterService: rosterService,
            managementService: managementService,
            isClubAdmin: true);

        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("button.btn-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Add player"));
        cut.Find("#player-first-name").Change("Taylor");
        cut.Find("#player-last-name").Change("Lane");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Player created successfully."));
    }

    [Fact]
    public void Players_PreservesFilterContext_InPlayerDetailLink()
    {
        RegisterServices(isClubAdmin: true);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/players?view=archived&search=Avery&graduationYear=2032&tag=11");

        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        var detailLink = cut.Find("tbody a");
        detailLink.GetAttribute("href").ShouldBe(
            "/players/7?returnUrl=%2Fplayers%3Fview%3Darchived%26search%3DAvery%26graduationYear%3D2032%26tag%3D11");
    }

    [Fact]
    public void Players_ShowsGraduationYearConflictBlockers_WhenUpdateReturnsConflict()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail())));

        var managementService = Substitute.For<IPlayerManagementService>();
        managementService.UpdateAsync(Arg.Any<UpdatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDto>(
                ServiceProblem.Conflict(
                    "Update blocked.",
                    new Dictionary<string, string[]>
                    {
                        ["blockers[0].assignmentId"] = ["99"],
                        ["blockers[0].campaignId"] = ["400"],
                        ["blockers[0].teamId"] = ["501"],
                        ["blockers[0].teamGraduationYear"] = ["2034"]
                    }))));

        RegisterServices(isClubAdmin: true, detailService: detailService, managementService: managementService);

        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("button.btn-outline-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit player"));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Resolve active placements before lowering graduation year:");
            cut.Markup.ShouldContain("Campaign 400, Team 501 requires graduation year 2034.");
        });
    }

    [Fact]
    public void Players_ShowsArchiveBlockers_WhenArchiveReturnsConflict()
    {
        var lifecycleService = Substitute.For<IPlayerLifecycleService>();
        lifecycleService.ArchiveAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(
                ServiceProblem.Conflict(
                    "Archive blocked.",
                    PlayerLifecycleProblemExtensions.CreateArchiveBlockerExtensions(
                    [
                        new PlayerArchiveBlocker
                        {
                            CampaignId = 15,
                            CampaignName = "Summer Tryouts",
                            ParticipationIds = [44]
                        }
                    ])))));

        RegisterServices(isClubAdmin: true, lifecycleService: lifecycleService);

        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("button.btn-outline-warning").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Archive Avery Johnson?"));

        cut.Find("#archive-confirm-checkbox").Change(true);
        cut.Find("button.btn-warning").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Archive blockers:");
            cut.Markup.ShouldContain("Summer Tryouts (Campaign 15): participation IDs 44");
        });
    }

    [Fact]
    public void PlayerForm_ShowsValidationMessages_WhenSubmittedInvalid()
    {
        var model = new Nova.UI.Features.Players.Components.PlayerFormState
        {
            FirstName = "",
            LastName = "",
            DateOfBirth = new DateOnly(2012, 4, 1),
            GraduationYear = 2032
        };

        var cut = Render<Nova.UI.Features.Players.Components.PlayerForm>(parameters => parameters
            .Add(component => component.Heading, "Add player")
            .Add(component => component.Model, model)
            .Add(component => component.SubmitButtonText, "Create player"));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("The FirstName field is required.");
            cut.Markup.ShouldContain("The LastName field is required.");
        });
    }

    [Fact]
    public void PlayerDetail_UsesPlayersFallback_WhenReturnUrlIsExternal()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail())));
        RegisterServices(isClubAdmin: false, detailService: detailService);

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/players/7?returnUrl=https%3A%2F%2Fevil.example%2Fphish");

        var cut = Render<PlayerDetailPage>(parameters => parameters
            .Add(component => component.PlayerId, 7));

        cut.WaitForAssertion(() =>
            cut.Find("a.btn-outline-secondary").GetAttribute("href").ShouldBe("/players"));
    }

    [Fact]
    public void PlayerDetail_PreservesSafeRelativeReturnUrl()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail())));
        RegisterServices(isClubAdmin: false, detailService: detailService);

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/players/7?returnUrl=%2Fplayers%3Fview%3Darchived%26search%3DAvery");

        var cut = Render<PlayerDetailPage>(parameters => parameters
            .Add(component => component.PlayerId, 7));

        cut.WaitForAssertion(() =>
            cut.Find("a.btn-outline-secondary").GetAttribute("href").ShouldBe("/players?view=archived&search=Avery"));
    }

    [Fact]
    public void Players_UsesFallbackTagColor_WhenRosterTagColorIsInvalid()
    {
        var rosterService = Substitute.For<IPlayerService>();
        rosterService.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems(tagColor: "#0055AA; color: red;"))));

        RegisterServices(rosterService: rosterService, isClubAdmin: true);

        var cut = Render<PlayersPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find(".tag-pill").GetAttribute("style").ShouldBe("background-color: #6C757D; color: #FFFFFF;");
    }

    [Fact]
    public void PlayerDetail_UsesFallbackTagColor_WhenTraitColorIsInvalid()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(
                CreatePlayerDetail(currentTraits:
                [
                    new PlayerCurrentTraitDto(11, "Defender", "#0055AA; color: red;")
                ]))));
        RegisterServices(isClubAdmin: false, detailService: detailService);

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/players/7");

        var cut = Render<PlayerDetailPage>(parameters => parameters
            .Add(component => component.PlayerId, 7));

        cut.WaitForAssertion(() =>
            cut.Find("span.badge.rounded-pill").GetAttribute("style")
                .ShouldBe("background-color: #6C757D; color: #FFFFFF;"));
    }

    private void RegisterServices(
        bool isClubAdmin,
        IPlayerService? rosterService = null,
        IPlayerManagementService? managementService = null,
        IPlayerLifecycleService? lifecycleService = null,
        IPlayerDetailService? detailService = null)
    {
        if (rosterService is null)
        {
            rosterService = Substitute.For<IPlayerService>();
            rosterService.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(SuccessRosterResult(CreateRosterItems())));
        }

        managementService ??= Substitute.For<IPlayerManagementService>();
        lifecycleService ??= Substitute.For<IPlayerLifecycleService>();
        if (detailService is null)
        {
            detailService = Substitute.For<IPlayerDetailService>();
            detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail())));
        }

        Services.AddSingleton(rosterService);
        Services.AddSingleton(managementService);
        Services.AddSingleton(lifecycleService);
        Services.AddSingleton(detailService);
        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuthenticationStateProvider(CreatePrincipal(isClubAdmin)));
    }

    private static ServiceResult<PagedResult<PlayerListItem>> SuccessRosterResult(
        IReadOnlyList<PlayerListItem> items,
        int? totalCount = null,
        int page = 1,
        int pageSize = 20)
        => new(new PagedResult<PlayerListItem>(items, page, pageSize, totalCount ?? items.Count));

    private static List<PlayerListItem> CreateRosterItems(string tagColor = "#0055AA")
    {
        return
        [
            new PlayerListItem
            {
                PlayerId = 7,
                DisplayName = "Avery Johnson",
                GraduationYear = 2032,
                LifecycleStatus = LifecycleStatus.Active,
                CurrentTags = [new PlayerRosterTagItem(11, "Defender", tagColor)],
                ActiveCampaigns = ["Summer Tryouts"],
                JoinedAt = DateTimeOffset.UtcNow
            }
        ];
    }

    private static PlayerDetailDto CreatePlayerDetail(IReadOnlyList<PlayerCurrentTraitDto>? currentTraits = null)
        => new(
            7,
            "Avery",
            "Johnson",
            new DateOnly(2012, 4, 1),
            Gender.Female,
            2032,
            12,
            LifecycleStatus.Active,
            currentTraits ?? [],
            []);

    /// <summary>Builds an authenticated club principal for scope and authorization tests.</summary>
    /// <param name="isClubAdmin">Whether to grant club administrator authority.</param>
    /// <param name="clubId">The current club identifier.</param>
    /// <returns>The authenticated principal.</returns>
    private static ClaimsPrincipal CreatePrincipal(bool isClubAdmin, long clubId = 42)
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "101"),
            new(NovaClaimTypes.ClubId, clubId.ToString(System.Globalization.CultureInfo.InvariantCulture))
        };

        if (isClubAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, Roles.ClubAdmin));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>Restores a synthetic prerender snapshot before normal page initialization.</summary>
    /// <param name="roster">The roster query service.</param>
    /// <param name="management">The player management service.</param>
    /// <param name="lifecycle">The player lifecycle service.</param>
    /// <param name="details">The player detail service.</param>
    /// <param name="authentication">The current authentication provider.</param>
    /// <param name="navigation">The test navigation manager.</param>
    private sealed class SnapshotPlayers(IPlayerService roster, IPlayerManagementService management,
        IPlayerLifecycleService lifecycle, IPlayerDetailService details,
        AuthenticationStateProvider authentication, NavigationManager navigation)
        : PlayersPage(roster, management, lifecycle, details, authentication, navigation)
    {
        /// <summary>Gets or sets the scope serialized with the old roster.</summary>
        [Parameter] public string? RestoredScope { get; set; }

        /// <inheritdoc />
        protected override Task OnInitializedAsync()
        {
            Initialized = true;
            SnapshotScope = RestoredScope;
            PersistedRoster = new PagedResult<PlayerListItem>(CreateRosterItems(), 1, 50, 1);
            return base.OnInitializedAsync();
        }
    }

    /// <summary>Controls the startup identity independently of later notifications.</summary>
    /// <param name="initial">The startup identity task.</param>
    private sealed class DeferredAuthentication(Task<AuthenticationState> initial) : AuthenticationStateProvider
    {
        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => initial;

        /// <summary>Publishes a newer identity task.</summary>
        /// <param name="state">The state to publish.</param>
        public void Publish(Task<AuthenticationState> state) => NotifyAuthenticationStateChanged(state);
    }

    /// <summary>
    /// Provides a mutable authentication state for bUnit component tests.
    /// </summary>
    /// <param name="principal">The principal to return from <see cref="GetAuthenticationStateAsync"/>.</param>
    private sealed class FakeAuthenticationStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        /// <summary>The currently published authentication state.</summary>
        private Task<AuthenticationState> _state = Task.FromResult(new AuthenticationState(principal));

        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            _state;

        /// <summary>Publishes a changed principal to mounted components.</summary>
        /// <param name="newPrincipal">The replacement authenticated principal.</param>
        public void Change(ClaimsPrincipal newPrincipal)
            => NotifyAuthenticationStateChanged(_state = Task.FromResult(new AuthenticationState(newPrincipal)));
    }
}
