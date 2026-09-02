using System.Security.Claims;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.UI.Features.Campaigns.Components;
using NSubstitute;
using Shouldly;
using CampaignsPage = Nova.UI.Features.Campaigns.Pages.Campaigns;
using NewCampaignPage = Nova.UI.Features.Campaigns.Pages.NewCampaign;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Component-level tests for the campaign list, creation, and metadata correction workflows.
/// </summary>
public sealed class CampaignComponentsTests : BunitContext
{
    [Fact]
    public void CampaignsRoute_DeclaresInteractiveAutoRenderMode()
    {
        var razorPath = Path.Join(FindRepoRoot(), "Nova.UI", "Features", "Campaigns", "Pages", "Campaigns.razor");
        File.ReadAllText(razorPath).ShouldContain("@rendermode InteractiveAuto");
    }

    [Fact]
    public void NewCampaignRoute_DeclaresInteractiveAutoRenderMode()
    {
        var razorPath = Path.Join(FindRepoRoot(), "Nova.UI", "Features", "Campaigns", "Pages", "NewCampaign.razor");
        File.ReadAllText(razorPath).ShouldContain("@rendermode InteractiveAuto");
    }

    [Fact]
    public void Campaigns_ShowsLoadingState_WhileListRequestIsPending()
    {
        var pending = new TaskCompletionSource<ServiceResult<CampaignListResult>>();
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(pending.Task);

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.Markup.ShouldContain("Loading campaigns...");

        pending.SetResult(SuccessListResult(CreateSeasonGroups()));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));
    }

    [Fact]
    public void Campaigns_ShowsAdminEmptyState_WhenNoActiveCampaigns()
    {
        RegisterServices(isClubAdmin: true, seasonGroups: []);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("No active campaigns");
            cut.Markup.ShouldContain("Create your first campaign");
        });
    }

    [Fact]
    public void Campaigns_ShowsNeutralEmptyState_ForEvaluator()
    {
        RegisterServices(isClubAdmin: false, seasonGroups: []);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("No active campaigns"));

        cut.Markup.ShouldContain("There are no active campaigns right now");
        cut.Markup.ShouldNotContain("Create campaign");
    }

    [Fact]
    public void Campaigns_ShowsErrorAndRetries_WhenInitialLoadFails()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<CampaignListResult>(ServiceProblem.ServerError("Transport failed."))),
                Task.FromResult(SuccessListResult(CreateSeasonGroups())));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Transport failed."));
        cut.Find("button.btn-outline-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));
    }

    [Fact]
    public void Campaigns_GroupsRowsBySeason_WithCountsAndStatus()
    {
        RegisterServices(isClubAdmin: false);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Markup.ShouldContain("Summer 2026");
        cut.Markup.ShouldContain("Active");
        cut.Markup.ShouldContain("12");
        cut.Markup.ShouldContain("3");

        var nameLink = cut.Find("tbody a");
        nameLink.GetAttribute("href").ShouldBe("campaigns/10");
        nameLink.TextContent.Trim().ShouldBe("Summer Tryouts");
    }

    [Fact]
    public void Campaigns_ShowsAdminControls_ForClubAdmin()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Markup.ShouldContain("Create campaign");
        cut.Markup.ShouldContain("Edit season");
        cut.Find("tbody button").TextContent.ShouldContain("Edit");
    }

    [Fact]
    public void Campaigns_HidesAdminControls_ForEvaluator()
    {
        RegisterServices(isClubAdmin: false);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Markup.ShouldNotContain("Create campaign");
        cut.Markup.ShouldNotContain("Edit season");
        cut.Markup.ShouldNotContain("btn-outline-primary");
    }

    [Fact]
    public void Campaigns_RequestsActiveViewWithMaxLimit_ByDefault()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateSeasonGroups())));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        queryService.Received().GetCampaignListAsync(
            Arg.Is<GetCampaignListInput>(input =>
                input != null
                && string.Equals(input.Status, "active", StringComparison.Ordinal)
                && input.Limit == GetCampaignListInput.MaxLimit),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Campaigns_SwitchesToClosedView_WhenFilterChanges()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateSeasonGroups())));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("#campaigns-view-filter").Change("closed");
        cut.WaitForAssertion(() =>
            queryService.Received().GetCampaignListAsync(
                Arg.Is<GetCampaignListInput>(input =>
                    input != null && string.Equals(input.Status, "closed", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Campaigns_ShowsReadOnlyNoteWithoutEdit_ForClosedCampaigns()
    {
        RegisterServices(isClubAdmin: true, seasonGroups: CreateClosedSeasonGroups());

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Spring ID Camp"));

        cut.Markup.ShouldContain("Closed campaigns are read-only; reopen to edit.");
        cut.FindAll("tbody button").Count.ShouldBe(0);
    }

    [Fact]
    public void Campaigns_ShowsTruncationMessage_WhenTotalExceedsLoadedRows()
    {
        RegisterServices(isClubAdmin: true, totalCount: 120);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() =>
            cut.Markup.ShouldContain("Showing the most recent 1 of 120 campaigns."));
    }

    [Fact]
    public void Campaigns_ShowsSuccessMessage_AfterCampaignMetadataUpdate()
    {
        var metadataService = Substitute.For<ICampaignMetadataService>();
        metadataService.UpdateAsync(Arg.Any<UpdateCampaignMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<UpdateCampaignMetadataResult>(
                new UpdateCampaignMetadataResult(
                    10, "Summer Tryouts 2026", new DateOnly(2026, 6, 1), null, CampaignStatus.Active, 5, "Summer 2026"))));

        RegisterServices(isClubAdmin: true, metadataService: metadataService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit campaign metadata"));

        cut.Find("#edit-campaign-name").Change("Summer Tryouts 2026");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Campaign \"Summer Tryouts 2026\" metadata updated."));
    }

    [Fact]
    public void Campaigns_ShowsConflictMessage_WhenMetadataUpdateReturnsConflict()
    {
        var metadataService = Substitute.For<ICampaignMetadataService>();
        metadataService.UpdateAsync(Arg.Any<UpdateCampaignMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<UpdateCampaignMetadataResult>(
                ServiceProblem.Conflict("The campaign is Closed. Reopen the campaign before editing its metadata."))));

        RegisterServices(isClubAdmin: true, metadataService: metadataService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit campaign metadata"));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("The campaign is Closed. Reopen the campaign before editing its metadata.");
            cut.Markup.ShouldNotContain("_mutationError");
        });
    }

    [Fact]
    public void Campaigns_ShowsSuccessMessage_AfterSeasonMetadataUpdate()
    {
        var seasonMetadataService = Substitute.For<ISeasonMetadataService>();
        seasonMetadataService.UpdateAsync(Arg.Any<UpdateSeasonMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<UpdateSeasonMetadataResult>(
                new UpdateSeasonMetadataResult(5, "Summer 2026 Updated", new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31)))));

        RegisterServices(isClubAdmin: true, seasonMetadataService: seasonMetadataService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.FindAll("button").First(button => button.TextContent.Trim() == "Edit season").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit season metadata"));

        cut.Find("#edit-season-name").Change("Summer 2026 Updated");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Season \"Summer 2026 Updated\" metadata updated."));
    }

    [Fact]
    public void Campaigns_DoesNotReloadList_WhenPersistedStateIsRestored()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<PersistedStateCampaigns>(parameters => parameters
            .Add(component => component.StartInitialized, true)
            .Add(component => component.PersistedGroups, CreateSeasonGroups()));

        cut.Markup.ShouldContain("Summer Tryouts");
        queryService.DidNotReceive().GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Campaigns_AppliesClosedViewQuery_BeforeInitialLoad()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateClosedSeasonGroups())));

        RegisterServices(isClubAdmin: true, queryService: queryService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns?view=closed");

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Spring ID Camp"));

        queryService.Received().GetCampaignListAsync(
            Arg.Is<GetCampaignListInput>(input =>
                input != null && string.Equals(input.Status, "closed", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
        queryService.DidNotReceive().GetCampaignListAsync(
            Arg.Is<GetCampaignListInput>(input =>
                input != null && string.Equals(input.Status, "active", StringComparison.Ordinal)),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Campaigns_IgnoresStaleLoadCompletion_WhenViewChanges()
    {
        var pendingActive = new TaskCompletionSource<ServiceResult<CampaignListResult>>();
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(
                pendingActive.Task,
                Task.FromResult(SuccessListResult(CreateClosedSeasonGroups())));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.Find("#campaigns-view-filter").Change("closed");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Spring ID Camp"));

        // The superseded Active load completes late and must not overwrite the Closed view.
        pendingActive.SetResult(SuccessListResult(CreateSeasonGroups()));
        Thread.Sleep(150);
        cut.Markup.ShouldContain("Spring ID Camp");
        cut.Markup.ShouldNotContain("Summer Tryouts");
    }

    [Fact]
    public void Campaigns_RetryResumesSeasonLoad_AfterEditSeasonChoicesFailure()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateSeasonGroups())));
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<CampaignCreationSetupResult>(ServiceProblem.ServerError("Season load failed."))),
                Task.FromResult(SuccessSetupResult()));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Season load failed."));

        cut.Find("button.btn-outline-danger").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Edit campaign metadata");
            cut.Markup.ShouldNotContain("Season load failed.");
        });
    }

    [Fact]
    public void Campaigns_ClosesEditForm_WhenViewChanges()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit campaign metadata"));

        cut.Find("#campaigns-view-filter").Change("closed");
        cut.WaitForAssertion(() => cut.Markup.ShouldNotContain("Edit campaign metadata"));
    }

    [Fact]
    public void Campaigns_ShowsFieldErrors_WhenMetadataUpdateReturnsEmptyDetail()
    {
        var metadataService = Substitute.For<ICampaignMetadataService>();
        metadataService.UpdateAsync(Arg.Any<UpdateCampaignMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<UpdateCampaignMetadataResult>(
                ServiceProblem.Validation(
                    new Dictionary<string, string[]> { ["StartDate"] = ["The start date must be inside the season."] },
                    detail: string.Empty))));

        RegisterServices(isClubAdmin: true, metadataService: metadataService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit campaign metadata"));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The start date must be inside the season."));
    }

    [Fact]
    public void Campaigns_ShowsRetryableError_WhenMetadataUpdateThrowsTransportFailure()
    {
        var metadataService = Substitute.For<ICampaignMetadataService>();
        metadataService.UpdateAsync(Arg.Any<UpdateCampaignMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns<Task<ServiceResult<UpdateCampaignMetadataResult>>>(_ => throw new HttpRequestException("offline"));

        RegisterServices(isClubAdmin: true, metadataService: metadataService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit campaign metadata"));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Could not reach the server. Check your connection and retry.");
            cut.Find("button[type='submit']").HasAttribute("disabled").ShouldBeFalse();
        });
    }

    [Fact]
    public void Campaigns_HidesEditSeasonAction_InClosedView()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateClosedSeasonGroups())));

        RegisterServices(isClubAdmin: true, queryService: queryService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns?view=closed");

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Spring ID Camp"));

        cut.Markup.ShouldNotContain("Edit season");
    }

    [Fact]
    public void Campaigns_SyncsViewQueryParam_WhenFilterChanges()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateSeasonGroups())));

        RegisterServices(isClubAdmin: true, queryService: queryService);
        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns?view=closed");

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("#campaigns-view-filter").Change("active");
        cut.WaitForAssertion(() =>
            navigationManager.Uri.ShouldContain("view=active"));
    }

    [Fact]
    public void Campaigns_EditActions_HaveTargetSpecificAccessibleNames()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").GetAttribute("aria-label").ShouldBe("Edit campaign Summer Tryouts in Summer 2026");
        cut.FindAll("button").First(button => button.TextContent.Trim() == "Edit season")
            .GetAttribute("aria-label").ShouldBe("Edit season Summer 2026");
    }

    [Fact]
    public void Campaigns_SupersedesEarlierEdit_WhenSeasonChoicesLoadIsPending()
    {
        var pendingSetup = new TaskCompletionSource<ServiceResult<CampaignCreationSetupResult>>();
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateSeasonGroups())));
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(pendingSetup.Task);

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.FindAll("button").First(button => button.TextContent.Trim() == "Edit season").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit season metadata"));

        pendingSetup.SetResult(SuccessSetupResult());
        Thread.Sleep(150);
        cut.Markup.ShouldContain("Edit season metadata");
        cut.Markup.ShouldNotContain("Edit campaign metadata");
    }

    [Fact]
    public void Campaigns_ReloadsSeasonChoices_AfterSeasonMetadataUpdate()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateSeasonGroups())));
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessSetupResult()));

        var seasonMetadataService = Substitute.For<ISeasonMetadataService>();
        seasonMetadataService.UpdateAsync(Arg.Any<UpdateSeasonMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<UpdateSeasonMetadataResult>(
                new UpdateSeasonMetadataResult(5, "Summer 2026 Updated", new DateOnly(2026, 6, 1), new DateOnly(2026, 8, 31)))));

        RegisterServices(isClubAdmin: true, queryService: queryService, seasonMetadataService: seasonMetadataService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        // Prime the season-choice cache, then rename the season.
        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit campaign metadata"));
        cut.FindAll("button").First(button => button.TextContent.Trim() == "Cancel").Click();

        cut.FindAll("button").First(button => button.TextContent.Trim() == "Edit season").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit season metadata"));
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("metadata updated."));

        // Reopening campaign edit must reload season choices with current names/dates.
        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() =>
            queryService.Received(2).GetCreationSetupAsync(Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void Campaigns_DisablesEditActions_WhileMutationIsPending()
    {
        var pendingUpdate = new TaskCompletionSource<ServiceResult<UpdateCampaignMetadataResult>>();
        var metadataService = Substitute.For<ICampaignMetadataService>();
        metadataService.UpdateAsync(Arg.Any<UpdateCampaignMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns(pendingUpdate.Task);

        RegisterServices(isClubAdmin: true, metadataService: metadataService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit campaign metadata"));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Find("tbody button").HasAttribute("disabled").ShouldBeTrue();
            cut.FindAll("button").First(button => button.TextContent.Trim() == "Edit season")
                .HasAttribute("disabled").ShouldBeTrue();
            cut.Find("#campaigns-view-filter").HasAttribute("disabled").ShouldBeTrue();
        });

        pendingUpdate.SetResult(new ServiceResult<UpdateCampaignMetadataResult>(
            new UpdateCampaignMetadataResult(10, "Summer Tryouts", new DateOnly(2026, 6, 15), new DateOnly(2026, 6, 20), CampaignStatus.Active, 5, "Summer 2026")));
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("metadata updated."));
    }

    [Fact]
    public void Campaigns_ClosesEditForm_WhenViewQueryNavigatesToClosed()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateSeasonGroups())));
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessSetupResult()));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit campaign metadata"));

        var navigationManager = Services.GetRequiredService<NavigationManager>();
        navigationManager.NavigateTo("/campaigns?view=closed");
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldNotContain("Edit campaign metadata");
            queryService.Received().GetCampaignListAsync(
                Arg.Is<GetCampaignListInput>(input =>
                    input != null && string.Equals(input.Status, "closed", StringComparison.Ordinal)),
                Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public void Campaigns_DoesNotInstallRetryState_WhenEditSelectionIsSuperseded()
    {
        var pendingSetup = new TaskCompletionSource<ServiceResult<CampaignCreationSetupResult>>();
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateSeasonGroups())));
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(pendingSetup.Task);

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.FindAll("button").First(button => button.TextContent.Trim() == "Edit season").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit season metadata"));

        // The superseded campaign edit's setup load fails late; it must not publish its error
        // or become the Retry target while the season form is open.
        pendingSetup.SetResult(new ServiceResult<CampaignCreationSetupResult>(ServiceProblem.ServerError("Season load failed.")));
        Thread.Sleep(150);
        cut.Markup.ShouldContain("Edit season metadata");
        cut.Markup.ShouldNotContain("Season load failed.");
        cut.FindAll("button.btn-outline-danger").Count.ShouldBe(0);
    }

    [Fact]
    public void Campaigns_NotesSeasonTruncation_InEditForm()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateSeasonGroups())));
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignCreationSetupResult>(new CampaignCreationSetupResult
            {
                Seasons = CreateSeasonChoices(),
                TotalSeasonCount = 150,
                ActivePlayerCount = 34,
                ActiveTeamCount = 6
            })));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() =>
            cut.Markup.ShouldContain("Only the newest 1 of 150 seasons are available for selection."));
    }

    [Fact]
    public void Campaigns_KeepsFresherSeasonChoices_WhenStaleSetupCompletesLate()
    {
        var staleSetup = new TaskCompletionSource<ServiceResult<CampaignCreationSetupResult>>();
        var freshSetup = new TaskCompletionSource<ServiceResult<CampaignCreationSetupResult>>();
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateSeasonGroups())));
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(staleSetup.Task, freshSetup.Task);

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        // First edit selection starts a slow setup load; a second selection supersedes it.
        cut.Find("tbody button").Click();
        cut.FindAll("button").First(button => button.TextContent.Trim() == "Edit season").Click();
        cut.Find("tbody button").Click();

        // The stale completion must not publish its payload or clear state.
        staleSetup.SetResult(new ServiceResult<CampaignCreationSetupResult>(new CampaignCreationSetupResult
        {
            Seasons =
            [
                new CampaignSeasonChoice
                {
                    SeasonId = 99,
                    Name = "Stale Season",
                    StartDate = new DateOnly(2020, 1, 1),
                    EndDate = null
                }
            ],
            TotalSeasonCount = 1,
            ActivePlayerCount = 0,
            ActiveTeamCount = 0
        }));
        Thread.Sleep(150);
        cut.Markup.ShouldNotContain("Stale Season");

        freshSetup.SetResult(SuccessSetupResult());
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Edit campaign metadata");
            cut.Markup.ShouldContain("Summer 2026");
            cut.Markup.ShouldNotContain("Stale Season");
        });
    }

    [Fact]
    public void Campaigns_ClearsStaleSetupError_WhenSwitchingToSeasonEdit()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateSeasonGroups())));
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignCreationSetupResult>(ServiceProblem.ServerError("Season load failed."))));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Season load failed."));

        cut.FindAll("button").First(button => button.TextContent.Trim() == "Edit season").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Edit season metadata");
            cut.Markup.ShouldNotContain("Season load failed.");
        });
    }

    [Fact]
    public void Campaigns_OffersCloseAndReload_WhenUpdateReturnsConflict()
    {
        var metadataService = Substitute.For<ICampaignMetadataService>();
        metadataService.UpdateAsync(Arg.Any<UpdateCampaignMetadataInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<UpdateCampaignMetadataResult>(
                ServiceProblem.Conflict("The campaign is Closed. Reopen the campaign before editing its metadata."))));

        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(CreateSeasonGroups())));
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessSetupResult()));

        RegisterServices(isClubAdmin: true, queryService: queryService, metadataService: metadataService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Summer Tryouts"));

        cut.Find("tbody button").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit campaign metadata"));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Close and reload"));

        cut.Find("button.btn-outline-warning").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldNotContain("Edit campaign metadata");
            cut.Markup.ShouldNotContain("Close and reload");
        });
    }

    [Fact]
    public void Campaigns_DoesNotRetainFallbackSeason_InLaterEdits()
    {
        var groups = new[]
        {
            new CampaignSeasonGroup
            {
                SeasonId = 5,
                Name = "Summer 2026",
                StartDate = new DateOnly(2026, 6, 1),
                EndDate = new DateOnly(2026, 8, 31),
                Campaigns =
                [
                    new CampaignListItem
                    {
                        CampaignId = 10,
                        Name = "Summer Tryouts",
                        StartDate = new DateOnly(2026, 6, 15),
                        PlannedEndDate = new DateOnly(2026, 6, 20),
                        Status = CampaignStatus.Active,
                        ParticipantCount = 12,
                        UnresolvedCount = 3
                    }
                ]
            },
            new CampaignSeasonGroup
            {
                SeasonId = 6,
                Name = "Old 2020",
                StartDate = new DateOnly(2020, 1, 1),
                EndDate = new DateOnly(2020, 6, 30),
                Campaigns =
                [
                    new CampaignListItem
                    {
                        CampaignId = 12,
                        Name = "Legacy Cup",
                        StartDate = new DateOnly(2020, 2, 1),
                        PlannedEndDate = null,
                        Status = CampaignStatus.Active,
                        ParticipantCount = 8,
                        UnresolvedCount = 0
                    }
                ]
            }
        };

        // The bounded setup payload omits the older season.
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessListResult(groups)));
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessSetupResult()));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<CampaignsPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Legacy Cup"));

        // Editing the out-of-window campaign prepends its current season for that edit only.
        cut.FindAll("tbody button").First(button => button.GetAttribute("aria-label") == "Edit campaign Legacy Cup in Old 2020").Click();
        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Edit campaign metadata");
            cut.Markup.ShouldContain("Old 2020");
        });
        cut.FindAll("button").First(button => button.TextContent.Trim() == "Cancel").Click();

        // A later edit of an in-window campaign must not retain the fallback season.
        cut.FindAll("tbody button").First(button => button.GetAttribute("aria-label") == "Edit campaign Summer Tryouts in Summer 2026").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Edit campaign metadata"));
        var formMarkup = cut.Find("#edit-campaign-season").InnerHtml;
        formMarkup.ShouldContain("Summer 2026");
        formMarkup.ShouldNotContain("Old 2020");
    }

    [Fact]
    public void CampaignCreateForm_ShowsValidationMessages_WhenSubmittedInvalid()
    {
        var model = new CampaignCreateFormState
        {
            OperationId = Guid.CreateVersion7(),
            Name = string.Empty,
            StartDate = new DateOnly(2026, 6, 1),
            UseInlineSeason = false,
            ExistingSeasonId = 5
        };

        var cut = Render<CampaignCreateForm>(parameters => parameters
            .Add(component => component.Heading, "Campaign details")
            .Add(component => component.Model, model)
            .Add(component => component.Seasons, CreateSeasonChoices()));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("The Name field is required."));
    }

    [Fact]
    public void CampaignCreateForm_ShowsSeasonChoiceError_WhenNoSeasonIsSpecified()
    {
        var model = new CampaignCreateFormState
        {
            OperationId = Guid.CreateVersion7(),
            Name = "Summer Tryouts",
            StartDate = new DateOnly(2026, 6, 1),
            UseInlineSeason = false,
            ExistingSeasonId = null
        };

        var cut = Render<CampaignCreateForm>(parameters => parameters
            .Add(component => component.Heading, "Campaign details")
            .Add(component => component.Model, model)
            .Add(component => component.Seasons, CreateSeasonChoices()));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Specify exactly one season choice"));
    }

    [Fact]
    public void CampaignCreateForm_ShowsInlineSeasonFields_WhenInlineModeSelected()
    {
        var model = new CampaignCreateFormState
        {
            OperationId = Guid.CreateVersion7(),
            Name = "Summer Tryouts",
            StartDate = new DateOnly(2026, 6, 1)
        };

        var cut = Render<CampaignCreateForm>(parameters => parameters
            .Add(component => component.Heading, "Campaign details")
            .Add(component => component.Model, model)
            .Add(component => component.Seasons, CreateSeasonChoices()));

        cut.Find("#season-mode-inline").Change("True");
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("inline-season-name"));
    }

    [Fact]
    public void NewCampaign_ShowsPreviewCountsAndExplainer_WhenSetupLoads()
    {
        RegisterServices(isClubAdmin: true);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Current enrollment preview"));

        cut.Markup.ShouldContain("34");
        cut.Markup.ShouldContain("6");
        cut.Markup.ShouldContain("active players will be enrolled as participants");
        cut.Markup.ShouldContain("active teams will be available for placement");
        cut.Markup.ShouldContain("Active");
        cut.Markup.ShouldContain("does not close the campaign");
    }

    [Fact]
    public void NewCampaign_ShowsErrorAndRetries_WhenSetupLoadFails()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(
                Task.FromResult(new ServiceResult<CampaignCreationSetupResult>(ServiceProblem.ServerError("Setup unavailable."))),
                Task.FromResult(SuccessSetupResult()));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Setup unavailable."));
        cut.Find("button.btn-outline-danger").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Current enrollment preview"));
    }

    [Fact]
    public void NewCampaign_CreatesWithInlineSeason_AndNavigatesToList()
    {
        var creationService = Substitute.For<ICampaignCreationService>();
        creationService.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CreateCampaignResult>(new CreateCampaignResult(
                Guid.CreateVersion7(),
                21,
                "Summer Tryouts",
                new DateOnly(2026, 6, 1),
                null,
                CampaignStatus.Active,
                9,
                "Summer 2026",
                new DateOnly(2026, 6, 1),
                new DateOnly(2026, 8, 31),
                SeasonCreatedInline: true,
                EnrolledPlayerCount: 34))));

        RegisterServices(isClubAdmin: true, creationService: creationService);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Current enrollment preview"));

        cut.Find("#campaign-name").Change("Summer Tryouts");
        cut.Find("#season-mode-inline").Change("True");
        cut.Find("#inline-season-name").Change("Summer 2026");
        cut.Find("#inline-season-start-date").Change("2026-06-01");
        cut.Find("#inline-season-end-date").Change("2026-08-31");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() =>
            creationService.Received().CreateAsync(
                Arg.Is<CreateCampaignInput>(input =>
                    input != null
                    && input.OperationId != Guid.Empty
                    && input.Name == "Summer Tryouts"
                    && input.ExistingSeasonId == null
                    && input.InlineSeason != null
                    && input.InlineSeason.Name == "Summer 2026"),
                Arg.Any<CancellationToken>()));

        var navigationManager = (BunitNavigationManager)Services.GetRequiredService<NavigationManager>();
        new Uri(navigationManager.Uri).AbsolutePath.ShouldBe("/campaigns");
    }

    [Fact]
    public void NewCampaign_CreatesWithExistingSeason_WhenSelected()
    {
        var creationService = Substitute.For<ICampaignCreationService>();
        creationService.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CreateCampaignResult>(new CreateCampaignResult(
                Guid.CreateVersion7(),
                22,
                "Fall ID",
                new DateOnly(2026, 6, 1),
                null,
                CampaignStatus.Active,
                5,
                "Summer 2026",
                new DateOnly(2026, 6, 1),
                null,
                SeasonCreatedInline: false,
                EnrolledPlayerCount: 34))));

        RegisterServices(isClubAdmin: true, creationService: creationService);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Current enrollment preview"));

        cut.Find("#campaign-name").Change("Fall ID");
        cut.Find("#existing-season").Change("5");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() =>
            creationService.Received().CreateAsync(
                Arg.Is<CreateCampaignInput>(input =>
                    input != null && input.ExistingSeasonId == 5 && input.InlineSeason == null),
                Arg.Any<CancellationToken>()));
    }

    [Fact]
    public void NewCampaign_ShowsConflictMessage_WhenCreationReturnsConflict()
    {
        var creationService = Substitute.For<ICampaignCreationService>();
        creationService.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CreateCampaignResult>(
                ServiceProblem.Conflict("A campaign named Summer Tryouts already exists in this season."))));

        RegisterServices(isClubAdmin: true, creationService: creationService);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Current enrollment preview"));

        cut.Find("#campaign-name").Change("Summer Tryouts");
        cut.Find("#existing-season").Change("5");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("A campaign named Summer Tryouts already exists in this season.");
            cut.Markup.ShouldNotContain("_formError");
        });
    }

    [Fact]
    public void NewCampaign_ShowsFieldErrors_WhenCreationReturnsValidationProblemWithoutDetail()
    {
        var creationService = Substitute.For<ICampaignCreationService>();
        creationService.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CreateCampaignResult>(
                ServiceProblem.Validation(new Dictionary<string, string[]>
                {
                    ["PlannedEndDate"] = ["A campaign in a finite season must have a planned end date."]
                }))));

        RegisterServices(isClubAdmin: true, creationService: creationService);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Current enrollment preview"));

        cut.Find("#campaign-name").Change("Fall ID Camp");
        cut.Find("#existing-season").Change("5");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("A campaign in a finite season must have a planned end date.");
            cut.Markup.ShouldNotContain("_formError");
        });
    }

    [Fact]
    public void NewCampaign_ReusesOperationId_WhenRetryingIdenticalPayload()
    {
        var creationService = Substitute.For<ICampaignCreationService>();
        creationService.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns<Task<ServiceResult<CreateCampaignResult>>>(_ => throw new HttpRequestException("offline"));

        RegisterServices(isClubAdmin: true, creationService: creationService);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Current enrollment preview"));

        cut.Find("#campaign-name").Change("Fall ID Camp");
        cut.Find("#existing-season").Change("5");
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Could not reach the server"));

        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() =>
        {
            var calls = creationService.ReceivedCalls()
                .Select(call => call.GetArguments()[0] as CreateCampaignInput)
                .Where(input => input is not null)
                .ToList();
            calls.Count.ShouldBe(2);
            calls[0]!.OperationId.ShouldBe(calls[1]!.OperationId);
        });
    }

    [Fact]
    public void NewCampaign_MintsNewOperationId_WhenPayloadChangesAfterFailure()
    {
        var creationService = Substitute.For<ICampaignCreationService>();
        creationService.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns<Task<ServiceResult<CreateCampaignResult>>>(_ => throw new HttpRequestException("offline"));

        RegisterServices(isClubAdmin: true, creationService: creationService);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Current enrollment preview"));

        cut.Find("#campaign-name").Change("Fall ID Camp");
        cut.Find("#existing-season").Change("5");
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Could not reach the server"));

        cut.Find("#campaign-name").Change("Fall ID Camp 2026");
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() =>
        {
            var calls = creationService.ReceivedCalls()
                .Select(call => call.GetArguments()[0] as CreateCampaignInput)
                .Where(input => input is not null)
                .ToList();
            calls.Count.ShouldBe(2);
            calls[0]!.OperationId.ShouldNotBe(calls[1]!.OperationId);
        });

        // A further identical retry of the changed payload must reuse the minted identifier,
        // never the original one. Wait for the submit flag to reset before clicking again.
        cut.WaitForAssertion(() =>
            cut.Find("button[type='submit']").HasAttribute("disabled").ShouldBeFalse());
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() =>
        {
            var calls = creationService.ReceivedCalls()
                .Select(call => call.GetArguments()[0] as CreateCampaignInput)
                .Where(input => input is not null)
                .ToList();
            calls.Count.ShouldBe(3);
            calls[2]!.OperationId.ShouldBe(calls[1]!.OperationId);
        });
    }

    [Fact]
    public void NewCampaign_MintsNewOperationId_WhenNameWithDelimiterChanges()
    {
        var creationService = Substitute.For<ICampaignCreationService>();
        creationService.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns<Task<ServiceResult<CreateCampaignResult>>>(_ => throw new HttpRequestException("offline"));

        RegisterServices(isClubAdmin: true, creationService: creationService);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Current enrollment preview"));

        cut.Find("#campaign-name").Change("Fall | 2026-07-15 | ID Camp");
        cut.Find("#existing-season").Change("5");
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Could not reach the server"));

        cut.Find("#campaign-name").Change("Fall | 2026-07-16 | ID Camp");
        cut.Find("button[type='submit']").Click();
        cut.WaitForAssertion(() =>
        {
            var calls = creationService.ReceivedCalls()
                .Select(call => call.GetArguments()[0] as CreateCampaignInput)
                .Where(input => input is not null)
                .ToList();
            calls.Count.ShouldBe(2);
            calls[0]!.OperationId.ShouldNotBe(calls[1]!.OperationId);
        });
    }

    [Fact]
    public void NewCampaign_ShowsRetryableError_WhenCreationThrowsTransportFailure()
    {
        var creationService = Substitute.For<ICampaignCreationService>();
        creationService.CreateAsync(Arg.Any<CreateCampaignInput>(), Arg.Any<CancellationToken>())
            .Returns<Task<ServiceResult<CreateCampaignResult>>>(_ => throw new HttpRequestException("offline"));

        RegisterServices(isClubAdmin: true, creationService: creationService);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Current enrollment preview"));

        cut.Find("#campaign-name").Change("Fall ID Camp");
        cut.Find("#existing-season").Change("5");
        cut.Find("button[type='submit']").Click();

        cut.WaitForAssertion(() =>
        {
            cut.Markup.ShouldContain("Could not reach the server");
            cut.Find("button[type='submit']").HasAttribute("disabled").ShouldBeFalse();
        });
    }

    [Fact]
    public void NewCampaign_ShowsSetupError_WhenSetupThrowsTransportFailure()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns<Task<ServiceResult<CampaignCreationSetupResult>>>(_ => throw new HttpRequestException("offline"));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Could not reach the server. Check your connection and retry."));
    }

    [Fact]
    public void NewCampaign_NotesSeasonTruncation_WhenMoreSeasonsExist()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new ServiceResult<CampaignCreationSetupResult>(new CampaignCreationSetupResult
            {
                Seasons = CreateSeasonChoices(),
                TotalSeasonCount = 150,
                ActivePlayerCount = 34,
                ActiveTeamCount = 6
            })));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Only the newest 1 of 150 seasons are available for"));
    }

    [Fact]
    public void NewCampaign_AllowsInlineSeason_WhenNoSeasonsExist()
    {
        var queryService = Substitute.For<ICampaignQueryService>();
        queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(SuccessSetupResult(seasons: [])));

        RegisterServices(isClubAdmin: true, queryService: queryService);

        var cut = Render<NewCampaignPage>();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("no seasons available yet"));

        cut.Find("#season-mode-existing").HasAttribute("disabled").ShouldBeTrue();
    }

    private void RegisterServices(
        bool isClubAdmin,
        ICampaignQueryService? queryService = null,
        ICampaignCreationService? creationService = null,
        ICampaignMetadataService? metadataService = null,
        ISeasonMetadataService? seasonMetadataService = null,
        IReadOnlyList<CampaignSeasonGroup>? seasonGroups = null,
        int? totalCount = null)
    {
        if (queryService is null)
        {
            queryService = Substitute.For<ICampaignQueryService>();
            queryService.GetCampaignListAsync(Arg.Any<GetCampaignListInput>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(SuccessListResult(seasonGroups ?? CreateSeasonGroups(), totalCount)));
            queryService.GetCreationSetupAsync(Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(SuccessSetupResult()));
        }

        creationService ??= Substitute.For<ICampaignCreationService>();
        metadataService ??= Substitute.For<ICampaignMetadataService>();
        seasonMetadataService ??= Substitute.For<ISeasonMetadataService>();

        Services.AddSingleton(queryService);
        Services.AddSingleton(creationService);
        Services.AddSingleton(metadataService);
        Services.AddSingleton(seasonMetadataService);
        Services.AddSingleton<AuthenticationStateProvider>(new FakeAuthenticationStateProvider(CreatePrincipal(isClubAdmin)));
    }

    private static ServiceResult<CampaignListResult> SuccessListResult(
        IReadOnlyList<CampaignSeasonGroup> seasons,
        int? totalCount = null)
        => new(new CampaignListResult
        {
            TotalCount = totalCount ?? seasons.Sum(season => season.Campaigns.Count),
            Seasons = seasons
        });

    private static ServiceResult<CampaignCreationSetupResult> SuccessSetupResult(
        IReadOnlyList<CampaignSeasonChoice>? seasons = null)
        => new(new CampaignCreationSetupResult
        {
            Seasons = seasons ?? CreateSeasonChoices(),
            TotalSeasonCount = (seasons ?? CreateSeasonChoices()).Count,
            ActivePlayerCount = 34,
            ActiveTeamCount = 6
        });

    private static IReadOnlyList<CampaignSeasonChoice> CreateSeasonChoices() =>
    [
        new CampaignSeasonChoice
        {
            SeasonId = 5,
            Name = "Summer 2026",
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 8, 31)
        }
    ];

    private static IReadOnlyList<CampaignSeasonGroup> CreateSeasonGroups() =>
    [
        new CampaignSeasonGroup
        {
            SeasonId = 5,
            Name = "Summer 2026",
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 8, 31),
            Campaigns =
            [
                new CampaignListItem
                {
                    CampaignId = 10,
                    Name = "Summer Tryouts",
                    StartDate = new DateOnly(2026, 6, 15),
                    PlannedEndDate = new DateOnly(2026, 6, 20),
                    Status = CampaignStatus.Active,
                    ParticipantCount = 12,
                    UnresolvedCount = 3
                }
            ]
        }
    ];

    private static IReadOnlyList<CampaignSeasonGroup> CreateClosedSeasonGroups() =>
    [
        new CampaignSeasonGroup
        {
            SeasonId = 4,
            Name = "Spring 2026",
            StartDate = new DateOnly(2026, 3, 1),
            EndDate = new DateOnly(2026, 5, 31),
            Campaigns =
            [
                new CampaignListItem
                {
                    CampaignId = 11,
                    Name = "Spring ID Camp",
                    StartDate = new DateOnly(2026, 3, 10),
                    PlannedEndDate = null,
                    Status = CampaignStatus.Closed,
                    ParticipantCount = 20,
                    UnresolvedCount = 0
                }
            ]
        }
    ];

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

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var gitDirectoryPath = Path.Join(directory.FullName, ".git");
            if (Directory.Exists(gitDirectoryPath) || File.Exists(gitDirectoryPath))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not locate repository root for campaign route assertion.");
    }

    /// <summary>
    /// Provides a fixed authentication state for bUnit component tests.
    /// </summary>
    /// <param name="principal">The principal to return from <see cref="GetAuthenticationStateAsync"/>.</param>
    private sealed class FakeAuthenticationStateProvider(ClaimsPrincipal principal) : AuthenticationStateProvider
    {
        /// <inheritdoc />
        public override Task<AuthenticationState> GetAuthenticationStateAsync()
            => Task.FromResult(new AuthenticationState(principal));
    }

    /// <summary>
    /// A test-only <see cref="CampaignsPage"/> subclass that seeds persisted prerender state.
    /// </summary>
    private sealed class PersistedStateCampaigns(
        ICampaignQueryService campaignQueryService,
        ICampaignMetadataService campaignMetadataService,
        ISeasonMetadataService seasonMetadataService,
        AuthenticationStateProvider authenticationStateProvider,
        NavigationManager navigationManager)
        : CampaignsPage(campaignQueryService, campaignMetadataService, seasonMetadataService, authenticationStateProvider, navigationManager)
    {
        [Parameter]
        public bool StartInitialized { get; set; }

        [Parameter]
        public IReadOnlyList<CampaignSeasonGroup>? PersistedGroups { get; set; }

        /// <inheritdoc />
        protected override Task OnInitializedAsync()
        {
            if (StartInitialized)
            {
                Initialized = true;
                PersistedList = new CampaignListResult
                {
                    TotalCount = PersistedGroups?.Sum(season => season.Campaigns.Count) ?? 0,
                    Seasons = PersistedGroups ?? []
                };
            }

            return base.OnInitializedAsync();
        }
    }
}
