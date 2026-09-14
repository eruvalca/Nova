using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Teams;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Verifies route and response handling for the team-roster HTTP client.
/// </summary>
public sealed class HttpTeamRosterServiceTests
{
    [Fact]
    public async Task GetRosterSendsFiltersToTeamRouteAndReadsRowsAsync()
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
        using var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamRosterService(http).GetRosterAsync(
            new GetTeamRosterInput { Search = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Single().ActivePlacementCount.ShouldBe(3);
        handler.LastRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/teams?search=U16&graduationYear=2028");
    }

    /// <summary>
    /// Verifies the bounded limit reaches the team roster route.
    /// </summary>
    [Fact]
    public async Task GetRosterSendsLimitToTeamRouteAsync()
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
                    ActivePlacementCount = 0
                }
            })
        };
        using var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamRosterService(http).GetRosterAsync(
            new GetTeamRosterInput { Limit = 200 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.RequestUri!.PathAndQuery.ShouldBe("/api/teams?limit=200");
    }

    /// <summary>
    /// Verifies the inclusive maximum graduation year reaches the team roster route, because the placement
    /// surface asks for a cutoff rather than one cohort.
    /// </summary>
    [Fact]
    public async Task GetRosterSendsMaximumGraduationYearToTeamRouteAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new TeamRosterItem
                {
                    TeamId = 7,
                    Name = "U16",
                    GraduationYear = 2030,
                    LifecycleStatus = LifecycleStatus.Active,
                    ActivePlacementCount = 0
                }
            })
        };
        using var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamRosterService(http).GetRosterAsync(
            new GetTeamRosterInput { LifecycleStatus = "active", MaxGraduationYear = 2032, Limit = 200 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.RequestUri!.PathAndQuery
            .ShouldBe("/api/teams?lifecycleStatus=active&limit=200&maxGraduationYear=2032");
    }

    /// <summary>
    /// Verifies a row above the requested cutoff is rejected, so a server that ignored the range cannot be
    /// mistaken for one that applied it.
    /// </summary>
    [Fact]
    public async Task GetRosterAsyncReturnsServerErrorWhenRowIsAboveTheMaximumGraduationYearAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new[]
            {
                new TeamRosterItem
                {
                    TeamId = 7,
                    Name = "U16",
                    GraduationYear = 2033,
                    LifecycleStatus = LifecycleStatus.Active,
                    ActivePlacementCount = 0
                }
            })
        };
        using var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamRosterService(http).GetRosterAsync(
            new GetTeamRosterInput { MaxGraduationYear = 2032 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>
    /// Verifies a valid empty roster remains a successful response.
    /// </summary>
    [Fact]
    public async Task GetRosterAsyncReturnsEmptyListWhenSuccessBodyIsEmptyArrayAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("[]", Encoding.UTF8, "application/json")
        };
        using var handler = new CapturingHandler(response);
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
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{not-json")]
    public async Task GetRosterAsyncReturnsServerErrorWhenSuccessBodyIsInvalidAsync(string body)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        };
        using var handler = new CapturingHandler(response);
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
    public async Task GetRosterAsyncReturnsServerErrorWhenRosterElementIsInvalidAsync()
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
        using var handler = new CapturingHandler(response);
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
    public async Task GetRosterAsyncReturnsServerErrorWhenGraduationYearIsOutOfRangeAsync()
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
        using var handler = new CapturingHandler(response);
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
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(LifecycleStatus.Active, 2029)]
    [InlineData(LifecycleStatus.Archived, 2028)]
    public async Task GetRosterAsyncReturnsServerErrorWhenRowDoesNotMatchExactFiltersAsync(
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
        using var handler = new CapturingHandler(response);
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
    public async Task GetRosterAsyncReturnsValidationProblemBeforeSendingInvalidInputAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var handler = new CapturingHandler(response);
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
