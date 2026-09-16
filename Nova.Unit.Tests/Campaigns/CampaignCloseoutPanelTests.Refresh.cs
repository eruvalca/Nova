using Bunit;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.UI.Features.Campaigns.Services;
using NSubstitute;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignCloseoutPanelTests
{
    [Fact]
    public void SuccessfulParentRefreshRestoresActionsWithoutRedundantRetry()
    {
        var cut = Actions();
        _evidence = null;
        Button(cut, "Review close").Click();
        cut.Markup.ShouldContain("Retry lifecycle read");
        cut.Markup.ShouldContain("The required review could not be refreshed");
        cut.Render(p => p.Add(x => x.Evidence, Evidence()));
        Button(cut, "Review close").HasAttribute("disabled").ShouldBeFalse();
        cut.Markup.ShouldNotContain("Retry lifecycle read");
        cut.Markup.ShouldNotContain("The required review could not be refreshed");
        _refreshes.ShouldBe(1);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void FreshEvidenceClearsReadFailureButRetainsMutationOutcome(bool unknown, bool parentRefresh)
    {
        _lifecycle.CloseAsync(10, Arg.Any<CancellationToken>()).Returns(unknown
            ? new ServiceResult<Success>(ServiceProblem.ServerError("Unknown")) : new ServiceResult<Success>(new Success()));
        var cut = Actions();
        Button(cut, "Review close").Click();
        _evidence = null;
        Button(cut, "Close campaign").Click();
        var message = unknown ? "request outcome is unknown" : "Campaign closed.";
        cut.Markup.ShouldContain(message);
        cut.Markup.ShouldContain("Current state is unavailable");
        _evidence = Evidence(status: unknown ? Nova.SharedKernel.Enums.CampaignStatus.Active : Nova.SharedKernel.Enums.CampaignStatus.Closed);
        if (parentRefresh) { cut.Render(p => p.Add(x => x.Evidence, _evidence)); }
        else { Button(cut, "Retry lifecycle read").Click(); }
        cut.Markup.ShouldContain(message);
        cut.Markup.ShouldNotContain("Current state is unavailable");
        cut.Markup.ShouldNotContain("Retry lifecycle read");
        cut.FindAll(".confirmation").ShouldBeEmpty();
        _refreshes.ShouldBe(parentRefresh ? 2 : 3);
        _ = _lifecycle.Received(1).CloseAsync(10, Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DelayedPreflightCannotResurrectObsoleteConfirmationAsync(bool failed)
    {
        var cut = Actions();
        var obsolete = _evidence;
        var pending = new TaskCompletionSource<CampaignLifecycleEvidence?>();
        cut.Render(p => p.Add(x => x.RefreshEvidence, () => pending.Task));
        var reviewing = Button(cut, "Review close").ClickAsync(new());
        cut.Render(p => p.Add(x => x.Evidence, Evidence()));
        pending.SetResult(failed ? null : obsolete);
        await reviewing;
        cut.Markup.ShouldNotContain("Lifecycle confirmation");
        cut.Markup.ShouldContain("The campaign changed");
        cut.Markup.ShouldNotContain("Retry lifecycle read");
        Button(cut, "Review close").HasAttribute("disabled").ShouldBeFalse();
        _lifecycle.ReceivedCalls().ShouldBeEmpty();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObsoleteRecoveryReadCannotDisableNewerEvidenceAsync(bool postCommit)
    {
        var cut = Actions();
        if (postCommit)
        {
            await Button(cut, "Review close").ClickAsync(new());
            _lifecycle.CloseAsync(10, Arg.Any<CancellationToken>())
                .Returns(new ServiceResult<Success>(ServiceProblem.ServerError("Unknown")));
        }
        else
        {
            _evidence = null;
            await Button(cut, "Review close").ClickAsync(new());
        }
        var pending = new TaskCompletionSource<CampaignLifecycleEvidence?>();
        cut.Render(p => p.Add(x => x.RefreshEvidence, () => pending.Task));
        var reading = Button(cut, postCommit ? "Close campaign" : "Retry lifecycle read").ClickAsync(new());
        cut.Render(p => p.Add(x => x.Evidence, Evidence()));
        pending.SetResult(null);
        await reading;
        cut.Markup.ShouldNotContain("Retry lifecycle read");
        cut.Markup.ShouldNotContain("Lifecycle confirmation");
        Button(cut, "Review close").HasAttribute("disabled").ShouldBeFalse();
        if (postCommit) { cut.Markup.ShouldContain("request outcome is unknown"); }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DisposedPreflightOrDispatchCancellationDoesNotPublishRecoveryAsync(bool dispatch)
    {
        var cut = Actions();
        var refresh = new TaskCompletionSource<CampaignLifecycleEvidence?>();
        var command = new TaskCompletionSource<ServiceResult<Success>>();
        if (dispatch)
        {
            await Button(cut, "Review close").ClickAsync(new());
            _lifecycle.CloseAsync(10, Arg.Any<CancellationToken>()).Returns(command.Task);
        }
        else { cut.Render(p => p.Add(x => x.RefreshEvidence, () => refresh.Task)); }
        var action = Button(cut, dispatch ? "Close campaign" : "Review close").ClickAsync(new());
        await DisposeComponentsAsync();
        if (dispatch) { command.SetCanceled(Xunit.TestContext.Current.CancellationToken); } else { refresh.SetCanceled(Xunit.TestContext.Current.CancellationToken); }
        // ComponentBase treats an owned canceled event as canceled rendering, not a recovery render.
        await action;
        _refreshes.ShouldBe(dispatch ? 1 : 0);
    }
}
