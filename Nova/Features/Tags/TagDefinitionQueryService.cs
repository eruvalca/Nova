using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Features.Common;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Tags;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.Features.Tags;

/// <summary>
/// Provides tenant-safe, read-only tag-definition projections for club administrators and club members.
/// </summary>
/// <param name="readDbContextFactory">The read-only context factory.</param>
/// <param name="currentUserProvider">The current user and club context.</param>
/// <param name="logger">The logger for rejected access attempts.</param>
internal sealed partial class TagDefinitionQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<TagDefinitionQueryService> logger) : ITagDefinitionQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<TagDefinitionListResult>> GetManagementListAsync(
        GetTagDefinitionsInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (!currentUserProvider.IsClubAdmin || currentUserProvider.ClubId is not long clubId)
        {
            LogTagDefinitionsForbidden(currentUserProvider.UserId ?? 0, isManagement: true);
            return ServiceProblem.Forbidden("You must be a club administrator to manage tag definitions.");
        }

        var lifecycleStatus = NormalizeLifecycleStatus(input.LifecycleStatus);
        var search = string.IsNullOrWhiteSpace(input.Search) ? null : input.Search.Trim();

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.PlayerTags.Where(tag => tag.ClubId == clubId);

        if (lifecycleStatus is LifecycleStatus selectedStatus)
        {
            query = query.Where(tag => tag.LifecycleStatus == selectedStatus);
        }

        if (search is not null)
        {
            var uppercaseSearch = search.ToUpperInvariant();
            var escapedSearch = LikePatternEscaper.EscapeLikePattern(search);
            query = db.Database.IsNpgsql()
                ? query.Where(tag => EF.Functions.ILike(tag.Name, $"%{escapedSearch}%", @"\"))
#pragma warning disable CA1311, CA1862, CA1304, MA0011 // This expression is translated to SQL UPPER; culture overloads are not supported by the SQLite fallback provider. Preserve SQL-translatable comparison against normalized data; StringComparison overloads are not translated by EF.
                : query.Where(tag => tag.Name.ToUpper().Contains(uppercaseSearch));
#pragma warning restore CA1311, CA1862, CA1304, MA0011
        }

        var rows = await query
            .OrderBy(tag => tag.Name)
            .ThenBy(tag => tag.PlayerTagId)
            .Take(TagDefinitionLimits.MaxTagDefinitions + 1)
            .Select(tag => new TagDefinitionDto
            {
                PlayerTagId = tag.PlayerTagId,
                Name = tag.Name,
                Color = tag.Color,
                LifecycleStatus = tag.LifecycleStatus
            })
            .ToListAsync(cancellationToken);

        var hasMore = rows.Count > TagDefinitionLimits.MaxTagDefinitions;
        return new TagDefinitionListResult
        {
            Items = rows.Take(TagDefinitionLimits.MaxTagDefinitions).ToList().AsReadOnly(),
            HasMore = hasMore
        };
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<TagDefinitionDto>>> GetChoicesAsync(
        CancellationToken cancellationToken = default)
    {
        if (currentUserProvider.UserId is not long
            || currentUserProvider.ClubId is not long clubId)
        {
            LogTagDefinitionsForbidden(currentUserProvider.UserId ?? 0, isManagement: false);
            return ServiceProblem.Forbidden("You must be a club member to view tag definitions.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        // Creation and restoration enforce the active tag limit.
        // this Take is a hard defensive bound that keeps the result set bounded regardless of source.
        var rows = await db.PlayerTags
            .Where(tag => tag.ClubId == clubId && tag.LifecycleStatus == LifecycleStatus.Active)
            .OrderBy(tag => tag.Name)
            .ThenBy(tag => tag.PlayerTagId)
            .Take(TagDefinitionLimits.MaxTagDefinitions)
            .Select(tag => new TagDefinitionDto
            {
                PlayerTagId = tag.PlayerTagId,
                Name = tag.Name,
                Color = tag.Color,
                LifecycleStatus = tag.LifecycleStatus
            })
            .ToListAsync(cancellationToken);

        return rows.AsReadOnly();
    }

    /// <summary>
    /// Normalizes the optional management lifecycle filter. <c>all</c> and <c>null</c> both
    /// mean "no filter", so the caller applies no status predicate.
    /// </summary>
    /// <param name="lifecycleStatus">The incoming lifecycle filter.</param>
    /// <returns>The lifecycle state to query, or <see langword="null"/> for no filter.</returns>
    private static LifecycleStatus? NormalizeLifecycleStatus(string? lifecycleStatus)
#pragma warning disable CA1308 // Lowercase is required for this display text or ASCII route token, not for an identity comparison.
        => lifecycleStatus?.Trim().ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "active" => LifecycleStatus.Active,
            "archived" => LifecycleStatus.Archived,
            _ => null
        };

    /// <summary>
    /// Logs an attempted tag-definition read without the required authorization.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    /// <param name="isManagement">Whether the rejected read was the management list rather than the evaluator choices.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition read forbidden for UserId={UserId} (Management={IsManagement}).")]
    private partial void LogTagDefinitionsForbidden(long userId, bool isManagement);
}
