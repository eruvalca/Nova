using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Security;
using Shouldly;

namespace Nova.Integration.Tests.Data;

public sealed partial class EffectivePlacementPostgresTests
{
    /// <summary>Readiness, local totals, discovery counts and capabilities retain one PostgreSQL snapshot.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CloseoutSnapshotRetainsOutcomesAndCapabilitiesAcrossCommittedChangesAsync(bool closed)
    {
        var token = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(1);
        await PrepareCloseoutSnapshotAsync(seed, closed);
        var actor = await SeedCloseoutAdministratorAsync(seed.ClubId);
        using var user = fixture.UseUser(actor, seed.ClubId, isClubAdmin: true);
        var input = new GetCampaignCloseoutReadinessInput { CampaignId = seed.ActiveId };
        var original = (await CreateCloseoutService().GetCloseoutReadinessAsync(input, token)).Value;
        original.Summary.ShouldBe(closed ? new(1, 0, 0, 0, 1) : new(0, 0, 0, 1, 1));
        original.NeedsPlacementCount.ShouldBe(closed ? 0 : 1);
        original.IsReady.ShouldBe(closed);
        original.Lifecycle.IsAdministrator.ShouldBeTrue();
        original.Lifecycle.CanClose.ShouldBeFalse();
        original.Lifecycle.CanReopen.ShouldBe(closed);
        original.Blockers.Select(blocker => blocker.Condition).ShouldBe(closed ? Array.Empty<string>() : [CloseoutBlockerConditions.Outcomes]);
        var gate = new PlacementReadGateInterceptor("Campaigns");
        var pending = CreateCloseoutService(gate).GetCloseoutReadinessAsync(input, token);
        try
        {
            await gate.WaitUntilBlockedAsync(token).WaitAsync(TimeSpan.FromSeconds(30), token);
            gate.CompletedIdentityRead.ShouldBeTrue();
            await ChangeCloseoutFactsAsync(seed, closed);
            var fresh = (await CreateCloseoutService().GetCloseoutReadinessAsync(input, token)).Value;
            fresh.Summary.ShouldBe(closed ? new(1, 0, 0, 0, 1) : new(0, 1, 0, 0, 1));
            fresh.IsReady.ShouldBe(!closed);
            fresh.NeedsPlacementCount.ShouldBe(0);
            fresh.Blockers.Select(blocker => blocker.Condition).ShouldBe(closed ? [CloseoutBlockerConditions.ArchivedTeams] : Array.Empty<string>());
            fresh.Lifecycle.CanClose.ShouldBe(!closed);
            fresh.Lifecycle.CanReopen.ShouldBeFalse();
            fresh.Lifecycle.ReopenUnavailableReason.ShouldBe(closed ? CampaignReopenUnavailableReason.HistoricalSeason : CampaignReopenUnavailableReason.NotClosed);
        }
        finally { gate.Release(); }
        var snapshot = (await pending).Value;
        snapshot.CampaignId.ShouldBe(original.CampaignId);
        snapshot.Status.ShouldBe(original.Status);
        snapshot.IsReady.ShouldBe(original.IsReady);
        snapshot.Summary.ShouldBe(original.Summary);
        snapshot.NeedsPlacementCount.ShouldBe(original.NeedsPlacementCount);
        snapshot.Lifecycle.ShouldBe(original.Lifecycle);
        snapshot.Blockers.Select(blocker => (blocker.Condition, blocker.Count)).ShouldBe(original.Blockers.Select(blocker => (blocker.Condition, blocker.Count)));
        snapshot.Blockers.SelectMany(blocker => blocker.AssignmentIds).ShouldBe(original.Blockers.SelectMany(blocker => blocker.AssignmentIds));
    }

    private async Task PrepareCloseoutSnapshotAsync(Seed seed, bool closed)
    {
        if (closed) { await PrepareClosedCampaignAsync(seed); return; }
        await using var db = fixture.CreateAdminContext();
        var inherited = await db.PlayerCampaignAssignments.SingleAsync(assignment => assignment.CampaignId == seed.LatestClosedId, TestContext.Current.CancellationToken);
        inherited.PlacementOutcome = PlacementOutcome.NotSelected;
        inherited.TeamId = null;
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    private async Task<long> SeedCloseoutAdministratorAsync(long clubId)
    {
#pragma warning disable CA5394 // Random identity isolates this test fixture; it is not a credential or security token.
        var actor = Random.Shared.NextInt64(1, long.MaxValue);
#pragma warning restore CA5394
        await using var db = fixture.CreateAdminContext();
        db.Users.Add(new NovaUserEntity { Id = actor, ClubId = clubId, FirstName = "Closeout", LastName = "Administrator" });
        var normalizedRole = Roles.ClubAdmin.ToUpperInvariant();
        var roleId = await db.Roles.Where(role => role.NormalizedName == normalizedRole).Select(role => role.Id).SingleAsync(TestContext.Current.CancellationToken);
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = actor, RoleId = roleId });
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        return actor;
    }

    private async Task ChangeCloseoutFactsAsync(Seed seed, bool closed)
    {
        await using var db = fixture.CreateAdminContext();
        if (closed)
        {
            var team = await db.Teams.SingleAsync(team => team.TeamId == seed.LatestTeamId, TestContext.Current.CancellationToken);
            team.LifecycleStatus = LifecycleStatus.Archived;
            team.ArchivedAt = DateTimeOffset.UtcNow;
            team.ArchivedById = fixture.CurrentUser.UserId;
        }
        else
        {
            var assignment = await db.PlayerCampaignAssignments.SingleAsync(assignment => assignment.CampaignId == seed.ActiveId, TestContext.Current.CancellationToken);
            assignment.PlacementOutcome = PlacementOutcome.NotSelected;
            assignment.TeamId = null;
        }
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);
        if (closed) { await AdvanceSeasonAsync(seed.ClubId); }
    }

    private CampaignCloseoutQueryService CreateCloseoutService(DbCommandInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<NovaReadDbContext>().UseNpgsql(fixture.ConnectionString)
            .UseApplicationServiceProvider(IdentityStoreServiceProvider.Instance);
        if (interceptor is not null) { options.AddInterceptors(interceptor); }
        return new(new ReadFactory(options.Options, fixture.CurrentUser), fixture.CurrentUser, NullLogger<CampaignCloseoutQueryService>.Instance);
    }
}
