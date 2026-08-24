using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Entities;
using Nova.Integration.Tests.Data;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Photos;
using Nova.Shared.Results;
using Shouldly;
using SixLabors.ImageSharp;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP tests for the club crest endpoints against the real running app
/// (Aspire AppHost: Postgres + Azurite + the Nova web app). Covers the required-crest
/// multipart club creation, crest retrieval variants with ETag/304 caching, and the
/// ClubAdmin-only change/remove management boundary including blob lifecycle.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class ClubCrestHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies club creation without a crest file fails with a validation problem and
    /// creates no club (the crest is a required part of the multipart payload).
    /// </summary>
    [Fact]
    public async Task CreateClub_WithoutCrest_ReturnsValidationProblem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client, SeedingHelpers.UniqueEmail("crest-nofile"), Password, cancellationToken);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("No Crest Club"), "name");
        form.Add(new StringContent("Austin"), "city");
        form.Add(new StringContent("TX"), "state");

        using var response = await client.PostAsync(ClubEndpoints.Create, form, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        problem.Errors.ShouldNotBeNull();
        problem.Errors.ShouldContainKey("crest");
    }

    /// <summary>
    /// Verifies the full create-with-crest flow: the club row, the crest row with four blob
    /// names, and the served small/medium/large WebP variants with ETag caching (304 on a
    /// conditional request).
    /// </summary>
    [Fact]
    public async Task CreateClub_WithCrest_PersistsRowAndServesVariants()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        var email = SeedingHelpers.UniqueEmail("crest-owner");
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client, email, Password, cancellationToken);

        var club = await SeedingHelpers.CreateClubAsync(client, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);

        // The crest row is persisted with all four blob names and the original content type.
        await using (var db = fixture.CreateAdminContext())
        {
            // Creation-time blob names are keyed by clubs/{userId}/{batchId} — stable across
            // retried inserts that can get a different club id (see ClubService.CreateClubAsync) —
            // so look the acting user up through the admin context before asserting the prefix.
            var userId = (await db.Users.SingleAsync(
                candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken)).Id;
            var crest = await db.ClubCrests.SingleAsync(
                candidate => candidate.ClubId == club.ClubId, cancellationToken);
            crest.OriginalBlobName.ShouldStartWith($"clubs/{userId}/");
            crest.OriginalBlobName.ShouldEndWith("-original.jpg");
            crest.SmallBlobName.ShouldNotBeNull();
            crest.SmallBlobName.ShouldEndWith("-small.webp");
            crest.MediumBlobName.ShouldNotBeNull();
            crest.MediumBlobName.ShouldEndWith("-medium.webp");
            crest.LargeBlobName.ShouldNotBeNull();
            crest.LargeBlobName.ShouldEndWith("-large.webp");
            crest.ContentType.ShouldBe("image/jpeg");
        }

        // Each square variant is served as WebP with no-cache + ETag headers.
        var etags = new List<string>();
        foreach (var size in new[] { ProfilePhotoSize.Small, ProfilePhotoSize.Medium, ProfilePhotoSize.Large })
        {
            using var variant = await client.GetAsync(
                ClubCrestEndpoints.GetCrestUrl(club.ClubId, size), cancellationToken);
            variant.StatusCode.ShouldBe(HttpStatusCode.OK);
            variant.Content.Headers.ContentType?.MediaType.ShouldBe("image/webp");
            variant.Headers.CacheControl?.NoCache.ShouldBeTrue($"the {size} variant must be revalidated");
            variant.Headers.ETag.ShouldNotBeNull();
            etags.Add(variant.Headers.ETag.Tag);
        }

        // A conditional request for the same variant returns 304.
        using (var conditional = new HttpRequestMessage(
            HttpMethod.Get, ClubCrestEndpoints.GetCrestUrl(club.ClubId, ProfilePhotoSize.Medium)))
        {
            conditional.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etags[1]));
            using var notModified = await client.SendAsync(conditional, cancellationToken);
            notModified.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        }
    }

    /// <summary>
    /// Verifies the original variant is never served (404, non-disclosing) and an undefined
    /// size value falls back to the medium WebP variant rather than the original.
    /// </summary>
    [Fact]
    public async Task GetCrest_OriginalIsRejected_AndUndefinedSizeFallsBackToMedium()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            client, SeedingHelpers.UniqueEmail("crest-sizes"), Password, cancellationToken);

        var club = await SeedingHelpers.CreateClubAsync(client, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);

        using (var original = await client.GetAsync(
            ClubCrestEndpoints.GetCrestUrl(club.ClubId, ProfilePhotoSize.Original), cancellationToken))
        {
            original.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            var problem = await original.ToServiceProblemAsync(cancellationToken);
            problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
        }

        using var fallback = await client.GetAsync(
            $"/api/clubs/{club.ClubId}/crest?size=99", cancellationToken);
        fallback.StatusCode.ShouldBe(HttpStatusCode.OK);
        fallback.Content.Headers.ContentType?.MediaType.ShouldBe("image/webp");
    }

    /// <summary>
    /// Verifies GET crest requires authentication (route registered at its absolute path,
    /// not inside a group that would redirect anonymous callers).
    /// </summary>
    [Fact]
    public async Task GetCrest_ReturnsUnauthorized_ForAnonymous()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var response = await client.GetAsync(
            ClubCrestEndpoints.GetCrestUrl(long.MaxValue, ProfilePhotoSize.Medium), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    /// <summary>
    /// Verifies a user without a club cannot read a crest (tenant filter yields 404) and an
    /// authenticated user of another club cannot read this club's crest either.
    /// </summary>
    [Fact]
    public async Task GetCrest_ReturnsNotFound_ForOutOfTenantUsers()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var ownerClient = fixture.CreateNovaHttpClient();
        using var foreignClient = fixture.CreateNovaHttpClient();

        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            ownerClient, SeedingHelpers.UniqueEmail("crest-tenant-a"), Password, cancellationToken);
        var club = await SeedingHelpers.CreateClubAsync(ownerClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(ownerClient, cancellationToken);

        // User B is registered but has no club yet.
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(
            foreignClient, SeedingHelpers.UniqueEmail("crest-tenant-b"), Password, cancellationToken);
        using (var clubless = await foreignClient.GetAsync(
            ClubCrestEndpoints.GetCrestUrl(club.ClubId, ProfilePhotoSize.Medium), cancellationToken))
        {
            clubless.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // User B joins a different club; club A's crest is still invisible to them.
        var foreignClub = await SeedingHelpers.CreateClubAsync(foreignClient, cancellationToken);
        await SeedingHelpers.RefreshClubMembershipCookieAsync(foreignClient, cancellationToken);
        foreignClub.ClubId.ShouldNotBe(club.ClubId);
        using (var crossClub = await foreignClient.GetAsync(
            ClubCrestEndpoints.GetCrestUrl(club.ClubId, ProfilePhotoSize.Medium), cancellationToken))
        {
            crossClub.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    /// <summary>
    /// Verifies a club admin can change the crest (204): the row points at a new batch of
    /// blobs, the previous blobs are deleted, the new blobs exist, and the response reissues
    /// the admin's authentication cookie so the HasClubCrest claim updates immediately.
    /// </summary>
    [Fact]
    public async Task ChangeCrest_ReturnsNoContent_ReplacesBlobs_AndRefreshesCookie()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "crest-change", "Crest Change Club", cancellationToken);
        var oldBlobNames = await GetCrestBlobNamesAsync(admin.Club.ClubId, cancellationToken);

        using var content = CreateCrestContent(SeedingHelpers.CreateJpegBytes(width: 300, height: 200));
        using var response = await adminClient.PostAsync(
            ClubCrestEndpoints.ChangeCrestUrl(admin.Club.ClubId), content, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        // Set-Cookie proves the immediate cookie refresh hop reissued the auth cookie.
        response.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue();
        cookies.ShouldContain(cookie => cookie.StartsWith(".AspNetCore.Identity.Application", StringComparison.Ordinal),
            "the acting admin's cookie must be reissued so HasClubCrest applies immediately");

        var newBlobNames = await GetCrestBlobNamesAsync(admin.Club.ClubId, cancellationToken);
        newBlobNames.ShouldNotBe(oldBlobNames, "the crest row must reference a new batch of blobs");
        newBlobNames.Count.ShouldBe(4);

        foreach (var blobName in oldBlobNames)
        {
            (await fixture.ClubCrestsContainer.GetBlobClient(blobName).ExistsAsync(cancellationToken))
                .Value.ShouldBeFalse($"the previous blob '{blobName}' must be deleted after the change");
        }

        foreach (var blobName in newBlobNames)
        {
            (await fixture.ClubCrestsContainer.GetBlobClient(blobName).ExistsAsync(cancellationToken))
                .Value.ShouldBeTrue($"the new blob '{blobName}' must exist after the change");
        }
    }

    /// <summary>
    /// Verifies variant generation for a non-square crest source after a change:
    /// the small variant is a 64×64 square, while the medium and large variants preserve the
    /// source aspect ratio and never exceed their maximum bound.
    /// </summary>
    [Fact]
    public async Task ChangeCrest_NonSquareSource_ServesAspectPreservingVariants()
    {
        const int sourceWidth = 300;
        const int sourceHeight = 200;
        const double sourceAspect = (double)sourceWidth / sourceHeight;
        const double aspectTolerance = 0.02;

        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "crest-aspect", "Aspect Crest Club", cancellationToken);

        using var content = CreateCrestContent(SeedingHelpers.CreateJpegBytes(width: sourceWidth, height: sourceHeight));
        using (var response = await adminClient.PostAsync(
            ClubCrestEndpoints.ChangeCrestUrl(admin.Club.ClubId), content, cancellationToken))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // The small variant is a 64×64 center-cropped square.
        using (var small = await adminClient.GetAsync(
            ClubCrestEndpoints.GetCrestUrl(admin.Club.ClubId, ProfilePhotoSize.Small), cancellationToken))
        {
            small.StatusCode.ShouldBe(HttpStatusCode.OK);
            small.Content.Headers.ContentType?.MediaType.ShouldBe("image/webp");
            using var image = Image.Load(await small.Content.ReadAsByteArrayAsync(cancellationToken));
            image.Width.ShouldBe(ProfilePhotoConstraints.SmallSize);
            image.Height.ShouldBe(ProfilePhotoConstraints.SmallSize);
        }

        // Medium and large variants preserve the source aspect ratio and fit their bound.
        foreach (var (size, maxDimension) in new[]
        {
            (ProfilePhotoSize.Medium, ProfilePhotoConstraints.MediumSize),
            (ProfilePhotoSize.Large, ProfilePhotoConstraints.LargeSize)
        })
        {
            using var variant = await adminClient.GetAsync(
                ClubCrestEndpoints.GetCrestUrl(admin.Club.ClubId, size), cancellationToken);
            variant.StatusCode.ShouldBe(HttpStatusCode.OK);
            variant.Content.Headers.ContentType?.MediaType.ShouldBe("image/webp");

            using var image = Image.Load(await variant.Content.ReadAsByteArrayAsync(cancellationToken));
            image.Width.ShouldBeLessThanOrEqualTo(maxDimension);
            image.Height.ShouldBeLessThanOrEqualTo(maxDimension);
            image.Width.ShouldNotBe(image.Height, "a non-square source must not produce a square variant");
            (image.Width / (double)image.Height).ShouldBe(sourceAspect, aspectTolerance);
        }
    }

    /// <summary>
    /// Verifies the change endpoint rejects a missing crest file (400 validation, not a 500).
    /// </summary>
    [Fact]
    public async Task ChangeCrest_WithoutFile_ReturnsValidationProblem()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "crest-change-nofile", "No File Change Club", cancellationToken);

        using var form = new MultipartFormDataContent();
        form.Add(new StringContent("ignored"), "name");
        using var response = await adminClient.PostAsync(
            ClubCrestEndpoints.ChangeCrestUrl(admin.Club.ClubId), form, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        problem.Errors.ShouldNotBeNull();
        problem.Errors.ShouldContainKey("crest");
    }

    /// <summary>
    /// Verifies a non-admin club member cannot change the crest (403).
    /// </summary>
    [Fact]
    public async Task ChangeCrest_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "crest-member-admin", "Member Crest Club", cancellationToken);
        await RegisterUserAsync(memberClient, "crest-member", "Member", "Crest", admin.Club.ClubId, cancellationToken);

        using var content = CreateCrestContent(SeedingHelpers.CreateJpegBytes());
        using var response = await memberClient.PostAsync(
            ClubCrestEndpoints.ChangeCrestUrl(admin.Club.ClubId), content, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies an admin of another club cannot change this club's crest (403 at the service
    /// boundary, after the global ClubAdmin role policy passes).
    /// </summary>
    [Fact]
    public async Task ChangeCrest_ReturnsForbidden_ForCrossClubAdmin()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var clubAClient = fixture.CreateNovaHttpClient();
        using var clubBClient = fixture.CreateNovaHttpClient();

        var clubA = await RegisterClubAdminAsync(clubAClient, "crest-xclub-a", "Cross Crest A", cancellationToken);
        _ = await RegisterClubAdminAsync(clubBClient, "crest-xclub-b", "Cross Crest B", cancellationToken);

        using var content = CreateCrestContent(SeedingHelpers.CreateJpegBytes());
        using var response = await clubBClient.PostAsync(
            ClubCrestEndpoints.ChangeCrestUrl(clubA.Club.ClubId), content, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies a club admin can remove the crest (204): the row and all blobs disappear and
    /// the GET endpoint returns 404 afterwards.
    /// </summary>
    [Fact]
    public async Task RemoveCrest_ReturnsNoContent_DeletesRowAndBlobs()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "crest-remove", "Crest Remove Club", cancellationToken);
        var blobNames = await GetCrestBlobNamesAsync(admin.Club.ClubId, cancellationToken);

        using (var response = await adminClient.DeleteAsync(
            ClubCrestEndpoints.RemoveCrestUrl(admin.Club.ClubId), cancellationToken))
        {
            response.StatusCode.ShouldBe(HttpStatusCode.NoContent);
            response.Headers.TryGetValues("Set-Cookie", out var cookies).ShouldBeTrue(
                "the remove must reissue the acting admin's cookie so HasClubCrest disappears immediately");
            cookies.ShouldContain(cookie => cookie.StartsWith(".AspNetCore.Identity.Application", StringComparison.Ordinal));
        }

        await using (var db = fixture.CreateAdminContext())
        {
            var crestCount = await db.ClubCrests.CountAsync(candidate => candidate.ClubId == admin.Club.ClubId, cancellationToken);
            crestCount.ShouldBe(0, "the crest row must be deleted");
        }

        foreach (var blobName in blobNames)
        {
            (await fixture.ClubCrestsContainer.GetBlobClient(blobName).ExistsAsync(cancellationToken))
                .Value.ShouldBeFalse($"the blob '{blobName}' must be deleted after the remove");
        }

        using (var after = await adminClient.GetAsync(
            ClubCrestEndpoints.GetCrestUrl(admin.Club.ClubId, ProfilePhotoSize.Medium), cancellationToken))
        {
            after.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }
    }

    /// <summary>
    /// Verifies a non-admin club member cannot remove the crest (403).
    /// </summary>
    [Fact]
    public async Task RemoveCrest_ReturnsForbidden_ForClubMember()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();
        using var memberClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "crest-rm-member-admin", "Remove Member Club", cancellationToken);
        await RegisterUserAsync(memberClient, "crest-rm-member", "Member", "Remover", admin.Club.ClubId, cancellationToken);

        using var response = await memberClient.DeleteAsync(
            ClubCrestEndpoints.RemoveCrestUrl(admin.Club.ClubId), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Forbidden);
    }

    /// <summary>
    /// Verifies removing a crest that does not exist is a non-disclosing 404.
    /// </summary>
    [Fact]
    public async Task RemoveCrest_ReturnsNotFound_WhenClubHasNoCrest()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var adminClient = fixture.CreateNovaHttpClient();

        var admin = await RegisterClubAdminAsync(adminClient, "crest-rm-missing", "Missing Crest Club", cancellationToken);
        using (await adminClient.DeleteAsync(
            ClubCrestEndpoints.RemoveCrestUrl(admin.Club.ClubId), cancellationToken))
        {
            // First remove deletes the seeded crest.
        }

        using var response = await adminClient.DeleteAsync(
            ClubCrestEndpoints.RemoveCrestUrl(admin.Club.ClubId), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        var problem = await response.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    // ── Helpers ─────────────────────────────────────────────────────────────────

    private async Task<(ClubDto Club, long UserId)> RegisterClubAdminAsync(
        HttpClient client,
        string emailPrefix,
        string clubName,
        CancellationToken cancellationToken)
    {
        var (email, userId) = await RegisterUserAsync(client, emailPrefix, "Club", "Admin", clubId: null, cancellationToken);

        using var response = await client.PostAsync(
            ClubEndpoints.Create,
            SeedingHelpers.CreateClubMultipartContent($"{clubName} {Guid.CreateVersion7():N}", "Austin", "TX"),
            cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.Created);
        var club = await response.Content.ReadFromJsonAsync<ClubDto>(cancellationToken);
        club.ShouldNotBeNull();

        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
        return (club, userId);
    }

    private async Task<(string Email, long UserId)> RegisterUserAsync(
        HttpClient client,
        string emailPrefix,
        string firstName,
        string lastName,
        long? clubId,
        CancellationToken cancellationToken)
    {
        var email = SeedingHelpers.UniqueEmail(emailPrefix);
        await IdentityHttpClientHelper.RegisterUserWithCompletedProfilePhotoAsync(client, email, Password, cancellationToken);

        await using var db = fixture.CreateAdminContext();
        var user = await db.Users.SingleAsync(candidate => candidate.NormalizedEmail == email.ToUpperInvariant(), cancellationToken);
        user.FirstName = firstName;
        user.LastName = lastName;
        user.ClubId = clubId;
        await db.SaveChangesAsync(cancellationToken);

        await SeedingHelpers.RefreshClubMembershipCookieAsync(client, cancellationToken);
        return (email, user.Id);
    }

    /// <summary>
    /// Reads the four blob names currently referenced by a club's crest row.
    /// </summary>
    /// <param name="clubId">The club whose crest blob names to read.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The four blob names in original/small/medium/large order.</returns>
    private async Task<List<string>> GetCrestBlobNamesAsync(long clubId, CancellationToken cancellationToken)
    {
        await using var db = fixture.CreateAdminContext();
        var crest = await db.ClubCrests.SingleAsync(candidate => candidate.ClubId == clubId, cancellationToken);
        return [crest.OriginalBlobName, crest.SmallBlobName!, crest.MediumBlobName!, crest.LargeBlobName!];
    }

    /// <summary>
    /// Builds the multipart content for the crest-change endpoint's <c>crest</c> field.
    /// </summary>
    /// <param name="bytes">The image bytes.</param>
    /// <returns>The multipart content.</returns>
    private static MultipartFormDataContent CreateCrestContent(byte[] bytes)
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("image/jpeg");
        return new MultipartFormDataContent { { fileContent, "crest", "crest.jpg" } };
    }
}
