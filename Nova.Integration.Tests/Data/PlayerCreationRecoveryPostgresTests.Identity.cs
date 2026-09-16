using System.Globalization;
using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Components.Server;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data.Tenancy;
using Nova.Features.Common;
using Nova.Features.Players;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Shouldly;

namespace Nova.Integration.Tests.Data;

public sealed partial class PlayerCreationRecoveryPostgresTests
{
    /// <summary>Queries, tenant stamping, enrollment, and attribution retain the identity that acquired the locks.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(false, false, true)]
    public async Task CreationKeepsOriginalIdentityAcrossLockWaitAsync(bool activeCampaign, bool otherDuplicate, bool sameClub)
    {
        var ct = TestContext.Current.CancellationToken;
        var original = await SeedAsync(activeCampaign ? CampaignStatus.Active : CampaignStatus.Draft, ct);
        var otherClub = await SeedAsync(otherDuplicate ? CampaignStatus.Active : CampaignStatus.Draft, ct);
        var replacement = otherClub;
        if (sameClub)
        {
            await using var setup = fixture.CreateAdminContext();
            (await setup.Users.SingleAsync(user => user.Id == otherClub.ActorUserId, ct)).ClubId = original.ClubId;
            await setup.SaveChangesAsync(ct);
            replacement = otherClub with { ClubId = original.ClubId };
        }
        if (otherDuplicate)
        {
            ActAs(otherClub);
            (await Service().CreateAsync(Input(otherClub.ClubId), ct)).IsSuccess.ShouldBeTrue();
        }
        using var identity = new CircuitIdentity(original);
        var input = Input(original.ClubId);
        await using var blocker = fixture.CreateAdminContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync(ct);
        await blocker.AcquireClubRosterLockAsync(original.ClubId, ct);
        var pending = CircuitService(identity).CreateAsync(input, ct);
        try
        {
            await WaitForLockAsync((long.MinValue / 4) + original.ClubId, ct);
            identity.SwitchTo(replacement);
        }
        finally { await transaction.RollbackAsync(ct); }

        var result = await pending;
        result.IsSuccess.ShouldBeTrue();
        result.Value.Player.ClubId.ShouldBe(original.ClubId);
        if (activeCampaign) { result.Value.Enrollment.ShouldNotBeNull().CampaignId.ShouldBe(original.CampaignId); }
        else { result.Value.Enrollment.ShouldBeNull(); }
        await AssertCountsAsync(original, 1, activeCampaign ? 1 : 0, 1, ct);
        await AssertCountsAsync(otherClub, otherDuplicate ? 1 : 0, otherDuplicate ? 1 : 0, otherDuplicate ? 1 : 0, ct);
        await using var verify = fixture.CreateAdminContext();
        var player = await verify.Players.SingleAsync(row => row.PlayerId == result.Value.Player.PlayerId, ct);
        player.CreatedById.ShouldBe(original.ActorUserId);
        var receipt = await verify.PlayerCreationReceipts.SingleAsync(row => row.ClubId == original.ClubId && row.OperationId == input.OperationId, ct);
        receipt.ActorUserId.ShouldBe(original.ActorUserId);
        receipt.CreatedById.ShouldBe(original.ActorUserId);
        identity.SwitchTo(original);
        (await CircuitService(identity).CreateAsync(input, ct)).Value.ShouldBe(result.Value);
    }

    /// <summary>A fresh commit-verification context denies the changed caller without losing original commit proof.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RecoveryRejectsChangedCircuitIdentityAsync(bool sameClub)
    {
        var ct = TestContext.Current.CancellationToken;
        var original = await SeedAsync(CampaignStatus.Active, ct);
        var replacement = await SeedAsync(CampaignStatus.Draft, ct);
        if (sameClub)
        {
            await using var setup = fixture.CreateAdminContext();
            (await setup.Users.SingleAsync(user => user.Id == replacement.ActorUserId, ct)).ClubId = original.ClubId;
            await setup.SaveChangesAsync(ct);
            replacement = replacement with { ClubId = original.ClubId };
        }
        using var identity = new CircuitIdentity(original);
        var input = Input(original.ClubId);
        var gate = new GatedLostPlacementAcknowledgementInterceptor();
        var pending = CircuitService(identity, gate).CreateAsync(input, ct);
        try
        {
            await gate.WaitForCommitAsync(ct);
            identity.SwitchTo(replacement);
        }
        finally { gate.Release(); }

        var denied = await pending;
        gate.FailureCount.ShouldBe(1);
        denied.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        PlayerCreationProblems.IsNotCommitted(denied.Problem, input.OperationId).ShouldBeFalse();
        identity.SwitchTo(original);
        var recovered = await CircuitService(identity).CreateAsync(input, ct);
        recovered.IsSuccess.ShouldBeTrue();
        recovered.Value.OperationId.ShouldBe(input.OperationId);
        recovered.Value.Player.ClubId.ShouldBe(original.ClubId);
        recovered.Value.Enrollment!.CampaignId.ShouldBe(original.CampaignId);
        await AssertCountsAsync(original, 1, 1, 1, ct);
    }

