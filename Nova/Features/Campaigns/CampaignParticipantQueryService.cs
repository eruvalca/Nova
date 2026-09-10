using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Features.Common;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.Features.Campaigns;

/// <summary>
/// Server-side implementation for campaign-participant roster and detail queries.
/// </summary>
/// <param name="readDbContextFactory">The read-only tenant-scoped context factory.</param>
/// <param name="currentUserProvider">The current user provider used for authorization checks.</param>
/// <param name="logger">The logger for expected authorization failures.</param>
internal sealed partial class CampaignParticipantQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignParticipantQueryService> logger) : ICampaignParticipantQueryService
{
    /// <inheritdoc />
#pragma warning disable MA0051 // Keep authorization, bounded database reads, and their result projection together for this query.
    public async Task<ServiceResult<PagedResult<CampaignParticipantRosterItem>>> GetParticipantRosterAsync(
#pragma warning restore MA0051
        GetCampaignParticipantRosterInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUserProvider.UserId is not long currentUserId)
        {
            return ServiceProblem.Forbidden("You must be signed in to view campaign participants.");
        }

        if (currentUserProvider.ClubId is not long currentClubId)
        {
            LogForbiddenRosterAccess(currentUserId);
            return ServiceProblem.Forbidden("You do not have permission to view this campaign roster.");
        }

