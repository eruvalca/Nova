using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services.Clubs;
using Nova.Shared.Features.Clubs;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Clubs;

public sealed class HttpClubIdentityQueryServiceTests
{
    [Fact]
    public async Task GetCurrentAsync_UsesCanonicalRoute_AndReturnsValidatedIdentity()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new ClubIdentityResult
            {
                ClubId = 42,
                Name = "Harbor Volleyball",
                City = "Erie",
                State = "PA",
                HasCrest = true
            })
        };
        var handler = new CaptureHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubIdentityQueryService(http).GetCurrentAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.HasCrest.ShouldBeTrue();
        handler.Request!.Method.ShouldBe(HttpMethod.Get);
        handler.Request.RequestUri!.AbsolutePath.ShouldBe(ClubEndpoints.GetCurrent);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{not-json")]
    [InlineData("{\"clubId\":0,\"name\":\"Club\",\"city\":\"Erie\",\"state\":\"PA\",\"hasCrest\":false}")]
    [InlineData("{\"clubId\":1,\"name\":\"   \",\"city\":\"Erie\",\"state\":\"PA\",\"hasCrest\":false}")]
    [InlineData("{\"clubId\":1,\"name\":\"Club\",\"city\":\"Erie\",\"state\":\"PA\",\"hasCrest\":\"yes\"}")]
    public async Task GetCurrentAsync_ReturnsServerError_ForMalformedOrInvalidSuccessPayload(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var http = new HttpClient(new CaptureHandler(response)) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubIdentityQueryService(http).GetCurrentAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    public static TheoryData<string> OverlengthPayloads =>
        new()
        {
            PayloadWith("name", new string('a', 201)),
            PayloadWith("city", new string('a', 101)),
            PayloadWith("state", new string('a', 101))
        };

    [Theory]
    [MemberData(nameof(OverlengthPayloads))]
    public async Task GetCurrentAsync_ReturnsServerError_ForOverlengthSuccessPayload(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var http = new HttpClient(new CaptureHandler(response)) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpClubIdentityQueryService(http).GetCurrentAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    // Mirrors the shared club bounds declared on CreateClubInput (200/100/100).
    private static string PayloadWith(string overlongProperty, string longValue)
    {
        var (name, city, state) = overlongProperty switch
        {
            "name" => (longValue, "Erie", "PA"),
            "city" => ("Club", longValue, "PA"),
            _ => ("Club", "Erie", longValue)
        };
        return $$"""{"clubId":1,"name":"{{name}}","city":"{{city}}","state":"{{state}}","hasCrest":false}""";
    }

    private sealed class CaptureHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(response);
        }
    }
}
