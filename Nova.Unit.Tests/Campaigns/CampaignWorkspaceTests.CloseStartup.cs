using System.Text.Json;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using Shouldly;
using CampaignWorkspacePage = Nova.UI.Features.Campaigns.Pages.CampaignWorkspace;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignWorkspaceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseStartupResultSurvivesSerializedAttachmentWithoutRepeatingReadAsync(bool failed)
    {
        RegisterServices();
        var queries = Services.GetRequiredService<ICampaignCloseoutQueryService>();
        var ready = CreateReadiness() with { Lifecycle = new(true, true, false, CampaignReopenUnavailableReason.NotClosed, null) };
        queries.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>())
            .Returns(failed ? new ServiceResult<CampaignCloseoutReadinessDto>(ServiceProblem.ServerError("Startup unavailable")) : new(ready));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10?tab=close");
        var startup = Render<CampaignWorkspacePage>(p => p.Add(x => x.CampaignId, 10));
        var evidence = JsonSerializer.Deserialize<CampaignLifecycleEvidence>(JsonSerializer.Serialize(startup.Instance.PersistedCloseEvidence));
        var failure = JsonSerializer.Deserialize<CampaignCloseReadFailure>(JsonSerializer.Serialize(startup.Instance.PersistedCloseFailure));
        if (failed) { failure.ShouldNotBeNull(); evidence.ShouldBeNull(); }
        else { evidence.ShouldNotBeNull(); failure.ShouldBeNull(); }
        await DisposeComponentsAsync();
        queries.ClearReceivedCalls();
        var attached = Render<PersistedStateCampaignWorkspace>(p => p.Add(x => x.CampaignId, 10).Add(x => x.StartInitialized, true)
            .Add(x => x.PersistedCampaignDetail, CreateDetail()).Add(x => x.SeedCloseEvidence, evidence).Add(x => x.SeedCloseFailure, failure));
        _ = queries.DidNotReceive().GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>());
        attached.Find(".lifecycle-checkpoint button").TextContent.ShouldBe(failed ? "Retry lifecycle read" : "Review close");
        if (!failed) { return; }
        attached.Find(".close-review [role=alert]").TextContent.ShouldContain(failure!.Message);
        queries.GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>()).Returns(new ServiceResult<CampaignCloseoutReadinessDto>(ready));
        await attached.Find(".lifecycle-checkpoint button").ClickAsync(new MouseEventArgs());
        attached.Find(".lifecycle-checkpoint button").TextContent.ShouldBe("Review close");
        attached.Markup.ShouldNotContain(failure.Message);
        attached.Instance.PersistedCloseFailure.ShouldBeNull();
        attached.Instance.PersistedCloseEvidence.ShouldNotBeNull();
        _ = queries.Received(1).GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>());
        Services.GetRequiredService<ICampaignLifecycleService>().ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData("user")]
    [InlineData("club")]
    [InlineData("role")]
    [InlineData("campaign")]
    [InlineData("generation")]
    [InlineData("detail")]
    [InlineData("lifecycle")]
    public void CloseStartupRejectsFailureFromDifferentOwnerOrDetail(string difference)
    {
        RegisterServices();
        var owner = difference switch
        {
            "user" => "102:42:False:10:1",
            "club" => "101:43:False:10:1",
            "role" => "101:42:True:10:1",
            "campaign" => "101:42:False:11:1",
            "generation" => "101:42:False:10:2",
            _ => "101:42:False:10:1",
        };
        var detail = difference switch
        {
            "detail" => CreateDetail("Previous name"),
            "lifecycle" => CreateDetail(status: CampaignStatus.Closed),
            _ => CreateDetail(),
        };
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10?tab=close");
        var cut = Render<PersistedStateCampaignWorkspace>(p => p.Add(x => x.CampaignId, 10).Add(x => x.StartInitialized, true)
            .Add(x => x.PersistedCampaignDetail, CreateDetail())
            .Add(x => x.SeedCloseFailure, new CampaignCloseReadFailure(owner, 7, detail, "Obsolete readiness failure")));
        cut.Markup.ShouldNotContain("Obsolete readiness failure");
        cut.Instance.PersistedCloseFailure.ShouldBeNull();
        cut.Instance.PersistedCloseEvidence.ShouldNotBeNull();
        _ = Services.GetRequiredService<ICampaignCloseoutQueryService>().Received(1)
            .GetCloseoutReadinessAsync(Arg.Any<GetCampaignCloseoutReadinessInput>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void CloseNavigationPreservesOtherDestinationsWithoutUsingTheirFilters()
    {
        RegisterServices();
        var navigation = Services.GetRequiredService<NavigationManager>();
        navigation.NavigateTo("/campaigns/10?tab=close&search=roster&participant=301&placementSearch=place&placementPage=4&placementOutcome=assigned&placementTeamId=21&placementParticipant=302&evalSearch=evaluate&evalPage=2&closeSearch=close&closePage=3");
        var cut = Render<CampaignWorkspacePage>(p => p.Add(x => x.CampaignId, 10));
        cut.Find("#close-blocker").Change("eligibility");
        var query = QueryHelpers.ParseQuery(new Uri(navigation.Uri).Query);
        query["tab"].ToString().ShouldBe("close");
        query["search"].ToString().ShouldBe("roster");
        query["participant"].ToString().ShouldBe("301");
        query["placementSearch"].ToString().ShouldBe("place");
        query["placementPage"].ToString().ShouldBe("4");
        query["placementOutcome"].ToString().ShouldBe("assigned");
        query["placementTeamId"].ToString().ShouldBe("21");
        query["placementParticipant"].ToString().ShouldBe("302");
        query["evalSearch"].ToString().ShouldBe("evaluate");
        query["evalPage"].ToString().ShouldBe("2");
        query["closeSearch"].ToString().ShouldBe("close");
        query["closeBlocker"].ToString().ShouldBe("eligibility");
        query.ContainsKey("closePage").ShouldBeFalse();
        _ = Services.GetRequiredService<IEffectivePlacementQueryService>().Received(1)
            .GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(x => string.Equals(x.SortBy, "closeout", StringComparison.Ordinal)
                && string.Equals(x.CloseoutBlocker, "eligibility", StringComparison.Ordinal) && string.Equals(x.Search, "close", StringComparison.Ordinal) && x.Page == 1 && x.PageSize == 50
                && x.LocalOutcome == null && x.LocalTeamId == null && x.ParticipantId == null && x.TeamId == null
                && x.GraduationYears == null && x.TagDefinitionIds == null), Arg.Any<CancellationToken>());
    }
}
