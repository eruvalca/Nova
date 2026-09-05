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
    /// <summary>Commits preserve the exact recovery identity and reconcile valid populated, mixed, and zero-creation results.</summary>
    /// <param name="shape">The legitimate completion shape.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("enrolled")]
    [InlineData("mixed")]
    [InlineData("blocked")]
    public async Task CommitAsync_SendsExactMultipartIdentity_AndAcceptsValidCompletion(string shape)
    {
        var completion = ValidCompletion();
        completion = shape switch
        {
            "mixed" => completion with
            {
                TotalRows = 3,
                SkippedInvalidRows = 1,
                SkippedDuplicateRows = 1,
                Rows = [completion.Rows[0], new(3, PlayerImportCommitRowStatus.SkippedInvalidAtPreview, null, [], null),
                    new(4, PlayerImportCommitRowStatus.SkippedDuplicateAtPreview, null, [], null)]
            },
            "blocked" => completion with
            {
                CreatedRows = 0,
                EnrolledPlayers = 0,
                BlockedRows = 1,
                Rows = [new(2, PlayerImportCommitRowStatus.BlockedAtCommit, null, [], new(PlayerImportDuplicateKind.ExistingActivePlayer, 44, null))]
            },
            _ => completion
        };
        var handler = new CapturingHandler(new(HttpStatusCode.OK) { Content = JsonContent.Create(completion) });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var input = CommitInput(completion.OperationId);
        var result = await new HttpPlayerImportService(http).CommitAsync(input, TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBeTrue();
        System.Text.Json.JsonSerializer.Serialize(result.Value).ShouldBe(System.Text.Json.JsonSerializer.Serialize(completion));
        handler.Method.ShouldBe(HttpMethod.Post);
        handler.PathAndQuery.ShouldBe(PlayerEndpoints.ImportCommit);
        handler.MediaType.ShouldBe("multipart/form-data");
        handler.Body.ShouldContain("name=operationId");
        handler.Body.ShouldContain(input.OperationId.ToString());
        handler.Body.ShouldContain("name=confirmationToken");
        handler.Body.ShouldContain(input.ConfirmationToken);
        handler.Body.ShouldContain("content");
    }

    /// <summary>Strict success validation rejects contradictory aggregates, malformed rows, and mismatched operation identity.</summary>
    /// <param name="malformation">The invalid completion case.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("operation")]
    [InlineData("timestamp")]
    [InlineData("retention")]
    [InlineData("total")]
    [InlineData("count")]
    [InlineData("enrollment")]
    [InlineData("campaign")]
    [InlineData("null rows")]
    [InlineData("null row")]
    [InlineData("row order")]
    [InlineData("status")]
    [InlineData("player id")]
    [InlineData("duplicate player id")]
    [InlineData("null errors")]
    [InlineData("created errors")]
    [InlineData("blocked without reason")]
    [InlineData("invalid duplicate")]
    [InlineData("skip with player")]
    public async Task CommitAsync_RejectsMalformedCompletion(string malformation)
    {
        var valid = ValidCompletion();
        var row = valid.Rows[0];
        var invalid = malformation switch
        {
            "operation" => valid with { OperationId = Guid.CreateVersion7() },
            "timestamp" => valid with { CompletedAt = default },
            "retention" => valid with { RecoveryExpiresAt = valid.CompletedAt.AddHours(25) },
            "total" => valid with { TotalRows = 1001 },
            "count" => valid with { CreatedRows = 0 },
            "enrollment" => valid with { WaitingPlayers = 1 },
            "campaign" => valid with { CampaignName = null },
            "null rows" => valid with { Rows = null! },
            "null row" => valid with { Rows = [null!] },
            "row order" => valid with { Rows = [row with { SourceRowNumber = 3 }] },
            "status" => valid with { Rows = [row with { Status = (PlayerImportCommitRowStatus)999 }] },
            "player id" => valid with { Rows = [row with { PlayerId = 0 }] },
            "duplicate player id" => valid with { TotalRows = 2, CreatedRows = 2, EnrolledPlayers = 2, Rows = [row, row with { SourceRowNumber = 3 }] },
            "null errors" => valid with { Rows = [row with { Errors = null! }] },
            "created errors" => valid with { Rows = [row with { Errors = [new(PlayerImportField.FirstName, "Invalid")] }] },
            "blocked without reason" => valid with { CreatedRows = 0, EnrolledPlayers = 0, BlockedRows = 1, Rows = [row with { Status = PlayerImportCommitRowStatus.BlockedAtCommit, PlayerId = null }] },
            "invalid duplicate" => valid with { CreatedRows = 0, EnrolledPlayers = 0, BlockedRows = 1, Rows = [new(2, PlayerImportCommitRowStatus.BlockedAtCommit, null, [], new(PlayerImportDuplicateKind.ExistingActivePlayer, 0, null))] },
            "skip with player" => valid with { TotalRows = 2, SkippedDuplicateRows = 1, Rows = [row, row with { SourceRowNumber = 3, Status = PlayerImportCommitRowStatus.SkippedDuplicateAtPreview }] },
            _ => throw new ArgumentOutOfRangeException(nameof(malformation))
        };
        var handler = new CapturingHandler(new(HttpStatusCode.OK) { Content = JsonContent.Create(invalid) });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpPlayerImportService(http).CommitAsync(CommitInput(valid.OperationId), TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Malformed JSON cannot be reported as a completed import.</summary>
    /// <param name="body">The invalid JSON body.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not-json")]
    public async Task CommitAsync_RejectsMalformedSuccessJson(string body)
    {
        var handler = new CapturingHandler(new(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpPlayerImportService(http).CommitAsync(CommitInput(Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
    }

    /// <summary>Non-success HTTP responses retain their service failure classification.</summary>
    /// <param name="status">The HTTP response status.</param>
    /// <param name="kind">The expected service failure.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(HttpStatusCode.Forbidden, ServiceProblemKind.Forbidden)]
    [InlineData(HttpStatusCode.Conflict, ServiceProblemKind.Conflict)]
    [InlineData(HttpStatusCode.BadRequest, ServiceProblemKind.Validation)]
    public async Task CommitAsync_PropagatesProblemDetails(HttpStatusCode status, ServiceProblemKind kind)
    {
        var handler = new CapturingHandler(new(status)
        {
            Content = JsonContent.Create(new { detail = "Preview again.", errors = new Dictionary<string, string[]> { ["file"] = ["Invalid confirmation."] } })
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpPlayerImportService(http).CommitAsync(CommitInput(Guid.CreateVersion7()), TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(kind);
    }

    /// <summary>Creates a valid populated completion with authoritative server timestamps.</summary>
    /// <returns>A valid enrollment receipt.</returns>
    private static PlayerImportCompletion ValidCompletion() => new(
        Guid.CreateVersion7(), DateTimeOffset.UnixEpoch, DateTimeOffset.UnixEpoch.AddHours(24),
        1, 1, 0, 0, 0, 1, 0, 41, "Campaign", [new(2, PlayerImportCommitRowStatus.Created, 42, [], null)]);

    /// <summary>Builds a replayable request for the expected operation.</summary>
    /// <param name="operationId">The preview operation identity.</param>
    /// <returns>The commit input.</returns>
    private static PlayerImportCommitInput CommitInput(Guid operationId) => new()
    {
        Upload = ValidUpload(),
        OperationId = operationId,
        ConfirmationToken = "exact-protected-token"
    };

    /// <summary>Invalid confirmation inputs fail locally without transmitting a mutation.</summary>
    /// <param name="invalidField">The invalid request field.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData("operation")]
    [InlineData("token")]
    [InlineData("file size")]
    public async Task CommitAsync_RejectsInvalidInput_WithoutSendingRequest(string invalidField)
    {
        var input = CommitInput(Guid.CreateVersion7());
        input = invalidField switch
        {
            "operation" => input with { OperationId = Guid.Empty },
            "token" => input with { ConfirmationToken = " " },
            "file size" => input with { Upload = input.Upload with { Content = new byte[PlayerImportConstraints.MaxFileBytes + 1] } },
            _ => throw new ArgumentOutOfRangeException(nameof(invalidField))
        };
        var handler = new CapturingHandler(new(HttpStatusCode.OK));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpPlayerImportService(http).CommitAsync(input, TestContext.Current.CancellationToken);
        result.IsProblem.ShouldBeTrue();
        result.Problem.Kind.ShouldBe(ServiceProblemKind.Validation);
        handler.Method.ShouldBeNull();
    }

    /// <summary>Cancellation stays cancellation rather than being misreported as a completed or rolled-back batch.</summary>
    [Fact]
    public async Task CommitAsync_PreservesCancellation()
    {
        var handler = new CapturingHandler(new(HttpStatusCode.OK));
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Should.ThrowAsync<OperationCanceledException>(() => new HttpPlayerImportService(http)
            .CommitAsync(CommitInput(Guid.CreateVersion7()), cancellation.Token));
    }

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

    /// <summary>Preview success respects the same confirmation length bound as commit input.</summary>
    /// <param name="excess">Characters beyond the inclusive maximum.</param>
    [Theory(IncludeTestCaseIndex = true)]
    [InlineData(0)]
    [InlineData(1)]
    public async Task PreviewAsync_EnforcesConfirmationTokenLength(int excess)
    {
        var preview = ValidPreview() with
        {
            ConfirmationToken = new string('x', PlayerImportConstraints.MaxConfirmationTokenCharacters + excess)
        };
        using var handler = new CapturingHandler(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(preview)
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://localhost/") };
        var result = await new HttpPlayerImportService(http).PreviewAsync(ValidUpload(), TestContext.Current.CancellationToken);
        result.IsSuccess.ShouldBe(excess == 0);
        if (excess > 0)
        {
            result.Problem.Kind.ShouldBe(ServiceProblemKind.ServerError);
        }
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
