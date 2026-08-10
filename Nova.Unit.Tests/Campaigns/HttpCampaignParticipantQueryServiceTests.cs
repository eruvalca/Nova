using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Nova.Client.Services;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class HttpCampaignParticipantQueryServiceTests
{
    [Fact]
    public async Task GetParticipantRosterAsync_GeneratesRepeatedQueryValuesAndAcceptsBoundedPayload()
    {
        HttpRequestMessage? capturedRequest = null;
        var handler = new RecordingHandler(async request =>
        {
            capturedRequest = request;
            var payload = new PagedResult<CampaignParticipantRosterItem>(
                [new CampaignParticipantRosterItem(
                    101,
                    202,
                    "Avery Adams",
                    2028,
                    7,
                    PlacementOutcome.Assigned,
                    new CampaignParticipantTeamSummaryDto(301, "Alpha"),
                    [new CampaignParticipantTagSummaryDto(401, "Blue", "Blue", false)])],
                2,
                1,
                3);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(payload) };
        });

        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetParticipantRosterAsync(new GetCampaignParticipantRosterInput
        {
            CampaignId = 42,
            GraduationYears = [2028, 2029],
            TagDefinitionIds = [11, 22],
            Page = 2,
            PageSize = 1
        }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.Page.ShouldBe(2);
        result.Value.PageSize.ShouldBe(1);
        result.Value.Items.Count.ShouldBe(1);
        capturedRequest.ShouldNotBeNull();
        capturedRequest!.RequestUri.ShouldNotBeNull();
        capturedRequest.RequestUri!.Query.ShouldContain("graduationYears=2028");
        capturedRequest.RequestUri.Query.ShouldContain("graduationYears=2029");
        capturedRequest.RequestUri.Query.ShouldContain("tagDefinitionIds=11");
        capturedRequest.RequestUri.Query.ShouldContain("tagDefinitionIds=22");
        capturedRequest.RequestUri.Query.ShouldContain("page=2");
        capturedRequest.RequestUri.Query.ShouldContain("pageSize=1");
    }

    [Fact]
    public async Task GetParticipantDetailAsync_ReturnsServerError_ForMalformedNestedPayload()
    {
        var handler = new RecordingHandler(_ =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new CampaignParticipantDetailDto(
                    101,
                    202,
                    "Avery Adams",
                    2028,
                    7,
                    PlacementOutcome.Assigned,
                    new CampaignParticipantTeamSummaryDto(301, "Alpha"),
                    DateTimeOffset.UtcNow,
                    null,
                    CampaignStatus.Active,
                    Guid.NewGuid(),
                    [new CampaignParticipantNoteDto(1, "", "A Member", DateTimeOffset.UtcNow)],
                    [new CampaignParticipantTagApplicationDto(0, 401, "Blue", "Blue", false, "", DateTimeOffset.UtcNow)],
                    new CampaignParticipantCapabilitiesDto(true, true, true, true, true, true, true)))
            }));

        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetParticipantDetailAsync(new GetCampaignParticipantDetailInput
        {
            CampaignId = 42,
            PlayerCampaignAssignmentId = 101
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetParticipantDetailAsync_ReturnsServerError_ForNullOrBlankNestedTagData()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new CampaignParticipantDetailDto(
                101,
                202,
                "Avery Adams",
                2028,
                7,
                PlacementOutcome.Assigned,
                new CampaignParticipantTeamSummaryDto(301, "Alpha"),
                DateTimeOffset.UtcNow,
                null,
                CampaignStatus.Active,
                Guid.NewGuid(),
                [new CampaignParticipantNoteDto(1, "Hello", "A Member", DateTimeOffset.UtcNow)],
                [null!, new CampaignParticipantTagApplicationDto(2, 401, "Blue", string.Empty, false, "A Member", DateTimeOffset.UtcNow)],
                new CampaignParticipantCapabilitiesDto(true, true, true, true, true, true, true)))
        };
        var handler = new RecordingHandler(_ => Task.FromResult(response));

        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetParticipantDetailAsync(new GetCampaignParticipantDetailInput
        {
            CampaignId = 42,
            PlayerCampaignAssignmentId = 101
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetParticipantRosterAsync_ReturnsServerError_WhenSuccessBodyIsInvalidJson()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
        };
        var handler = new RecordingHandler(_ => Task.FromResult(response));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetParticipantRosterAsync(new GetCampaignParticipantRosterInput
        {
            CampaignId = 42,
            Page = 1,
            PageSize = 50
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetParticipantRosterAsync_ReturnsServerError_WhenPageSizeIsExceeded()
    {
        var payload = new PagedResult<CampaignParticipantRosterItem>(
            [
                new CampaignParticipantRosterItem(
                    101,
                    202,
                    "Avery Adams",
                    2028,
                    7,
                    PlacementOutcome.Assigned,
                    new CampaignParticipantTeamSummaryDto(301, "Alpha"),
                    [new CampaignParticipantTagSummaryDto(401, "Blue", "Blue", false)]),
                new CampaignParticipantRosterItem(
                    102,
                    203,
                    "Brett Baker",
                    2029,
                    8,
                    PlacementOutcome.Undecided,
                    new CampaignParticipantTeamSummaryDto(302, "Beta"),
                    [])
            ],
            1,
            1,
            2);

        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
        var handler = new RecordingHandler(_ => Task.FromResult(response));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetParticipantRosterAsync(new GetCampaignParticipantRosterInput
        {
            CampaignId = 42,
            Page = 1,
            PageSize = 1
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetParticipantDetailAsync_ReturnsProblem_WhenServerReturnsProblemDetails()
    {
        var problemDetails = new ProblemDetails
        {
            Title = "Not Found",
            Detail = "Participant not found.",
            Status = (int)HttpStatusCode.NotFound
        };
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(problemDetails)
        };
        var handler = new RecordingHandler(_ => Task.FromResult(response));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetParticipantDetailAsync(new GetCampaignParticipantDetailInput
        {
            CampaignId = 42,
            PlayerCampaignAssignmentId = 101
        }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => callback(request);
    }
}
