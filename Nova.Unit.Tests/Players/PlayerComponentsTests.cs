using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Nova.Shared.Security;
using OneOf.Types;
using PlayerDetailPage = Nova.UI.Features.Players.Pages.PlayerDetail;
using PlayersPage = Nova.UI.Features.Players.Pages.Players;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Component-level tests for player roster state handling, role matrix, and mutation UX.
/// </summary>
public sealed class PlayerComponentsTests : BunitContext
{
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
        Services.AddSingleton(detailService);

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
        Services.AddSingleton(detailService);

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

        cut.Find(".tag-pill").GetAttribute("style").ShouldBe("background-color: #6C757D; color: #ffffff;");
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
        Services.AddSingleton(detailService);

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/players/7");

        var cut = Render<PlayerDetailPage>(parameters => parameters
            .Add(component => component.PlayerId, 7));

        cut.WaitForAssertion(() =>
            cut.Find("span.badge.rounded-pill").GetAttribute("style")
                .ShouldBe("background-color: #6C757D; color: #ffffff;"));
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
        public override Task<AuthenticationState> GetAuthenticationStateAsync() =>
            Task.FromResult(new AuthenticationState(principal));
    }
}
