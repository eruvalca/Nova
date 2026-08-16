using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Shared.Enums;
using Nova.Shared.Features.Campaigns;
using Nova.Shared.Results;
using Nova.Shared.Validation;

namespace Nova.Features.Campaigns;

/// <summary>
/// Server-side implementation for tenant-safe campaign placement roster and summary queries.
/// </summary>
/// <param name="readDbContextFactory">The read-only tenant-scoped context factory.</param>
/// <param name="currentUserProvider">The current user provider used for authorization checks.</param>
/// <param name="logger">The logger for expected authorization failures.</param>
public sealed partial class CampaignPlacementQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignPlacementQueryService> logger) : ICampaignPlacementQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PagedResult<CampaignPlacementRosterItem>>> GetPlacementRosterAsync(
        GetCampaignPlacementRosterInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUserProvider.UserId is not long currentUserId)
        {
            return ServiceProblem.Forbidden("You must be signed in to view campaign placements.");
        }

        if (currentUserProvider.ClubId is not long currentClubId)
        {
            LogForbiddenRosterAccess(currentUserId);
            return ServiceProblem.Forbidden("You do not have permission to view this campaign's placements.");
        }

        var page = input.Page ?? GetCampaignPlacementRosterInput.DefaultPage;
        var pageSize = input.PageSize ?? GetCampaignPlacementRosterInput.DefaultPageSize;

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var campaignExists = await db.Campaigns
            .AsNoTracking()
            .AnyAsync(campaign => campaign.ClubId == currentClubId && campaign.CampaignId == input.CampaignId, cancellationToken);
        if (!campaignExists)
        {
            return ServiceProblem.NotFound();
        }

        var query = db.PlayerCampaignAssignments
            .Where(assignment => assignment.ClubId == currentClubId && assignment.CampaignId == input.CampaignId);

        if (input.GraduationYear is int graduationYear)
        {
            query = query.Where(assignment => assignment.Player.GraduationYear == graduationYear);
        }

        if (input.UnresolvedOnly == true)
        {
            query = query.Where(assignment => assignment.PlacementOutcome == PlacementOutcome.Undecided);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var pageRows = await query
            .OrderBy(assignment => assignment.Player.LastName)
            .ThenBy(assignment => assignment.Player.FirstName)
            .ThenBy(assignment => assignment.PlayerCampaignAssignmentId)
            .Select(assignment => new PlacementRosterPageRow(
                assignment.PlayerCampaignAssignmentId,
                assignment.PlayerId,
                assignment.Player.FirstName,
                assignment.Player.LastName,
                assignment.Player.GraduationYear,
                assignment.PlacementOutcome,
                assignment.TeamId,
                assignment.Team != null ? assignment.Team.Name : null,
                assignment.ConcurrencyToken))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var items = pageRows
            .Select(row => new CampaignPlacementRosterItem(
                row.PlayerCampaignAssignmentId,
                row.PlayerId,
                string.Join(" ", new[] { row.FirstName, row.LastName }.Where(value => !string.IsNullOrWhiteSpace(value))),
                row.GraduationYear,
                row.PlacementOutcome,
                row.TeamId is null
                    ? null
                    : new CampaignParticipantTeamSummaryDto(row.TeamId.Value, row.TeamName ?? string.Empty),
                row.ConcurrencyToken))
            .ToList()
            .AsReadOnly();

        return new PagedResult<CampaignPlacementRosterItem>(items, page, pageSize, totalCount);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<CampaignPlacementSummaryDto>> GetPlacementSummaryAsync(
        GetCampaignPlacementSummaryInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUserProvider.UserId is not long currentUserId)
        {
            return ServiceProblem.Forbidden("You must be signed in to view campaign placements.");
        }

        if (currentUserProvider.ClubId is not long currentClubId)
        {
            LogForbiddenSummaryAccess(currentUserId, input.CampaignId);
            return ServiceProblem.Forbidden("You do not have permission to view this campaign's placements.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var campaignExists = await db.Campaigns
            .AsNoTracking()
            .AnyAsync(campaign => campaign.ClubId == currentClubId && campaign.CampaignId == input.CampaignId, cancellationToken);
        if (!campaignExists)
        {
            return ServiceProblem.NotFound();
        }

        // Whole-campaign counts in one grouped SQL statement, independent of paging and filters.
        var outcomeRows = await db.PlayerCampaignAssignments
            .AsNoTracking()
            .Where(assignment => assignment.ClubId == currentClubId && assignment.CampaignId == input.CampaignId)
            .GroupBy(assignment => assignment.PlacementOutcome)
            .Select(group => new PlacementOutcomeCountRow(group.Key, group.Count()))
            .ToListAsync(cancellationToken);

        var assignedCount = outcomeRows.FirstOrDefault(row => row.Outcome == PlacementOutcome.Assigned)?.Count ?? 0;
        var notSelectedCount = outcomeRows.FirstOrDefault(row => row.Outcome == PlacementOutcome.NotSelected)?.Count ?? 0;
        var withdrawnCount = outcomeRows.FirstOrDefault(row => row.Outcome == PlacementOutcome.Withdrawn)?.Count ?? 0;
        var undecidedCount = outcomeRows.FirstOrDefault(row => row.Outcome == PlacementOutcome.Undecided)?.Count ?? 0;
        var totalCount = outcomeRows.Sum(row => row.Count);

        return new CampaignPlacementSummaryDto(
            assignedCount,
            notSelectedCount,
            withdrawnCount,
            undecidedCount,
            totalCount);
    }

    /// <summary>
    /// Logs a placement roster read rejected because the caller is not scoped to a club.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "User {UserId} attempted to access a campaign placement roster without a club scope.")]
    private partial void LogForbiddenRosterAccess(long userId);

    /// <summary>
    /// Logs a placement summary read rejected because the caller is not scoped to a club.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    /// <param name="campaignId">The campaign whose placement summary was requested.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "User {UserId} attempted to read campaign {CampaignId} placement summary without a club scope.")]
    private partial void LogForbiddenSummaryAccess(long userId, long campaignId);

    /// <summary>
    /// Projection of one placement roster row, flattened from the assignment, its player, and its optional team.
    /// </summary>
    private sealed record PlacementRosterPageRow(
        long PlayerCampaignAssignmentId,
        long PlayerId,
        string FirstName,
        string LastName,
        int GraduationYear,
        PlacementOutcome PlacementOutcome,
        long? TeamId,
        string? TeamName,
        Guid ConcurrencyToken);

    /// <summary>
    /// Projection of one outcome count from the grouped summary query.
    /// </summary>
    private sealed record PlacementOutcomeCountRow(
        PlacementOutcome Outcome,
        int Count);
}
