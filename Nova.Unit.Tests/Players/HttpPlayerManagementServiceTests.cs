using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>
/// Tests route, payload, and required-response handling for <see cref="HttpPlayerManagementService"/>.
/// </summary>
public sealed class HttpPlayerManagementServiceTests
{
    /// <summary>
    /// Verifies create sends the expected request and returns the response player.
    /// </summary>
    [Fact]
    public async Task CreateAsync_SendsPostAndReturnsPlayer_WhenPayloadIsValid()
    {
        var input = CreateInput();
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(CreatePlayer())
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerManagementService(http).CreateAsync(
            input,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PlayerId.ShouldBe(7);
        handler.Method.ShouldBe(HttpMethod.Post);
        handler.PathAndQuery.ShouldBe("/api/players");
        var sent = JsonSerializer.Deserialize<CreatePlayerInput>(
            handler.RequestBody!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        sent.ShouldNotBeNull();
        sent.ShouldBe(input);
    }

    /// <summary>
    /// Verifies update sends the expected request and returns the response player.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_SendsPutAndReturnsPlayer_WhenPayloadIsValid()
    {
        var input = new UpdatePlayerInput
        {
            PlayerId = 7,
            FirstName = "Alex",
            LastName = "Archer",
            DateOfBirth = new DateOnly(2010, 2, 3),
            GraduationYear = 2028,
            Gender = Gender.Male,
            JerseyNumber = 11
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(CreatePlayer())
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerManagementService(http).UpdateAsync(
            input,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PlayerId.ShouldBe(7);
        handler.Method.ShouldBe(HttpMethod.Put);
        handler.PathAndQuery.ShouldBe("/api/players/7");
        var sent = JsonSerializer.Deserialize<UpdatePlayerInput>(
            handler.RequestBody!,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        sent.ShouldNotBeNull();
        sent.ShouldBe(input);
    }

    /// <summary>
    /// Verifies update rejects a success payload for a different player.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsServerError_WhenResponsePlayerIdDoesNotMatch()
    {
        var input = new UpdatePlayerInput
        {
            PlayerId = 7,
            FirstName = "Alex",
            LastName = "Archer",
            DateOfBirth = new DateOnly(2010, 2, 3),
            GraduationYear = 2028
        };
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(CreatePlayer() with { PlayerId = 8 })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerManagementService(http).UpdateAsync(
            input,
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies invalid successful create-response bodies become server errors.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task CreateAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerManagementService(http).CreateAsync(
            CreateInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies create rejects a response that violates portable player invariants.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ReturnsServerError_WhenPlayerInvariantIsInvalid()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(CreatePlayer() with { PlayerId = 0 })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerManagementService(http).CreateAsync(
            CreateInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies bounded years and defined lifecycle states are required in player responses.
    /// </summary>
    /// <param name="graduationYear">The graduation year returned by the server.</param>
    /// <param name="lifecycleStatus">The lifecycle status returned by the server.</param>
    [Theory]
    [InlineData(1999, LifecycleStatus.Active)]
    [InlineData(2028, (LifecycleStatus)99)]
    public async Task CreateAsync_ReturnsServerError_WhenPlayerStateIsInvalid(
        int graduationYear,
        LifecycleStatus lifecycleStatus)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(CreatePlayer() with
            {
                GraduationYear = graduationYear,
                LifecycleStatus = lifecycleStatus
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerManagementService(http).CreateAsync(
            CreateInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies the client does not reject a date value currently permitted by the shared input contract.
    /// </summary>
    [Fact]
    public async Task CreateAsync_ReturnsPlayer_WhenDateOfBirthIsMinimumValue()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(CreatePlayer() with { DateOfBirth = DateOnly.MinValue })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerManagementService(http).CreateAsync(
            CreateInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DateOfBirth.ShouldBe(DateOnly.MinValue);
    }

    private static CreatePlayerInput CreateInput() => new()
    {
        FirstName = "Alex",
        LastName = "Archer",
        DateOfBirth = new DateOnly(2010, 2, 3),
        GraduationYear = 2028,
        Gender = Gender.Male,
        JerseyNumber = 11
    };

    private static PlayerDto CreatePlayer() => new()
    {
        PlayerId = 7,
        ClubId = 42,
        FirstName = "Alex",
        LastName = "Archer",
        DateOfBirth = new DateOnly(2010, 2, 3),
        GraduationYear = 2028,
        Gender = Gender.Male,
        JerseyNumber = 11,
        LifecycleStatus = LifecycleStatus.Active
    };

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }

        public string? PathAndQuery { get; private set; }

        public string? RequestBody { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri!.PathAndQuery;
            RequestBody = request.Content is null
                ? null
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
