using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using Nova.Shared.Features.Tags;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Tags;

/// <summary>
/// Provides tenant-safe, read-only tag-definition projections for club administrators and club members.
/// </summary>
/// <param name="readDbContextFactory">The read-only context factory.</param>
/// <param name="currentUserProvider">The current user and club context.</param>
/// <param name="logger">The logger for rejected access attempts.</param>
public sealed partial class TagDefinitionQueryService(
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
            var escapedSearch = EscapeLikePattern(search);
            query = db.Database.IsNpgsql()
                ? query.Where(tag => EF.Functions.ILike(tag.Name, $"%{escapedSearch}%", @"\"))
                : query.Where(tag => tag.Name.ToUpper().Contains(uppercaseSearch));
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
        // The create/restore paths cap active definitions at TagDefinitionLimits.MaxActiveTagDefinitions,
        // so this Take is a defensive bound that only binds for legacy clubs already above the cap.
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
        => lifecycleStatus?.Trim().ToLowerInvariant() switch
        {
            "active" => LifecycleStatus.Active,
            "archived" => LifecycleStatus.Archived,
            _ => null
        };

    /// <summary>
    /// Escapes <c>ILIKE</c> pattern metacharacters so that a user-supplied search term is treated
    /// as a literal substring. Backslash is escaped first to avoid double-escaping, then
    /// <c>%</c> and <c>_</c> are escaped with the backslash escape character.
    /// </summary>
    /// <param name="value">The raw user search term.</param>
    /// <returns>The term with <c>\</c>, <c>%</c>, and <c>_</c> escaped for use in an <c>ILIKE '%…%' ESCAPE '\'</c> pattern.</returns>
    private static string EscapeLikePattern(string value)
        => value.Replace(@"\", @"\\").Replace("%", @"\%").Replace("_", @"\_");

    /// <summary>
    /// Logs an attempted tag-definition read without the required authorization.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    /// <param name="isManagement">Whether the rejected read was the management list rather than the evaluator choices.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Tag-definition read forbidden for UserId={UserId} (Management={IsManagement}).")]
    private partial void LogTagDefinitionsForbidden(long userId, bool isManagement);
}
