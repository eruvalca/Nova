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
/// Server-side implementation for campaign-participant roster and detail queries.
/// </summary>
/// <param name="readDbContextFactory">The read-only tenant-scoped context factory.</param>
/// <param name="currentUserProvider">The current user provider used for authorization checks.</param>
/// <param name="logger">The logger for expected authorization failures.</param>
public sealed partial class CampaignParticipantQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<CampaignParticipantQueryService> logger) : ICampaignParticipantQueryService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PagedResult<CampaignParticipantRosterItem>>> GetParticipantRosterAsync(
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
            var escapedSearch = EscapeLikePattern(normalizedSearch);
            var likePattern = $"%{escapedSearch}%";
            var isNpgsql = db.Database.IsNpgsql();
            query = query.Where(assignment => isNpgsql
                ? EF.Functions.ILike(assignment.Player.FirstName + " " + assignment.Player.LastName, likePattern, @"\")
                    || EF.Functions.ILike(assignment.Player.FirstName, likePattern, @"\")
                    || EF.Functions.ILike(assignment.Player.LastName, likePattern, @"\")
                : (assignment.Player.FirstName + " " + assignment.Player.LastName).ToUpper().Contains(uppercaseSearch)
                    || assignment.Player.FirstName.ToUpper().Contains(uppercaseSearch)
                    || assignment.Player.LastName.ToUpper().Contains(uppercaseSearch));
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
    public async Task<ServiceResult<CampaignParticipantDetailDto>> GetParticipantDetailAsync(
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

        var notes = await db.Notes
            .AsNoTracking()
            .Where(note => note.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId)
            .Select(note => new ParticipantNoteProjection(
                note.NoteId,
                note.Content,
                note.CreatedById,
                note.CreatedAt))
            .ToListAsync(cancellationToken);

        var orderedNotes = notes
            .OrderByDescending(note => note.CreatedAt)
            .ThenByDescending(note => note.NoteId)
            .ToList();

        var tagApplications = await db.CampaignTagApplications
            .AsNoTracking()
            .Where(application => application.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId)
            .Select(application => new ParticipantTagProjection(
                application.CampaignTagApplicationId,
                application.PlayerTagId,
                application.PlayerTag.Name,
                application.PlayerTag.Color,
                application.PlayerTag.LifecycleStatus == LifecycleStatus.Archived,
                application.CreatedById,
                application.CreatedAt))
            .ToListAsync(cancellationToken);

        var orderedTagApplications = tagApplications
            .OrderByDescending(application => application.CreatedAt)
            .ThenByDescending(application => application.CampaignTagApplicationId)
            .ToList();

        var actorIds = orderedNotes
            .Select(note => note.CreatedById)
            .Concat(orderedTagApplications.Select(application => application.CreatedById))
            .Where(id => id > 0)
            .Distinct()
            .ToArray();

        var actorDisplayNames = actorIds.Length == 0
            ? []
            : (await db.Users
                .AsNoTracking()
                .Where(user => actorIds.Contains(user.Id))
                .Select(user => new { user.Id, user.FirstName, user.LastName })
                .ToListAsync(cancellationToken))
                .ToDictionary(
                    row => row.Id,
                    row => string.Join(" ", new[] { row.FirstName, row.LastName }.Where(value => !string.IsNullOrWhiteSpace(value))),
                    EqualityComparer<long>.Default);

        var isActiveCampaign = assignment.CampaignStatus == CampaignStatus.Active;
        var isClubAdmin = currentUserProvider.IsClubAdmin;
        var canEditPlacement = isClubAdmin && isActiveCampaign && assignment.PlayerLifecycleStatus == LifecycleStatus.Active;
        var canAddNote = isActiveCampaign && currentUserId > 0;
        var canApplyTag = isActiveCampaign && currentUserId > 0;
        var canArchiveTagDefinitions = isClubAdmin;

        var noteDtos = orderedNotes
            .Select(note =>
            {
                var canEditOrDeleteNote = isActiveCampaign && (isClubAdmin || note.CreatedById == currentUserId);
                return new CampaignParticipantNoteDto(
                    note.NoteId,
                    note.Content,
                    actorDisplayNames.GetValueOrDefault(note.CreatedById) ?? "Unknown user",
                    note.CreatedAt,
                    canEditOrDeleteNote,
                    canEditOrDeleteNote);
            })
            .ToList()
            .AsReadOnly();

        var tagDtos = orderedTagApplications
            .Select(application => new CampaignParticipantTagApplicationDto(
                application.CampaignTagApplicationId,
                application.PlayerTagId,
                application.TagName,
                application.TagColor,
                application.IsArchived,
                actorDisplayNames.GetValueOrDefault(application.CreatedById) ?? "Unknown user",
                application.CreatedAt,
                isActiveCampaign && (isClubAdmin || (application.CreatedById == currentUserId && !application.IsArchived))))
            .ToList()
            .AsReadOnly();

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
            noteDtos,
            tagDtos,
            capabilities);
    }

    private static string EscapeLikePattern(string value)
    {
        return value
            .Replace("\\", "\\\\")
            .Replace("%", "\\%")
            .Replace("_", "\\_");
    }

    private static PlacementOutcome? NormalizeOutcome(string? outcome)
        => string.IsNullOrWhiteSpace(outcome)
            ? null
            : Enum.TryParse<PlacementOutcome>(outcome.Trim(), true, out var parsedOutcome)
                ? parsedOutcome
                : null;

    private static IQueryable<PlayerCampaignAssignmentEntity> ApplyOrdering(
        IQueryable<PlayerCampaignAssignmentEntity> query,
        string sortBy,
        string sortDirection)
    {
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        var normalizedSortBy = string.IsNullOrWhiteSpace(sortBy) ? "displayname" : sortBy.Trim();
        return normalizedSortBy.ToLowerInvariant() switch
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

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning, Message = "User {UserId} attempted to access a campaign roster without a club scope.")]
    private partial void LogForbiddenRosterAccess(long userId);

    [LoggerMessage(EventId = 2, Level = LogLevel.Warning, Message = "User {UserId} attempted to access campaign {CampaignId} participant detail without a club scope.")]
    private partial void LogForbiddenDetailAccess(long userId, long campaignId);

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

    private sealed record RosterTagSummaryRow(
        long PlayerCampaignAssignmentId,
        long PlayerTagId,
        string TagName,
        string TagColor,
        bool IsArchived);

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

    private sealed record ParticipantNoteProjection(
        long NoteId,
        string Content,
        long CreatedById,
        DateTimeOffset CreatedAt);

    private sealed record ParticipantTagProjection(
        long CampaignTagApplicationId,
        long PlayerTagId,
        string TagName,
        string TagColor,
        bool IsArchived,
        long CreatedById,
        DateTimeOffset CreatedAt);

    private sealed record ActorDisplayNameProjection(
        long UserId,
        string DisplayName);
}
