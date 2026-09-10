using System.Net;
using System.Net.Http.Json;
using Nova.Client.Services.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed partial class HttpCampaignTagApplicationServiceTests
{
    [Fact]
    public async Task CreateAndApplyPostsOriginalLabelAndReturnsResolvedDefinitionAndReceiptAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new CampaignTagApplicationMutationSuccess(42, 200, true, Receipt())) };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpCampaignTagApplicationService(http).CreateAndApplyAsync(new CreateAndApplyCampaignTagInput
        {
            OperationId = _operationId,
            PlayerCampaignAssignmentId = 100,
            Label = "Good  control"
        }, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        result.Value.PlayerTagId.ShouldBe(200);
        result.Value.AlreadyApplied.ShouldBeTrue();
        handler.LastRequest.ShouldNotBeNull();
        handler.LastRequest.Method.ShouldBe(HttpMethod.Post);
        handler.LastRequestBody.ShouldNotBeNull();
        handler.LastRequestBody.ShouldContain("Good  control");
        handler.LastRequestBody.ShouldContain(_operationId.ToString());
        handler.LastRequest.RequestUri!.AbsolutePath.ShouldBe(CampaignEndpoints.CreateAndApplyCampaignTag);
    }

    [Theory]
    [InlineData("wrong-operation")]
    [InlineData("wrong-participant")]
    [InlineData("wrong-tag")]
    [InlineData("null-receipt")]
    public async Task ApplyRejectsReceiptForAnotherOperationOrSubjectAsync(string fault)
    {
        var receipt = Receipt();
        receipt = fault switch
        {
            "wrong-operation" => receipt with { OperationId = Guid.CreateVersion7() },
            "wrong-participant" => receipt with { PlayerCampaignAssignmentId = 101 },
            "null-receipt" => null!,
            _ => receipt
        };
        using var response = new HttpResponseMessage(HttpStatusCode.Created) { Content = JsonContent.Create(new CampaignTagApplicationMutationSuccess(42, string.Equals(fault, "wrong-tag", StringComparison.Ordinal) ? 201 : 200, false, receipt)) };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpCampaignTagApplicationService(http).ApplyAsync(ValidApplyInput(), TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task RemoveRequiresReceiptAndRejectsAnEmptySuccessAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.NoContent);
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpCampaignTagApplicationService(http).RemoveAsync(ValidRemoveInput(), TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }
}
