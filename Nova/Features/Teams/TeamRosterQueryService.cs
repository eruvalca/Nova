using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Shared.Enums;
using Nova.Shared.Results;
using Nova.Shared.Teams;
using Nova.Shared.Validation;

namespace Nova.Features.Teams;

/// <summary>
/// Provides tenant-safe, read-only team roster projections.
/// </summary>
/// <param name="readDbContextFactory">The read-only context factory.</param>
/// <param name="currentUserProvider">The current user and club context.</param>
/// <param name="logger">The logger for rejected access attempts.</param>
public sealed partial class TeamRosterQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<TeamRosterQueryService> logger) : ITeamRosterService
{
    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<TeamRosterItem>>> GetRosterAsync(
        GetTeamRosterInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUserProvider.UserId is not long userId
            || currentUserProvider.ClubId is not long clubId)
        {
            LogForbiddenRosterAccess(currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You must be an approved club member to view team rosters.");
        }

        var lifecycleStatus = NormalizeLifecycleStatus(input.LifecycleStatus);
        var search = string.IsNullOrWhiteSpace(input.Search) ? null : input.Search.Trim();

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.Teams
            .Where(team => team.ClubId == clubId && team.LifecycleStatus == lifecycleStatus);

        if (search is not null)
        {
            query = db.Database.IsNpgsql()
                ? query.Where(team => EF.Functions.ILike(team.Name, $"%{search}%"))
                : query.Where(team => team.Name.ToUpper().Contains(search.ToUpper()));
        }

        if (input.GraduationYear is int graduationYear)
        {
            query = query.Where(team => team.GraduationYear == graduationYear);
        }

        var rows = await query
            .OrderBy(team => team.Name)
            .ThenBy(team => team.TeamId)
            .Select(team => new TeamRosterItem
            {
                TeamId = team.TeamId,
                Name = team.Name,
                GraduationYear = team.GraduationYear,
                LifecycleStatus = team.LifecycleStatus,
                ActivePlacementCount = team.PlayerAssignments.Count(assignment =>
                    assignment.Campaign.Status == CampaignStatus.Active
                    && assignment.PlacementOutcome == PlacementOutcome.Assigned)
            })
            .ToListAsync(cancellationToken);

        return rows.AsReadOnly();
    }

    /// <summary>
    /// Normalizes the optional lifecycle filter to the active default.
    /// </summary>
    /// <param name="lifecycleStatus">The incoming lifecycle filter.</param>
    /// <returns>The lifecycle state to query.</returns>
    private static LifecycleStatus NormalizeLifecycleStatus(string? lifecycleStatus)
        => string.Equals(lifecycleStatus, "archived", StringComparison.OrdinalIgnoreCase)
            ? LifecycleStatus.Archived
            : LifecycleStatus.Active;

    /// <summary>
    /// Logs an attempted roster read without an approved club membership.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Team roster access forbidden for UserId={UserId}.")]
    private partial void LogForbiddenRosterAccess(long userId);
}
