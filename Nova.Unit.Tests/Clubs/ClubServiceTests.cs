using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using NSubstitute;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Direct SQLite shell tests for <see cref="ClubService"/>: club search projection and club creation
/// authorization, role assignment, and error mapping.
/// </summary>
public sealed class ClubServiceTests : IDisposable
{
    private const long NoClubUserId = 200;
    private const long ExistingClubUserId = 201;

    private readonly TenancyTestHarness _harness = new();
    private readonly BlobContainerClient _crestContainer = CreateCrestContainer();
    private UserManager<NovaUserEntity> _userManager = null!;

    /// <summary>
    /// Initializes the mocked <see cref="UserManager{TUser}"/> and seeded club data.
    /// </summary>
    public ClubServiceTests()
    {
        _userManager = CreateUserManagerMock();
        Seed();
    }

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task SearchClubsAsync_ReturnsAllClubsOrderedByName_WhenQueryIsBlank()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.SearchClubsAsync(null, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(club => club.Name).ShouldBe(["Alpha Club", "Beta Club", "Gamma Club"]);
    }

    [Fact]
    public async Task SearchClubsAsync_MatchesCaseInsensitively_AcrossNameCityAndState()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.SearchClubsAsync("AUSTIN", TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Select(club => club.Name).ShouldBe(["Alpha Club"]);
    }

    [Fact]
    public async Task SearchClubsAsync_TreatsLikeMetacharactersAsLiterals()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;

        await using (var seed = _harness.CreateAdminContext())
        {
            seed.Clubs.AddRange(
                new ClubEntity { Name = "50% Wins", City = "Dallas", State = "TX", CreatedById = ExistingClubUserId },
                new ClubEntity { Name = "50 Losses", City = "Erie", State = "PA", CreatedById = ExistingClubUserId },
                new ClubEntity { Name = "a_b Squad", City = "Fargo", State = "ND", CreatedById = ExistingClubUserId },
                new ClubEntity { Name = "axb Squad", City = "Tulsa", State = "OK", CreatedById = ExistingClubUserId },
                new ClubEntity { Name = @"Path\Team", City = "Boise", State = "ID", CreatedById = ExistingClubUserId },
                new ClubEntity { Name = "PathTeam", City = "Reno", State = "NV", CreatedById = ExistingClubUserId });
            await seed.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        var service = CreateService();

        var percent = await service.SearchClubsAsync("50%", TestContext.Current.CancellationToken);
        percent.Value.Select(club => club.Name).ShouldBe(["50% Wins"]);

        var underscore = await service.SearchClubsAsync("a_b", TestContext.Current.CancellationToken);
        underscore.Value.Select(club => club.Name).ShouldBe(["a_b Squad"]);

        var backslash = await service.SearchClubsAsync(@"Path\T", TestContext.Current.CancellationToken);
        backslash.Value.Select(club => club.Name).ShouldBe([@"Path\Team"]);
    }

    [Fact]
    public async Task CreateClubAsync_ReturnsValidation_WhenInputIsInvalid()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "   ", City = "Austin", State = "TX", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    [Fact]
    public async Task CreateClubAsync_ReturnsConflict_WhenUserAlreadyBelongsToClub()
    {
        _harness.CurrentUser.UserId = ExistingClubUserId;
        _harness.CurrentUser.ClubId = 1;
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "New Club", City = "Austin", State = "TX", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Fact]
    public async Task CreateClubAsync_ReturnsForbidden_WhenNotAuthenticated()
    {
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "New Club", City = "Austin", State = "TX", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task CreateClubAsync_ReturnsServerError_WhenUserNotFound()
    {
        _harness.CurrentUser.UserId = 999_999;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "New Club", City = "Austin", State = "TX", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task CreateClubAsync_CreatesClub_AndAssignsMembership()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        _userManager.FindByIdAsync(NoClubUserId.ToString()).Returns(Task.FromResult((NovaUserEntity?)null));
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "Created Club", City = "Austin", State = "TX", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Name.ShouldBe("Created Club");

        await using var verify = _harness.CreateAdminContext();
        var club = await verify.Clubs
            .SingleAsync(candidate => candidate.Name == "Created Club", TestContext.Current.CancellationToken);
        club.City.ShouldBe("Austin");
        club.State.ShouldBe("TX");

        var user = await verify.Users
            .SingleAsync(candidate => candidate.Id == NoClubUserId, TestContext.Current.CancellationToken);
        user.ClubId.ShouldBe(club.ClubId);
    }

    [Fact]
    public async Task CreateClubAsync_AssignsClubAdminRole_WhenRoleAssignmentSucceeds()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        var user = await LoadUserAsync(NoClubUserId);
        _userManager.FindByIdAsync(NoClubUserId.ToString()).Returns(Task.FromResult(user));
        _userManager.AddToRoleAsync(Arg.Any<NovaUserEntity>(), Roles.ClubAdmin)
            .Returns(Task.FromResult(IdentityResult.Success));
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "Role Club", City = "Austin", State = "TX", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        await _userManager.Received().AddToRoleAsync(Arg.Any<NovaUserEntity>(), Roles.ClubAdmin);
    }

