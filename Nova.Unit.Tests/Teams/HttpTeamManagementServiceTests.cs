using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services;
using Nova.Shared.Teams;
using Shouldly;

namespace Nova.Unit.Tests.Teams;

/// <summary>
/// Verifies route and response handling for the typed team HTTP client.
/// </summary>
public sealed class HttpTeamManagementServiceTests
{
    [Fact]
    public async Task Create_SendsPostToTeamRoute_AndReadsDto()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created)
        {
            Content = JsonContent.Create(new TeamDto
            {
                TeamId = 7,
                ClubId = 42,
                Name = "U16",
                GraduationYear = 2028,
                LifecycleStatus = Nova.Shared.Enums.LifecycleStatus.Active
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamManagementService(http).CreateAsync(
            new CreateTeamInput { Name = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/teams");
    }

    [Fact]
    public async Task Update_SendsPutToTeamRoute_AndReadsDto()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new TeamDto
            {
                TeamId = 7,
                ClubId = 42,
                Name = "U16",
                GraduationYear = 2028,
                LifecycleStatus = Nova.Shared.Enums.LifecycleStatus.Active
            })
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpTeamManagementService(http).UpdateAsync(
            new UpdateTeamInput { TeamId = 7, Name = "U16", GraduationYear = 2028 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.LastRequest!.Method.ShouldBe(HttpMethod.Put);
        handler.LastRequest.RequestUri!.PathAndQuery.ShouldBe("/api/teams/7");
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
