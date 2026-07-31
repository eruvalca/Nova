using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Teams;
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