    [Fact]
    public async Task CreateClubAsync_ReturnsValidation_WhenCrestIsMissing()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "Crestless", City = "Austin", State = "TX", CrestContent = [], CrestContentType = "image/jpeg" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.Keys.ShouldContain("crest");
    }

    [Fact]
    public async Task CreateClubAsync_ReturnsValidation_WhenCrestIsNotAnImage()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "Stray", City = "Austin", State = "TX", CrestContent = [1, 2, 3, 4, 5, 6, 7, 8], CrestContentType = "image/jpeg" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.Keys.ShouldContain("crest");
    }

    [Fact]
    public async Task CreateClubAsync_PersistsCrestEntity_AndUploadsVariants()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        _userManager.FindByIdAsync(NoClubUserId.ToString()).Returns(Task.FromResult((NovaUserEntity?)null));
        var service = CreateService();

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "Crest Club", City = "Austin", State = "TX", CrestContent = TestImages.CreateJpeg(128, 96), CrestContentType = "image/jpeg" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var crest = await verify.ClubCrests
            .SingleAsync(candidate => candidate.Club!.Name == "Crest Club", TestContext.Current.CancellationToken);
        crest.OriginalBlobName.ShouldNotBeNullOrWhiteSpace();
        crest.SmallBlobName.ShouldNotBeNullOrWhiteSpace();
        crest.MediumBlobName.ShouldNotBeNullOrWhiteSpace();
        crest.LargeBlobName.ShouldNotBeNullOrWhiteSpace();
        crest.ContentType.ShouldBe("image/jpeg");

        _crestContainer.Received().GetBlobClient(crest.OriginalBlobName);
        _crestContainer.Received().GetBlobClient(crest.SmallBlobName);
        _crestContainer.Received().GetBlobClient(crest.MediumBlobName);
        _crestContainer.Received().GetBlobClient(crest.LargeBlobName);
    }

    [Fact]
    public async Task CreateClubAsync_DeletesUploadedBlobs_WhenBlobUploadFails()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        _userManager.FindByIdAsync(NoClubUserId.ToString()).Returns(Task.FromResult((NovaUserEntity?)null));

        var blob = Substitute.For<BlobClient>();
        blob.UploadAsync(Arg.Any<BinaryData>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Substitute.For<Response<BlobContentInfo>>(),
                _ => throw new RequestFailedException("upload failed"));
        var container = Substitute.For<BlobContainerClient>();
        container.GetBlobClient(Arg.Any<string>()).Returns(blob);
        container.DeleteBlobIfExistsAsync(Arg.Any<string>(), Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response.FromValue(true, (Response?)null)));
        var service = new ClubService(
            new TestDbContextFactory<NovaAdminDbContext>(_harness.CreateAdminContext),
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _userManager,
            _harness.CurrentUser,
            container,
            NullLogger<ClubService>.Instance);

        var result = await service.CreateClubAsync(
            new CreateClubInput { Name = "Doomed Club", City = "Austin", State = "TX", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        await container.Received().DeleteBlobIfExistsAsync(Arg.Any<string>(), Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>());

        await using var verify = _harness.CreateAdminContext();
        verify.Clubs.Any(candidate => candidate.Name == "Doomed Club").ShouldBeFalse();
    }

    [Fact]
    public async Task CreateClubAsync_DeletesUploadedBlobs_WhenUploadCancelled()
    {
        _harness.CurrentUser.UserId = NoClubUserId;
        _harness.CurrentUser.ClubId = null;
        _userManager.FindByIdAsync(NoClubUserId.ToString()).Returns(Task.FromResult((NovaUserEntity?)null));

        var blob = Substitute.For<BlobClient>();
        blob.UploadAsync(Arg.Any<BinaryData>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Substitute.For<Response<BlobContentInfo>>(),
                _ => throw new OperationCanceledException("upload cancelled"));
        var container = Substitute.For<BlobContainerClient>();
        container.GetBlobClient(Arg.Any<string>()).Returns(blob);
        container.DeleteBlobIfExistsAsync(Arg.Any<string>(), Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response.FromValue(true, (Response?)null)));
        var service = new ClubService(
            new TestDbContextFactory<NovaAdminDbContext>(_harness.CreateAdminContext),
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _userManager,
            _harness.CurrentUser,
            container,
            NullLogger<ClubService>.Instance);

        await Should.ThrowAsync<OperationCanceledException>(
            () => service.CreateClubAsync(
                new CreateClubInput { Name = "Cancelled Club", City = "Austin", State = "TX", CrestContent = TestImages.CreateJpeg(), CrestContentType = "image/jpeg" },
                TestContext.Current.CancellationToken));

        // Exactly one upload succeeded before the cancellation, so exactly one blob is cleaned up.
        await container.Received(1).DeleteBlobIfExistsAsync(
            Arg.Any<string>(),
            Arg.Any<DeleteSnapshotsOption>(),
            Arg.Any<BlobRequestConditions>(),
            Arg.Any<CancellationToken>());

        await using var verify = _harness.CreateAdminContext();
        verify.Clubs.Any(candidate => candidate.Name == "Cancelled Club").ShouldBeFalse();
    }

    private ClubService CreateService()
        => new(
            new TestDbContextFactory<NovaAdminDbContext>(_harness.CreateAdminContext),
            new TestDbContextFactory<NovaReadDbContext>(_harness.CreateReadContext),
            _userManager,
            _harness.CurrentUser,
            _crestContainer,
            NullLogger<ClubService>.Instance);

    /// <summary>
    /// Creates a substitute blob container that records blob uploads (used for
    /// asserting crest variant uploads) and reports a successful nullable upload.
    /// </summary>
    private static BlobContainerClient CreateCrestContainer()
    {
        var blob = Substitute.For<BlobClient>();
        blob.UploadAsync(Arg.Any<BinaryData>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>())
            .Returns(Substitute.For<Response<BlobContentInfo>>());
        var container = Substitute.For<BlobContainerClient>();
        container.GetBlobClient(Arg.Any<string>()).Returns(blob);
        container.DeleteBlobIfExistsAsync(Arg.Any<string>(), Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response.FromValue(true, (Response?)null)));
        return container;
    }

    private async Task<NovaUserEntity?> LoadUserAsync(long userId)
    {
        await using var db = _harness.CreateAdminContext();
        return await db.Users.SingleAsync(
            candidate => candidate.Id == userId,
            TestContext.Current.CancellationToken);
    }

    private static UserManager<NovaUserEntity> CreateUserManagerMock()
    {
        var store = Substitute.For<IUserStore<NovaUserEntity>>();
        var userManager = Substitute.For<UserManager<NovaUserEntity>>(
            store,
            Substitute.For<IOptions<IdentityOptions>>(),
            Substitute.For<IPasswordHasher<NovaUserEntity>>(),
            new List<IUserValidator<NovaUserEntity>>(),
            new List<IPasswordValidator<NovaUserEntity>>(),
            Substitute.For<ILookupNormalizer>(),
            Substitute.For<IdentityErrorDescriber>(),
            Substitute.For<IServiceProvider>(),
            Substitute.For<ILogger<UserManager<NovaUserEntity>>>());

        userManager.FindByIdAsync(Arg.Any<string>()).Returns(Task.FromResult((NovaUserEntity?)null));
        userManager.AddToRoleAsync(Arg.Any<NovaUserEntity>(), Roles.ClubAdmin)
            .Returns(Task.FromResult(IdentityResult.Success));
        return userManager;
    }

    private void Seed()
    {
        using var db = _harness.CreateAdminContext();

        db.Clubs.AddRange(
            new ClubEntity { ClubId = 1, Name = "Alpha Club", City = "Austin", State = "TX", CreatedById = ExistingClubUserId },
            new ClubEntity { ClubId = 2, Name = "Beta Club", City = "Boston", State = "MA", CreatedById = ExistingClubUserId },
            new ClubEntity { ClubId = 3, Name = "Gamma Club", City = "Denver", State = "CO", CreatedById = ExistingClubUserId });
        db.SaveChanges();

        db.Users.AddRange(
            new NovaUserEntity
            {
                Id = NoClubUserId,
                UserName = "noclub@example.com",
                Email = "noclub@example.com",
                FirstName = "No",
                LastName = "Club",
                ClubId = null
            },
            new NovaUserEntity
            {
                Id = ExistingClubUserId,
                UserName = "member@example.com",
                Email = "member@example.com",
                FirstName = "Existing",
                LastName = "Member",
                ClubId = 1
            });

        db.SaveChanges();
    }
}
