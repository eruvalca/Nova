using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Features.Players;

/// <summary>Creates independent write contexts over the test database.</summary>
/// <param name="harness">The shared test database.</param>
internal sealed class PlayerImportWriteContextFactory(TenancyTestHarness harness) : IDbContextFactory<NovaDbContext>
{
    /// <summary>Creates a tenant-filtered write context.</summary>
    /// <returns>A new context.</returns>
    public NovaDbContext CreateDbContext() => harness.CreateTenantContext();
}

/// <summary>Creates infrastructure contexts over the test database.</summary>
/// <param name="harness">The shared test database.</param>
internal sealed class PlayerImportAdminContextFactory(TenancyTestHarness harness) : IDbContextFactory<NovaAdminDbContext>
{
    /// <summary>Creates an unfiltered infrastructure context.</summary>
    /// <returns>A new context.</returns>
    public NovaAdminDbContext CreateDbContext() => harness.CreateAdminContext();
}

/// <summary>Controls application time without sleeping.</summary>
internal sealed class PlayerImportTestClock : TimeProvider
{
    /// <summary>Gets or sets the current UTC instant.</summary>
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    /// <summary>Returns the configured instant.</summary>
    /// <returns>The current test time.</returns>
    public override DateTimeOffset GetUtcNow() => Now;
}

/// <summary>Verifies commit reconciliation, current authority, and durable exact-request recovery.</summary>
public sealed class PlayerImportCommitServiceTests : IDisposable
{
    /// <summary>The owning club.</summary>
    private const long ClubId = 9101;
    /// <summary>The other tenant.</summary>
    private const long OtherClubId = 9102;
    /// <summary>The confirmed actor.</summary>
    private const long ActorId = 9103;
    /// <summary>The persisted administrator role.</summary>
    private const long RoleId = 9104;
    /// <summary>The isolated database.</summary>
    private readonly TenancyTestHarness _harness = new();
    /// <summary>The controllable authorization and recovery clock.</summary>
    private readonly PlayerImportTestClock _clock = new();
    /// <summary>The token protector shared between preview and commit.</summary>
    private readonly PlayerImportPreviewTokenProtector _protector;

