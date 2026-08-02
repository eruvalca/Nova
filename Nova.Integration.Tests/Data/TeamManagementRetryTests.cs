using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Teams;
using Nova.Shared.Results;
using Nova.Shared.Teams;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies team create and update mutations remain correct when Npgsql retries a failed
/// transaction.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TeamManagementRetryTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies PostgreSQL rejects two teams in the same club with the same creation-operation
    /// identifier.
    /// </summary>
    [Fact]
    public async Task CreationOperationId_RejectsDuplicateWithinClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var creationOperationId = Guid.CreateVersion7();

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using var db = fixture.CreateAdminContext();
        var clubId = await SeedClubAsync(db, $"Team Idempotency Club {suffix}", actorUserId, cancellationToken);

        db.Teams.AddRange(
            CreateTeam($"First {suffix}", clubId, actorUserId, creationOperationId),
            CreateTeam($"Second {suffix}", clubId, actorUserId, creationOperationId));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    /// <summary>
    /// Verifies PostgreSQL rejects two teams in the same club sharing a name and graduation year.
    /// </summary>
    [Fact]
    public async Task TeamName_RejectsDuplicateNameAndGraduationYearWithinClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using var db = fixture.CreateAdminContext();
        var clubId = await SeedClubAsync(db, $"Team Uniqueness Club {suffix}", actorUserId, cancellationToken);

        db.Teams.AddRange(
            CreateTeam($"Shared {suffix}", clubId, actorUserId, Guid.CreateVersion7()),
            CreateTeam($"Shared {suffix}", clubId, actorUserId, Guid.CreateVersion7()));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    /// <summary>
    /// Verifies a team transaction that committed before a transient connection failure is
    /// recognized by its stable operation identifier and is not replayed as a duplicate insert.
    /// </summary>
    [Fact]
    public async Task Create_VerifiesCommittedOperation_AfterAmbiguousCommitFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var teamName = $"Ambiguous Commit Team {suffix}";
        long clubId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(
                seed,
                $"Team Ambiguous Commit Club {suffix}",
                actorUserId,
                cancellationToken);
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TeamManagementService(
            factory,
            fixture.CurrentUser,
            NullLogger<TeamManagementService>.Instance);

        var result = await service.CreateAsync(
            new CreateTeamInput { Name = teamName, GraduationYear = 2030 },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);

        await using var verify = fixture.CreateAdminContext();
        var teams = await verify.Teams
            .Where(team => team.ClubId == clubId && team.Name == teamName)
            .Select(team => team.TeamId)
            .ToListAsync(cancellationToken);
        teams.ShouldBe([result.Value.TeamId]);
    }

    /// <summary>
    /// Verifies a transient post-save failure during team creation rolls back and retries with a
    /// fresh context and transaction without leaving a duplicate team behind.
    /// </summary>
    [Fact]
    public async Task Create_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var teamName = $"Retry Create Team {suffix}";
        long clubId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(
                seed,
                $"Team Retry Create Club {suffix}",
                actorUserId,
                cancellationToken);
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TeamManagementService(
            factory,
            fixture.CurrentUser,
            NullLogger<TeamManagementService>.Instance);

        var result = await service.CreateAsync(
            new CreateTeamInput { Name = teamName, GraduationYear = 2030 },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBeGreaterThan(1);

        await using var verify = fixture.CreateAdminContext();
        var createdTeams = await verify.Teams
            .Where(team => team.ClubId == clubId && team.Name == teamName)
            .Select(team => team.TeamId)
            .ToListAsync(cancellationToken);
        createdTeams.ShouldBe([result.Value.TeamId]);
    }

    /// <summary>
    /// Verifies a transient post-save failure during a team update rolls back and retries with a
    /// fresh context and transaction.
    /// </summary>
    [Fact]
    public async Task Update_RetriesWithFreshContext_AfterTransientSaveFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var updatedName = $"After Retry {suffix}";
        long clubId;
        long teamId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(
                seed,
                $"Team Retry Update Club {suffix}",
                actorUserId,
                cancellationToken);

            var team = CreateTeam($"Before Retry {suffix}", clubId, actorUserId, Guid.CreateVersion7());
            seed.Teams.Add(team);
            await seed.SaveChangesAsync(cancellationToken);
            teamId = team.TeamId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var service = new TeamManagementService(
            factory,
            fixture.CurrentUser,
            NullLogger<TeamManagementService>.Instance);

        var result = await service.UpdateAsync(
            new UpdateTeamInput { TeamId = teamId, Name = updatedName, GraduationYear = 2031 },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBeGreaterThan(1);

        await using var verify = fixture.CreateAdminContext();
        var updatedTeam = await verify.Teams
            .Where(team => team.TeamId == teamId)
            .Select(team => new { team.Name, team.GraduationYear })
            .SingleAsync(cancellationToken);

        updatedTeam.Name.ShouldBe(updatedName);
        updatedTeam.GraduationYear.ShouldBe(2031);
    }

    /// <summary>
    /// Verifies an update that loses the race to the unique team index is translated into a
    /// conflict instead of letting the provider exception escape.
    /// </summary>
    /// <remarks>
    /// The service probes for a duplicate before writing, so the losing update can only reach the
    /// database constraint when the conflicting team appears after that probe. Committing the
    /// conflicting team from an independent context immediately after the probe reproduces that
    /// window deterministically instead of relying on two updates interleaving by chance.
    /// </remarks>
    [Fact]
    public async Task Update_ReportsConflict_WhenDuplicateAppearsAfterTheProbe()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var contestedName = $"Contested {suffix}";
        long clubId;
        long teamId;

        ActAs(userId: null, clubId: null, isAdmin: false);

        await using (var seed = fixture.CreateAdminContext())
        {
            clubId = await SeedClubAsync(
                seed,
                $"Team Update Conflict Club {suffix}",
                actorUserId,
                cancellationToken);

            var team = CreateTeam($"Original {suffix}", clubId, actorUserId, Guid.CreateVersion7());
            seed.Teams.Add(team);
            await seed.SaveChangesAsync(cancellationToken);
            teamId = team.TeamId;
        }

        ActAs(actorUserId, clubId, isAdmin: true);

        var conflictInterceptor = new InsertAfterTeamExistsProbeInterceptor(async () =>
        {
            await using var conflicting = fixture.CreateAdminContext();
            conflicting.Teams.Add(CreateTeam(contestedName, clubId, actorUserId, Guid.CreateVersion7()));
            await conflicting.SaveChangesAsync(CancellationToken.None);
        });

        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            conflictInterceptor);
        var service = new TeamManagementService(
            factory,
            fixture.CurrentUser,
            NullLogger<TeamManagementService>.Instance);

        var result = await service.UpdateAsync(
            new UpdateTeamInput { TeamId = teamId, Name = contestedName, GraduationYear = 2030 },
            cancellationToken);

        conflictInterceptor.InsertCount.ShouldBe(1);
        result.IsProblem.ShouldBeTrue("the losing update must surface as a conflict, not a provider exception");
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        await using var verify = fixture.CreateAdminContext();
        var names = await verify.Teams
            .Where(team => team.ClubId == clubId)
            .Select(team => team.Name)
            .ToListAsync(cancellationToken);

        names.ShouldBe([$"Original {suffix}", contestedName], ignoreOrder: true);
    }

    /// <summary>
    /// Sets the current simulated user for the fixture-backed tenant contexts.
    /// </summary>
    /// <param name="userId">The simulated user identifier.</param>
    /// <param name="clubId">The simulated club identifier.</param>
    /// <param name="isAdmin">Whether the simulated user is a club administrator.</param>
    private void ActAs(long? userId, long? clubId, bool isAdmin)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isAdmin;
    }

    /// <summary>
    /// Persists one club and returns its identifier.
    /// </summary>
    /// <param name="db">The admin context used to bypass tenant filters while seeding.</param>
    /// <param name="name">The club name.</param>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="cancellationToken">A token that cancels the seed operation.</param>
    /// <returns>The seeded club identifier.</returns>
    private static async Task<long> SeedClubAsync(
        NovaAdminDbContext db,
        string name,
        long actorUserId,
        CancellationToken cancellationToken)
    {
        var club = new ClubEntity
        {
            Name = name,
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);
        return club.ClubId;
    }

    /// <summary>
    /// Creates a team entity for persistence-focused constraint tests.
    /// </summary>
    /// <param name="name">The team name.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="creationOperationId">The stable creation-operation identifier.</param>
    /// <returns>A new team entity ready to persist.</returns>
    private static TeamEntity CreateTeam(
        string name,
        long clubId,
        long actorUserId,
        Guid creationOperationId) => new()
        {
            Name = name,
            GraduationYear = 2030,
            ClubId = clubId,
            CreationOperationId = creationOperationId,
            CreatedById = actorUserId
        };
}
