using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services.Dashboard;
using Nova.Shared.Enums;
using Nova.Shared.Features.Dashboard;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Dashboard;

/// <summary>
/// Verifies route building and strict success-payload validation for the dashboard HTTP client.
/// </summary>
public sealed class HttpDashboardQueryServiceTests
{
    /// <summary>Verifies the summary request uses the shared route and reads a populated payload.</summary>
    [Fact]
    public async Task GetDashboardAsync_RequestsSharedRoute_AndReadsPayload()
    {
        HttpRequestMessage? capturedRequest = null;
        var payload = new ClubDashboardResult
        {
            ActiveCampaigns =
            [
                new ActiveCampaignCardDto
                {
                    CampaignId = 42,
                    Name = "Campaign A",
                    SeasonName = "Season 1",
                    StartDate = new DateOnly(2026, 6, 1),
                    PlannedEndDate = null,
                    Status = CampaignStatus.Active,
                    ParticipantCount = 3,
                    UnresolvedCount = 1,
                    WorkspaceUrl = "/campaigns/42"
                }
            ],
            Roster = new RosterCountsDto { ActivePlayers = 1, ArchivedPlayers = 0 },
            Teams = new TeamCountsDto { ActiveTeams = 1, ArchivedTeams = 0 },
            AdminAttention = null
        };
        var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpDashboardQueryService(http);

        var result = await service.GetDashboardAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ActiveCampaigns.Count.ShouldBe(1);
        result.Value.ActiveCampaigns[0].WorkspaceUrl.ShouldBe("/campaigns/42");
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/dashboard");
    }

    /// <summary>Verifies a non-success dashboard response retains its problem kind.</summary>
    [Fact]
    public async Task GetDashboardAsync_ReturnsProblem_FromNonSuccess()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
            {
                Content = JsonContent.Create(new ProblemPayload(403, "Forbidden", "Not allowed."))
            }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpDashboardQueryService(http);

        var result = await service.GetDashboardAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    /// <summary>
    /// Verifies invalid successful dashboard bodies are surfaced as protocol failures.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    [InlineData("{}")]
    [InlineData("""{"activeCampaigns":null,"roster":{"activePlayers":0,"archivedPlayers":0},"teams":{"activeTeams":0,"archivedTeams":0},"adminAttention":null}""")]
    [InlineData("""{"activeCampaigns":[null],"roster":{"activePlayers":0,"archivedPlayers":0},"teams":{"activeTeams":0,"archivedTeams":0},"adminAttention":null}""")]
    [InlineData("""{"activeCampaigns":[{"campaignId":0,"name":"A","seasonName":"S","startDate":"2026-06-01","plannedEndDate":null,"status":0,"participantCount":1,"unresolvedCount":0,"workspaceUrl":"/campaigns/1"}],"roster":{"activePlayers":0,"archivedPlayers":0},"teams":{"activeTeams":0,"archivedTeams":0},"adminAttention":null}""")]
    [InlineData("""{"activeCampaigns":[{"campaignId":1,"name":" ","seasonName":"S","startDate":"2026-06-01","plannedEndDate":null,"status":0,"participantCount":1,"unresolvedCount":0,"workspaceUrl":"/campaigns/1"}],"roster":{"activePlayers":0,"archivedPlayers":0},"teams":{"activeTeams":0,"archivedTeams":0},"adminAttention":null}""")]
    [InlineData("""{"activeCampaigns":[{"campaignId":1,"name":"A","seasonName":"S","startDate":"2026-06-01","plannedEndDate":null,"status":0,"participantCount":1,"unresolvedCount":2,"workspaceUrl":"/campaigns/1"}],"roster":{"activePlayers":0,"archivedPlayers":0},"teams":{"activeTeams":0,"archivedTeams":0},"adminAttention":null}""")]
    [InlineData("""{"activeCampaigns":[],"roster":{"activePlayers":-1,"archivedPlayers":0},"teams":{"activeTeams":0,"archivedTeams":0},"adminAttention":null}""")]
    [InlineData("""{"activeCampaigns":[],"roster":{"activePlayers":0,"archivedPlayers":0},"teams":{"activeTeams":0,"archivedTeams":0},"adminAttention":{"pendingJoinRequestCount":-1,"unresolvedPlacementCount":0,"firstUnresolvedCampaignId":null}}""")]
    public async Task GetDashboardAsync_ReturnsServerError_ForInvalidSuccessPayload(string body)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpDashboardQueryService(http);

        var result = await service.GetDashboardAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies an over-cap dashboard payload is rejected.</summary>
    [Fact]
    public async Task GetDashboardAsync_ReturnsServerError_ForOverCapPayload()
    {
        var cards = Enumerable.Range(1, 21)
            .Select(index => new ActiveCampaignCardDto
            {
                CampaignId = index,
                Name = $"Campaign {index}",
                SeasonName = "Season",
                StartDate = new DateOnly(2026, 6, 1),
                PlannedEndDate = null,
                Status = CampaignStatus.Active,
                ParticipantCount = 1,
                UnresolvedCount = 0,
                WorkspaceUrl = $"/campaigns/{index}"
            })
            .ToList();
        var payload = new ClubDashboardResult
        {
            ActiveCampaigns = cards,
            Roster = new RosterCountsDto { ActivePlayers = 0, ArchivedPlayers = 0 },
            Teams = new TeamCountsDto { ActiveTeams = 0, ArchivedTeams = 0 },
            AdminAttention = null
        };
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpDashboardQueryService(http);

        var result = await service.GetDashboardAsync(TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies the first activity page uses the shared route and reads structured context.</summary>
    [Fact]
    public async Task GetActivityAsync_RequestsSharedRoute_AndReadsStructuredPayload()
    {
        HttpRequestMessage? capturedRequest = null;
        var payload = new DashboardActivityResult([BuildCampaignEvent(1, ActivityAt)]);
        var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpDashboardQueryService(http);

        var result = await service.GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Events[0].Context.ShouldBeOfType<CampaignActivityContextDto>();
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/dashboard/activity");
    }

    /// <summary>Verifies a continuation token is escaped into the shared activity route.</summary>
    [Fact]
    public async Task GetActivityAsync_EmitsEscapedContinuationToken()
    {
        HttpRequestMessage? capturedRequest = null;
        var payload = new DashboardActivityResult([]);
        var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) });
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpDashboardQueryService(http);

