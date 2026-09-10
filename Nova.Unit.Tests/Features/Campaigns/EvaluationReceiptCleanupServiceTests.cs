using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nova.Features.Campaigns;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Features.Campaigns;

public sealed class EvaluationReceiptCleanupServiceTests
{
    [Fact]
    public async Task CleanupPassLogsScopeFailureAndAllowsAnotherPassAsync()
    {
        var failure = new InvalidOperationException("Administrative context registration unavailable.");
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(_ => throw failure);
        var logger = Substitute.For<ILogger<EvaluationReceiptCleanupService>>();
        logger.IsEnabled(LogLevel.Warning).Returns(true);
        using var service = new EvaluationReceiptCleanupService(scopes, logger);

        await service.RunPassAsync(TestContext.Current.CancellationToken);
        await service.RunPassAsync(TestContext.Current.CancellationToken);

        scopes.Received(2).CreateScope();
        var logged = logger.ReceivedCalls().Where(call => string.Equals(call.GetMethodInfo().Name, "Log", StringComparison.Ordinal)).ToArray();
        logged.Length.ShouldBe(2);
        foreach (var call in logged)
        {
            call.GetArguments()[0].ShouldBe(LogLevel.Warning);
            call.GetArguments()[3].ShouldBeSameAs(failure);
        }
    }

    [Fact]
    public async Task CleanupPassPropagatesShutdownCancellationWithoutWarningAsync()
    {
        using var shutdown = new CancellationTokenSource();
        await shutdown.CancelAsync();
        var scopes = Substitute.For<IServiceScopeFactory>();
        scopes.CreateScope().Returns(_ => throw new OperationCanceledException(shutdown.Token));
        var logger = Substitute.For<ILogger<EvaluationReceiptCleanupService>>();
        logger.IsEnabled(LogLevel.Warning).Returns(true);
        using var service = new EvaluationReceiptCleanupService(scopes, logger);

        var exception = await Should.ThrowAsync<OperationCanceledException>(() => service.RunPassAsync(shutdown.Token));

        exception.CancellationToken.ShouldBe(shutdown.Token);
        logger.ReceivedCalls().ShouldNotContain(call => string.Equals(call.GetMethodInfo().Name, "Log", StringComparison.Ordinal));
    }
}
