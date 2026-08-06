using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Shared.Security;
using OneOf.Types;
using PlayerDetailPage = Nova.UI.Features.Players.Pages.PlayerDetail;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Component-level tests for the <see cref="PlayerDetailPage"/> covering profile display, campaign history,
/// role matrix, admin mutations with refresh, attribution, archived data, and error/empty states.
/// </summary>
public sealed class PlayerDetailComponentsTests : BunitContext
{
    // ── Loading state ─────────────────────────────────────────────────────────

    [Fact]
    public void PlayerDetail_ShowsLoadingState_WhileDetailRequestIsPending()
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
    public void PlayerDetail_ShowsNotFoundState_WhenServiceReturnsNotFound()
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
    public void PlayerDetail_RedirectsToAccessDenied_WhenServiceReturnsForbidden()
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
    public void PlayerDetail_ShowsErrorAndRetry_WhenTransportFails()
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
    public void PlayerDetail_DisplaysProfileFields()
    {
        RegisterServices();

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Avery Johnson");
            cut.Markup.ShouldContain("2032");
            cut.Markup.ShouldContain("Active");
            cut.Markup.ShouldContain(new DateOnly(2012, 4, 1).ToString("MMMM d, yyyy"));
            cut.Markup.ShouldContain("Female");
            cut.Markup.ShouldContain("12");
        });
    }

    [Fact]
    public void PlayerDetail_ShowsArchivedLifecycleBadge_WhenPlayerIsArchived()
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
    public void PlayerDetail_RendersGroupsNewestFirst()
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
    public void PlayerDetail_ShowsEmptyHistoryMessage_WhenNoCampaigns()
    {
        RegisterServices();

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No campaign history yet."));
    }

    // ── Closed campaign rendering ─────────────────────────────────────────────

    [Fact]
    public void PlayerDetail_ShowsClosedCampaignWithStatus()
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
    public void PlayerDetail_ShowsNoteContentAndAttribution_InCampaignHistory()
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
    public void PlayerDetail_ShowsTagNameAndAttribution_InCampaignHistory()
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
    public void PlayerDetail_ShowsArchivedIndicator_WhenTagDefinitionIsArchived()
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

    // ── Role matrix: admin sees actions ──────────────────────────────────────

    [Fact]
    public void PlayerDetail_ShowsAdminActions_ForClubAdmin()
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
    public void PlayerDetail_ShowsRestoreButton_ForArchivedPlayerAndAdmin()
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

    // ── Role matrix: evaluator is read-only ───────────────────────────────────

    [Fact]
    public void PlayerDetail_HidesAdminActions_ForEvaluator()
    {
        RegisterServices(isClubAdmin: false);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldNotContain("btn-outline-primary");
            cut.Markup.ShouldNotContain("btn-outline-warning");
            cut.Markup.ShouldNotContain("btn-outline-success");
        });
    }

    // ── Edit mutation with refresh ────────────────────────────────────────────

    [Fact]
    public void PlayerDetail_RefreshesDetail_AfterSuccessfulEdit()
    {
        var managementService = Substitute.For<IPlayerManagementService>();
        managementService.UpdateAsync(Arg.Any<UpdatePlayerInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<PlayerDto>(new PlayerDto
            {
                PlayerId = 7,
                ClubId = 42,
                FirstName = "Avery",
                LastName = "Johnson",
                DateOfBirth = new DateOnly(2012, 4, 1),
                GraduationYear = 2032,
                LifecycleStatus = LifecycleStatus.Active
            })));

        RegisterServices(isClubAdmin: true, managementService: managementService);

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Avery Johnson"));

        cut.Find("button.btn-outline-primary").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit player"));
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Player updated successfully."));
    }

    // ── Archive mutation with refresh ─────────────────────────────────────────

    [Fact]
    public void PlayerDetail_RefreshesDetail_AfterSuccessfulArchive()
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
    public void PlayerDetail_ShowsArchiveBlockers_WhenArchiveReturnsConflict()
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
    public void PlayerDetail_RefreshesDetail_AfterSuccessfulRestore()
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
    public void PlayerDetail_UsesFallbackReturnUrl_WhenReturnUrlIsExternal()
    {
        RegisterServices();
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/players/7?returnUrl=https%3A%2F%2Fevil.example%2Fphish");

        var cut = Render<PlayerDetailPage>(p => p.Add(c => c.PlayerId, 7));
        cut.WaitForAssertion(() =>
            cut.Find("a.btn-outline-secondary").GetAttribute("href").ShouldBe("/players"));
    }

    [Fact]
    public void PlayerDetail_PreservesSafeRelativeReturnUrl_InBackLink()
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
    public void PlayerDetail_UsesFallbackColor_WhenTraitColorContainsInjection()
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
                .ShouldBe("background-color: #6C757D; color: #ffffff;"));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void RegisterServices(
        bool isClubAdmin = false,
        IPlayerDetailService? detailService = null,
        IPlayerManagementService? managementService = null,
        IPlayerLifecycleService? lifecycleService = null)
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
            new FakeAuthenticationStateProvider(CreatePrincipal(isClubAdmin)));
    }

    private static PlayerDetailDto CreatePlayerDetail(
        LifecycleStatus lifecycleStatus = LifecycleStatus.Active,
        IReadOnlyList<PlayerCurrentTraitDto>? currentTraits = null,
        IReadOnlyList<PlayerCampaignHistoryDto>? campaignHistory = null)
        => new(
            7,
            "Avery",
            "Johnson",
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
