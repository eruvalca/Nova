using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Nova.Client.Services;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class HttpCampaignParticipantQueryServiceTests
{
    [Fact]
    public async Task GetParticipantRosterAsyncGeneratesRepeatedQueryValuesAndAcceptsBoundedPayloadAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        using var handler = new RecordingHandler(async request =>
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
        capturedRequest.RequestUri.ShouldNotBeNull();
        capturedRequest.RequestUri.Query.ShouldContain("graduationYears=2028");
        capturedRequest.RequestUri.Query.ShouldContain("graduationYears=2029");
        capturedRequest.RequestUri.Query.ShouldContain("tagDefinitionIds=11");
        capturedRequest.RequestUri.Query.ShouldContain("tagDefinitionIds=22");
        capturedRequest.RequestUri.Query.ShouldContain("page=2");
        capturedRequest.RequestUri.Query.ShouldContain("pageSize=1");
    }

    [Fact]
    public async Task GetParticipantDetailAsyncReturnsServerErrorForMalformedNestedPayloadAsync()
    {
        using var handler = new RecordingHandler(_ =>
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
                    [new CampaignParticipantNoteDto(1, "", "A Member", DateTimeOffset.UtcNow, null, true, true)],
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
    public async Task GetParticipantDetailAsyncReturnsServerErrorForNullOrBlankNestedTagDataAsync()
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
                [new CampaignParticipantNoteDto(1, "Hello", "A Member", DateTimeOffset.UtcNow, null, true, true)],
                [null!, new CampaignParticipantTagApplicationDto(2, 401, "Blue", string.Empty, false, "A Member", DateTimeOffset.UtcNow, true)],
                new CampaignParticipantCapabilitiesDto(true, true, true, true)))
        };
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025

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
    public async Task GetParticipantDetailAsyncReturnsServerErrorWhenPlacementAndOrderingContractIsViolatedAsync()
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
                    new CampaignParticipantNoteDto(2, "Older note", "A Member", DateTimeOffset.UtcNow.AddMinutes(-5), null, true, true),
                    new CampaignParticipantNoteDto(1, "Newer note", "A Member", DateTimeOffset.UtcNow, null, true, true)
                ],
                [new CampaignParticipantTagApplicationDto(2, 401, "Blue", "Blue", false, "A Member", DateTimeOffset.UtcNow.AddMinutes(-2), true)],
                new CampaignParticipantCapabilitiesDto(true, true, true, true)))
        };
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025

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
    public async Task GetParticipantDetailAsyncReturnsSuccessWhenNoteModifiedAtFollowsCreatedAtAsync()
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
            [new CampaignParticipantNoteDto(1, "Hello", "A Member", now, now.AddMinutes(5), true, true)],
            [new CampaignParticipantTagApplicationDto(1, 401, "Blue", "Blue", false, "A Member", now, true)],
            new CampaignParticipantCapabilitiesDto(true, true, true, true));
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
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
        result.Value.Notes.Count.ShouldBe(1);
        result.Value.Notes[0].ModifiedAt.ShouldBe(now.AddMinutes(5));
    }

    [Fact]
    public async Task GetParticipantDetailAsyncReturnsServerErrorWhenNoteModifiedAtPrecedesCreatedAtAsync()
    {
        var now = DateTimeOffset.UtcNow;
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
                now,
                null,
                CampaignStatus.Active,
                Guid.NewGuid(),
                [new CampaignParticipantNoteDto(1, "Hello", "A Member", now, now.AddMinutes(-5), true, true)],
                [new CampaignParticipantTagApplicationDto(1, 401, "Blue", "Blue", false, "A Member", now, true)],
                new CampaignParticipantCapabilitiesDto(true, true, true, true)))
        };
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
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
#pragma warning disable MA0051 // Keep the complete arrangement, operation, and assertions together as one regression scenario.
    public async Task GetParticipantDetailAsyncReturnsSuccessForValidPayloadAsync()
