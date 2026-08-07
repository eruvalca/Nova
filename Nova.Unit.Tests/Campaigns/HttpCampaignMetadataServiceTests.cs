using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

/// <summary>
/// Verifies the WebAssembly campaign metadata update client route and response contract.
/// </summary>
public sealed class HttpCampaignMetadataServiceTests
{
    private sealed class FakeHttpMessageHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        /// <summary>Gets the last request sent by the client.</summary>
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

    /// <summary>
    /// Verifies a successful update PUT to the shared route and deserializes the result.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_PutsToSharedRoute_AndReturnsResult()
    {
        var input = ValidInput();
        var expected = SuccessResult(input.CampaignId);
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(expected)
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignMetadataService(http).UpdateAsync(
            input,
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(expected);
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Put);
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.UpdateCampaignMetadata);
    }

    /// <summary>
    /// Verifies a Conflict ProblemDetails response is propagated correctly.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsConflict_FromProblemDetails()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Conflict)
        {
            Content = JsonContent.Create(new
            {
                title = "Conflict",
                status = 409,
                detail = "Metadata cannot be changed while the campaign is closed."
            })
        };
        var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpCampaignMetadataService(http).UpdateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    /// <summary>
    /// Verifies a null success response body is surfaced as a server error.
    /// </summary>
    [Fact]
    public async Task UpdateAsync_ReturnsServerError_ForNullBody()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("null", Encoding.UTF8, "application/json")
        };
        using var http = new HttpClient(new FakeHttpMessageHandler(response))
        {
            BaseAddress = new Uri("https://localhost/")
        };

        var result = await new HttpCampaignMetadataService(http).UpdateAsync(
            ValidInput(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    private static UpdateCampaignMetadataInput ValidInput() => new()
    {
        CampaignId = 1,
        Name = "Updated Campaign",
        SeasonId = 10,
        StartDate = new DateOnly(2026, 6, 1)
    };

    private static UpdateCampaignMetadataResult SuccessResult(long campaignId) => new(
        CampaignId: campaignId,
        Name: "Updated Campaign",
        StartDate: new DateOnly(2026, 6, 1),
        PlannedEndDate: null,
        Status: CampaignStatus.Active,
        SeasonId: 10,
        SeasonName: "Season 2026");
}
