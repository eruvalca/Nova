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
        var normalizedOutcome = NormalizeOutcome(input.Outcome);
        var graduationYears = input.GraduationYears?.Where(year => year > 0).Distinct().ToArray();
        var tagDefinitionIds = input.TagDefinitionIds?.Where(id => id > 0).Distinct().ToArray();

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var query = db.PlayerCampaignAssignments
            .Where(assignment => assignment.ClubId == currentClubId && assignment.CampaignId == input.CampaignId);

        if (!string.IsNullOrWhiteSpace(normalizedSearch))
        {
            var uppercaseSearch = normalizedSearch.ToUpperInvariant();
            var isNpgsql = db.Database.IsNpgsql();
            query = query.Where(assignment => isNpgsql
                ? EF.Functions.ILike(assignment.Player.FirstName + " " + assignment.Player.LastName, $"%{normalizedSearch}%")
                    || EF.Functions.ILike(assignment.Player.FirstName, $"%{normalizedSearch}%")
                    || EF.Functions.ILike(assignment.Player.LastName, $"%{normalizedSearch}%")
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
            query = query.Where(assignment => assignment.CampaignTagApplications.Any(application => tagDefinitionIds.Contains(application.PlayerTagId)));
        }

        if (normalizedOutcome is not null)
        {
            query = query.Where(assignment => assignment.PlacementOutcome == normalizedOutcome.Value);
        }

        if (input.TeamId is not null)
        {
            query = query.Where(assignment => assignment.TeamId == input.TeamId.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken);
        var projectedRows = await query
            .Select(assignment => new RosterPageRow(
                assignment.PlayerCampaignAssignmentId,
                assignment.PlayerId,
                assignment.Player.FirstName,
                assignment.Player.LastName,
                assignment.Player.GraduationYear,
                assignment.TryoutNumber,
                assignment.PlacementOutcome,
                assignment.TeamId,
                assignment.Team != null ? assignment.Team.Name : null,
                assignment.CampaignTagApplications.Select(application => new RosterTagSummaryRow(
                    application.PlayerTagId,
                    application.PlayerTag.Name,
                    application.PlayerTag.Color,
                    application.PlayerTag.LifecycleStatus == LifecycleStatus.Archived)).ToList()))
            .ToListAsync(cancellationToken);

        var orderedRows = ApplyOrdering(projectedRows, normalizedSortBy, normalizedSortDirection);
        var pageRows = orderedRows
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
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
                row.Tags.Select(tag => new CampaignParticipantTagSummaryDto(tag.PlayerTagId, tag.TagName, tag.TagColor, tag.IsArchived)).ToList().AsReadOnly()))
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
            .ThenBy(note => note.NoteId)
            .ToList();

        var tagApplications = await db.CampaignTagApplications
            .AsNoTracking()
            .Where(application => application.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId)
            .Select(application => new ParticipantTagProjection(
                application.PlayerTagId,
                application.PlayerTag.Name,
                application.PlayerTag.Color,
                application.PlayerTag.LifecycleStatus == LifecycleStatus.Archived,
                application.CreatedById,
                application.CreatedAt))
            .ToListAsync(cancellationToken);

        var orderedTagApplications = tagApplications
            .OrderByDescending(application => application.CreatedAt)
            .ThenBy(application => application.PlayerTagId)
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

        var noteDtos = orderedNotes
            .Select(note => new CampaignParticipantNoteDto(
                note.NoteId,
                note.Content,
                actorDisplayNames.GetValueOrDefault(note.CreatedById) ?? "Unknown user",
                note.CreatedAt))
            .ToList()
            .AsReadOnly();

        var tagDtos = orderedTagApplications
            .Select(application => new CampaignParticipantTagApplicationDto(
                application.PlayerTagId,
                application.TagName,
                application.TagColor,
                application.IsArchived,
                actorDisplayNames.GetValueOrDefault(application.CreatedById) ?? "Unknown user",
                application.CreatedAt))
            .ToList()
            .AsReadOnly();
 
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
            new CampaignParticipantCapabilitiesDto(true, true, true, true));
    }

    private static PlacementOutcome? NormalizeOutcome(string? outcome)
    {
        if (string.IsNullOrWhiteSpace(outcome))
        {
            return null;
        }

        return Enum.TryParse<PlacementOutcome>(outcome.Trim(), true, out var parsedOutcome)
            ? parsedOutcome
            : null;
    }

    private static IReadOnlyList<RosterPageRow> ApplyOrdering(
        IReadOnlyList<RosterPageRow> rows,
        string sortBy,
        string sortDirection)
    {
        var descending = string.Equals(sortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return sortBy.ToLowerInvariant() switch
        {
            "assignmentid" => descending
                ? rows.OrderByDescending(row => row.PlayerCampaignAssignmentId).ThenBy(row => row.PlayerCampaignAssignmentId).ToList().AsReadOnly()
                : rows.OrderBy(row => row.PlayerCampaignAssignmentId).ThenBy(row => row.PlayerCampaignAssignmentId).ToList().AsReadOnly(),
            "graduationyear" => descending
                ? rows.OrderByDescending(row => row.GraduationYear).ThenBy(row => row.PlayerCampaignAssignmentId).ToList().AsReadOnly()
                : rows.OrderBy(row => row.GraduationYear).ThenBy(row => row.PlayerCampaignAssignmentId).ToList().AsReadOnly(),
            "tryoutnumber" => descending
                ? rows.OrderByDescending(row => row.TryoutNumber ?? int.MinValue).ThenBy(row => row.PlayerCampaignAssignmentId).ToList().AsReadOnly()
                : rows.OrderBy(row => row.TryoutNumber ?? int.MaxValue).ThenBy(row => row.PlayerCampaignAssignmentId).ToList().AsReadOnly(),
            "outcome" => descending
                ? rows.OrderByDescending(row => row.PlacementOutcome).ThenBy(row => row.PlayerCampaignAssignmentId).ToList().AsReadOnly()
                : rows.OrderBy(row => row.PlacementOutcome).ThenBy(row => row.PlayerCampaignAssignmentId).ToList().AsReadOnly(),
            "teamname" => descending
                ? rows.OrderByDescending(row => row.TeamName ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.PlayerCampaignAssignmentId).ToList().AsReadOnly()
                : rows.OrderBy(row => row.TeamName ?? string.Empty, StringComparer.OrdinalIgnoreCase).ThenBy(row => row.PlayerCampaignAssignmentId).ToList().AsReadOnly(),
            _ => descending
                ? rows.OrderByDescending(row => row.LastName, StringComparer.OrdinalIgnoreCase)
                    .ThenByDescending(row => row.FirstName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.PlayerCampaignAssignmentId)
                    .ToList()
                    .AsReadOnly()
                : rows.OrderBy(row => row.LastName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.FirstName, StringComparer.OrdinalIgnoreCase)
                    .ThenBy(row => row.PlayerCampaignAssignmentId)
                    .ToList()
                    .AsReadOnly()
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
        string? TeamName,
        IReadOnlyList<RosterTagSummaryRow> Tags);

    private sealed record RosterTagSummaryRow(
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
        Guid ConcurrencyToken,
        DateTimeOffset CreatedAt,
        DateTimeOffset? ModifiedAt);

    private sealed record ParticipantNoteProjection(
        long NoteId,
        string Content,
        long CreatedById,
        DateTimeOffset CreatedAt);

    private sealed record ParticipantTagProjection(
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
