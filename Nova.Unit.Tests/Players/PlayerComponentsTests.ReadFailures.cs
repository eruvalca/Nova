using Bunit;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
using NSubstitute;
using Shouldly;
using PlayersPage = Nova.UI.Features.Players.Pages.Players;

namespace Nova.Unit.Tests.Players;

public sealed partial class PlayerComponentsTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("roster", false, false)]
    [InlineData("summary", false, false)]
    [InlineData("tags", false, false)]
    [InlineData("roster", true, false)]
    [InlineData("summary", true, false)]
    [InlineData("tags", true, false)]
    [InlineData("roster", false, true)]
    [InlineData("summary", false, true)]
    [InlineData("tags", false, true)]
    public async Task ThrownDirectoryReadFailurePreservesNeighborsAndRetriesOnlyItsRegionAsync(string region, bool synchronous, bool wrapped)
    {
        RegisterServices(isClubAdmin: false);
        var logger = Substitute.For<ILogger<PlayersPage>>();
        logger.IsEnabled(LogLevel.Error).Returns(true);
        Services.AddSingleton(logger);
        var roster = Services.GetRequiredService<IPlayerService>();
        var tags = Services.GetRequiredService<ITagDefinitionQueryService>();
        ConfigureReadFailure(region, synchronous, wrapped, roster, tags);

        var cut = RenderPlayers();
        await cut.WaitForAssertionAsync(() => cut.FindAll("[role='alert']").Count.ShouldBe(1));
        cut.Markup.ShouldNotContain("Provider diagnostic must not reach the page");
        cut.Markup.ShouldNotContain("Your club has no players yet");
        logger.ReceivedCalls().ShouldContain(call => string.Equals(call.GetMethodInfo().Name, "Log", StringComparison.Ordinal)
            && (wrapped ? call.GetArguments()[3] is Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException
                : call.GetArguments()[3] is Npgsql.NpgsqlException));
        if (!string.Equals(region, "roster", StringComparison.Ordinal))
        {
            cut.Find("tbody .player-record-link").TextContent.ShouldBe("Avery Johnson");
        }
        if (!string.Equals(region, "summary", StringComparison.Ordinal))
        {
            cut.Find("#players-grad-year option[value='2032']").TextContent.ShouldBe("2032");
        }
        if (!string.Equals(region, "tags", StringComparison.Ordinal))
        {
            cut.Find("#players-tag-filter option[value='11']").TextContent.ShouldBe("Defender");
        }
        await cut.Find("[role='alert'] button").ClickAsync(new());
        await cut.WaitForAssertionAsync(() => cut.FindAll("[role='alert']").ShouldBeEmpty());
        cut.Find("tbody .player-record-link").TextContent.ShouldBe("Avery Johnson");
        cut.Find("#players-grad-year option[value='2032']").TextContent.ShouldBe("2032");
        cut.Find("#players-tag-filter option[value='11']").TextContent.ShouldBe("Defender");
        await roster.Received(string.Equals(region, "roster", StringComparison.Ordinal) ? 2 : 1)
            .GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>());
        await roster.Received(string.Equals(region, "summary", StringComparison.Ordinal) ? 2 : 1)
            .GetPlayerDirectorySummaryAsync(Arg.Any<GetPlayerDirectorySummaryInput>(), Arg.Any<CancellationToken>());
        await tags.Received(string.Equals(region, "tags", StringComparison.Ordinal) ? 2 : 1)
            .GetChoicesAsync(Arg.Any<CancellationToken>());
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObsoleteReadExceptionCannotReplaceNewClubSummaryAsync(bool canceled)
    {
        RegisterServices(isClubAdmin: true);
        var authentication = new FakeAuthenticationStateProvider(CreatePrincipal(true));
        Services.AddSingleton<AuthenticationStateProvider>(authentication);
        var old = new TaskCompletionSource<ServiceResult<PlayerDirectorySummary>>();
        CancellationToken ownedToken = default;
        Services.GetRequiredService<IPlayerService>().GetPlayerDirectorySummaryAsync(
            Arg.Any<GetPlayerDirectorySummaryInput>(), Arg.Any<CancellationToken>()).Returns(call =>
            {
                if (call.Arg<GetPlayerDirectorySummaryInput>().ClubId != 42)
                {
                    return Task.FromResult(new ServiceResult<PlayerDirectorySummary>(new PlayerDirectorySummary
                    {
                        ActiveCount = 9,
                        ArchivedCount = 2,
                        GraduationYears = [2044]
                    }));
                }
                ownedToken = call.Arg<CancellationToken>();
                return old.Task;
            });
        var cut = RenderPlayers();
        await cut.InvokeAsync(() => authentication.Change(CreatePrincipal(true, 43)));
        await cut.WaitForAssertionAsync(() => cut.Find("#players-grad-year option[value='2044']").ShouldNotBeNull());
        ownedToken.IsCancellationRequested.ShouldBeTrue();
        await cut.InvokeAsync(() =>
        {
            if (canceled) { old.SetCanceled(ownedToken); }
            else { old.SetException(new Npgsql.NpgsqlException("Old club provider failure")); }
        });
        await cut.WaitForAssertionAsync(() => cut.Instance.Initialized.ShouldBeTrue());
        cut.FindAll("[role='alert']").ShouldBeEmpty();
        cut.Instance.PersistedSummary.ShouldNotBeNull().ActiveCount.ShouldBe(9);
        cut.Instance.SnapshotScope.ShouldBe("101:43:True");
    }

    private static void ConfigureReadFailure(string region, bool synchronous, bool wrapped, IPlayerService roster, ITagDefinitionQueryService tags)
    {
        var attempts = 0;
        Task<ServiceResult<T>> Read<T>(T value)
        {
            if (++attempts > 1) { return Task.FromResult(new ServiceResult<T>(value)); }
            var providerException = new Npgsql.NpgsqlException("Provider diagnostic must not reach the page");
            Exception exception = wrapped
                ? new Microsoft.EntityFrameworkCore.Storage.RetryLimitExceededException("Provider retries exhausted", providerException)
                : providerException;
            return synchronous ? throw exception : Task.FromException<ServiceResult<T>>(exception);
        }
        switch (region)
        {
            case "roster":
                roster.GetPlayerRosterAsync(Arg.Any<GetPlayerRosterInput>(), Arg.Any<CancellationToken>())
                    .Returns(_ => Read(new PagedResult<PlayerListItem>(CreateRosterItems(), 1, 20, 1)));
                break;
            case "summary":
                roster.GetPlayerDirectorySummaryAsync(Arg.Any<GetPlayerDirectorySummaryInput>(), Arg.Any<CancellationToken>())
                    .Returns(_ => Read(new PlayerDirectorySummary { ActiveCount = 1, ArchivedCount = 0, GraduationYears = [2032] }));
                break;
            case "tags":
                tags.GetChoicesAsync(Arg.Any<CancellationToken>()).Returns(_ => Read<IReadOnlyList<TagDefinitionDto>>(
                    [new() { PlayerTagId = 11, Name = "Defender", Color = "#0055AA", LifecycleStatus = Nova.SharedKernel.Enums.LifecycleStatus.Active }]));
                break;
        }
    }
}
