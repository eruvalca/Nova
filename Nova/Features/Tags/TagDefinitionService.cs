using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Shared;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.Shared.Security;
using Nova.Shared.Validation;
using OneOf;
using OneOf.Types;

namespace Nova.Features.Tags;

/// <summary>
/// Provides server-side tag-definition management and read operations within the current club.
/// </summary>
/// <param name="dbContextFactory">The tenant-scoped database factory.</param>
/// <param name="currentUserProvider">The current authenticated user and club.</param>
/// <param name="tagDefinitionLifecycleService">The lifecycle service used for archive and restore operations.</param>
/// <param name="logger">The logger used for expected forbidden or duplicate-name outcomes.</param>
public sealed partial class TagDefinitionService(
    IDbContextFactory<NovaDbContext> dbContextFactory,
    ICurrentUserProvider currentUserProvider,
    TagDefinitionLifecycleService tagDefinitionLifecycleService,
    ILogger<TagDefinitionService> logger) : ITagDefinitionService
{
    /// <inheritdoc />
    public async Task<ServiceResult<TagDefinitionMutationSuccess>> CreateAsync(
        CreateTagDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            return ServiceProblem.Validation(validationErrors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogCreateForbidden(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to create tag definitions.");
        }

        var normalizedName = NormalizeName(input.Name);
        var normalizedNameKey = NormalizeNameKey(normalizedName);
        var normalizedColor = NormalizeColor(input.Color);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var nameExists = await db.PlayerTags
            .AnyAsync(tag => tag.ClubId == clubId && tag.NormalizedName == normalizedNameKey, cancellationToken);

        if (nameExists)
        {
            LogDuplicateName(clubId, normalizedName);
            return ServiceProblem.Conflict($"A tag definition named '{normalizedName}' already exists in this club.");
        }

        var tagDefinition = new PlayerTagEntity
        {
            Name = normalizedName,
            NormalizedName = normalizedNameKey,
            Color = normalizedColor,
            ClubId = clubId,
            LifecycleStatus = LifecycleStatus.Active,
            CreatedById = actorUserId
        };

        db.PlayerTags.Add(tagDefinition);
        await db.SaveChangesAsync(cancellationToken);

        return new TagDefinitionMutationSuccess { TagDefinitionId = tagDefinition.PlayerTagId };
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TagDefinitionMutationSuccess>> UpdateAsync(
        UpdateTagDefinitionInput input,
        CancellationToken cancellationToken = default)
    {
        var validationErrors = InputValidator.Validate(input);
        if (validationErrors.Count > 0)
        {
            return ServiceProblem.Validation(validationErrors);
        }

        if (currentUserProvider.UserId is not long actorUserId
            || currentUserProvider.ClubId is not long clubId
            || !currentUserProvider.IsClubAdmin)
        {
            LogUpdateForbidden(input.TagDefinitionId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be a club administrator to edit tag definitions.");
        }

        var normalizedName = NormalizeName(input.Name);
        var normalizedNameKey = NormalizeNameKey(normalizedName);
        var normalizedColor = NormalizeColor(input.Color);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var tagDefinition = await db.PlayerTags
            .SingleOrDefaultAsync(tag => tag.PlayerTagId == input.TagDefinitionId, cancellationToken);

        if (tagDefinition is null || tagDefinition.ClubId != clubId)
        {
            LogUpdateNotFound(input.TagDefinitionId, clubId);
            return ServiceProblem.NotFound("The requested tag definition was not found in this club.");
        }

        if (tagDefinition.LifecycleStatus == LifecycleStatus.Archived)
        {
            LogArchiveUpdateBlocked(input.TagDefinitionId, clubId);
            return ServiceProblem.Conflict("Archived tag definitions cannot be edited. Restore them first.");
        }

        var nameExists = await db.PlayerTags
            .AnyAsync(tag => tag.ClubId == clubId
                && tag.PlayerTagId != input.TagDefinitionId
                && tag.NormalizedName == normalizedNameKey,
                cancellationToken);

        if (nameExists)
        {
            LogDuplicateName(clubId, normalizedName);
            return ServiceProblem.Conflict($"A tag definition named '{normalizedName}' already exists in this club.");
        }

        tagDefinition.Name = normalizedName;
        tagDefinition.NormalizedName = normalizedNameKey;
        tagDefinition.Color = normalizedColor;
        tagDefinition.ModifiedById = actorUserId;
        tagDefinition.ModifiedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);

        return new TagDefinitionMutationSuccess { TagDefinitionId = tagDefinition.PlayerTagId };
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<TagDefinitionSummary>>> GetActiveAsync(
        GetTagDefinitionsInput? input = null,
        CancellationToken cancellationToken = default)
        => await GetDefinitionsAsync(includeArchived: false, input, cancellationToken);

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<TagDefinitionSummary>>> GetArchivedAsync(
        GetTagDefinitionsInput? input = null,
        CancellationToken cancellationToken = default)
        => await GetDefinitionsAsync(includeArchived: true, input, cancellationToken);

    /// <inheritdoc />
    public async Task<ServiceResult<TagDefinitionMutationSuccess>> ArchiveAsync(
        long tagDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var result = await tagDefinitionLifecycleService.ArchiveAsync(tagDefinitionId, cancellationToken);
        return result.Match<ServiceResult<TagDefinitionMutationSuccess>>(
            _ => new TagDefinitionMutationSuccess { TagDefinitionId = tagDefinitionId },
            _ => ServiceProblem.NotFound("The requested tag definition was not found."),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    /// <inheritdoc />
    public async Task<ServiceResult<TagDefinitionMutationSuccess>> RestoreAsync(
        long tagDefinitionId,
        CancellationToken cancellationToken = default)
    {
        var result = await tagDefinitionLifecycleService.RestoreAsync(tagDefinitionId, cancellationToken);
        return result.Match<ServiceResult<TagDefinitionMutationSuccess>>(
            _ => new TagDefinitionMutationSuccess { TagDefinitionId = tagDefinitionId },
            _ => ServiceProblem.NotFound("The requested tag definition was not found."),
            forbidden => ServiceProblem.Forbidden(forbidden.Detail),
            conflict => ServiceProblem.Conflict(conflict.Detail));
    }

    private async Task<ServiceResult<IReadOnlyList<TagDefinitionSummary>>> GetDefinitionsAsync(
        bool includeArchived,
        GetTagDefinitionsInput? input,
        CancellationToken cancellationToken)
    {
        var normalizedInput = input ?? new GetTagDefinitionsInput();
        if (currentUserProvider.UserId is not long || currentUserProvider.ClubId is not long clubId)
        {
            return ServiceProblem.Forbidden("You must be signed in and in a club to view tag definitions.");
        }

        var limit = Math.Clamp(normalizedInput.Limit ?? 50, 1, 100);

        await using var db = await dbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.PlayerTags
            .Where(tag => tag.ClubId == clubId)
            .Where(tag => includeArchived || tag.LifecycleStatus == LifecycleStatus.Active)
            .OrderBy(tag => tag.Name)
            .ThenBy(tag => tag.PlayerTagId)
            .Take(limit)
            .Select(tag => new TagDefinitionSummary
            {
                TagDefinitionId = tag.PlayerTagId,
                Name = tag.Name,
                Color = tag.Color,
                LifecycleStatus = tag.LifecycleStatus,
                CreatedAt = tag.CreatedAt,
                ArchivedAt = tag.ArchivedAt
            });

        var items = await query.ToListAsync(cancellationToken);
        return items.AsReadOnly();
    }

    private static string NormalizeName(string value)
    {
        var trimmed = value.Trim();
        return trimmed;
    }

    private static string NormalizeNameKey(string value)
        => NormalizeName(value).ToUpperInvariant();

    private static string NormalizeColor(string value)
        => string.IsNullOrWhiteSpace(value) ? "#FFFFFF" : value.Trim();

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition create forbidden for UserId={UserId}.")]
    private partial void LogCreateForbidden(long userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition update forbidden for PlayerTagId={TagDefinitionId} by UserId={UserId}.")]
    private partial void LogUpdateForbidden(long tagDefinitionId, long userId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition not found for PlayerTagId={TagDefinitionId} in ClubId={ClubId}.")]
    private partial void LogUpdateNotFound(long tagDefinitionId, long clubId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Archived tag definition update blocked for PlayerTagId={TagDefinitionId} in ClubId={ClubId}.")]
    private partial void LogArchiveUpdateBlocked(long tagDefinitionId, long clubId);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Duplicate tag definition name '{Name}' rejected for ClubId={ClubId}.")]
    private partial void LogDuplicateName(long clubId, string name);
}
