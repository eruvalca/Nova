using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Tests for <see cref="HttpPlayerService"/> roster query behavior.
/// </summary>
public sealed class HttpPlayerServiceTests
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

    [Fact]
    public async Task GetPlayerRosterAsync_SendsGetToRosterEndpoint_AndReturnsPagedResult()
    {
        var payload = new PagedResult<PlayerListItem>(
            [
                new PlayerListItem
                {
                    PlayerId = 10,
                    DisplayName = "Alex Archer",
                    GraduationYear = 2031,
                    LifecycleStatus = Nova.Shared.Enums.LifecycleStatus.Archived,
                    CurrentTags = [new PlayerRosterTagItem(17, "Speed", "#001122")],
                    ActiveCampaigns = [],
                    JoinedAt = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero)
                }
            ],
            Page: 1,
            PageSize: 20,
            TotalCount: 1);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerService(httpClient);

        var result = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput
            {
                ClubId = 42,
                Search = "Alex",
                LifecycleStatus = "archived",
                GraduationYear = 2031,
                PlayerTagId = 17,
                SortBy = "joinedAt",
                SortDirection = "desc",
                Page = 1,
                PageSize = 20
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.TotalCount.ShouldBe(1);
        result.Value.Items.Single().DisplayName.ShouldBe("Alex Archer");
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/clubs/42/players/roster?search=Alex&lifecycleStatus=archived&graduationYear=2031&playerTagId=17&sortBy=joinedAt&sortDirection=desc&page=1&pageSize=20");
    }

    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsServiceProblem_OnNonSuccessStatusCode()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = JsonContent.Create(new { detail = "Forbidden." })
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerService(httpClient);

        var result = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Forbidden);
    }

    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsServerError_OnNullSuccessPayload()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", System.Text.Encoding.UTF8, "application/json")
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerService(httpClient);

        var result = await service.GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies an empty roster page is a valid successful response.
    /// </summary>
    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsEmptyPage_WhenItemsAreEmpty()
    {
        var payload = new PagedResult<PlayerListItem>([], Page: 1, PageSize: 20, TotalCount: 0);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.ShouldNotBeNull();
        result.Value.Items.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies other invalid successful roster-response bodies become server errors.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task GetPlayerRosterAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a roster response with an invalid page number is rejected.
    /// </summary>
    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsServerError_WhenPageInvariantIsInvalid()
    {
        var payload = new PagedResult<PlayerListItem>([], Page: 0, PageSize: 20, TotalCount: 0);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies invalid shared input is rejected before a lossy URL builder can normalize it.
    /// </summary>
    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsValidationProblem_BeforeSendingInvalidInput()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 0, PageSize = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        handler.LastRequest.ShouldBeNull();
    }

    /// <summary>
    /// Verifies a roster response cannot exceed the shared page-size contract.
    /// </summary>
    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsServerError_WhenPageSizeExceedsMaximum()
    {
        var payload = new PagedResult<PlayerListItem>(
            [],
            Page: 1,
            PageSize: GetPlayerRosterInput.MaxPageSize + 1,
            TotalCount: 0);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies page metadata must match the requested roster slice.
    /// </summary>
    /// <param name="responsePage">The page returned by the server.</param>
    /// <param name="responsePageSize">The page size returned by the server.</param>
    [Theory]
    [InlineData(1, 25)]
    [InlineData(2, 20)]
    public async Task GetPlayerRosterAsync_ReturnsServerError_WhenPageMetadataDoesNotMatchRequest(
        int responsePage,
        int responsePageSize)
    {
        var payload = new PagedResult<PlayerListItem>(
            [],
            Page: responsePage,
            PageSize: responsePageSize,
            TotalCount: 0);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42, Page = 2, PageSize = 25 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies exact lifecycle and graduation-year filters are reflected in returned rows.
    /// </summary>
    /// <param name="lifecycleStatus">The lifecycle status returned by the server.</param>
    /// <param name="graduationYear">The graduation year returned by the server.</param>
    [Theory]
    [InlineData(Nova.Shared.Enums.LifecycleStatus.Active, 2031)]
    [InlineData(Nova.Shared.Enums.LifecycleStatus.Archived, 2030)]
    public async Task GetPlayerRosterAsync_ReturnsServerError_WhenRowDoesNotMatchExactFilters(
        Nova.Shared.Enums.LifecycleStatus lifecycleStatus,
        int graduationYear)
    {
        var player = new PlayerListItem
        {
            PlayerId = 10,
            DisplayName = "Alex Archer",
            GraduationYear = graduationYear,
            LifecycleStatus = lifecycleStatus,
            CurrentTags = [],
            ActiveCampaigns = [],
            JoinedAt = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero)
        };
        var payload = new PagedResult<PlayerListItem>([player], Page: 1, PageSize: 20, TotalCount: 1);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput
            {
                ClubId = 42,
                LifecycleStatus = "archived",
                GraduationYear = 2031
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies roster rows always require a graduation year within the shared contract.
    /// </summary>
    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsServerError_WhenGraduationYearIsOutOfRange()
    {
        var player = CreatePlayer(
            10,
            "Alex Archer",
            new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero)) with
        {
            GraduationYear = 1999
        };
        var payload = new PagedResult<PlayerListItem>([player], Page: 1, PageSize: 20, TotalCount: 1);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies every returned row contains the requested active-campaign tag.
    /// </summary>
    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsServerError_WhenRowDoesNotMatchTagFilter()
    {
        var player = CreatePlayer(
            10,
            "Alex Archer",
            new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero)) with
        {
            CurrentTags = [new PlayerRosterTagItem(18, "Other Tag", "#001122")]
        };
        var payload = new PagedResult<PlayerListItem>([player], Page: 1, PageSize: 20, TotalCount: 1);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42, PlayerTagId = 17 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies joined-date sorting preserves the requested direction.
    /// </summary>
    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsServerError_WhenJoinedAtOrderIsIncorrect()
    {
        var older = CreatePlayer(1, "Older", new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = CreatePlayer(2, "Newer", new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero));
        var payload = new PagedResult<PlayerListItem>([older, newer], Page: 1, PageSize: 20, TotalCount: 2);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput
            {
                ClubId = 42,
                SortBy = "joinedAt",
                SortDirection = "desc"
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies the player identifier remains the ascending tie-breaker for joined-date sorting.
    /// </summary>
    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsServerError_WhenJoinedAtTieBreakerIsIncorrect()
    {
        var joinedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var payload = new PagedResult<PlayerListItem>(
            [CreatePlayer(2, "Second", joinedAt), CreatePlayer(1, "First", joinedAt)],
            Page: 1,
            PageSize: 20,
            TotalCount: 2);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42, SortBy = "joinedAt" },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies display-name ties are accepted because the DTO omits the component sort keys.
    /// </summary>
    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsRows_WhenDisplayNamesMatchButIdsAreReversed()
    {
        var joinedAt = new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero);
        var payload = new PagedResult<PlayerListItem>(
            [CreatePlayer(2, "Same Name", joinedAt), CreatePlayer(1, "Same Name", joinedAt)],
            Page: 1,
            PageSize: 20,
            TotalCount: 2);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Select(player => player.PlayerId).ShouldBe([2, 1]);
    }

    /// <summary>
    /// Verifies an eventually consistent total may briefly lag valid returned rows.
    /// </summary>
    [Fact]
    public async Task GetPlayerRosterAsync_ReturnsRows_WhenTotalTemporarilyLags()
    {
        var player = new PlayerListItem
        {
            PlayerId = 10,
            DisplayName = "Alex Archer",
            GraduationYear = 2030,
            LifecycleStatus = Nova.Shared.Enums.LifecycleStatus.Active,
            CurrentTags = [],
            ActiveCampaigns = [],
            JoinedAt = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero)
        };
        var payload = new PagedResult<PlayerListItem>([player], Page: 1, PageSize: 20, TotalCount: 0);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerService(httpClient).GetPlayerRosterAsync(
            new GetPlayerRosterInput { ClubId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(1);
        result.Value.TotalCount.ShouldBe(0);
    }

    /// <summary>
    /// Creates a structurally valid roster row for ordering tests.
    /// </summary>
    /// <param name="playerId">The player identifier.</param>
    /// <param name="displayName">The player display name.</param>
    /// <param name="joinedAt">The roster join timestamp.</param>
    /// <returns>A valid active roster row.</returns>
    private static PlayerListItem CreatePlayer(
        long playerId,
        string displayName,
        DateTimeOffset joinedAt)
        => new()
        {
            PlayerId = playerId,
            DisplayName = displayName,
            GraduationYear = 2030,
            LifecycleStatus = Nova.Shared.Enums.LifecycleStatus.Active,
            CurrentTags = [],
            ActiveCampaigns = [],
            JoinedAt = joinedAt
        };
}
