using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Nova.Components.Account;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using Nova.Unit.Tests.Account;
using Nova.Unit.Tests.Data;
using NSubstitute;
using OneOf.Types;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Direct SQLite shell tests for <see cref="ClubCrestService"/>: change/remove authorization,
/// validation, variant uploads, replacement cleanup, failure cleanup, and claim staleness.
/// </summary>
public sealed class ClubCrestServiceTests : IDisposable
{
    private const long ClubAId = 200;
    private const long ClubBId = 201;
    private const long AdminUserId = 300;
    private const long MemberUserId = 301;
    private const long ClubBAdminUserId = 302;

    private readonly TenancyTestHarness _harness = new();

    public ClubCrestServiceTests()
    {
        Seed();
    }

    /// <inheritdoc />
    public void Dispose() => _harness.Dispose();

    [Fact]
    public async Task ChangeClubCrestAsync_ReturnsForbidden_WhenUserIsNotClubAdmin()
    {
        // Arrange
        _harness.CurrentUser.UserId = MemberUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;
        var service = CreateService();

        // Act
        var result = await service.ChangeClubCrestAsync(
            ClubAId,
            new ClubCrestUpload(TestImages.CreateJpeg(), "image/jpeg"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task ChangeClubCrestAsync_ReturnsForbidden_WhenUserHasNoUserId()
    {
        // Arrange
        _harness.CurrentUser.UserId = null;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;
        var service = CreateService();

        // Act
        var result = await service.ChangeClubCrestAsync(
            ClubAId,
            new ClubCrestUpload(TestImages.CreateJpeg(), "image/jpeg"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task ChangeClubCrestAsync_ReturnsForbidden_WhenAdminOfAnotherClub()
    {
        // Arrange
        _harness.CurrentUser.UserId = ClubBAdminUserId;
        _harness.CurrentUser.ClubId = ClubBId;
        _harness.CurrentUser.IsClubAdmin = true;
        var service = CreateService();

        // Act
        var result = await service.ChangeClubCrestAsync(
            ClubAId,
            new ClubCrestUpload(TestImages.CreateJpeg(), "image/jpeg"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// An empty upload is rejected with a structured "crest" validation error.
    /// </summary>
    [Fact]
    public async Task ChangeClubCrestAsync_ReturnsValidation_WhenCrestIsMissing()
    {
        // Arrange
        SetClubAAdmin();
        var service = CreateService();

        // Act
        var result = await service.ChangeClubCrestAsync(
            ClubAId,
            new ClubCrestUpload([], "image/jpeg"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.Keys.ShouldContain("crest");
        result.Problem.Errors["crest"].ShouldContain("A club crest is required.");
    }

    /// <summary>
    /// An upload whose declared content type is not allowed is rejected with validation errors.
    /// </summary>
    [Fact]
    public async Task ChangeClubCrestAsync_ReturnsValidation_WhenContentTypeNotAllowed()
    {
        // Arrange
        SetClubAAdmin();
        var service = CreateService();

        // Act
        var result = await service.ChangeClubCrestAsync(
            ClubAId,
            new ClubCrestUpload([0xFF, 0xD8, 0xFF, 0xE0, 0x00], "image/gif"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors["crest"].ShouldContain("Only JPEG, PNG, and WebP images are allowed.");
    }

    /// <summary>
    /// An image whose dimensions exceed the processing maximum is rejected before decoding.
    /// </summary>
    [Fact]
    public async Task ChangeClubCrestAsync_ReturnsBadRequest_WhenImageTooLarge()
    {
        // Arrange
        SetClubAAdmin();
        var service = CreateService();

        // Act
        var result = await service.ChangeClubCrestAsync(
            ClubAId,
            new ClubCrestUpload(TestImages.CreateJpeg(9000, 8), "image/jpeg"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.BadRequest);
    }

    /// <summary>
    /// A file carrying an allowed signature but no actual image data is rejected as unprocessable.
    /// </summary>
    [Fact]
    public async Task ChangeClubCrestAsync_ReturnsBadRequest_WhenContentIsNotAnImage()
    {
        // Arrange
        SetClubAAdmin();
        var service = CreateService();

        // Act
        var result = await service.ChangeClubCrestAsync(
            ClubAId,
            new ClubCrestUpload([0xFF, 0xD8, 0xFF, 0xE0], "image/jpeg"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.BadRequest);
    }

    /// <summary>
    /// A successful change inserts a crest row for the club and uploads all four variants.
    /// </summary>
    [Fact]
    public async Task ChangeClubCrestAsync_InsertsCrestRow_AndUploadsVariants()
    {
        // Arrange
        SetClubAAdmin();
        var container = CreateCrestContainer();
        var service = CreateService(container);

        // Act
        var result = await service.ChangeClubCrestAsync(
            ClubAId,
            new ClubCrestUpload(TestImages.CreateJpeg(128, 96), "image/jpeg"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var crest = await verify.ClubCrests
            .SingleAsync(candidate => candidate.ClubId == ClubAId, TestContext.Current.CancellationToken);
        crest.OriginalBlobName.ShouldNotBeNullOrWhiteSpace();
        crest.SmallBlobName.ShouldNotBeNullOrWhiteSpace();
        crest.MediumBlobName.ShouldNotBeNullOrWhiteSpace();
        crest.LargeBlobName.ShouldNotBeNullOrWhiteSpace();
        crest.ContentType.ShouldBe("image/jpeg");
        crest.CreatedById.ShouldBe(AdminUserId);

        container.Received().GetBlobClient(crest.OriginalBlobName);
        container.Received().GetBlobClient(crest.SmallBlobName);
        container.Received().GetBlobClient(crest.MediumBlobName);
        container.Received().GetBlobClient(crest.LargeBlobName);
        container.Received(4).GetBlobClient(Arg.Any<string>());
    }

    /// <summary>
    /// Changing an existing crest updates the row and best-effort deletes the previous blobs.
    /// </summary>
    [Fact]
    public async Task ChangeClubCrestAsync_ReplacesExistingCrest_AndDeletesPreviousBlobs()
    {
        // Arrange
        SetClubAAdmin();
        SeedCrest(original: "clubs/200/old-original.jpg", small: "clubs/200/old-small.webp",
            medium: "clubs/200/old-medium.webp", large: "clubs/200/old-large.webp");
        var container = CreateCrestContainer();
        var service = CreateService(container);

        // Act
        var result = await service.ChangeClubCrestAsync(
            ClubAId,
            new ClubCrestUpload(TestImages.CreateJpeg(), "image/jpeg"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        var crest = await verify.ClubCrests
            .SingleAsync(candidate => candidate.ClubId == ClubAId, TestContext.Current.CancellationToken);
        crest.OriginalBlobName.ShouldNotBe("clubs/200/old-original.jpg");
        crest.CreatedById.ShouldBe(AdminUserId);
        verify.ClubCrests.Count().ShouldBe(1);

        await container.Received().DeleteBlobIfExistsAsync(
            "clubs/200/old-original.jpg", Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>());
        await container.Received().DeleteBlobIfExistsAsync(
            "clubs/200/old-small.webp", Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>());
        await container.Received().DeleteBlobIfExistsAsync(
            "clubs/200/old-medium.webp", Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>());
        await container.Received().DeleteBlobIfExistsAsync(
            "clubs/200/old-large.webp", Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// When a variant upload fails, already-uploaded blobs are deleted and a server error is returned.
    /// </summary>
    [Fact]
    public async Task ChangeClubCrestAsync_DeletesUploadedBlobs_WhenBlobUploadFails()
    {
        // Arrange
        SetClubAAdmin();
        var blob = Substitute.For<BlobClient>();
        blob.UploadAsync(Arg.Any<BinaryData>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>())
            .Returns(
                _ => Substitute.For<Response<BlobContentInfo>>(),
                _ => throw new RequestFailedException("upload failed"));
        var container = Substitute.For<BlobContainerClient>();
        container.GetBlobClient(Arg.Any<string>()).Returns(blob);
        container.DeleteBlobIfExistsAsync(Arg.Any<string>(), Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response.FromValue(true, (Response?)null)));
        var service = CreateService(container);

        // Act
        var result = await service.ChangeClubCrestAsync(
            ClubAId,
            new ClubCrestUpload(TestImages.CreateJpeg(), "image/jpeg"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        await container.Received(1).DeleteBlobIfExistsAsync(Arg.Any<string>(), Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>());

        await using var verify = _harness.CreateAdminContext();
        verify.ClubCrests.Any(candidate => candidate.ClubId == ClubAId).ShouldBeFalse();
    }

    [Fact]
    public async Task RemoveClubCrestAsync_ReturnsForbidden_WhenUserIsNotClubAdmin()
    {
        // Arrange
        _harness.CurrentUser.UserId = MemberUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = false;
        var service = CreateService();

        // Act
        var result = await service.RemoveClubCrestAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task RemoveClubCrestAsync_ReturnsNotFound_WhenNoCrestExists()
    {
        // Arrange
        SetClubAAdmin();
        var service = CreateService();

        // Act
        var result = await service.RemoveClubCrestAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Removing an existing crest deletes the row and best-effort deletes the blob set.
    /// </summary>
    [Fact]
    public async Task RemoveClubCrestAsync_DeletesRowAndBlobs_WhenCrestExists()
    {
        // Arrange
        SetClubAAdmin();
        SeedCrest(original: "clubs/200/old-original.jpg", small: "clubs/200/old-small.webp",
            medium: "clubs/200/old-medium.webp", large: "clubs/200/old-large.webp");
        var container = CreateCrestContainer();
        var service = CreateService(container);

        // Act
        var result = await service.RemoveClubCrestAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();

        await using var verify = _harness.CreateAdminContext();
        verify.ClubCrests.Any(candidate => candidate.ClubId == ClubAId).ShouldBeFalse();

        await container.Received().DeleteBlobIfExistsAsync(
            "clubs/200/old-original.jpg", Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>());
        await container.Received().DeleteBlobIfExistsAsync(
            "clubs/200/old-small.webp", Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>());
        await container.Received().DeleteBlobIfExistsAsync(
            "clubs/200/old-medium.webp", Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>());
        await container.Received().DeleteBlobIfExistsAsync(
            "clubs/200/old-large.webp", Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>());
    }

    /// <summary>
    /// After a successful change, every member of the club gets its security stamp bumped so the
    /// HasClubCrest claim propagates on the next revalidation; members of other clubs are untouched.
    /// </summary>
    [Fact]
    public async Task ChangeClubCrestAsync_MarksAllClubMembersClaimsStale_OnSuccess()
    {
        // Arrange
        SetClubAAdmin();
        var adminBefore = await LoadSecurityStampAsync(AdminUserId);
        var memberBefore = await LoadSecurityStampAsync(MemberUserId);
        var otherAdminBefore = await LoadSecurityStampAsync(ClubBAdminUserId);
        var service = CreateService();

        // Act
        var result = await service.ChangeClubCrestAsync(
            ClubAId,
            new ClubCrestUpload(TestImages.CreateJpeg(), "image/jpeg"),
            TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await LoadSecurityStampAsync(AdminUserId)).ShouldNotBe(adminBefore);
        (await LoadSecurityStampAsync(MemberUserId)).ShouldNotBe(memberBefore);
        (await LoadSecurityStampAsync(ClubBAdminUserId)).ShouldBe(otherAdminBefore);
    }

    /// <summary>
    /// After a successful remove, members' security stamps are bumped the same way.
    /// </summary>
    [Fact]
    public async Task RemoveClubCrestAsync_MarksAllClubMembersClaimsStale_OnSuccess()
    {
        // Arrange
        SetClubAAdmin();
        SeedCrest(original: "clubs/200/old-original.jpg", small: "clubs/200/old-small.webp",
            medium: "clubs/200/old-medium.webp", large: "clubs/200/old-large.webp");
        var memberBefore = await LoadSecurityStampAsync(MemberUserId);
        var service = CreateService();

        // Act
        var result = await service.RemoveClubCrestAsync(ClubAId, TestContext.Current.CancellationToken);

        // Assert
        result.IsSuccess.ShouldBeTrue();
        (await LoadSecurityStampAsync(MemberUserId)).ShouldNotBe(memberBefore);
    }

    private void SetClubAAdmin()
    {
        _harness.CurrentUser.UserId = AdminUserId;
        _harness.CurrentUser.ClubId = ClubAId;
        _harness.CurrentUser.IsClubAdmin = true;
    }

    private void SeedCrest(string original, string small, string medium, string large)
    {
        using var context = _harness.CreateAdminContext();
        context.ClubCrests.Add(new ClubCrestEntity
        {
            ClubId = ClubAId,
            OriginalBlobName = original,
            SmallBlobName = small,
            MediumBlobName = medium,
            LargeBlobName = large,
            ContentType = "image/jpeg",
            CreatedById = AdminUserId
        });
        context.SaveChanges();
    }

    private async Task<string?> LoadSecurityStampAsync(long userId)
    {
        await using var context = _harness.CreateAdminContext();
        return await context.Users
            .Where(user => user.Id == userId)
            .Select(user => user.SecurityStamp)
            .SingleAsync(TestContext.Current.CancellationToken);
    }

    private ClubCrestService CreateService(BlobContainerClient? container = null)
    {
        // The claim refresher queries userManager.Users (an IQueryable), so the user manager is
        // backed by a real Identity UserStore over the harness's shared SQLite database.
        var adminContext = _harness.CreateAdminContext();
        var userStore = new UserStore<NovaUserEntity, IdentityRole<long>, NovaAdminDbContext, long>(adminContext);
        var userManager = new UserManager<NovaUserEntity>(
            userStore,
            Options.Create(new IdentityOptions()),
            new PasswordHasher<NovaUserEntity>(),
            Array.Empty<IUserValidator<NovaUserEntity>>(),
            Array.Empty<IPasswordValidator<NovaUserEntity>>(),
            new UpperInvariantLookupNormalizer(),
            new IdentityErrorDescriber(),
            Substitute.For<IServiceProvider>(),
            NullLogger<UserManager<NovaUserEntity>>.Instance);

        var signInManager = Substitute.For<SignInManager<NovaUserEntity>>(
            userManager,
            Substitute.For<IHttpContextAccessor>(),
            Substitute.For<IUserClaimsPrincipalFactory<NovaUserEntity>>(),
            Substitute.For<IOptions<IdentityOptions>>(),
            NullLogger<SignInManager<NovaUserEntity>>.Instance,
            Substitute.For<Microsoft.AspNetCore.Authentication.IAuthenticationSchemeProvider>(),
            Substitute.For<IUserConfirmation<NovaUserEntity>>());

        var claimRefresher = new ClubMembershipClaimRefresher(userManager, signInManager);

        return new ClubCrestService(
            container ?? CreateCrestContainer(),
            new TestDbContextFactory<NovaDbContext>(_harness.CreateTenantContext),
            _harness.CurrentUser,
            claimRefresher,
            NullLogger<ClubCrestService>.Instance);
    }

    /// <summary>
    /// Creates a substitute blob container whose <see cref="BlobContainerClient.GetBlobClient"/>
    /// returns a fresh uploadable blob client per call, and whose best-effort deletes succeed.
    /// </summary>
    private static BlobContainerClient CreateCrestContainer()
    {
        var container = Substitute.For<BlobContainerClient>();
        container.GetBlobClient(Arg.Any<string>()).Returns(call =>
        {
            var blob = Substitute.For<BlobClient>();
            blob.UploadAsync(Arg.Any<BinaryData>(), Arg.Any<BlobUploadOptions>(), Arg.Any<CancellationToken>())
                .Returns(Substitute.For<Response<BlobContentInfo>>());
            return blob;
        });
        container.DeleteBlobIfExistsAsync(Arg.Any<string>(), Arg.Any<DeleteSnapshotsOption>(), Arg.Any<BlobRequestConditions>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(Response.FromValue(true, (Response?)null)));
        return container;
    }

    /// <summary>
    /// Seeds the club/user fixtures used across all tests.
    /// </summary>
    private void Seed()
    {
        using var context = _harness.CreateAdminContext();

        context.Clubs.AddRange(
            new ClubEntity { ClubId = ClubAId, Name = "Club A", City = "Austin", State = "TX", CreatedById = AdminUserId },
            new ClubEntity { ClubId = ClubBId, Name = "Club B", City = "Boston", State = "MA", CreatedById = ClubBAdminUserId });

        context.Users.AddRange(
            new NovaUserEntity
            {
                Id = AdminUserId,
                UserName = "admin@cluba.com",
                Email = "admin@cluba.com",
                FirstName = "ClubA",
                LastName = "Admin",
                ClubId = ClubAId
            },
            new NovaUserEntity
            {
                Id = MemberUserId,
                UserName = "member@cluba.com",
                Email = "member@cluba.com",
                FirstName = "ClubA",
                LastName = "Member",
                ClubId = ClubAId
            },
            new NovaUserEntity
            {
                Id = ClubBAdminUserId,
                UserName = "admin@clubb.com",
                Email = "admin@clubb.com",
                FirstName = "ClubB",
                LastName = "Admin",
                ClubId = ClubBId
            });

        context.SaveChanges();
    }
}
