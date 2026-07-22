using Nova.UI.Components;
using Shouldly;
using Bunit;

namespace Nova.Unit.Tests.Components;

public class NovaComponentBaseTests
{
    [Fact]
    public void ComponentCancellationToken_IsNotCancelled_WhileRendered()
    {
        using var testContext = new BunitContext();
        var cut = testContext.Render<TestNovaComponent>();

        cut.Instance.GetToken().IsCancellationRequested.ShouldBeFalse();
    }

    [Fact]
    public void ComponentCancellationToken_ReturnsSameToken_OnRepeatedAccess()
    {
        using var testContext = new BunitContext();
        var cut = testContext.Render<TestNovaComponent>();

        var first = cut.Instance.GetToken();
        var second = cut.Instance.GetToken();

        second.ShouldBe(first);
    }

    [Fact]
    public async Task ComponentCancellationToken_IsCancelled_AfterDisposal()
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
    public async Task ComponentCancellationToken_AccessAfterDisposal_ReturnsCancelledTokenWithoutThrow()
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
    public async Task DisposeAsync_InvokesDisposeAsyncCoreHook()
    {
        using var testContext = new BunitContext();
        var cut = testContext.Render<TestNovaComponent>();
        var component = cut.Instance;

        await component.DisposeAsync();
        cut.Dispose();

        component.DisposeAsyncCoreInvoked.ShouldBeTrue();
    }

    private sealed class TestNovaComponent : NovaComponentBase
    {
        public bool DisposeAsyncCoreInvoked { get; private set; }

        public CancellationToken GetToken() => ComponentCancellationToken;

        protected override ValueTask DisposeAsyncCore()
        {
            DisposeAsyncCoreInvoked = true;
            return ValueTask.CompletedTask;
        }
    }
}
