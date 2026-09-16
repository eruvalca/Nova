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
        cut.Render(p => p.Add(x => x.Evidence, Evidence()));
        Button(cut, "Review close").HasAttribute("disabled").ShouldBeFalse();
        cut.Markup.ShouldNotContain("Retry lifecycle read");
        _refreshes.ShouldBe(1);
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
