using System.Data.Common;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Entities;
using Nova.Features.Campaigns;
using Nova.Features.Players;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Players;
using Nova.Shared.Security;
using Shouldly;

namespace Nova.Integration.Tests.Data;

/// <summary>Exercises import transaction durability and real PostgreSQL lock contention.</summary>
[Collection(NovaAppHostCollection.Name)]
public sealed class PlayerImportCommitPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>Provides isolated data-protection keys for this test instance.</summary>
    private readonly EphemeralDataProtectionProvider _protection = new();

    /// <summary>Verifies that a late save failure rolls back players, enrollment, and proof together.</summary>
    [Fact]
    public async Task Commit_RollsBackEntireBatch_WhenSecondSaveFails()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Active, ct);
        ActAs(seed);
        var failure = new FailSecondSaveChangesInterceptor();
        var service = CreateService(failure);
        var input = await PreviewAsync(service, Upload("First", "Second"), ct);

        await Should.ThrowAsync<InvalidOperationException>(() => service.CommitAsync(input, ct));

        failure.FailureCount.ShouldBe(1);
        await AssertCountsAsync(seed, players: 0, enrollments: 0, receipts: 0, ct);
    }

    /// <summary>Verifies exactly one complete aggregate after either provider retry failure mode.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commit_PersistsExactlyOnce_AfterTransientFailure(bool lostAcknowledgement)
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Active, ct);
        ActAs(seed);
        var before = new FailFirstSaveChangesInterceptor();
        var after = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingTenantDbContextFactory(
            fixture.ConnectionString, fixture.CurrentUser, lostAcknowledgement ? after : before);
        var service = CreateService(factory);
        var input = await PreviewAsync(service, Upload("First", "Second"), ct);

        var result = await service.CommitAsync(input, ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedRows.ShouldBe(2);
        result.Value.EnrolledPlayers.ShouldBe(2);
        (lostAcknowledgement ? after.FailureCount : before.FailureCount).ShouldBe(1);
        factory.CreatedContextCount.ShouldBeGreaterThanOrEqualTo(3);
        await AssertCountsAsync(seed, players: 2, enrollments: 2, receipts: 1, ct);
        var recovered = await service.CommitAsync(input, ct);
        recovered.IsSuccess.ShouldBeTrue();
        JsonSerializer.Serialize(recovered.Value).ShouldBe(JsonSerializer.Serialize(result.Value));
        await using var verify = fixture.CreateAdminContext();
        var assignments = await verify.PlayerCampaignAssignments.Where(x => x.ClubId == seed.ClubId).ToListAsync(ct);
        assignments.Select(x => x.PlayerId).Order().ShouldBe(result.Value.Rows.Select(x => x.PlayerId!.Value).Order());
        assignments.ShouldAllBe(x => x.PlacementOutcome == PlacementOutcome.Undecided && x.TeamId == null);
        (await verify.ActivityEvents.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(0);
    }

    /// <summary>Verifies simultaneous exact requests recover the winner and overlapping previews block new duplicates.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(false, true)]
    public async Task Commit_SerializesCompetingImports_WithoutDuplicatePlayers(bool sameOperation, bool distinctActors)
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Active, ct);
        var secondActorId = distinctActors ? await SeedAdditionalAdministratorAsync(seed.ClubId, ct) : seed.ActorUserId;
        ActAs(seed);
        var gate = new AdvisoryLockGateInterceptor(advisoryLocksToSkip: 3);
        var firstService = CreateService(gate);
        var secondService = CreateService(new NoOpInterceptor());
        var firstInput = await PreviewAsync(firstService, Upload("Shared", "FirstOnly"), ct);
        ActAs(seed with { ActorUserId = secondActorId });
        var secondInput = sameOperation ? firstInput : await PreviewAsync(secondService, Upload("Shared", "SecondOnly"), ct);
        ActAs(seed);
        var first = firstService.CommitAsync(firstInput, ct);
        await gate.WaitForAcquiredAsync(ct);
        ActAs(seed with { ActorUserId = secondActorId });
        var second = secondService.CommitAsync(secondInput, ct);
        try
        {
            await using var probe = fixture.CreateAdminContext();
            // Distinct actors contend on club membership; the same actor waits earlier on user membership.
            await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
                probe, distinctActors ? (long.MinValue / 32) + seed.ClubId : (long.MinValue / 64) + seed.ActorUserId, ct);
        }
        finally
        {
            gate.Release();
        }

        var firstResult = await first;
        var secondResult = await second;
        firstResult.IsSuccess.ShouldBeTrue();
        secondResult.IsSuccess.ShouldBeTrue();
        firstResult.Value.CreatedRows.ShouldBe(2);
        if (sameOperation)
        {
            JsonSerializer.Serialize(secondResult.Value).ShouldBe(JsonSerializer.Serialize(firstResult.Value));
            await AssertCountsAsync(seed, 2, 2, 1, ct);
        }
        else
        {
            secondResult.Value.CreatedRows.ShouldBe(1);
            secondResult.Value.BlockedRows.ShouldBe(1);
            secondResult.Value.Rows[0].Status.ShouldBe(PlayerImportCommitRowStatus.BlockedAtCommit);
            secondResult.Value.Rows[0].Duplicate.ShouldNotBeNull();
            await AssertCountsAsync(seed, 3, 3, 2, ct);
        }
    }

    /// <summary>Verifies import rechecks identities after a competing manual create or profile update commits.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Commit_BlocksNewDuplicate_AfterContendingManualMutation(bool update)
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Draft, ct);
        ActAs(seed);
        var service = CreateService(new NoOpInterceptor());
        var input = await PreviewAsync(service, Upload("Shared"), ct);
        long playerId = 0;
        if (update)
        {
            await using var db = fixture.CreateAdminContext();
            var player = new PlayerEntity
            {
                CreationOperationId = Guid.CreateVersion7(),
                FirstName = "Before",
                LastName = "Import",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = seed.ClubId,
                CreatedById = seed.ActorUserId
            };
            db.Players.Add(player);
            await db.SaveChangesAsync(ct);
            playerId = player.PlayerId;
        }
        var gate = new AdvisoryLockGateInterceptor();
        var manual = new PlayerManagementService(
            new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, gate),
            fixture.CurrentUser, NullLogger<PlayerManagementService>.Instance);
        var mutation = update
            ? CompleteManualUpdateAsync(manual, playerId, ct)
            : CompleteManualCreateAsync(manual, ct);
        await gate.WaitForAcquiredAsync(ct);
        var commit = service.CommitAsync(input, ct);
        try
        {
            await using var probe = fixture.CreateAdminContext();
            await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
                probe, (long.MinValue / 4) + seed.ClubId, ct);
        }
        finally
        {
            gate.Release();
        }
        await mutation;
        var result = await commit;
        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedRows.ShouldBe(0);
        result.Value.BlockedRows.ShouldBe(1);
        result.Value.Rows[0].Duplicate.ShouldNotBeNull();
        await AssertCountsAsync(seed, 1, 0, 1, ct);
    }

    /// <summary>Verifies import sees campaign state committed by a lifecycle transaction it actually waited for.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("open")]
    [InlineData("close")]
    [InlineData("reopen")]
    public async Task Commit_UsesAuthoritativeCampaign_AfterContendingLifecycleChange(string transition)
    {
        var ct = TestContext.Current.CancellationToken;
        var initial = transition == "open" ? CampaignStatus.Draft
            : transition == "close" ? CampaignStatus.Active : CampaignStatus.Closed;
        var seed = await SeedAsync(initial, ct);
        ActAs(seed);
        if (transition == "open")
        {
            await using var db = fixture.CreateAdminContext();
            db.Players.Add(new PlayerEntity
            {
                CreationOperationId = Guid.CreateVersion7(),
                FirstName = "Opening",
                LastName = "Sentinel",
                DateOfBirth = new DateOnly(2012, 1, 1),
                GraduationYear = 2030,
                ClubId = seed.ClubId,
                CreatedById = seed.ActorUserId
            });
            await db.SaveChangesAsync(ct);
        }
        var service = CreateService(new NoOpInterceptor());
        var input = await PreviewAsync(service, Upload("First", "Second"), ct);
        var gate = new AdvisoryLockGateInterceptor();
        ICampaignLifecycleService campaign = new CampaignLifecycleService(
            new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, gate),
            fixture.CurrentUser, NullLogger<CampaignLifecycleService>.Instance);
        var change = ChangeCampaignAsync(campaign, seed.CampaignId, transition, ct);
        await gate.WaitForAcquiredAsync(ct);
        var commit = service.CommitAsync(input, ct);
        try
        {
            await using var probe = fixture.CreateAdminContext();
            var lockKey = transition == "close" ? long.MinValue + seed.CampaignId : (long.MinValue / 16) + seed.ClubId;
            await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(probe, lockKey, ct);
        }
        finally
        {
            gate.Release();
        }
        await change;
        var result = await commit;
        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedRows.ShouldBe(2);
        result.Value.EnrolledPlayers.ShouldBe(transition == "close" ? 0 : 2);
        result.Value.WaitingPlayers.ShouldBe(transition == "close" ? 2 : 0);
        result.Value.CampaignId.ShouldBe(transition == "close" ? null : seed.CampaignId);
        await AssertCountsAsync(seed, transition == "open" ? 3 : 2,
            transition == "close" ? 0 : transition == "open" ? 3 : 2, 1, ct);
    }

    /// <summary>Verifies opening and closure use the roster committed by a winning import.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CampaignTransition_ObservesImportedPlayers_AfterWaitingForImport(bool close)
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(close ? CampaignStatus.Active : CampaignStatus.Draft, ct);
        ActAs(seed);
        // Hold campaign for close; hold roster (with season already held) for opening.
        var gate = new AdvisoryLockGateInterceptor(advisoryLocksToSkip: close ? 4 : 3);
        var service = CreateService(gate);
        var input = await PreviewAsync(service, Upload("First", "Second"), ct);
        var commit = service.CommitAsync(input, ct);
        await gate.WaitForAcquiredAsync(ct);
        ICampaignLifecycleService campaign = new CampaignLifecycleService(
            new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, new NoOpInterceptor()),
            fixture.CurrentUser, NullLogger<CampaignLifecycleService>.Instance);
        var change = AssertCampaignAfterImportAsync(campaign, seed.CampaignId, close, ct);
        try
        {
            await using var probe = fixture.CreateAdminContext();
            await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
                probe, close ? long.MinValue + seed.CampaignId : (long.MinValue / 16) + seed.ClubId, ct);
        }
        finally
        {
            gate.Release();
        }

        var result = await commit;
        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedRows.ShouldBe(2);
        result.Value.EnrolledPlayers.ShouldBe(close ? 2 : 0);
        result.Value.WaitingPlayers.ShouldBe(close ? 0 : 2);
        await change;
        await AssertCountsAsync(seed, 2, 2, 1, ct);
        var recovered = await service.CommitAsync(input, ct);
        recovered.IsSuccess.ShouldBeTrue();
        JsonSerializer.Serialize(recovered.Value).ShouldBe(JsonSerializer.Serialize(result.Value));
        await using var verify = fixture.CreateAdminContext();
        (await verify.Campaigns.Where(x => x.CampaignId == seed.CampaignId).Select(x => x.Status).SingleAsync(ct))
            .ShouldBe(CampaignStatus.Active);
    }

    /// <summary>Verifies the complete lock order before any campaign enrollment is persisted.</summary>
    [Fact]
    public async Task Commit_AcquiresMembershipSeasonRosterAndCampaignLocks_InGlobalOrder()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Active, ct);
        ActAs(seed);
        var recorder = new AdvisoryLockRecordingInterceptor();
        var service = CreateService(recorder);
        var input = await PreviewAsync(service, Upload("First"), ct);

        (await service.CommitAsync(input, ct)).IsSuccess.ShouldBeTrue();

        recorder.AcquiredKeys.ShouldBe(new long[]
        {
            (long.MinValue / 64) + seed.ActorUserId,
            (long.MinValue / 32) + seed.ClubId,
            (long.MinValue / 16) + seed.ClubId,
            (long.MinValue / 4) + seed.ClubId,
            long.MinValue + seed.CampaignId
        });
    }

    /// <summary>Verifies persisted role revocation defeats stale administrator claims after lock contention.</summary>
    [Fact]
    public async Task Commit_RejectsRevokedAdministrator_AfterMembershipLockWait()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Active, ct);
        ActAs(seed);
        var service = CreateService(new NoOpInterceptor());
        var input = await PreviewAsync(service, Upload("First"), ct);
        await using var revocation = fixture.CreateAdminContext();
        await using var transaction = await revocation.Database.BeginTransactionAsync(ct);
        await revocation.AcquireUserMembershipLockAsync(seed.ActorUserId, ct);
        await revocation.AcquireClubMembershipLockAsync(seed.ClubId, ct);
        var commit = service.CommitAsync(input, ct);
        await PostgresAdvisoryLockTestHelper.WaitForAdvisoryLockWaiterAsync(
            revocation, (long.MinValue / 64) + seed.ActorUserId, ct);
        var role = await revocation.UserRoles.SingleAsync(x => x.UserId == seed.ActorUserId, ct);
        revocation.UserRoles.Remove(role);
        await revocation.SaveChangesAsync(ct);
        await transaction.CommitAsync(ct);

        var result = await commit;
        result.IsSuccess.ShouldBeFalse();
        result.Problem.StatusCode.ShouldBe(403);
        await AssertCountsAsync(seed, 0, 0, 0, ct);
    }

    /// <summary>Verifies the database's final operation identity guard independently of the service.</summary>
    [Fact]
    public async Task Receipt_RejectsDuplicateOperation_WithinClub()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Draft, ct);
        ActAs(seed);
        var service = CreateService(new NoOpInterceptor());
        var input = await PreviewAsync(service, Upload("First"), ct);
        (await service.CommitAsync(input, ct)).IsSuccess.ShouldBeTrue();
        await using var db = fixture.CreateAdminContext();
        var receipt = await db.PlayerImportReceipts.AsNoTracking().SingleAsync(x => x.ClubId == seed.ClubId, ct);
        receipt.PlayerImportReceiptId = 0;
        db.PlayerImportReceipts.Add(receipt);
        var error = await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(ct));
        error.InnerException.ShouldBeOfType<Npgsql.PostgresException>().SqlState.ShouldBe("23505");
    }

    /// <summary>Verifies deleting mutable aggregates cannot delete commit proof or change recovered results.</summary>
    [Fact]
    public async Task Commit_RecoversOriginalSnapshot_AfterPlayersAndCampaignAreDeleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Active, ct);
        ActAs(seed);
        var service = CreateService(new NoOpInterceptor());
        var input = await PreviewAsync(service, Upload("First", "Second"), ct);
        var original = await service.CommitAsync(input, ct);
        original.IsSuccess.ShouldBeTrue();
        await using (var deletion = fixture.CreateAdminContext())
        {
            await deletion.Players.Where(x => x.ClubId == seed.ClubId).ExecuteDeleteAsync(ct);
            await deletion.Campaigns.Where(x => x.CampaignId == seed.CampaignId).ExecuteDeleteAsync(ct);
        }

        var recovered = await service.CommitAsync(input, ct);

        recovered.IsSuccess.ShouldBeTrue();
        JsonSerializer.Serialize(recovered.Value).ShouldBe(JsonSerializer.Serialize(original.Value));
        await AssertCountsAsync(seed, 0, 0, 1, ct);
    }

    /// <summary>Verifies provider reader work stays bounded for representative and maximum files.</summary>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(200, "ready")]
    [InlineData(1000, "ready")]
    [InlineData(200, "duplicates")]
    [InlineData(1000, "duplicates")]
    [InlineData(200, "invalid")]
    [InlineData(1000, "invalid")]
    public async Task Commit_UsesBoundedReaders_ForLargeFiles(int rowCount, string shape)
    {
        var ct = TestContext.Current.CancellationToken;
        var seed = await SeedAsync(CampaignStatus.Active, ct);
        ActAs(seed);
        var counter = new CountingCommandInterceptor();
        var service = CreateService(counter);
        var names = Enumerable.Range(0, rowCount).Select(index => shape switch
        {
            "duplicates" => $"Player{index / 2}",
            "invalid" when index % 2 == 1 => "",
            _ => $"Player{index}"
        }).ToArray();
        var input = await PreviewAsync(service, Upload(names), ct);

        var result = await service.CommitAsync(input, ct);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalRows.ShouldBe(rowCount);
        var expectedCreated = shape == "ready" ? rowCount : rowCount / 2;
        result.Value.CreatedRows.ShouldBe(expectedCreated);
        result.Value.EnrolledPlayers.ShouldBe(expectedCreated);
        result.Value.SkippedDuplicateRows.ShouldBe(shape == "duplicates" ? rowCount / 2 : 0);
        result.Value.SkippedInvalidRows.ShouldBe(shape == "invalid" ? rowCount / 2 : 0);
        // Includes batched INSERT ... RETURNING readers as well as SELECTs. A per-row read
        // adds hundreds of commands and cannot fit this ceiling at either accepted size.
        counter.ReaderExecutionCount.ShouldBeGreaterThan(0);
        counter.ReaderExecutionCount.ShouldBeLessThanOrEqualTo(70);
        // Authorization, receipt before/after roster lock, duplicate candidates, and the two
        // campaign snapshots are the six SELECT readers regardless of file size or row shape.
        counter.SelectReaderExecutionCount.ShouldBe(6);
        await AssertCountsAsync(seed, expectedCreated, expectedCreated, 1, ct);
    }

    /// <summary>Creates an import service with a provider failure or command-observation interceptor.</summary>
    private PlayerImportService CreateService(IInterceptor interceptor) => CreateService(
        new RetryingTenantDbContextFactory(fixture.ConnectionString, fixture.CurrentUser, interceptor));

    /// <summary>Creates an import service sharing this test's protection keys and actor.</summary>
    private PlayerImportService CreateService(RetryingTenantDbContextFactory factory) => new(
        new PostgresReadContextFactory(fixture), fixture.CurrentUser, new PlayerImportCsvParser(),
        new PlayerImportPreviewTokenProtector(_protection, TimeProvider.System), TimeProvider.System,
        NullLogger<PlayerImportService>.Instance, factory, new PostgresAdminContextFactory(fixture));

    /// <summary>Builds a valid authoritative CSV with the supplied first names.</summary>
    private static PlayerImportUploadInput Upload(params string[] names) => new()
    {
        FileName = "players.csv",
        ContentType = "text/csv",
        Content = Encoding.UTF8.GetBytes(string.Join(',', PlayerImportConstraints.Headers) + "\r\n"
            + string.Concat(names.Select(name => $"{name},Import,2012-01-01,,,2030\r\n")))
    };

    /// <summary>Obtains the real preview confirmation for a file with eligible rows.</summary>
    private static async Task<PlayerImportCommitInput> PreviewAsync(PlayerImportService service, PlayerImportUploadInput upload, CancellationToken ct)
    {
        var preview = await service.PreviewAsync(upload, ct);
        preview.IsSuccess.ShouldBeTrue();
        preview.Value.ReadyRows.ShouldBeGreaterThan(0);
        return new PlayerImportCommitInput
        {
            Upload = upload,
            OperationId = preview.Value.OperationId,
            ConfirmationToken = preview.Value.ConfirmationToken
        };
    }

    /// <summary>Verifies all three transaction aggregates within this test's isolated club.</summary>
    private async Task AssertCountsAsync(Seed seed, int players, int enrollments, int receipts, CancellationToken ct)
    {
        await using var db = fixture.CreateAdminContext();
        (await db.Players.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(players);
        (await db.PlayerCampaignAssignments.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(enrollments);
        (await db.PlayerImportReceipts.CountAsync(x => x.ClubId == seed.ClubId, ct)).ShouldBe(receipts);
    }

    /// <summary>Creates the identity that a racing import must discover as a new duplicate.</summary>
    private static async Task CompleteManualCreateAsync(PlayerManagementService service, CancellationToken ct)
    {
        (await service.CreateAsync(new CreatePlayerInput
        {
            FirstName = "Shared",
            LastName = "Import",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030
        }, ct)).IsSuccess.ShouldBeTrue();
    }

    /// <summary>Changes an existing identity into the one held by a racing import.</summary>
    private static async Task CompleteManualUpdateAsync(PlayerManagementService service, long playerId, CancellationToken ct)
    {
        (await service.UpdateAsync(new UpdatePlayerInput
        {
            PlayerId = playerId,
            FirstName = "Shared",
            LastName = "Import",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030
        }, ct)).IsSuccess.ShouldBeTrue();
    }

    /// <summary>Completes the selected lifecycle mutation through its production service.</summary>
    private static async Task ChangeCampaignAsync(ICampaignLifecycleService service, long campaignId, string transition, CancellationToken ct)
    {
        if (transition == "open")
        {
            (await service.OpenAsync(campaignId, new OpenCampaignInput { OperationId = Guid.CreateVersion7() }, ct)).IsSuccess.ShouldBeTrue();
        }
        else if (transition == "close")
        {
            (await service.CloseAsync(campaignId, ct)).IsSuccess.ShouldBeTrue();
        }
        else
        {
            (await service.ReopenAsync(campaignId, ct)).IsSuccess.ShouldBeTrue();
        }
    }

    /// <summary>Checks the campaign transition against players committed by the winning import.</summary>
    private static async Task AssertCampaignAfterImportAsync(ICampaignLifecycleService service, long campaignId, bool close, CancellationToken ct)
    {
        if (close)
        {
            var result = await service.CloseAsync(campaignId, ct);
            result.IsSuccess.ShouldBeFalse();
            result.Problem.StatusCode.ShouldBe(409);
        }
        else
        {
            var result = await service.OpenAsync(campaignId, new OpenCampaignInput { OperationId = Guid.CreateVersion7() }, ct);
            result.IsSuccess.ShouldBeTrue();
            result.Value.EnrolledPlayerCount.ShouldBe(2);
        }
    }

    /// <summary>Sets this asynchronous flow's current administrator.</summary>
    private void ActAs(Seed seed)
    {
        fixture.CurrentUser.UserId = seed.ActorUserId;
        fixture.CurrentUser.ClubId = seed.ClubId;
        fixture.CurrentUser.IsClubAdmin = true;
    }

    /// <summary>Seeds persisted administrator membership and a current-season campaign.</summary>
    private async Task<Seed> SeedAsync(CampaignStatus status, CancellationToken ct)
    {
        fixture.CurrentUser.UserId = null;
        fixture.CurrentUser.ClubId = null;
        fixture.CurrentUser.IsClubAdmin = false;
        await using var db = fixture.CreateAdminContext();
        var user = new NovaUserEntity { FirstName = "Import", LastName = "Administrator" };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var club = new ClubEntity
        {
            CreationOperationId = Guid.CreateVersion7(),
            Name = $"Import {Guid.NewGuid():N}",
            City = "Austin",
            State = "TX",
            CreatedById = user.Id
        };
        db.Clubs.Add(club);
        await db.SaveChangesAsync(ct);
        user.ClubId = club.ClubId;
        var roleId = await db.Roles.Where(x => x.NormalizedName == Roles.ClubAdmin.ToUpperInvariant()).Select(x => x.Id).SingleAsync(ct);
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = user.Id, RoleId = roleId });
        var season = new SeasonEntity
        {
            CreationOperationId = Guid.CreateVersion7(),
            ClubId = club.ClubId,
            CreatedById = user.Id,
            Name = "Import season",
            StartDate = new DateOnly(2026, 1, 1)
        };
        db.Seasons.Add(season);
        await db.SaveChangesAsync(ct);
        club.CurrentSeasonId = season.SeasonId;
        var campaign = new CampaignEntity
        {
            CreationOperationId = Guid.CreateVersion7(),
            ClubId = club.ClubId,
            CreatedById = user.Id,
            SeasonId = season.SeasonId,
            Name = "Import campaign",
            StartDate = new DateOnly(2026, 6, 1),
            Status = status,
            ClosedAt = status == CampaignStatus.Closed ? DateTimeOffset.UtcNow : null,
            ClosedById = status == CampaignStatus.Closed ? user.Id : null
        };
        db.Campaigns.Add(campaign);
        await db.SaveChangesAsync(ct);
        return new Seed(club.ClubId, user.Id, campaign.CampaignId);
    }

    /// <summary>Seeds a second real administrator to exercise club-wide contention across actors.</summary>
    private async Task<long> SeedAdditionalAdministratorAsync(long clubId, CancellationToken ct)
    {
        await using var db = fixture.CreateAdminContext();
        var user = new NovaUserEntity { FirstName = "Second", LastName = "Administrator", ClubId = clubId };
        db.Users.Add(user);
        await db.SaveChangesAsync(ct);
        var roleId = await db.Roles.Where(x => x.NormalizedName == Roles.ClubAdmin.ToUpperInvariant()).Select(x => x.Id).SingleAsync(ct);
        db.UserRoles.Add(new IdentityUserRole<long> { UserId = user.Id, RoleId = roleId });
        await db.SaveChangesAsync(ct);
        return user.Id;
    }

    /// <summary>Identifies the isolated club, real actor, and campaign used by one scenario.</summary>
    private sealed record Seed(long ClubId, long ActorUserId, long CampaignId);

    /// <summary>Counts actual asynchronous provider readers, including batched write readers.</summary>
    private sealed class CountingCommandInterceptor : DbCommandInterceptor
    {
        /// <summary>Gets the number of asynchronous relational readers executed.</summary>
        public int ReaderExecutionCount { get; private set; }

        /// <summary>Gets query readers independently of provider INSERT RETURNING batching.</summary>
        public int SelectReaderExecutionCount { get; private set; }

        /// <inheritdoc />
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            ReaderExecutionCount++;
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.Ordinal))
            {
                SelectReaderExecutionCount++;
            }
            return ValueTask.FromResult(result);
        }
    }
}