        var result = await service.GetActivityAsync(
            new GetDashboardActivityInput { ContinuationToken = "cursor+/=" },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/dashboard/activity?continuationToken=cursor%2B%2F%3D");
    }

    /// <summary>Verifies invalid caller input is rejected before any HTTP request is made.</summary>
    [Fact]
    public async Task GetActivityAsync_ReturnsValidation_ForOversizedContinuationToken()
    {
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpDashboardQueryService(http);

        var result = await service.GetActivityAsync(
            new GetDashboardActivityInput { ContinuationToken = new string('x', 513) },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    /// <summary>
    /// Verifies invalid successful activity bodies are surfaced as protocol failures.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    [InlineData("{}")]
    [InlineData("""{"events":null}""")]
    [InlineData("""{"events":[null]}""")]
    [InlineData("""{"events":[{"kind":0,"eventId":1,"eventAt":"2026-10-01T09:00:00+00:00"}]}""")]
    [InlineData("""{"events":[{"kind":2,"eventId":1,"eventAt":"2026-10-01T09:00:00+00:00","context":{"family":"membership","memberUserId":1,"memberDisplayName":"Member"}}]}""")]
    [InlineData("""{"events":[{"kind":0,"eventId":0,"eventAt":"2026-10-01T09:00:00+00:00","context":{"family":"campaign","actorDisplayName":"Admin","campaignId":1,"campaignName":"Campaign"}}]}""")]
    [InlineData("""{"events":[{"kind":0,"eventId":1,"eventAt":"2026-10-01T09:00:00+00:00","context":{"family":"campaign","actorDisplayName":" ","campaignId":1,"campaignName":"Campaign"}}]}""")]
    [InlineData("""{"events":[],"nextContinuationToken":" "}""")]
    public async Task GetActivityAsync_ReturnsServerError_ForInvalidSuccessPayload(string body)
    {
        var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpDashboardQueryService(http);

        var result = await service.GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies an ordering-violating activity payload is rejected.</summary>
    [Fact]
    public async Task GetActivityAsync_ReturnsServerError_ForOrderingViolation()
    {
        var payload = new DashboardActivityResult(
        [
            BuildCampaignEvent(1, new DateTimeOffset(2026, 10, 1, 9, 0, 0, TimeSpan.Zero)),
            BuildCampaignEvent(2, new DateTimeOffset(2026, 10, 2, 9, 0, 0, TimeSpan.Zero))
        ]);
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpDashboardQueryService(http);

        var result = await service.GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies an activity payload over the fixed server page size is rejected.</summary>
    [Fact]
    public async Task GetActivityAsync_ReturnsServerError_ForOverBoundPayload()
    {
        var payload = new DashboardActivityResult(Enumerable.Range(1, 21)
            .Select(index => BuildCampaignEvent(index, ActivityAt.AddMinutes(-index)))
            .ToList());
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpDashboardQueryService(http);

        var result = await service.GetActivityAsync(
            new GetDashboardActivityInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies equal timestamps accept the same event-identity ordering as the server.</summary>
    [Fact]
    public async Task GetActivityAsync_AcceptsEqualTimestamp_WhenEventIdsDescend()
    {
        var payload = new DashboardActivityResult(
        [
            BuildCampaignEvent(2, ActivityAt),
            BuildCampaignEvent(1, ActivityAt)
        ]);
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) }));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.com") };
        var service = new HttpDashboardQueryService(http);

        var result = await service.GetActivityAsync(new(), TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
    }

    private static readonly DateTimeOffset ActivityAt = new(2026, 10, 1, 9, 0, 0, TimeSpan.Zero);

    /// <summary>Builds a valid campaign-opened activity event.</summary>
    /// <param name="eventId">The event identifier.</param>
    /// <param name="eventAt">When the event occurred.</param>
    /// <returns>A campaign-opened event.</returns>
    private static DashboardActivityItemDto BuildCampaignEvent(long eventId, DateTimeOffset eventAt)
        => new()
        {
            Kind = DashboardActivityEventKind.CampaignOpened,
            EventId = eventId,
            EventAt = eventAt,
            Context = new CampaignActivityContextDto
            {
                ActorDisplayName = "Admin A",
                CampaignId = 42,
                CampaignName = "Campaign A",
                SeasonName = "Fall"
            }
        };

    /// <summary>Minimal problem-details payload shape for problem-response tests.</summary>
    private sealed record ProblemPayload(int Status, string Title, string Detail);

    /// <summary>Records and serves a canned response for the next request.</summary>
    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => callback(request);
    }
}
