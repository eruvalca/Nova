using System.Net;
using System.Text;
using Nova.Client.Services;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Players;

public sealed partial class HttpPlayerServiceTests
{
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("{\"activeCount\":0,\"archivedCount\":0,\"graduationYears\":[]}", true)]
    [InlineData("{\"activeCount\":123,\"archivedCount\":4,\"graduationYears\":[2000,2030,2100]}", true)]
    [InlineData("{\"activeCount\":0,\"graduationYears\":[]}", false)]
    [InlineData("{\"archivedCount\":0,\"graduationYears\":[]}", false)]
    [InlineData("{\"activeCount\":0,\"archivedCount\":0}", false)]
    [InlineData("{\"activeCount\":0,\"archivedCount\":0,\"graduationYears\":null}", false)]
    [InlineData("{\"activeCount\":-1,\"archivedCount\":0,\"graduationYears\":[]}", false)]
    [InlineData("{\"activeCount\":0,\"archivedCount\":-1,\"graduationYears\":[]}", false)]
    [InlineData("{\"activeCount\":2,\"archivedCount\":0,\"graduationYears\":[2030,2030]}", false)]
    [InlineData("{\"activeCount\":2,\"archivedCount\":0,\"graduationYears\":[2031,2030]}", false)]
    [InlineData("{\"activeCount\":1,\"archivedCount\":0,\"graduationYears\":[1999]}", false)]
    [InlineData("{\"activeCount\":1,\"archivedCount\":0,\"graduationYears\":[2101]}", false)]
    [InlineData("null", false)]
    [InlineData("", false)]
    [InlineData("{", false)]
    public async Task SummaryRequiresCompleteValidPayloadAsync(string body, bool valid)
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpPlayerService(http).GetPlayerDirectorySummaryAsync(new() { ClubId = 42 }, TestContext.Current.CancellationToken);
        handler.LastRequest!.RequestUri!.AbsolutePath.ShouldBe("/api/clubs/42/players/summary");
        result.IsSuccess.ShouldBe(valid);
        if (!valid) { result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError); }
    }

    [Fact]
    public async Task SummaryRejectsInvalidInputWithoutSendingAsync()
    {
        using var response = new HttpResponseMessage(HttpStatusCode.OK);
        using var handler = new FakeHttpMessageHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpPlayerService(http).GetPlayerDirectorySummaryAsync(new() { ClubId = 0 }, TestContext.Current.CancellationToken);
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        handler.LastRequest.ShouldBeNull();
    }
}
