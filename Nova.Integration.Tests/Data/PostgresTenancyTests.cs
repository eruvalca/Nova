using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Tenancy tests that run against the real PostgreSQL provider via the Aspire AppHost, covering
/// behavior the SQLite-based unit suite cannot: the production migrations, Npgsql's
/// <c>timestamptz</c> mapping of the <see cref="DateTimeOffset"/> audit fields, <see cref="DateOnly"/>
/// round-trips, and the SQL translation of the tenant and bespoke query filters.
/// Each test seeds its own clubs/users/players with database-generated ids, so the tenant filters
/// naturally isolate tests from one another's data.
/// </summary>
[Collection(NovaAppHostCollection.Name)]
public class PostgresTenancyTests(NovaAppHostFixture fixture)
{
    private long _clubAId;
    private long _clubBId;
    private long _clubAMember1Id;
    private long _clubAMember2Id;
    private long _clubBMemberId;
    private long _noClubUserId;

    /// <summary>
    /// Seeds two clubs, their members, players, and a pending join request through the admin
    /// context, capturing the database-generated ids for use by the test.
    /// </summary>
    private async Task SeedAsync()
    {
        ActAs(userId: null, clubId: null);
        await using var context = fixture.CreateAdminContext();

        NovaUserEntity[] users =
        [
            new() { FirstName = "Alice", LastName = "A" },
            new() { FirstName = "Aaron", LastName = "A" },
            new() { FirstName = "Bob", LastName = "B" },
            new() { FirstName = "Nadia", LastName = "N" },
        ];
        context.Users.AddRange(users);
        await context.SaveChangesAsync();

        _clubAMember1Id = users[0].Id;
        _clubAMember2Id = users[1].Id;
        _clubBMemberId = users[2].Id;
        _noClubUserId = users[3].Id;

        var clubA = new ClubEntity { Name = "Club A", City = "Austin", State = "TX", CreatedById = _noClubUserId };
        var clubB = new ClubEntity { Name = "Club B", City = "Boston", State = "MA", CreatedById = _noClubUserId };
        context.Clubs.AddRange(clubA, clubB);
        await context.SaveChangesAsync();

        _clubAId = clubA.ClubId;
        _clubBId = clubB.ClubId;

        users[0].ClubId = _clubAId;
        users[1].ClubId = _clubAId;
        users[2].ClubId = _clubBId;

        context.Players.AddRange(
            new PlayerEntity { FirstName = "PA", LastName = "One", DateOfBirth = new DateOnly(2010, 1, 1), GraduationYear = 2028, ClubId = _clubAId, CreatedById = _clubAMember1Id },
            new PlayerEntity { FirstName = "PA", LastName = "Two", DateOfBirth = new DateOnly(2011, 2, 2), GraduationYear = 2029, ClubId = _clubAId, CreatedById = _clubAMember1Id },
            new PlayerEntity { FirstName = "PB", LastName = "One", DateOfBirth = new DateOnly(2012, 3, 3), GraduationYear = 2030, ClubId = _clubBId, CreatedById = _clubBMemberId });

        // Pending request from the club-less user to join Club A.
        context.ClubJoinRequests.Add(
            new ClubJoinRequestEntity { ClubId = _clubAId, RequestingUserId = _noClubUserId, CreatedById = _noClubUserId });

        await context.SaveChangesAsync();
    }

