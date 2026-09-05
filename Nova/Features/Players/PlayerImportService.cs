using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;

namespace Nova.Features.Players;

/// <summary>Previews bounded CSV imports and commits explicitly reviewed rows with durable recovery.</summary>
/// <param name="readDbContextFactory">The tenant-filtered preview context factory.</param>
/// <param name="currentUserProvider">The request's actor and tenant claims.</param>
/// <param name="parser">The authoritative structural CSV validator.</param>
/// <param name="tokenProtector">The protected review identity boundary.</param>
/// <param name="timeProvider">The clock for preview and receipt lifetimes.</param>
/// <param name="logger">The structured import outcome logger.</param>
/// <param name="dbContextFactory">Fresh transactional tenant contexts.</param>
/// <param name="adminDbContextFactory">Infrastructure contexts used only to prune expired receipts.</param>
internal sealed partial class PlayerImportService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    PlayerImportCsvParser parser,
    PlayerImportPreviewTokenProtector tokenProtector,
    TimeProvider timeProvider,
    ILogger<PlayerImportService> logger,
    IDbContextFactory<NovaDbContext> dbContextFactory,
    IDbContextFactory<NovaAdminDbContext> adminDbContextFactory) : IPlayerImportService
{
    private static readonly byte[] TemplateContent = CreateTemplateContent();

    /// <inheritdoc />
    public Task<ServiceResult<PlayerImportTemplate>> GetTemplateAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!TryGetAdministrator(out var actorUserId, out var clubId))
        {
            LogTemplateForbidden(currentUserProvider.UserId ?? 0);
            return Task.FromResult<ServiceResult<PlayerImportTemplate>>(
                ServiceProblem.Forbidden("You must be a club administrator to download the player import template."));
        }

        LogTemplateGenerated(actorUserId, clubId);
        return Task.FromResult<ServiceResult<PlayerImportTemplate>>(new PlayerImportTemplate(
            TemplateContent.ToArray(),
            PlayerImportConstraints.CsvContentType,
            PlayerImportConstraints.TemplateFileName));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<PlayerImportPreview>> PreviewAsync(
        PlayerImportUploadInput upload,
        CancellationToken cancellationToken = default)
    {
        var uploadProblem = ValidateUpload(upload);
        if (uploadProblem is not null)
        {
            return uploadProblem.Value;
        }

        if (!TryGetAdministrator(out var actorUserId, out var clubId))
        {
            LogPreviewForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to preview a player import.");
        }

        upload = upload with { Content = upload.Content.ToArray() };
        var startedAt = Stopwatch.GetTimestamp();
        var parseResult = parser.Parse(upload.Content, cancellationToken);
        return await parseResult.Match<Task<ServiceResult<PlayerImportPreview>>>(
            parsed => BuildPreviewAsync(
                parsed,
                upload.Content,
                actorUserId,
                clubId,
                startedAt,
                cancellationToken),
            failure =>
            {
                LogPreviewRejected(actorUserId, clubId, upload.Content.Length, failure.Message);
                return Task.FromResult<ServiceResult<PlayerImportPreview>>(
                    ServiceProblem.Validation("file", failure.Message));
            });
    }

    /// <summary>Classifies the parsed file and protects the exact reviewed eligibility set.</summary>
    /// <param name="parsed">The structurally parsed source rows.</param>
    /// <param name="content">The frozen original bytes.</param>
    /// <param name="actorUserId">The reviewing administrator.</param>
    /// <param name="clubId">The reviewing club.</param>
    /// <param name="startedAt">The request start for duration logging.</param>
    /// <param name="cancellationToken">Cancels classification.</param>
    /// <returns>The advisory review with protected confirmation identity.</returns>
    private async Task<ServiceResult<PlayerImportPreview>> BuildPreviewAsync(
        ParsedPlayerImport parsed,
        byte[] content,
        long actorUserId,
        long clubId,
        long startedAt,
        CancellationToken cancellationToken)
    {
        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var classifiedRows = await PlayerImportRowClassifier.ClassifyAsync(db, parsed, cancellationToken);

        var operationId = Guid.CreateVersion7();
        var issuedAt = timeProvider.GetUtcNow();
        var lifetime = TimeSpan.FromMinutes(PlayerImportConstraints.PreviewLifetimeMinutes);
        var expiresAt = issuedAt.Add(lifetime);
        var payload = new PlayerImportPreviewTokenPayload(
            Version: 2,
            operationId,
            clubId,
            actorUserId,
            Convert.ToHexString(SHA256.HashData(content)),
            content.Length,
            issuedAt,
            expiresAt,
            classifiedRows.Select(row => row.Status).ToArray());
        var confirmationToken = tokenProtector.Protect(payload, lifetime);

        var readyRows = classifiedRows.Count(row => row.Status == PlayerImportRowStatus.Ready);
        var invalidRows = classifiedRows.Count(row => row.Status == PlayerImportRowStatus.Invalid);
        var duplicateRows = classifiedRows.Count(row => row.Status == PlayerImportRowStatus.Duplicate);
        var preview = new PlayerImportPreview(
            operationId,
            confirmationToken,
            expiresAt,
            classifiedRows.Count,
            readyRows,
            invalidRows,
            duplicateRows,
            classifiedRows);

        LogPreviewCompleted(
            operationId,
            actorUserId,
            clubId,
            content.Length,
            preview.TotalRows,
            readyRows,
            invalidRows,
            duplicateRows,
            Stopwatch.GetElapsedTime(startedAt).TotalMilliseconds);
        return preview;
    }

    private bool TryGetAdministrator(out long actorUserId, out long clubId)
    {
        actorUserId = currentUserProvider.UserId ?? 0;
        clubId = currentUserProvider.ClubId ?? 0;
        return actorUserId > 0 && clubId > 0 && currentUserProvider.IsClubAdmin;
    }

    private static ServiceProblem? ValidateUpload(PlayerImportUploadInput upload)
    {
        if (upload is null)
        {
            return ServiceProblem.Validation("file", "A CSV file is required.");
        }

        if (upload.Content is null || upload.Content.Length == 0)
        {
            return ServiceProblem.Validation("file", "The CSV file must not be empty.");
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

        if (!string.IsNullOrEmpty(upload.ContentType))
        {
            var mediaType = MediaTypeHeaderValue.TryParse(upload.ContentType, out var parsedContentType)
                ? parsedContentType.MediaType
                : null;
            if (mediaType is null || !PlayerImportConstraints.AllowedContentTypes.Contains(mediaType))
            {
                return ServiceProblem.Validation("file", "The uploaded file type is not supported.");
            }
        }

        return null;
    }

    private static byte[] CreateTemplateContent()
    {
        var header = string.Join(',', PlayerImportConstraints.Headers) + "\r\n";
        var body = Encoding.UTF8.GetBytes(header);
        return [.. Encoding.UTF8.GetPreamble(), .. body];
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Player import template forbidden for UserId={UserId}.")]
    private partial void LogTemplateForbidden(long userId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Player import template generated for UserId={ActorUserId} in ClubId={ClubId}.")]
    private partial void LogTemplateGenerated(long actorUserId, long clubId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Player import preview forbidden for UserId={UserId}.")]
    private partial void LogPreviewForbidden(long userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Player import preview rejected for UserId={ActorUserId} in ClubId={ClubId}; Bytes={Bytes}; Reason={Reason}.")]
    private partial void LogPreviewRejected(long actorUserId, long clubId, int bytes, string reason);

    [LoggerMessage(Level = LogLevel.Information, Message = "Player import preview OperationId={OperationId} completed for UserId={ActorUserId} in ClubId={ClubId}; Bytes={Bytes}; Total={TotalRows}; Ready={ReadyRows}; Invalid={InvalidRows}; Duplicate={DuplicateRows}; DurationMs={DurationMs}.")]
    private partial void LogPreviewCompleted(
        Guid operationId,
        long actorUserId,
        long clubId,
        int bytes,
        int totalRows,
        int readyRows,
        int invalidRows,
        int duplicateRows,
        double durationMs);
}
