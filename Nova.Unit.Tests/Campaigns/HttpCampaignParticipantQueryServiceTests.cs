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
                    [new CampaignParticipantNoteDto(1, "", "A Member", DateTimeOffset.UtcNow, true, true)],
                    [new CampaignParticipantTagApplicationDto(0, 401, "Blue", "Blue", false, "", DateTimeOffset.UtcNow, true)],
                    new CampaignParticipantCapabilitiesDto(true, true, true, true)))
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
                [new CampaignParticipantNoteDto(1, "Hello", "A Member", DateTimeOffset.UtcNow, true, true)],
                [null!, new CampaignParticipantTagApplicationDto(2, 401, "Blue", string.Empty, false, "A Member", DateTimeOffset.UtcNow, true)],
                new CampaignParticipantCapabilitiesDto(true, true, true, true)))
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
    public async Task GetParticipantDetailAsync_ReturnsServerError_WhenPlacementAndOrderingContractIsViolated()
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
                null,
                DateTimeOffset.UtcNow,
                DateTimeOffset.UtcNow.AddMinutes(-1),
                CampaignStatus.Active,
                Guid.NewGuid(),
                [
                    new CampaignParticipantNoteDto(2, "Older note", "A Member", DateTimeOffset.UtcNow.AddMinutes(-5), true, true),
                    new CampaignParticipantNoteDto(1, "Newer note", "A Member", DateTimeOffset.UtcNow, true, true)
                ],
                [new CampaignParticipantTagApplicationDto(2, 401, "Blue", "Blue", false, "A Member", DateTimeOffset.UtcNow.AddMinutes(-2), true)],
                new CampaignParticipantCapabilitiesDto(true, true, true, true)))
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
    public async Task GetParticipantDetailAsync_ReturnsSuccess_ForValidPayload()
    {
        var now = DateTimeOffset.UtcNow;
        var payload = new CampaignParticipantDetailDto(
            101,
            202,
            "Avery Adams",
            2028,
            7,
            PlacementOutcome.Assigned,
            new CampaignParticipantTeamSummaryDto(301, "Alpha"),
            now,
            null,
            CampaignStatus.Active,
            Guid.NewGuid(),
            [
                new CampaignParticipantNoteDto(2, "Newer note", "A Member", now, true, true),
                new CampaignParticipantNoteDto(1, "Older note", "A Member", now.AddMinutes(-5), true, true)
            ],
            [
                new CampaignParticipantTagApplicationDto(3, 401, "Blue", "Blue", false, "A Member", now, true),
                new CampaignParticipantTagApplicationDto(2, 402, "Gold", "Gold", false, "A Member", now.AddMinutes(-2), true)
            ],
            new CampaignParticipantCapabilitiesDto(true, true, true, true));
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

        var result = await service.GetParticipantDetailAsync(new GetCampaignParticipantDetailInput
        {
            CampaignId = 42,
            PlayerCampaignAssignmentId = 101
        }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.PlayerCampaignAssignmentId.ShouldBe(101);
        result.Value.PlayerId.ShouldBe(202);
        result.Value.DisplayName.ShouldBe("Avery Adams");
        result.Value.GraduationYear.ShouldBe(2028);
        result.Value.TryoutNumber.ShouldBe(7);
        result.Value.PlacementOutcome.ShouldBe(PlacementOutcome.Assigned);
        result.Value.Team.ShouldNotBeNull();
        result.Value.Team!.TeamId.ShouldBe(301);
        result.Value.Team.TeamName.ShouldBe("Alpha");
        result.Value.CampaignStatus.ShouldBe(CampaignStatus.Active);
        result.Value.ConcurrencyToken.ShouldNotBe(Guid.Empty);
        result.Value.Capabilities.ShouldNotBeNull();
        result.Value.Capabilities!.CanAddNote.ShouldBeTrue();
        result.Value.Capabilities.CanApplyTag.ShouldBeTrue();
        result.Value.Notes.Count.ShouldBe(2);
        result.Value.Notes[0].NoteId.ShouldBe(2);
        result.Value.Notes[0].Content.ShouldBe("Newer note");
        result.Value.Notes[0].CanEdit.ShouldBeTrue();
        result.Value.Notes[0].CanDelete.ShouldBeTrue();
        result.Value.Notes[1].NoteId.ShouldBe(1);
        result.Value.AppliedTags.Count.ShouldBe(2);
        result.Value.AppliedTags[0].CampaignTagApplicationId.ShouldBe(3);
        result.Value.AppliedTags[0].TagName.ShouldBe("Blue");
        result.Value.AppliedTags[0].CanRemove.ShouldBeTrue();
        result.Value.AppliedTags[1].CampaignTagApplicationId.ShouldBe(2);
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
