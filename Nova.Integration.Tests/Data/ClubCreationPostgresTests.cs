using Azure.Storage.Blobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Nova.Data;
using Nova.Entities;
using Nova.Features.Clubs;
using Nova.Shared.Features.Clubs;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Integration.Tests.Data;

/// <summary>
/// Verifies club creation retry behavior on PostgreSQL: an ambiguous commit is reconstructed
/// from the committed row instead of replayed, a transient pre-commit failure retries with a
/// fresh context, and the filtered operation-id index rejects a duplicate club per creator.
/// These scenarios cannot be modeled by the SQLite harness (no provider execution strategy,
/// no ambiguous commits), so they are exercised against the live database.
/// </summary>
/// <param name="fixture">The shared Aspire AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class ClubCreationPostgresTests(NovaAppHostFixture fixture)
{
    /// <summary>
    /// Verifies an ambiguous commit returns the original club: the committed club and crest row
    /// are found by the operation id and the reconstructed <see cref="ClubDto"/> is returned
    /// without replaying the insert, and the uploaded crest blobs are retained.
    /// </summary>
    [Fact]
    public async Task Create_VerifiesCompleteClub_AfterAmbiguousCommitFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedUserAsync(cancellationToken);
        ActAs(seed.UserId, clubId: null);

        var failureInterceptor = new FailFirstCommittedTransactionInterceptor();
        var factory = new RetryingAdminDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var input = ClubInput(seed.Suffix);

        var result = await CreateClubService(factory).CreateClubAsync(input, cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBe(3);

        await using var verify = fixture.CreateAdminContext();
        var club = await verify.Clubs.SingleAsync(
            candidate => candidate.CreatedById == seed.UserId
                && candidate.Name == input.Name,
            cancellationToken);
        club.CreationOperationId.ShouldNotBe(Guid.Empty);
        (await verify.Clubs.CountAsync(
            candidate => candidate.CreatedById == seed.UserId
                && candidate.CreationOperationId == club.CreationOperationId,
            cancellationToken)).ShouldBe(1);
        (await verify.ClubCrests.CountAsync(
            candidate => candidate.ClubId == club.ClubId,
            cancellationToken)).ShouldBe(1);

        result.Value.ClubId.ShouldBe(club.ClubId);
        result.Value.Name.ShouldBe(input.Name);
        result.Value.City.ShouldBe(input.City);
        result.Value.State.ShouldBe(input.State);
        await AssertMembershipIdentityEffectsAsync(verify, seed, club.ClubId, cancellationToken);

        // The commit happened, so the blobs the committed crest row references must still exist.
        var crest = await verify.ClubCrests.SingleAsync(
            candidate => candidate.ClubId == club.ClubId,
            cancellationToken);
        await AssertBlobsExistAsync(
            [crest.OriginalBlobName, crest.SmallBlobName!, crest.MediumBlobName!, crest.LargeBlobName!],
            cancellationToken);
    }

    /// <summary>
    /// Verifies a transient failure before the commit rolls back and retries with a fresh context,
    /// leaving exactly one club and one crest row for the operation. The service creates three
    /// contexts: strategy setup, the failed attempt, and the successful retry — the verification
    /// callback returns early without a context when the attempt never reached its commit.
    /// </summary>
    [Fact]
    public async Task Create_RetriesFreshTransaction_AfterTransientSaveFailure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedUserAsync(cancellationToken);
        ActAs(seed.UserId, clubId: null);

        var failureInterceptor = new FailFirstSaveChangesInterceptor();
        var factory = new RetryingAdminDbContextFactory(
            fixture.ConnectionString,
            fixture.CurrentUser,
            failureInterceptor);
        var input = ClubInput(seed.Suffix);

        var result = await CreateClubService(factory).CreateClubAsync(input, cancellationToken);

        result.IsSuccess.ShouldBeTrue();
        failureInterceptor.FailureCount.ShouldBe(1);
        factory.CreatedContextCount.ShouldBe(3);

        await using var verify = fixture.CreateAdminContext();
        var club = await verify.Clubs.SingleAsync(
            candidate => candidate.CreatedById == seed.UserId
                && candidate.Name == input.Name,
            cancellationToken);
        (await verify.Clubs.CountAsync(
            candidate => candidate.CreatedById == seed.UserId
                && candidate.CreationOperationId == club.CreationOperationId,
            cancellationToken)).ShouldBe(1);
        (await verify.ClubCrests.CountAsync(
            candidate => candidate.ClubId == club.ClubId,
            cancellationToken)).ShouldBe(1);
        await AssertMembershipIdentityEffectsAsync(verify, seed, club.ClubId, cancellationToken);

        var crest = await verify.ClubCrests.SingleAsync(
            candidate => candidate.ClubId == club.ClubId,
            cancellationToken);
        await AssertBlobsExistAsync(
            [crest.OriginalBlobName, crest.SmallBlobName!, crest.MediumBlobName!, crest.LargeBlobName!],
            cancellationToken);
    }

    /// <summary>
    /// Verifies the filtered unique index on (CreatedById, CreationOperationId) rejects a second
    /// club created by the same user for the same logical creation operation.
    /// </summary>
    [Fact]
    public async Task ClubCreationOperationId_RejectsDuplicateWithinCreator()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var seed = await SeedUserAsync(cancellationToken);
        var operationId = Guid.CreateVersion7();

        await using var db = fixture.CreateAdminContext();
        db.Clubs.AddRange(
            ClubEntity(seed.Suffix, seed.UserId, operationId, name: "First"),
            ClubEntity(seed.Suffix, seed.UserId, operationId, name: "Second"));

        await Should.ThrowAsync<DbUpdateException>(() => db.SaveChangesAsync(cancellationToken));
    }

    /// <summary>
    /// Creates the club service with the supplied retry-enabled admin context factory.
    /// </summary>
    /// <param name="factory">The context factory used for execution attempts.</param>
    /// <returns>A club service.</returns>
    private ClubService CreateClubService(IDbContextFactory<NovaAdminDbContext> factory) => new(
        factory,
        new PostgresReadContextFactory(fixture),
        fixture.CurrentUser,
        fixture.ClubCrestsContainer,
        NullLogger<ClubService>.Instance);

    /// <summary>
    /// Seeds one identity user with a database-generated id and returns its id and unique suffix.
    /// </summary>
    /// <param name="cancellationToken">A token that cancels seeding.</param>
    /// <returns>The generated user identifiers.</returns>
    private async Task<ClubCreationSeed> SeedUserAsync(CancellationToken cancellationToken)
    {
        ActAs(userId: null, clubId: null);
        await using var db = fixture.CreateAdminContext();
        var suffix = Guid.NewGuid().ToString("N");
        var securityStamp = Guid.NewGuid().ToString("N");
        var concurrencyStamp = Guid.NewGuid().ToString("N");
        var user = new NovaUserEntity
        {
            FirstName = "Club",
            LastName = $"Creator {suffix}",
            SecurityStamp = securityStamp,
            ConcurrencyStamp = concurrencyStamp,
        };
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
        return new ClubCreationSeed(user.Id, suffix, securityStamp, concurrencyStamp);
    }

    /// <summary>
    /// Sets the current tenant identity used by newly created contexts.
    /// </summary>
    /// <param name="userId">The acting user identifier.</param>
    /// <param name="clubId">The acting club identifier.</param>
    private void ActAs(long? userId, long? clubId)
    {
        fixture.CurrentUser.UserId = userId;
        fixture.CurrentUser.ClubId = clubId;
        fixture.CurrentUser.IsClubAdmin = false;
    }

    /// <summary>
    /// Creates a valid club-creation request.
    /// </summary>
    /// <param name="suffix">A value that keeps names isolated in the shared database.</param>
    /// <returns>A valid club-creation request.</returns>
    private static CreateClubInput ClubInput(string suffix) => new()
    {
        Name = $"Club Creation Club {suffix}",
        City = "Austin",
        State = "TX",
        CrestContent = CreateJpeg(),
        CrestContentType = "image/jpeg"
    };

    /// <summary>
    /// Creates a club entity for direct constraint tests.
    /// </summary>
    /// <param name="suffix">A value that keeps names isolated in the shared database.</param>
    /// <param name="createdById">The seeding actor identifier.</param>
    /// <param name="operationId">The creation operation identifier.</param>
    /// <param name="name">A name segment that separates the two inserted rows.</param>
    /// <returns>A club ready for insertion.</returns>
    private static ClubEntity ClubEntity(
        string suffix,
        long createdById,
        Guid operationId,
        string name) => new()
        {
            Name = $"{name} Club {suffix}",
            City = "Austin",
            State = "TX",
            CreatedById = createdById,
            CreationOperationId = operationId
        };

    /// <summary>
    /// Creates an in-memory JPEG of the requested dimensions for the crest upload.
    /// </summary>
    /// <returns>The encoded JPEG bytes.</returns>
    private static byte[] CreateJpeg()
    {
        using var image = new Image<Rgba32>(128, 96, new Rgba32(120, 180, 240));
        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder());
        return stream.ToArray();
    }

    /// <summary>
    /// Asserts every named crest blob still exists in the live crest container.
    /// </summary>
    /// <param name="blobNames">The blob names to assert on.</param>
    /// <param name="cancellationToken">A token that cancels the existence checks.</param>
    private async Task AssertBlobsExistAsync(
        IReadOnlyList<string> blobNames,
        CancellationToken cancellationToken)
    {
        foreach (var blobName in blobNames)
        {
            (await fixture.ClubCrestsContainer.GetBlobClient(blobName).ExistsAsync(cancellationToken)).Value
                .ShouldBeTrue($"blob '{blobName}' should still exist after the committed club creation");
        }
    }

    /// <summary>
    /// Asserts club creation atomically assigned membership, administrator role, and fresh Identity
    /// stamps to the creator.
    /// </summary>
    /// <param name="db">The verification context.</param>
    /// <param name="seed">The creator's original Identity state.</param>
    /// <param name="clubId">The created club identifier.</param>
    /// <param name="cancellationToken">A token that cancels verification.</param>
    private static async Task AssertMembershipIdentityEffectsAsync(
        NovaAdminDbContext db,
        ClubCreationSeed seed,
        long clubId,
        CancellationToken cancellationToken)
    {
        var user = await db.Users.SingleAsync(candidate => candidate.Id == seed.UserId, cancellationToken);
        user.ClubId.ShouldBe(clubId);
        user.SecurityStamp.ShouldNotBe(seed.SecurityStamp);
        user.ConcurrencyStamp.ShouldNotBe(seed.ConcurrencyStamp);

        var administratorRoleId = await db.Roles
            .Where(role => role.NormalizedName == Nova.Shared.Security.Roles.ClubAdmin.ToUpperInvariant())
            .Select(role => role.Id)
            .SingleAsync(cancellationToken);
        (await db.UserRoles.AnyAsync(
            role => role.UserId == seed.UserId && role.RoleId == administratorRoleId,
            cancellationToken)).ShouldBeTrue();
    }

    /// <summary>
    /// Holds one test's user identity and unique data suffix.
    /// </summary>
    /// <param name="UserId">The seeded user identifier.</param>
    /// <param name="Suffix">The unique data suffix.</param>
    /// <param name="SecurityStamp">The creator's security stamp before club creation.</param>
    /// <param name="ConcurrencyStamp">The creator's concurrency stamp before club creation.</param>
    private sealed record ClubCreationSeed(
        long UserId,
        string Suffix,
        string SecurityStamp,
        string ConcurrencyStamp);
}
