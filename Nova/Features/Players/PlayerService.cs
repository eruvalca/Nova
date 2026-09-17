using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Common;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.Features.Players;

/// <summary>
/// Server-side implementation of <see cref="IPlayerService"/> for player-roster queries.
/// </summary>
/// <param name="readDbContextFactory">The read-only tenant-scoped context factory.</param>
/// <param name="currentUserProvider">The current user provider used for authorization checks.</param>
/// <param name="logger">The logger for expected authorization failures.</param>
internal sealed partial class PlayerService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<PlayerService> logger) : IPlayerService
{
    /// <inheritdoc />
#pragma warning disable MA0051 // Keep authorization, bounded database reads, and their result projection together for this query.
    public async Task<ServiceResult<PagedResult<PlayerListItem>>> GetPlayerRosterAsync(
#pragma warning restore MA0051
        GetPlayerRosterInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUserProvider.UserId is not long currentUserId)
        {
            return ServiceProblem.Forbidden("You must be signed in to view player rosters.");
        }

        if (currentUserProvider.ClubId is not long currentUserClubId || currentUserClubId != input.ClubId)
        {
            LogForbiddenRosterAccess(input.ClubId, currentUserId);
            return ServiceProblem.Forbidden("You do not have permission to view this club roster.");
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(input.Search) ? null : input.Search.Trim();
        var normalizedLifecycleStatus = NormalizeLifecycleStatus(input.LifecycleStatus);
        var normalizedSortBy = string.IsNullOrWhiteSpace(input.SortBy) ? "displayName" : input.SortBy.Trim();
        var normalizedSortDirection = string.IsNullOrWhiteSpace(input.SortDirection) ? "asc" : input.SortDirection.Trim();
        var page = input.Page ?? GetPlayerRosterInput.DefaultPage;
        var pageSize = input.PageSize ?? GetPlayerRosterInput.DefaultPageSize;
        var graduationYear = input.GraduationYear;
        var playerTagId = input.PlayerTagId;

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var isNpgsql = db.Database.IsNpgsql();
        var query = db.Players
            .Where(player => player.ClubId == currentUserClubId && player.LifecycleStatus == normalizedLifecycleStatus);

        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            var hasTryoutNumberSearch = int.TryParse(normalizedSearch, System.Globalization.CultureInfo.InvariantCulture, out var tryoutNumberSearch);
            if (isNpgsql)
            {
                var escapedSearch = LikePatternEscaper.EscapeLikePattern(normalizedSearch);
                var likePattern = $"%{escapedSearch}%";
                query = hasTryoutNumberSearch
                    ? query.Where(player =>
                        EF.Functions.ILike(player.FirstName + " " + player.LastName, likePattern, @"\")
                        || player.CampaignAssignments.Any(assignment =>
                            assignment.Campaign.Status == CampaignStatus.Active
                            && assignment.TryoutNumber == tryoutNumberSearch))
                    : query.Where(player => EF.Functions.ILike(player.FirstName + " " + player.LastName, likePattern, @"\"));
            }
            else
            {
#pragma warning disable CA1311, CA1304, MA0011 // This expression is translated to SQL UPPER; culture overloads are not supported by the SQLite fallback provider.
                var uppercaseSearch = normalizedSearch.ToUpper();
#pragma warning restore CA1311, CA1304, MA0011
                query = hasTryoutNumberSearch
                    ? query.Where(player =>
#pragma warning disable CA1311, CA1862, CA1304, MA0011 // This expression is translated to SQL UPPER; culture overloads are not supported by the SQLite fallback provider. Preserve SQL-translatable comparison against normalized data; StringComparison overloads are not translated by EF.
                        (player.FirstName + " " + player.LastName).ToUpper().Contains(uppercaseSearch)
#pragma warning restore CA1311, CA1862, CA1304, MA0011
                        || player.CampaignAssignments.Any(assignment =>
                            assignment.Campaign.Status == CampaignStatus.Active
                            && assignment.TryoutNumber == tryoutNumberSearch))
                    : query.Where(player =>
#pragma warning disable CA1311, CA1862, CA1304, MA0011 // This expression is translated to SQL UPPER; culture overloads are not supported by the SQLite fallback provider. Preserve SQL-translatable comparison against normalized data; StringComparison overloads are not translated by EF.
                        (player.FirstName + " " + player.LastName).ToUpper().Contains(uppercaseSearch));
#pragma warning restore CA1311, CA1862, CA1304, MA0011
            }
        }

        if (graduationYear is not null)
        {
            query = query.Where(player => player.GraduationYear == graduationYear.Value);
        }

        if (playerTagId is not null)
        {
            query = query.Where(player =>
                player.CampaignAssignments.Any(assignment =>
                    assignment.Campaign.Status == CampaignStatus.Active
                    && assignment.CampaignTagApplications.Any(application => application.PlayerTagId == playerTagId.Value)));
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue)
        {
            return new PagedResult<PlayerListItem>([], page, pageSize, totalCount);
        }
        IQueryable<PlayerEntity> orderedQuery;
        List<RosterPageRow> pageRows;
        if (string.Equals(normalizedSortBy, "joinedAt", StringComparison.OrdinalIgnoreCase))
        {
            var descending = string.Equals(normalizedSortDirection, "desc", StringComparison.OrdinalIgnoreCase);
            if (string.Equals(db.Database.ProviderName, "Microsoft.EntityFrameworkCore.Sqlite", StringComparison.Ordinal))
            {
                var joinedRows = await query
                    .Select(player => new RosterPageRow(
                        player.PlayerId,
                        player.FirstName,
                        player.LastName,
                        player.GraduationYear,
                        player.LifecycleStatus,
                        player.CreatedAt))
                    .ToListAsync(cancellationToken);

                var orderedJoinedRows = descending
                    ? joinedRows.OrderByDescending(player => player.CreatedAt).ThenBy(player => player.PlayerId)
                    : joinedRows.OrderBy(player => player.CreatedAt).ThenBy(player => player.PlayerId);

                pageRows = orderedJoinedRows
                    .Skip((int)offset)
                    .Take(pageSize)
                    .ToList();
            }
            else
            {
                orderedQuery = descending
                    ? query.OrderByDescending(player => player.CreatedAt).ThenBy(player => player.PlayerId)
                    : query.OrderBy(player => player.CreatedAt).ThenBy(player => player.PlayerId);
                pageRows = await orderedQuery
                    .Skip((int)offset)
                    .Take(pageSize)
                    .Select(player => new RosterPageRow(
                        player.PlayerId,
                        player.FirstName,
                        player.LastName,
                        player.GraduationYear,
                        player.LifecycleStatus,
                        player.CreatedAt))
                    .ToListAsync(cancellationToken);
            }
        }
        else
        {
            orderedQuery = BuildOrderedQuery(query, normalizedSortDirection);
            pageRows = await orderedQuery
                .Skip((int)offset)
                .Take(pageSize)
                .Select(player => new RosterPageRow(
                    player.PlayerId,
                    player.FirstName,
                    player.LastName,
                    player.GraduationYear,
                    player.LifecycleStatus,
                    player.CreatedAt))
                .ToListAsync(cancellationToken);
        }

        var playerIds = pageRows
            .Select(player => player.PlayerId)
            .ToArray();

        var activeCampaignRows = playerIds.Length == 0
            ? []
            : await db.PlayerCampaignAssignments
                .Where(assignment =>
                    playerIds.Contains(assignment.PlayerId)
                    && assignment.Campaign.Status == CampaignStatus.Active)
                .Select(assignment => new ActiveCampaignRow(assignment.PlayerId, assignment.Campaign.Name))
                .ToListAsync(cancellationToken);

        var tagRows = playerIds.Length == 0
            ? []
            : await db.CampaignTagApplications
                .Where(application =>
                    playerIds.Contains(application.PlayerCampaignAssignment.PlayerId)
                    && application.PlayerCampaignAssignment.Campaign.Status == CampaignStatus.Active)
                .Select(application => new ActiveTagRow(
                    application.PlayerCampaignAssignment.PlayerId,
                    application.PlayerTagId,
                    application.PlayerTag.Name,
                    application.PlayerTag.Color))
                .ToListAsync(cancellationToken);

        var activeCampaignsByPlayer = activeCampaignRows
            .GroupBy(row => row.PlayerId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<string>)group
                    .Select(row => row.CampaignName)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(name => name, StringComparer.Ordinal)
                    .ToList()
                    .AsReadOnly());

        var tagsByPlayer = tagRows
            .GroupBy(row => row.PlayerId)
            .ToDictionary(
                group => group.Key,
                group => (IReadOnlyList<PlayerRosterTagItem>)group
                    .GroupBy(row => new { row.PlayerTagId, row.TagName, row.TagColor })
                    .OrderBy(tag => tag.Key.TagName, StringComparer.Ordinal)
                    .ThenBy(tag => tag.Key.PlayerTagId)
                    .Select(tag => new PlayerRosterTagItem(
                        tag.Key.PlayerTagId,
                        tag.Key.TagName,
                        tag.Key.TagColor))
                    .ToList()
                    .AsReadOnly());

        var items = pageRows
            .Select(row => new PlayerListItem
            {
                PlayerId = row.PlayerId,
                DisplayName = $"{row.FirstName} {row.LastName}",
                GraduationYear = row.GraduationYear,
                LifecycleStatus = row.LifecycleStatus,
                CurrentTags = tagsByPlayer.GetValueOrDefault(row.PlayerId) ?? [],
                ActiveCampaigns = activeCampaignsByPlayer.GetValueOrDefault(row.PlayerId) ?? [],
                JoinedAt = row.CreatedAt
            })
            .ToList()
            .AsReadOnly();

        return new PagedResult<PlayerListItem>(items, page, pageSize, totalCount);
    }

    /// <summary>
    /// Applies display-name ordering for roster queries.
    /// </summary>
    /// <param name="query">The source roster query.</param>
    /// <param name="sortDirection">The normalized sort direction.</param>
    /// <returns>The ordered query.</returns>
    private static IQueryable<PlayerEntity> BuildOrderedQuery(
        IQueryable<PlayerEntity> query,
        string sortDirection)
    {
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return descending
            ? query.OrderByDescending(player => player.FirstName).ThenByDescending(player => player.LastName).ThenBy(player => player.PlayerId)
            : query.OrderBy(player => player.FirstName).ThenBy(player => player.LastName).ThenBy(player => player.PlayerId);
    }

    /// <summary>
    /// Normalizes the lifecycle-status view filter to a concrete enum value.
    /// </summary>
    /// <param name="lifecycleStatus">The optional incoming lifecycle filter string.</param>
    /// <returns>
    /// <see cref="LifecycleStatus.Archived"/> when the incoming value is <c>archived</c>;
    /// otherwise <see cref="LifecycleStatus.Active"/>.
    /// </returns>
    private static LifecycleStatus NormalizeLifecycleStatus(string? lifecycleStatus)
        => string.Equals(lifecycleStatus, "archived", StringComparison.OrdinalIgnoreCase)
            ? LifecycleStatus.Archived
            : LifecycleStatus.Active;

    /// <summary>
    /// Represents one ordered roster row projected before tag/campaign enrichment.
    /// </summary>
    /// <param name="PlayerId">The player identifier.</param>
    /// <param name="FirstName">The player first name.</param>
    /// <param name="LastName">The player last name.</param>
    /// <param name="GraduationYear">The player graduation year.</param>
    /// <param name="LifecycleStatus">The player lifecycle status.</param>
    /// <param name="CreatedAt">The roster-join timestamp.</param>
    private sealed record RosterPageRow(
        long PlayerId,
        string FirstName,
        string LastName,
        int GraduationYear,
        LifecycleStatus LifecycleStatus,
        DateTimeOffset CreatedAt);

    /// <summary>
    /// Represents one active-campaign row used for roster campaign summaries.
    /// </summary>
    /// <param name="PlayerId">The player identifier.</param>
    /// <param name="CampaignName">The active campaign display name.</param>
    private sealed record ActiveCampaignRow(long PlayerId, string CampaignName);

    /// <summary>
    /// Represents one active-campaign tag row used for roster tag summaries.
    /// </summary>
    /// <param name="PlayerId">The player identifier.</param>
    /// <param name="PlayerTagId">The tag-definition identifier.</param>
    /// <param name="TagName">The tag display name.</param>
    /// <param name="TagColor">The tag color token.</param>
    private sealed record ActiveTagRow(long PlayerId, long PlayerTagId, string TagName, string TagColor);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Forbidden player-roster access attempt for ClubId={ClubId} by UserId={UserId}.")]
    private partial void LogForbiddenRosterAccess(long clubId, long userId);
}
