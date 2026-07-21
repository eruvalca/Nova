using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Shared.Enums;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies campaign participation migrations, PostgreSQL constraints, filtered uniqueness, and concurrency.
/// </summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignParticipationPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies the clean Aspire database applied the campaign participation migration.
    /// </summary>
    [Fact]
    public async Task Migration_AppliesCampaignParticipationIntegritySchema()
    {
        await using var db = fixture.CreateTenantContext();

        var appliedMigrations = await db.Database.GetAppliedMigrationsAsync(TestContext.Current.CancellationToken);

        appliedMigrations.ShouldContain(migration => migration.EndsWith("_AddCampaignParticipationIntegrity", StringComparison.Ordinal));
    }

    /// <summary>
    /// Verifies PostgreSQL rejects a second enrollment for the same campaign and player.
    /// </summary>
    [Fact]
    public async Task UniqueEnrollment_RejectsDuplicateCampaignPlayer()
    {
        var data = await SeedAsync(playerCount: 2, initialTryoutNumber: null);
        ActAs(data.ActorUserId, data.ClubId, isClubAdmin: true);
        await using var db = fixture.CreateTenantContext();
        db.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
        {
            PlayerId = data.PlayerIds[0],
            CampaignId = data.CampaignIds[0],
            ClubId = default,
            CreatedById = default
        });

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies the filtered tryout index permits nulls and cross-campaign reuse but rejects a same-campaign duplicate.
    /// </summary>
    [Fact]
    public async Task TryoutNumberIndex_EnforcesCampaignScopedNonNullUniqueness()
    {
        var data = await SeedAsync(playerCount: 4, initialTryoutNumber: 42);
        ActAs(data.ActorUserId, data.ClubId, isClubAdmin: true);

        await using (var allowed = fixture.CreateTenantContext())
        {
            allowed.PlayerCampaignAssignments.AddRange(
                new PlayerCampaignAssignmentEntity
                {
                    PlayerId = data.PlayerIds[1],
                    CampaignId = data.CampaignIds[0],
                    TryoutNumber = null,
                    ClubId = default,
                    CreatedById = default
                },
                new PlayerCampaignAssignmentEntity
                {
                    PlayerId = data.PlayerIds[2],
                    CampaignId = data.CampaignIds[0],
                    TryoutNumber = null,
                    ClubId = default,
                    CreatedById = default
                },
                new PlayerCampaignAssignmentEntity
                {
                    PlayerId = data.PlayerIds[0],
                    CampaignId = data.CampaignIds[1],
                    TryoutNumber = 42,
                    ClubId = default,
                    CreatedById = default
                });
            await allowed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var duplicate = fixture.CreateTenantContext();
        duplicate.PlayerCampaignAssignments.Add(new PlayerCampaignAssignmentEntity
        {
            PlayerId = data.PlayerIds[3],
            CampaignId = data.CampaignIds[0],
            TryoutNumber = 42,
            ClubId = default,
            CreatedById = default
        });

        await Should.ThrowAsync<DbUpdateException>(
            () => duplicate.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies PostgreSQL rejects invalid outcome/team combinations and undefined outcome values.
    /// </summary>
    /// <param name="outcomeValue">The numeric placement outcome to persist.</param>
    /// <param name="useTeam">Whether to persist a team reference.</param>
    [Theory]
    [InlineData((int)PlacementOutcome.Assigned, false)]
    [InlineData((int)PlacementOutcome.Undecided, true)]
    [InlineData((int)PlacementOutcome.NotSelected, true)]
    [InlineData((int)PlacementOutcome.Withdrawn, true)]
    [InlineData(99, false)]
    public async Task OutcomeTeamConstraint_RejectsInvalidCombination(int outcomeValue, bool useTeam)
    {
        var data = await SeedAsync(playerCount: 1, initialTryoutNumber: null);
        ActAs(data.ActorUserId, data.ClubId, isClubAdmin: true);
        await using var db = fixture.CreateTenantContext();
        var participation = await db.PlayerCampaignAssignments
            .SingleAsync(
                assignment => assignment.PlayerCampaignAssignmentId == data.AssignmentId,
                TestContext.Current.CancellationToken);
        participation.PlacementOutcome = (PlacementOutcome)outcomeValue;
        participation.TeamId = useTeam ? data.TeamId : null;
        participation.ConcurrencyToken = Guid.NewGuid();

        await Should.ThrowAsync<DbUpdateException>(
            () => db.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Verifies EF's application-managed token prevents a stale context from overwriting a newer placement.
    /// </summary>
    [Fact]
    public async Task ConcurrencyToken_RejectsStalePlacementUpdate()
    {
        var data = await SeedAsync(playerCount: 1, initialTryoutNumber: null);
        ActAs(data.ActorUserId, data.ClubId, isClubAdmin: true);
        await using var first = fixture.CreateTenantContext();
        await using var stale = fixture.CreateTenantContext();

        var firstCopy = await first.PlayerCampaignAssignments
            .SingleAsync(
                assignment => assignment.PlayerCampaignAssignmentId == data.AssignmentId,
                TestContext.Current.CancellationToken);
        var staleCopy = await stale.PlayerCampaignAssignments
            .SingleAsync(
                assignment => assignment.PlayerCampaignAssignmentId == data.AssignmentId,
                TestContext.Current.CancellationToken);

        firstCopy.PlacementOutcome = PlacementOutcome.NotSelected;
        firstCopy.ConcurrencyToken = Guid.NewGuid();
        await first.SaveChangesAsync(TestContext.Current.CancellationToken);

        staleCopy.PlacementOutcome = PlacementOutcome.Withdrawn;
        staleCopy.ConcurrencyToken = Guid.NewGuid();

        await Should.ThrowAsync<DbUpdateConcurrencyException>(
            () => stale.SaveChangesAsync(TestContext.Current.CancellationToken));
    }

    /// <summary>
    /// Seeds one club with two campaigns, placement teams, players, and an initial participation.
    /// </summary>
    /// <param name="playerCount">The number of players to seed.</param>
    /// <param name="initialTryoutNumber">The initial participation's tryout number.</param>
    /// <returns>Database-generated identifiers for the seeded graph.</returns>
    private async Task<CampaignParticipationSeed> SeedAsync(int playerCount, int? initialTryoutNumber)
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var suffix = Guid.NewGuid().ToString("N");

        var club = new ClubEntity
        {
            Name = $"Participation Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var season = new SeasonEntity
        {
            Name = $"Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Seasons.Add(season);

        var players = Enumerable.Range(0, playerCount)
            .Select(index => new PlayerEntity
            {
                FirstName = $"Player{index}",
                LastName = suffix,
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            })
            .ToArray();
        db.Players.AddRange(players);

        var team = new TeamEntity
        {
            Name = $"Team {suffix}",
            GraduationYear = 2029,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Teams.Add(team);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        CampaignEntity[] campaigns =
        [
            new()
            {
                Name = $"Campaign A {suffix}",
                StartDate = new DateOnly(2026, 6, 1),
                SeasonId = season.SeasonId,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            },
            new()
            {
                Name = $"Campaign B {suffix}",
                StartDate = new DateOnly(2026, 7, 1),
                SeasonId = season.SeasonId,
                ClubId = club.ClubId,
                CreatedById = actorUserId
            }
        ];
        db.Campaigns.AddRange(campaigns);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        var assignment = new PlayerCampaignAssignmentEntity
        {
            PlayerId = players[0].PlayerId,
            CampaignId = campaigns[0].CampaignId,
            TryoutNumber = initialTryoutNumber,
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.PlayerCampaignAssignments.Add(assignment);
        await db.SaveChangesAsync(TestContext.Current.CancellationToken);

        return new CampaignParticipationSeed(
            club.ClubId,
            actorUserId,
            players.Select(player => player.PlayerId).ToArray(),
            campaigns.Select(campaign => campaign.CampaignId).ToArray(),
            team.TeamId,
            assignment.PlayerCampaignAssignmentId);
    }

    /// <summary>
    /// Sets the simulated current user for subsequent contexts.
    /// </summary>
    /// <param name="userId">The current user identifier, or null for anonymous.</param>
    /// <param name="clubId">The current club identifier, or null for no club.</param>
    /// <param name="isClubAdmin">Whether the user is a club administrator.</param>
    private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    /// <summary>
    /// Carries identifiers for a seeded campaign-participation graph.
    /// </summary>
    /// <param name="ClubId">The seeded club identifier.</param>
    /// <param name="ActorUserId">The simulated acting user identifier.</param>
    /// <param name="PlayerIds">The seeded player identifiers.</param>
    /// <param name="CampaignIds">The seeded campaign identifiers.</param>
    /// <param name="TeamId">The seeded eligible team identifier.</param>
    /// <param name="AssignmentId">The initial participation identifier.</param>
    private sealed record CampaignParticipationSeed(
        long ClubId,
        long ActorUserId,
        long[] PlayerIds,
        long[] CampaignIds,
        long TeamId,
        long AssignmentId);
}
