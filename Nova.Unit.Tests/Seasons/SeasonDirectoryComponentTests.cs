using System.Globalization;
using System.Reflection;
using System.Security.Claims;
using Bunit;
using Bunit.Rendering;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Nova.SharedKernel.Features.Clubs;
using Nova.SharedKernel.Features.Seasons;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Nova.UI.Features.Seasons.Pages;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Seasons;

/// <summary>
/// Component-level tests for the seasons directory: current-season identity, bounded paging,
/// first-season and recovery states, role-shaped affordances, per-region recovery, and stale results.
/// </summary>
public sealed class SeasonDirectoryComponentTests : BunitContext
{
    private const string RoutePath = "/club/seasons";

    /// <summary>Configures the shell's browser-only focus restoration while component tests exercise its content.</summary>
    public SeasonDirectoryComponentTests()
    {
        JSInterop.SetupModule("./_content/Nova.UI/Features/Clubs/Components/ClubShell.razor.js")
            .Setup<bool>("restoreHeadingFocusAfterAttach", _ => true).SetResult(true);
    }

    [Fact]
    public void RouteDeclaresInteractiveAutoAndKeepsLogicInCodeBehind()
    {
        var root = FindRepoRoot();
        var razorPath = Path.Join(root, "Nova.UI", "Features", "Seasons", "Pages", "SeasonDirectory.razor");
        var markup = File.ReadAllText(razorPath);

        markup.ShouldContain("@page \"/club/seasons\"");
        markup.ShouldContain("@attribute [Authorize(Policy = Policies.RequireClubMember)]");
        markup.ShouldNotContain("@code");
        markup.ShouldNotContain("@inject");
        File.Exists($"{razorPath}.cs").ShouldBeTrue();
        AssertInteractiveAutoRenderMode<SeasonDirectory>();
    }

    /// <summary>Verifies both reserved Seasons destinations keep the interactive shell boundary.</summary>
    /// <param name="componentType">The reserved page component type.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(typeof(SeasonDetailReserved))]
    [InlineData(typeof(StartNextSeasonReserved))]
    public void ReservedSeasonPagesDeclareInteractiveAutoRenderMode(Type componentType)
    {
        ArgumentNullException.ThrowIfNull(componentType);
        AssertInteractiveAutoRenderMode(componentType);
    }

    /// <summary>
    /// Asserts the compiler-generated render-mode attribute, so a commented-out directive cannot satisfy
    /// this required interaction boundary the way a source-text check could.
    /// </summary>
    /// <typeparam name="TComponent">The page component type.</typeparam>
    private static void AssertInteractiveAutoRenderMode<TComponent>()
        => AssertInteractiveAutoRenderMode(typeof(TComponent));

    /// <summary>Asserts the compiler-generated render-mode attribute for a component type.</summary>
    /// <param name="componentType">The component type to inspect.</param>
    private static void AssertInteractiveAutoRenderMode(Type componentType)
    {
        var attribute = componentType
            .GetCustomAttributes(inherit: false)
            .OfType<RenderModeAttribute>()
            .SingleOrDefault();

        attribute.ShouldNotBeNull();
        attribute.Mode.ShouldBeOfType<InteractiveAutoRenderMode>();
    }

    /// <summary>Verifies the current season leads with explicit identity and role-shaped advancement entry points.</summary>
    /// <param name="isClubAdmin">Whether the current member has administrator permissions.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void RenderLeadsWithTheCurrentSeasonAndShapesAdvancementByRole(bool isClubAdmin)
    {
        Register(isClubAdmin: isClubAdmin);

        var cut = RenderDirectory();

        cut.Find("#current-season-heading").TextContent.ShouldBe("Current season");
        cut.Find(".season-stop-current .season-status").TextContent.ShouldBe("Current");
        cut.Find(".season-stop-current .season-stop-name").TextContent.Trim().ShouldBe("2026–27");
        cut.Markup.ShouldContain(FormatWindow(new DateOnly(2026, 9, 1), new DateOnly(2027, 5, 31)));
        // Every approved member reaches a season record; only administrators see advancement.
        cut.Find(".season-stop-current .season-stop-name a").GetAttribute("href")
            .ShouldBe(ClubRoutes.SeasonDetail(2));
        cut.Find(".season-stops a").GetAttribute("href").ShouldBe(ClubRoutes.SeasonDetail(1));
        if (isClubAdmin)
        {
            cut.Markup.ShouldContain($"href=\"{ClubRoutes.StartNextSeason}\"");
        }
        else
        {
            cut.Markup.ShouldNotContain(ClubRoutes.StartNextSeason);
        }
    }

