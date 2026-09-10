using System.Data;
using System.Globalization;
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

/// <summary>Reads effective season membership, Active work, and separate Closed records.</summary>
internal sealed partial class EffectivePlacementQueryService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<EffectivePlacementQueryService> logger) : IEffectivePlacementQueryService
{
    /// <inheritdoc />
    public Task<ServiceResult<CurrentSeasonRosterResult>> GetCurrentSeasonRosterAsync(
        GetCurrentSeasonRosterInput input, CancellationToken cancellationToken = default)
        => ReadAsync(input, (db, clubId, token) => ReadRosterAsync(db, clubId, input, token), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<CampaignEffectivePlacementsResult>> GetCampaignEffectivePlacementsAsync(
        GetCampaignEffectivePlacementsInput input, CancellationToken cancellationToken = default)
        => ReadAsync(input, (db, clubId, token) => ReadWorkingAsync(db, clubId, input, token), cancellationToken);

    /// <inheritdoc />
    public Task<ServiceResult<ClosedCampaignRosterResult>> GetClosedCampaignRosterAsync(
        GetClosedCampaignRosterInput input, CancellationToken cancellationToken = default)
        => ReadAsync(input, (db, clubId, token) => ReadClosedAsync(db, clubId, input, token), cancellationToken);

    /// <summary>Keeps response identity, lifecycle, counts, and rows in one snapshot per retry attempt.</summary>
    private async Task<ServiceResult<T>> ReadAsync<T>(PlacementPageInput input,
        Func<NovaReadDbContext, long, CancellationToken, Task<ServiceResult<T>>> read,
        CancellationToken cancellationToken)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }
        if (currentUserProvider.UserId is not long || currentUserProvider.ClubId is not long clubId)
        {
            return ServiceProblem.Forbidden("You must be an approved club member to read placements.");
        }
        try
        {
            await using var strategyDb = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
            return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var db = await readDbContextFactory.CreateDbContextAsync(token);
                await using var transaction = await db.Database.BeginTransactionAsync(
                    db.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, token);
                var result = await read(db, clubId, token);
                await transaction.CommitAsync(token);
                return result;
            }, cancellationToken);
        }
        catch (Exception exception)
        {
            if (exception is OperationCanceledException || cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            LogReadFailed(exception, currentUserProvider.UserId.Value, clubId, input.GetType().Name);
            return ServiceProblem.ServerError("Placement information is unavailable.");
        }
    }

    private static async Task<ServiceResult<CurrentSeasonRosterResult>> ReadRosterAsync(
        NovaReadDbContext db, long clubId, GetCurrentSeasonRosterInput input, CancellationToken token)
    {
        if (!await TeamExistsAsync(db, clubId, input.TeamId, token))
        {
            return ServiceProblem.NotFound();
        }
        var season = await db.Seasons.Where(s => s.ClubId == clubId && s.SeasonId == s.Club.CurrentSeasonId)
            .Select(s => new PlacementSeasonIdentity(s.SeasonId, s.Name)).SingleOrDefaultAsync(token);
        if (season is null)
        {
            return new CurrentSeasonRosterResult(null, new([], input.Page ?? 1, Size(input), 0));
        }
        var query = EffectivePlacementQueries.Roster(db, clubId);
        if (input.TeamId is long teamId)
        {
            query = query.Where(a => a.TeamId == teamId);
        }
        query = FilterPlayers(db, query, input.GraduationYear, input.Search, tryoutSearch: false);
        var count = await query.CountAsync(token);
        var rows = await query.OrderBy(a => a.Player.LastName).ThenBy(a => a.Player.FirstName).ThenBy(a => a.PlayerId)
            .Skip(Offset(input)).Take(Size(input)).Select(PlacementReadProjection.RosterRow()).ToListAsync(token);
        return new CurrentSeasonRosterResult(season, new(rows.AsReadOnly(), input.Page ?? 1, Size(input), count));
    }

    private static async Task<ServiceResult<CampaignEffectivePlacementsResult>> ReadWorkingAsync(
        NovaReadDbContext db, long clubId, GetCampaignEffectivePlacementsInput input, CancellationToken token)
    {
        var campaign = await ReadCampaignAsync(db, clubId, input.CampaignId, token);
        if (campaign is null || !await TeamExistsAsync(db, clubId, input.TeamId, token)
            || !await DiscoveryIdentifiersExistAsync(db, clubId, input, token))
        {
            return ServiceProblem.NotFound();
        }
        if (campaign.Status != CampaignStatus.Active || !await db.Clubs.AnyAsync(
            c => c.ClubId == clubId && c.CurrentSeasonId == campaign.Season.SeasonId, token))
        {
            return ServiceProblem.Conflict("Effective placement work requires the current season's Active campaign.");
        }
        var query = EffectivePlacementQueries.WorkingSet(db, clubId).Where(row => row.Participation.CampaignId == input.CampaignId);
        var counts = await query.GroupBy(row => row.Eligibility)
            .Select(group => new EligibilityCount(group.Key, group.Count())).ToListAsync(token);
        var playerFilter = FilterDiscovery(db, db.PlayerCampaignAssignments.Where(a => a.ClubId == clubId
            && a.Player.ClubId == clubId && a.CampaignId == input.CampaignId), input);
        playerFilter = FilterPlayers(db, playerFilter, input.GraduationYear, null, tryoutSearch: true);
        // Apply local discovery before the effective-decision join. An EXISTS over the same
        // participation root duplicates its tenant/navigation joins in every count and page.
        query = EffectivePlacementQueries.WorkingSet(db, clubId, playerFilter);
        if (input.TeamId is long teamId)
        {
            query = query.Where(row => row.Eligibility == EffectivePlacementEligibility.OptionalReassignment && row.Decision!.TeamId == teamId);
        }
        if (Enum.TryParse<EffectivePlacementEligibility>(input.Eligibility, true, out var eligibility) && Enum.IsDefined(eligibility))
        {
            query = query.Where(row => row.Eligibility == eligibility);
        }
        var count = input.TeamId is null && input.Eligibility is null
            ? await playerFilter.CountAsync(token) : await query.CountAsync(token);
        var ordered = input.SortBy is null ? query.OrderBy(row => row.Participation.Player.GraduationYear)
            .ThenBy(row => row.Participation.Player.LastName).ThenBy(row => row.Participation.Player.FirstName)
            .ThenBy(row => row.Participation.PlayerId) : OrderWorking(query, input);
        var rows = await ordered.Skip(Offset(input)).Take(Size(input))
            .Select(PlacementReadProjection.WorkingRow(clubId)).ToListAsync(token);
        var tags = await ReadTagsAsync(db, clubId, rows.Select(row => row.PlayerCampaignAssignmentId).ToArray(), token);
        rows = rows.Select(row => row with { AppliedTags = tags.GetValueOrDefault(row.PlayerCampaignAssignmentId, []) }).ToList();
        return new CampaignEffectivePlacementsResult(campaign,
            new EffectivePlacementCounts(Count(EffectivePlacementEligibility.NeedsPlacement), Count(EffectivePlacementEligibility.OptionalReassignment),
                Count(EffectivePlacementEligibility.Resolved), Count(EffectivePlacementEligibility.Unavailable)),
            new(rows.AsReadOnly(), input.Page ?? 1, Size(input), count));

        int Count(EffectivePlacementEligibility state) => counts.FirstOrDefault(row => row.Eligibility == state)?.Count ?? 0;
    }

    private static async Task<ServiceResult<ClosedCampaignRosterResult>> ReadClosedAsync(
        NovaReadDbContext db, long clubId, GetClosedCampaignRosterInput input, CancellationToken token)
    {
        var campaign = await ReadCampaignAsync(db, clubId, input.CampaignId, token);
        if (campaign is null)
        {
            return ServiceProblem.NotFound();
        }
        if (campaign.Status != CampaignStatus.Closed)
        {
            return ServiceProblem.Conflict("The final roster is available only while the campaign is Closed.");
        }
        var query = db.PlayerCampaignAssignments.Where(a => a.ClubId == clubId && a.CampaignId == input.CampaignId
            && a.Player.ClubId == clubId);
        if (await query.AnyAsync(a => a.PlacementOutcome == PlacementOutcome.Undecided || a.DecisionRecordedAt == null
            || a.DecisionRecordedById == null || a.DecisionActorDisplayName == null
            || a.PlacementOutcome == PlacementOutcome.Assigned && (a.Team == null || a.Team.ClubId != clubId), token))
        {
            return ServiceProblem.Conflict("The Closed campaign contains an incomplete decision record.");
        }
        var participantCount = await query.CountAsync(token);
        if (!await DiscoveryIdentifiersExistAsync(db, clubId, input, token))
        {
            return ServiceProblem.NotFound();
        }
        query = FilterDiscovery(db, query, input);
        var count = await query.CountAsync(token);
        var ordered = input.SortBy is null ? query.OrderBy(a => a.Player.LastName).ThenBy(a => a.Player.FirstName).ThenBy(a => a.PlayerId)
            : OrderAssignments(query, input);
        var rows = await ordered
            .Skip(Offset(input)).Take(Size(input)).Select(PlacementReadProjection.ClosedRow(clubId)).ToListAsync(token);
        var tags = await ReadTagsAsync(db, clubId, rows.Select(row => row.PlayerCampaignAssignmentId).ToArray(), token);
        rows = rows.Select(row => row with { AppliedTags = tags.GetValueOrDefault(row.PlayerCampaignAssignmentId, []) }).ToList();
        return new ClosedCampaignRosterResult(campaign, new(rows.AsReadOnly(), input.Page ?? 1, Size(input), count))
        {
            ParticipantCount = participantCount,
        };
    }

    private static Task<PlacementCampaignIdentity?> ReadCampaignAsync(NovaReadDbContext db, long clubId, long campaignId, CancellationToken token)
        => db.Campaigns.Where(c => c.ClubId == clubId && c.CampaignId == campaignId && c.Season.ClubId == clubId)
            .Select(c => new PlacementCampaignIdentity(c.CampaignId, c.Name, c.Status, new(c.SeasonId, c.Season.Name)))
            .SingleOrDefaultAsync(token);

    private static async Task<bool> TeamExistsAsync(NovaReadDbContext db, long clubId, long? teamId, CancellationToken token)
        => teamId is null || await db.Teams.AnyAsync(t => t.TeamId == teamId && t.ClubId == clubId, token);

    private static IQueryable<PlayerCampaignAssignmentEntity> FilterPlayers(NovaReadDbContext db,
        IQueryable<PlayerCampaignAssignmentEntity> query, int? graduationYear, string? search, bool tryoutSearch)
    {
        if (graduationYear is int year)
        {
            query = query.Where(a => a.Player.GraduationYear == year);
        }
        if (string.IsNullOrWhiteSpace(search))
        {
            return query;
        }
        var value = search.Trim();
        var hasNumber = tryoutSearch && int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out _);
        var number = hasNumber ? int.Parse(value, CultureInfo.InvariantCulture) : 0;
        var pattern = $"%{LikePatternEscaper.EscapeLikePattern(value)}%";
        if (db.Database.IsNpgsql())
        {
            return query.Where(a => EF.Functions.ILike(a.Player.FirstName + " " + a.Player.LastName, pattern, @"\")
                || hasNumber && a.TryoutNumber == number);
        }
        var upper = value.ToUpperInvariant();
#pragma warning disable CA1311, CA1862, CA1304, MA0011 // SQLite SQL UPPER has no culture/StringComparison overload; PostgreSQL uses escaped ILIKE above.
        return query.Where(a => (a.Player.FirstName + " " + a.Player.LastName).ToUpper().Contains(upper)
            || hasNumber && a.TryoutNumber == number);
#pragma warning restore CA1311, CA1862, CA1304, MA0011
    }

    private static int Size(PlacementPageInput input) => input.PageSize ?? PlacementPageInput.DefaultPageSize;
    private static int Offset(PlacementPageInput input) => ((input.Page ?? 1) - 1) * Size(input);

    [LoggerMessage(Level = LogLevel.Error, Message = "Placement read {Operation} failed for UserId={UserId}, ClubId={ClubId}.")]
    private partial void LogReadFailed(Exception exception, long userId, long clubId, string operation);

    private sealed record EligibilityCount(EffectivePlacementEligibility Eligibility, int Count);
}
