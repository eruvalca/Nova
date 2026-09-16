using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Components;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class CampaignCloseRosterTests : BunitContext
{
    private readonly IEffectivePlacementQueryService _queries = Substitute.For<IEffectivePlacementQueryService>();

    public CampaignCloseRosterTests()
    {
        Services.AddSingleton(_queries);
        _queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(new CampaignEffectivePlacementsResult(new(10, "Campaign", CampaignStatus.Active, new(20, "Season")), new(0, 0, 0, 0), new([], 1, 50, 0))));
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public void MatchingStartupPageOrErrorRestoresWithoutAnotherRead(bool error)
    {
        var cut = Render<RestoredCloseRoster>(p => p.Add(x => x.CampaignId, 10).Add(x => x.Owner, "member:club:10:1")
            .Add(x => x.RestoreError, error).Add(x => x.BuildCloseUrl, _ => "/campaigns/10?tab=close")
            .Add(x => x.BuildParticipantUrl, _ => "/campaigns/10?tab=place"));
        cut.Markup.ShouldContain(error ? "Startup roster unavailable" : "No participants match");
        _queries.ReceivedCalls().ShouldBeEmpty();
        cut.Render(p => p.Add(x => x.Owner, "member:other-club:10:2"));
        _ = _queries.Received(1).GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
        cut.Markup.ShouldNotContain("Startup roster unavailable");
        cut.Render(p => p.Add(x => x.State, new CampaignWorkspaceCloseState { Search = "new search" }));
        _ = _queries.Received(1).GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(x => x.Search == "new search"), Arg.Any<CancellationToken>());
    }

    [Fact]
    public void InheritedNotSelectedOutcomeUsesReadableCopy()
    {
        var source = new PlacementDecisionSource(new(91, 30, 9, 20, 1, PlacementOutcome.NotSelected,
            null, DateTimeOffset.UnixEpoch, 40, "Member", Guid.NewGuid()), "Prior", null);
        var row = new CampaignEffectivePlacementItem(101, 30, "Alex", "Player", 2030, 1, LifecycleStatus.Active,
            Guid.NewGuid(), null, source, null, EffectivePlacementEligibility.NeedsPlacement, PlacementCorrectionReason.None);
        _queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(new CampaignEffectivePlacementsResult(
                new(10, "Campaign", CampaignStatus.Active, new(20, "Season")), new(1, 0, 0, 0), new([row], 1, 50, 1))));
        var cut = Render<CampaignCloseRoster>(p => p.Add(x => x.CampaignId, 10).Add(x => x.Owner, "member:club:10:1")
            .Add(x => x.BuildCloseUrl, _ => "/campaigns/10?tab=close").Add(x => x.BuildParticipantUrl, _ => "/campaigns/10?tab=place"));
        cut.Markup.ShouldContain("Inherited not selected; still needs a local outcome");
        cut.Markup.ShouldNotContain("notselected");
    }

    [Fact]
    public async Task DisposedRosterRetryDoesNotPublishAnUnavailableResultAsync()
    {
        _queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.ServerError("Unavailable")));
        var cut = Render<CampaignCloseRoster>(p => p.Add(x => x.CampaignId, 10).Add(x => x.Owner, "member:club:10:1")
            .Add(x => x.BuildCloseUrl, _ => "/campaigns/10?tab=close").Add(x => x.BuildParticipantUrl, _ => "/campaigns/10?tab=place"));
        var pending = new TaskCompletionSource<ServiceResult<CampaignEffectivePlacementsResult>>();
        CancellationToken requestToken = default;
        _queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(call => { requestToken = call.Arg<CancellationToken>(); return pending.Task; });
        var retry = cut.FindAll("button").Single(x => string.Equals(x.TextContent, "Retry roster", StringComparison.Ordinal)).ClickAsync(new());
        await DisposeComponentsAsync();
        requestToken.IsCancellationRequested.ShouldBeTrue();
        pending.SetCanceled(requestToken);
        await retry;
        _ = _queries.Received(2).GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>());
    }

#pragma warning disable CA1812 // bUnit constructs this startup-state test component through reflection.
    internal sealed class RestoredCloseRoster(IEffectivePlacementQueryService queries) : CampaignCloseRoster(queries)
#pragma warning restore CA1812
    {
        [Parameter] public bool RestoreError { get; set; }
        protected override void OnInitialized()
        {
            Initialized = true;
            PersistedKey = $"{Owner}:{CampaignId}:{State}";
            PersistedError = RestoreError ? "Startup roster unavailable" : null;
            PersistedPage = RestoreError ? null : new([], 1, 50, 0);
        }
    }
}
