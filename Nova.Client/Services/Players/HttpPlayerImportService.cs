using System.Globalization;
using System.Net.Http.Headers;
using System.Text;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Client.Services.Players;

/// <summary>WebAssembly HTTP implementation of player import preview, commit, and exact-request recovery.</summary>
/// <param name="httpClient">The client used to call the server player-import endpoints.</param>
public sealed class HttpPlayerImportService(HttpClient httpClient) : IPlayerImportService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlayerImportCompletion>> CommitAsync(
        PlayerImportCommitInput input, CancellationToken cancellationToken = default)
    {
        if (input is null)
        {
            return ServiceProblem.Validation("input", "A confirmed preview is required.");
        }

        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        var problem = ValidateUpload(input.Upload);
        if (problem is not null)
        {
            return problem.Value;
        }

        using var form = new MultipartFormDataContent();
        using var file = new ByteArrayContent(input.Upload.Content);
        if (!string.IsNullOrEmpty(input.Upload.ContentType))
        {
            file.Headers.ContentType = MediaTypeHeaderValue.Parse(input.Upload.ContentType);
        }

        form.Add(file, PlayerImportConstraints.FileFormFieldName, PlayerImportConstraints.TemplateFileName);
        form.Add(new StringContent(input.OperationId.ToString()), PlayerImportConstraints.OperationIdFormFieldName);
        form.Add(new StringContent(input.ConfirmationToken), PlayerImportConstraints.ConfirmationTokenFormFieldName);
        using var response = await httpClient.PostAsync(PlayerEndpoints.ImportCommit, form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<PlayerImportCompletion>(
            "The server returned an invalid player import completion.",
            completion => completion.OperationId == input.OperationId && IsValidCompletion(completion),
            cancellationToken);
    }

    /// <summary>Rejects incomplete or contradictory success bodies rather than reporting false success.</summary>
    /// <param name="completion">The decoded server result.</param>
    /// <returns>Whether every aggregate and row relationship is valid.</returns>
    private static bool IsValidCompletion(PlayerImportCompletion completion)
    {
        if (completion.OperationId == Guid.Empty || completion.OperationId.Version != 7
            || completion.CompletedAt == default || completion.CompletedAt.Offset != TimeSpan.Zero
            || completion.RecoveryExpiresAt.Offset != TimeSpan.Zero
            || completion.RecoveryExpiresAt - completion.CompletedAt != TimeSpan.FromHours(PlayerImportConstraints.RecoveryLifetimeHours)
            || completion.TotalRows is < 1 or > PlayerImportConstraints.MaxDataRows
            || completion.Rows is null || completion.Rows.Count != completion.TotalRows
            || completion.CreatedRows < 0 || completion.SkippedInvalidRows < 0
            || completion.SkippedDuplicateRows < 0 || completion.BlockedRows < 0
            || (long)completion.CreatedRows + completion.SkippedInvalidRows + completion.SkippedDuplicateRows + completion.BlockedRows != completion.TotalRows
            || completion.CreatedRows + completion.BlockedRows == 0
            || completion.EnrolledPlayers < 0 || completion.WaitingPlayers < 0
            || (long)completion.EnrolledPlayers + completion.WaitingPlayers != completion.CreatedRows
            || (completion.CampaignId is null
                ? completion.CampaignName is not null || completion.EnrolledPlayers != 0
                : completion.CampaignId <= 0 || string.IsNullOrWhiteSpace(completion.CampaignName) || completion.WaitingPlayers != 0))
        {
            return false;
        }

        var counts = new int[4];
        var players = new HashSet<long>();
        for (var index = 0; index < completion.Rows.Count; index++)
        {
            var row = completion.Rows[index];
            if (row is null || row.SourceRowNumber != index + 2 || !Enum.IsDefined(row.Status)
                || row.Errors is null
                || row.Errors.Any(error => error is null || !Enum.IsDefined(error.Field) || string.IsNullOrWhiteSpace(error.Message)))
            {
                return false;
            }

            counts[(int)row.Status]++;
            if (row.Status == PlayerImportCommitRowStatus.Created)
            {
                if (row.PlayerId is not > 0 || !players.Add(row.PlayerId.Value) || row.Errors.Count != 0 || row.Duplicate is not null)
                {
                    return false;
                }
            }
            else if (row.PlayerId is not null)
            {
                return false;
            }
            else if (row.Status == PlayerImportCommitRowStatus.BlockedAtCommit)
            {
                if (row.Duplicate is { } duplicate)
                {
                    if (row.Errors.Count != 0 || !Enum.IsDefined(duplicate.Kind)
                        || (duplicate.Kind == PlayerImportDuplicateKind.EarlierUploadRow
                            ? duplicate.ExistingPlayerId is not null || duplicate.EarlierSourceRowNumber is not >= 2 || duplicate.EarlierSourceRowNumber >= row.SourceRowNumber
                            : duplicate.ExistingPlayerId is not > 0 || duplicate.EarlierSourceRowNumber is not null))
                    {
                        return false;
                    }
                }
                else if (row.Errors.Count == 0)
                {
                    return false;
                }
            }
            else if (row.Errors.Count != 0 || row.Duplicate is not null)
            {
                return false;
            }
        }

        return counts[(int)PlayerImportCommitRowStatus.Created] == completion.CreatedRows
            && counts[(int)PlayerImportCommitRowStatus.SkippedInvalidAtPreview] == completion.SkippedInvalidRows
            && counts[(int)PlayerImportCommitRowStatus.SkippedDuplicateAtPreview] == completion.SkippedDuplicateRows
            && counts[(int)PlayerImportCommitRowStatus.BlockedAtCommit] == completion.BlockedRows;
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PlayerImportTemplate>> GetTemplateAsync(
        CancellationToken cancellationToken = default)
    {
        using var response = await httpClient.GetAsync(PlayerEndpoints.ImportTemplate, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        var mediaType = response.Content.Headers.ContentType?.MediaType;
        var downloadFileName = response.Content.Headers.ContentDisposition?.FileNameStar
            ?? response.Content.Headers.ContentDisposition?.FileName?.Trim('"');
        var content = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        if (!string.Equals(mediaType, "text/csv", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                response.Content.Headers.ContentType?.CharSet,
                "utf-8",
                StringComparison.OrdinalIgnoreCase)
            || !string.Equals(
                downloadFileName,
                PlayerImportConstraints.TemplateFileName,
                StringComparison.Ordinal)
            || !content.AsSpan().SequenceEqual(CreateExpectedTemplateContent()))
        {
            return ServiceProblem.ServerError("The server returned an invalid player import template.");
        }

        return new PlayerImportTemplate(
            content,
            response.Content.Headers.ContentType!.ToString(),
            PlayerImportConstraints.TemplateFileName);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PlayerImportPreview>> PreviewAsync(
        PlayerImportUploadInput upload,
        CancellationToken cancellationToken = default)
    {
        var validationProblem = ValidateUpload(upload);
        if (validationProblem is not null)
        {
            return validationProblem.Value;
        }

        using var form = new MultipartFormDataContent();
        using var fileContent = new ByteArrayContent(upload.Content);
        if (!string.IsNullOrEmpty(upload.ContentType))
        {
            fileContent.Headers.ContentType = MediaTypeHeaderValue.Parse(upload.ContentType);
        }

        form.Add(
            fileContent,
            PlayerImportConstraints.FileFormFieldName,
            PlayerImportConstraints.TemplateFileName);
        using var response = await httpClient.PostAsync(PlayerEndpoints.ImportPreview, form, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return await response.ToServiceProblemAsync(cancellationToken);
        }

        return await response.Content.ReadRequiredJsonAsync<PlayerImportPreview>(
            "The server returned an invalid player import preview.",
            IsValidPreview,
            cancellationToken);
    }

    /// <summary>Validates upload bounds and metadata before constructing a multipart request.</summary>
    /// <param name="upload">The prospective CSV upload.</param>
    /// <returns>A validation problem when the upload is invalid; otherwise, <see langword="null" />.</returns>
    private static ServiceProblem? ValidateUpload(PlayerImportUploadInput upload)
    {
        if (upload is null || upload.Content is null || upload.Content.Length == 0)
        {
            return ServiceProblem.Validation("file", "A non-empty CSV file is required.");
        }

        if (upload.Content.Length > PlayerImportConstraints.MaxFileBytes)
        {
            return ServiceProblem.Validation(
                "file",
                $"The CSV file must not exceed {PlayerImportConstraints.MaxFileBytes} bytes.");
        }

        if (string.IsNullOrWhiteSpace(upload.FileName)
            || upload.FileName.Contains('\r')
            || upload.FileName.Contains('\n')
            || !string.Equals(Path.GetExtension(upload.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
        {
            return ServiceProblem.Validation("file", "The uploaded file must have a .csv extension.");
        }

        if (!string.IsNullOrEmpty(upload.ContentType)
            && (!MediaTypeHeaderValue.TryParse(upload.ContentType, out var contentType)
                || contentType.MediaType is null
                || !PlayerImportConstraints.AllowedContentTypes.Contains(contentType.MediaType)))
        {
            return ServiceProblem.Validation("file", "The uploaded file type is not supported.");
        }

        return null;
    }

    /// <summary>Validates the complete successful preview response contract.</summary>
    /// <param name="preview">The deserialized server response.</param>
    /// <returns><see langword="true" /> when every preview invariant is satisfied.</returns>
    private static bool IsValidPreview(PlayerImportPreview preview)
    {
        if (preview.OperationId == Guid.Empty
            || preview.OperationId.Version != 7
            || string.IsNullOrWhiteSpace(preview.ConfirmationToken)
            || preview.ConfirmationToken.Length > PlayerImportConstraints.MaxConfirmationTokenCharacters
            || preview.ExpiresAt == default
            || preview.TotalRows is < 1 or > PlayerImportConstraints.MaxDataRows
            || preview.ReadyRows < 0
            || preview.InvalidRows < 0
            || preview.DuplicateRows < 0
            || preview.ReadyRows + preview.InvalidRows + preview.DuplicateRows != preview.TotalRows
            || preview.Rows is null
            || preview.Rows.Count != preview.TotalRows)
        {
            return false;
        }

        var priorRows = new Dictionary<int, PlayerImportPreviewRow>();
        var expectedSourceRow = 2;
        var readyRows = 0;
        var invalidRows = 0;
        var duplicateRows = 0;
        foreach (var row in preview.Rows)
        {
            if (row is null
                || row.SourceRowNumber != expectedSourceRow
                || row.Values is null
                || !IsValidValues(row.Values)
                || row.Errors is null
                || !Enum.IsDefined(row.Status)
                || row.Errors.Any(error => error is null || !Enum.IsDefined(error.Field) || string.IsNullOrWhiteSpace(error.Message)))
            {
                return false;
            }

            expectedSourceRow++;
            switch (row.Status)
            {
                case PlayerImportRowStatus.Ready:
                    readyRows++;
                    if (!IsValidCandidate(row.Values, row.Candidate)
                        || row.Errors.Count != 0
                        || row.Duplicate is not null)
                    {
                        return false;
                    }
                    break;
                case PlayerImportRowStatus.Invalid:
                    invalidRows++;
                    if (row.Candidate is not null
                        || row.Errors.Count == 0
                        || row.Duplicate is not null
                        || TryParseCandidate(row.Values, out _))
                    {
                        return false;
                    }
                    break;
                case PlayerImportRowStatus.Duplicate:
                    duplicateRows++;
                    if (!IsValidCandidate(row.Values, row.Candidate)
                        || row.Errors.Count != 0
                        || row.Duplicate is null
                        || !IsValidDuplicate(row, priorRows))
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }

            priorRows.Add(row.SourceRowNumber, row);
        }

        return readyRows == preview.ReadyRows
            && invalidRows == preview.InvalidRows
            && duplicateRows == preview.DuplicateRows;
    }

    /// <summary>Validates duplicate payload shape and earlier-row identity relationships.</summary>
    /// <param name="row">The duplicate preview row being validated.</param>
    /// <param name="priorRows">Previously validated rows indexed by source row.</param>
    /// <returns><see langword="true" /> when the duplicate payload and relationship are valid.</returns>
    private static bool IsValidDuplicate(
        PlayerImportPreviewRow row,
        IReadOnlyDictionary<int, PlayerImportPreviewRow> priorRows) =>
        row.Duplicate is { } duplicate
        && row.Candidate is { } candidate
        && Enum.IsDefined(duplicate.Kind)
        && duplicate.Kind switch
        {
            PlayerImportDuplicateKind.ExistingActivePlayer or PlayerImportDuplicateKind.ExistingArchivedPlayer =>
                duplicate.ExistingPlayerId is > 0 && duplicate.EarlierSourceRowNumber is null,
            PlayerImportDuplicateKind.EarlierUploadRow =>
                duplicate.ExistingPlayerId is null
                && duplicate.EarlierSourceRowNumber is >= 2
                && duplicate.EarlierSourceRowNumber < row.SourceRowNumber
                && priorRows.TryGetValue(duplicate.EarlierSourceRowNumber.Value, out var earlierRow)
                && earlierRow.Status == PlayerImportRowStatus.Ready
                && earlierRow.Candidate is { } earlierCandidate
                && HasSameDuplicateKey(candidate, earlierCandidate),
            _ => false
        };

    /// <summary>Checks that a candidate is the exact strict parse of its original row values.</summary>
    /// <param name="values">The original CSV cell values.</param>
    /// <param name="candidate">The typed candidate returned by the server.</param>
    /// <returns><see langword="true" /> when the candidate matches a valid strict parse.</returns>
    private static bool IsValidCandidate(PlayerImportRowValues values, CreatePlayerInput? candidate) =>
        candidate is not null
        && TryParseCandidate(values, out var parsedCandidate)
        && candidate == parsedCandidate;

    /// <summary>Attempts to reproduce the server parser's locale-independent player conversion.</summary>
    /// <param name="values">The original CSV cell values.</param>
    /// <param name="candidate">The parsed candidate, or <see langword="null" /> on failure.</param>
    /// <returns><see langword="true" /> when every cell parses and validates.</returns>
    private static bool TryParseCandidate(PlayerImportRowValues values, out CreatePlayerInput? candidate)
    {
        candidate = null;
        if (!IsValidValues(values)
            || HasFormulaLikeValue(values)
            || !DateOnly.TryParseExact(
                values.DateOfBirth,
                PlayerImportConstraints.DateFormat,
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var dateOfBirth)
            || !TryParseGender(values.Gender, out var gender)
            || !TryParseOptionalUnsignedInt(values.JerseyNumber, out var jerseyNumber)
            || !TryParseUnsignedInt(values.GraduationYear, out var graduationYear))
        {
            return false;
        }

        var parsedCandidate = new CreatePlayerInput
        {
            FirstName = values.FirstName,
            LastName = values.LastName,
            DateOfBirth = dateOfBirth,
            Gender = gender,
            JerseyNumber = jerseyNumber,
            GraduationYear = graduationYear
        };
        if (InputValidator.Validate(parsedCandidate).Count != 0)
        {
            return false;
        }

        candidate = parsedCandidate;
        return true;
    }

    /// <summary>Attempts to parse an optional gender using only the documented names.</summary>
    /// <param name="value">The raw gender cell.</param>
    /// <param name="gender">The parsed gender, or <see langword="null" /> for an empty cell.</param>
    /// <returns><see langword="true" /> when the cell is empty or names a supported gender.</returns>
    private static bool TryParseGender(string value, out Gender? gender)
    {
        gender = null;
        if (value.Length == 0)
        {
            return true;
        }

        if (!Enum.TryParse<Gender>(value, ignoreCase: true, out var parsedGender)
            || !Enum.IsDefined(parsedGender)
            || !string.Equals(Enum.GetName(parsedGender), value, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        gender = parsedGender;
        return true;
    }

    /// <summary>Attempts to parse an optional ASCII-decimal integer.</summary>
    /// <param name="value">The raw optional numeric cell.</param>
    /// <param name="result">The parsed number, or <see langword="null" /> for an empty cell.</param>
    /// <returns><see langword="true" /> when the cell is empty or contains only a supported integer.</returns>
    private static bool TryParseOptionalUnsignedInt(string value, out int? result)
    {
        result = null;
        if (value.Length == 0)
        {
            return true;
        }

        if (!TryParseUnsignedInt(value, out var parsed))
        {
            return false;
        }

        result = parsed;
        return true;
    }

    /// <summary>Attempts to parse a required ASCII-decimal integer without signs or separators.</summary>
    /// <param name="value">The raw numeric cell.</param>
    /// <param name="result">The parsed integer, or zero on failure.</param>
    /// <returns><see langword="true" /> when the cell contains only a supported integer.</returns>
    private static bool TryParseUnsignedInt(string value, out int result)
    {
        result = default;
        return value.Length > 0
            && value.All(char.IsAsciiDigit)
            && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out result);
    }

    /// <summary>Determines whether any raw cell has a spreadsheet-formula prefix.</summary>
    /// <param name="values">The original CSV cell values.</param>
    /// <returns><see langword="true" /> when any cell is formula-like.</returns>
    private static bool HasFormulaLikeValue(PlayerImportRowValues values) =>
        IsFormulaLike(values.FirstName)
        || IsFormulaLike(values.LastName)
        || IsFormulaLike(values.DateOfBirth)
        || IsFormulaLike(values.Gender)
        || IsFormulaLike(values.JerseyNumber)
        || IsFormulaLike(values.GraduationYear);

    /// <summary>Determines whether a raw value begins with a forbidden formula or control prefix.</summary>
    /// <param name="value">The raw cell value.</param>
    /// <returns><see langword="true" /> when the value is formula-like.</returns>
    private static bool IsFormulaLike(string value)
    {
        if (value.Length == 0)
        {
            return false;
        }

        var firstMeaningfulCharacter = value.FirstOrDefault(character => character != ' ');
        return firstMeaningfulCharacter is '\t' or '\r' or '\n' or '=' or '+' or '-' or '@';
    }

    /// <summary>Compares two candidates using the server's normalized upload duplicate key.</summary>
    /// <param name="candidate">The later candidate.</param>
    /// <param name="earlierCandidate">The earlier candidate referenced by the duplicate.</param>
    /// <returns><see langword="true" /> when the normalized name and birth-date key matches.</returns>
    private static bool HasSameDuplicateKey(CreatePlayerInput candidate, CreatePlayerInput earlierCandidate) =>
        string.Equals(
            candidate.FirstName.Trim().ToUpperInvariant(),
            earlierCandidate.FirstName.Trim().ToUpperInvariant(),
            StringComparison.Ordinal)
        && string.Equals(
            candidate.LastName.Trim().ToUpperInvariant(),
            earlierCandidate.LastName.Trim().ToUpperInvariant(),
            StringComparison.Ordinal)
        && candidate.DateOfBirth == earlierCandidate.DateOfBirth;

    /// <summary>Validates that every raw cell is present and within the shared character bound.</summary>
    /// <param name="values">The original CSV cell values.</param>
    /// <returns><see langword="true" /> when all six cells satisfy the response contract.</returns>
    private static bool IsValidValues(PlayerImportRowValues values) =>
        values.FirstName is not null
        && values.FirstName.Length <= PlayerImportConstraints.MaxFieldCharacters
        && values.LastName is not null
        && values.LastName.Length <= PlayerImportConstraints.MaxFieldCharacters
        && values.DateOfBirth is not null
        && values.DateOfBirth.Length <= PlayerImportConstraints.MaxFieldCharacters
        && values.Gender is not null
        && values.Gender.Length <= PlayerImportConstraints.MaxFieldCharacters
        && values.JerseyNumber is not null
        && values.JerseyNumber.Length <= PlayerImportConstraints.MaxFieldCharacters
        && values.GraduationYear is not null
        && values.GraduationYear.Length <= PlayerImportConstraints.MaxFieldCharacters;

    private static byte[] CreateExpectedTemplateContent()
    {
        var header = string.Join(',', PlayerImportConstraints.Headers) + "\r\n";
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(header)];
    }
}
