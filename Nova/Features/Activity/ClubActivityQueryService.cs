using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Shared.Features.Activity;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Activity;

/// <summary>
/// Provides tenant-safe, role-shaped read access to the club activity feed with deterministic
/// keyset paging. On PostgreSQL the visibility filter and keyset predicate are pushed into SQL;
/// on SQLite the club's rows are materialized and the same deterministic feed policy pages them
/// in memory so the projection and cursor rules stay identical across providers.
/// </summary>
/// <param name="readDbContextFactory">The read-only context factory.</param>
/// <param name="currentUserProvider">The current user and club context.</param>
/// <param name="logger">The logger for rejected access attempts.</param>
public sealed partial class ClubActivityQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<ClubActivityQueryService> logger) : IClubActivityQueryService
{
    /// <summary>
    /// The serializer options used to read persisted payloads. Payloads are written camelCase by
    /// <see cref="ActivityEventWriter"/>; the polymorphic discriminator and property matching must
    /// therefore be case-insensitive.
    /// </summary>
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
    };

    /// <inheritdoc />
    public async Task<ServiceResult<ClubActivityResult>> GetClubActivityAsync(
        GetClubActivityInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUserProvider.UserId is not long
            || currentUserProvider.ClubId is not long clubId)
        {
            LogForbiddenActivityAccess(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be an approved club member to view the club activity feed.");
        }

        var isAdmin = currentUserProvider.IsClubAdmin;
        var cursor = input.BeforeActivityEventId is long beforeId && input.BeforeOccurredAt is DateTimeOffset beforeAt
            ? new ClubActivityCursor(beforeId, beforeAt)
            : null;

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);

        IReadOnlyList<ActivityEventEntity> rows;
        if (db.Database.IsNpgsql())
        {
            // Push the role visibility filter and the keyset predicate into SQL so only the page
            // worth of rows is loaded; ordering matches the deterministic policy.
            IQueryable<ActivityEventEntity> query = db.ActivityEvents
                .AsNoTracking()
                .Where(row => row.ClubId == clubId)
                .Where(row => isAdmin || !row.IsAdminOnly)
                .OrderByDescending(row => row.CreatedAt)
                .ThenByDescending(row => row.ActivityEventId);

            if (cursor is not null)
            {
                query = query.Where(row =>
                    row.CreatedAt < cursor.OccurredAt
                    || (row.CreatedAt == cursor.OccurredAt && row.ActivityEventId < cursor.ActivityEventId));
            }

            rows = await query
                .Take(GetClubActivityInput.PageSize + 1)
                .ToListAsync(cancellationToken);
        }
        else
        {
            // SQLite cannot translate ORDER BY on DateTimeOffset columns. The club's rows are
            // materialized and the deterministic feed policy orders and pages them in memory, so
            // the projection and cursor rules stay identical across providers.
            rows = await db.ActivityEvents
                .AsNoTracking()
                .Where(row => row.ClubId == clubId)
                .ToListAsync(cancellationToken);
        }

        return ClubActivityFeedPolicy.BuildPage(rows, isAdmin, cursor, JsonOptions);
    }

    /// <summary>
    /// Logs an attempted feed read without an approved club membership.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Club activity feed access forbidden for UserId={UserId}.")]
    private partial void LogForbiddenActivityAccess(long userId);
}
