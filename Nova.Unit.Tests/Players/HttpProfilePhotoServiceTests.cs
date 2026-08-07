using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.Shared.Features.Photos;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Tests required success-response handling in <see cref="HttpProfilePhotoService"/>.
/// </summary>
public sealed class HttpProfilePhotoServiceTests
{
    /// <summary>
    /// Verifies a valid photo response may omit its content type.
    /// </summary>
    [Fact]
    public async Task GetCurrentUserPhotoAsync_ReturnsPhotoInfo_WhenContentTypeIsNull()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ProfilePhotoInfo(42, null))
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpProfilePhotoService(http).GetCurrentUserPhotoAsync(
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.NovaUserId.ShouldBe(42);
        result.Value.ContentType.ShouldBeNull();
    }

    /// <summary>
    /// Verifies invalid successful photo-response bodies become server errors.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task GetCurrentUserPhotoAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpProfilePhotoService(http).GetCurrentUserPhotoAsync(
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a photo response with a non-positive user identifier is rejected.
    /// </summary>
    [Fact]
    public async Task GetCurrentUserPhotoAsync_ReturnsServerError_WhenPhotoInvariantIsInvalid()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ProfilePhotoInfo(0, "image/png"))
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpProfilePhotoService(http).GetCurrentUserPhotoAsync(
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(response);
    }
}
