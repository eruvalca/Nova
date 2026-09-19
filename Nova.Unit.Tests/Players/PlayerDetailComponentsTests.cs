using System.Globalization;
using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using NSubstitute;
using OneOf.Types;
using Shouldly;
using PlayerDetailPage = Nova.UI.Features.Players.Pages.PlayerDetail;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Component-level tests for the <see cref="PlayerDetailPage"/> covering profile display, campaign history,
/// the authority matrix (admin, ordinary member, no club), mutations with refresh, attribution, archived data,
/// and error/empty states.
/// </summary>
public sealed class PlayerDetailComponentsTests : BunitContext
{
    [Fact]
    public void PlayerDetailRefreshesReturnContextOnQueryOnlyNavigationWithoutReloadingDetail()
    {
        var detail = Substitute.For<IPlayerDetailService>();
        detail.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail()));
        RegisterServices(detailService: detail);
        var navigation = Services.GetRequiredService<NavigationManager>();
        const string First = "/campaigns/10?tab=place&placementPage=2&placementParticipant=301";
        const string Second = "/players?search=Chen&view=archived";
        navigation.NavigateTo("/players/7?returnUrl=" + Uri.EscapeDataString(First));
        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        foreach (var target in new[] { First, Second, First, "https://evil.example/", string.Empty })
        {
            navigation.NavigateTo("/players/7?returnUrl=" + Uri.EscapeDataString(target));
            cut.WaitForAssertion(() => cut.Find("a.btn-outline-secondary").GetAttribute("href")
                .ShouldBe(target is First or Second ? target : "/players"));
        }
        _ = detail.Received(1).GetPlayerDetailAsync(7, Arg.Any<CancellationToken>());
    }

    // ── Loading state ─────────────────────────────────────────────────────────

    [Fact]
    public void PlayerDetailShowsLoadingStateWhileDetailRequestIsPending()
    {
        var pending = new TaskCompletionSource<ServiceResult<PlayerDetailDto>>();
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(detailService: detailService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.Markup.ShouldContain("Loading player details...");

        pending.SetResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
    }

    // ── Not-found state ───────────────────────────────────────────────────────

    [Fact]
    public void PlayerDetailShowsNotFoundStateWhenServiceReturnsNotFound()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(
                ServiceProblem.NotFound("Player not found."))));

        RegisterServices(detailService: detailService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 99));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Player not found"));
        cut.Markup.ShouldNotContain("Loading player details...");
    }

    [Fact]
    public void PlayerDetailRedirectsToAccessDeniedWhenServiceReturnsForbidden()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(
                ServiceProblem.Forbidden("Access denied."))));

        RegisterServices(detailService: detailService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() => navigationManager.Uri.ShouldEndWith("/Account/AccessDenied"));
    }

    // ── Transport error with retry ────────────────────────────────────────────

    [Fact]
    public void PlayerDetailShowsErrorAndRetryWhenTransportFails()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<PlayerDetailDto>(ServiceProblem.ServerError("Service unavailable."))),
                Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail())));

        RegisterServices(detailService: detailService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Service unavailable."));
        cut.Find("button.btn-outline-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));
    }

    // ── Profile fields ────────────────────────────────────────────────────────

    [Fact]
    public void PlayerDetailDisplaysProfileFields()
    {
        RegisterServices();

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Avery Johnson");
            cut.Markup.ShouldContain("2032");
            cut.Markup.ShouldContain("Active");
            cut.Markup.ShouldContain(new DateOnly(2012, 4, 1).ToString("MMMM d, yyyy", System.Globalization.CultureInfo.InvariantCulture));
            cut.Markup.ShouldContain("Female");
            cut.Markup.ShouldContain("12");
        });
    }

    [Fact]
    public void PlayerDetailShowsArchivedLifecycleBadgeWhenPlayerIsArchived()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(
                CreatePlayerDetail(lifecycleStatus: LifecycleStatus.Archived))));

        RegisterServices(detailService: detailService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
            cut.Find("span.badge.text-bg-secondary").TextContent.Trim().ShouldBe("Archived"));
    }

    // ── Campaign history grouping and ordering ────────────────────────────────

    [Fact]
    public void PlayerDetailRendersGroupsNewestFirst()
    {
        var history = new List<PlayerCampaignHistoryDto>
        {
            BuildCampaignHistory(1, "Early Campaign", new DateOnly(2024, 1, 1)),
            BuildCampaignHistory(2, "Recent Campaign", new DateOnly(2025, 6, 1))
        };

        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail(campaignHistory: history))));

        RegisterServices(detailService: detailService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
        {
            var articles = cut.FindAll("article");
            articles.Count.ShouldBe(2);
            articles[0].TextContent.ShouldContain("Recent Campaign");
            articles[1].TextContent.ShouldContain("Early Campaign");
        });
    }

    // ── Empty campaign history ────────────────────────────────────────────────

    [Fact]
    public void PlayerDetailShowsEmptyHistoryMessageWhenNoCampaigns()
    {
        RegisterServices();

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No campaign history yet."));
    }

    // ── Closed campaign rendering ─────────────────────────────────────────────

    [Fact]
    public void PlayerDetailShowsClosedCampaignWithStatus()
    {
        var history = new List<PlayerCampaignHistoryDto>
        {
            BuildCampaignHistory(1, "Closed Campaign", new DateOnly(2024, 1, 1), CampaignStatus.Closed)
        };

        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail(campaignHistory: history))));

        RegisterServices(detailService: detailService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Closed Campaign");
            cut.Markup.ShouldContain("Closed");
        });
    }

    // ── Note attribution ──────────────────────────────────────────────────────

    [Fact]
    public void PlayerDetailShowsNoteContentAndAttributionInCampaignHistory()
    {
        var note = new PlayerEvaluationNoteDto(
            NoteId: 1,
            Content: "Strong leadership presence.",
            AuthorUserId: 50,
            AuthorDisplayName: "Coach Riley",
            CreatedAt: new DateTimeOffset(2025, 3, 10, 0, 0, 0, TimeSpan.Zero));

        var history = new List<PlayerCampaignHistoryDto>
        {
            BuildCampaignHistory(1, "Spring Tryouts", new DateOnly(2025, 3, 1), notes: [note])
        };

        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail(campaignHistory: history))));

        RegisterServices(detailService: detailService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Strong leadership presence.");
            cut.Markup.ShouldContain("Coach Riley");
        });
    }

    // ── Tag attribution ───────────────────────────────────────────────────────

    [Fact]
    public void PlayerDetailShowsTagNameAndAttributionInCampaignHistory()
    {
        var tagApplication = new PlayerTagApplicationDto(
            CampaignTagApplicationId: 1,
            PlayerTagId: 10,
            TagName: "Defender",
            TagColor: "#0055AA",
            IsTagArchived: false,
            ApplyingUserId: 55,
            ApplyingUserDisplayName: "Scout Jordan",
            AppliedAt: new DateTimeOffset(2025, 4, 2, 0, 0, 0, TimeSpan.Zero));

        var history = new List<PlayerCampaignHistoryDto>
        {
            BuildCampaignHistory(1, "Spring Tryouts", new DateOnly(2025, 3, 1), tagApplications: [tagApplication])
        };

        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail(campaignHistory: history))));

        RegisterServices(detailService: detailService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Defender");
            cut.Markup.ShouldContain("Scout Jordan");
        });
    }

    // ── Archived tag definition ───────────────────────────────────────────────

    [Fact]
    public void PlayerDetailShowsArchivedIndicatorWhenTagDefinitionIsArchived()
    {
        var archivedTag = new PlayerTagApplicationDto(
            CampaignTagApplicationId: 2,
            PlayerTagId: 20,
            TagName: "OldTag",
            TagColor: "#AABBCC",
            IsTagArchived: true,
            ApplyingUserId: 60,
            ApplyingUserDisplayName: "Evaluator Kim",
            AppliedAt: new DateTimeOffset(2024, 1, 5, 0, 0, 0, TimeSpan.Zero));

        var history = new List<PlayerCampaignHistoryDto>
        {
            BuildCampaignHistory(1, "Winter Tryouts", new DateOnly(2024, 1, 1), tagApplications: [archivedTag])
        };

        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail(campaignHistory: history))));

        RegisterServices(detailService: detailService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("OldTag (archived)");
            cut.Markup.ShouldContain("badge-archived");
        });
    }

    // ── Role matrix: any authenticated club member reaches the lifecycle actions ──

    [Fact]
    public void PlayerDetailShowsAdminActionsForClubAdmin()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Edit");
            cut.Markup.ShouldContain("Archive");
        });
    }

    [Fact]
    public void PlayerDetailShowsRestoreButtonForArchivedPlayerAndAdmin()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(
                CreatePlayerDetail(lifecycleStatus: LifecycleStatus.Archived))));

        RegisterServices(detailService: detailService, isClubAdmin: true);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Restore");
            cut.Markup.ShouldNotContain("btn-outline-warning"); // Archive button should not be present
        });
    }

    // ── Role matrix: membership, not the admin role, reaches the record's lifecycle actions ──

    [Fact]
    public void PlayerDetailShowsArchiveForOrdinaryClubMember()
    {
        RegisterServices(isClubAdmin: false);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() => cut.FindAll("button.btn-outline-warning").Count.ShouldBe(1));
        cut.Markup.ShouldNotContain("btn-outline-success");
    }

    [Fact]
    public void PlayerDetailShowsRestoreForOrdinaryClubMemberOnAnArchivedRecord()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(
                CreatePlayerDetail(lifecycleStatus: LifecycleStatus.Archived))));

        RegisterServices(detailService: detailService, isClubAdmin: false);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() => cut.FindAll("button.btn-outline-success").Count.ShouldBe(1));
        cut.Markup.ShouldNotContain("btn-outline-warning");
    }

    // ── Role matrix: a principal without club authority is read-only ──────────

    [Fact]
    public void PlayerDetailHidesLifecycleActionsWithoutClubMembership()
    {
        RegisterServices(isClubAdmin: false, hasClubMembership: false);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Find("a.btn-outline-primary").GetAttribute("href").ShouldBe("/players/7/edit");
            cut.Markup.ShouldNotContain("btn-outline-warning");
            cut.Markup.ShouldNotContain("btn-outline-success");
        });
    }

    // ── Edit mutation with refresh ────────────────────────────────────────────

    [Fact]
    public void PlayerDetailLinksToSharedEditHost()
    {
        RegisterServices(isClubAdmin: false);
        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() => cut.Find("a.btn-outline-primary").GetAttribute("href").ShouldBe("/players/7/edit"));
        cut.FindAll("form").ShouldBeEmpty();
    }

    // ── Archive mutation with refresh ─────────────────────────────────────────

    [Fact]
    public void PlayerDetailRefreshesDetailAfterSuccessfulArchive()
    {
        var lifecycleService = Substitute.For<IPlayerLifecycleService>();
        lifecycleService.ArchiveAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));

        RegisterServices(isClubAdmin: true, lifecycleService: lifecycleService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("button.btn-outline-warning").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Archive Avery Johnson?"));

        cut.Find("#archive-confirm-checkbox").Change(true);
        cut.Find("button.btn-warning").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Player archived."));
    }

    // ── Archive blockers displayed ────────────────────────────────────────────

    [Fact]
    public void PlayerDetailShowsArchiveBlockersWhenArchiveReturnsConflict()
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

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
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

    // ── Restore mutation ──────────────────────────────────────────────────────

    [Fact]
    public void PlayerDetailRefreshesDetailAfterSuccessfulRestore()
    {
        var lifecycleService = Substitute.For<IPlayerLifecycleService>();
        lifecycleService.RestoreAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));

        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(7, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(
                CreatePlayerDetail(lifecycleStatus: LifecycleStatus.Archived))));

        RegisterServices(isClubAdmin: true, detailService: detailService, lifecycleService: lifecycleService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Restore"));

        cut.Find("button.btn-outline-success").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("restored"));
    }

    // ── Return URL ────────────────────────────────────────────────────────────

    [Fact]
    public void PlayerDetailUsesFallbackReturnUrlWhenReturnUrlIsExternal()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/players/7?returnUrl=https%3A%2F%2Fevil.example%2Fphish");

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
            cut.Find("a.btn-outline-secondary").GetAttribute("href").ShouldBe("/players"));
    }

    [Fact]
    public void PlayerDetailPreservesSafeRelativeReturnUrlInBackLink()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/players/7?returnUrl=%2Fplayers%3Fview%3Darchived%26search%3DAvery");

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
            cut.Find("a.btn-outline-secondary").GetAttribute("href")
                .ShouldBe("/players?view=archived&search=Avery"));
    }

    // ── Tag color sanitization ────────────────────────────────────────────────

    [Fact]
    public void PlayerDetailUsesFallbackColorWhenTraitColorContainsInjection()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(
                CreatePlayerDetail(currentTraits:
                [
                    new PlayerCurrentTraitDto(11, "Defender", "#0055AA; color: red;")
                ]))));

        RegisterServices(detailService: detailService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
            cut.Find("span.badge.rounded-pill").GetAttribute("style")
                .ShouldBe("background-color: #6C757D; color: #FFFFFF;"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RegisterServices(
        bool isClubAdmin = false,
        IPlayerDetailService? detailService = null,
        IPlayerManagementService? managementService = null,
        IPlayerLifecycleService? lifecycleService = null,
        bool hasClubMembership = true)
    {
        if (detailService is null)
        {
            detailService = Substitute.For<IPlayerDetailService>();
            detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail())));
        }

        managementService ??= Substitute.For<IPlayerManagementService>();
        lifecycleService ??= Substitute.For<IPlayerLifecycleService>();

        Services.AddSingleton(detailService);
        Services.AddSingleton(managementService);
        Services.AddSingleton(lifecycleService);
        Services.AddSingleton<AuthenticationStateProvider>(
            new FakeAuthenticationStateProvider(CreatePrincipal(isClubAdmin, hasClubMembership)));
    }

    /// <summary>The archive confirmation reviews the player it was opened for, not the current route.</summary>
    [Fact]
    public async Task PlayerDetailArchivesTheReviewedSubjectWhenTheRouteChangesWhileThePanelIsOpenAsync()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail())));
        var lifecycleService = Substitute.For<IPlayerLifecycleService>();
        lifecycleService.ArchiveAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<Success>(new Success())));
        RegisterServices(isClubAdmin: true, detailService: detailService, lifecycleService: lifecycleService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.Find("button.btn-outline-warning").ClickAsync(new());
        cut.Find("#archive-confirmation-heading").TextContent.ShouldContain("Avery Johnson");

        // The routed detail is reused for another player while the panel is open.
        cut.Render(p => p.Add(c => c.PlayerId, 21));

        // The panel still reviews Avery, and confirming archives Avery rather than the new route.
        cut.Find("#archive-confirmation-heading").TextContent.ShouldContain("Avery Johnson");
        await cut.Find("#archive-confirm-checkbox").ChangeAsync(new ChangeEventArgs { Value = true });
        await cut.Find("#archive-commit").ClickAsync(new());

        await lifecycleService.Received(1).ArchiveAsync(7, Arg.Any<CancellationToken>());
        await lifecycleService.DidNotReceive().ArchiveAsync(21, Arg.Any<CancellationToken>());
    }

    /// <summary>An archive reviewed for the routed player reports nothing once the route moves on.</summary>
    [Fact]
    public async Task PlayerDetailDropsAnArchiveOutcomeWhenTheRouteMovesOnMidFlightAsync()
    {
        var held = new TaskCompletionSource<ServiceResult<Success>>();
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail())));
        var lifecycleService = Substitute.For<IPlayerLifecycleService>();
        lifecycleService.ArchiveAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ => held.Task);
        RegisterServices(isClubAdmin: true, detailService: detailService, lifecycleService: lifecycleService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.Find("button.btn-outline-warning").ClickAsync(new());
        await cut.Find("#archive-confirm-checkbox").ChangeAsync(new ChangeEventArgs { Value = true });
        var archive = cut.Find("#archive-commit").ClickAsync(new());

        // The route names another player before the archive answers.
        cut.Render(p => p.Add(c => c.PlayerId, 21));
        await cut.InvokeAsync(() => held.SetResult(new ServiceResult<Success>(new Success())));
        await archive;

        // That submission started under the route the page left, so its outcome is not reported here — and
        // the state it set is released with it rather than leaving the new page's controls disabled.
        cut.Markup.ShouldNotContain("Player archived.");
        cut.Find("button.btn-outline-warning").HasAttribute("disabled").ShouldBeFalse();
    }

    /// <summary>A reused routed page binds the player the route names and rejects the previous one's read.</summary>
    [Fact]
    public async Task PlayerDetailBindsTheRoutedPlayerWhenTheRouteNamesAnotherAsync()
    {
        var held = new TaskCompletionSource<ServiceResult<PlayerDetailDto>>();
        var calls = 0;
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1
                ? held.Task
                : Task.FromResult(new ServiceResult<PlayerDetailDto>(
                    CreatePlayerDetail(playerId: 21, firstName: "Blake", lastName: "Stone"))));
        RegisterServices(isClubAdmin: true, detailService: detailService);
        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));

        // The routed page is reused for another player while the first read is still in flight.
        cut.Render(p => p.Add(c => c.PlayerId, 21));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Blake Stone"));
        await detailService.Received(1).GetPlayerDetailAsync(21, Arg.Any<CancellationToken>());

        // The previous player's read answers last and must not bind that player to this route.
        await cut.InvokeAsync(() => held.SetResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail())));
        cut.Markup.ShouldContain("Blake Stone");
        cut.Markup.ShouldNotContain("Avery Johnson");
    }

    /// <summary>A claim change closes the reviewed panel and rebinds the page to the new club.</summary>
    [Fact]
    public async Task PlayerDetailRebindsClubScopeWhenTheClaimedClubChangesAsync()
    {
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail())));
        RegisterServices(isClubAdmin: true, detailService: detailService);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.Find("button.btn-outline-warning").ClickAsync(new());
        cut.FindAll("#archive-confirmation").Count.ShouldBe(1);

        // The claimed club changes while the page stays mounted.
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true, clubId: 43)));

        // The reviewed panel belonged to the previous scope, and the detail is re-read for the new one.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#archive-confirmation").Count.ShouldBe(0));
        await detailService.Received(2).GetPlayerDetailAsync(7, Arg.Any<CancellationToken>());
    }

    /// <summary>Another member of the same club is a different caller, so the scope rebinds.</summary>
    [Fact]
    public async Task PlayerDetailRebindsScopeWhenAnotherMemberOfTheSameClubTakesOverAsync()
    {
        var held = new TaskCompletionSource<ServiceResult<Success>>();
        var reads = 0;
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++reads == 1
                ? Task.FromResult(new ServiceResult<PlayerDetailDto>(CreatePlayerDetail()))
                : Task.FromResult(new ServiceResult<PlayerDetailDto>(
                    CreatePlayerDetail(firstName: "Blake", lastName: "Stone"))));
        var lifecycleService = Substitute.For<IPlayerLifecycleService>();
        lifecycleService.ArchiveAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ => held.Task);
        RegisterServices(isClubAdmin: true, detailService: detailService, lifecycleService: lifecycleService);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.Find("button.btn-outline-warning").ClickAsync(new());
        await cut.Find("#archive-confirm-checkbox").ChangeAsync(new ChangeEventArgs { Value = true });
        var archive = cut.Find("#archive-commit").ClickAsync(new());

        // Another member of the same club takes over while the archive is still in flight.
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true, userId: "202")));
        await cut.InvokeAsync(() => held.SetResult(new ServiceResult<Success>(new Success())));
        await archive;

        // The club is the same but the caller is not: the reviewed panel closes, the detail is re-read for
        // the caller now on screen, and the previous caller's completed mutation reports nothing here.
        await cut.WaitForAssertionAsync(() => cut.FindAll("#archive-confirmation").Count.ShouldBe(0));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Blake Stone"));
        cut.Markup.ShouldNotContain("Player archived.");
        reads.ShouldBe(2);
    }

    /// <summary>Losing club membership closes the reviewed panel and its controls.</summary>
    [Fact]
    public async Task PlayerDetailClosesTheReviewedPanelWhenMembershipIsRevokedAsync()
    {
        RegisterServices(isClubAdmin: false, hasClubMembership: true);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(false));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Avery Johnson"));
        await cut.Find("button.btn-outline-warning").ClickAsync(new());
        cut.FindAll("#archive-confirmation").Count.ShouldBe(1);

        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(false, hasClubMembership: false)));

        // A revoked membership must not leave the confirmation actionable on a page the member can no
        // longer mutate.
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldNotContain("Archive"));
        cut.FindAll("#archive-confirmation").Count.ShouldBe(0);
    }

    /// <summary>A detail read that finished after the claimed club changed cannot repopulate the view.</summary>
    [Fact]
    public async Task PlayerDetailIgnoresADetailReadThatFinishedAfterTheClaimedClubChangedAsync()
    {
        var held = new TaskCompletionSource<ServiceResult<PlayerDetailDto>>();
        var calls = 0;
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1
                ? held.Task
                : Task.FromResult(new ServiceResult<PlayerDetailDto>(
                    CreatePlayerDetail(firstName: "Blake", lastName: "Stone"))));
        RegisterServices(isClubAdmin: true, detailService: detailService);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true, clubId: 43)));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Blake Stone"));

        // The previous club's read answers last and must not overwrite the new scope's player.
        await cut.InvokeAsync(() => held.SetResult(new ServiceResult<PlayerDetailDto>(
            CreatePlayerDetail(firstName: "Avery", lastName: "Johnson"))));

        cut.Markup.ShouldContain("Blake Stone");
        cut.Markup.ShouldNotContain("Avery Johnson");
    }

    /// <summary>A startup read that resolves after a notification cannot overwrite the new principal.</summary>
    [Fact]
    public async Task PlayerDetailIgnoresAStartupAuthenticationReadThatResolvedAfterANotificationAsync()
    {
        var pending = new TaskCompletionSource<AuthenticationState>();
        RegisterServices(isClubAdmin: true);
        var authentication = new DeferredAuthentication(pending.Task);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));

        // A notification for a club member arrives while the startup read is still pending, and the
        // page binds to it: the lifecycle controls are offered.
        await cut.InvokeAsync(() => authentication.Publish(Task.FromResult(
            new AuthenticationState(CreatePrincipal(true, clubId: 43)))));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Archive"));

        // The startup read then resolves as a principal with no club membership; it must not win.
        await cut.InvokeAsync(() => pending.SetResult(
            new AuthenticationState(CreatePrincipal(false, hasClubMembership: false))));

        cut.Markup.ShouldContain("Archive");
    }

    /// <summary>A startup read that lost the race loads neither its principal nor its detail.</summary>
    [Fact]
    public async Task PlayerDetailDoesNotLoadDetailFromAStaleStartupAuthenticationReadAsync()
    {
        var pendingAuth = new TaskCompletionSource<AuthenticationState>();
        var currentScopeLoad = new TaskCompletionSource<ServiceResult<PlayerDetailDto>>();
        var calls = 0;
        var detailService = Substitute.For<IPlayerDetailService>();
        detailService.GetPlayerDetailAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(_ => ++calls == 1
                ? currentScopeLoad.Task
                : Task.FromResult(new ServiceResult<PlayerDetailDto>(
                    CreatePlayerDetail(firstName: "Stale", lastName: "Read"))));
        RegisterServices(isClubAdmin: true, detailService: detailService);
        var authentication = new DeferredAuthentication(pendingAuth.Task);
        Services.AddSingleton<AuthenticationStateProvider>(authentication);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));

        // The notification rebinds the page to club 43 and starts its load, which stays open.
        await cut.InvokeAsync(() => authentication.Publish(Task.FromResult(
            new AuthenticationState(CreatePrincipal(true, clubId: 43)))));
        await cut.WaitForAssertionAsync(() => calls.ShouldBe(1));

        // The startup read then resolves as the old club. Its own load must not run: both loads share
        // the club-scope generation, so a stale one would win the race to apply.
        await cut.InvokeAsync(() => pendingAuth.SetResult(new AuthenticationState(CreatePrincipal(true))));
        calls.ShouldBe(1);

        await cut.InvokeAsync(() => currentScopeLoad.SetResult(new ServiceResult<PlayerDetailDto>(
            CreatePlayerDetail(firstName: "Blake", lastName: "Stone"))));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Blake Stone"));
        cut.Markup.ShouldNotContain("Stale Read");
    }

    private static PlayerDetailDto CreatePlayerDetail(
        LifecycleStatus lifecycleStatus = LifecycleStatus.Active,
        IReadOnlyList<PlayerCurrentTraitDto>? currentTraits = null,
        IReadOnlyList<PlayerCampaignHistoryDto>? campaignHistory = null,
        long playerId = 7,
        string firstName = "Avery",
        string lastName = "Johnson")
        => new(
            playerId,
            firstName,
            lastName,
            new DateOnly(2012, 4, 1),
            Gender.Female,
            2032,
            12,
            lifecycleStatus,
            currentTraits ?? [],
            campaignHistory ?? []);

    private static PlayerCampaignHistoryDto BuildCampaignHistory(
        long assignmentId,
        string name,
        DateOnly startDate,
        CampaignStatus status = CampaignStatus.Active,
        IReadOnlyList<PlayerEvaluationNoteDto>? notes = null,
        IReadOnlyList<PlayerTagApplicationDto>? tagApplications = null)
        => new(
            assignmentId,
            CampaignId: assignmentId * 100,
            CampaignName: name,
            CampaignStatus: status,
            CampaignStartDate: startDate,
            TryoutNumber: (int?)assignmentId,
            PlacementOutcome: PlacementOutcome.NotSelected,
            Team: null,
            Notes: notes ?? [],
            TagApplications: tagApplications ?? []);

    private static ClaimsPrincipal CreatePrincipal(bool isClubAdmin, bool hasClubMembership = true, long clubId = 42,
        string userId = "101")
    {
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, userId)
        };

        if (hasClubMembership)
        {
            claims.Add(new Claim(NovaClaimTypes.ClubId, clubId.ToString(CultureInfo.InvariantCulture)));
        }

        if (isClubAdmin)
        {
            claims.Add(new Claim(ClaimTypes.Role, Roles.ClubAdmin));
        }

        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }

    /// <summary>Provides a pending authentication state that a notification can overtake.</summary>
    /// <param name="pending">The state the startup read awaits.</param>
    private sealed class DeferredAuthentication(Task<AuthenticationState> pending) : AuthenticationStateProvider
    {
        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync() => pending;

        /// <summary>Publishes a notification, as the framework does when the state changes.</summary>
        /// <param name="state">The replacement state.</param>
        public void Publish(Task<AuthenticationState> state) => NotifyAuthenticationStateChanged(state);
    }

    /// <summary>
    /// Provides a fixed authentication state for bUnit component tests.
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
        {
            _state = Task.FromResult(new AuthenticationState(newPrincipal));
            NotifyAuthenticationStateChanged(_state);
        }
    }
}
