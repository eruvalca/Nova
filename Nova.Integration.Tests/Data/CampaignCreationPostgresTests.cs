using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies campaign creation constraints, retries, rollback, and roster-lock races on PostgreSQL.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class CampaignCreationPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies campaign operation identifiers are unique within a club.
    /// </summary>
    [Fact]
    public async Task CampaignOperationId_RejectsDuplicateWithinClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(includePlayers: false, cancellationToken);
        var operationId = Guid.CreateVersion7();

        await using var db = fixture.CreateAdminContext();
        db.Campaigns.AddRange(
            CreateCampaign("First", seed, operationId),
            CreateCampaign("Second", seed, operationId));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    /// <summary>
    /// Verifies the same campaign operation identifier may be used independently by two clubs.
    /// </summary>
    [Fact]
    public async Task CampaignOperationId_AllowsDuplicateAcrossClubs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstClub = await SeedAsync(includePlayers: false, cancellationToken);
        var secondClub = await SeedAsync(includePlayers: false, cancellationToken);
        var operationId = Guid.CreateVersion7();

        await using var db = fixture.CreateAdminContext();
        db.Campaigns.AddRange(
            CreateCampaign("First Club", firstClub, operationId),
            CreateCampaign("Second Club", secondClub, operationId));

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Verifies inline-season operation identifiers are unique within a club.
    /// </summary>
    [Fact]
    public async Task SeasonOperationId_RejectsDuplicateWithinClub()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(includePlayers: false, cancellationToken);
        var operationId = Guid.CreateVersion7();

        await using var db = fixture.CreateAdminContext();
        db.Seasons.AddRange(
            CreateSeason("First Inline Season", seed, operationId),
            CreateSeason("Second Inline Season", seed, operationId));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    /// <summary>
    /// Verifies the same inline-season operation identifier may be used independently by two clubs.
    /// </summary>
    [Fact]
    public async Task SeasonOperationId_AllowsDuplicateAcrossClubs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstClub = await SeedAsync(includePlayers: false, cancellationToken);
        var secondClub = await SeedAsync(includePlayers: false, cancellationToken);
        var operationId = Guid.CreateVersion7();

        await using var db = fixture.CreateAdminContext();
        db.Seasons.AddRange(
            CreateSeason("First Club Inline Season", firstClub, operationId),
            CreateSeason("Second Club Inline Season", secondClub, operationId));

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Verifies campaign names are unique within one season but may repeat in a different season.
    /// </summary>
    [Fact]
    public async Task CampaignName_RejectsDuplicateWithinSeason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(includePlayers: false, cancellationToken);

        await using var db = fixture.CreateAdminContext();
        db.Campaigns.AddRange(
            CreateCampaign("Shared Name", seed, Guid.CreateVersion7()),
            CreateCampaign("Shared Name", seed, Guid.CreateVersion7()));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    /// <summary>
    /// Verifies campaign names may repeat in different seasons within the same club.
    /// </summary>
    [Fact]
    public async Task CampaignName_AllowsDuplicateInDifferentSeason()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(includePlayers: false, cancellationToken);

        await using var db = fixture.CreateAdminContext();
        var otherSeason = new SeasonEntity
        {
            Name = $"Other Season {seed.Suffix}",
            StartDate = new DateOnly(2027, 1, 1),
            EndDate = new DateOnly(2027, 12, 31),
            ClubId = seed.ClubId,
            CreatedById = seed.ActorUserId
        };
        db.Seasons.Add(otherSeason);
        await db.SaveChangesAsync(cancellationToken);

        db.Campaigns.AddRange(
            CreateCampaign("Shared Name", seed, Guid.CreateVersion7()),
            CreateCampaign(
                "Shared Name",
                seed with { SeasonId = otherSeason.SeasonId },
                Guid.CreateVersion7()));

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Verifies the composite foreign key rejects a campaign linked to another club's season.
    /// </summary>
    [Fact]
    public async Task CampaignSeasonForeignKey_RejectsCrossTenantRelationship()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var firstClub = await SeedAsync(includePlayers: false, cancellationToken);
        var secondClub = await SeedAsync(includePlayers: false, cancellationToken);

        await using var db = fixture.CreateAdminContext();
        db.Campaigns.Add(CreateCampaign(
            "Cross Tenant Campaign",
            firstClub with { SeasonId = secondClub.SeasonId },
            Guid.CreateVersion7()));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    /// <summary>
    /// Verifies an ambiguous commit returns the original inline season, campaign, and participation set.
    /// </summary>
    [Fact]
    public async Task Create_VerifiesCompleteAggregate_AfterAmbiguousCommitFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(includePlayers: true, cancellationToken);
        ActAs(seed.ActorUserId, seed.ClubId, isAdmin: true);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var input = InlineInput(seed.Suffix);

        var result = await CreateCampaignService(factory).CreateAsync(input, cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBe(3);

        await using var verify = fixture.CreateAdminContext();
        var campaigns = await verify.Campaigns
            .Where(campaign => campaign.ClubId == seed.ClubId
                && campaign.CreationOperationId == input.OperationId)
            .ToListAsync(cancellationToken);
        campaigns.Count.ShouldBe(1);

        var seasons = await verify.Seasons
            .Where(season => season.ClubId == seed.ClubId
                && season.CreationOperationId == input.OperationId)
            .ToListAsync(cancellationToken);
        seasons.Count.ShouldBe(1);

        var assignmentCount = await verify.PlayerCampaignAssignments
            .CountAsync(
                assignment => assignment.CampaignId == result.Value.CampaignId,
                cancellationToken);
        assignmentCount.ShouldBe(seed.ActivePlayerCount);
        result.Value.EnrolledPlayerCount.ShouldBe(seed.ActivePlayerCount);
    }

    /// <summary>
    /// Verifies a transient failure after the first save rolls back and retries with a fresh context.
    /// </summary>
    [Fact]
    public async Task Create_RetriesFreshTransaction_AfterTransientSaveFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(includePlayers: true, cancellationToken);
        ActAs(seed.ActorUserId, seed.ClubId, isAdmin: true);

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var input = InlineInput(seed.Suffix);

        var result = await CreateCampaignService(factory).CreateAsync(input, cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBe(4);

        await using var verify = fixture.CreateAdminContext();
        (await verify.Campaigns.CountAsync(
            campaign => campaign.ClubId == seed.ClubId
                && campaign.CreationOperationId == input.OperationId,
            cancellationToken)).ShouldBe(1);
        (await verify.Seasons.CountAsync(
            season => season.ClubId == seed.ClubId
                && season.CreationOperationId == input.OperationId,
            cancellationToken)).ShouldBe(1);
        (await verify.PlayerCampaignAssignments.CountAsync(
            assignment => assignment.CampaignId == result.Value.CampaignId,
            cancellationToken)).ShouldBe(seed.ActivePlayerCount);
    }

    /// <summary>
    /// Verifies a failure before participation persistence rolls back inline season and campaign writes.
    /// </summary>
    [Fact]
    public async Task Create_RollsBackSeasonCampaignAndParticipations_WhenSecondSaveFails()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(includePlayers: true, cancellationToken);
        ActAs(seed.ActorUserId, seed.ClubId, isAdmin: true);

        var failureInterceptor = new FailSecondSaveChangesInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var input = InlineInput(seed.Suffix);

        await Should.ThrowAsync<InvalidOperationException>(
            () => CreateCampaignService(factory).CreateAsync(input, cancellationToken));

        failureInterceptor.FailureCount.ShouldBe(1);
        await using var verify = fixture.CreateAdminContext();
        (await verify.Campaigns.AnyAsync(
            campaign => campaign.ClubId == seed.ClubId
                && campaign.CreationOperationId == input.OperationId,
            cancellationToken)).ShouldBeFalse();
        (await verify.Seasons.AnyAsync(
            season => season.ClubId == seed.ClubId
                && season.CreationOperationId == input.OperationId,
            cancellationToken)).ShouldBeFalse();
    }

    /// <summary>
    /// Verifies either roster-lock winner leaves exactly one participation linking the new player and
    /// campaign.
    /// </summary>
    /// <param name="campaignWinsLock">Whether campaign creation acquires the roster lock first.</param>
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentCampaignAndPlayerCreation_ProducesParticipation_ForEitherLockWinner(
        bool campaignWinsLock)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(includePlayers: false, cancellationToken);
        ActAs(seed.ActorUserId, seed.ClubId, isAdmin: true);

        var campaignGate = new AdvisoryLockGateInterceptor();
        var playerGate = new AdvisoryLockGateInterceptor();
        var campaignFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            campaignGate);
        var playerFactory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            playerGate);
        var playerInput = new CreatePlayerInput
        {
            FirstName = "Concurrent",
            LastName = "Player",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030
        };

        Task<ServiceResult<CreateCampaignResult>> campaignTask;
        Task<ServiceResult<PlayerDto>> playerTask;
        try
        {
            if (campaignWinsLock)
            {
                campaignTask = CreateCampaignService(campaignFactory).CreateAsync(
                    ExistingSeasonInput(seed),
                    cancellationToken);
                await campaignGate.WaitForAcquiredAsync(cancellationToken);

                playerTask = CreatePlayerService(playerFactory).CreateAsync(
                    playerInput,
                    cancellationToken);
                await playerGate.WaitForAttemptAsync(cancellationToken);

                campaignGate.Release();
                await campaignTask;
                await playerGate.WaitForAcquiredAsync(cancellationToken);
                playerGate.Release();
            }
            else
            {
                playerTask = CreatePlayerService(playerFactory).CreateAsync(
                    playerInput,
                    cancellationToken);
                await playerGate.WaitForAcquiredAsync(cancellationToken);

                campaignTask = CreateCampaignService(campaignFactory).CreateAsync(
                    ExistingSeasonInput(seed),
                    cancellationToken);
                await campaignGate.WaitForAttemptAsync(cancellationToken);

                playerGate.Release();
                await playerTask;
                await campaignGate.WaitForAcquiredAsync(cancellationToken);
                campaignGate.Release();
            }
        }
        finally
        {
            campaignGate.Release();
            playerGate.Release();
        }

        await Task.WhenAll(campaignTask, playerTask);
        var campaignResult = await campaignTask;
        var playerResult = await playerTask;
        campaignResult.IsSuccess.ShouldBeTrue();
        playerResult.IsSuccess.ShouldBeTrue();

        await using var verify = fixture.CreateAdminContext();
        var assignments = await verify.PlayerCampaignAssignments
            .Where(assignment => assignment.CampaignId == campaignResult.Value.CampaignId
                && assignment.PlayerId == playerResult.Value.PlayerId)
            .ToListAsync(cancellationToken);
        assignments.Count.ShouldBe(1);
        assignments[0].PlacementOutcome.ShouldBe(PlacementOutcome.Undecided);
    }

    /// <summary>
    /// Creates the campaign service with the supplied tenant context factory.
    /// </summary>
    /// <param name="factory">The context factory used for execution attempts.</param>
    /// <returns>A campaign creation service.</returns>
    private CampaignCreationService CreateCampaignService(IDbContextFactory<NovaDbContext> factory)
        => new(
            factory,
            fixture.CurrentUser,
            NullLogger<CampaignCreationService>.Instance);

    /// <summary>
    /// Creates the player service used by the shared roster-lock race.
    /// </summary>
    /// <returns>A player management service.</returns>
    private PlayerManagementService CreatePlayerService(
        IDbContextFactory<NovaDbContext>? factory = null)
        => new(
            factory ?? new FixtureDbContextFactory(fixture),
            fixture.CurrentUser,
            NullLogger<PlayerManagementService>.Instance);

    /// <summary>
    /// Sets the current tenant identity used by newly created contexts.
    /// </summary>
    /// <param name="userId">The acting user identifier.</param>
    /// <param name="clubId">The acting club identifier.</param>
    /// <param name="isAdmin">Whether the actor is a club administrator.</param>
    private void ActAs(long? userId, long? clubId, bool isAdmin)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = isAdmin;
    }

    /// <summary>
    /// Creates a campaign entity for direct constraint tests.
    /// </summary>
    /// <param name="name">The campaign name.</param>
    /// <param name="seed">The owning club and season.</param>
    /// <param name="operationId">The creation operation identifier.</param>
    /// <returns>A campaign ready for insertion.</returns>
    private static CampaignEntity CreateCampaign(
        string name,
        CampaignCreationSeed seed,
        Guid operationId)
        => new()
        {
            CreationOperationId = operationId,
            Name = name,
            StartDate = new DateOnly(2026, 6, 1),
            EndDate = new DateOnly(2026, 6, 30),
            Status = CampaignStatus.Active,
            ClubId = seed.ClubId,
            SeasonId = seed.SeasonId,
            CreatedById = seed.ActorUserId
        };

    private static SeasonEntity CreateSeason(
        string name,
        CampaignCreationSeed seed,
        Guid operationId)
        => new()
        {
            CreationOperationId = operationId,
            Name = $"{name} {seed.Suffix}",
            StartDate = new DateOnly(2027, 1, 1),
            EndDate = new DateOnly(2027, 12, 31),
            ClubId = seed.ClubId,
            CreatedById = seed.ActorUserId
        };

    /// <summary>
    /// Creates a valid inline-season campaign request.
    /// </summary>
    /// <param name="suffix">A value that keeps names isolated in the shared database.</param>
    /// <returns>A valid inline campaign request.</returns>
    private static CreateCampaignInput InlineInput(string suffix) => new()
    {
        OperationId = Guid.CreateVersion7(),
        Name = $"Campaign {suffix}",
        StartDate = new DateOnly(2026, 6, 1),
        PlannedEndDate = new DateOnly(2026, 6, 30),
        InlineSeason = new InlineSeasonInput
        {
            Name = $"Inline Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31)
        }
    };

    /// <summary>
    /// Creates a valid request targeting the seeded season.
    /// </summary>
    /// <param name="seed">The owning club and season.</param>
    /// <returns>A valid existing-season request.</returns>
    private static CreateCampaignInput ExistingSeasonInput(CampaignCreationSeed seed) => new()
    {
        OperationId = Guid.CreateVersion7(),
        Name = $"Concurrent Campaign {seed.Suffix}",
        StartDate = new DateOnly(2026, 6, 1),
        PlannedEndDate = new DateOnly(2026, 6, 30),
        ExistingSeasonId = seed.SeasonId
    };

    /// <summary>
    /// Seeds one isolated club and season with optional Active and Archived players.
    /// </summary>
    /// <param name="includePlayers">Whether to seed two Active players and one Archived player.</param>
    /// <param name="cancellationToken">A token that cancels seeding.</param>
    /// <returns>The generated tenant identifiers and expected Active player count.</returns>
    private async Task<CampaignCreationSeed> SeedAsync(
        bool includePlayers,
        CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null, isAdmin: false);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var actorUserId = Random.Shared.NextInt64(1, long.MaxValue);
        var club = new ClubEntity
        {
            Name = $"Campaign Creation Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = actorUserId
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(cancellationToken);

        var season = new SeasonEntity
        {
            Name = $"Existing Season {suffix}",
            StartDate = new DateOnly(2026, 1, 1),
            EndDate = new DateOnly(2026, 12, 31),
            ClubId = club.ClubId,
            CreatedById = actorUserId
        };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(cancellationToken);

        var activePlayerCount = 0;
        if (includePlayers)
        {
            db.Players.AddRange(
                CreatePlayer("Active One", LifecycleStatus.Active, club.ClubId, actorUserId),
                CreatePlayer("Active Two", LifecycleStatus.Active, club.ClubId, actorUserId),
                CreatePlayer("Archived", LifecycleStatus.Archived, club.ClubId, actorUserId));
            await db.SaveChangesAsync(cancellationToken);
            activePlayerCount = 2;
        }

        return new CampaignCreationSeed(
            club.ClubId,
            season.SeasonId,
            actorUserId,
            suffix,
            activePlayerCount);
    }

    /// <summary>
    /// Creates a player for campaign creation tests.
    /// </summary>
    /// <param name="firstName">The player first name.</param>
    /// <param name="status">The player lifecycle status.</param>
    /// <param name="clubId">The owning club identifier.</param>
    /// <param name="actorUserId">The seeding actor identifier.</param>
    /// <returns>A player ready for insertion.</returns>
    private static PlayerEntity CreatePlayer(
        string firstName,
        LifecycleStatus status,
        long clubId,
        long actorUserId)
        => new()
        {
            FirstName = firstName,
            LastName = "Player",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030,
            LifecycleStatus = status,
            ArchivedAt = status == LifecycleStatus.Archived ? DateTimeOffset.UtcNow : null,
            ArchivedById = status == LifecycleStatus.Archived ? actorUserId : null,
            ClubId = clubId,
            CreatedById = actorUserId
        };

    /// <summary>
    /// Adapts the fixture context creation to the service factory contract.
    /// </summary>
    /// <param name="fixture">The shared AppHost fixture.</param>
    private sealed class FixtureDbContextFactory(NovaAppHostFixture fixture)
        : IDbContextFactory<NovaDbContext>
    {
        /// <inheritdoc />
        public NovaDbContext CreateDbContext() => fixture.CreateTenantContext();

        /// <inheritdoc />
        public Task<NovaDbContext> CreateDbContextAsync(
            CancellationToken cancellationToken = default)
            => Task.FromResult(fixture.CreateTenantContext());
    }

    /// <summary>
    /// Holds one test's tenant identifiers and expected Active roster size.
    /// </summary>
    /// <param name="ClubId">The seeded club identifier.</param>
    /// <param name="SeasonId">The seeded existing season identifier.</param>
    /// <param name="ActorUserId">The simulated club administrator identifier.</param>
    /// <param name="Suffix">The unique data suffix.</param>
    /// <param name="ActivePlayerCount">The number of Active players seeded.</param>
    private sealed record CampaignCreationSeed(
        long ClubId,
        long SeasonId,
        long ActorUserId,
        string Suffix,
        int ActivePlayerCount);
}
