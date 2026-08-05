using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Teams;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Verifies response handling for the team-detail HTTP client.
/// </summary>
public sealed class HttpTeamDetailServiceTests
{
    /// <summary>
    /// Verifies successful detail payloads are requested through the shared route.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsDetail_OnSuccess()
    {
        var payload = new TeamDetailDto(7, 8, "U16", 2028, LifecycleStatus.Active, [], []);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(7, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TeamId.ShouldBe(7);
        result.Value.ActivePlacementImpacts.ShouldNotBeNull();
        result.Value.ActivePlacementImpacts.ShouldBeEmpty();
        result.Value.PlacementHistory.ShouldNotBeNull();
        result.Value.PlacementHistory.ShouldBeEmpty();
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/teams/7");
    }

    /// <summary>
    /// Verifies malformed successful responses are surfaced as protocol failures.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenSuccessBodyIsJsonNull()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create<object?>(null)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(7, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(Nova.Shared.Results.ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies other invalid successful response bodies are surfaced as protocol failures.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies detail responses that violate portable team-detail invariants are rejected.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenTeamDetailInvariantIsInvalid()
    {
        var payload = new TeamDetailDto(7, 8, "U16", 2028, LifecycleStatus.Active, [], [])
        {
            ActivePlacementImpactTotalCount = -1
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies detail rejects a success payload for a different team.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenResponseTeamIdDoesNotMatch()
    {
        var payload = new TeamDetailDto(8, 9, "U16", 2028, LifecycleStatus.Active, [], []);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a matching requested identifier does not make a zero response identifier valid.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenRequestedAndResponseTeamIdsAreZero()
    {
        var payload = new TeamDetailDto(0, 9, "U16", 2028, LifecycleStatus.Active, [], []);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            0,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies active placement summaries must be Active rows from the returned history.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenActivePlacementIsContradictory()
    {
        var placement = new TeamPlacementImpactDto(
            1,
            2,
            "Campaign",
            CampaignStatus.Closed,
            new DateOnly(2025, 1, 1),
            3,
            "Player",
            2028,
            null,
            PlacementOutcome.Assigned);
        var payload = new TeamDetailDto(
            7,
            8,
            "U16",
            2028,
            LifecycleStatus.Active,
            [placement],
            [placement]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies active placement summaries must exactly match their placement-history record.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenActivePlacementDiffersFromHistory()
    {
        var history = new TeamPlacementImpactDto(
            1,
            2,
            "Campaign",
            CampaignStatus.Active,
            new DateOnly(2025, 1, 1),
            3,
            "Player",
            2028,
            null,
            PlacementOutcome.Assigned);
        var active = history with { CampaignName = "Different Campaign" };
        var payload = new TeamDetailDto(
            7,
            8,
            "U16",
            2028,
            LifecycleStatus.Active,
            [active],
            [history]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies active placement summaries contain every Active row from placement history.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenActivePlacementIsMissing()
    {
        var placement = new TeamPlacementImpactDto(
            1,
            2,
            "Campaign",
            CampaignStatus.Active,
            new DateOnly(2025, 1, 1),
            3,
            "Player",
            2028,
            null,
            PlacementOutcome.Assigned);
        var payload = new TeamDetailDto(
            7,
            8,
            "U16",
            2028,
            LifecycleStatus.Active,
            [],
            [placement]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies team placement rows require the Assigned outcome.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenPlacementOutcomeIsNotAssigned()
    {
        var placement = new TeamPlacementImpactDto(
            1,
            2,
            "Campaign",
            CampaignStatus.Closed,
            new DateOnly(2025, 1, 1),
            3,
            "Player",
            2028,
            null,
            PlacementOutcome.NotSelected);
        var payload = new TeamDetailDto(
            7,
            8,
            "U16",
            2028,
            LifecycleStatus.Active,
            [],
            [placement]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies team placement rows reject undefined campaign statuses.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenCampaignStatusIsUndefined()
    {
        var placement = new TeamPlacementImpactDto(
            1,
            2,
            "Campaign",
            (CampaignStatus)99,
            new DateOnly(2025, 1, 1),
            3,
            "Player",
            2028,
            null,
            PlacementOutcome.Assigned);
        var payload = new TeamDetailDto(
            7,
            8,
            "U16",
            2028,
            LifecycleStatus.Active,
            [],
            [placement]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies placement history retains descending campaign-date ordering.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenPlacementHistoryIsOutOfOrder()
    {
        var older = new TeamPlacementImpactDto(
            1,
            2,
            "Older Campaign",
            CampaignStatus.Closed,
            new DateOnly(2025, 1, 1),
            3,
            "Player One",
            2028,
            null,
            PlacementOutcome.Assigned);
        var newer = older with
        {
            PlayerCampaignAssignmentId = 2,
            CampaignId = 3,
            CampaignName = "Newer Campaign",
            CampaignStartDate = new DateOnly(2025, 2, 1),
            PlayerId = 4,
            PlayerDisplayName = "Player Two"
        };
        var payload = new TeamDetailDto(
            7,
            8,
            "U16",
            2028,
            LifecycleStatus.Active,
            [],
            [older, newer]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies the truncation flag remains consistent with its shared total and bound.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenTruncationFlagIsContradictory()
    {
        var payload = new TeamDetailDto(7, 8, "U16", 2028, LifecycleStatus.Active, [], [])
        {
            PlacementHistoryTotalCount = TeamDetailDto.MaxPlacementHistoryItems + 1,
            IsPlacementHistoryTruncated = false
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies placement history cannot exceed the shared detail bound.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsServerError_WhenPlacementHistoryExceedsBound()
    {
        var placements = Enumerable.Range(1, TeamDetailDto.MaxPlacementHistoryItems + 1)
            .Select(index => new TeamPlacementImpactDto(
                index,
                index,
                $"Campaign {index}",
                CampaignStatus.Closed,
                new DateOnly(2025, 1, 1),
                index,
                $"Player {index}",
                2028,
                null,
                PlacementOutcome.Assigned))
            .ToList();
        var payload = new TeamDetailDto(
            7,
            8,
            "U16",
            2028,
            LifecycleStatus.Active,
            [],
            placements);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies an eventually consistent placement total may briefly lag returned rows.
    /// </summary>
    [Fact]
    public async Task GetTeamDetailAsync_ReturnsRows_WhenTotalTemporarilyLags()
    {
        var placement = new TeamPlacementImpactDto(
            1,
            2,
            "Campaign",
            CampaignStatus.Closed,
            new DateOnly(2025, 1, 1),
            3,
            "Player",
            2028,
            null,
            PlacementOutcome.Assigned);
        var payload = new TeamDetailDto(
            7,
            8,
            "U16",
            2028,
            LifecycleStatus.Active,
            [],
            [placement])
        {
            PlacementHistoryTotalCount = 0
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamDetailService(http).GetTeamDetailAsync(
            7,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PlacementHistory.Count.ShouldBe(1);
        result.Value.PlacementHistoryTotalCount.ShouldBe(0);
    }

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpRequestMessage? LastRequest { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }
}
