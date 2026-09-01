using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Teams;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies team lifecycle mutations remain correct when Npgsql retries a failed transaction.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TeamLifecycleRetryTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies a transient post-save failure rolls back and retries with database state loaded by a fresh context.
    /// </summary>
    [Fact]
    public async Task TeamArchive_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, teamId) = await SeedClubAndTeamAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TeamLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<TeamLifecycleService>.Instance);

        var result = await service.ArchiveAsync(teamId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        // One context for execution-strategy setup and one per mutation attempt. The save failed
        // before the commit, so verification short-circuits without creating a context.
        factory.CreatedContextCount.ShouldBe(3);

        await using var verify = fixture.CreateAdminContext();
        var team = await verify.Teams
            .Where(candidate => candidate.TeamId == teamId)
            .Select(candidate => new { candidate.LifecycleStatus, candidate.ArchivedById })
            .SingleAsync(TestContext.Current.CancellationToken);
        team.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        team.ArchivedById.ShouldBe(actorUserId);
    }

    /// <summary>
    /// Verifies a restore replayed by the execution strategy leaves the team active exactly once.
    /// </summary>
    [Fact]
    public async Task TeamRestore_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, teamId) = await SeedClubAndTeamAsync(actorUserId, suffix, archived: true);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TeamLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<TeamLifecycleService>.Instance);

        var result = await service.RestoreAsync(teamId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var team = await verify.Teams
            .Where(candidate => candidate.TeamId == teamId)
            .Select(candidate => new { candidate.LifecycleStatus, candidate.ArchivedAt, candidate.ArchivedById })
            .SingleAsync(TestContext.Current.CancellationToken);
        team.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        team.ArchivedAt.ShouldBeNull();
        team.ArchivedById.ShouldBeNull();
    }

    /// <summary>
    /// Verifies an archive whose commit reached the database but surfaced a transient failure is
    /// reported as success rather than replayed into a spurious "already archived" conflict.
    /// </summary>
    [Fact]
    public async Task TeamArchive_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, teamId) = await SeedClubAndTeamAsync(actorUserId, suffix);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TeamLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<TeamLifecycleService>.Instance);

        var result = await service.ArchiveAsync(teamId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var team = await verify.Teams
            .Where(candidate => candidate.TeamId == teamId)
            .Select(candidate => new { candidate.LifecycleStatus, candidate.ArchivedById })
            .SingleAsync(TestContext.Current.CancellationToken);
        team.LifecycleStatus.ShouldBe(LifecycleStatus.Archived);
        team.ArchivedById.ShouldBe(actorUserId);
    }

    /// <summary>
    /// Verifies the same ambiguous-commit protection applies to restore.
    /// </summary>
    [Fact]
    public async Task TeamRestore_ReportsSuccess_WhenCommitSucceedsButTransientFailureSurfaces()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, teamId) = await SeedClubAndTeamAsync(actorUserId, suffix, archived: true);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TeamLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<TeamLifecycleService>.Instance);

        var result = await service.RestoreAsync(teamId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var team = await verify.Teams
            .Where(candidate => candidate.TeamId == teamId)
            .Select(candidate => new { candidate.LifecycleStatus, candidate.ArchivedAt })
            .SingleAsync(TestContext.Current.CancellationToken);
        team.LifecycleStatus.ShouldBe(LifecycleStatus.Active);
        team.ArchivedAt.ShouldBeNull();
    }

    /// <summary>
    /// Verifies a transient failure raised before any commit does not let commit verification
    /// convert a genuine "already archived" conflict into a success.
    /// </summary>
    /// <remarks>
    /// The team is archived before the request runs, so the correct answer is a conflict. The
    /// injected failure interrupts the read, which means no commit was attempted and the applied
    /// status belongs to an earlier request rather than this one's ambiguous commit.
    /// </remarks>
    [Fact]
    public async Task TeamArchive_ReportsConflict_WhenTransientFailurePrecedesCommitOnArchivedTeam()
    {
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");
        var (clubId, teamId) = await SeedClubAndTeamAsync(actorUserId, suffix, archived: true);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var failureInterceptor = new FailFirstTeamReadInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TeamLifecycleService(
            factory,
            fixture.CurrentUser,
            NullLogger<TeamLifecycleService>.Instance);

        var result = await service.ArchiveAsync(teamId, TestContext.Current.CancellationToken);

        failureInterceptor.FailureCount.ShouldBe(1);
        result.IsProblem.ShouldBeTrue("an already-archived team must still conflict after a pre-commit retry");
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>
    /// Seeds one club and one team owned by it, bypassing tenant filters.
    /// </summary>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="suffix">A unique suffix for generated names.</param>
    /// <param name="archived">Whether the seeded team starts archived.</param>
    /// <returns>The seeded club and team identifiers.</returns>
    private async Task<(long ClubId, long TeamId)> SeedClubAndTeamAsync(
        long actorUserId,
        string suffix,
        bool archived = false)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using var seed = fixture.CreateAdminContext();
        var club = new ClubEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Team Retry Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        seed.Clubs.Add(club);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        var team = new TeamEntity
        {
            CreationOperationId = Guid.NewGuid(),
            Name = $"Retry Team {suffix}",
            GraduationYear = 2030,
            ClubId = club.ClubId,
            CreatedById = actorUserId,
            LifecycleStatus = archived ? LifecycleStatus.Archived : LifecycleStatus.Active,
            ArchivedAt = archived ? DateTimeOffset.UtcNow : null,
            ArchivedById = archived ? actorUserId : null
        };
        seed.Teams.Add(team);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);

        return (club.ClubId, team.TeamId);
    }
}
