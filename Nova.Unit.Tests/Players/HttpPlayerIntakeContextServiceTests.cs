using System.Net;
using System.Text;
using Nova.Client.Services.Players;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Tests for <see cref="HttpPlayerIntakeContextService"/> success-payload validation and routing.
/// </summary>
public sealed class HttpPlayerIntakeContextServiceTests
{
    /// <summary>
    /// A test HTTP handler that captures the outgoing request.
    /// </summary>
    /// <param name="response">The response to return.</param>
    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("{\"campaignId\":7,\"campaignName\":\"Summer Tryouts\"}", true)]
    [InlineData("{\"campaignId\":9223372036854775807,\"campaignName\":\"Wrap\"}", true)]
    [InlineData("{\"campaignId\":null,\"campaignName\":null}", true)]
    [InlineData("{\"campaignId\":7}", false)]
    [InlineData("{\"campaignName\":\"Summer Tryouts\"}", false)]
    [InlineData("{\"campaignId\":0,\"campaignName\":\"Summer Tryouts\"}", false)]
    [InlineData("{\"campaignId\":-1,\"campaignName\":\"Summer Tryouts\"}", false)]
    [InlineData("{\"campaignId\":7,\"campaignName\":\"\"}", false)]
    [InlineData("{\"campaignId\":7,\"campaignName\":\"   \"}", false)]
    [InlineData("{\"campaignId\":null,\"campaignName\":\"Summer Tryouts\"}", false)]
    [InlineData("{\"campaignId\":7,\"campaignName\":null}", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    [InlineData("{", false)]
    public async Task RequiresAPairedCompleteSuccessPayloadAsync(string body, bool valid)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerIntakeContextService(http).GetPlayerIntakeContextAsync(
            new() { ClubId = 42 },
            TestContext.Current.CancellationToken);

        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/clubs/42/players/intake-context");
        result.IsSuccess.ShouldBe(valid);
        if (!valid)
        {
            result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
            result.Problem.Detail.ShouldBe("The server returned an invalid player intake context.");
        }
    }

    [Fact]
    public async Task ReturnsTheNamedActiveCampaignAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"campaignId\":7,\"campaignName\":\"Summer Tryouts\"}", Encoding.UTF8, "application/json")
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerIntakeContextService(http).GetPlayerIntakeContextAsync(
            new() { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CampaignId.ShouldBe(7);
        result.Value.CampaignName.ShouldBe("Summer Tryouts");
    }

    [Fact]
    public async Task RejectsInvalidInputWithoutSendingAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerIntakeContextService(http).GetPlayerIntakeContextAsync(
            new() { ClubId = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        handler.LastRequest.ShouldBeNull();
    }

    [Fact]
    public async Task PreservesForbiddenProblemFromTheServerAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent("{\"title\":\"Forbidden\",\"status\":403,\"detail\":\"Nope.\"}", Encoding.UTF8, "application/problem+json")
        };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerIntakeContextService(http).GetPlayerIntakeContextAsync(
            new() { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
        result.Problem.Detail.ShouldBe("Nope.");
    }
}
