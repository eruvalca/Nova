using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Tests for <see cref="HttpPlayerDetailService"/> and shared player endpoint URL builders.
/// </summary>
public sealed class HttpPlayerDetailServiceTests
{
    /// <summary>
    /// Captures outgoing requests and returns a preconfigured response.
    /// </summary>
    /// <param name="response">The response returned to the caller.</param>
    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        /// <summary>
        /// Gets the last request sent through the handler.
        /// </summary>
        public HttpRequestMessage? LastRequest { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }

    /// <summary>
    /// Verifies the client calls the shared detail URL and deserializes a successful payload.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsPlayerDetail_OnSuccess()
    {
        var tagApplication = new PlayerTagApplicationDto(
            1,
            1,
            "Leadership",
            "#001122",
            false,
            5,
            "Coach",
            new DateTimeOffset(2025, 3, 2, 0, 0, 0, TimeSpan.Zero));
        var activeHistory = new PlayerCampaignHistoryDto(
            11,
            12,
            "Spring Tryouts",
            CampaignStatus.Active,
            new DateOnly(2025, 3, 1),
            null,
            PlacementOutcome.NotSelected,
            null,
            [],
            [tagApplication]);
        var payload = new PlayerDetailDto(
            PlayerId: 42,
            FirstName: "Alex",
            LastName: "Athlete",
            DateOfBirth: new DateOnly(2010, 2, 3),
            Gender: Gender.Male,
            GraduationYear: 2028,
            JerseyNumber: 11,
            LifecycleStatus: LifecycleStatus.Active,
            CurrentTraits: [new PlayerCurrentTraitDto(1, "Leadership", "#001122")],
            CampaignHistory: [activeHistory]);

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerDetailService(httpClient);

        var result = await service.GetPlayerDetailAsync(42, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PlayerId.ShouldBe(42);
        result.Value.FirstName.ShouldBe("Alex");
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Get);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe("/api/players/42");
    }

