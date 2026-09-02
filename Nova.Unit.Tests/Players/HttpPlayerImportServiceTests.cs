using System.Net;
using System.Net.Http.Json;
using System.Text;
using Nova.Client.Services.Players;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Shouldly;

namespace Nova.Unit.Tests.Players;

/// <summary>Tests the strict WebAssembly player-import HTTP boundary.</summary>
public sealed class HttpPlayerImportServiceTests
{
    [Fact]
    public async Task PreviewAsync_SendsMultipartFile_AndReturnsValidPreview()
    {
        var preview = ValidPreview();
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(preview)
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var content = Encoding.UTF8.GetBytes("csv-content");

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            new PlayerImportUpload(content, "players.csv", "text/csv"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.Method.ShouldBe(HttpMethod.Post);
        handler.PathAndQuery.ShouldBe(PlayerEndpoints.ImportPreview);
        handler.MediaType.ShouldBe("multipart/form-data");
        handler.Body.ShouldContain("name=file");
        handler.Body.ShouldContain("filename=players.csv");
        handler.Body.ShouldContain("csv-content");
    }

    [Fact]
    public async Task PreviewAsync_DoesNotSendRequest_WhenFileExceedsSharedBound()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            new PlayerImportUpload(new byte[PlayerImportConstraints.MaxFileBytes + 1], "players.csv", "text/csv"),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        handler.Method.ShouldBeNull();
    }

    [Fact]
    public async Task PreviewAsync_PropagatesValidationProblemDetails()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.BadRequest)
        {
            Content = JsonContent.Create(new
            {
                detail = "Please correct the validation errors.",
                errors = new Dictionary<string, string[]> { ["file"] = ["The CSV is malformed."] }
            })
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            ValidUpload(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        result.Problem.Errors!["file"].ShouldBe(["The CSV is malformed."]);
    }

    [Fact]
    public async Task PreviewAsync_PreservesCancellation()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Should.ThrowAsync<OperationCanceledException>(() =>
            new HttpPlayerImportService(http).PreviewAsync(ValidUpload(), cancellation.Token));
    }

    [Fact]
    public async Task PreviewAsync_ReturnsServerError_WhenCountsAreInconsistent()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidPreview() with { ReadyRows = 0 })
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            ValidUpload(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsServerError_WhenRowsAreOutOfOrder()
    {
        var preview = ValidPreview() with
        {
            TotalRows = 2,
            ReadyRows = 2,
            Rows = [ValidPreview().Rows[0] with { SourceRowNumber = 3 }, ValidPreview().Rows[0]]
        };
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(preview)
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            ValidUpload(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsServerError_WhenSuccessfulPreviewExceedsSharedRowBound()
    {
        var preview = ValidPreview() with
        {
            TotalRows = PlayerImportConstraints.MaxDataRows + 1,
            ReadyRows = PlayerImportConstraints.MaxDataRows + 1
        };
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(preview)
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            ValidUpload(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task GetTemplateAsync_ReturnsBytesAndFilename_ForCsvResponse()
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent([0xEF, 0xBB, 0xBF, (byte)'a'])
        };
        response.Content.Headers.ContentType = new("text/csv") { CharSet = "utf-8" };
        response.Content.Headers.ContentDisposition = new("attachment") { FileName = "template.csv" };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).GetTemplateAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DownloadFileName.ShouldBe("template.csv");
        handler.PathAndQuery.ShouldBe(PlayerEndpoints.ImportTemplate);
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null")]
    [InlineData("")]
    [InlineData("{bad-json")]
    public async Task PreviewAsync_ReturnsServerError_WhenSuccessBodyIsMalformed(string body)
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json")
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            ValidUpload(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    private static PlayerImportUpload ValidUpload() => new(
        Encoding.UTF8.GetBytes("content"),
        "players.csv",
        "text/csv");

    private static PlayerImportPreview ValidPreview()
    {
        var candidate = new CreatePlayerInput
        {
            FirstName = "Alex",
            LastName = "Archer",
            DateOfBirth = new DateOnly(2012, 1, 1),
            GraduationYear = 2030
        };
        return new PlayerImportPreview(
            Guid.CreateVersion7(),
            "protected-token",
            DateTimeOffset.UtcNow.AddMinutes(30),
            1,
            1,
            0,
            0,
            [new PlayerImportPreviewRow(
                2,
                new("Alex", "Archer", "2012-01-01", "", "", "2030"),
                candidate,
                PlayerImportRowStatus.Ready,
                [],
                null)]);
    }

    private sealed class CapturingHandler(HttpResponseMessage response) : HttpMessageHandler
    {
        public HttpMethod? Method { get; private set; }
        public string? PathAndQuery { get; private set; }
        public string? MediaType { get; private set; }
        public string Body { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            Method = request.Method;
            PathAndQuery = request.RequestUri!.PathAndQuery;
            MediaType = request.Content?.Headers.ContentType?.MediaType;
            Body = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            return response;
        }
    }
}
