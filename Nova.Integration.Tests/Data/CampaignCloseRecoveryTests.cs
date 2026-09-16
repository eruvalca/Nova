using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Npgsql;
using Shouldly;

namespace Nova.Integration.Tests.Data;

public sealed partial class CampaignLifecycleRetryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostLifecycleAcknowledgementAfterOppositeTransitionDoesNotReplayOrInferSuccessAsync(bool reopen)
    {
#pragma warning disable CA5394 // Isolated fixture identity, not a security token.
        var actor = Random.Shared.NextInt64(1, long.MaxValue);
#pragma warning restore CA5394
        var (club, campaign) = await SeedClubAndCampaignAsync(actor, Guid.NewGuid().ToString("N"), closed: reopen);
        ActAsAdmin(actor, club);
        var gate = new LostLifecycleAcknowledgementGate();
        var factory = new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, gate);
        var service = CreateService(factory);
        var token = TestContext.Current.CancellationToken;
        var attempt = reopen ? ReopenValueAsync(service, campaign, token) : CloseValueAsync(service, campaign, token);
        try
        {
            await gate.Committed.Task.WaitAsync(TimeSpan.FromSeconds(30), token);
            var competing = CreateService(new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser));
            var other = reopen ? await CloseValueAsync(competing, campaign, token) : await ReopenValueAsync(competing, campaign, token);
            other.ShouldBeOfType<OneOf.Types.Success>();
        }
        finally
        {
            gate.Release.TrySetResult();
        }
        (await attempt).ShouldBeOfType<LifecycleOutcomeUnknown>();
        factory.CreatedContextCount.ShouldBe(2);
        await using var verify = fixture.CreateAdminContext();
        (await verify.Campaigns.SingleAsync(row => row.CampaignId == campaign, token)).Status.ShouldBe(reopen ? CampaignStatus.Closed : CampaignStatus.Active);
        (await verify.ActivityEvents.CountAsync(row => row.CampaignId == campaign && row.EventKind == ActivityEventKind.CampaignClosed, token)).ShouldBe(1);
        (await verify.ActivityEvents.CountAsync(row => row.CampaignId == campaign && row.EventKind == ActivityEventKind.CampaignReopened, token)).ShouldBe(1);
    }

    private static async Task<object> CloseValueAsync(CampaignLifecycleService service, long campaign, CancellationToken token)
        => (await service.CloseAsync(campaign, token)).Value;
    private static async Task<object> ReopenValueAsync(CampaignLifecycleService service, long campaign, CancellationToken token)
        => (await service.ReopenAsync(campaign, token)).Value;

    private sealed class LostLifecycleAcknowledgementGate : DbTransactionInterceptor
    {
        public TaskCompletionSource Committed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
        {
            Committed.TrySetResult();
            await Release.Task.WaitAsync(cancellationToken);
            throw new NpgsqlException("Lost commit acknowledgement after a competing transition.", new TimeoutException());
        }
    }
}
