using System.Security.Claims;
using Bunit;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.JSInterop;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using NSubstitute;
using Shouldly;
using CampaignWorkspacePage = Nova.UI.Features.Campaigns.Pages.CampaignWorkspace;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class CampaignWorkspaceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("user", "102:42:False")]
    [InlineData("club", "101:43:False")]
    [InlineData("role", "101:42:True")]
    public async Task OpeningReceiptIsReadOnceForEachNewAuthorityScopeAsync(string change, string nextScope)
    {
        var authentication = new ChangingPlacementAuthenticationStateProvider(CreatePrincipal());
        RegisterServices(authenticationStateProvider: authentication);
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10/roster");
        var module = JSInterop.SetupModule(WorkspaceModulePath);
        module.Mode = JSRuntimeMode.Loose;
        var oldOperation = Guid.NewGuid();
        var nextOperation = Guid.NewGuid();
        module.Setup<OpenCampaignResult?>("readOpeningReceipt", call => Equals(call.Arguments[0], "101:42:False"))
            .SetResult(new(oldOperation, 10, DateTimeOffset.UtcNow, 101, 1, 0, []));
        module.Setup<OpenCampaignResult?>("readOpeningReceipt", call => Equals(call.Arguments[0], nextScope))
            .SetResult(new(nextOperation, 10, DateTimeOffset.UtcNow, 101, 7, 0, []));
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Campaign opened and enrolled 1 player."));
        var claims = new List<Claim>
        {
            new(ClaimTypes.NameIdentifier, string.Equals(change, "user", StringComparison.Ordinal) ? "102" : "101"),
            new(NovaClaimTypes.ClubId, string.Equals(change, "club", StringComparison.Ordinal) ? "43" : "42")
        };
        if (string.Equals(change, "role", StringComparison.Ordinal)) { claims.Add(new(ClaimTypes.Role, Roles.ClubAdmin)); }
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
        await cut.InvokeAsync(() => authentication.Change(principal));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Campaign opened and enrolled 7 players."));
        await cut.InvokeAsync(() => authentication.Change(principal));
        cut.Markup.ShouldNotContain("Campaign opened and enrolled 1 player.");
        var reads = module.Invocations.Where(call => string.Equals(call.Identifier, "readOpeningReceipt", StringComparison.Ordinal)).ToList();
        reads.Count.ShouldBe(2);
        reads.Select(call => call.Arguments[0]).ShouldBe(["101:42:False", nextScope]);
        var acknowledgements = module.Invocations.Where(call => string.Equals(call.Identifier, "acknowledgeOpeningReceipt", StringComparison.Ordinal)).ToList();
        acknowledgements.Count.ShouldBe(2);
        acknowledgements[0].Arguments[2].ShouldBe(oldOperation);
        acknowledgements[1].Arguments[0].ShouldBe(nextScope);
        acknowledgements[1].Arguments[2].ShouldBe(nextOperation);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("readOpeningReceipt", false)]
    [InlineData("focus", false)]
    [InlineData("acknowledgeOpeningReceipt", false)]
    [InlineData("readOpeningReceipt", true)]
    [InlineData("acknowledgeOpeningReceipt", true)]
    public async Task ObsoleteOpeningReceiptCannotContinueUnderNewAuthorityAsync(string stage, bool fails)
    {
        var authentication = new ChangingPlacementAuthenticationStateProvider(CreatePrincipal());
        RegisterServices(authenticationStateProvider: authentication);
        var module = JSInterop.SetupModule(WorkspaceModulePath);
        module.Mode = JSRuntimeMode.Loose;
        var oldOperation = Guid.NewGuid();
        var nextOperation = Guid.NewGuid();
        module.Setup<OpenCampaignResult?>("readOpeningReceipt", call => Equals(call.Arguments[0], "101:42:False"))
            .SetResult(new(oldOperation, 10, DateTimeOffset.UtcNow, 101, 1, 0, []));
        module.Setup<OpenCampaignResult?>("readOpeningReceipt", call => Equals(call.Arguments[0], "101:42:True"))
            .SetResult(new(nextOperation, 10, DateTimeOffset.UtcNow, 101, 7, 0, []));
        var runtime = JSInterop.JSRuntime;
        var reference = await runtime.InvokeAsync<IJSObjectReference>("import", Xunit.TestContext.Current.CancellationToken, WorkspaceModulePath);
        await using var gate = new OpeningReceiptGate(reference, stage);
        Services.AddSingleton<IJSRuntime>(new OpeningReceiptRuntime(runtime, gate));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10/roster");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => gate.IsEntered.ShouldBeTrue());
        var detail = new TaskCompletionSource<ServiceResult<CampaignDetailResult>>(TaskCreationOptions.RunContinuationsAsynchronously);
        Services.GetRequiredService<ICampaignQueryService>()
            .GetCampaignDetailAsync(Arg.Any<GetCampaignDetailInput>(), Arg.Any<CancellationToken>()).Returns(detail.Task);
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(isClubAdmin: true)));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Loading campaign..."));
        var attachments = module.Invocations.Count(call => string.Equals(call.Identifier, "attachRosterActivationSuppression", StringComparison.Ordinal));
        await cut.InvokeAsync(() => gate.Release(fails ? new JSException("Storage unavailable") : null));
        cut.Markup.ShouldNotContain("Campaign opened and enrolled");
        module.Invocations.Count(call => string.Equals(call.Identifier, "attachRosterActivationSuppression", StringComparison.Ordinal)).ShouldBe(attachments);
        await cut.InvokeAsync(() => detail.SetResult(CreateDetail()));
        await cut.WaitForAssertionAsync(() => module.Invocations.Any(call => string.Equals(call.Identifier, "acknowledgeOpeningReceipt", StringComparison.Ordinal)
            && Equals(call.Arguments[2], nextOperation)).ShouldBeTrue());
        cut.Markup.ShouldContain("Campaign opened and enrolled 7 players.");
        cut.Markup.ShouldNotContain("Campaign opened and enrolled 1 player.");
        var oldAcknowledgements = module.Invocations.Count(call => string.Equals(call.Identifier, "acknowledgeOpeningReceipt", StringComparison.Ordinal)
            && Equals(call.Arguments[2], oldOperation));
        oldAcknowledgements.ShouldBe(string.Equals(stage, "acknowledgeOpeningReceipt", StringComparison.Ordinal) ? 1 : 0);
    }

    [Fact]
    public async Task ReturningToTheOriginalAuthorityDoesNotReviveAnOlderReceiptReadAsync()
    {
        var authentication = new ChangingPlacementAuthenticationStateProvider(CreatePrincipal());
        RegisterServices(authenticationStateProvider: authentication);
        var module = JSInterop.SetupModule(WorkspaceModulePath);
        module.Mode = JSRuntimeMode.Loose;
        var originalOperation = Guid.NewGuid();
        module.Setup<OpenCampaignResult?>("readOpeningReceipt", call => Equals(call.Arguments[0], "101:42:False"))
            .SetResult(new(originalOperation, 10, DateTimeOffset.UtcNow, 101, 1, 0, []));
        module.Setup<OpenCampaignResult?>("readOpeningReceipt", call => Equals(call.Arguments[0], "101:42:True"))
            .SetResult(new(Guid.NewGuid(), 10, DateTimeOffset.UtcNow, 101, 7, 0, []));
        var runtime = JSInterop.JSRuntime;
        var reference = await runtime.InvokeAsync<IJSObjectReference>("import", Xunit.TestContext.Current.CancellationToken, WorkspaceModulePath);
        await using var gate = new OpeningReceiptGate(reference, "readOpeningReceipt");
        Services.AddSingleton<IJSRuntime>(new OpeningReceiptRuntime(runtime, gate));
        Services.GetRequiredService<NavigationManager>().NavigateTo("/campaigns/10/roster");
        var cut = Render<CampaignWorkspacePage>(parameters => parameters.Add(component => component.CampaignId, 10));
        await cut.WaitForAssertionAsync(() => gate.IsEntered.ShouldBeTrue());
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(isClubAdmin: true)));
        await cut.WaitForAssertionAsync(() => cut.Markup.ShouldContain("Campaign opened and enrolled 7 players."));
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal()));
        await cut.WaitForAssertionAsync(() => module.Invocations.Count(call => string.Equals(call.Identifier, "acknowledgeOpeningReceipt", StringComparison.Ordinal)).ShouldBe(2));
        await cut.InvokeAsync(() => gate.Release());
        cut.Markup.ShouldContain("Campaign opened and enrolled 1 player.");
        module.Invocations.Count(call => string.Equals(call.Identifier, "readOpeningReceipt", StringComparison.Ordinal)).ShouldBe(3);
        module.Invocations.Count(call => string.Equals(call.Identifier, "acknowledgeOpeningReceipt", StringComparison.Ordinal)
            && Equals(call.Arguments[2], originalOperation)).ShouldBe(1);
    }

    private sealed class OpeningReceiptRuntime(IJSRuntime inner, OpeningReceiptGate gate) : IJSRuntime
    {
        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
            => string.Equals(identifier, "import", StringComparison.Ordinal) && args is { Length: > 0 }
                && Equals(args[0], WorkspaceModulePath)
                    ? ValueTask.FromResult((TValue)(object)gate)
                    : inner.InvokeAsync<TValue>(identifier, cancellationToken, args);
    }

    private sealed class OpeningReceiptGate(IJSObjectReference inner, string stage) : IJSObjectReference
    {
        private bool _used;
        private bool _disposed;
        private Action<Exception?>? _release;
        private Action? _cancel;
        private SynchronizationContext? _context;
        public bool IsEntered => _release is not null;

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, object?[]? args)
            => InvokeAsync<TValue>(identifier, CancellationToken.None, args);

        public ValueTask<TValue> InvokeAsync<TValue>(string identifier, CancellationToken cancellationToken, object?[]? args)
        {
            var result = inner.InvokeAsync<TValue>(identifier, cancellationToken, args);
            if (_used || !string.Equals(identifier, stage, StringComparison.Ordinal)) { return result; }
            _used = true;
            return new(HoldAsync(result));
        }

        private async Task<TValue> HoldAsync<TValue>(ValueTask<TValue> configuredResult)
        {
            if (!configuredResult.IsCompletedSuccessfully) { throw new InvalidOperationException("Configure the bUnit result before gating the invocation."); }
            var value = await configuredResult;
            // Intentionally inline: Release runs on the captured renderer context and drains the old
            // callback through its completed interop calls before negative assertions inspect it.
            var completion = new TaskCompletionSource<TValue>();
            _context = SynchronizationContext.Current;
            _cancel = () => { completion.TrySetCanceled(); };
            _release = error =>
            {
                if (error is null) { completion.SetResult(value); }
                else { completion.SetException(error); }
            };
            return await completion.Task;
        }

        public void Release(Exception? error = null)
        {
            if (!ReferenceEquals(_context, SynchronizationContext.Current)) { throw new InvalidOperationException("Release on the original renderer context."); }
            var release = _release ?? throw new InvalidOperationException("No invocation is pending.");
            _release = null;
            _cancel = null;
            release(error);
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) { return; }
            _disposed = true;
            _cancel?.Invoke();
            await inner.DisposeAsync();
        }
    }
}
