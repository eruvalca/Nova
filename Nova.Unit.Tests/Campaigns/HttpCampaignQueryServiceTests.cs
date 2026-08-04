using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.Shared.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class HttpCampaignQueryServiceTests
{
    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }
        private readonly HttpResponseMessage _response = response;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }

    [Fact]
    public async Task GetCampaignListAsync_RequestsSharedRoute_AndRespectsQuery()
    {
        var sample = new CampaignListResult { TotalCount = 0, Seasons = new List<CampaignSeasonGroup>() };
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(sample) };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var svc = new HttpCampaignQueryService(http);
        var input = new GetCampaignListInput { Status = "active", Limit = 10 };
        var result = await svc.GetCampaignListAsync(input, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.GetCampaignList);
        handler.LastRequest.RequestUri!.Query.ShouldContain("status=active");
        handler.LastRequest.RequestUri!.Query.ShouldContain("limit=10");
    }

    [Fact]
    public async Task GetCampaignListAsync_ReturnsServerError_ForEmptySuccessPayload()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(string.Empty) };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignQueryService(http).GetCampaignListAsync(new GetCampaignListInput(), TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetCreationSetupAsync_UsesSetupRoute_AndValidatesPayload()
    {
        var sample = new CampaignCreationSetupResult { TotalSeasonCount = 0, Seasons = new List<CampaignSeasonChoice>(), ActivePlayerCount = 0, ActiveTeamCount = 0 };
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(sample) };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var svc = new HttpCampaignQueryService(http);
        var result = await svc.GetCreationSetupAsync(TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.GetCreationSetup);
    }

    [Fact]
    public async Task GetCampaignListAsync_ReturnsProblem_FromProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new { title = "Bad", status = 400, detail = "bad" })
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignQueryService(http).GetCampaignListAsync(new GetCampaignListInput(), TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.BadRequest);
    }

    [Fact]
    public async Task GetCampaignListAsync_ReturnsServerError_ForMalformedJson()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{not-json") };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignQueryService(http).GetCampaignListAsync(new GetCampaignListInput(), TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }
}
