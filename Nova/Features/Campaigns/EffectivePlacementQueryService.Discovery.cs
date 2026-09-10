using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Entities;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Campaigns;

namespace Nova.Features.Campaigns;

internal sealed partial class EffectivePlacementQueryService
{
    private static async Task<bool> DiscoveryIdentifiersExistAsync(NovaReadDbContext db, long clubId,
        CampaignRosterDiscoveryInput input, CancellationToken token)
    {
        if (!await TeamExistsAsync(db, clubId, input.LocalTeamId, token))
        {
            return false;
        }
        var ids = input.TagDefinitionIds?.Distinct().ToArray() ?? [];
        return ids.Length == 0 || await db.PlayerTags.CountAsync(tag => tag.ClubId == clubId && ids.Contains(tag.PlayerTagId), token) == ids.Length;
    }

    private static IQueryable<PlayerCampaignAssignmentEntity> FilterDiscovery(NovaReadDbContext db,
        IQueryable<PlayerCampaignAssignmentEntity> query, CampaignRosterDiscoveryInput input)
    {
        query = FilterPlayers(db, query, null, input.Search, tryoutSearch: true);
        if (input.GraduationYears is { Length: > 0 } years)
        {
            query = query.Where(a => years.Contains(a.Player.GraduationYear));
        }
        if (input.TagDefinitionIds is { Length: > 0 } tags)
        {
            query = query.Where(a => a.CampaignTagApplications.Any(tag => tags.Contains(tag.PlayerTagId)));
        }
        if (Enum.TryParse<PlacementOutcome>(input.LocalOutcome, true, out var outcome))
        {
            query = query.Where(a => a.PlacementOutcome == outcome);
        }
        if (input.LocalTeamId is long teamId)
        {
            query = query.Where(a => a.TeamId == teamId);
        }
        if (input.ParticipantId is long participantId)
        {
            query = query.Where(a => a.PlayerCampaignAssignmentId == participantId);
        }
        return query;
    }

    private static async Task<Dictionary<long, IReadOnlyList<CampaignParticipantTagSummaryDto>>> ReadTagsAsync(
        NovaReadDbContext db, long clubId, long[] assignmentIds, CancellationToken token)
    {
        if (assignmentIds.Length == 0)
        {
            return [];
        }
        var rows = await db.CampaignTagApplications
            .Where(a => a.ClubId == clubId && a.PlayerTag.ClubId == clubId && assignmentIds.Contains(a.PlayerCampaignAssignmentId))
            .OrderBy(a => a.PlayerTag.Name).ThenBy(a => a.PlayerTagId)
            .Select(a => new TagRow(a.PlayerCampaignAssignmentId,
                new CampaignParticipantTagSummaryDto(a.PlayerTagId, a.PlayerTag.Name, a.PlayerTag.Color,
                    a.PlayerTag.LifecycleStatus == LifecycleStatus.Archived)))
            .ToListAsync(token);
        return rows.GroupBy(row => row.AssignmentId).ToDictionary(group => group.Key,
            group => (IReadOnlyList<CampaignParticipantTagSummaryDto>)group.Select(row => row.Tag).ToList().AsReadOnly());
    }

    private sealed record TagRow(long AssignmentId, CampaignParticipantTagSummaryDto Tag);