    /// <summary>
    /// Points the shared current-user provider at the given simulated user.
    /// </summary>
    /// <param name="userId">The simulated user id, or <see langword="null"/> for anonymous.</param>
    /// <param name="clubId">The simulated club id, or <see langword="null"/> for no club.</param>
    /// <param name="isClubAdmin">Whether the simulated user is a club admin.</param>
    private void ActAs(long? userId, long? clubId, bool isClubAdmin = false)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isClubAdmin;
    }

    /// <summary>
    /// Verifies the model and the applied production migrations agree on the real provider —
    /// the SQLite suite uses <c>EnsureCreated()</c> and never exercises the migrations.
    /// </summary>
    [Fact]
    public async Task Database_HasNoPendingMigrations()
    {
        await using var context = fixture.CreateTenantContext();

        var pending = await context.Database.GetPendingMigrationsAsync(TestContext.Current.CancellationToken);

        pending.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies the generic tenant query filter translates and executes correctly as Postgres SQL.
    /// </summary>
    [Fact]
    public async Task TenantContext_FiltersPlayersToCurrentClub()
    {
        await SeedAsync();
        ActAs(_clubAMember1Id, _clubAId);
        await using var context = fixture.CreateTenantContext();

        var players = await context.Players.ToListAsync(TestContext.Current.CancellationToken);

        players.Count.ShouldBe(2);
        players.ShouldAllBe(p => p.ClubId == _clubAId);
    }

    /// <summary>
    /// Verifies the bespoke <see cref="ClubJoinRequestEntity"/> filter (requester sees own;
    /// target club admin sees their club's; everyone else sees none) translates on Postgres.
    /// </summary>
    [Fact]
    public async Task JoinRequests_BespokeFilter_TranslatesOnPostgres()
    {
        await SeedAsync();

        ActAs(_noClubUserId, clubId: null);
        await using (var context = fixture.CreateTenantContext())
        {
            var requests = await context.ClubJoinRequests.ToListAsync(TestContext.Current.CancellationToken);
            requests.Count.ShouldBe(1);
            requests[0].RequestingUserId.ShouldBe(_noClubUserId);
        }

        ActAs(_clubAMember1Id, _clubAId, isClubAdmin: true);
        await using (var context = fixture.CreateTenantContext())
        {
            (await context.ClubJoinRequests.CountAsync(r => r.ClubId == _clubAId, TestContext.Current.CancellationToken)).ShouldBe(1);
        }

        ActAs(_clubAMember2Id, _clubAId, isClubAdmin: false);
        await using (var context = fixture.CreateTenantContext())
        {
            (await context.ClubJoinRequests.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        }

        ActAs(_clubBMemberId, _clubBId, isClubAdmin: true);
        await using (var context = fixture.CreateTenantContext())
        {
            (await context.ClubJoinRequests.CountAsync(TestContext.Current.CancellationToken)).ShouldBe(0);
        }
    }

    /// <summary>
    /// Verifies the interceptor's <see cref="DateTimeOffset"/> audit stamps survive Npgsql's
    /// <c>timestamptz</c> mapping (which requires UTC offsets) and that <see cref="DateOnly"/>
    /// round-trips through the Postgres <c>date</c> type.
    /// </summary>
    [Fact]
    public async Task Interceptor_AuditStampsAndDateOnly_RoundTripThroughPostgres()
    {
        await SeedAsync();
        ActAs(_clubAMember1Id, _clubAId);

        var dateOfBirth = new DateOnly(2013, 4, 4);
        long playerId;
        await using (var context = fixture.CreateTenantContext())
        {
            var player = new PlayerEntity
            {
                FirstName = "New",
                LastName = "Player",
                DateOfBirth = dateOfBirth,
                GraduationYear = 2031,
                ClubId = default,
                CreatedById = default,
            };
            context.Players.Add(player);
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            playerId = player.PlayerId;
        }

        await using (var context = fixture.CreateAdminContext())
        {
            var reloaded = await context.Players.SingleAsync(p => p.PlayerId == playerId, TestContext.Current.CancellationToken);

            reloaded.ClubId.ShouldBe(_clubAId);
            reloaded.CreatedById.ShouldBe(_clubAMember1Id);
            reloaded.DateOfBirth.ShouldBe(dateOfBirth);
            reloaded.CreatedAt.Offset.ShouldBe(TimeSpan.Zero);
            reloaded.CreatedAt.ShouldBeGreaterThan(DateTimeOffset.UtcNow.AddMinutes(-1));
        }
    }

    /// <summary>
    /// Verifies update stamping round-trips through Postgres for an entity modified via the
    /// tenant context.
    /// </summary>
    [Fact]
    public async Task Interceptor_StampsModifiedFields_OnUpdate()
    {
        await SeedAsync();
        ActAs(_clubAMember1Id, _clubAId);

        long playerId;
        await using (var context = fixture.CreateTenantContext())
        {
            var player = await context.Players.OrderBy(p => p.PlayerId).FirstAsync(TestContext.Current.CancellationToken);
            player.JerseyNumber = 42;
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
            playerId = player.PlayerId;
        }

        await using (var context = fixture.CreateAdminContext())
        {
            var reloaded = await context.Players.SingleAsync(p => p.PlayerId == playerId, TestContext.Current.CancellationToken);

            reloaded.ModifiedAt.ShouldNotBeNull();
            reloaded.ModifiedAt.Value.Offset.ShouldBe(TimeSpan.Zero);
            reloaded.ModifiedById.ShouldBe(_clubAMember1Id);
        }
    }

    /// <summary>
    /// Verifies the interceptor blocks cross-tenant writes before they reach the real database.
    /// </summary>
    [Fact]
    public async Task Interceptor_Throws_OnCrossTenantAdd()
    {
        await SeedAsync();
        ActAs(_clubAMember1Id, _clubAId);
        await using var context = fixture.CreateTenantContext();

        context.Players.Add(new PlayerEntity
        {
            FirstName = "Sneaky",
            LastName = "Player",
            DateOfBirth = new DateOnly(2013, 5, 5),
            GraduationYear = 2031,
            ClubId = _clubBId,
            CreatedById = _clubAMember1Id,
        });

        var exception = await Should.ThrowAsync<InvalidOperationException>(
            () => context.SaveChangesAsync(TestContext.Current.CancellationToken));
        exception.Message.ShouldContain("Cross-tenant");

        await using var verify = fixture.CreateAdminContext();
        (await verify.Players.CountAsync(p => p.FirstName == "Sneaky" && p.ClubId == _clubBId, TestContext.Current.CancellationToken)).ShouldBe(0);
    }
}
