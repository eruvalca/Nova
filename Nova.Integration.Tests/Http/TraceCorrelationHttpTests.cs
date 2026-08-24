using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Nova.Integration.Tests.Data;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Features.Photos;
using Shouldly;

namespace Nova.Integration.Tests.Http;

/// <summary>
/// End-to-end W3C trace-continuation coverage: every ProblemDetails producer must return a
/// <c>traceId</c> extension equal to the trace id sent in the request's <c>traceparent</c> header.
/// Covers the three distinct producers: service problems (<c>ToHttpResult</c>), framework 400s
/// (<c>BadHttpRequestExceptionHandler</c>), and status-code pages (<c>UseStatusCodePages</c>).
/// </summary>
/// <param name="fixture">The shared AppHost fixture.</param>
[Collection(NovaAppHostCollection.Name)]
public sealed class TraceCorrelationHttpTests(NovaAppHostFixture fixture)
{
    private const string Password = "Test#Passw0rd!";

    /// <summary>
    /// Verifies a service problem converted by <c>ToHttpResult</c> (an authenticated POST whose
    /// structurally invalid payload is rejected as a 400 <c>ValidationProblem</c>) carries a
    /// <c>traceId</c> equal to the client-sent <c>traceparent</c> trace id.
    /// </summary>
    [Fact]
    public async Task ServiceProblem_ReturnsTraceIdMatchingSentTraceparent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserAsync(
            client,
            UniqueEmail("trace-service-problem"),
            Password,
            cancellationToken);

        var (traceId, traceparent) = CreateTraceContext();
        using var content = CreateUploadContent("not an image"u8.ToArray(), "image/jpeg");
        using var request = new HttpRequestMessage(HttpMethod.Post, PhotoEndpoints.Upload)
        {
            Content = content
        };
        request.Headers.TryAddWithoutValidation("traceparent", traceparent);

        using var response = await client.SendAsync(request, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        (await ReadTraceIdAsync(response, cancellationToken)).ShouldBe(traceId);
    }

    /// <summary>
    /// Verifies a framework-generated 400 from <c>BadHttpRequestExceptionHandler</c> (a malformed
    /// multipart form body to a body-taking endpoint) carries a <c>traceId</c> equal to the
    /// client-sent <c>traceparent</c> trace id.
    /// </summary>
    [Fact]
    public async Task MalformedForm_ReturnsTraceIdMatchingSentTraceparent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();
        await IdentityHttpClientHelper.RegisterUserAsync(
            client,
            UniqueEmail("trace-malformed"),
            Password,
            cancellationToken);

        var (traceId, traceparent) = CreateTraceContext();
        using var request = new HttpRequestMessage(HttpMethod.Post, ClubEndpoints.Create)
        {
            // Club creation is a multipart-form endpoint; a malformed multipart body (no
            // boundary) makes the form reader throw BadHttpRequestException (400) instead of
            // the 415 a JSON body would produce. Keep the 'framework 400' coverage intact.
            Content = new StringContent("not a multipart body", Encoding.UTF8, "multipart/form-data")
        };
        request.Headers.TryAddWithoutValidation("traceparent", traceparent);

        using var response = await client.SendAsync(request, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType?.MediaType.ShouldBe("application/problem+json");
        (await ReadTraceIdAsync(response, cancellationToken)).ShouldBe(traceId);
    }

    /// <summary>
    /// Verifies a status-code page produced by <c>UseStatusCodePages</c> (an unauthenticated
    /// request to an <c>/api</c> route) carries a <c>traceId</c> equal to the client-sent
    /// <c>traceparent</c> trace id.
    /// </summary>
    [Fact]
    public async Task StatusCodePage_ReturnsTraceIdMatchingSentTraceparent()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        using var client = fixture.CreateNovaHttpClient();

        var (traceId, traceparent) = CreateTraceContext();
        using var request = new HttpRequestMessage(HttpMethod.Get, CampaignEndpoints.GetCampaignList);
        request.Headers.TryAddWithoutValidation("traceparent", traceparent);

        using var response = await client.SendAsync(request, cancellationToken);

        response.StatusCode.ShouldBe(HttpStatusCode.Unauthorized);
        (await ReadTraceIdAsync(response, cancellationToken)).ShouldBe(traceId);
    }

    /// <summary>
    /// Generates a random W3C <c>traceparent</c> value and the trace id it carries.
    /// </summary>
    /// <returns>The sent trace id and the corresponding <c>traceparent</c> header value.</returns>
    private static (string TraceId, string Traceparent) CreateTraceContext()
    {
        var traceId = ActivityTraceId.CreateRandom().ToHexString();
        var spanId = ActivitySpanId.CreateRandom().ToHexString();
        return (traceId, $"00-{traceId}-{spanId}-01");
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

    /// <summary>
    /// Builds multipart content for the profile-photo upload endpoint.
    /// </summary>
    /// <param name="bytes">The uploaded file bytes.</param>
    /// <param name="contentType">The declared media type.</param>
    /// <returns>The multipart payload for the <c>file</c> form field.</returns>
    private static MultipartFormDataContent CreateUploadContent(byte[] bytes, string contentType)
    {
        var fileContent = new ByteArrayContent(bytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        return new MultipartFormDataContent { { fileContent, "file", "photo.jpg" } };
    }

    /// <summary>
    /// Generates a unique email address so each test seeds its own user in the shared database.
    /// </summary>
    /// <param name="prefix">A stable prefix included in the address.</param>
    /// <returns>A unique email address.</returns>
    private static string UniqueEmail(string prefix) => $"{prefix}-{Guid.CreateVersion7():N}@example.com";
}