    [Fact]
    public void RenderShowsTheCurrentSeasonOnceSoItNeverRepeatsInHistory()
    {
        Register(history: HistoryPage(1, 2, CurrentSeasonSummary(), PastSeasonSummary(1, "2025–26", 2025)));

        var cut = RenderDirectory();

        cut.FindAll(".season-stop-current").Count.ShouldBe(1);
        cut.FindAll(".season-stops .season-stop").Count.ShouldBe(1);
        cut.Markup.ShouldContain("2025–26");
        CountOccurrences(cut.Markup, "2026–27").ShouldBe(1);
        cut.Markup.ShouldContain("The current season is shown above.");
    }

    /// <summary>Verifies the first-season state is stated rather than inferred from an empty list.</summary>
    /// <param name="isClubAdmin">Whether the current member has administrator permissions.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void RenderStatesTheFirstSeasonStateWhenNoSeasonIsRecorded(bool isClubAdmin)
    {
        Register(
            isClubAdmin: isClubAdmin,
            current: Read([], 0),
            history: HistoryPage(1, 0));

        var cut = RenderDirectory();

        cut.Markup.ShouldContain("No season has been established yet");
        cut.Markup.ShouldContain(isClubAdmin
            ? "Establish the club's first season, including when you create its first campaign."
            : "A club administrator establishes the club's first season.");
        cut.Markup.ShouldContain("No past seasons are recorded yet");
        cut.Markup.ShouldNotContain("No current season");
    }

    [Fact]
    public void RenderStatesTheRecoveryStateWhenRecordedSeasonsHaveNoCurrentOne()
    {
        Register(
            isClubAdmin: true,
            current: Read([PastSeasonSummary(1, "2025–26", 2025)], 3),
            history: HistoryPage(1, 3, PastSeasonSummary(1, "2025–26", 2025), PastSeasonSummary(2, "2024–25", 2024)));

        var cut = RenderDirectory();

        cut.Markup.ShouldContain("No current season");
        cut.Markup.ShouldContain("3 recorded seasons");
        cut.Markup.ShouldContain("Start next season establishes one.");
        cut.FindAll(".season-stops .season-stop").Count.ShouldBe(2);
    }

    [Fact]
    public void RenderPagesHistoryFromTheUrlAndKeepsTheCurrentSeasonOffEveryPage()
    {
        Register(history: HistoryPage(2, 45, PastSeasonSummary(11, "2016–17", 2016), PastSeasonSummary(12, "2015–16", 2015)));
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"{RoutePath}?page=2");

        var cut = RenderDirectory();