    /// <summary>Editing, archiving, and restoring use the same stable query/audit scope as creation.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("edit")]
    [InlineData("archive")]
    [InlineData("restore")]
    public async Task ManualMutationKeepsOriginalIdentityAcrossLockWaitAsync(string mutation)
    {
        var ct = TestContext.Current.CancellationToken;
        var original = await SeedAsync(CampaignStatus.Draft, ct);
        var replacement = await SeedAsync(CampaignStatus.Draft, ct);
        using var identity = new CircuitIdentity(original);
        var player = (await CircuitService(identity).CreateAsync(Input(original.ClubId), ct)).Value.Player;
        var lifecycle = new PlayerLifecycleService(
            new RetryingTenantDbContextFactory(fixture.ConnectionString, identity.User),
            identity.User, NullLogger<PlayerLifecycleService>.Instance);
        if (mutation is "restore") { (await lifecycle.ArchiveAsync(player.PlayerId, ct)).IsSuccess.ShouldBeTrue(); }
        await using var blocker = fixture.CreateAdminContext();
        await using var transaction = await blocker.Database.BeginTransactionAsync(ct);
        await blocker.AcquireClubRosterLockAsync(original.ClubId, ct);
        var pending = RunMutationAsync();
        try
        {
            await WaitForLockAsync((long.MinValue / 4) + original.ClubId, ct);
            identity.SwitchTo(replacement);
        }
        finally { await transaction.RollbackAsync(ct); }

        (await pending).ShouldBeTrue();
        await using var verify = fixture.CreateAdminContext();
        var persisted = await verify.Players.SingleAsync(row => row.PlayerId == player.PlayerId, ct);
        persisted.ClubId.ShouldBe(original.ClubId);
        persisted.ModifiedById.ShouldBe(original.ActorUserId);
        persisted.LifecycleStatus.ShouldBe(mutation is "archive" ? LifecycleStatus.Archived : LifecycleStatus.Active);
        if (mutation is "edit") { persisted.LastName.ShouldBe("Edited"); }
        if (mutation is "archive") { persisted.ArchivedById.ShouldBe(original.ActorUserId); }
        await AssertCountsAsync(replacement, 0, 0, 0, ct);

        async Task<bool> RunMutationAsync() => mutation switch
        {
            "archive" => (await lifecycle.ArchiveAsync(player.PlayerId, ct)).IsSuccess,
            "restore" => (await lifecycle.RestoreAsync(player.PlayerId, ct)).IsSuccess,
            _ => (await CircuitService(identity).UpdateAsync(new UpdatePlayerInput
            {
                PlayerId = player.PlayerId,
                FirstName = player.FirstName,
                LastName = "Edited",
                DateOfBirth = player.DateOfBirth,
                GraduationYear = player.GraduationYear
            }, ct)).IsSuccess
        };
    }

    private PlayerManagementService CircuitService(CircuitIdentity identity, params IInterceptor[] interceptors) => new(
        new RetryingTenantDbContextFactory(fixture.ConnectionString, identity.User, interceptors),
        identity.User, NullLogger<PlayerManagementService>.Instance, TimeProvider.System);

    /// <summary>Uses the real ambient-provider fallback with the same authentication replacement API as a retained circuit.</summary>
    private sealed class CircuitIdentity : IDisposable
    {
        private readonly ServerAuthenticationStateProvider _authentication = new();
        private readonly ServiceProvider _services;

        public CircuitIdentity(Seed seed)
        {
            _services = new ServiceCollection().AddSingleton<AuthenticationStateProvider>(_authentication).BuildServiceProvider();
            User = new CurrentUserProvider(new HttpContextAccessor(), _services);
            SwitchTo(seed);
        }

        public ICurrentUserProvider User { get; }

        public void SwitchTo(Seed seed) => _authentication.SetAuthenticationState(Task.FromResult(new AuthenticationState(
            new ClaimsPrincipal(new ClaimsIdentity([
                new Claim(ClaimTypes.NameIdentifier, seed.ActorUserId.ToString(CultureInfo.InvariantCulture)),
                new Claim(NovaClaimTypes.ClubId, seed.ClubId.ToString(CultureInfo.InvariantCulture))
            ], "TestCircuit")))));

        public void Dispose() => _services.Dispose();
    }
}