#pragma warning restore MA0051
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
                new CampaignParticipantNoteDto(2, "Newer note", "A Member", now, null, true, true),
                new CampaignParticipantNoteDto(1, "Older note", "A Member", now.AddMinutes(-5), null, true, true)
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
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
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
        result.Value.Team.TeamId.ShouldBe(301);
        result.Value.Team.TeamName.ShouldBe("Alpha");
        result.Value.CampaignStatus.ShouldBe(CampaignStatus.Active);
        result.Value.ConcurrencyToken.ShouldNotBe(Guid.Empty);
        result.Value.Capabilities.ShouldNotBeNull();
        result.Value.Capabilities.CanAddNote.ShouldBeTrue();
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

    /// <summary>
    /// Verifies participant detail accepts a valid administrator-visible Draft payload.
    /// </summary>
    [Fact]
    public async Task GetParticipantDetailAsyncReturnsSuccessForDraftPayloadAsync()
    {
        var payload = new CampaignParticipantDetailDto(
            101,
            202,
            "Avery Adams",
            2028,
            null,
            PlacementOutcome.Undecided,
            null,
            DateTimeOffset.UtcNow,
            null,
            CampaignStatus.Draft,
            Guid.NewGuid(),
            [],
            [],
            new CampaignParticipantCapabilitiesDto(false, false, false, true));
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
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
        result.Value.CampaignStatus.ShouldBe(CampaignStatus.Draft);
        result.Value.Capabilities.CanEditPlacement.ShouldBeFalse();
        result.Value.Capabilities.CanAddNote.ShouldBeFalse();
        result.Value.Capabilities.CanApplyTag.ShouldBeFalse();
        result.Value.Capabilities.CanArchiveTagDefinitions.ShouldBeTrue();
    }

    /// <summary>
    /// Verifies participant detail rejects campaign lifecycle values outside the shared enum.
    /// </summary>
    [Fact]
    public async Task GetParticipantDetailAsyncReturnsServerErrorWhenCampaignStatusIsUndefinedAsync()
    {
        var payload = new CampaignParticipantDetailDto(
            101,
            202,
            "Avery Adams",
            2028,
            null,
            PlacementOutcome.Undecided,
            null,
            DateTimeOffset.UtcNow,
            null,
            (CampaignStatus)99,
            Guid.NewGuid(),
            [],
            [],
            new CampaignParticipantCapabilitiesDto(false, false, false, true));
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(payload)
        };
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
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
    public async Task GetParticipantRosterAsyncReturnsServerErrorWhenSuccessBodyIsInvalidJsonAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{not-json", Encoding.UTF8, "application/json")
        };
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
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
    public async Task GetParticipantRosterAsyncReturnsServerErrorWhenPageSizeIsExceededAsync()
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
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
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
    public async Task GetParticipantDetailAsyncReturnsProblemWhenServerReturnsProblemDetailsAsync()
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
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
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

    [Fact]
    public async Task GetRosterGraduationYearsAsyncReturnsSuccessAndBuildsRouteForValidPayloadAsync()
    {
        HttpRequestMessage? capturedRequest = null;
        using var handler = new RecordingHandler(request =>
        {
            capturedRequest = request;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new List<int> { 2028, 2029 })
            });
        });

        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe([2028, 2029]);
        capturedRequest.ShouldNotBeNull();
        capturedRequest.RequestUri.ShouldNotBeNull();
        capturedRequest.RequestUri.AbsolutePath.ShouldBe("/api/campaigns/42/participants/graduation-years");
    }

    [Fact]
    public async Task GetRosterGraduationYearsAsyncReturnsServerErrorWhenYearsAreUnsortedAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<int> { 2029, 2028 })
        };
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetRosterGraduationYearsAsyncReturnsServerErrorWhenYearsContainDuplicatesAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<int> { 2028, 2028 })
        };
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetRosterGraduationYearsAsyncReturnsServerErrorWhenYearIsNonPositiveAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<int> { 0, 2028 })
        };
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetRosterGraduationYearsAsyncReturnsSuccessWhenResponseHasManyYearsAsync()
    {
        var years = Enumerable.Range(2020, 25).ToList();
        using var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(years)
        };
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.ShouldBe(years);
    }

    [Fact]
    public async Task GetRosterGraduationYearsAsyncReturnsProblemWhenServerReturnsProblemDetailsAsync()
    {
        var problemDetails = new ProblemDetails
        {
            Title = "Not Found",
            Detail = "Campaign not found.",
            Status = (int)HttpStatusCode.NotFound
        };
        using var response = new HttpResponseMessage(HttpStatusCode.NotFound)
        {
            Content = JsonContent.Create(problemDetails)
        };
#pragma warning disable CA2025 // This handler returns an already-completed task; the request is awaited before the test disposes the response.
        using var handler = new RecordingHandler(_ => Task.FromResult(response));
#pragma warning restore CA2025
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = 42 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.NotFound);
    }

    [Fact]
    public async Task GetRosterGraduationYearsAsyncReturnsValidationForNonPositiveCampaignIdAsync()
    {
        using var handler = new RecordingHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new List<int>())
        }));
        using var http = new HttpClient(handler)
        {
            BaseAddress = new Uri("https://example.com")
        };
        var service = new HttpCampaignParticipantQueryService(http);

        var result = await service.GetRosterGraduationYearsAsync(
            new GetCampaignParticipantGraduationYearsInput { CampaignId = 0 },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    private sealed class RecordingHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => callback(request);
    }
}