    private static IOrderedQueryable<PlayerCampaignAssignmentEntity> OrderAssignments(IQueryable<PlayerCampaignAssignmentEntity> query, CampaignRosterDiscoveryInput input)
    {
        if (string.Equals(input.SortBy, "searchRelevance", StringComparison.OrdinalIgnoreCase))
        {
            var number = int.TryParse(input.Search?.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? (int?)parsed : null;
            return query.OrderByDescending(assignment => number.HasValue && assignment.TryoutNumber == number)
                .ThenBy(assignment => assignment.Player.LastName).ThenBy(assignment => assignment.Player.FirstName)
                .ThenBy(assignment => assignment.PlayerCampaignAssignmentId);
        }

        var descending = string.Equals(input.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return input.SortBy?.ToUpperInvariant() switch
        {
            "ASSIGNMENTID" => descending
                ? query.OrderByDescending(a => a.PlayerCampaignAssignmentId)
                : query.OrderBy(a => a.PlayerCampaignAssignmentId),
            "GRADUATIONYEAR" => descending
                ? query.OrderByDescending(a => a.Player.GraduationYear).ThenBy(a => a.PlayerCampaignAssignmentId)
                : query.OrderBy(a => a.Player.GraduationYear).ThenBy(a => a.PlayerCampaignAssignmentId),
            "TRYOUTNUMBER" => descending
                ? query.OrderByDescending(a => a.TryoutNumber ?? int.MinValue).ThenBy(a => a.PlayerCampaignAssignmentId)
                : query.OrderBy(a => a.TryoutNumber ?? int.MaxValue).ThenBy(a => a.PlayerCampaignAssignmentId),
            "OUTCOME" => descending
                ? query.OrderByDescending(a => a.PlacementOutcome).ThenBy(a => a.PlayerCampaignAssignmentId)
                : query.OrderBy(a => a.PlacementOutcome).ThenBy(a => a.PlayerCampaignAssignmentId),
            "TEAMNAME" => descending
                ? query.OrderByDescending(a => a.Team != null ? a.Team.Name : string.Empty).ThenBy(a => a.PlayerCampaignAssignmentId)
                : query.OrderBy(a => a.Team != null ? a.Team.Name : string.Empty).ThenBy(a => a.PlayerCampaignAssignmentId),
            _ => descending
                ? query.OrderByDescending(a => a.Player.LastName).ThenByDescending(a => a.Player.FirstName).ThenBy(a => a.PlayerCampaignAssignmentId)
                : query.OrderBy(a => a.Player.LastName).ThenBy(a => a.Player.FirstName).ThenBy(a => a.PlayerCampaignAssignmentId),
        };
    }

    private static IOrderedQueryable<PlacementWorkingState> OrderWorking(IQueryable<PlacementWorkingState> query, CampaignRosterDiscoveryInput input)
    {
        if (string.Equals(input.SortBy, "searchRelevance", StringComparison.OrdinalIgnoreCase))
        {
            var number = int.TryParse(input.Search?.Trim(), System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsed) ? (int?)parsed : null;
            return query.OrderByDescending(row => number.HasValue && row.Participation.TryoutNumber == number)
                .ThenBy(row => row.Participation.Player.LastName).ThenBy(row => row.Participation.Player.FirstName)
                .ThenBy(row => row.Participation.PlayerCampaignAssignmentId);
        }

        var descending = string.Equals(input.SortDirection, "desc", StringComparison.OrdinalIgnoreCase);
        return input.SortBy?.ToUpperInvariant() switch
        {
            "ASSIGNMENTID" => descending
                ? query.OrderByDescending(a => a.Participation.PlayerCampaignAssignmentId)
                : query.OrderBy(a => a.Participation.PlayerCampaignAssignmentId),
            "GRADUATIONYEAR" => descending
                ? query.OrderByDescending(a => a.Participation.Player.GraduationYear).ThenBy(a => a.Participation.PlayerCampaignAssignmentId)
                : query.OrderBy(a => a.Participation.Player.GraduationYear).ThenBy(a => a.Participation.PlayerCampaignAssignmentId),
            "TRYOUTNUMBER" => descending
                ? query.OrderByDescending(a => a.Participation.TryoutNumber ?? int.MinValue).ThenBy(a => a.Participation.PlayerCampaignAssignmentId)
                : query.OrderBy(a => a.Participation.TryoutNumber ?? int.MaxValue).ThenBy(a => a.Participation.PlayerCampaignAssignmentId),
            "OUTCOME" => descending
                ? query.OrderByDescending(a => a.Participation.PlacementOutcome).ThenBy(a => a.Participation.PlayerCampaignAssignmentId)
                : query.OrderBy(a => a.Participation.PlacementOutcome).ThenBy(a => a.Participation.PlayerCampaignAssignmentId),
            "TEAMNAME" => descending
                ? query.OrderByDescending(a => a.Participation.Team != null ? a.Participation.Team.Name : string.Empty).ThenBy(a => a.Participation.PlayerCampaignAssignmentId)
                : query.OrderBy(a => a.Participation.Team != null ? a.Participation.Team.Name : string.Empty).ThenBy(a => a.Participation.PlayerCampaignAssignmentId),
            _ => descending
                ? query.OrderByDescending(a => a.Participation.Player.LastName).ThenByDescending(a => a.Participation.Player.FirstName).ThenBy(a => a.Participation.PlayerCampaignAssignmentId)
                : query.OrderBy(a => a.Participation.Player.LastName).ThenBy(a => a.Participation.Player.FirstName).ThenBy(a => a.Participation.PlayerCampaignAssignmentId),
        };
    }
}
