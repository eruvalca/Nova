using Bunit;
using Nova.UI.Components;
using Shouldly;

namespace Nova.Unit.Tests.Components;

public class NovaComponentBaseTests
{
    [Fact]
    public void ComponentCancellationTokenIsNotCancelledWhileRendered()
    {
        using var testContext = new BunitContext();
        var cut = testContext.Render<TestNovaComponent>();

        cut.Instance.GetToken().IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public void ComponentCancellationTokenReturnsSameTokenOnRepeatedAccess()
    {
        using var testContext = new BunitContext();
        var cut = testContext.Render<TestNovaComponent>();

        var first = cut.Instance.GetToken();
        var second = cut.Instance.GetToken();

        second.ShouldBe(first);
    }

    [Fact]
    public async Task ComponentCancellationTokenIsCancelledAfterDisposalAsync()
    {
        using var testContext = new BunitContext();
        var cut = testContext.Render<TestNovaComponent>();
        var component = cut.Instance;
        var token = cut.Instance.GetToken();

        await component.DisposeAsync();
        cut.Dispose();

        token.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task ComponentCancellationTokenAccessAfterDisposalReturnsCancelledTokenWithoutThrowAsync()
    {
        using var testContext = new BunitContext();
        var cut = testContext.Render<TestNovaComponent>();
        var component = cut.Instance;

        await component.DisposeAsync();
        cut.Dispose();

        var token = Should.NotThrow(component.GetToken);

        token.IsCancellationRequested.ShouldBeTrue();
    }

    [Fact]
    public async Task DisposeAsyncInvokesDisposeAsyncCoreHookAsync()
    {
        using var testContext = new BunitContext();
        var cut = testContext.Render<TestNovaComponent>();
        var component = cut.Instance;

        await component.DisposeAsync();
        cut.Dispose();

        component.DisposeAsyncCoreInvoked.ShouldBeTrue();
    }

#pragma warning disable CA1812 // The test framework constructs this type through bUnit rendering, DI, or reflection.
    private sealed class TestNovaComponent : NovaComponentBase
#pragma warning restore CA1812
    {
        public bool DisposeAsyncCoreInvoked { get; private set; }

        public CancellationToken GetToken() => ComponentCancellationToken;

        protected override ValueTask DisposeAsyncCore()
        {
            DisposeAsyncCoreInvoked = true;
            return base.DisposeAsyncCore();
        }
    }
}
