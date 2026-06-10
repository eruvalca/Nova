using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Nova.Integration.Tests.Data;
using Nova.Shared.Photos;
using Nova.Shared.Results;
using Shouldly;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats.Jpeg;
using SixLabors.ImageSharp.PixelFormats;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end HTTP tests for the profile photo endpoints against the real running app
/// (Aspire AppHost: Postgres + Azurite + the Nova web app). Covers route reachability,
/// the full register → upload → fetch → cookie-refresh onboarding flow, ProblemDetails
/// error responses with trace ids, and owner-only access to the original photo.
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public class ProfilePhotoHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    [Fact]
    public async Task UploadEndpoint_Returns401NotARedirect_WhenUnauthenticated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        using var content = CreateUploadContent(CreateJpeg(64, 64), "image/jpeg");
        using var response = await client.PostAsync(PhotoEndpoints.Upload, content, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PhotoRoute_IsRegisteredAtApiUsersPath()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        // 401 (auth challenge) proves the route is registered at /api/users/{id}/photo;
        // a 404 here would mean the endpoint regressed back inside the /api/account group.
        using var response = await client.GetAsync(
            PhotoEndpoints.GetPhotoUrl(long.MaxValue, ProfilePhotoSize.Medium), cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task RegisterUploadFetchComplete_FullOnboardingFlow_Succeeds()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserAsync(client, UniqueEmail(), Password, cancellationToken);

        // Before upload: status is a 404 ProblemDetails carrying a trace id.
        using (var statusBefore = await client.GetAsync(PhotoEndpoints.Status, cancellationToken))
        {
            statusBefore.StatusCode.ShouldBe(HttpStatusCode.NotFound);
            (await ReadTraceIdAsync(statusBefore, cancellationToken)).ShouldNotBeNullOrEmpty();
        }

        // The photo gate bounces a photo-less user away from the home page.
        using (var gated = await client.GetAsync("/", cancellationToken))
        {
            gated.StatusCode.ShouldBe(HttpStatusCode.Found);
            gated.Headers.Location.ShouldNotBeNull();
            gated.Headers.Location.OriginalString.ShouldStartWith("/Account/ProfilePhoto");
        }

        // Upload a photo.
        using (var content = CreateUploadContent(CreateJpeg(300, 200), "image/jpeg"))
        using (var upload = await client.PostAsync(PhotoEndpoints.Upload, content, cancellationToken))
        {
            upload.StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        // Status now reports the photo.
        long userId;
        using (var statusAfter = await client.GetAsync(PhotoEndpoints.Status, cancellationToken))
        {
            statusAfter.StatusCode.ShouldBe(HttpStatusCode.OK);
            var info = await statusAfter.Content.ReadFromJsonAsync<ProfilePhotoInfo>(cancellationToken);
            info.ShouldNotBeNull();
            info.ContentType.ShouldBe("image/jpeg");
            userId = info.NovaUserId;
        }

        // The photo is served from /api/users/{id}/photo with an ETag...
        string etag;
        using (var photo = await client.GetAsync(
            PhotoEndpoints.GetPhotoUrl(userId, ProfilePhotoSize.Medium), cancellationToken))
        {
            photo.StatusCode.ShouldBe(HttpStatusCode.OK);
            photo.Content.Headers.ContentType?.MediaType.ShouldBe("image/webp");
            photo.Headers.ETag.ShouldNotBeNull();
            etag = photo.Headers.ETag.Tag;
        }

        // ...and a conditional request returns 304.
        using (var conditional = new HttpRequestMessage(
            HttpMethod.Get, PhotoEndpoints.GetPhotoUrl(userId, ProfilePhotoSize.Medium)))
        {
            conditional.Headers.IfNoneMatch.Add(new EntityTagHeaderValue(etag));
            using var notModified = await client.SendAsync(conditional, cancellationToken);
            notModified.StatusCode.ShouldBe(HttpStatusCode.NotModified);
        }

        // The cookie-refresh hop is reachable and redirects to the return URL.
        using (var complete = await client.GetAsync($"{PhotoEndpoints.Complete}?returnUrl=/", cancellationToken))
        {
            complete.StatusCode.ShouldBe(HttpStatusCode.Found,
                "the complete endpoint must be reachable at /Account/ProfilePhoto/Complete");
        }

        // With the refreshed cookie carrying the HasProfilePhoto claim, the gate lets the user in.
        using (var home = await client.GetAsync("/", cancellationToken))
        {
            home.StatusCode.ShouldBe(HttpStatusCode.OK, "the photo gate should no longer redirect");
        }
    }

    [Fact]
    public async Task Upload_Returns400ValidationProblem_ForNonImageContent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserAsync(client, UniqueEmail(), Password, cancellationToken);

        using var content = CreateUploadContent("this is not an image"u8.ToArray(), "image/jpeg");
        using var response = await client.PostAsync(PhotoEndpoints.Upload, content, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);

        // The WASM client's parser must reconstruct the structured validation problem
        // from a single body read (regression coverage for the double-read bug).
        var problem = await response.ToServiceProblemAsync(cancellationToken);
        problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        problem.Errors.ShouldNotBeNull();
        problem.Errors.ShouldContainKey("photo");
    }

    [Fact]
    public async Task Upload_Returns400_ForOversizedDimensions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserAsync(client, UniqueEmail(), Password, cancellationToken);

        using var content = CreateUploadContent(CreateJpeg(8193, 4), "image/jpeg");
        using var response = await client.PostAsync(PhotoEndpoints.Upload, content, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        var problem = await response.ToServiceProblemAsync(cancellationToken);
        problem.Detail.ShouldNotBeNull();
        problem.Detail.ShouldContain("dimensions exceed");
    }

    [Fact]
    public async Task OriginalSize_IsServedToOwnerOnly()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        // User A registers and uploads a photo.
        using var ownerClient = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserAsync(ownerClient, UniqueEmail(), Password, cancellationToken);
        using (var content = CreateUploadContent(CreateJpeg(300, 200), "image/jpeg"))
        {
            (await ownerClient.PostAsync(PhotoEndpoints.Upload, content, cancellationToken))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        long ownerId;
        using (var status = await ownerClient.GetAsync(PhotoEndpoints.Status, cancellationToken))
        {
            ownerId = (await status.Content.ReadFromJsonAsync<ProfilePhotoInfo>(cancellationToken))!.NovaUserId;
        }

        // User B (a different authenticated user with no shared club) cannot see A's photo at
        // all: the NovaUserPhotos query filter scopes visibility to the same club or the owner.
        using var otherClient = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserAsync(otherClient, UniqueEmail(), Password, cancellationToken);

        using (var variant = await otherClient.GetAsync(
            PhotoEndpoints.GetPhotoUrl(ownerId, ProfilePhotoSize.Medium), cancellationToken))
        {
            variant.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // The original is rejected for non-owners before any lookup (404, not 403, to avoid
        // leaking existence) — this holds even for same-club users who can see the variants.
        using (var foreignOriginal = await otherClient.GetAsync(
            PhotoEndpoints.GetPhotoUrl(ownerId, ProfilePhotoSize.Original), cancellationToken))
        {
            foreignOriginal.StatusCode.ShouldBe(HttpStatusCode.NotFound);
        }

        // The owner can fetch the original, and it carries no EXIF metadata.
        using var ownOriginal = await ownerClient.GetAsync(
            PhotoEndpoints.GetPhotoUrl(ownerId, ProfilePhotoSize.Original), cancellationToken);
        ownOriginal.StatusCode.ShouldBe(HttpStatusCode.OK);
        var bytes = await ownOriginal.Content.ReadAsByteArrayAsync(cancellationToken);
        using var image = Image.Load(bytes);
        image.Metadata.ExifProfile.ShouldBeNull("the served original must not carry EXIF metadata");
    }

    [Fact]
    public async Task UndefinedSizeValue_FallsBackToMediumVariant_NotOriginal()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserAsync(client, UniqueEmail(), Password, cancellationToken);

        using (var content = CreateUploadContent(CreateJpeg(300, 200), "image/jpeg"))
        {
            (await client.PostAsync(PhotoEndpoints.Upload, content, cancellationToken))
                .StatusCode.ShouldBe(HttpStatusCode.NoContent);
        }

        long userId;
        using (var status = await client.GetAsync(PhotoEndpoints.Status, cancellationToken))
        {
            userId = (await status.Content.ReadFromJsonAsync<ProfilePhotoInfo>(cancellationToken))!.NovaUserId;
        }

        // Enum.TryParse accepts arbitrary numeric strings; an undefined value like 99 must not
        // bypass the owner-only check and resolve to the original blob — it falls back to the
        // medium WebP variant (the original here is image/jpeg).
        using var response = await client.GetAsync($"/api/users/{userId}/photo?size=99", cancellationToken);
        response.StatusCode.ShouldBe(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("image/webp");
    }

    /// <summary>
    /// Builds a unique email address so each test seeds its own user in the shared database.
    /// </summary>
    /// <returns>The unique email.</returns>
    private static string UniqueEmail() => $"photo-e2e-{Guid.CreateVersion7():N}@example.com";

    /// <summary>
    /// Builds the multipart form content for the upload endpoint's <c>file</c> parameter.
    /// </summary>
    /// <param name="bytes">The file content.</param>
    /// <param name="contentType">The declared content type.</param>
    /// <returns>The multipart content.</returns>
    private static MultipartFormDataContent CreateUploadContent(byte[] bytes, string contentType)
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { fileContent, "file", "photo.jpg" } };
    }

    /// <summary>
    /// Creates an in-memory JPEG of the requested dimensions.
    /// </summary>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <returns>The encoded JPEG bytes.</returns>
    private static byte[] CreateJpeg(int width, int height)
    {
        using var image = new Image<Rgba32>(width, height, new Rgba32(120, 180, 240));
        using var stream = new MemoryStream();
        image.Save(stream, new JpegEncoder());
        return stream.ToArray();
    }

    /// <summary>
    /// Reads the <c>traceId</c> extension from a ProblemDetails response body.
    /// </summary>
    /// <param name="response">The problem response.</param>
    /// <param name="cancellationToken">The test cancellation token.</param>
    /// <returns>The trace id, or <see langword="null"/> when absent.</returns>
    private static async Task<string?> ReadTraceIdAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        return document.RootElement.TryGetProperty("traceId", out var traceId) ? traceId.GetString() : null;
    }
}