        cut.FindAll(".season-stops .season-stop").Count.ShouldBe(2);
        cut.Markup.ShouldContain("Page 2 of 3");
        cut.Markup.ShouldContain("45 recorded seasons");
        // Page one is canonical, so the previous link carries no query string.
        var pagerLinks = cut.FindAll(".season-pager a");
        pagerLinks.Count.ShouldBe(2);
        pagerLinks[0].TextContent.Trim().ShouldBe("Previous page");
        pagerLinks[0].GetAttribute("href").ShouldBe(RoutePath);
        pagerLinks[1].TextContent.Trim().ShouldBe("Next page");
        pagerLinks[1].GetAttribute("href").ShouldBe($"{RoutePath}?page=3");
    }

    [Fact]
    public void RenderOffersRecoveryToTheFirstPageWhenTheRequestedPageIsBeyondHistory()
    {
        Register(history: HistoryPage(9, 45));
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"{RoutePath}?page=9");

        var cut = RenderDirectory();

        cut.Markup.ShouldContain("No seasons are recorded on page 9.");
        cut.Find(".season-history-recover").GetAttribute("href").ShouldBe(RoutePath);
        // A "Page 9 of 3" pager must not accompany the recovery message.
        cut.FindAll(".season-pager").Count.ShouldBe(0);
    }

    /// <summary>Verifies one failing region leaves the other region's loaded content intact.</summary>
    /// <param name="currentFails">Whether the current-season read fails.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(true)]
    [InlineData(false)]
    public void RenderPreservesTheLoadedRegionWhenTheOtherFails(bool currentFails)
    {
        Register(
            current: currentFails ? Failed("Current season unavailable.") : CurrentSeasonPage(),
            history: currentFails
                ? HistoryPage(1, 2, CurrentSeasonSummary(), PastSeasonSummary(1, "2025–26", 2025))
                : Failed("Season history unavailable."));

        var cut = RenderDirectory();

        cut.FindAll(".region-failure").Count.ShouldBe(1);
        if (currentFails)
        {
            cut.Markup.ShouldContain("Current season unavailable.");
            cut.Markup.ShouldContain("2025–26");
        }
        else
        {
            cut.Markup.ShouldContain("Season history unavailable.");
            cut.Markup.ShouldContain("2026–27");
        }
    }

    [Fact]
    public void RetryCurrentReloadsOnlyTheCurrentRegion()
    {
        var currentReads = 0;
        var seasons = Substitute.For<ISeasonQueryService>();
        seasons.ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.Arg<GetSeasonListInput>().PageSize != 1)
                {
                    return Task.FromResult(HistoryPage(
                        1, 2, CurrentSeasonSummary(), PastSeasonSummary(1, "2025–26", 2025)));
                }

                return Interlocked.Increment(ref currentReads) == 1
                    ? Task.FromResult(Failed("Current season unavailable."))
                    : Task.FromResult(CurrentSeasonPage());
            });
        Register(seasons: seasons);

        var cut = RenderDirectory();
        cut.Markup.ShouldContain("Current season unavailable.");

        cut.Find(".season-stop-current .region-failure a").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Current season unavailable."));
        cut.Markup.ShouldContain("2026–27");
        cut.Markup.ShouldContain("2025–26");
        _ = seasons.Received(1).ListAsync(
            Arg.Is<GetSeasonListInput>(input => input.PageSize != 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RetryHistoryReloadsOnlyTheHistoryRegion()
    {
        var historyReads = 0;
        var seasons = Substitute.For<ISeasonQueryService>();
        seasons.ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.Arg<GetSeasonListInput>().PageSize == 1)
                {
                    return Task.FromResult(CurrentSeasonRead());
                }

                return Interlocked.Increment(ref historyReads) == 1
                    ? Task.FromResult(Failed("Season history unavailable."))
                    : Task.FromResult(HistoryPage(1, 2, CurrentSeasonSummary(), PastSeasonSummary(1, "2025–26", 2025)));
            });
        Register(seasons: seasons);

        var cut = RenderDirectory();
        cut.Markup.ShouldContain("Season history unavailable.");

        cut.Find(".season-history .region-failure a").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Season history unavailable."));
        cut.Markup.ShouldContain("2025–26");
        cut.Markup.ShouldContain("2026–27");
        // The history retry must not repeat the current-season read.
        _ = seasons.Received(1).ListAsync(
            Arg.Is<GetSeasonListInput>(input => input.PageSize == 1), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void RenderKeepsNonInteractiveRetriesOnTheDirectory()
    {
        Register(current: Failed("Current season unavailable."));

        var cut = RenderDirectory();

        // With no interactive circuit the fallback would navigate away; it must re-enter the directory.
        cut.Find(".season-stop-current .region-failure a").GetAttribute("href").ShouldBe(RoutePath);
    }

    [Fact]
    public void RenderKeepsTheRequestedPageInTheHistoryRetryFallback()
    {
        Register(history: Failed("Season history unavailable."));
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"{RoutePath}?page=2");

        var cut = RenderDirectory();

        cut.Find(".season-history .region-failure a").GetAttribute("href").ShouldBe($"{RoutePath}?page=2");
    }

    [Fact]
    public void RenderDoesNotClaimTheCurrentSeasonIsShownWhenItsRegionFailed()
    {
        // The history page still carries the current row, but the current region failed and shows its error.
        Register(
            current: Failed("Current season unavailable."),
            history: HistoryPage(1, 2, CurrentSeasonSummary(), PastSeasonSummary(1, "2025–26", 2025)));

        var cut = RenderDirectory();

        cut.Markup.ShouldNotContain("The current season is shown above.");
        cut.Markup.ShouldContain("Recorded seasons, newest first.");
        cut.Markup.ShouldContain("Current season unavailable.");
        cut.Markup.ShouldNotContain("2026–27");
    }

    [Fact]
    public async Task RenderDiscardsStaleResultsWhenTheClubChangesAsync()
    {
        var previousClubRead = new TaskCompletionSource<ServiceResult<SeasonPageResult>>();
        var currentReads = 0;
        var seasons = Substitute.For<ISeasonQueryService>();
        seasons.ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.Arg<GetSeasonListInput>().PageSize != 1)
                {
                    return Task.FromResult(HistoryPage(1, 1, CurrentSeasonSummary()));
                }

                return Interlocked.Increment(ref currentReads) == 1
                    ? previousClubRead.Task
                    : Task.FromResult(CurrentSeasonPage());
            });
        var auth = new TestAuthenticationStateProvider(MemberPrincipal(clubId: "1"));
        Register(seasons: seasons, auth: auth);

        var cut = RenderDirectory();

        auth.Change(MemberPrincipal(clubId: "2"));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("2026–27"));

        // The superseded club's slower response must not repopulate the directory.
        previousClubRead.SetResult(Read(
            [PastSeasonSummary(99, "SUPERSEDED CLUB SEASON", 2001) with { IsCurrent = true }], 1));
        await Task.Yield();

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldNotContain("SUPERSEDED CLUB SEASON"));
        cut.Markup.ShouldContain("2026–27");
    }

    [Fact]
    public void RenderDoesNotRefetchARestoredHistoryFailure()
    {
        var seasons = Substitute.For<ISeasonQueryService>();
        Register(seasons: seasons);

        var cut = Render<PersistedHistoryFailureDirectory>(parameters => parameters
            .Add(component => component.StartInitialized, true)
            .Add(component => component.StartPage, 1)
            .Add(component => component.StartHistoryError, "Season history is unavailable."));

        cut.Markup.ShouldContain("Season history is unavailable.");
        cut.Markup.ShouldContain("2026–27");
        _ = seasons.DidNotReceive().ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RenderRemovesTheAdvancementEntryBeforeReplacementReadsCompleteAsync()
    {
        var replacementRead = new TaskCompletionSource<ServiceResult<SeasonPageResult>>();
        var currentReads = 0;
        var seasons = Substitute.For<ISeasonQueryService>();
        seasons.ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.Arg<GetSeasonListInput>().PageSize != 1)
                {
                    return Task.FromResult(HistoryPage(1, 1, CurrentSeasonSummary()));
                }

                return Interlocked.Increment(ref currentReads) == 1
                    ? Task.FromResult(CurrentSeasonRead())
                    : replacementRead.Task;
            });
        var auth = new TestAuthenticationStateProvider(AdministratorPrincipal());
        Register(seasons: seasons, auth: auth);

        var cut = RenderDirectory();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain($"href=\"{ClubRoutes.StartNextSeason}\""));

        // Revoking authority must withdraw the entry point while the replacement reads are outstanding.
        auth.Change(MemberPrincipal());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldNotContain(ClubRoutes.StartNextSeason));
        cut.Markup.ShouldContain("Seasons");

        replacementRead.SetResult(CurrentSeasonRead());
    }

    [Fact]
    public async Task RenderClearsThePreviousClubRowsAndRoutesWhenMembershipDisappearsAsync()
    {
        var reads = 0;
        var seasons = Substitute.For<ISeasonQueryService>();
        seasons.ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>())
            .Returns(_ => Interlocked.Increment(ref reads) <= 2
                ? Task.FromResult(HistoryPage(1, 2, CurrentSeasonSummary(), PastSeasonSummary(1, "2025–26", 2025)))
                : Task.FromResult(new ServiceResult<SeasonPageResult>(
                    ServiceProblem.Forbidden("A current club membership is required."))));
        var auth = new TestAuthenticationStateProvider(MemberPrincipal(clubId: "1"));
        Register(seasons: seasons, auth: auth);

        var cut = RenderDirectory();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("2026–27"));

        auth.Change(MemberPrincipal(clubId: null));

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        await cut.WaitForAssertionAsync(() => navigationManager.Uri.ShouldEndWith("/Account/AccessDenied"));
        cut.Markup.ShouldNotContain("2025–26");
        cut.Markup.ShouldNotContain("2026–27");
    }

    [Fact]
    public void RenderRoutesForbiddenSeasonReadsToAccessDenied()
    {
        Register(current: new ServiceResult<SeasonPageResult>(
            ServiceProblem.Forbidden("A current club membership is required.")));

        _ = RenderDirectory();

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.Uri.ShouldEndWith("/Account/AccessDenied");
    }

    [Fact]
    public void RenderCanonicalizesAMalformedSeasonPageInTheUrl()
    {
        Register();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"{RoutePath}?page=not-a-page");

        var cut = RenderDirectory();

        cut.WaitForAssertion(() => new Uri(navigationManager.Uri).PathAndQuery.ShouldBe(RoutePath));
        cut.Markup.ShouldContain("2026–27");
    }

    [Fact]
    public void RenderKeepsTheFirstSeasonPageFreeOfAQueryString()
    {
        Register();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"{RoutePath}?page=1");

        var cut = RenderDirectory();

        cut.WaitForAssertion(() => new Uri(navigationManager.Uri).PathAndQuery.ShouldBe(RoutePath));
    }

    [Fact]
    public async Task RenderKeepsTheNewerPageWhenABatchHistoryResponseArrivesLateAsync()
    {
        var batchHistory = new TaskCompletionSource<ServiceResult<SeasonPageResult>>();
        var historyReads = 0;
        var seasons = Substitute.For<ISeasonQueryService>();
        seasons.ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.Arg<GetSeasonListInput>().PageSize == 1)
                {
                    return Task.FromResult(CurrentSeasonRead());
                }

                return Interlocked.Increment(ref historyReads) switch
                {
                    1 => Task.FromResult(HistoryPage(1, 45, PastSeasonSummary(9, "STARTUP SEASON", 2018))),
                    2 => batchHistory.Task,
                    _ => Task.FromResult(HistoryPage(2, 45, PastSeasonSummary(11, "PAGE TWO SEASON", 2016)))
                };
            });
        var auth = new TestAuthenticationStateProvider(MemberPrincipal(clubId: "1"));
        Register(seasons: seasons, auth: auth);

        var cut = Render<SeasonDirectory>();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("STARTUP SEASON"));

        // A club change starts a batch whose history read stays outstanding.
        auth.Change(MemberPrincipal(clubId: "2"));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldNotContain("STARTUP SEASON"));

        // A URL page change must supersede that still-pending batch read.
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"{RoutePath}?page=2");
        cut.Render();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("PAGE TWO SEASON"));

        // The superseded batch response must not replace the newer page's rows.
        batchHistory.SetResult(HistoryPage(1, 45, PastSeasonSummary(7, "STALE PAGE ONE SEASON", 2010)));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Page 2 of 3"));
        cut.Markup.ShouldContain("PAGE TWO SEASON");
        cut.Markup.ShouldNotContain("STALE PAGE ONE SEASON");
    }

    [Fact]
    public async Task RenderReconcilesMismatchedCurrentIdentityBetweenRegionsAsync()
    {
        var currentRead = new TaskCompletionSource<ServiceResult<SeasonPageResult>>();
        var seasons = Substitute.For<ISeasonQueryService>();
        seasons.ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>())
            .Returns(call => call.Arg<GetSeasonListInput>().PageSize == 1
                ? currentRead.Task
                : Task.FromResult(HistoryPage(1, 2,
                    SeasonFor(3, "NEXT CLUB SEASON", 2027, isCurrent: true),
                    SeasonFor(2, "PRIOR CLUB SEASON", 2026, isCurrent: false))));
        Register(seasons: seasons);

        var cut = RenderDirectory();

        // The current region observed an older snapshot than the history region.
        currentRead.SetResult(Read([SeasonFor(2, "PRIOR CLUB SEASON", 2026, isCurrent: true)], 2));

        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("PRIOR CLUB SEASON"));
        // The season shown as current must not also appear as a past row.
        CountOccurrences(cut.Markup, "PRIOR CLUB SEASON").ShouldBe(1);
        cut.Markup.ShouldNotContain("NEXT CLUB SEASON");
    }

    [Fact]
    public async Task RenderKeepsTheNewerPageWhenAStartupHistoryResponseArrivesLateAsync()
    {
        var startupHistory = new TaskCompletionSource<ServiceResult<SeasonPageResult>>();
        var historyReads = 0;
        var seasons = Substitute.For<ISeasonQueryService>();
        seasons.ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                if (call.Arg<GetSeasonListInput>().PageSize == 1)
                {
                    return Task.FromResult(CurrentSeasonRead());
                }

                return Interlocked.Increment(ref historyReads) switch
                {
                    1 => startupHistory.Task,
                    _ => Task.FromResult(HistoryPage(2, 45, PastSeasonSummary(11, "PAGE TWO SEASON", 2016)))
                };
            });
        Register(seasons: seasons);

        var cut = Render<SeasonDirectory>();

        // The requested page changes while the startup load's history read is still outstanding.
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo($"{RoutePath}?page=2");
        cut.Render();
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("PAGE TWO SEASON"));

        // The superseded startup response must not publish page-one rows under the page-two URL.
        startupHistory.SetResult(HistoryPage(1, 45, PastSeasonSummary(9, "STARTUP PAGE ONE SEASON", 2018)));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Page 2 of 3"));
        cut.Markup.ShouldContain("PAGE TWO SEASON");
        cut.Markup.ShouldNotContain("STARTUP PAGE ONE SEASON");
    }

    private static int CountOccurrences(string text, string value)
    {
        var count = 0;
        var index = text.IndexOf(value, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = text.IndexOf(value, index + value.Length, StringComparison.Ordinal);
        }

        return count;
    }

    private static string FormatWindow(DateOnly start, DateOnly? end)
        => end is null
            ? $"{start.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} onward"
            : $"{start.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)} – {end.Value.ToString("MMM d, yyyy", CultureInfo.CurrentCulture)}";

    private IRenderedComponent<ContainerFragment> RenderDirectory()
        => Render(builder =>
        {
            builder.OpenComponent<CascadingAuthenticationState>(0);
            builder.AddAttribute(1, "ChildContent", (RenderFragment)(child =>
            {
                child.OpenComponent<SeasonDirectory>(0);
                child.CloseComponent();
            }));
            builder.CloseComponent();
        });

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Join(directory.FullName, "Nova.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new DirectoryNotFoundException();
    }

    private void Register(
        bool isClubAdmin = false,
        ServiceResult<SeasonPageResult>? current = null,
        ServiceResult<SeasonPageResult>? history = null,
        ISeasonQueryService? seasons = null,
        AuthenticationStateProvider? auth = null)
    {
        var service = seasons ?? Substitute.For<ISeasonQueryService>();
        if (seasons is null)
        {
            var currentResult = current ?? CurrentSeasonRead();
            var historyResult = history ?? HistoryPage(1, 2, CurrentSeasonSummary(), PastSeasonSummary(1, "2025–26", 2025));
            service.ListAsync(Arg.Any<GetSeasonListInput>(), Arg.Any<CancellationToken>())
                .Returns(call => Task.FromResult(
                    call.Arg<GetSeasonListInput>().PageSize == 1 ? currentResult : historyResult));
        }

        Services.AddSingleton(service);
        Services.AddSingleton(auth ?? new TestAuthenticationStateProvider(
            isClubAdmin ? AdministratorPrincipal() : MemberPrincipal()));
        Services.AddSingleton<IAuthorizationPolicyProvider>(
            new DefaultAuthorizationPolicyProvider(Options.Create(new AuthorizationOptions())));
        Services.AddSingleton<IAuthorizationService>(new RoleAuthorizationService());
    }

    private static ServiceResult<SeasonPageResult> CurrentSeasonRead()
        => Read([CurrentSeasonSummary()], 1);

    private static ServiceResult<SeasonPageResult> Read(IReadOnlyList<SeasonSummary> items, int totalCount)
        => new(new SeasonPageResult { Items = items, Page = 1, PageSize = 1, TotalCount = totalCount });

    private static ServiceResult<SeasonPageResult> HistoryPage(int page, int totalCount, params SeasonSummary[] items)
        => new(new SeasonPageResult
        {
            Items = items,
            Page = page,
            PageSize = GetSeasonListInput.DefaultPageSize,
            TotalCount = totalCount
        });

    private static ServiceResult<SeasonPageResult> CurrentSeasonPage()
        => HistoryPage(1, 1, CurrentSeasonSummary());

    private static ServiceResult<SeasonPageResult> Failed(string detail)
        => new(ServiceProblem.ServerError(detail));

    private static SeasonSummary CurrentSeasonSummary() => new()
    {
        SeasonId = 2,
        Name = "2026–27",
        StartDate = new DateOnly(2026, 9, 1),
        EndDate = new DateOnly(2027, 5, 31),
        IsCurrent = true,
        ConcurrencyToken = Guid.NewGuid()
    };

    private static SeasonSummary PastSeasonSummary(long seasonId, string name, int startYear) => new()
    {
        SeasonId = seasonId,
        Name = name,
        StartDate = new DateOnly(startYear, 9, 1),
        EndDate = new DateOnly(startYear + 1, 5, 31),
        IsCurrent = false,
        ConcurrencyToken = Guid.NewGuid()
    };

    private static SeasonSummary SeasonFor(long seasonId, string name, int startYear, bool isCurrent) => new()
    {
        SeasonId = seasonId,
        Name = name,
        StartDate = new DateOnly(startYear, 9, 1),
        EndDate = new DateOnly(startYear + 1, 5, 31),
        IsCurrent = isCurrent,
        ConcurrencyToken = Guid.NewGuid()
    };

    private static ClaimsPrincipal MemberPrincipal(string? clubId = "1")
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, "10") };
        if (clubId is not null)
        {
            claims.Add(new Claim(NovaClaimTypes.ClubId, clubId));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    private static ClaimsPrincipal AdministratorPrincipal()
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, "10"),
            new(NovaClaimTypes.ClubId, "1"),
            new(ClaimTypes.Role, Roles.ClubAdmin)
        };
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

