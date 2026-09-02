using System.Net.Http.Headers;
using System.Text;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Client.Services.Players;

/// <summary>WebAssembly HTTP implementation of the player import preview service.</summary>
public sealed class HttpPlayerImportService(HttpClient httpClient) : IPlayerImportService
{
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

        if (!string.Equals(Path.GetExtension(upload.FileName), ".csv", StringComparison.OrdinalIgnoreCase))
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

    private static bool IsValidPreview(PlayerImportPreview preview)
    {
        if (preview.OperationId == Guid.Empty
            || preview.OperationId.Version != 7
            || string.IsNullOrWhiteSpace(preview.ConfirmationToken)
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
                    if (!IsValidCandidate(row.Candidate) || row.Errors.Count != 0 || row.Duplicate is not null)
                    {
                        return false;
                    }
                    break;
                case PlayerImportRowStatus.Invalid:
                    invalidRows++;
                    if (row.Candidate is not null || row.Errors.Count == 0 || row.Duplicate is not null)
                    {
                        return false;
                    }
                    break;
                case PlayerImportRowStatus.Duplicate:
                    duplicateRows++;
                    if (!IsValidCandidate(row.Candidate)
                        || row.Errors.Count != 0
                        || row.Duplicate is null
                        || !IsValidDuplicate(row.Duplicate, row.SourceRowNumber))
                    {
                        return false;
                    }
                    break;
                default:
                    return false;
            }
        }

        return readyRows == preview.ReadyRows
            && invalidRows == preview.InvalidRows
            && duplicateRows == preview.DuplicateRows;
    }

    private static bool IsValidDuplicate(PlayerImportDuplicate duplicate, int sourceRowNumber) =>
        Enum.IsDefined(duplicate.Kind)
        && duplicate.Kind switch
        {
            PlayerImportDuplicateKind.ExistingActivePlayer or PlayerImportDuplicateKind.ExistingArchivedPlayer =>
                duplicate.ExistingPlayerId is > 0 && duplicate.EarlierSourceRowNumber is null,
            PlayerImportDuplicateKind.EarlierUploadRow =>
                duplicate.ExistingPlayerId is null
                && duplicate.EarlierSourceRowNumber is >= 2
                && duplicate.EarlierSourceRowNumber < sourceRowNumber,
            _ => false
        };

    private static bool IsValidCandidate(CreatePlayerInput? candidate) =>
        candidate is not null
        && InputValidator.Validate(candidate).Count == 0
        && (candidate.Gender is null || Enum.IsDefined(candidate.Gender.Value));

    private static bool IsValidValues(PlayerImportRowValues values) =>
        values.FirstName is not null
        && values.LastName is not null
        && values.DateOfBirth is not null
        && values.Gender is not null
        && values.JerseyNumber is not null
        && values.GraduationYear is not null;

    private static byte[] CreateExpectedTemplateContent()
    {
        var header = string.Join(',', PlayerImportConstraints.Headers) + "\r\n";
        return [.. Encoding.UTF8.GetPreamble(), .. Encoding.UTF8.GetBytes(header)];
    }
}
