using System.Text;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Players;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Unit.Tests.Data;
using Shouldly;

namespace Nova.Unit.Tests.Features.Players;

internal sealed class PlayerImportReadContextFactory(TenancyTestHarness harness) : IDbContextFactory<NovaReadDbContext>
{
    public int CreatedContexts { get; private set; }

    public NovaReadDbContext CreateDbContext()
    {
        CreatedContexts++;
        return harness.CreateReadContext();
    }

    public Task<NovaReadDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(CreateDbContext());
    }
}

/// <summary>Tests authorization, tenancy, duplicate classification, and non-persistence for import previews.</summary>
public sealed class PlayerImportServiceTests : IDisposable
{
    private const long ClubAId = 901;
    private const long ClubBId = 902;
    private const long AdminId = 903;
    private readonly TenancyTestHarness _harness = new();
    private readonly PlayerImportPreviewTokenProtector _tokenProtector;

    public PlayerImportServiceTests()
    {
        _tokenProtector = new(new EphemeralDataProtectionProvider());
        Seed();
    }

    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task GetTemplateAsync_ReturnsExactBomPrefixedTemplate_ForClubAdministrator()
    {
        ActAs(AdminId, ClubAId, isAdmin: true);

        var result = await CreateService().GetTemplateAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Content.Take(3).ShouldBe([0xEF, 0xBB, 0xBF]);
        Encoding.UTF8.GetString(result.Value.Content[3..]).ShouldBe(
            "First name,Last name,Date of birth,Gender,Jersey number,Graduation year\r\n");
        result.Value.ContentType.ShouldBe(PlayerImportConstraints.CsvContentType);
        result.Value.DownloadFileName.ShouldBe(PlayerImportConstraints.TemplateFileName);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsForbidden_ForOrdinaryClubMember()
    {
        ActAs(AdminId, ClubAId, isAdmin: false);

        var result = await CreateService().PreviewAsync(Upload(ValidRows()), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, null)]
    [InlineData(AdminId, null)]
    public async Task PreviewAsync_ReturnsForbidden_WithoutSignedInClubAdministrator(long? userId, long? clubId)
    {
        ActAs(userId, clubId, isAdmin: true);

        var result = await CreateService().PreviewAsync(Upload(ValidRows()), TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null, "text/csv")]
    [InlineData("", "text/csv")]
    [InlineData("players.txt", "text/csv")]
    [InlineData("players\r\n.csv", "text/csv")]
    [InlineData("players.csv", "image/png")]
    public async Task PreviewAsync_RejectsUnsupportedUploadMetadata(string? fileName, string contentType)
    {
        ActAs(AdminId, ClubAId, isAdmin: true);
        var valid = Upload(ValidRows());

        var result = await CreateService().PreviewAsync(
            valid with { FileName = fileName!, ContentType = contentType },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    [Fact]
    public async Task PreviewAsync_ClassifiesArchivedExistingPlayer()
    {
        ActAs(AdminId, ClubAId, isAdmin: true);

        var result = await CreateService().PreviewAsync(
            Upload("Archived,Player,2011-01-01,,,2030\r\n"),
            TestContext.Current.CancellationToken);

        result.Value.Rows.ShouldHaveSingleItem().Duplicate!.Kind
            .ShouldBe(PlayerImportDuplicateKind.ExistingArchivedPlayer);
    }

    [Fact]
    public async Task PreviewAsync_PrefersActiveExistingMatch_ThenLowestPlayerId()
    {
        ActAs(AdminId, ClubAId, isAdmin: true);
        var archived = Player(
            ClubAId,
            "Priority",
            "Match",
            new DateOnly(2010, 5, 6),
            LifecycleStatus.Archived);
        var firstActive = Player(
            ClubAId,
            " priority ",
            "MATCH",
            new DateOnly(2010, 5, 6),
            LifecycleStatus.Active);
        var secondActive = Player(
            ClubAId,
            "PRIORITY",
            "match",
            new DateOnly(2010, 5, 6),
            LifecycleStatus.Active);
        using (var db = _harness.CreateAdminContext())
        {
            db.Players.AddRange(archived, firstActive, secondActive);
            db.SaveChanges();
        }

        var result = await CreateService().PreviewAsync(
            Upload("Priority,Match,2010-05-06,,,2030\r\n"),
            TestContext.Current.CancellationToken);

        var duplicate = result.Value.Rows.ShouldHaveSingleItem().Duplicate;
        duplicate.ShouldNotBeNull();
        duplicate.Kind.ShouldBe(PlayerImportDuplicateKind.ExistingActivePlayer);
        duplicate.ExistingPlayerId.ShouldBe(firstActive.PlayerId);
    }

    [Fact]
    public async Task PreviewAsync_ReconcilesInvalidExistingAndUploadDuplicates_WithoutPersisting()
    {
        ActAs(AdminId, ClubAId, isAdmin: true);
        var factory = new PlayerImportReadContextFactory(_harness);
        var sut = CreateService(factory);
        int playerCountBefore;
        int assignmentCountBefore;
        using (var before = _harness.CreateAdminContext())
        {
            playerCountBefore = before.Players.Count();
            assignmentCountBefore = before.PlayerCampaignAssignments.Count();
        }

        var rows = "  Alex,ARCHER,2012-01-01,,,2030\r\n"
            + "Taylor,Stone,2013-02-03,,,2031\r\n"
            + " taylor ,stone,2013-02-03,,,2031\r\n"
            + " ,Invalid,not-a-date,,,1999\r\n";
        var result = await sut.PreviewAsync(Upload(rows), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalRows.ShouldBe(4);
        result.Value.ReadyRows.ShouldBe(1);
        result.Value.InvalidRows.ShouldBe(1);
        result.Value.DuplicateRows.ShouldBe(2);
        factory.CreatedContexts.ShouldBe(1, "all existing-player duplicates should use one tenant read context");
        result.Value.Rows[0].Duplicate!.Kind.ShouldBe(PlayerImportDuplicateKind.ExistingActivePlayer);
        result.Value.Rows[2].Duplicate.ShouldBe(new PlayerImportDuplicate(
            PlayerImportDuplicateKind.EarlierUploadRow,
            null,
            3));

        using var after = _harness.CreateAdminContext();
        after.Players.Count().ShouldBe(playerCountBefore);
        after.PlayerCampaignAssignments.Count().ShouldBe(assignmentCountBefore);
    }

    [Fact]
    public async Task PreviewAsync_DoesNotLetInvalidRowReserveDuplicateIdentity()
    {
        ActAs(AdminId, ClubAId, isAdmin: true);
        var rows = "Taylor,Stone,2013-02-03,,10000,2031\r\n"
            + "Taylor,Stone,2013-02-03,,,2031\r\n";

        var result = await CreateService().PreviewAsync(
            Upload(rows),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ReadyRows.ShouldBe(1);
        result.Value.InvalidRows.ShouldBe(1);
        result.Value.DuplicateRows.ShouldBe(0);
        result.Value.Rows.Select(row => row.Status).ShouldBe([
            PlayerImportRowStatus.Invalid,
            PlayerImportRowStatus.Ready
        ]);
    }

    [Fact]
    public async Task PreviewAsync_DoesNotLeakOtherClubDuplicate()
    {
        ActAs(AdminId, ClubAId, isAdmin: true);

        var result = await CreateService().PreviewAsync(
            Upload("Only,OtherClub,2014-04-05,,,2032\r\n"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ReadyRows.ShouldBe(1);
        result.Value.DuplicateRows.ShouldBe(0);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsFreshSignedIdentity_BoundToActorClubAndFile()
    {
        ActAs(AdminId, ClubAId, isAdmin: true);
        var upload = Upload(ValidRows());
        var sut = CreateService();

        var first = await sut.PreviewAsync(upload, TestContext.Current.CancellationToken);
        var second = await sut.PreviewAsync(upload, TestContext.Current.CancellationToken);

        first.Value.OperationId.ShouldNotBe(second.Value.OperationId);
        first.Value.OperationId.Version.ShouldBe(7);
        _tokenProtector.TryUnprotect(first.Value.ConfirmationToken, out var payload).ShouldBeTrue();
        payload.ShouldNotBeNull();
        payload.OperationId.ShouldBe(first.Value.OperationId);
        payload.ActorUserId.ShouldBe(AdminId);
        payload.ClubId.ShouldBe(ClubAId);
        payload.FileLength.ShouldBe(upload.Content.Length);
        payload.FileSha256.ShouldNotBeNullOrWhiteSpace();
        payload.ExpiresAt.ShouldBe(first.Value.ExpiresAt);
        (payload.ExpiresAt - payload.IssuedAt).ShouldBe(TimeSpan.FromMinutes(60));
    }

    [Fact]
    public void TokenProtector_RejectsTamperedToken()
    {
        var token = _tokenProtector.Protect(TokenPayload(), TimeSpan.FromHours(1));
        var tampered = token[..^1] + (token[^1] == 'A' ? 'B' : 'A');

        _tokenProtector.TryUnprotect(tampered, out var payload).ShouldBeFalse();
        payload.ShouldBeNull();
    }

    [Fact]
    public void TokenProtector_RejectsExpiredToken()
    {
        var token = _tokenProtector.Protect(TokenPayload(), TimeSpan.FromMilliseconds(1));
        Thread.Sleep(25);

        _tokenProtector.TryUnprotect(token, out var payload).ShouldBeFalse();
        payload.ShouldBeNull();
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    public void TokenProtector_ReturnsSafeOut_ForMissingToken(string? token)
    {
        _tokenProtector.TryUnprotect(token!, out var payload).ShouldBeFalse();
        payload.ShouldBeNull();
    }

    [Fact]
    public void TokenProtector_ValidatesExactActorClubOperationAndBytes()
    {
        var content = Encoding.UTF8.GetBytes("exact csv bytes");
        var operationId = Guid.CreateVersion7();
        var now = DateTimeOffset.UtcNow;
        var payload = new PlayerImportPreviewTokenPayload(
            1,
            operationId,
            ClubAId,
            AdminId,
            Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(content)),
            content.Length,
            now,
            now.AddHours(1));
        var token = _tokenProtector.Protect(payload, TimeSpan.FromHours(1));

        _tokenProtector.TryValidate(token, operationId, ClubAId, AdminId, content, out var valid).ShouldBeTrue();
        valid.ShouldNotBeNull();

        _tokenProtector.TryValidate(token, Guid.CreateVersion7(), ClubAId, AdminId, content, out var wrongOperation)
            .ShouldBeFalse();
        wrongOperation.ShouldBeNull();
        _tokenProtector.TryValidate(token, operationId, ClubBId, AdminId, content, out var wrongClub)
            .ShouldBeFalse();
        wrongClub.ShouldBeNull();
        _tokenProtector.TryValidate(token, operationId, ClubAId, AdminId + 1, content, out var wrongActor)
            .ShouldBeFalse();
        wrongActor.ShouldBeNull();
        _tokenProtector.TryValidate(
                token,
                operationId,
                ClubAId,
                AdminId,
                [.. content, (byte)'!'],
                out var wrongContent)
            .ShouldBeFalse();
        wrongContent.ShouldBeNull();
    }

    [Fact]
    public void TokenProtector_ReturnsSafeOut_ForUnsupportedVersionAndMalformedHash()
    {
        var unsupported = _tokenProtector.Protect(TokenPayload() with { Version = 2 }, TimeSpan.FromHours(1));

        _tokenProtector.TryUnprotect(unsupported, out var unsupportedPayload).ShouldBeFalse();
        unsupportedPayload.ShouldBeNull();

        var malformedHashPayload = TokenPayload() with { FileSha256 = "not-hex" };
        var malformedHash = _tokenProtector.Protect(malformedHashPayload, TimeSpan.FromHours(1));
        _tokenProtector.TryValidate(
                malformedHash,
                malformedHashPayload.OperationId,
                malformedHashPayload.ClubId,
                malformedHashPayload.ActorUserId,
                new byte[malformedHashPayload.FileLength],
                out var rejectedPayload)
            .ShouldBeFalse();
        rejectedPayload.ShouldBeNull();

        var nullHashPayload = TokenPayload() with { FileSha256 = null! };
        var nullHash = _tokenProtector.Protect(nullHashPayload, TimeSpan.FromHours(1));
        _tokenProtector.TryValidate(
                nullHash,
                nullHashPayload.OperationId,
                nullHashPayload.ClubId,
                nullHashPayload.ActorUserId,
                new byte[nullHashPayload.FileLength],
                out var nullHashResult)
            .ShouldBeFalse();
        nullHashResult.ShouldBeNull();
    }

    private PlayerImportService CreateService(PlayerImportReadContextFactory? factory = null) => new(
        factory ?? new PlayerImportReadContextFactory(_harness),
        _harness.CurrentUser,
        new PlayerImportCsvParser(),
        _tokenProtector,
        TimeProvider.System,
        NullLogger<PlayerImportService>.Instance);

    private static PlayerImportUploadInput Upload(string rows)
    {
        const string header = "First name,Last name,Date of birth,Gender,Jersey number,Graduation year\r\n";
        return new()
        {
            Content = Encoding.UTF8.GetBytes(header + rows),
            FileName = "players.csv",
            ContentType = "text/csv"
        };
    }

    private static string ValidRows() => "Taylor,Stone,2013-02-03,,,2031\r\n";

    private static PlayerImportPreviewTokenPayload TokenPayload()
    {
        var now = DateTimeOffset.UtcNow;
        return new(1, Guid.CreateVersion7(), ClubAId, AdminId, "HASH", 42, now, now.AddHours(1));
    }

    private void ActAs(long? userId, long? clubId, bool isAdmin)
    {
        _harness.CurrentUser.UserId = userId;
        _harness.CurrentUser.ClubId = clubId;
        _harness.CurrentUser.IsClubAdmin = isAdmin;
    }

    private void Seed()
    {
        using var db = _harness.CreateAdminContext();
        db.Clubs.AddRange(
            Club(ClubAId, "Club A"),
            Club(ClubBId, "Club B"));
        db.SaveChanges();

        db.Players.AddRange(
            Player(ClubAId, "Alex", "Archer", new DateOnly(2012, 1, 1), LifecycleStatus.Active),
            Player(ClubAId, "Archived", "Player", new DateOnly(2011, 1, 1), LifecycleStatus.Archived),
            Player(ClubBId, "Only", "OtherClub", new DateOnly(2014, 4, 5), LifecycleStatus.Active));
        db.SaveChanges();
    }

    private static ClubEntity Club(long clubId, string name) => new()
    {
        ClubId = clubId,
        Name = name,
        City = "Austin",
        State = "TX",
        CreationOperationId = Guid.CreateVersion7(),
        CreatedById = AdminId
    };

    private static PlayerEntity Player(
        long clubId,
        string firstName,
        string lastName,
        DateOnly dateOfBirth,
        LifecycleStatus status) => new()
        {
            ClubId = clubId,
            FirstName = firstName,
            LastName = lastName,
            DateOfBirth = dateOfBirth,
            GraduationYear = 2030,
            LifecycleStatus = status,
            ArchivedAt = status == LifecycleStatus.Archived ? DateTimeOffset.UtcNow : null,
            ArchivedById = status == LifecycleStatus.Archived ? AdminId : null,
            CreationOperationId = Guid.CreateVersion7(),
            CreatedById = AdminId
        };
}
