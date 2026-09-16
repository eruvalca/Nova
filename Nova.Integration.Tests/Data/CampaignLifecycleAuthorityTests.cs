using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Features.Campaigns;
using Nova.Features.Common;
using Nova.SharedKernel.Enums;
using Shouldly;

namespace Nova.Integration.Tests.Data;

public sealed partial class CampaignLifecyclePostgresTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PersistedAdministratorRoleIsRecheckedAfterWaitingForLifecycleLockAsync(bool reopen)
    {
        var seed = await SeedCampaignAsync(closed: reopen);
        fixture.CurrentUser.UserId = seed.ActorUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;
        var token = TestContext.Current.CancellationToken;
        await using var holder = fixture.CreateAdminContext();
        await using var transaction = await holder.Database.BeginTransactionAsync(token);
        var lockKey = reopen ? (long.MinValue / 16) + seed.ClubId : long.MinValue + seed.CampaignId;
        await holder.Database.ExecuteSqlInterpolatedAsync($"SELECT pg_advisory_xact_lock({lockKey})", token);
        var service = new CampaignLifecycleService(new FixtureDbContextFactory(fixture), fixture.CurrentUser, NullLogger<CampaignLifecycleService>.Instance);
        var attempt = ObserveLifecycleResultAsync(service, seed.CampaignId, reopen, token);
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(holder, lockKey, token);
        // Simulate persisted authority changing while this actor's stale cookie waits for lifecycle evidence.
        await holder.UserRoles.Where(role => role.UserId == seed.ActorUserId).ExecuteDeleteAsync(token);
        await transaction.CommitAsync(token);
        (await attempt).ShouldBeOfType<LifecycleForbidden>();
        await using var verify = fixture.CreateAdminContext();
        (await verify.Campaigns.SingleAsync(row => row.CampaignId == seed.CampaignId, token)).Status.ShouldBe(reopen ? CampaignStatus.Closed : CampaignStatus.Active);
        (await verify.ActivityEvents.CountAsync(row => row.CampaignId == seed.CampaignId, token)).ShouldBe(0);
    }

    private static async Task<object> ObserveLifecycleResultAsync(CampaignLifecycleService service, long campaignId, bool reopen, CancellationToken token)
    {
        if (reopen) { return (await service.ReopenAsync(campaignId, token)).Value; }
        return (await service.CloseAsync(campaignId, token)).Value;
    }
}
