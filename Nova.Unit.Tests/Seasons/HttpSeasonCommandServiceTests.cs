using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services.Seasons;
using Nova.Shared.Features.Seasons;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Seasons;

/// <summary>Verifies strict season command HTTP-client behavior.</summary>
public sealed class HttpSeasonCommandServiceTests
{
    /// <summary>Verifies malformed successful create payloads become protocol failures.</summary>
    [Fact]
    public async Task CreateAsync_ReturnsServerError_ForNonCurrentSuccessBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new SeasonSummary
            {
                SeasonId = 7,
                Name = "Season",
                StartDate = new DateOnly(2026, 1, 1),
                IsCurrent = false,
                ConcurrencyToken = Guid.NewGuid()
            })
        };
        using var http = new HttpClient(new RecordingHandler(response))
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonCommandService(http).CreateAsync(
            new CreateSeasonInput
            {
                OperationId = Guid.NewGuid(),
                Name = "Season",
                StartDate = new DateOnly(2026, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies update uses the first-class detail route and accepts a valid response.</summary>
    [Fact]
    public async Task UpdateAsync_PutsToSeasonDetailRoute()
    {
        var token = Guid.NewGuid();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonSummary
            {
                SeasonId = 7,
                Name = "Season",
                StartDate = new DateOnly(2026, 1, 1),
                IsCurrent = true,
                ConcurrencyToken = token
            })
        };
        var handler = new RecordingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpSeasonCommandService(http).UpdateAsync(
            7,
            new UpdateSeasonInput
            {
                ExpectedConcurrencyToken = Guid.NewGuid(),
                Name = "Season",
                StartDate = new DateOnly(2026, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Put);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe("/api/seasons/7");
    }

    /// <summary>Verifies a successful metadata response must rotate the expected concurrency token.</summary>
    [Fact]
    public async Task UpdateAsync_ReturnsServerError_WhenConcurrencyTokenDoesNotRotate()
    {
        var token = Guid.NewGuid();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new SeasonSummary
            {
                SeasonId = 7,
                Name = "Season",
                StartDate = new DateOnly(2026, 1, 1),
                IsCurrent = true,
                ConcurrencyToken = token
            })
        };
        using var http = new HttpClient(new RecordingHandler(response))
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonCommandService(http).UpdateAsync(
            7,
            new UpdateSeasonInput
            {
                ExpectedConcurrencyToken = token,
                Name = "Season",
                StartDate = new DateOnly(2026, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Verifies a successful advancement payload must identify a real season transition.</summary>
    [Fact]
    public async Task StartNextAsync_ReturnsServerError_WhenResponseDoesNotAdvance()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new StartNextSeasonResult
            {
                PreviousSeasonId = 7,
                CurrentSeason = new SeasonSummary
                {
                    SeasonId = 7,
                    Name = "Same Season",
                    StartDate = new DateOnly(2026, 1, 1),
                    IsCurrent = true,
                    ConcurrencyToken = Guid.NewGuid()
                }
            })
        };
        var handler = new RecordingHandler(response);
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpSeasonCommandService(http).StartNextAsync(
            new StartNextSeasonInput
            {
                OperationId = Guid.NewGuid(),
                ExpectedCurrentSeasonId = 7,
                Name = "Next",
                StartDate = new DateOnly(2027, 1, 1)
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe(SeasonEndpoints.StartNext);
    }

    /// <summary>Records the request while returning a fixed response.</summary>
    private sealed class RecordingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        /// <summary>Gets the last request.</summary>
        public HttpRequestMessage? LastRequest { get; private set; }

        /// <inheritdoc />
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequest = request;
            return Task.FromResult(response);
        }
    }
}
