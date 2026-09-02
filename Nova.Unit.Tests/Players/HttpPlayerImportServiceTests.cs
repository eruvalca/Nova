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
            new PlayerImportUploadInput
            {
                Content = content,
                FileName = "custom-player-list.csv",
                ContentType = "text/csv"
            },
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        handler.Method.ShouldBe(HttpMethod.Post);
        handler.PathAndQuery.ShouldBe(PlayerEndpoints.ImportPreview);
        handler.MediaType.ShouldBe("multipart/form-data");
        handler.Body.ShouldContain("name=file");
        handler.Body.ShouldContain($"filename={PlayerImportConstraints.TemplateFileName}");
        handler.Body.ShouldContain("csv-content");
    }

    [Fact]
    public async Task PreviewAsync_DoesNotSendRequest_WhenFileExceedsSharedBound()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            new PlayerImportUploadInput
            {
                Content = new byte[PlayerImportConstraints.MaxFileBytes + 1],
                FileName = "players.csv",
                ContentType = "text/csv"
            },
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        handler.Method.ShouldBeNull();
    }

    [Fact]
    public async Task PreviewAsync_DoesNotSendRequest_WhenFilenameContainsNewline()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidPreview())
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            ValidUpload() with { FileName = "players\r\n.csv" },
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
    public async Task PreviewAsync_ReturnsServerError_WhenOperationIdIsNotUuidVersion7()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidPreview() with { OperationId = Guid.NewGuid() })
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            ValidUpload(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsServerError_WhenNestedRowValueIsNull()
    {
        var invalidRow = new PlayerImportPreviewRow(
            2,
            new(null!, "Archer", "invalid", "", "", "2030"),
            Candidate: null,
            PlayerImportRowStatus.Invalid,
            [new(PlayerImportField.FirstName, "First name is required.")],
            Duplicate: null);
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidPreview() with
            {
                ReadyRows = 0,
                InvalidRows = 1,
                Rows = [invalidRow]
            })
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            ValidUpload(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsServerError_WhenInvalidRowValuesAreActuallyValid()
    {
        var row = ValidPreview().Rows[0];
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidPreview() with
            {
                ReadyRows = 0,
                InvalidRows = 1,
                Rows = [row with
                {
                    Candidate = null,
                    Status = PlayerImportRowStatus.Invalid,
                    Errors = [new(PlayerImportField.FirstName, "Arbitrary error.")]
                }]
            })
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            ValidUpload(),
            TestContext.Current.CancellationToken);

        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    [Fact]
    public async Task PreviewAsync_ReturnsServerError_WhenInvalidRowExceedsCellBound()
    {
        var invalidRow = new PlayerImportPreviewRow(
            2,
            new(
                new string('A', PlayerImportConstraints.MaxFieldCharacters + 1),
                "Archer",
                "invalid",
                "",
                "",
                "2030"),
            Candidate: null,
            PlayerImportRowStatus.Invalid,
            [new(PlayerImportField.FirstName, "First name is too long.")],
            Duplicate: null);
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidPreview() with
            {
                ReadyRows = 0,
                InvalidRows = 1,
                Rows = [invalidRow]
            })
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
    public async Task PreviewAsync_ReturnsServerError_WhenSourceRowsContainGap()
    {
        var first = ValidPreview().Rows[0];
        var preview = ValidPreview() with
        {
            TotalRows = 2,
            ReadyRows = 2,
            Rows = [first, first with { SourceRowNumber = 4 }]
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
    public async Task PreviewAsync_ReturnsServerError_WhenReadyOrDuplicateCandidateIsInvalid()
    {
        var invalidInputs = new[]
        {
            ValidPreview().Rows[0].Candidate! with { FirstName = " " },
            ValidPreview().Rows[0].Candidate! with { Gender = (Nova.Shared.Enums.Gender)999 }
        };

        foreach (var invalidInput in invalidInputs)
        {
            foreach (var status in new[] { PlayerImportRowStatus.Ready, PlayerImportRowStatus.Duplicate })
            {
                var duplicate = status == PlayerImportRowStatus.Duplicate
                    ? new PlayerImportDuplicate(PlayerImportDuplicateKind.ExistingActivePlayer, 1, null)
                    : null;
                var preview = ValidPreview() with
                {
                    ReadyRows = status == PlayerImportRowStatus.Ready ? 1 : 0,
                    DuplicateRows = status == PlayerImportRowStatus.Duplicate ? 1 : 0,
                    Rows = [ValidPreview().Rows[0] with { Candidate = invalidInput, Status = status, Duplicate = duplicate }]
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
        }
    }

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(PlayerImportRowStatus.Ready)]
    [InlineData(PlayerImportRowStatus.Duplicate)]
    public async Task PreviewAsync_ReturnsServerError_WhenCandidateDoesNotMatchRawValues(
        PlayerImportRowStatus status)
    {
        var row = ValidPreview().Rows[0];
        var duplicate = status == PlayerImportRowStatus.Duplicate
            ? new PlayerImportDuplicate(PlayerImportDuplicateKind.ExistingActivePlayer, 1, null)
            : null;
        var preview = ValidPreview() with
        {
            ReadyRows = status == PlayerImportRowStatus.Ready ? 1 : 0,
            DuplicateRows = status == PlayerImportRowStatus.Duplicate ? 1 : 0,
            Rows = [row with
            {
                Values = row.Values with { FirstName = "Taylor" },
                Status = status,
                Duplicate = duplicate
            }]
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

    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreviewAsync_ReturnsServerError_WhenEarlierUploadReferenceIsIneligible(
        bool referencedRowIsInvalid)
    {
        var first = ValidPreview().Rows[0];
        var referencedRow = referencedRowIsInvalid
            ? first with
            {
                Candidate = null,
                Status = PlayerImportRowStatus.Invalid,
                Errors = [new(PlayerImportField.GraduationYear, "Graduation year is invalid.")]
            }
            : first;
        var duplicateCandidate = referencedRowIsInvalid
            ? first.Candidate!
            : first.Candidate! with { FirstName = "Taylor" };
        var duplicateValues = referencedRowIsInvalid
            ? first.Values
            : first.Values with { FirstName = "Taylor" };
        var duplicateRow = new PlayerImportPreviewRow(
            3,
            duplicateValues,
            duplicateCandidate,
            PlayerImportRowStatus.Duplicate,
            [],
            new(PlayerImportDuplicateKind.EarlierUploadRow, null, 2));
        var preview = ValidPreview() with
        {
            TotalRows = 2,
            ReadyRows = referencedRowIsInvalid ? 0 : 1,
            InvalidRows = referencedRowIsInvalid ? 1 : 0,
            DuplicateRows = 1,
            Rows = [referencedRow, duplicateRow]
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
    public async Task PreviewAsync_AcceptsNonDefaultExpiry_WithoutUsingClientClock()
    {
        var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(ValidPreview() with { ExpiresAt = DateTimeOffset.UnixEpoch })
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).PreviewAsync(
            ValidUpload(),
            TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
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
            Content = new ByteArrayContent(TemplateBytes())
        };
        response.Content.Headers.ContentType = new("text/csv") { CharSet = "utf-8" };
        response.Content.Headers.ContentDisposition = new("attachment")
        {
            FileName = PlayerImportConstraints.TemplateFileName
        };
        var handler = new CapturingHandler(response);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

        var result = await new HttpPlayerImportService(http).GetTemplateAsync(TestContext.Current.CancellationToken);

        result.IsSuccess.ShouldBeTrue();
        result.Value.DownloadFileName.ShouldBe(PlayerImportConstraints.TemplateFileName);
        result.Value.Content.ShouldBe(TemplateBytes());
        handler.PathAndQuery.ShouldBe(PlayerEndpoints.ImportTemplate);
    }

    [Fact]
    public async Task GetTemplateAsync_ReturnsServerError_WhenTemplateContractDrifts()
    {
        var cases = new[]
        {
            TemplateResponse(TemplateBytes(), "text/csv", null, PlayerImportConstraints.TemplateFileName),
            TemplateResponse(TemplateBytes(), "text/csv", "utf-8", "unsafe.csv"),
            TemplateResponse([0xEF, 0xBB, 0xBF, (byte)'a'], "text/csv", "utf-8", PlayerImportConstraints.TemplateFileName),
            TemplateResponse(TemplateBytes(), "application/octet-stream", "utf-8", PlayerImportConstraints.TemplateFileName)
        };

        foreach (var response in cases)
        {
            using var handler = new CapturingHandler(response);
            using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };

            var result = await new HttpPlayerImportService(http).GetTemplateAsync(
                TestContext.Current.CancellationToken);

            result.IsProblem.ShouldBeTrue();
            result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        }
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

    private static PlayerImportUploadInput ValidUpload() => new()
    {
        Content = Encoding.UTF8.GetBytes("content"),
        FileName = "players.csv",
        ContentType = "text/csv"
    };

    private static byte[] TemplateBytes()
    {
        var header = string.Join(',', PlayerImportConstraints.Headers) + "\r\n";
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(header)];
    }

    private static HttpResponseMessage TemplateResponse(
        byte[] content,
        string mediaType,
        string? charset,
        string fileName)
    {
        var response = new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new ByteArrayContent(content)
        };
        response.Content.Headers.ContentType = new(mediaType) { CharSet = charset };
        response.Content.Headers.ContentDisposition = new("attachment") { FileName = fileName };
        return response;
    }

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
