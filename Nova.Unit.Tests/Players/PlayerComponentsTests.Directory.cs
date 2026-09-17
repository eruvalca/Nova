using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;
using PlayersPage = Nova.UI.Features.Players.Pages.Players;

namespace Nova.Unit.Tests.Players;

public sealed partial class PlayerComponentsTests
{
    [Fact]
    public async Task ChangingActorInsideSameClubImmediatelyClearsPriorEvidenceAndContextAsync()
    {
        RegisterServices(isClubAdmin: true);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/players?search=Avery&tag=11&returnToDraft=8");
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        var roster = new TaskCompletionSource<ServiceResult<PagedResult<PlayerListItem>>>();
        var summary = new TaskCompletionSource<ServiceResult<PlayerDirectorySummary>>();
        var tags = new TaskCompletionSource<ServiceResult<IReadOnlyList<TagDefinitionDto>>>();
        Services.GetRequiredService<IPlayerService>().GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>()).Returns(roster.Task);
        Services.GetRequiredService<IPlayerService>().GetPlayerDirectorySummaryAsync(Arg.Any<GetPlayerDirectorySummaryInput>(), Arg.Any<CancellationToken>()).Returns(summary.Task);
        Services.GetRequiredService<ITagDefinitionQueryService>().GetChoicesAsync(Arg.Any<CancellationToken>()).Returns(tags.Task);
        var replacement = CreatePrincipal(false);
        var identity = (System.Security.Claims.ClaimsIdentity)replacement.Identity!;
        identity.RemoveClaim(identity.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier));
        identity.AddClaim(new(System.Security.Claims.ClaimTypes.NameIdentifier, "102"));
        await cut.InvokeAsync(() => authentication.Change(replacement));
        await cut.WaitForAssertionAsync(() => navigation.Uri.ShouldBe("http://localhost/players"));
        cut.Markup.ShouldNotContain("Avery Johnson");
        cut.Markup.ShouldNotContain("Defender");
        cut.Markup.ShouldNotContain("Return to draft");
        cut.Instance.PersistedRoster.ShouldBeNull();
        cut.Instance.PersistedSummary.ShouldBeNull();
        cut.Find("#players-search").GetAttribute("value").ShouldBe(string.Empty);
        await cut.InvokeAsync(() => roster.SetResult(SuccessRosterResult([])));
        await cut.InvokeAsync(() => summary.SetResult(new(new PlayerDirectorySummary { ActiveCount = 0, ArchivedCount = 0, GraduationYears = [] })));
        await cut.InvokeAsync(() => tags.SetResult(new(Array.Empty<TagDefinitionDto>())));
        await cut.WaitForAssertionAsync(() => cut.Instance.SnapshotScope.ShouldBe("102:42:False"));
    }

    [Fact]
    public async Task TagChoicesIncludeDefinitionsAbsentFromVisiblePlayersAsync()
    {
        RegisterServices(isClubAdmin: false);
        Services.GetRequiredService<ITagDefinitionQueryService>().GetChoicesAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(new[]
            {
                new TagDefinitionDto { PlayerTagId = 99, Name = "Later-page tag", Color = "#0055AA", LifecycleStatus = Nova.SharedKernel.Enums.LifecycleStatus.Active }
            })));
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Find("#players-tag-filter option[value='99']").TextContent.ShouldBe("Later-page tag"));
        cut.Find("tbody").TextContent.ShouldNotContain("Later-page tag");
        await cut.Find("#players-tag-filter").ChangeAsync(new() { Value = "99" });
        await Services.GetRequiredService<IPlayerService>().Received().GetPlayerRosterAsync(
            Arg.Is<GetPlayerRosterInput>(input => input.PlayerTagId == 99 && input.Page == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompletedDirectoryRegionsRenderWhileTagChoicesArePendingAsync()
    {
        RegisterServices(isClubAdmin: false);
        var pendingTags = new TaskCompletionSource<ServiceResult<IReadOnlyList<TagDefinitionDto>>>();
        var pendingRoster = new TaskCompletionSource<ServiceResult<PagedResult<PlayerListItem>>>();
        Services.GetRequiredService<ITagDefinitionQueryService>().GetChoicesAsync(Arg.Any<CancellationToken>()).Returns(pendingTags.Task);
        Services.GetRequiredService<IPlayerService>().GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(pendingRoster.Task);
        var cut = RenderPlayers();
        await cut.InvokeAsync(() => pendingRoster.SetResult(SuccessRosterResult(CreateRosterItems())));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        cut.Find("#players-grad-year option[value='2032']").TextContent.ShouldBe("2032");
        cut.Markup.ShouldContain("Loading tags…");
        await cut.InvokeAsync(() => pendingTags.SetResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(Array.Empty<TagDefinitionDto>())));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldNotContain("Loading tags…"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LateUpdateCannotReplaceANewerRoutedFormAsync(bool success)
    {
        RegisterServices(isClubAdmin: false);
        var pending = new TaskCompletionSource<ServiceResult<PlayerDto>>();
        var management = Services.GetRequiredService<IPlayerManagementService>();
        management.UpdateAsync(Arg.Any<UpdatePlayerInput>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var detail = CreatePlayerDetail() with { PlayerId = 8, FirstName = "Newer" };
        Services.GetRequiredService<IPlayerDetailService>().GetPlayerDetailAsync(8, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(detail)));
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/players/7/edit");
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Find("#player-first-name").GetAttribute("value").ShouldBe("Avery"));
        var save = cut.Find("button[type='submit']").ClickAsync(new());
        await management.Received(1).UpdateAsync(Arg.Is<UpdatePlayerInput>(input => input.PlayerId == 7), Arg.Any<CancellationToken>());
        await cut.InvokeAsync(() => navigation.NavigateTo("/players/8/edit"));
        await cut.WaitForAssertionAsync(() => cut.Find("#player-first-name").GetAttribute("value").ShouldBe("Newer"));
        await cut.InvokeAsync(() => pending.SetResult(success
            ? new ServiceResult<PlayerDto>(new PlayerDto
            {
                PlayerId = 7,
                ClubId = 42,
                FirstName = "Avery",
                LastName = "Johnson",
                DateOfBirth = new DateOnly(2012, 4, 1),
                GraduationYear = 2032,
                LifecycleStatus = Nova.SharedKernel.Enums.LifecycleStatus.Active
            })
            : new ServiceResult<PlayerDto>(ServiceProblem.ServerError("Old edit failed"))));
        await save;
        cut.Find("#player-first-name").GetAttribute("value").ShouldBe("Newer");
        cut.Markup.ShouldNotContain("Old edit failed");
        cut.Markup.ShouldNotContain("Player updated successfully");
        navigation.Uri.ShouldEndWith("/players/8/edit");
    }

    [Fact]
    public async Task SuccessfulEditRetryClearsObsoleteLoadErrorAsync()
    {
        RegisterServices(isClubAdmin: false);
        Services.GetRequiredService<IPlayerDetailService>().GetPlayerDetailAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(ServiceProblem.ServerError("Old load failed"))),
                Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail())));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/players/7/edit");
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Old load failed"));
        await cut.FindAll("button").Single(button => string.Equals(button.TextContent, "Retry player", StringComparison.Ordinal)).ClickAsync(new());
        cut.Find("#player-first-name").GetAttribute("value").ShouldBe("Avery");
        cut.Markup.ShouldNotContain("Old load failed");
    }

    [Fact]
    public async Task MatchingIdentitySnapshotCannotRestoreADifferentQueryAsync()
    {
        RegisterServices(isClubAdmin: true);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/players?search=Lee&page=2");
        var cut = Render<SnapshotPlayers>(parameters => parameters.Add(component => component.RestoredScope, "101:42:True"));
        await cut.WaitForAssertionAsync(() => cut.Instance.SnapshotQuery.ShouldBe("active|Lee|||2"));
        await Services.GetRequiredService<IPlayerService>().Received(1).GetPlayerRosterAsync(
            Arg.Is<GetPlayerRosterInput>(input => input.Page == 2 && string.Equals(input.Search, "Lee", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0, 0, "/players", "Your club has no players yet")]
    [InlineData(0, 4, "/players", "All players are archived")]
    [InlineData(4, 0, "/players?view=archived", "No archived players")]
    [InlineData(4, 0, "/players?search=missing", "No matching players")]
    [InlineData(0, 0, "/players?page=2147483647", "This page is unavailable")]
    public async Task DirectoryDistinguishesEmptyResultsAsync(int active, int archived, string destination, string heading)
    {
        ArgumentNullException.ThrowIfNull(destination);
        var roster = Substitute.For<IPlayerService>();
        roster.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult([])));
        RegisterServices(isClubAdmin: false, rosterService: roster);
        SetSummary(roster, active, archived);
        Services.GetRequiredService<NavigationManager>().NavigateTo(destination);
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Find("#players-empty-title").TextContent.ShouldBe(heading));
        if (destination.Contains("page=", StringComparison.Ordinal))
        {
            cut.Find(".players-empty a[href='/players']").TextContent.ShouldBe("Go to first page");
            cut.Markup.ShouldNotContain("Add a player to start");
        }
    }

    [Fact]
    public async Task DirectoryRetriesSummaryAndTagsIndependentlyWithoutInventingFirstUseAsync()
    {
        var roster = Substitute.For<IPlayerService>();
        roster.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessRosterResult([])));
        RegisterServices(isClubAdmin: false, rosterService: roster);
        roster.GetPlayerDirectorySummaryAsync(Arg.Any<GetPlayerDirectorySummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDirectorySummary>(ServiceProblem.ServerError("Summary failed"))),
                Task.FromResult(new ServiceResult<PlayerDirectorySummary>(new PlayerDirectorySummary
                {
                    ActiveCount = 120,
                    ArchivedCount = 7,
                    GraduationYears = [2027, 2040]
                })));
        var tags = Services.GetRequiredService<ITagDefinitionQueryService>();
        tags.GetChoicesAsync(Arg.Any<CancellationToken>()).Returns(
            Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(ServiceProblem.ServerError("Tags failed"))),
            Task.FromResult(new ServiceResult<IReadOnlyList<TagDefinitionDto>>(Array.Empty<TagDefinitionDto>())));
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Club totals unavailable"));
        cut.Markup.ShouldNotContain("Your club has no players yet");
        await cut.FindAll("button").Single(button => string.Equals(button.TextContent, "Retry club totals", StringComparison.Ordinal)).ClickAsync(new());
        cut.Find("#players-grad-year option[value='2040']").TextContent.ShouldBe("2040");
        cut.Markup.ShouldContain("Tags failed");
        await tags.Received(1).GetChoicesAsync(Arg.Any<CancellationToken>());
        await cut.FindAll("button").Single(button => string.Equals(button.TextContent, "Retry tag choices", StringComparison.Ordinal)).ClickAsync(new());
        cut.Markup.ShouldNotContain("Tags failed");
        await roster.Received(1).GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>());
        await roster.Received(2).GetPlayerDirectorySummaryAsync(Arg.Any<GetPlayerDirectorySummaryInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task MountedDirectoryAppliesChangedUrlAndRetainsSavedTagAsync()
    {
        RegisterServices(isClubAdmin: false);
        var navigation = Services.GetRequiredService<NavigationManager>();
        var roster = Services.GetRequiredService<IPlayerService>();
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.InvokeAsync(() => navigation.NavigateTo("/players?view=archived&page=3&tag=987&graduationYear=2031&search=Lee"));
        await cut.WaitForAssertionAsync(() => cut.Find("#players-tag-filter option[value='987']").TextContent.ShouldBe("Saved tag filter"));
        await roster.Received().GetPlayerRosterAsync(Arg.Is<GetPlayerRosterInput>(input =>
            input.Page == 3 && input.PageSize == 20 && input.PlayerTagId == 987 && input.GraduationYear == 2031
            && string.Equals(input.Search, "Lee", StringComparison.Ordinal)), Arg.Any<CancellationToken>());
        await cut.InvokeAsync(() => navigation.NavigateTo("/players?view=active&page=oops&graduationYear=bad&tag=-1"));
        await cut.WaitForAssertionAsync(() => cut.Find("#players-tag-filter").GetAttribute("value").ShouldBe(string.Empty));
        await roster.Received().GetPlayerRosterAsync(Arg.Is<GetPlayerRosterInput>(input =>
            input.Page == 1 && input.PlayerTagId == null && input.GraduationYear == null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OverlongUrlSearchShowsFeedbackWithoutDispatchingRosterRequestAsync()
    {
        RegisterServices(isClubAdmin: false);
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/players?search=" + new string('x', 201));
        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.Find("#players-search-error").TextContent.ShouldContain("200 characters"));
        await Services.GetRequiredService<IPlayerService>().DidNotReceive()
            .GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>());
        await cut.Find("#players-search").InputAsync(new() { Value = "Avery" });
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"), timeout: TimeSpan.FromSeconds(3));
        cut.FindAll("#players-search-error").ShouldBeEmpty();
    }

    [Fact]
    public async Task ObsoleteSummaryCannotPublishAfterClubChangeAsync()
    {
        RegisterServices(isClubAdmin: true);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var oldSummary = new TaskCompletionSource<ServiceResult<PlayerDirectorySummary>>();
        var roster = Services.GetRequiredService<IPlayerService>();
        roster.GetPlayerDirectorySummaryAsync(Arg.Any<GetPlayerDirectorySummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<GetPlayerDirectorySummaryInput>().ClubId == 42 ? oldSummary.Task
                : Task.FromResult(new ServiceResult<PlayerDirectorySummary>(new PlayerDirectorySummary
                {
                    ActiveCount = 9,
                    ArchivedCount = 2,
                    GraduationYears = [2044]
                })));
        var cut = RenderPlayers();
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true, 43)));
        await cut.WaitForAssertionAsync(() => cut.Find("#players-grad-year option[value='2044']").ShouldNotBeNull());
        await cut.InvokeAsync(() => oldSummary.SetResult(new ServiceResult<PlayerDirectorySummary>(new PlayerDirectorySummary
        {
            ActiveCount = 999,
            ArchivedCount = 999,
            GraduationYears = [2001]
        })));
        cut.FindAll("#players-grad-year option[value='2001']").ShouldBeEmpty();
        cut.Instance.PersistedSummary.ShouldNotBeNull().ActiveCount.ShouldBe(9);
    }

    private static void SetSummary(IPlayerService roster, int active, int archived)
        => roster.GetPlayerDirectorySummaryAsync(Arg.Any<GetPlayerDirectorySummaryInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDirectorySummary>(new PlayerDirectorySummary
            {
                ActiveCount = active,
                ArchivedCount = archived,
                GraduationYears = []
            })));
}
