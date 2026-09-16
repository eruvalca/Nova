using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Components;
using NSubstitute;
using OneOf.Types;
using Shouldly;
using CampaignWorkspacePage = Nova.UI.Features.Campaigns.Pages.CampaignWorkspace;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignWorkspaceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(200)]
    [InlineData(201)]
    public void CloseBookmarkSearchIsBoundedBeforeQuerying(int length)
    {
        RegisterServices();
        var search = new string('a', length);
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/campaigns/10?tab=close&closeSearch={search}");
        var cut = Render<CampaignWorkspacePage>(p => p.Add(x => x.CampaignId, 10));
        var expected = length <= CampaignRosterDiscoveryInput.MaximumSearchLength ? search : null;
        _ = Services.GetRequiredService<IEffectivePlacementQueryService>().Received(1)
            .GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(x => x.SortBy == "closeout" && x.Search == expected), Arg.Any<CancellationToken>());
        cut.Find("#close-search").GetAttribute("maxlength").ShouldBe("200");
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("unknown", null)]
    [InlineData("ARCHIVEDTEAMS", "archivedTeams")]
    [InlineData("%20Eligibility%20", "eligibility")]
    public void CloseBookmarkNormalizesBlockerBeforeQuerying(string query, string? expected)
    {
        RegisterServices();
        Services.GetRequiredService<NavigationManager>().NavigateTo($"/campaigns/10?tab=close&closeBlocker={query}");
        var cut = Render<CampaignWorkspacePage>(p => p.Add(x => x.CampaignId, 10));
        _ = Services.GetRequiredService<IEffectivePlacementQueryService>().Received(1)
            .GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(x => x.SortBy == "closeout" && x.CloseoutBlocker == expected), Arg.Any<CancellationToken>());
        cut.Find("#close-blocker").GetAttribute("value").ShouldBe(expected);
    }

    [Fact]
    public async Task ReturningFromEvaluateLoadsEachRosterOnlyOnceAsync()
    {
        RegisterServices();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10?tab=evaluate");
        var cut = Render<CampaignWorkspacePage>(p => p.Add(x => x.CampaignId, 10));
        var queries = Services.GetRequiredService<IEffectivePlacementQueryService>();
        queries.ClearReceivedCalls();
        await cut.InvokeAsync(() => navigation.NavigateTo("/campaigns/10?tab=close"));
        await cut.WaitForAssertionAsync(() => cut.Find("#close-roster-heading").TextContent.ShouldBe("Campaign roster"));
        _ = queries.Received(1).GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(x => x.SortBy == "closeout"), Arg.Any<CancellationToken>());
        _ = queries.Received(1).GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(x => x.SortBy != "closeout"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CloseConfirmationUsesCompletedWorkspacePreflightAfterDelayedReadAsync()
    {
        RegisterServices();
        var readiness = CreateReadiness() with { Lifecycle = new(true, true, false, CampaignReopenUnavailableReason.NotClosed, null) };
        var queries = Services.GetRequiredService<ICampaignCloseoutQueryService>();
        queries.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>()).Returns(new ServiceResult<CampaignCloseoutReadinessDto>(readiness));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10?tab=close");
        var cut = Render<CampaignWorkspacePage>(p => p.Add(x => x.CampaignId, 10));
        var pending = new TaskCompletionSource<ServiceResult<CampaignCloseoutReadinessDto>>();
        queries.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var reviewing = cut.Find(".lifecycle-checkpoint button").ClickAsync(new MouseEventArgs());
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Refreshing or saving"));
        pending.SetResult(new ServiceResult<CampaignCloseoutReadinessDto>(readiness));
        await reviewing;
        await cut.WaitForAssertionAsync(() => cut.Find(".confirmation").TextContent.ShouldContain("Summer Tryouts"));
        cut.Markup.ShouldNotContain("_closeError");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseOutcomeSurvivesFailedRequiredRefreshWithoutReplayingAsync(bool unknown)
    {
        RegisterServices();
        var queries = Services.GetRequiredService<ICampaignCloseoutQueryService>();
        queries.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignCloseoutReadinessDto>(CreateReadiness() with { Lifecycle = new(true, true, false, CampaignReopenUnavailableReason.NotClosed, null) }));
        var lifecycle = Services.GetRequiredService<ICampaignLifecycleService>();
        lifecycle.CloseAsync(10, Arg.Any<CancellationToken>()).Returns(unknown ? new ServiceResult<Success>(ServiceProblem.ServerError("Lost acknowledgement")) : new ServiceResult<Success>(new Success()));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10?tab=close");
        var cut = Render<CampaignWorkspacePage>(p => p.Add(x => x.CampaignId, 10));
        await cut.Find(".lifecycle-checkpoint button").ClickAsync(new MouseEventArgs());
        var pending = new TaskCompletionSource<ServiceResult<CampaignDetailResult>>();
        Services.GetRequiredService<ICampaignQueryService>().GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>()).Returns(pending.Task);
        var saving = cut.Find(".confirmation .btn-primary").ClickAsync(new MouseEventArgs());
        var expected = unknown ? "request outcome is unknown" : "Campaign closed.";
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain(expected));
        pending.SetResult(new ServiceResult<CampaignDetailResult>(ServiceProblem.ServerError("Detail unavailable")));
        await saving;
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Retry lifecycle read"));
        cut.Markup.ShouldContain(expected);
        cut.Markup.ShouldNotContain("Lifecycle confirmation");
        _ = lifecycle.Received(1).CloseAsync(10, Arg.Any<CancellationToken>());
    }
}
