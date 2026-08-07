using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Features.Teams;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Features.Teams;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies team and player graduation-year updates cannot interleave into an ineligible placement.
/// </summary>
/// <remarks>
/// A team's graduation year is the minimum eligible player graduation year, so every Assigned
/// placement in an Active campaign must satisfy <c>Player.GraduationYear &gt;= Team.GraduationYear</c>.
/// Both sides validate that invariant, but they validate it against the counterpart row they read.
/// If they take disjoint locks, each reads the other's pre-change value, both pass, and together
/// they commit a violation. <see cref="TeamManagementService"/> therefore locks the placed players
/// before locking the team, joining the campaign-then-players-then-team order that
/// <see cref="Nova.Features.Campaigns.CampaignPlacementService"/> and
/// <see cref="PlayerManagementService"/> already follow.
/// </remarks>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TeamPlayerGraduationYearRaceTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies a team graduation-year change locks every player placed on that team before locking
    /// the team itself, which is the ordering that serializes it against a concurrent player change.
    /// </summary>
    /// <remarks>
    /// Asserting on the advisory-lock statements the service issues keeps this deterministic: the
    /// ordering is a property of the emitted SQL, not of how two connections happen to interleave.
    /// </remarks>
    [Fact]
    public async Task TeamUpdate_LocksPlacedPlayersBeforeTeam()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, int.MaxValue);
        var seed = await SeedPlacementAsync(actorUserId, teamGraduationYear: 2029, playerGraduationYear: 2030);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var lockRecorder = new AdvisoryLockRecordingInterceptor();
        var service = new TeamManagementService(
            new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, lockRecorder),
            fixture.CurrentUser,
            NullLogger<TeamManagementService>.Instance);

        var result = await service.UpdateAsync(
            new UpdateTeamInput { TeamId = seed.TeamId, Name = seed.TeamName, GraduationYear = 2030 },
            cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        lockRecorder.AcquiredKeys.ShouldBe(
            [seed.PlayerId, -seed.TeamId],
            "the placed player must be locked before the team so every writer of this invariant shares one lock order");
    }
    /// <summary>
    /// Verifies concurrent team and player graduation-year changes that would together strand an
    /// ineligible placement cannot both succeed, and that the surviving state satisfies the invariant.
    /// </summary>
    [Fact]
    public async Task ConcurrentTeamAndPlayerUpdates_CannotStrandIneligiblePlacement()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, int.MaxValue);
        var seed = await SeedPlacementAsync(actorUserId, teamGraduationYear: 2029, playerGraduationYear: 2030);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var teamService = new TeamManagementService(
            new RaceTestDbContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<TeamManagementService>.Instance);
        var playerService = new PlayerManagementService(
            new RaceTestDbContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<PlayerManagementService>.Instance);

        // Raising the team's minimum to 2030 is valid against the player's current 2030, and lowering
        // the player to 2029 is valid against the team's current 2029. Applying both leaves the
        // placement ineligible, so exactly one must be rejected.
        var teamUpdate = Task.Run(
            () => teamService.UpdateAsync(
                new UpdateTeamInput { TeamId = seed.TeamId, Name = seed.TeamName, GraduationYear = 2030 },
                cancellationToken),
            cancellationToken);

        var playerUpdate = Task.Run(
            () => playerService.UpdateAsync(
                new UpdatePlayerInput
                {
                    PlayerId = seed.PlayerId,
                    FirstName = seed.PlayerFirstName,
                    LastName = seed.PlayerLastName,
                    DateOfBirth = seed.PlayerDateOfBirth,
                    GraduationYear = 2029
                },
                cancellationToken),
            cancellationToken);

        var teamResult = await teamUpdate;
        var playerResult = await playerUpdate;

        var successes = (teamResult.IsSuccess ? 1 : 0) + (playerResult.IsSuccess ? 1 : 0);
        successes.ShouldBe(1, "exactly one of the two conflicting graduation-year changes may commit");

        await using var verify = fixture.CreateAdminContext();
        var state = await verify.PlayerCampaignAssignments
            .Where(assignment => assignment.PlayerCampaignAssignmentId == seed.PlacementId)
            .Select(assignment => new
            {
                PlayerGraduationYear = assignment.Player.GraduationYear,
                TeamGraduationYear = assignment.Team!.GraduationYear
            })
            .SingleAsync(cancellationToken);

        state.PlayerGraduationYear.ShouldBeGreaterThanOrEqualTo(
            state.TeamGraduationYear,
            "the placement must remain eligible after both updates settle");
    }

    /// <summary>
    /// Verifies a team graduation-year change is rejected when a placement for a player outside the
    /// lock set appears between computing that set and taking the team lock.
    /// </summary>
    /// <remarks>
    /// The lock set is computed before the team lock is held, so
    /// <see cref="Nova.Features.Campaigns.CampaignPlacementService"/> can assign another player to
    /// this team inside that window. That player is unlocked, so its graduation year could still be
    /// changing, and validating against it would enforce nothing. Committing the extra placement from
    /// an independent connection at exactly that point reproduces the window deterministically rather
    /// than waiting for two requests to interleave by chance.
    /// </remarks>
    [Fact]
    public async Task TeamUpdate_ReportsConflict_WhenPlacementAppearsForUnlockedPlayer()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var actorUserId = Random.Shared.NextInt64(1, int.MaxValue);
        var seed = await SeedPlacementAsync(actorUserId, teamGraduationYear: 2029, playerGraduationYear: 2030);
        var latecomerPlayerId = await SeedPlayerAsync(seed.ClubId, actorUserId, graduationYear: 2030);
        var campaignId = await ReadCampaignIdAsync(seed.PlacementId);

        fixture.CurrentUser.UserId = actorUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;

        var latecomer = new PlacementAfterLockSetInterceptor(async () =>
        {
            await using var independent = fixture.CreateAdminContext();
            independent.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
            {
                PlayerId = latecomerPlayerId,
                CampaignId = campaignId,
                TeamId = seed.TeamId,
                PlacementOutcome = PlacementOutcome.Assigned,
                ClubId = seed.ClubId,
                CreatedById = actorUserId
            });
            await independent.SaveChangesAsync(CancellationToken.None);
        });

        var service = new TeamManagementService(
            new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, latecomer),
            fixture.CurrentUser,
            NullLogger<TeamManagementService>.Instance);

        var result = await service.UpdateAsync(
            new UpdateTeamInput { TeamId = seed.TeamId, Name = seed.TeamName, GraduationYear = 2030 },
            cancellationToken);

        latecomer.InsertCount.ShouldBe(1);
        result.IsProblem.ShouldBeTrue(
            "a placement for an unlocked player means eligibility cannot be validated, so the update must not commit");
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);

        await using var verify = fixture.CreateAdminContext();
        var graduationYear = await verify.Teams
            .Where(team => team.TeamId == seed.TeamId)
            .Select(team => team.GraduationYear)
            .SingleAsync(cancellationToken);

        graduationYear.ShouldBe(2029, "the rejected update must leave the team unchanged");
    }

    /// <summary>
    /// Reads the campaign that owns the seeded placement.
    /// </summary>
    /// <param name="placementId">The seeded placement identifier.</param>
    /// <returns>The owning campaign identifier.</returns>
    private async Task<long> ReadCampaignIdAsync(long placementId)
    {
        await using var read = fixture.CreateAdminContext();
        return await read.PlayerCampaignAssignments
            .Where(assignment => assignment.PlayerCampaignAssignmentId == placementId)
            .Select(assignment => assignment.CampaignId)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>
    /// Seeds one additional player in the supplied club.
    /// </summary>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="graduationYear">The player's graduation year.</param>
    /// <returns>The seeded player identifier.</returns>
    private async Task<long> SeedPlayerAsync(long clubId, long actorUserId, int graduationYear)
    {
        await using var seed = fixture.CreateAdminContext();
        var player = new PlayerEntity
        {
            FirstName = "Latecomer",
            LastName = $"Player{Guid.CreateVersion7().ToString("N")[..8]}",
            DateOfBirth = new DateOnly(2012, 6, 6),
            GraduationYear = graduationYear,
            ClubId = clubId,
            CreatedById = actorUserId
        };
        seed.Players.Add(player);
        await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        return player.PlayerId;
    }

    /// <summary>
    /// Seeds a club, an active campaign, a team, a player, and one Assigned placement joining them.
    /// </summary>
    /// <param name="actorUserId">The creating user identifier.</param>
    /// <param name="teamGraduationYear">The team's starting graduation year.</param>
    /// <param name="playerGraduationYear">The player's starting graduation year.</param>
    /// <returns>Identifiers and current values needed by the assertions.</returns>
    private async Task<PlacementSeed> SeedPlacementAsync(
        long actorUserId,
        int teamGraduationYear,
        int playerGraduationYear)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var suffix = Guid.CreateVersion7().ToString("N");

        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;

        await using var seed = fixture.CreateAdminContext();

        var club = new ClubEntity
        {
            Name = $"Race Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        seed.Clubs.Add(club);
        await seed.SaveChangesAsync(cancellationToken);

        var season = new SeasonEntity
        {
            Name = $"Race Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        seed.Seasons.Add(season);
        await seed.SaveChangesAsync(cancellationToken);

        var campaign = new CampaignEntity
        {
            Name = $"Race Campaign {suffix}",
            StartDate = new DateOnly(2026, 8, 1),
            Status = CampaignStatus.Active,
            SeasonId = season.SeasonId,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        seed.Campaigns.Add(campaign);

        var team = new TeamEntity
        {
            Name = $"Race Team {suffix}",
            GraduationYear = teamGraduationYear,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        seed.Teams.Add(team);

        var player = new PlayerEntity
        {
            FirstName = "Race",
            LastName = $"Player{suffix[..8]}",
            DateOfBirth = new DateOnly(2012, 5, 5),
            GraduationYear = playerGraduationYear,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        seed.Players.Add(player);
        await seed.SaveChangesAsync(cancellationToken);

        var placement = new PlayerCampaignAssignmentEntity
        {
            PlayerId = player.PlayerId,
            CampaignId = campaign.CampaignId,
            TeamId = team.TeamId,
            PlacementOutcome = PlacementOutcome.Assigned,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        seed.PlayerCampaignAssignments.Add(placement);
        await seed.SaveChangesAsync(cancellationToken);

        return new PlacementSeed(
            club.ClubId,
            team.TeamId,
            team.Name,
            player.PlayerId,
            player.FirstName,
            player.LastName,
            player.DateOfBirth,
            placement.PlayerCampaignAssignmentId);
    }

    /// <summary>
    /// Captures the seeded rows the race assertions operate on.
    /// </summary>
    /// <param name="ClubId">The owning club identifier.</param>
    /// <param name="TeamId">The seeded team identifier.</param>
    /// <param name="TeamName">The seeded team name, resent on update to isolate the year change.</param>
    /// <param name="PlayerId">The seeded player identifier.</param>
    /// <param name="PlayerFirstName">The seeded player first name.</param>
    /// <param name="PlayerLastName">The seeded player last name.</param>
    /// <param name="PlayerDateOfBirth">The seeded player date of birth.</param>
    /// <param name="PlacementId">The seeded Assigned placement identifier.</param>
    private sealed record PlacementSeed(
        long ClubId,
        long TeamId,
        string TeamName,
        long PlayerId,
        string PlayerFirstName,
        string PlayerLastName,
        DateOnly PlayerDateOfBirth,
        long PlacementId);

    /// <summary>
    /// Records the advisory-lock keys a mutation acquires, in acquisition order.
    /// </summary>
    private sealed class AdvisoryLockRecordingInterceptor : DbCommandInterceptor
    {
        private readonly List<long> _acquiredKeys = [];

        /// <summary>Gets the advisory-lock keys acquired so far, in order.</summary>
        public IReadOnlyList<long> AcquiredKeys
        {
            get
            {
                lock (_acquiredKeys)
                {
                    return [.. _acquiredKeys];
                }
            }
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.NonQueryExecutingAsync(command, eventData, result, cancellationToken);
        }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Record(command);
            return base.ReaderExecutingAsync(command, eventData, result, cancellationToken);
        }

        /// <summary>
        /// Captures the lock key when the command is an advisory-lock acquisition.
        /// </summary>
        /// <param name="command">The command about to execute.</param>
        private void Record(DbCommand command)
        {
            if (!command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal))
            {
                return;
            }

            foreach (DbParameter parameter in command.Parameters)
            {
                if (parameter.Value is long key)
                {
                    lock (_acquiredKeys)
                    {
                        _acquiredKeys.Add(key);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Creates tenant contexts against the live Aspire PostgreSQL database.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    private sealed class RaceTestDbContextFactory(NovaAppHostFixture fixture) : IDbContextFactory<NovaDbContext>
    {
        /// <summary>
        /// Creates a tenant context synchronously.
        /// </summary>
        /// <returns>A new tenant context.</returns>
        public NovaDbContext CreateDbContext() => fixture.CreateTenantContext();

        /// <summary>
        /// Creates a tenant context asynchronously.
        /// </summary>
        /// <param name="cancellationToken">A token that cancels context creation.</param>
        /// <returns>A new tenant context.</returns>
        public ValueTask<NovaDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            => ValueTask.FromResult(fixture.CreateTenantContext());
    }
}