#pragma warning disable CA1812 // The test framework constructs this type through bUnit rendering, DI, or reflection.
    private sealed class PersistedHistoryFailureDirectory(
#pragma warning restore CA1812
        ISeasonQueryService seasonQueryService,
        AuthenticationStateProvider authenticationStateProvider,
        NavigationManager navigationManager)
        : SeasonDirectory(seasonQueryService, authenticationStateProvider, navigationManager)
    {
        /// <summary>Gets or sets whether the restored snapshot represents a completed first load.</summary>
        [Parameter] public bool StartInitialized { get; set; }

        /// <summary>Gets or sets the season page the persisted snapshot describes.</summary>
        [Parameter] public int StartPage { get; set; }

        /// <summary>Gets or sets the persisted history error.</summary>
        [Parameter] public string? StartHistoryError { get; set; }

        /// <inheritdoc />
        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                PersistedIdentityScope = "10:1:False";
                PersistedPage = StartPage;
                PersistedHistoryError = StartHistoryError;
                PersistedCurrentSeason = CurrentSeasonSummary();
                PersistedCurrentSeasonCount = 1;
            }

            return base.OnInitializedAsync();
        }
    }

    private sealed class TestAuthenticationStateProvider(ClaimsPrincipal initialPrincipal)
        : AuthenticationStateProvider
    {
        private Task<AuthenticationState> _state = Task.FromResult(new AuthenticationState(initialPrincipal));

        public override Task<AuthenticationState> GetAuthenticationStateAsync() => _state;

        public void Change(ClaimsPrincipal principal)
        {
            _state = Task.FromResult(new AuthenticationState(principal));
            NotifyAuthenticationStateChanged(_state);
        }
    }

    private sealed class RoleAuthorizationService : IAuthorizationService
    {
        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, IEnumerable<IAuthorizationRequirement> requirements)
            => Task.FromResult(AuthorizationResult.Success());

        public Task<AuthorizationResult> AuthorizeAsync(ClaimsPrincipal user, object? resource, string policyName)
            => Task.FromResult(string.Equals(policyName, Roles.ClubAdmin, StringComparison.Ordinal) && !user.IsInRole(Roles.ClubAdmin)
                ? AuthorizationResult.Failed()
                : AuthorizationResult.Success());
    }
}
