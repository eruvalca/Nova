using Bunit;
using Microsoft.Extensions.DependencyInjection;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Components;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>Close evidence, explicit confirmation, authority replacement and unknown-result recovery.</summary>
public sealed partial class CampaignCloseoutPanelTests : BunitContext
{
    private readonly ICampaignLifecycleService _lifecycle = Substitute.For<ICampaignLifecycleService>();
    private CampaignLifecycleEvidence? _evidence = Evidence();
    private Action<CampaignLifecycleEvidence?>? _renderEvidence;
    private int _refreshes;

    public CampaignCloseoutPanelTests()
    {
        Services.AddSingleton(_lifecycle);
        JSInterop.Mode = JSRuntimeMode.Loose;
        _lifecycle.CloseAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new ServiceResult<Success>(new Success()));
        _lifecycle.ReopenAsync(Arg.Any<long>(), Arg.Any<CancellationToken>()).Returns(new ServiceResult<Success>(new Success()));
    }

    [Fact]
    public void ParentReadRemovesConfirmationAndRetryUntilEvidenceSettles()
    {
        var cut = Actions();
        Button(cut, "Review close").Click();
        cut.Find(".confirmation").ShouldNotBeNull();
        cut.Render(p => p.Add(x => x.Loading, true));
        cut.FindAll("button").ShouldBeEmpty();
        cut.Find("section").GetAttribute("aria-busy").ShouldBe("true");
        cut.Render(p => p.Add(x => x.Loading, false));
        cut.FindAll(".confirmation").ShouldBeEmpty();
        Button(cut, "Review close").HasAttribute("disabled").ShouldBeFalse();
        _refreshes.ShouldBe(1);
        _lifecycle.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void AnotherActiveRestrictionNamesTheClubAndLinksItsCampaign()
    {
        _evidence = Evidence(status: CampaignStatus.Closed);
        _evidence = _evidence with { Readiness = _evidence.Readiness with { Lifecycle = new(true, false, false, CampaignReopenUnavailableReason.AnotherActiveCampaign, 11) } };
        var cut = Actions();
        cut.Markup.ShouldContain("Only one campaign can be Active for this club.");
        cut.Find("a").GetAttribute("href").ShouldBe("/campaigns/11");
        cut.FindAll("button").ShouldBeEmpty();
    }

    [Theory]
    [InlineData(CampaignStatus.Active, "Close campaign")]
    [InlineData(CampaignStatus.Closed, "Reopen campaign")]
    public void ReviewRefreshesAndCancelSendsNoMutation(CampaignStatus status, string commitment)
    {
        _evidence = Evidence(status: status);
        var cut = Actions();
        Button(cut, status == CampaignStatus.Active ? "Review close" : "Review reopen").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain(commitment));
        _refreshes.ShouldBe(1);
        cut.Markup.ShouldContain("Summer Tryouts");
        cut.Markup.ShouldContain("2026 season");
        Button(cut, "Cancel").Click();
        cut.Markup.ShouldContain("No lifecycle request was sent");
        _lifecycle.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void MembersAndBlockedAdministratorsGetExplanationsWithoutMutationControls(bool admin, bool ready)
    {
        _evidence = Evidence(admin: admin, ready: ready);
        var cut = Actions();
        cut.FindAll("button").ShouldBeEmpty();
        cut.Markup.ShouldContain(admin ? "Resolve the blockers" : "A club administrator");
    }

    [Fact]
    public void ExplicitCommitSendsOneRequestAndAnnouncesSuccess()
    {
        var cut = Actions();
        Button(cut, "Review close").Click();
        Button(cut, "Close campaign").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Campaign closed."));
        _ = _lifecycle.Received(1).CloseAsync(10, Arg.Any<CancellationToken>());
        _refreshes.ShouldBe(2);
    }

    [Fact]
    public async Task DuplicateCommitAndDelayedAuthorityResponseCannotReappearAsync()
    {
        var pending = new TaskCompletionSource<ServiceResult<Success>>();
        _lifecycle.CloseAsync(10, Arg.Any<CancellationToken>()).Returns(pending.Task);
        var cut = Actions();
        await Button(cut, "Review close").ClickAsync(new());
        var saving = Button(cut, "Close campaign").ClickAsync(new());
        cut.FindAll("button").ShouldAllBe(button => button.HasAttribute("disabled"));
        cut.Render(parameters => parameters.Add(component => component.Owner, "different-user:club:campaign:2").Add(component => component.Evidence, Evidence(admin: false)));
        pending.SetResult(new ServiceResult<Success>(new Success()));
        await saving;
        cut.Markup.ShouldNotContain("Campaign closed.");
        _ = _lifecycle.Received(1).CloseAsync(10, Arg.Any<CancellationToken>());
        _refreshes.ShouldBe(1);
    }

    [Fact]
    public async Task DisposedLifecycleAttemptCannotRefreshOrPublishLateSuccessAsync()
    {
        var pending = new TaskCompletionSource<ServiceResult<Success>>();
        CancellationToken dispatchedToken = default;
        _lifecycle.CloseAsync(10, Arg.Any<CancellationToken>()).Returns(call =>
        {
            dispatchedToken = call.Arg<CancellationToken>();
            return pending.Task;
        });
        var cut = Actions();
        await Button(cut, "Review close").ClickAsync(new());
        var saving = Button(cut, "Close campaign").ClickAsync(new());
        dispatchedToken.CanBeCanceled.ShouldBeTrue();
        await DisposeComponentsAsync();
        dispatchedToken.IsCancellationRequested.ShouldBeTrue();
        pending.SetResult(new ServiceResult<Success>(new Success()));
        await saving;
        _refreshes.ShouldBe(1);
        _ = _lifecycle.Received(1).CloseAsync(10, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportFailureOrUnrelatedTimeoutDiscardsConfirmationAndRequiresNewReviewAsync(bool timeout)
    {
        _lifecycle.CloseAsync(10, Arg.Any<CancellationToken>()).Returns(Task.FromException<ServiceResult<Success>>(
            timeout ? new OperationCanceledException("Unrelated timeout") : new HttpRequestException("Lost response")));
        var cut = Actions();
        await Button(cut, "Review close").ClickAsync(new());
        await Button(cut, "Close campaign").ClickAsync(new());
        cut.Markup.ShouldContain("request outcome is unknown");
        cut.Markup.ShouldNotContain("Lifecycle confirmation");
        cut.Markup.ShouldNotContain("Campaign closed.");
        Button(cut, "Review close").HasAttribute("disabled").ShouldBeFalse();
        _refreshes.ShouldBe(2);
        _ = _lifecycle.Received(1).CloseAsync(10, Arg.Any<CancellationToken>());
    }

    [Fact]
    public void NewEvidenceInvalidatesAnOpenConfirmation()
    {
        var cut = Actions();
        Button(cut, "Review close").Click();
        cut.Render(parameters => parameters.Add(component => component.Evidence, Evidence(ready: false)));
        cut.Markup.ShouldNotContain("Lifecycle confirmation");
        _lifecycle.ReceivedCalls().ShouldBeEmpty();
    }

    [Fact]
    public void UnknownServerResultRefreshesStateWithoutReplayOrFalseSuccess()
    {
        _lifecycle.CloseAsync(10, Arg.Any<CancellationToken>()).Returns(new ServiceResult<Success>(ServiceProblem.ServerError("Lost acknowledgement")));
        var cut = Actions();
        Button(cut, "Review close").Click();
        _evidence = Evidence(status: CampaignStatus.Closed);
        Button(cut, "Close campaign").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("request outcome is unknown"));
        cut.Markup.ShouldContain("Current state:");
        cut.Markup.ShouldContain("Closed");
        cut.Markup.ShouldNotContain("Campaign closed.");
        cut.Markup.ShouldNotContain("Lifecycle confirmation");
        _ = _lifecycle.Received(1).CloseAsync(10, Arg.Any<CancellationToken>());
        _refreshes.ShouldBe(2);
    }

    [Fact]
    public void FailedPreflightLeavesActionsUnavailableUntilReadRetry()
    {
        var cut = Actions();
        _evidence = null;
        Button(cut, "Review close").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Retry lifecycle read"));
        cut.Markup.ShouldNotContain("Lifecycle confirmation");
        _lifecycle.ReceivedCalls().ShouldBeEmpty();
        _evidence = Evidence();
        Button(cut, "Retry lifecycle read").Click();
        cut.WaitForAssertion(() => cut.Markup.ShouldContain("Review close"));
    }

    [Fact]
    public void BoardKeepsZeroNeedsPlacementSeparateFromLocalOutcomesAndExactBlockerLinks()
    {
        var evidence = Evidence(ready: false);
        var queries = Substitute.For<IEffectivePlacementQueryService>();
        queries.GetCampaignEffectivePlacementsAsync(Arg.Any<GetCampaignEffectivePlacementsInput>(), Arg.Any<CancellationToken>())
            .Returns(new ServiceResult<CampaignEffectivePlacementsResult>(ServiceProblem.ServerError("Roster unavailable")));
        Services.AddSingleton(queries);
        var cut = Render<CampaignCloseoutPanel>(p => p.Add(x => x.Detail, evidence.Detail).Add(x => x.Evidence, evidence)
            .Add(x => x.Owner, "member:club:10").Add(x => x.RefreshEvidence, () => Task.FromResult<CampaignLifecycleEvidence?>(evidence))
            .Add(x => x.BuildCloseUrl, state => state.Apply("/campaigns/10?tab=close"))
            .Add(x => x.BuildParticipantUrl, id => $"/campaigns/10?tab=place&placementParticipant={id}"));
        cut.Markup.ShouldContain("0 need placement");
        cut.Markup.ShouldContain("No campaign decision 1");
        cut.Find(".close-blocker a").GetAttribute("href")!.ShouldContain("closeBlocker=outcomes");
        cut.Markup.ShouldContain("Retry roster");
        cut.Markup.ShouldContain("Work remains");
        _ = queries.Received(1).GetCampaignEffectivePlacementsAsync(Arg.Is<GetCampaignEffectivePlacementsInput>(x => x.PageSize == 50 && x.SortBy == "closeout" && x.Eligibility == null), Arg.Any<CancellationToken>());
    }

    private IRenderedComponent<CampaignLifecycleActions> Actions()
    {
        var cut = Render<CampaignLifecycleActions>(p => p.Add(x => x.Owner, "user:club:10:1").Add(x => x.Evidence, _evidence).Add(x => x.RefreshEvidence, RefreshAsync));
        _renderEvidence = evidence => cut.Render(p => p.Add(x => x.Evidence, evidence));
        return cut;
    }
    private Task<CampaignLifecycleEvidence?> RefreshAsync()
    {
        ++_refreshes;
        _renderEvidence!(_evidence);
        return Task.FromResult(_evidence);
    }
    private static AngleSharp.Dom.IElement Button(IRenderedComponent<CampaignLifecycleActions> cut, string text)
        => cut.FindAll("button").Single(button => string.Equals(button.TextContent.Trim(), text, StringComparison.Ordinal));
    private static CampaignLifecycleEvidence Evidence(bool admin = true, bool ready = true, CampaignStatus status = CampaignStatus.Active)
    {
        var detail = new CampaignDetailResult { CampaignId = 10, Name = "Summer Tryouts", SeasonId = 20, SeasonName = "2026 season", Status = status, StartDate = new(2026, 6, 1), ParticipantCount = 3 };
        var readiness = new CampaignCloseoutReadinessDto(10, status, ready, new(1, 1, ready ? 1 : 0, ready ? 0 : 1, 3), ready ? [] : [new(CloseoutBlockerConditions.Outcomes, 1, [301], "Missing outcome")])
        {
            Lifecycle = new(admin, admin && ready && status == CampaignStatus.Active, admin && status == CampaignStatus.Closed, status == CampaignStatus.Closed ? CampaignReopenUnavailableReason.None : CampaignReopenUnavailableReason.NotClosed, null),
        };
        return new("user:club:10:1", 1, detail, readiness);
    }
}
