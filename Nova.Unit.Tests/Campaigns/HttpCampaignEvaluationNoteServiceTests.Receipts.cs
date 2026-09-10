using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class HttpCampaignEvaluationNoteServiceTests
{
    [Theory]
    [InlineData("null-receipt")]
    [InlineData("wrong-operation")]
    [InlineData("wrong-subject")]
    [InlineData("empty-version")]
    [InlineData("zero-subject")]
    [InlineData("invalid-time")]
    [InlineData("unbounded-expiry")]
    public async Task AddRejectsIncompleteOrMismatchedImmutableReceiptAsync(string fault)
    {
        var receipt = Receipt();
        receipt = fault switch
        {
            "null-receipt" => null!,
            "wrong-operation" => receipt with { OperationId = Guid.CreateVersion7() },
            "wrong-subject" => receipt with { PlayerCampaignAssignmentId = 101 },
            "zero-subject" => receipt with { PlayerCampaignAssignmentId = 0 },
            "invalid-time" => receipt with { CommittedAt = DateTimeOffset.MinValue },
            "unbounded-expiry" => receipt with { RecoveryExpiresAt = receipt.CommittedAt.AddHours(25) },
            _ => receipt
        };
        using var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new EvaluationNoteMutationSuccess(7, string.Equals(fault, "empty-version", StringComparison.Ordinal) ? Guid.Empty : _version, receipt)) };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpCampaignEvaluationNoteService(http).AddAsync(ValidAddInput(), TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task DeleteDispatchesOriginalVersionAndOperationAndRejectsWrongDeletedVersionAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new EvaluationNoteMutationSuccess(7, Guid.NewGuid(), Receipt())) };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpCampaignEvaluationNoteService(http).DeleteAsync(new DeleteEvaluationNoteInput { NoteId = 7, ExpectedVersion = _version, OperationId = _operationId }, TestContext.Current.CancellationToken);
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        handler.LastRequestBody.ShouldNotBeNull();
        handler.LastRequestBody.ShouldContain(_operationId.ToString());
        handler.LastRequestBody.ShouldContain(_version.ToString());
    }
}
