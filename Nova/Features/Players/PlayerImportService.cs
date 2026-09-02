using System.Diagnostics;
using System.Globalization;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;

namespace Nova.Features.Players;

/// <summary>Generates and previews player CSV imports without persisting player records.</summary>
internal sealed partial class PlayerImportService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    PlayerImportCsvParser parser,
    PlayerImportPreviewTokenProtector tokenProtector,
    TimeProvider timeProvider,
    ILogger<PlayerImportService> logger) : IPlayerImportService
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

    private async Task<ServiceResult<PlayerImportPreview>> BuildPreviewAsync(
        ParsedPlayerImport parsed,
        byte[] content,
        long actorUserId,
        long clubId,
        long startedAt,
        CancellationToken cancellationToken)
    {
        var readyCandidates = parsed.Rows
            .Where(row => row.Status == PlayerImportRowStatus.Ready)
            .Select(row => row.Candidate!)
            .ToList();
        var dates = readyCandidates
            .Select(candidate => candidate.DateOfBirth)
            .Distinct()
            .ToArray();

        List<ExistingPlayer> existingPlayers;
        await using (var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken))
        {
            existingPlayers = dates.Length == 0
                ? []
                : await db.Players
                    .Where(player => dates.Contains(player.DateOfBirth))
                    .Select(player => new ExistingPlayer(
                        player.PlayerId,
                        player.FirstName,
                        player.LastName,
                        player.DateOfBirth,
                        player.LifecycleStatus))
                    .ToListAsync(cancellationToken);
        }

        var existingByKey = existingPlayers
            .GroupBy(player => PlayerDuplicateKey.Create(player.FirstName, player.LastName, player.DateOfBirth))
            .ToDictionary(
                group => group.Key,
                group => group
                    .OrderBy(player => player.LifecycleStatus == LifecycleStatus.Active ? 0 : 1)
                    .ThenBy(player => player.PlayerId)
                    .First());
        var firstUploadRows = new Dictionary<PlayerDuplicateKey, int>();
        var classifiedRows = new List<PlayerImportPreviewRow>(parsed.Rows.Count);

        foreach (var row in parsed.Rows)
        {
            if (row.Status != PlayerImportRowStatus.Ready)
            {
                classifiedRows.Add(row);
                continue;
            }

            var candidate = row.Candidate!;
            var key = PlayerDuplicateKey.Create(candidate.FirstName, candidate.LastName, candidate.DateOfBirth);
            if (existingByKey.TryGetValue(key, out var existing))
            {
                classifiedRows.Add(row with
                {
                    Status = PlayerImportRowStatus.Duplicate,
                    Duplicate = new PlayerImportDuplicate(
                        existing.LifecycleStatus == LifecycleStatus.Active
                            ? PlayerImportDuplicateKind.ExistingActivePlayer
                            : PlayerImportDuplicateKind.ExistingArchivedPlayer,
                        existing.PlayerId,
                        EarlierSourceRowNumber: null)
                });
                continue;
            }

            if (firstUploadRows.TryGetValue(key, out var earlierSourceRow))
            {
                classifiedRows.Add(row with
                {
                    Status = PlayerImportRowStatus.Duplicate,
                    Duplicate = new PlayerImportDuplicate(
                        PlayerImportDuplicateKind.EarlierUploadRow,
                        ExistingPlayerId: null,
                        earlierSourceRow)
                });
                continue;
            }

            firstUploadRows.Add(key, row.SourceRowNumber);
            classifiedRows.Add(row);
        }

        var operationId = Guid.CreateVersion7();
        var issuedAt = timeProvider.GetUtcNow();
        var lifetime = TimeSpan.FromMinutes(PlayerImportConstraints.PreviewLifetimeMinutes);
        var expiresAt = issuedAt.Add(lifetime);
        var payload = new PlayerImportPreviewTokenPayload(
            Version: 1,
            operationId,
            clubId,
            actorUserId,
            Convert.ToHexString(SHA256.HashData(content)),
            content.Length,
            issuedAt,
            expiresAt);
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
            classifiedRows.AsReadOnly());

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

        if (upload.FileName.Contains('\r')
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

    private readonly record struct ExistingPlayer(
        long PlayerId,
        string FirstName,
        string LastName,
        DateOnly DateOfBirth,
        LifecycleStatus LifecycleStatus);

    private readonly record struct PlayerDuplicateKey(string FirstName, string LastName, DateOnly DateOfBirth)
    {
        public static PlayerDuplicateKey Create(string firstName, string lastName, DateOnly dateOfBirth) => new(
            firstName.Trim().ToUpperInvariant(),
            lastName.Trim().ToUpperInvariant(),
            dateOfBirth);
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
