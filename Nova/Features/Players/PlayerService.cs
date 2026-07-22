using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Shared.Enums;
using Nova.Shared.Features.Players;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Players;

/// <summary>
/// Server-side implementation of <see cref="IPlayerService"/> for player-roster queries.
/// </summary>
/// <param name="readDbContextFactory">The read-only tenant-scoped context factory.</param>
/// <param name="currentUserProvider">The current user provider used for authorization checks.</param>
/// <param name="logger">The logger for expected authorization failures.</param>
public sealed partial class PlayerService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<PlayerService> logger) : IPlayerService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PagedResult<PlayerListItem>>> GetPlayerRosterAsync(
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
        var normalizedSortBy = string.IsNullOrWhiteSpace(input.SortBy) ? "displayName" : input.SortBy.Trim();
        var normalizedSortDirection = string.IsNullOrWhiteSpace(input.SortDirection) ? "asc" : input.SortDirection.Trim();
        var page = Math.Max(GetPlayerRosterInput.DefaultPage, input.Page);
        var pageSize = Math.Clamp(input.PageSize, 1, GetPlayerRosterInput.MaxPageSize);

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var isNpgsql = db.Database.IsNpgsql();
        var query = db.Players
            .Where(player => player.ClubId == currentUserClubId && player.LifecycleStatus == LifecycleStatus.Active);

        if (!string.IsNullOrEmpty(normalizedSearch))
        {
            if (isNpgsql)
            {
                query = query.Where(player => EF.Functions.ILike(player.FirstName + " " + player.LastName, $"%{normalizedSearch}%"));
            }
            else
            {
                var uppercaseSearch = normalizedSearch.ToUpperInvariant();
                query = query.Where(player =>
                    (player.FirstName + " " + player.LastName).ToUpper().Contains(uppercaseSearch));
            }
        }

        var totalCount = await query.CountAsync(cancellationToken);
        if (string.Equals(normalizedSortBy, "joinedAt", StringComparison.OrdinalIgnoreCase))
        {
            var descending = string.Equals(normalizedSortDirection, "desc", StringComparison.OrdinalIgnoreCase);
            if (db.Database.IsSqlite())
            {
                // SQLite cannot translate DateTimeOffset ORDER BY in EF Core; retain deterministic
                // behavior in tests by ordering in memory only for this provider.
                var joinedRows = await query
                    .Select(player => new
                    {
                        player.PlayerId,
                        player.FirstName,
                        player.LastName,
                        player.CreatedAt
                    })
                    .ToListAsync(cancellationToken);

                var orderedJoinedRows = descending
                    ? joinedRows.OrderByDescending(player => player.CreatedAt).ThenBy(player => player.PlayerId)
                    : joinedRows.OrderBy(player => player.CreatedAt).ThenBy(player => player.PlayerId);

                var joinedItems = orderedJoinedRows
                    .Skip((page - 1) * pageSize)
                    .Take(pageSize)
                    .Select(player => new PlayerListItem(
                        player.PlayerId,
                        player.FirstName + " " + player.LastName,
                        player.CreatedAt))
                    .ToList()
                    .AsReadOnly();

                return new PagedResult<PlayerListItem>(joinedItems, page, pageSize, totalCount);
            }

            var joinedAtQuery = descending
                ? query.OrderByDescending(player => player.CreatedAt).ThenBy(player => player.PlayerId)
                : query.OrderBy(player => player.CreatedAt).ThenBy(player => player.PlayerId);
            var joinedAtPlayers = await joinedAtQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(player => new PlayerListItem(
                    player.PlayerId,
                    player.FirstName + " " + player.LastName,
                    player.CreatedAt))
                .ToListAsync(cancellationToken);

            return new PagedResult<PlayerListItem>(joinedAtPlayers.AsReadOnly(), page, pageSize, totalCount);
        }

        var orderedQuery = BuildOrderedQuery(query, normalizedSortDirection);
        var players = await orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(player => new PlayerListItem(
                player.PlayerId,
                player.FirstName + " " + player.LastName,
                player.CreatedAt))
            .ToListAsync(cancellationToken);

        return new PagedResult<PlayerListItem>(players.AsReadOnly(), page, pageSize, totalCount);
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

    [LoggerMessage(Level = LogLevel.Warning, Message = "Forbidden player-roster access attempt for ClubId={ClubId} by UserId={UserId}.")]
    private partial void LogForbiddenRosterAccess(long clubId, long userId);
}
