using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Nodes;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies the WebAssembly campaign query HTTP boundary and payload validation.
/// </summary>
public sealed class HttpCampaignQueryServiceTests
{
    /// <summary>
    /// Captures the request and returns a configured response.
    /// </summary>
    /// <param name="response">The response returned for every request.</param>
    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        /// <summary>Gets the most recently captured request.</summary>
        public HttpRequestMessage? LastRequest { get; private set; }
        /// <summary>Stores the configured response.</summary>
        private readonly HttpResponseMessage _response = response;
        /// <summary>Sends the configured response while recording the request.</summary>
        /// <param name="request">The outgoing request.</param>
        /// <param name="cancellationToken">The cancellation token.</param>
        /// <returns>The configured response.</returns>
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(_response);
        }
    }

    /// <summary>Verifies list requests use the shared route and query values.</summary>
    [Fact]
    public async Task GetCampaignListAsync_RequestsSharedRoute_AndRespectsQuery()
    {
        var sample = new CampaignListResult { TotalCount = 0, Page = 2, Limit = 10, Seasons = new List<CampaignSeasonGroup>() };
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(sample) };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var svc = new HttpCampaignQueryService(http);
        var input = new GetCampaignListInput { Status = "active", Limit = 10, Page = 2 };
        var result = await svc.GetCampaignListAsync(input, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.GetCampaignList);
        handler.LastRequest.RequestUri!.Query.ShouldContain("status=active");
        handler.LastRequest.RequestUri!.Query.ShouldContain("limit=10");
        handler.LastRequest.RequestUri!.Query.ShouldContain("page=2");
    }

    /// <summary>Verifies an empty successful list body maps to a server error.</summary>
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

    /// <summary>Verifies setup requests use the shared route and accept valid payloads.</summary>
    [Fact]
    public async Task GetCreationSetupAsync_UsesSetupRoute_AndValidatesPayload()
    {
        var sample = new CampaignCreationSetupResult { CurrentSeason = null, ActivePlayerCount = 0, ActiveTeamCount = 0 };
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

    /// <summary>Verifies non-success ProblemDetails responses retain their problem kind.</summary>
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

    /// <summary>Verifies malformed successful JSON maps to a server error.</summary>
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

    /// <summary>Verifies invalid input is rejected before an HTTP request is sent.</summary>
    [Fact]
    public async Task GetCampaignListAsync_ReturnsValidationProblem_ForInvalidInput()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignQueryService(http).GetCampaignListAsync(
            new GetCampaignListInput { Status = string.Empty, Limit = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        handler.LastRequest.ShouldBeNull();
    }

    /// <summary>Verifies an invalid current-season payload is rejected.</summary>
    [Fact]
    public async Task GetCreationSetupAsync_ReturnsServerError_ForInvalidCurrentSeason()
    {
        var sample = new CampaignCreationSetupResult
        {
            CurrentSeason = new CampaignSeasonChoice
            {
                SeasonId = 0,
                Name = "Invalid",
                StartDate = new DateOnly(2026, 1, 1)
            },
            ActivePlayerCount = 0,
            ActiveTeamCount = 0
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(sample) };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignQueryService(http).GetCreationSetupAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies opening readiness uses the shared route and accepts a populated valid payload.</summary>
    [Fact]
    public async Task GetOpeningReadinessAsync_RequestsSharedRoute_AndAcceptsValidPayload()
    {
        const long campaignId = 42;
        const string payload = """
            {"campaignId":42,"activePlayerCount":3,"activeTeamCount":0,"canOpen":true,
            "blockers":[],"warnings":[0],"blockingCampaign":null}
            """;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignQueryService(http)
            .GetOpeningReadinessAsync(campaignId, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ActivePlayerCount.ShouldBe(3);
        result.Value.Warnings.ShouldBe([CampaignOpeningWarning.NoActiveTeams]);
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.GetOpeningReadinessUrl(campaignId));
    }

    /// <summary>Verifies invalid opening-readiness relationships are rejected as protocol errors.</summary>
    /// <param name="payload">The invalid successful JSON payload.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":null,"warnings":[]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":null}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":false,"blockers":[99],"warnings":[]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[99]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":0,"activeTeamCount":1,"canOpen":false,"blockers":[0,0],"warnings":[]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":0,"canOpen":true,"blockers":[],"warnings":[0,0]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":-1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":-1,"canOpen":true,"blockers":[],"warnings":[]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":false,"blockers":[],"warnings":[]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":0,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":0,"canOpen":true,"blockers":[],"warnings":[]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":false,"blockers":[1],"warnings":[],"blockingCampaign":null}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[],"blockingCampaign":{"campaignId":0,"campaignName":"Other"}}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[],"blockingCampaign":{"campaignId":42,"campaignName":"Other"}}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[],"blockingCampaign":{"campaignId":43,"campaignName":" "}}""")]
    [InlineData("""{"campaignId":43,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[],"blockingCampaign":null}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":0,"activeTeamCount":1,"canOpen":true,"blockers":[0],"warnings":[]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":false,"blockers":[0],"warnings":[]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[0]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[],"activeTeams":null}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[],"activeTeams":[null]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[],"activeTeams":[{"teamId":0,"name":"Team"}]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[],"activeTeams":[{"teamId":1,"name":" "}]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":2,"canOpen":true,"blockers":[],"warnings":[],"activeTeams":[{"teamId":1,"name":"A"},{"teamId":1,"name":"B"}]}""")]
    [InlineData("""{"campaignId":42,"activePlayerCount":1,"activeTeamCount":1,"canOpen":true,"blockers":[],"warnings":[],"activeTeams":[{"teamId":1,"name":"A"},{"teamId":2,"name":"B"}]}""")]
    public async Task GetOpeningReadinessAsync_ReturnsServerError_ForInvalidPayload(string payload)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        using var http = new HttpClient(new FakeHttpMessageHandler(response))
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignQueryService(http)
            .GetOpeningReadinessAsync(42, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a populated, structurally valid campaign response is accepted.
    /// </summary>
    [Fact]
    public async Task GetCampaignListAsync_AcceptsPopulatedValidPayload()
    {
        const string payload = """
            {"seasons":[{"seasonId":1,"name":"Season","startDate":"2026-01-01","endDate":null,
            "concurrencyToken":"11111111-1111-1111-1111-111111111111",
            "campaigns":[{"campaignId":2,"name":"Campaign","startDate":"2026-06-01",
            "plannedEndDate":null,"status":0,"participantCount":1,"unresolvedCount":1}]}],"totalCount":1}
            """;

        var result = await GetCampaignListFromJsonAsync(payload);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>Verifies Draft rows are accepted in Active, Draft, Closed lifecycle order.</summary>
    [Fact]
    public async Task GetCampaignListAsync_AcceptsDraftLifecycleOrder()
    {
        const string payload = """
            {"seasons":[{"seasonId":1,"name":"Season","startDate":"2026-01-01",
            "concurrencyToken":"11111111-1111-1111-1111-111111111111","campaigns":[
            {"campaignId":1,"name":"Active","startDate":"2026-06-01","status":0,"participantCount":1,"unresolvedCount":0},
            {"campaignId":2,"name":"Draft","startDate":"2026-06-01","status":2,"participantCount":0,"unresolvedCount":0},
            {"campaignId":3,"name":"Closed","startDate":"2026-06-01","closedAt":"2026-07-01T00:00:00Z","status":1,"participantCount":1,"unresolvedCount":0}
            ]}],"totalCount":3}
            """;

        var result = await GetCampaignListFromJsonAsync(payload);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>Verifies the authoritative current-season group may precede a newer dated historical season.</summary>
    [Fact]
    public async Task GetCampaignListAsync_AcceptsCurrentSeasonBeforeNewerHistory()
    {
        const string payload = """
            {"currentSeasonId":1,"seasons":[
            {"seasonId":1,"name":"Current","startDate":"2026-01-01",
            "concurrencyToken":"11111111-1111-1111-1111-111111111111","campaigns":[
            {"campaignId":1,"name":"Draft","startDate":"2026-06-01","status":2,"participantCount":0,"unresolvedCount":0}]},
            {"seasonId":2,"name":"History","startDate":"2027-01-01",
            "concurrencyToken":"22222222-2222-2222-2222-222222222222","campaigns":[
            {"campaignId":2,"name":"Closed","startDate":"2027-06-01","closedAt":"2027-07-01T00:00:00Z","status":1,"participantCount":0,"unresolvedCount":0}]}
            ],"totalCount":2}
            """;

        var result = await GetCampaignListFromJsonAsync(payload);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Seasons.Select(season => season.SeasonId).ShouldBe([1L, 2L]);
    }

    /// <summary>Verifies an empty season metadata token is rejected as a malformed success payload.</summary>
    [Fact]
    public async Task GetCampaignListAsync_ReturnsServerError_ForEmptySeasonConcurrencyToken()
    {
        const string payload = """
            {"seasons":[{"seasonId":1,"name":"Season","startDate":"2026-01-01","endDate":null,
            "concurrencyToken":"00000000-0000-0000-0000-000000000000","campaigns":[]}],"totalCount":0}
            """;

        var result = await GetCampaignListFromJsonAsync(payload);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies strict list-payload invariants map invalid successful responses to server errors.
    /// </summary>
    /// <param name="payload">The invalid successful JSON payload.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("""{"seasons":null,"totalCount":0}""")]
    [InlineData("""{"seasons":[null],"totalCount":0}""")]
    [InlineData("""{"seasons":[{"seasonId":1,"name":"Season","startDate":"2026-01-01","campaigns":null}],"totalCount":0}""")]
    [InlineData("""{"seasons":[],"totalCount":-1}""")]
    [InlineData("""{"seasons":[],"totalCount":0,"page":0}""")]
    [InlineData("""{"seasons":[],"totalCount":0,"limit":0}""")]
    [InlineData("""{"seasons":[],"totalCount":0,"limit":101}""")]
    [InlineData("""{"seasons":[],"totalCount":0,"currentSeasonId":0}""")]
    [InlineData("""{"seasons":[],"totalCount":0,"draftActivePlayerCount":-1}""")]
    [InlineData("""{"seasons":[{"seasonId":0,"name":"Season","startDate":"2026-01-01","campaigns":[]}],"totalCount":0}""")]
    [InlineData("""{"seasons":[{"seasonId":1,"name":"Season","startDate":"2026-01-02","endDate":"2026-01-01","campaigns":[]}],"totalCount":0}""")]
    [InlineData("""{"seasons":[{"seasonId":1,"name":"Season","startDate":"2026-01-01","campaigns":[{"campaignId":1,"name":"Campaign","startDate":"2026-06-02","plannedEndDate":"2026-06-01","status":0,"participantCount":0,"unresolvedCount":0}]}],"totalCount":1}""")]
    [InlineData("""{"seasons":[{"seasonId":1,"name":"Season","startDate":"2026-01-01","campaigns":[{"campaignId":1,"name":"Campaign","startDate":"2026-06-01","status":0,"participantCount":0,"unresolvedCount":1}]}],"totalCount":1}""")]
    [InlineData("""{"seasons":[{"seasonId":1,"name":"Season","startDate":"2026-01-01","campaigns":[{"campaignId":1,"name":"Older","startDate":"2026-06-01","status":0,"participantCount":0,"unresolvedCount":0},{"campaignId":2,"name":"Newer","startDate":"2026-06-02","status":0,"participantCount":0,"unresolvedCount":0}]}],"totalCount":2}""")]
    public async Task GetCampaignListAsync_ReturnsServerError_ForInvalidPopulatedPayload(string payload)
    {
        var result = await GetCampaignListFromJsonAsync(payload);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a successful response exceeding the requested row bound is rejected.
    /// </summary>
    [Fact]
    public async Task GetCampaignListAsync_ReturnsServerError_ForOverLimitPayload()
    {
        const string payload = """
            {"seasons":[{"seasonId":1,"name":"Season","startDate":"2026-01-01","campaigns":[
            {"campaignId":2,"name":"A","startDate":"2026-06-02","status":0,"participantCount":0,"unresolvedCount":0},
            {"campaignId":1,"name":"B","startDate":"2026-06-01","status":0,"participantCount":0,"unresolvedCount":0}
            ]}],"totalCount":2}
            """;

        var result = await GetCampaignListFromJsonAsync(payload, limit: 1);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies successful list responses echo the exact requested bound, including empty pages.</summary>
    /// <param name="responseLimit">The bound reported by the server.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(19)]
    [InlineData(21)]
    public async Task GetCampaignListAsync_RejectsDifferentResponseLimit(int responseLimit)
    {
        var payload = $$"""{"seasons":[],"totalCount":0,"limit":{{responseLimit}}}""";

        var result = await GetCampaignListFromJsonAsync(payload, limit: 20);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies closure metadata agrees with the declared lifecycle status.</summary>
    /// <param name="status">The wire lifecycle status.</param>
    /// <param name="closedAt">The JSON closure timestamp or null.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0, "\"2026-07-01T00:00:00Z\"")]
    [InlineData(2, "\"2026-07-01T00:00:00Z\"")]
    [InlineData(1, "null")]
    public async Task GetCampaignListAsync_RejectsInconsistentClosureMetadata(int status, string closedAt)
    {
        var payload = $$"""
            {"seasons":[{"seasonId":1,"name":"Season","startDate":"2026-01-01",
            "concurrencyToken":"11111111-1111-1111-1111-111111111111","campaigns":[
            {"campaignId":1,"name":"Campaign","startDate":"2026-06-01","status":{{status}},
            "closedAt":{{closedAt}},"participantCount":0,"unresolvedCount":0}]}],"totalCount":1}
            """;

        var result = await GetCampaignListFromJsonAsync(payload);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies strict setup-payload invariants map invalid successful responses to server errors.
    /// </summary>
    /// <param name="payload">The invalid successful JSON payload.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("""{"currentSeason":null,"activePlayerCount":-1,"activeTeamCount":0}""")]
    [InlineData("""{"currentSeason":null,"activePlayerCount":0,"activeTeamCount":-1}""")]
    [InlineData("""{"currentSeason":{"seasonId":0,"name":"Season","startDate":"2026-01-01"},"activePlayerCount":0,"activeTeamCount":0}""")]
    [InlineData("""{"currentSeason":{"seasonId":1,"name":"Season","startDate":"2026-01-02","endDate":"2026-01-01"},"activePlayerCount":0,"activeTeamCount":0}""")]
    public async Task GetCreationSetupAsync_ReturnsServerError_ForInvalidPayload(string payload)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignQueryService(http)
            .GetCreationSetupAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies missing required paging metadata is rejected rather than replaced with CLR defaults.</summary>
    /// <param name="payload">A raw successful response with omitted required properties.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("""{"seasons":[],"totalCount":0,"limit":50}""")]
    [InlineData("""{"seasons":[],"totalCount":0,"page":1}""")]
    [InlineData("""{"seasons":[],"totalCount":0}""")]
    public async Task GetCampaignListAsync_RejectsMissingPagingMetadata(string payload)
    {
        var result = await GetCampaignListFromJsonAsync(payload, supplyPaging: false);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Executes the list client against a supplied successful JSON response.
    /// </summary>
    /// <param name="payload">The response JSON.</param>
    /// <param name="limit">The optional requested result bound.</param>
    /// <param name="supplyPaging">Whether fixtures unrelated to paging receive valid required paging properties.</param>
    /// <returns>The client result.</returns>
    private static async Task<ServiceResult<CampaignListResult>> GetCampaignListFromJsonAsync(
        string payload,
        int? limit = null,
        bool supplyPaging = true)
    {
        if (supplyPaging)
        {
            var body = JsonNode.Parse(payload)!.AsObject();
            if (!body.ContainsKey("page"))
            {
                body["page"] = 1;
            }
            if (!body.ContainsKey("limit"))
            {
                body["limit"] = limit ?? GetCampaignListInput.DefaultLimit;
            }
            payload = body.ToJsonString();
        }
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        return await new HttpCampaignQueryService(http).GetCampaignListAsync(
            new GetCampaignListInput { Limit = limit },
            TestContext.Current.CancellationToken);
    }

    /// <summary>Verifies detail requests use the shared route builder.</summary>
    [Fact]
    public async Task GetCampaignDetailAsync_RequestsDetailRoute_AndParsesPayload()
    {
        var sample = new CampaignDetailResult
        {
            CampaignId = 42,
            Name = "Campaign",
            Status = CampaignStatus.Active,
            StartDate = new DateOnly(2026, 6, 1),
            PlannedEndDate = new DateOnly(2026, 8, 1),
            ParticipantCount = 3,
            SeasonId = 1,
            SeasonName = "Season 2026"
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(sample) };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignQueryService(http).GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.CampaignId.ShouldBe(42);
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.GetCampaignDetailUrl(42));
    }

    /// <summary>
    /// Verifies a populated, structurally valid campaign-detail response is accepted.
    /// </summary>
    [Fact]
    public async Task GetCampaignDetailAsync_AcceptsPopulatedValidPayload()
    {
        const string payload = """
            {"campaignId":2,"name":"Campaign","status":0,"startDate":"2026-06-01",
            "plannedEndDate":"2026-08-01","participantCount":3,"seasonId":1,"seasonName":"Season 2026"}
            """;

        var result = await GetCampaignDetailFromJsonAsync(payload);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies a populated, structurally valid Closed campaign-detail response is accepted.
    /// </summary>
    [Fact]
    public async Task GetCampaignDetailAsync_AcceptsPopulatedClosedPayload()
    {
        const string payload = """
            {"campaignId":2,"name":"Campaign","status":1,"startDate":"2026-06-01",
            "plannedEndDate":"2026-08-01","participantCount":3,"seasonId":1,"seasonName":"Season 2026",
            "closedAt":"2026-08-01T00:00:00+00:00","closedByUserId":5,"closedByDisplayName":"Admin A"}
            """;

        var result = await GetCampaignDetailFromJsonAsync(payload);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>Verifies a Draft detail without closure metadata is accepted.</summary>
    [Fact]
    public async Task GetCampaignDetailAsync_AcceptsDraftPayload()
    {
        const string payload = """
            {"campaignId":2,"name":"Draft Campaign","status":2,"startDate":"2026-06-01",
            "plannedEndDate":"2026-08-01","participantCount":0,"seasonId":1,"seasonName":"Season 2026"}
            """;

        var result = await GetCampaignDetailFromJsonAsync(payload);

        result.IsSuccess.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies strict detail-payload invariants map invalid successful responses to server errors.
    /// </summary>
    /// <param name="payload">The invalid successful JSON payload.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("""{"campaignId":0,"name":"Campaign","status":0,"startDate":"2026-06-01","plannedEndDate":null,"participantCount":0,"seasonId":1,"seasonName":"S"}""")]
    [InlineData("""{"campaignId":2,"name":null,"status":0,"startDate":"2026-06-01","plannedEndDate":null,"participantCount":0,"seasonId":1,"seasonName":"S"}""")]
    [InlineData("""{"campaignId":2,"name":" ","status":0,"startDate":"2026-06-01","plannedEndDate":null,"participantCount":0,"seasonId":1,"seasonName":"S"}""")]
    [InlineData("""{"campaignId":2,"name":"Campaign","status":0,"startDate":"0001-01-01","plannedEndDate":null,"participantCount":0,"seasonId":1,"seasonName":"S"}""")]
    [InlineData("""{"campaignId":2,"name":"Campaign","status":0,"startDate":"2026-06-01","plannedEndDate":"2026-05-01","participantCount":0,"seasonId":1,"seasonName":"S"}""")]
    [InlineData("""{"campaignId":2,"name":"Campaign","status":0,"startDate":"2026-06-01","plannedEndDate":null,"participantCount":-1,"seasonId":1,"seasonName":"S"}""")]
    [InlineData("""{"campaignId":2,"name":"Campaign","status":0,"startDate":"2026-06-01","plannedEndDate":null,"participantCount":0,"seasonId":0,"seasonName":"S"}""")]
    [InlineData("""{"campaignId":2,"name":"Campaign","status":0,"startDate":"2026-06-01","plannedEndDate":null,"participantCount":0,"seasonId":1,"seasonName":" "}""")]
    [InlineData("""{"campaignId":2,"name":"Campaign","status":99,"startDate":"2026-06-01","plannedEndDate":null,"participantCount":0,"seasonId":1,"seasonName":"S"}""")]
    [InlineData("""{"campaignId":2,"name":"Campaign","status":0,"startDate":"2026-06-01","plannedEndDate":null,"participantCount":0,"seasonId":1,"seasonName":"S","closedAt":"2026-08-01T00:00:00+00:00"}""")]
    [InlineData("""{"campaignId":2,"name":"Campaign","status":0,"startDate":"2026-06-01","plannedEndDate":null,"participantCount":0,"seasonId":1,"seasonName":"S","closedByUserId":5}""")]
    [InlineData("""{"campaignId":2,"name":"Campaign","status":1,"startDate":"2026-06-01","plannedEndDate":null,"participantCount":0,"seasonId":1,"seasonName":"S"}""")]
    [InlineData("""{"campaignId":2,"name":"Campaign","status":1,"startDate":"2026-06-01","plannedEndDate":null,"participantCount":0,"seasonId":1,"seasonName":"S","closedAt":"2026-08-01T00:00:00+00:00","closedByUserId":null,"closedByDisplayName":"Admin A"}""")]
    [InlineData("""{"campaignId":2,"name":"Campaign","status":1,"startDate":"2026-06-01","plannedEndDate":null,"participantCount":0,"seasonId":1,"seasonName":"S","closedAt":"2026-08-01T00:00:00+00:00","closedByUserId":5,"closedByDisplayName":" "}""")]
    public async Task GetCampaignDetailAsync_ReturnsServerError_ForInvalidPopulatedPayload(string payload)
    {
        var result = await GetCampaignDetailFromJsonAsync(payload);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies detail problems retain their problem kind from ProblemDetails.</summary>
    [Fact]
    public async Task GetCampaignDetailAsync_ReturnsNotFound_FromProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { title = "Missing", status = 404, detail = "no campaign" })
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignQueryService(http).GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>Verifies invalid detail input is rejected before an HTTP request is sent.</summary>
    [Fact]
    public async Task GetCampaignDetailAsync_ReturnsValidationProblem_ForInvalidInput()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignQueryService(http).GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        handler.LastRequest.ShouldBeNull();
    }

    /// <summary>
    /// Executes the detail client against a supplied successful JSON response.
    /// </summary>
    /// <param name="payload">The response JSON.</param>
    /// <returns>The client result.</returns>
    private static async Task<ServiceResult<CampaignDetailResult>> GetCampaignDetailFromJsonAsync(string payload)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        return await new HttpCampaignQueryService(http).GetCampaignDetailAsync(
            new GetCampaignDetailInput { CampaignId = 2 },
            TestContext.Current.CancellationToken);
    }
}
