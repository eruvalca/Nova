using System.Net.Http.Headers;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;

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
            || string.IsNullOrWhiteSpace(downloadFileName)
            || content.Length is 0 or > PlayerImportConstraints.MaxFileBytes)
        {
            return ServiceProblem.ServerError("The server returned an invalid player import template.");
        }

        return new PlayerImportTemplate(
            content,
            response.Content.Headers.ContentType!.ToString(),
            downloadFileName);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PlayerImportPreview>> PreviewAsync(
        PlayerImportUpload upload,
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

        form.Add(fileContent, PlayerImportConstraints.FileFormFieldName, upload.FileName);
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

    private static ServiceProblem? ValidateUpload(PlayerImportUpload upload)
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
            || string.IsNullOrWhiteSpace(preview.ConfirmationToken)
            || preview.ExpiresAt <= DateTimeOffset.UtcNow
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

        var previousSourceRow = 1;
        var readyRows = 0;
        var invalidRows = 0;
        var duplicateRows = 0;
        foreach (var row in preview.Rows)
        {
            if (row is null
                || row.SourceRowNumber <= previousSourceRow
                || row.Values is null
                || row.Errors is null
                || !Enum.IsDefined(row.Status)
                || row.Errors.Any(error => error is null || !Enum.IsDefined(error.Field) || string.IsNullOrWhiteSpace(error.Message)))
            {
                return false;
            }

            previousSourceRow = row.SourceRowNumber;
            switch (row.Status)
            {
                case PlayerImportRowStatus.Ready:
                    readyRows++;
                    if (row.Candidate is null || row.Errors.Count != 0 || row.Duplicate is not null)
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
                    if (row.Candidate is null
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
}
