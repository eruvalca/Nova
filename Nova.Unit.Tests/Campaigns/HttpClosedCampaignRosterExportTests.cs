using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Nova.Client.Services.Campaigns;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Shouldly;

namespace Nova.Unit.Tests.Campaigns;

public sealed class HttpClosedCampaignRosterExportTests
{
    private const string DefaultFileName = "nova-fall-tryouts-closed-roster.csv";

    [Fact]
    public async Task ExportUsesTheSharedRouteAndReturnsTheValidatedDownloadAsync()
    {
        string? capturedPath = null;
        using var handler = new StubHandler(request =>
        {
            request.Method.ShouldBe(HttpMethod.Get);
            capturedPath = request.RequestUri!.PathAndQuery;
            return CsvResponse();
        });
        using var http = CreateHttp(handler);

        var result = await new HttpEffectivePlacementQueryService(http)
            .ExportClosedCampaignRosterAsync(new() { CampaignId = 42 }, TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        capturedPath.ShouldBe("/api/campaigns/42/closed-roster/export");
        result.Value.FileName.ShouldBe(DefaultFileName);
        result.Value.ContentType.ShouldBe(ClosedCampaignRosterExportConstraints.CsvContentType);
        result.Value.Content.ShouldBe(ValidContent());
    }

    [Fact]
    public async Task ExportRejectsAnInvalidInputWithoutSendingARequestAsync()
    {
        using var handler = new StubHandler(_ => throw new InvalidOperationException("No request should be sent."));
        using var http = CreateHttp(handler);

        var result = await new HttpEffectivePlacementQueryService(http)
            .ExportClosedCampaignRosterAsync(new() { CampaignId = 0 }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
    }

    [Fact]
    public async Task ExportMapsProblemResponsesToTheirProblemKindAsync()
    {
        using var handler = new StubHandler(_ => new HttpResponseMessage(HttpStatusCode.Conflict));
        using var http = CreateHttp(handler);

        var result = await new HttpEffectivePlacementQueryService(http)
            .ExportClosedCampaignRosterAsync(new() { CampaignId = 42 }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Conflict);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("application/json", "utf-8", "attachment", DefaultFileName, null)]
    [InlineData("text/csv", null, "attachment", DefaultFileName, null)]
    [InlineData("text/csv", "utf-16", "attachment", DefaultFileName, null)]
    [InlineData("text/csv", "utf-8", "inline", DefaultFileName, null)]
    [InlineData("text/csv", "utf-8", "attachment", null, null)]
    [InlineData("text/csv", "utf-8", "attachment", "export.csv", null)]
    [InlineData("text/csv", "utf-8", "attachment", "nova-a.csv", null)]
    [InlineData("text/csv", "utf-8", "attachment", "NOVA-a-closed-roster.csv", null)]
    [InlineData("text/csv", "utf-8", "attachment", "nova-a/b-closed-roster.csv", null)]
    [InlineData("text/csv", "utf-8", "attachment", "nova--closed-roster.csv", null)]
    [InlineData("text/csv", "utf-8", "attachment", DefaultFileName, "")]
    [InlineData("text/csv", "utf-8", "attachment", DefaultFileName, "Campaign,Season")]
    [InlineData("text/csv", "utf-8", "attachment", DefaultFileName, "bad header row")]
    public async Task ExportRejectsAMalformedDownloadContractAsync(string mediaType, string? charSet,
        string? dispositionType, string? fileName, string? body)
    {
        using var handler = new StubHandler(_ => CsvResponse(
            body is null ? null : Encoding.UTF8.GetBytes(body), mediaType, charSet, dispositionType, fileName));
        using var http = CreateHttp(handler);

        var result = await new HttpEffectivePlacementQueryService(http)
            .ExportClosedCampaignRosterAsync(new() { CampaignId = 42 }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task ExportRejectsAnOverlongFileNameAsync()
    {
        var longName = "nova-" + new string('a', ClosedCampaignRosterExportConstraints.MaxFileBaseNameCharacters + 1)
            + ClosedCampaignRosterExportConstraints.FileNameSuffix;
        using var handler = new StubHandler(_ => CsvResponse(fileName: longName));
        using var http = CreateHttp(handler);

        var result = await new HttpEffectivePlacementQueryService(http)
            .ExportClosedCampaignRosterAsync(new() { CampaignId = 42 }, TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    private static byte[] ValidContent() => [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(
        string.Join(',', ClosedCampaignRosterExportConstraints.Headers) + "\r\n"
        + "Campaign,Season,Ada,Alpha,7,2028,Assigned,Alpha,Casey Member,1970-01-01T00:00:00.0000000+00:00\r\n")];

    private static HttpResponseMessage CsvResponse(byte[]? content = null, string mediaType = "text/csv",
        string? charSet = "utf-8", string? dispositionType = "attachment", string? fileName = DefaultFileName)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content ?? ValidContent())
        };
        response.Content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
        if (charSet is not null)
        {
            response.Content.Headers.ContentType.CharSet = charSet;
        }
        if (dispositionType is not null)
        {
            var disposition = new ContentDispositionHeaderValue(dispositionType);
            if (fileName is not null)
            {
                disposition.FileName = fileName;
            }
            response.Content.Headers.ContentDisposition = disposition;
        }
        return response;
    }

    private static HttpClient CreateHttp(StubHandler handler)
        => new(handler, disposeHandler: false) { BaseAddress = new Uri("https://example.com") };

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(callback(request));
    }
}