    /// <summary>Seeds current persisted club membership and administrator authority.</summary>
    public PlayerImportCommitServiceTests()
    {
        _protector = new(new EphemeralDataProtectionProvider(), _clock);
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            new ClubEntity { CreatedById = ActorId, ClubId = ClubId, Name = "Import club", City = "Austin", State = "TX", CreationOperationId = Guid.CreateVersion7() },
            new ClubEntity { CreatedById = ActorId, ClubId = OtherClubId, Name = "Other club", City = "Austin", State = "TX", CreationOperationId = Guid.CreateVersion7() });
        db.Users.Add(new NovaUserEntity { Id = ActorId, FirstName = "Import", LastName = "Admin", ClubId = ClubId });
        db.Roles.Add(new IdentityRole<long> { Id = RoleId, Name = Roles.ClubAdmin, NormalizedName = Roles.ClubAdmin.ToUpperInvariant() });
        db.UserRoles.Add(new IdentityUserRole<long> { RoleId = RoleId, UserId = ActorId });
        db.SaveChanges();
        _harness.CurrentUser.UserId = ActorId;
        _harness.CurrentUser.ClubId = ClubId;
        _harness.CurrentUser.IsClubAdmin = true;
    }

    /// <summary>Releases the isolated SQLite connection.</summary>
    public void Dispose() => _harness.Dispose();

    /// <summary>Mixed previews create only reviewed eligible rows and reconcile every source row.</summary>
    [Fact]
    public async Task CommitAsync_ReconcilesMixedPreview_AndPersistsOneReceipt()
    {
        var input = await Preview("Taylor,Stone,2013-02-03,,,2031\r\nTaylor,Stone,2013-02-03,,,2031\r\n,Invalid,not-a-date,,,1999\r\n");
        var result = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedRows.ShouldBe(1);
        result.Value.SkippedDuplicateRows.ShouldBe(1);
        result.Value.SkippedInvalidRows.ShouldBe(1);
        result.Value.BlockedRows.ShouldBe(0);
        result.Value.WaitingPlayers.ShouldBe(1);
        result.Value.EnrolledPlayers.ShouldBe(0);
        result.Value.CampaignId.ShouldBeNull();
        result.Value.Rows.Select(row => row.SourceRowNumber).ShouldBe([2, 3, 4]);
        result.Value.Rows.Select(row => row.Status).ShouldBe([
            PlayerImportCommitRowStatus.Created,
            PlayerImportCommitRowStatus.SkippedDuplicateAtPreview,
            PlayerImportCommitRowStatus.SkippedInvalidAtPreview]);
        using var db = _harness.CreateAdminContext();
        db.Players.Single().PlayerId.ShouldBe(result.Value.Rows[0].PlayerId!.Value);
        db.PlayerImportReceipts.Count().ShouldBe(1);
        db.PlayerCampaignAssignments.Count().ShouldBe(0);
        db.ActivityEvents.Count().ShouldBe(0);
    }

    /// <summary>All-invalid and all-duplicate previews cannot create completion receipts.</summary>
    /// <param name="duplicate">Whether the preview excludes an existing player.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitAsync_RejectsPreviewWithoutEligibleRows(bool duplicate)
    {
        if (duplicate)
        {
            AddPlayer("Taylor", "Stone");
        }

        var input = await Preview(duplicate ? "Taylor,Stone,2013-02-03,,,2031\r\n" : ",Invalid,not-a-date,,,1999\r\n");
        var result = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        using var db = _harness.CreateAdminContext();
        db.PlayerImportReceipts.Count().ShouldBe(0);
    }

    /// <summary>A concurrent roster addition blocks the reviewed row and permanently completes zero creations.</summary>
    [Fact]
    public async Task CommitAsync_CompletesZeroCreations_WhenEveryReadyRowBecomesDuplicate()
    {
        var input = await Preview("Taylor,Stone,2013-02-03,,,2031\r\n");
        var existingId = AddPlayer(" TAYLOR ", "stone");
        var result = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedRows.ShouldBe(0);
        result.Value.BlockedRows.ShouldBe(1);
        var row = result.Value.Rows.ShouldHaveSingleItem();
        row.Status.ShouldBe(PlayerImportCommitRowStatus.BlockedAtCommit);
        row.Duplicate!.ExistingPlayerId.ShouldBe(existingId);
        using (var db = _harness.CreateAdminContext())
        {
            db.Players.Remove(db.Players.Single());
            db.SaveChanges();
        }
        var recovered = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        recovered.IsSuccess.ShouldBeTrue();
        JsonSerializer.Serialize(recovered.Value).ShouldBe(JsonSerializer.Serialize(result.Value));
        using var after = _harness.CreateAdminContext();
        after.Players.Count().ShouldBe(0);
        after.PlayerImportReceipts.Count().ShouldBe(1);
    }

    /// <summary>Removing a duplicate after preview does not authorize its originally excluded row.</summary>
    [Fact]
    public async Task CommitAsync_KeepsOriginallyDuplicateRowExcluded_AfterMatchingPlayerIsDeleted()
    {
        AddPlayer("Taylor", "Stone");
        var input = await Preview("Taylor,Stone,2013-02-03,,,2031\r\nJordan,New,2013-02-03,,,2031\r\n");
        using (var db = _harness.CreateAdminContext())
        {
            db.Players.Remove(db.Players.Single());
            db.SaveChanges();
        }
        var result = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedRows.ShouldBe(1);
        result.Value.Rows[0].Status.ShouldBe(PlayerImportCommitRowStatus.SkippedDuplicateAtPreview);
        using var after = _harness.CreateAdminContext();
        after.Players.Single().FirstName.ShouldBe("Jordan");
    }

    /// <summary>Recovery returns the original snapshot after token expiration and later player edits.</summary>
    [Fact]
    public async Task CommitAsync_RecoversOriginalResult_AfterTokenExpiryAndPlayerChanges()
    {
        var input = await Preview("Taylor,Stone,2013-02-03,,,2031\r\n");
        var first = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        first.IsSuccess.ShouldBeTrue();
        _clock.Now = _clock.Now.AddHours(2);
        using (var db = _harness.CreateAdminContext())
        {
            db.Players.Single().FirstName = "Changed";
            db.SaveChanges();
        }
        var retry = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        retry.IsSuccess.ShouldBeTrue();
        JsonSerializer.Serialize(retry.Value).ShouldBe(JsonSerializer.Serialize(first.Value));
        (retry.Value.RecoveryExpiresAt - retry.Value.CompletedAt).ShouldBe(TimeSpan.FromHours(24));
        using var after = _harness.CreateAdminContext();
        after.Players.Count().ShouldBe(1);
        after.PlayerImportReceipts.Count().ShouldBe(1);
    }

    /// <summary>Neither a new expired preview nor an expired recovery can execute players.</summary>
    /// <param name="completed">Whether a receipt already exists.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitAsync_RejectsExpiredAuthorizationOrRecovery(bool completed)
    {
        var input = await Preview("Taylor,Stone,2013-02-03,,,2031\r\n");
        if (completed)
        {
            (await Service().CommitAsync(input, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        }

        _clock.Now = _clock.Now.AddHours(completed ? 24 : 1);
        var result = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        using var db = _harness.CreateAdminContext();
        db.Players.Count().ShouldBe(completed ? 1 : 0);
    }

    /// <summary>Persisted role revocation overrides stale administrator claims for commits and recovery.</summary>
    /// <param name="completed">Whether this is an exact-request recovery.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitAsync_RejectsRevokedPersistedAdministratorRole(bool completed)
    {
        var input = await Preview("Taylor,Stone,2013-02-03,,,2031\r\n");
        if (completed)
        {
            (await Service().CommitAsync(input, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        }

        using (var db = _harness.CreateAdminContext())
        {
            db.UserRoles.Remove(db.UserRoles.Single());
            db.SaveChanges();
        }
        var result = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>A receipt does not authorize a changed file, operation, actor, or tenant.</summary>
    /// <param name="changed">The identity component to alter.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("bytes")]
    [InlineData("operation")]
    [InlineData("token")]
    [InlineData("actor")]
    [InlineData("club")]
    public async Task CommitAsync_RejectsMismatchedRecoveryIdentity(string changed)
    {
        var input = await Preview("Taylor,Stone,2013-02-03,,,2031\r\n");
        (await Service().CommitAsync(input, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        switch (changed)
        {
            case "bytes": input = input with { Upload = input.Upload with { Content = [.. input.Upload.Content, (byte)'\n'] } }; break;
            case "operation": input = input with { OperationId = Guid.CreateVersion7() }; break;
            case "token": input = input with { ConfirmationToken = "tampered" }; break;
            case "actor": _harness.CurrentUser.UserId = ActorId + 10; break;
            case "club": _harness.CurrentUser.ClubId = OtherClubId; break;
        }
        var result = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        using var db = _harness.CreateAdminContext();
        db.Players.Count().ShouldBe(1);
    }

    /// <summary>Receipts are visible only in their owning tenant while infrastructure retains access.</summary>
    [Fact]
    public async Task ReceiptQueries_FilterByCurrentTenant()
    {
        var input = await Preview("Taylor,Stone,2013-02-03,,,2031\r\n");
        (await Service().CommitAsync(input, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        using (var own = _harness.CreateReadContext())
        {
            own.PlayerImportReceipts.Count().ShouldBe(1);
        }

        _harness.CurrentUser.ClubId = OtherClubId;
        using (var other = _harness.CreateReadContext())
        {
            other.PlayerImportReceipts.Count().ShouldBe(0);
        }

        using var admin = _harness.CreateAdminContext();
        admin.PlayerImportReceipts.Count().ShouldBe(1);
    }

    /// <summary>Final campaign state controls technical enrollment without placement activity.</summary>
    [Fact]
    public async Task CommitAsync_EnrollsIntoCampaignOpenedAfterPreview_WithUndecidedPlacement()
    {
        var input = await Preview("Taylor,Stone,2013-02-03,,,2031\r\nJordan,New,2013-02-03,,,2031\r\n");
        long campaignId;
        using (var db = _harness.CreateAdminContext())
        {
            var season = new SeasonEntity
            {
                CreatedById = ActorId,
                ClubId = ClubId,
                Name = "Season",
                StartDate = new DateOnly(2026, 1, 1),
                CreationOperationId = Guid.CreateVersion7()
            };
            db.Seasons.Add(season);
            db.SaveChanges();
            var campaign = new CampaignEntity
            {
                CreatedById = ActorId,
                ClubId = ClubId,
                SeasonId = season.SeasonId,
                Name = "Opened after preview",
                Status = CampaignStatus.Active,
                CreationOperationId = Guid.CreateVersion7()
            };
            db.Campaigns.Add(campaign);
            db.SaveChanges();
            campaignId = campaign.CampaignId;
        }
        var result = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.CreatedRows.ShouldBe(2);
        result.Value.EnrolledPlayers.ShouldBe(2);
        result.Value.WaitingPlayers.ShouldBe(0);
        result.Value.CampaignId.ShouldBe(campaignId);
        result.Value.CampaignName.ShouldBe("Opened after preview");
        using var after = _harness.CreateAdminContext();
        var assignments = after.PlayerCampaignAssignments.ToArray();
        assignments.Length.ShouldBe(2);
        assignments.ShouldAllBe(assignment => assignment.PlacementOutcome == PlacementOutcome.Undecided
            && assignment.TeamId == null && assignment.CampaignId == campaignId);
        assignments.Select(assignment => assignment.PlayerId).Order().ShouldBe(result.Value.Rows.Select(row => row.PlayerId!.Value).Order());
        after.ActivityEvents.Count().ShouldBe(0);
    }

    /// <summary>Cross-tenant writes cannot introduce a forged receipt into another club.</summary>
    [Fact]
    public async Task ReceiptWrites_RejectCrossTenantInsert()
    {
        using var db = _harness.CreateTenantContext();
        db.PlayerImportReceipts.Add(new PlayerImportReceiptEntity
        {
            CreatedById = ActorId,
            ClubId = OtherClubId,
            ActorUserId = ActorId,
            OperationId = Guid.CreateVersion7(),
            FileLength = 1,
            FileSha256 = new string('A', 64),
            ConfirmationTokenSha256 = new string('B', 64),
            ResultJson = "{}",
            CompletedAt = _clock.Now,
            RecoveryExpiresAt = _clock.Now.AddHours(24)
        });
        await Should.ThrowAsync<InvalidOperationException>(() => db.SaveChangesAsync(TestContext.Current.CancellationToken));
        using var after = _harness.CreateAdminContext();
        after.PlayerImportReceipts.Count().ShouldBe(0);
    }

    /// <summary>Persisted membership overrides a stale club claim.</summary>
    [Fact]
    public async Task CommitAsync_RejectsActorWhoLeftClub_AfterPreview()
    {
        var input = await Preview("Taylor,Stone,2013-02-03,,,2031\r\n");
        using (var db = _harness.CreateAdminContext())
        {
            db.Users.Single().ClubId = null;
            db.SaveChanges();
        }
        var result = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        using var after = _harness.CreateAdminContext();
        after.Players.Count().ShouldBe(0);
        after.PlayerImportReceipts.Count().ShouldBe(0);
    }

    /// <summary>Subsequent imports remove expired receipts globally without deleting live domain records.</summary>
    [Fact]
    public async Task CommitAsync_PrunesExpiredReceiptsAcrossTenants_AndPreservesLiveReceiptsAndPlayers()
    {
        var first = await Preview("Taylor,Stone,2013-02-03,,,2031\r\n");
        (await Service().CommitAsync(first, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        using (var db = _harness.CreateAdminContext())
        {
            db.PlayerImportReceipts.Add(new PlayerImportReceiptEntity
            {
                CreatedById = ActorId,
                ClubId = OtherClubId,
                ActorUserId = ActorId,
                OperationId = Guid.CreateVersion7(),
                FileLength = 1,
                FileSha256 = new string('A', 64),
                ConfirmationTokenSha256 = new string('B', 64),
                ResultJson = "{}",
                CompletedAt = _clock.Now.AddHours(-25),
                RecoveryExpiresAt = _clock.Now.AddHours(-1)
            });
            db.SaveChanges();
        }
        var second = await Preview("Jordan,New,2013-02-03,,,2031\r\n");
        (await Service().CommitAsync(second, TestContext.Current.CancellationToken)).IsSuccess.ShouldBeTrue();
        using var after = _harness.CreateAdminContext();
        after.PlayerImportReceipts.Count().ShouldBe(2);
        after.PlayerImportReceipts.ShouldAllBe(receipt => receipt.ClubId == ClubId);
        after.PlayerImportReceipts.Any(receipt => receipt.OperationId == first.OperationId).ShouldBeTrue();
        after.Players.Count().ShouldBe(2);
    }

    /// <summary>Representative and maximum uploads reconcile every row and retain distinct creation identities.</summary>
    /// <param name="count">The number of data rows.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(200)]
    [InlineData(1000)]
    public async Task CommitAsync_PersistsEveryEligibleRow_ForBoundedLargeUpload(int count)
    {
        var rows = string.Concat(Enumerable.Range(0, count).Select(index => $"Player{index},Import,2013-02-03,,,2031\r\n"));
        var input = await Preview(rows);
        var result = await Service().CommitAsync(input, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalRows.ShouldBe(count);
        result.Value.CreatedRows.ShouldBe(count);
        result.Value.WaitingPlayers.ShouldBe(count);
        result.Value.Rows.Select(row => row.SourceRowNumber).ShouldBe(Enumerable.Range(2, count));
        result.Value.Rows.Select(row => row.PlayerId).Distinct().Count().ShouldBe(count);
        using var after = _harness.CreateAdminContext();
        after.Players.Count().ShouldBe(count);
        after.Players.Select(player => player.CreationOperationId).Distinct().Count().ShouldBe(count);
        after.Players.Any(player => player.CreationOperationId == input.OperationId).ShouldBeFalse();
        after.PlayerImportReceipts.Count().ShouldBe(1);
    }

    /// <summary>Builds the service with independent factories and the same clock and protection keys.</summary>
    /// <returns>The service under test.</returns>
    private PlayerImportService Service() => new(
        new PlayerImportReadContextFactory(_harness), _harness.CurrentUser, new PlayerImportCsvParser(),
        _protector, _clock, NullLogger<PlayerImportService>.Instance,
        new PlayerImportWriteContextFactory(_harness), new PlayerImportAdminContextFactory(_harness));

    /// <summary>Creates a genuine server-authorized confirmation for the supplied source rows.</summary>
    /// <param name="rows">CSV data rows.</param>
    /// <returns>The exact replayable commit request.</returns>
    private async Task<PlayerImportCommitInput> Preview(string rows)
    {
        var upload = new PlayerImportUploadInput
        {
            FileName = "players.csv",
            ContentType = "text/csv",
            Content = Encoding.UTF8.GetBytes("First name,Last name,Date of birth,Gender,Jersey number,Graduation year\r\n" + rows)
        };
        var preview = await Service().PreviewAsync(upload, TestContext.Current.CancellationToken);
        preview.IsSuccess.ShouldBeTrue();
        return new() { Upload = upload, OperationId = preview.Value.OperationId, ConfirmationToken = preview.Value.ConfirmationToken };
    }

    /// <summary>Adds an existing roster identity independently of import.</summary>
    /// <param name="firstName">The stored first name.</param>
    /// <param name="lastName">The stored last name.</param>
    /// <returns>The new player identifier.</returns>
    private long AddPlayer(string firstName, string lastName)
    {
        using var db = _harness.CreateAdminContext();
        var player = new PlayerEntity
        {
            CreatedById = ActorId,
            ClubId = ClubId,
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = new DateOnly(2013, 2, 3),
            GraduationYear = 2031,
            CreationOperationId = Guid.CreateVersion7()
        };
        db.Players.Add(player);
        db.SaveChanges();
        return player.PlayerId;
    }
}
