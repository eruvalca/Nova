using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services.Clubs;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

/// <summary>
/// Tests for <see cref="HttpClubCrestService"/>, the WebAssembly client implementation of
/// <see cref="IClubCrestService"/>: the multipart change payload, result mapping, and the
/// 404-to-NotFound translation on remove.
/// </summary>
public class HttpClubCrestServiceTests
{
    /// <summary>
    /// ChangeClubCrestAsync posts a multipart form with a single <c>crest</c> file part and
    /// returns success on a 204 response.
    /// </summary>
    [Fact]
    public async Task ChangeClubCrestAsync_ReturnsSuccess_OnNoContent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubCrestService(http).ChangeClubCrestAsync(
            42,
            new ClubCrestUpload(TestImages.CreateJpeg(), "image/jpeg"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/clubs/42/crest");
        handler.LastRequest!.Content.ShouldBeOfType<MultipartFormDataContent>();
        handler.LastMultipartPartNames.ShouldBe(["crest"]);
    }

    /// <summary>
    /// ChangeClubCrestAsync maps a validation problem body to a validation ServiceProblem.
    /// </summary>
    [Fact]
    public async Task ChangeClubCrestAsync_ReturnsValidationProblem_OnBadRequest()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { errors = new Dictionary<string, string[]> { ["crest"] = ["A club crest is required."] } })
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubCrestService(http).ChangeClubCrestAsync(
            42,
            new ClubCrestUpload([], "image/jpeg"),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors.ShouldContainKey("crest");
    }

    /// <summary>
    /// ChangeClubCrestAsync surfaces a 403 response as a Forbidden problem.
    /// </summary>
    [Fact]
    public async Task ChangeClubCrestAsync_ReturnsForbidden_OnForbidden()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubCrestService(http).ChangeClubCrestAsync(
            42,
            new ClubCrestUpload(TestImages.CreateJpeg(), "image/jpeg"),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// RemoveClubCrestAsync sends a DELETE to the crest route and returns success on 204.
    /// </summary>
    [Fact]
    public async Task RemoveClubCrestAsync_ReturnsSuccess_OnNoContent()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubCrestService(http).RemoveClubCrestAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Delete);
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/clubs/42/crest");
    }

    /// <summary>
    /// RemoveClubCrestAsync maps a 404 response to a NotFound problem (the crest is gone).
    /// </summary>
    [Fact]
    public async Task RemoveClubCrestAsync_ReturnsNotFound_OnNotFound()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound);
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubCrestService(http).RemoveClubCrestAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// RemoveClubCrestAsync maps a 403 response to a Forbidden problem.
    /// </summary>
    [Fact]
    public async Task RemoveClubCrestAsync_ReturnsForbidden_OnForbidden()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden);
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubCrestService(http).RemoveClubCrestAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        public IReadOnlyList<string>? LastMultipartPartNames { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            LastMultipartPartNames = request.Content is MultipartFormDataContent multipart
                ? multipart.Select(part => part.Headers.ContentDisposition?.Name?.Trim('"')).ToArray()
                : null;
            return Task.FromResult(response);
        }
    }
}
