using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class HttpCampaignEvaluationQueryServiceTests
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task MissingCursorPartReturnsValidationWithoutAnyHttpRequestAsync(bool applications, bool onlyTimestamp)
    {
        var requests = 0;
        using var handler = new Handler(_ => { requests++; return new HttpResponseMessage(HttpStatusCode.OK); });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var service = new HttpCampaignEvaluationQueryService(http);
        var input = new GetEvaluationHistoryInput { CampaignId = 12, PlayerCampaignAssignmentId = 34, BeforeCreatedAt = onlyTimestamp ? DateTimeOffset.UtcNow : null, BeforeId = onlyTimestamp ? null : 5 };
        var problem = applications
            ? (await service.GetApplicationsAsync(input, TestContext.Current.CancellationToken)).Problem
            : (await service.GetNotesAsync(input, TestContext.Current.CancellationToken)).Problem;
        problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        requests.ShouldBe(0);
    }

    private sealed class Handler(Func<HttpRequestMessage, HttpResponseMessage> response) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(response(request));
    }

    [Fact]
    public async Task NotesReadPreservesVersionsEditedTimestampAndExclusiveCursorAsync()
    {
        var before = DateTimeOffset.UtcNow;
        var version = Guid.NewGuid();
        var input = new GetEvaluationHistoryInput { CampaignId = 12, PlayerCampaignAssignmentId = 34, BeforeCreatedAt = before, BeforeId = 9 };
        using var handler = new Handler(request =>
        {
            request.RequestUri!.PathAndQuery.ShouldBe(CampaignEndpoints.EvaluationNotesUrl(input));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = JsonContent.Create(new EvaluationHistoryPage<CampaignParticipantNoteDto>([
                    new(8, "Newer note", "Member", before, before.AddMinutes(5), true, true, version),
                    new(7, "Older note", "Other", before.AddMinutes(-1), null, false, false, Guid.NewGuid())], null))
            };
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var result = await new HttpCampaignEvaluationQueryService(http).GetNotesAsync(input, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.Items.Count.ShouldBe(2);
        result.Value.Items[0].NoteId.ShouldBe(8);
        result.Value.Items[0].Version.ShouldBe(version);
        result.Value.Items[0].ModifiedAt.ShouldBe(before.AddMinutes(5));
        result.Value.Items[1].CanEdit.ShouldBeFalse();
        result.Value.Next.ShouldBeNull();
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("null")]
    [InlineData("json")]
    [InlineData("null-items")]
    [InlineData("null-item")]
    [InlineData("blank-content")]
    [InlineData("blank-author")]
    [InlineData("no-version")]
    [InlineData("edited-before-created")]
    [InlineData("oversized")]
    [InlineData("duplicate")]
    [InlineData("ascending")]
    [InlineData("bad-next")]
    [InlineData("missing-next")]
    [InlineData("missing-items")]
    public async Task NotesReadRejectsMalformedOrInvalidSuccessWithoutManufacturingEvidenceAsync(string fault)
    {
        var now = DateTimeOffset.UtcNow;
        var note = new CampaignParticipantNoteDto(4, "Evidence", "Member", now, null, true, true, Guid.NewGuid());
        IReadOnlyList<CampaignParticipantNoteDto> items = fault switch
        {
            "null-items" => null!,
            "null-item" => [null!],
            "blank-content" => [note with { Content = " " }],
            "blank-author" => [note with { AuthorDisplayName = " " }],
            "no-version" => [note with { Version = Guid.Empty }],
            "edited-before-created" => [note with { ModifiedAt = now.AddSeconds(-1) }],
            "oversized" => Enumerable.Range(1, 21).Select(index => note with { NoteId = 30 - index }).ToList(),
            "duplicate" => [note, note],
            "ascending" => [note, note with { NoteId = 5 }],
            _ => [note]
        };
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = fault switch
            {
                "empty" => new StringContent("", Encoding.UTF8, "application/json"),
                "null" => new StringContent("null", Encoding.UTF8, "application/json"),
                "json" => new StringContent("{bad", Encoding.UTF8, "application/json"),
                "missing-next" => JsonContent.Create(new { items }),
                "missing-items" => new StringContent("{\"next\":null}", Encoding.UTF8, "application/json"),
                _ => JsonContent.Create(new EvaluationHistoryPage<CampaignParticipantNoteDto>(items, string.Equals(fault, "bad-next", StringComparison.Ordinal) ? new EvaluationHistoryCursor(now, 4) : null))
            }
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var result = await new HttpCampaignEvaluationQueryService(http).GetNotesAsync(new GetEvaluationHistoryInput { CampaignId = 12, PlayerCampaignAssignmentId = 34 }, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("[null]")]
    [InlineData("[{\"playerTagId\":1,\"name\":\"Fast\",\"color\":\"#006B6B\"}]")]
    [InlineData("[{\"playerTagId\":1,\"name\":\"Fast\",\"color\":\"#006B6B\",\"applicationId\":0}]")]
    public async Task CatalogRejectsIncompleteSelectedPlayerApplicationStatusAsync(string json)
    {
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var result = await new HttpCampaignEvaluationQueryService(http).GetTagChoicesAsync(new GetCampaignParticipantDetailInput { CampaignId = 12, PlayerCampaignAssignmentId = 34 }, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Theory]
    [InlineData("null-item")]
    [InlineData("empty-color")]
    [InlineData("empty-actor")]
    [InlineData("zero-id")]
    [InlineData("archived-removable")]
    public async Task ApplicationsRejectInvalidNestedEvidenceAsync(string fault)
    {
        var item = new CampaignParticipantTagApplicationDto(4, 8, "Strong", "#006B6B", false, "Member", DateTimeOffset.UtcNow, true);
        item = fault switch
        {
            "null-item" => null!,
            "empty-color" => item with { TagColor = "" },
            "empty-actor" => item with { ActorDisplayName = "" },
            "zero-id" => item with { CampaignTagApplicationId = 0 },
            _ => item with { IsArchived = true }
        };
        using var handler = new Handler(_ => new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new EvaluationHistoryPage<CampaignParticipantTagApplicationDto>([item], null)) });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var result = await new HttpCampaignEvaluationQueryService(http).GetApplicationsAsync(new GetEvaluationHistoryInput { CampaignId = 12, PlayerCampaignAssignmentId = 34 }, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task ApplicationsAndCatalogRetainOriginalActorAndAppliedStatusAsync()
    {
        using var handler = new Handler(request => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = request.RequestUri!.AbsolutePath.EndsWith("tag-choices", StringComparison.Ordinal)
                ? JsonContent.Create(new[] { new EvaluationTagChoice(8, "Strong", "#006B6B", 4), new EvaluationTagChoice(9, "Fast", "#006B6B", null) })
                : JsonContent.Create(new EvaluationHistoryPage<CampaignParticipantTagApplicationDto>([new(4, 8, "Strong", "#006B6B", false, "Original author", DateTimeOffset.UtcNow, false)], null))
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost") };
        var service = new HttpCampaignEvaluationQueryService(http);
        var applications = await service.GetApplicationsAsync(new GetEvaluationHistoryInput { CampaignId = 12, PlayerCampaignAssignmentId = 34 }, TestContext.Current.CancellationToken);
        applications.IsSuccess.ShouldBeTrue();
        applications.Value.Items.Single().ActorDisplayName.ShouldBe("Original author");
        applications.Value.Items.Single().CanRemove.ShouldBeFalse();
        var choices = await service.GetTagChoicesAsync(new GetCampaignParticipantDetailInput { CampaignId = 12, PlayerCampaignAssignmentId = 34 }, TestContext.Current.CancellationToken);
        choices.IsSuccess.ShouldBeTrue();
        choices.Value.Count.ShouldBe(2);
        choices.Value[0].ApplicationId.ShouldBe(4);
        choices.Value[1].ApplicationId.ShouldBeNull();
    }
}
