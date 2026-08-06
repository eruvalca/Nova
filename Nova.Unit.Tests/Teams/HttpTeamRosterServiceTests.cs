using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Features.Teams;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Verifies route and response handling for the team-roster HTTP client.
/// </summary>
public sealed class HttpTeamRosterServiceTests
{
    [Fact]
    public async Task GetRoster_SendsFiltersToTeamRoute_AndReadsRows()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new TeamRosterItem
                {
                    TeamId = 7,
                    Name = "U16",
                    GraduationYear = 2028,
                    LifecycleStatus = LifecycleStatus.Active,
                    ActivePlacementCount = 3
                }
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamRosterService(http).GetRosterAsync(
            new GetTeamRosterInput { Search = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Single().ActivePlacementCount.ShouldBe(3);
        handler.LastRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/teams?search=U16&graduationYear=2028");
    }

    /// <summary>
    /// Verifies a valid empty roster remains a successful response.
    /// </summary>
    [Fact]
    public async Task GetRosterAsync_ReturnsEmptyList_WhenSuccessBodyIsEmptyArray()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamRosterService(http).GetRosterAsync(
            new GetTeamRosterInput(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldNotBeNull();
        result.Value.ShouldBeEmpty();
    }

    /// <summary>
    /// Verifies invalid successful response bodies are surfaced as protocol failures.
    /// </summary>
    /// <param name="body">The invalid successful response body.</param>
    [Theory]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task GetRosterAsync_ReturnsServerError_WhenSuccessBodyIsInvalid(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamRosterService(http).GetRosterAsync(
            new GetTeamRosterInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies roster rows that violate portable invariants are rejected.
    /// </summary>
    [Fact]
    public async Task GetRosterAsync_ReturnsServerError_WhenRosterElementIsInvalid()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new TeamRosterItem
                {
                    TeamId = 7,
                    Name = "U16",
                    GraduationYear = 2028,
                    LifecycleStatus = LifecycleStatus.Active,
                    ActivePlacementCount = -1
                }
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamRosterService(http).GetRosterAsync(
            new GetTeamRosterInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies roster rows always require a graduation year within the shared contract.
    /// </summary>
    [Fact]
    public async Task GetRosterAsync_ReturnsServerError_WhenGraduationYearIsOutOfRange()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new TeamRosterItem
                {
                    TeamId = 7,
                    Name = "U16",
                    GraduationYear = 1999,
                    LifecycleStatus = LifecycleStatus.Active,
                    ActivePlacementCount = 0
                }
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamRosterService(http).GetRosterAsync(
            new GetTeamRosterInput(),
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
    [InlineData(LifecycleStatus.Active, 2029)]
    [InlineData(LifecycleStatus.Archived, 2028)]
    public async Task GetRosterAsync_ReturnsServerError_WhenRowDoesNotMatchExactFilters(
        LifecycleStatus lifecycleStatus,
        int graduationYear)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new TeamRosterItem
                {
                    TeamId = 7,
                    Name = "U16",
                    GraduationYear = graduationYear,
                    LifecycleStatus = lifecycleStatus,
                    ActivePlacementCount = 0
                }
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamRosterService(http).GetRosterAsync(
            new GetTeamRosterInput
            {
                LifecycleStatus = "archived",
                GraduationYear = 2029
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies invalid shared input is rejected before a lossy URL builder can normalize it.
    /// </summary>
    [Fact]
    public async Task GetRosterAsync_ReturnsValidationProblem_BeforeSendingInvalidInput()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamRosterService(http).GetRosterAsync(
            new GetTeamRosterInput { LifecycleStatus = "invalid", GraduationYear = 1999 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        handler.LastRequest.ShouldBeNull();
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