    /// <summary>
    /// Verifies current traits cannot diverge from active-campaign tag applications.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenCurrentTraitsDoNotMatchHistory()
    {
        var payload = new PlayerDetailDto(
            PlayerId: 42,
            FirstName: "Alex",
            LastName: "Athlete",
            DateOfBirth: new DateOnly(2010, 2, 3),
            Gender: null,
            GraduationYear: 2028,
            JerseyNumber: null,
            LifecycleStatus: LifecycleStatus.Active,
            CurrentTraits: [new PlayerCurrentTraitDto(1, "Leadership", "#001122")],
            CampaignHistory: []);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies the client does not reject a date value currently permitted by the shared contract.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsDetail_WhenDateOfBirthIsMinimumValue()
    {
        var payload = new PlayerDetailDto(
            42,
            "Alex",
            "Athlete",
            DateOnly.MinValue,
            null,
            2028,
            null,
            LifecycleStatus.Active,
            [],
            []);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DateOfBirth.ShouldBe(DateOnly.MinValue);
    }

    /// <summary>
    /// Verifies detail rejects a success payload for a different player.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenResponsePlayerIdDoesNotMatch()
    {
        var payload = new PlayerDetailDto(
            43,
            "Alex",
            "Athlete",
            new DateOnly(2010, 2, 3),
            null,
            2028,
            null,
            LifecycleStatus.Active,
            [],
            []);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a matching requested identifier does not make a zero response identifier valid.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenRequestedAndResponsePlayerIdsAreZero()
    {
        var payload = new PlayerDetailDto(
            0,
            "Alex",
            "Athlete",
            new DateOnly(2010, 2, 3),
            null,
            2028,
            null,
            LifecycleStatus.Active,
            [],
            []);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            0,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies nested team summaries require bounded years and defined lifecycle states.
    /// </summary>
    /// <param name="graduationYear">The team graduation year returned by the server.</param>
    /// <param name="lifecycleStatus">The team lifecycle status returned by the server.</param>
    [Theory]
    [InlineData(1999, LifecycleStatus.Active)]
    [InlineData(2028, (LifecycleStatus)99)]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenHistoryTeamStateIsInvalid(
        int graduationYear,
        LifecycleStatus lifecycleStatus)
    {
        var history = new PlayerCampaignHistoryDto(
            11,
            12,
            "Spring Tryouts",
            CampaignStatus.Closed,
            new DateOnly(2025, 3, 1),
            null,
            PlacementOutcome.Assigned,
            new PlayerTeamSummaryDto(7, "U16", graduationYear, lifecycleStatus),
            [],
            []);
        var payload = new PlayerDetailDto(
            42,
            "Alex",
            "Athlete",
            new DateOnly(2010, 2, 3),
            null,
            2028,
            null,
            LifecycleStatus.Active,
            [],
            [history]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies placement outcomes and team presence cannot contradict each other.
    /// </summary>
    /// <param name="outcome">The placement outcome returned by the server.</param>
    /// <param name="includeTeam">Whether the response includes a team summary.</param>
    [Theory]
    [InlineData(PlacementOutcome.Assigned, false)]
    [InlineData(PlacementOutcome.NotSelected, true)]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenPlacementTeamRelationshipIsInvalid(
        PlacementOutcome outcome,
        bool includeTeam)
    {
        var history = new PlayerCampaignHistoryDto(
            11,
            12,
            "Spring Tryouts",
            CampaignStatus.Closed,
            new DateOnly(2025, 3, 1),
            null,
            outcome,
            includeTeam
                ? new PlayerTeamSummaryDto(7, "U16", 2028, LifecycleStatus.Active)
                : null,
            [],
            []);
        var payload = new PlayerDetailDto(
            42,
            "Alex",
            "Athlete",
            new DateOnly(2010, 2, 3),
            null,
            2028,
            null,
            LifecycleStatus.Active,
            [],
            [history]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies campaign-history rows reject undefined campaign statuses.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenCampaignStatusIsUndefined()
    {
        var history = new PlayerCampaignHistoryDto(
            11,
            12,
            "Spring Tryouts",
            (CampaignStatus)99,
            new DateOnly(2025, 3, 1),
            null,
            PlacementOutcome.NotSelected,
            null,
            [],
            []);
        var payload = new PlayerDetailDto(
            42,
            "Alex",
            "Athlete",
            new DateOnly(2010, 2, 3),
            null,
            2028,
            null,
            LifecycleStatus.Active,
            [],
            [history]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies campaign history retains newest-campaign-first ordering.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenCampaignHistoryIsOutOfOrder()
    {
        var older = new PlayerCampaignHistoryDto(
            11,
            12,
            "Older Campaign",
            CampaignStatus.Closed,
            new DateOnly(2025, 1, 1),
            null,
            PlacementOutcome.NotSelected,
            null,
            [],
            []);
        var newer = older with
        {
            PlayerCampaignAssignmentId = 13,
            CampaignId = 14,
            CampaignName = "Newer Campaign",
            CampaignStartDate = new DateOnly(2025, 2, 1)
        };
        var payload = new PlayerDetailDto(
            42,
            "Alex",
            "Athlete",
            new DateOnly(2010, 2, 3),
            null,
            2028,
            null,
            LifecycleStatus.Active,
            [],
            [older, newer]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies the client maps unsuccessful responses into <see cref="ServiceProblem"/>.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsServiceProblem_OnNotFound()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(new { detail = "Not found." })
        };

        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var service = new HttpPlayerDetailService(httpClient);

        var result = await service.GetPlayerDetailAsync(404, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    /// <summary>
    /// Verifies the shared URL builder creates the canonical detail route.
    /// </summary>
    [Fact]
    public void GetDetailUrl_BuildsCanonicalPlayerDetailRoute()
    {
        PlayerEndpoints.GetDetailUrl(123).ShouldBe("/api/players/123");
    }

    /// <summary>
    /// Verifies invalid successful detail-response bodies become server errors.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, System.Text.Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies detail responses that violate portable player invariants are rejected.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenPlayerDetailInvariantIsInvalid()
    {
        var payload = new PlayerDetailDto(
            PlayerId: 0,
            FirstName: "Alex",
            LastName: "Athlete",
            DateOfBirth: new DateOnly(2010, 2, 3),
            Gender: null,
            GraduationYear: 2028,
            JerseyNumber: null,
            LifecycleStatus: LifecycleStatus.Active,
            CurrentTraits: [],
            CampaignHistory: []);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies bounded years and defined lifecycle states are required in player details.
    /// </summary>
    /// <param name="graduationYear">The graduation year returned by the server.</param>
    /// <param name="lifecycleStatus">The lifecycle status returned by the server.</param>
    [Theory]
    [InlineData(1999, LifecycleStatus.Active)]
    [InlineData(2028, (LifecycleStatus)99)]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenPlayerStateIsInvalid(
        int graduationYear,
        LifecycleStatus lifecycleStatus)
    {
        var payload = new PlayerDetailDto(
            42,
            "Alex",
            "Athlete",
            new DateOnly(2010, 2, 3),
            null,
            graduationYear,
            null,
            lifecycleStatus,
            [],
            []);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies nullable jersey numbers retain the shared range in player details.
    /// </summary>
    /// <param name="jerseyNumber">The invalid jersey number returned by the server.</param>
    [Theory]
    [InlineData(-1)]
    [InlineData(10000)]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenJerseyNumberIsOutOfRange(
        int jerseyNumber)
    {
        var payload = new PlayerDetailDto(
            42,
            "Alex",
            "Athlete",
            new DateOnly(2010, 2, 3),
            null,
            2028,
            jerseyNumber,
            LifecycleStatus.Active,
            [],
            []);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies player details reject undefined gender values.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenGenderIsUndefined()
    {
        var payload = new PlayerDetailDto(
            42,
            "Alex",
            "Athlete",
            new DateOnly(2010, 2, 3),
            (Gender)99,
            2028,
            null,
            LifecycleStatus.Active,
            [],
            []);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies history rows permit empty note and tag-application collections.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsDetail_WhenHistoryNestedCollectionsAreEmpty()
    {
        var history = new PlayerCampaignHistoryDto(
            PlayerCampaignAssignmentId: 11,
            CampaignId: 12,
            CampaignName: "Spring Tryouts",
            CampaignStatus: CampaignStatus.Closed,
            CampaignStartDate: new DateOnly(2025, 3, 1),
            TryoutNumber: null,
            PlacementOutcome: PlacementOutcome.NotSelected,
            Team: null,
            Notes: [],
            TagApplications: []);
        var payload = new PlayerDetailDto(
            PlayerId: 42,
            FirstName: "Alex",
            LastName: "Athlete",
            DateOfBirth: new DateOnly(2010, 2, 3),
            Gender: null,
            GraduationYear: 2028,
            JerseyNumber: null,
            LifecycleStatus: LifecycleStatus.Active,
            CurrentTraits: [],
            CampaignHistory: [history]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        var returnedHistory = result.Value.CampaignHistory.Single();
        returnedHistory.Notes.ShouldNotBeNull();
        returnedHistory.Notes.ShouldBeEmpty();
        returnedHistory.TagApplications.ShouldNotBeNull();
        returnedHistory.TagApplications.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies null elements in nested history collections are rejected.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenHistoryContainsNullNote()
    {
        const string payload = """
            {
              "playerId": 42,
              "firstName": "Alex",
              "lastName": "Athlete",
              "dateOfBirth": "2010-02-03",
              "gender": null,
              "graduationYear": 2028,
              "jerseyNumber": null,
              "lifecycleStatus": 0,
              "currentTraits": [],
              "campaignHistory": [{
                "playerCampaignAssignmentId": 11,
                "campaignId": 12,
                "campaignName": "Spring Tryouts",
                "campaignStatus": 1,
                "campaignStartDate": "2025-03-01",
                "tryoutNumber": null,
                "placementOutcome": 1,
                "team": null,
                "notes": [null],
                "tagApplications": []
              }]
            }
            """;
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(payload, System.Text.Encoding.UTF8, "application/json")
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies nested evaluation notes retain newest-first ordering.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenNotesAreOutOfOrder()
    {
        var older = new PlayerEvaluationNoteDto(
            1,
            "Older",
            5,
            "Coach",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = older with
        {
            NoteId = 2,
            Content = "Newer",
            CreatedAt = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero)
        };
        var history = new PlayerCampaignHistoryDto(
            11,
            12,
            "Spring Tryouts",
            CampaignStatus.Closed,
            new DateOnly(2025, 3, 1),
            null,
            PlacementOutcome.NotSelected,
            null,
            [older, newer],
            []);
        var payload = new PlayerDetailDto(
            42,
            "Alex",
            "Athlete",
            new DateOnly(2010, 2, 3),
            null,
            2028,
            null,
            LifecycleStatus.Active,
            [],
            [history]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies nested tag applications retain newest-first ordering.
    /// </summary>
    [Fact]
    public async Task GetPlayerDetailAsync_ReturnsServerError_WhenTagApplicationsAreOutOfOrder()
    {
        var older = new PlayerTagApplicationDto(
            1,
            2,
            "Speed",
            "#001122",
            false,
            5,
            "Coach",
            new DateTimeOffset(2025, 1, 1, 0, 0, 0, TimeSpan.Zero));
        var newer = older with
        {
            CampaignTagApplicationId = 2,
            AppliedAt = new DateTimeOffset(2025, 1, 2, 0, 0, 0, TimeSpan.Zero)
        };
        var history = new PlayerCampaignHistoryDto(
            11,
            12,
            "Spring Tryouts",
            CampaignStatus.Closed,
            new DateOnly(2025, 3, 1),
            null,
            PlacementOutcome.NotSelected,
            null,
            [],
            [older, newer]);
        var payload = new PlayerDetailDto(
            42,
            "Alex",
            "Athlete",
            new DateOnly(2010, 2, 3),
            null,
            2028,
            null,
            LifecycleStatus.Active,
            [],
            [history]);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerDetailService(httpClient).GetPlayerDetailAsync(
            42,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }
}