        var normalizedSearch = string.IsNullOrWhiteSpace(input.Search) ? null : input.Search.Trim();
        var normalizedSortBy = string.IsNullOrWhiteSpace(input.SortBy) ? "displayName" : input.SortBy.Trim();
        var normalizedSortDirection = string.IsNullOrWhiteSpace(input.SortDirection) ? "asc" : input.SortDirection.Trim();
        var page = input.Page ?? GetCampaignParticipantRosterInput.DefaultPage;
        var pageSize = input.PageSize ?? GetCampaignParticipantRosterInput.DefaultPageSize;
        if (page < 1 || pageSize < 1 || page > int.MaxValue / pageSize)
        {
            return ServiceProblem.Validation(nameof(input.Page), "The page number is too large for the requested page size.");
        }
        var normalizedOutcome = NormalizeOutcome(input.Outcome);
        var graduationYears = input.GraduationYears?.Distinct().ToArray();
        var tagDefinitionIds = input.TagDefinitionIds?.Distinct().ToArray();

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

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var uppercaseSearch = normalizedSearch.ToUpperInvariant();
            var escapedSearch = LikePatternEscaper.EscapeLikePattern(normalizedSearch);
            var likePattern = $"%{escapedSearch}%";
            var isNpgsql = db.Database.IsNpgsql();
            query = query.Where(assignment => isNpgsql
                ? EF.Functions.ILike(assignment.Player.FirstName + " " + assignment.Player.LastName, likePattern, @"\")
                    || EF.Functions.ILike(assignment.Player.FirstName, likePattern, @"\")
                    || EF.Functions.ILike(assignment.Player.LastName, likePattern, @"\")
#pragma warning disable CA1311, CA1862, CA1304, MA0011 // This expression is translated to SQL UPPER; culture overloads are not supported by the SQLite fallback provider. Preserve SQL-translatable comparison against normalized data; StringComparison overloads are not translated by EF.
                : (assignment.Player.FirstName + " " + assignment.Player.LastName).ToUpper().Contains(uppercaseSearch)
#pragma warning restore CA1311, CA1862, CA1304, MA0011
#pragma warning disable CA1311, CA1862, CA1304, MA0011 // This expression is translated to SQL UPPER; culture overloads are not supported by the SQLite fallback provider. Preserve SQL-translatable comparison against normalized data; StringComparison overloads are not translated by EF.
                    || assignment.Player.FirstName.ToUpper().Contains(uppercaseSearch)
#pragma warning restore CA1311, CA1862, CA1304, MA0011
#pragma warning disable CA1311, CA1862, CA1304, MA0011 // This expression is translated to SQL UPPER; culture overloads are not supported by the SQLite fallback provider. Preserve SQL-translatable comparison against normalized data; StringComparison overloads are not translated by EF.
                    || assignment.Player.LastName.ToUpper().Contains(uppercaseSearch));
#pragma warning restore CA1311, CA1862, CA1304, MA0011
        }

        if (graduationYears is { Length: > 0 })
        {
            query = query.Where(assignment => graduationYears.Contains(assignment.Player.GraduationYear));
        }

        if (tagDefinitionIds is { Length: > 0 })
        {
            var visibleTagIds = await db.PlayerTags
                .AsNoTracking()
                .Where(tag => tag.ClubId == currentClubId && tagDefinitionIds.Contains(tag.PlayerTagId))
                .Select(tag => tag.PlayerTagId)
                .ToArrayAsync(cancellationToken);

            if (visibleTagIds.Length != tagDefinitionIds.Length)
            {
                return ServiceProblem.NotFound();
            }

            query = query.Where(assignment => assignment.CampaignTagApplications.Any(application => visibleTagIds.Contains(application.PlayerTagId)));
        }

        if (normalizedOutcome is not null)
        {
            query = query.Where(assignment => assignment.PlacementOutcome == normalizedOutcome.Value);
        }

        if (input.TeamId is not null)
        {
            var teamExists = await db.Teams
                .AsNoTracking()
                .AnyAsync(team => team.ClubId == currentClubId && team.TeamId == input.TeamId.Value, cancellationToken);
            if (!teamExists)
            {
                return ServiceProblem.NotFound();
            }

            query = query.Where(assignment => assignment.TeamId == input.TeamId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var orderedQuery = ApplyOrdering(query, normalizedSortBy, normalizedSortDirection);
        var pageAssignments = await orderedQuery
            .Select(assignment => new RosterPageRow(
                assignment.PlayerCampaignAssignmentId,
                assignment.PlayerId,
                assignment.Player.FirstName,
                assignment.Player.LastName,
                assignment.Player.GraduationYear,
                assignment.TryoutNumber,
                assignment.PlacementOutcome,
                assignment.TeamId,
                assignment.Team != null ? assignment.Team.Name : null))
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        if (pageAssignments.Count == 0)
        {
            return new PagedResult<CampaignParticipantRosterItem>([], page, pageSize, totalCount);
        }

        var assignmentIds = pageAssignments.Select(row => row.PlayerCampaignAssignmentId).ToArray();
        var tagRows = assignmentIds.Length == 0
            ? []
            : await db.CampaignTagApplications
                .AsNoTracking()
                .Where(application => assignmentIds.Contains(application.PlayerCampaignAssignmentId))
                .Select(application => new RosterTagSummaryRow(
                    application.PlayerCampaignAssignmentId,
                    application.PlayerTagId,
                    application.PlayerTag.Name,
                    application.PlayerTag.Color,
                    application.PlayerTag.LifecycleStatus == LifecycleStatus.Archived))
                .ToListAsync(cancellationToken);

        var tagsByAssignmentId = tagRows
            .GroupBy(row => row.PlayerCampaignAssignmentId)
            .ToDictionary(
                group => group.Key,
                group => group.Select(row => new CampaignParticipantTagSummaryDto(row.PlayerTagId, row.TagName, row.TagColor, row.IsArchived)).ToList().AsReadOnly(),
                EqualityComparer<long>.Default);

        var pageRows = pageAssignments
            .Select(row => new CampaignParticipantRosterItem(
                row.PlayerCampaignAssignmentId,
                row.PlayerId,
                string.Join(" ", new[] { row.FirstName, row.LastName }.Where(value => !string.IsNullOrWhiteSpace(value))),
                row.GraduationYear,
                row.TryoutNumber,
                row.PlacementOutcome,
                row.TeamId is null
                    ? null
                    : new CampaignParticipantTeamSummaryDto(row.TeamId.Value, row.TeamName ?? string.Empty),
                tagsByAssignmentId.GetValueOrDefault(row.PlayerCampaignAssignmentId, [])))
            .ToList()
            .AsReadOnly();

        return new PagedResult<CampaignParticipantRosterItem>(pageRows.ToList(), page, pageSize, totalCount);
    }

    /// <inheritdoc />
#pragma warning disable MA0051 // Keep authorization, bounded database reads, and their result projection together for this query.
    public async Task<ServiceResult<CampaignParticipantDetailDto>> GetParticipantDetailAsync(
#pragma warning restore MA0051
        GetCampaignParticipantDetailInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUserProvider.UserId is not long currentUserId)
        {
            return ServiceProblem.Forbidden("You must be signed in to view campaign participants.");
        }

        if (currentUserProvider.ClubId is not long currentClubId)
        {
            LogForbiddenDetailAccess(currentUserId, input.CampaignId);
            return ServiceProblem.Forbidden("You do not have permission to view this campaign participant.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        if (!await db.Users.AnyAsync(user => user.Id == currentUserId && user.ClubId == currentClubId, cancellationToken))
        {
            return ServiceProblem.Forbidden("You must currently belong to this club to view the participant.");
        }

        var campaignExists = await db.Campaigns
            .AsNoTracking()
            .AnyAsync(campaign => campaign.ClubId == currentClubId && campaign.CampaignId == input.CampaignId, cancellationToken);
        if (!campaignExists)
        {
            return ServiceProblem.NotFound();
        }

        var assignment = await db.PlayerCampaignAssignments
            .AsNoTracking()
            .Where(assignment =>
                assignment.ClubId == currentClubId
                && assignment.CampaignId == input.CampaignId
                && assignment.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId)
            .Select(assignment => new ParticipantDetailProjection(
                assignment.PlayerCampaignAssignmentId,
                assignment.PlayerId,
                assignment.Player.FirstName,
                assignment.Player.LastName,
                assignment.Player.GraduationYear,
                assignment.TryoutNumber,
                assignment.PlacementOutcome,
                assignment.TeamId,
                assignment.Team != null ? assignment.Team.Name : null,
                assignment.Campaign.Status,
                assignment.Player.LifecycleStatus,
                assignment.ConcurrencyToken,
                assignment.CreatedAt,
                assignment.ModifiedAt))
            .FirstOrDefaultAsync(cancellationToken);

        if (assignment is null)
        {
            return ServiceProblem.NotFound();
        }

        var isActiveCampaign = assignment.CampaignStatus == CampaignStatus.Active && assignment.PlayerLifecycleStatus == LifecycleStatus.Active;
        var isClubAdmin = currentUserProvider.IsClubAdmin;
        var canEditPlacement = isActiveCampaign;
        var canAddNote = isActiveCampaign;
        var canApplyTag = isActiveCampaign;
        var canArchiveTagDefinitions = isClubAdmin;
        var capabilities = new CampaignParticipantCapabilitiesDto(
            canEditPlacement,
            canAddNote,
            canApplyTag,
            canArchiveTagDefinitions);

        return new CampaignParticipantDetailDto(
            assignment.PlayerCampaignAssignmentId,
            assignment.PlayerId,
            string.Join(" ", new[] { assignment.FirstName, assignment.LastName }.Where(value => !string.IsNullOrWhiteSpace(value))),
            assignment.GraduationYear,
            assignment.TryoutNumber,
            assignment.PlacementOutcome,
            assignment.TeamId is null
                ? null
                : new CampaignParticipantTeamSummaryDto(assignment.TeamId.Value, assignment.TeamName ?? string.Empty),
            assignment.CreatedAt,
            assignment.ModifiedAt,
            assignment.CampaignStatus,
            assignment.ConcurrencyToken,
            capabilities);
    }

    /// <inheritdoc />
    public async Task<ServiceResult<IReadOnlyList<int>>> GetRosterGraduationYearsAsync(
        GetCampaignParticipantGraduationYearsInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUserProvider.UserId is not long currentUserId)
        {
            return ServiceProblem.Forbidden("You must be signed in to view campaign participants.");
        }

        if (currentUserProvider.ClubId is not long currentClubId)
        {
            LogForbiddenGraduationYearsAccess(currentUserId, input.CampaignId);
            return ServiceProblem.Forbidden("You do not have permission to view this campaign roster.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var campaignExists = await db.Campaigns
            .AsNoTracking()
            .AnyAsync(campaign => campaign.ClubId == currentClubId && campaign.CampaignId == input.CampaignId, cancellationToken);
        if (!campaignExists)
        {
            return ServiceProblem.NotFound();
        }

        // No artificial bound is needed: distinct graduation years over one campaign roster are
        // inherently small (a handful of class years), so the server never truncates the result.
        var years = await db.PlayerCampaignAssignments
            .AsNoTracking()
            .Where(assignment => assignment.ClubId == currentClubId && assignment.CampaignId == input.CampaignId)
            .Select(assignment => assignment.Player.GraduationYear)
            .Distinct()
            .OrderBy(year => year)
            .ToListAsync(cancellationToken);

        return years.AsReadOnly();
    }

    /// <summary>
    /// Normalizes a placement-outcome filter string to its enum value, ignoring case and whitespace.
    /// </summary>
    /// <param name="outcome">The raw outcome filter, or <see langword="null"/> when absent.</param>
    /// <returns>The parsed <see cref="PlacementOutcome"/>, or <see langword="null"/> when blank or unparseable.</returns>
    private static PlacementOutcome? NormalizeOutcome(string? outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome))
        {
            return null;
        }

        return Enum.TryParse<PlacementOutcome>(outcome.Trim(), true, out var parsedOutcome) ? parsedOutcome : null;
    }

    /// <summary>
    /// Applies the requested roster sort key and direction with a stable assignment-id tie-breaker.
    /// </summary>
    /// <param name="query">The filtered roster query to order.</param>
    /// <param name="sortBy">The requested sort key.</param>
    /// <param name="sortDirection">The requested sort direction.</param>
    /// <returns>The query with ordering applied.</returns>
    private static IQueryable<PlayerCampaignAssignmentEntity> ApplyOrdering(
        IQueryable<PlayerCampaignAssignmentEntity> query,
        string sortBy,
        string sortDirection)
    {
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        var normalizedSortBy = string.IsNullOrWhiteSpace(sortBy) ? "displayname" : sortBy.Trim();
#pragma warning disable CA1308 // Lowercase is required for this display text or ASCII route token, not for an identity comparison.
        return normalizedSortBy.ToLowerInvariant() switch
#pragma warning restore CA1308
        {
            "assignmentid" => descending
                ? query.OrderByDescending(assignment => assignment.PlayerCampaignAssignmentId).ThenBy(assignment => assignment.PlayerCampaignAssignmentId)
                : query.OrderBy(assignment => assignment.PlayerCampaignAssignmentId).ThenBy(assignment => assignment.PlayerCampaignAssignmentId),
            "graduationyear" => descending
                ? query.OrderByDescending(assignment => assignment.Player.GraduationYear).ThenBy(assignment => assignment.PlayerCampaignAssignmentId)
                : query.OrderBy(assignment => assignment.Player.GraduationYear).ThenBy(assignment => assignment.PlayerCampaignAssignmentId),
            "tryoutnumber" => descending
                ? query.OrderByDescending(assignment => assignment.TryoutNumber ?? int.MinValue).ThenBy(assignment => assignment.PlayerCampaignAssignmentId)
                : query.OrderBy(assignment => assignment.TryoutNumber ?? int.MaxValue).ThenBy(assignment => assignment.PlayerCampaignAssignmentId),
            "outcome" => descending
                ? query.OrderByDescending(assignment => assignment.PlacementOutcome).ThenBy(assignment => assignment.PlayerCampaignAssignmentId)
                : query.OrderBy(assignment => assignment.PlacementOutcome).ThenBy(assignment => assignment.PlayerCampaignAssignmentId),
            "teamname" => descending
                ? query.OrderByDescending(assignment => assignment.Team != null ? assignment.Team.Name : string.Empty).ThenBy(assignment => assignment.PlayerCampaignAssignmentId)
                : query.OrderBy(assignment => assignment.Team != null ? assignment.Team.Name : string.Empty).ThenBy(assignment => assignment.PlayerCampaignAssignmentId),
            _ => descending
                ? query.OrderByDescending(assignment => assignment.Player.LastName)
                    .ThenByDescending(assignment => assignment.Player.FirstName)
                    .ThenBy(assignment => assignment.PlayerCampaignAssignmentId)
                : query.OrderBy(assignment => assignment.Player.LastName)
                    .ThenBy(assignment => assignment.Player.FirstName)
                    .ThenBy(assignment => assignment.PlayerCampaignAssignmentId)
        };
    }

    /// <summary>
    /// Logs a roster read rejected because the caller is not scoped to a club.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "User {UserId} attempted to access a campaign roster without a club scope.")]
    private partial void LogForbiddenRosterAccess(long userId);

    /// <summary>
    /// Logs a participant-detail read rejected because the caller is not scoped to a club.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    /// <param name="campaignId">The campaign whose participant detail was requested.</param>
    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "User {UserId} attempted to access campaign {CampaignId} participant detail without a club scope.")]
    private partial void LogForbiddenDetailAccess(long userId, long campaignId);

    /// <summary>
    /// Logs a graduation-years read rejected because the caller is not scoped to a club.
    /// </summary>
    /// <param name="userId">The current user identifier, or zero when unavailable.</param>
    /// <param name="campaignId">The campaign whose roster graduation years were requested.</param>
    [LoggerMessage(EventId = 3, Level = LogLevel.Warning, Message = "User {UserId} attempted to read campaign {CampaignId} roster graduation years without a club scope.")]
    private partial void LogForbiddenGraduationYearsAccess(long userId, long campaignId);

    /// <summary>
    /// Projection of one roster row, flattened from the assignment and its player.
    /// </summary>
    private sealed record RosterPageRow(
        long PlayerCampaignAssignmentId,
        long PlayerId,
        string FirstName,
        string LastName,
        int GraduationYear,
        int? TryoutNumber,
        PlacementOutcome PlacementOutcome,
        long? TeamId,
        string? TeamName);

    /// <summary>
    /// Projection of one tag application attached to a roster row.
    /// </summary>
    private sealed record RosterTagSummaryRow(
        long PlayerCampaignAssignmentId,
        long PlayerTagId,
        string TagName,
        string TagColor,
        bool IsArchived);

    /// <summary>
    /// Projection of one participant detail, flattened from the assignment and its player.
    /// </summary>
    private sealed record ParticipantDetailProjection(
        long PlayerCampaignAssignmentId,
        long PlayerId,
        string FirstName,
        string LastName,
        int GraduationYear,
        int? TryoutNumber,
        PlacementOutcome PlacementOutcome,
        long? TeamId,
        string? TeamName,
        CampaignStatus CampaignStatus,
        LifecycleStatus PlayerLifecycleStatus,
        Guid ConcurrencyToken,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ModifiedAt);

}
