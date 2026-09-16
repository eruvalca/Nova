using System.Data;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Entities;
using Nova.Extensions.Campaigns;
using Nova.Features.Activity;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Activity;
using Nova.SharedKernel.Features.Campaigns;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Security;
using Nova.SharedKernel.Validation;

namespace Nova.Features.Campaigns;

/// <summary>Reads previous-season assignment and bounded append-only changes in one snapshot.</summary>
internal sealed partial class PlacementContextQueryService(IDbContextFactory<NovaReadDbContext> factory,
    ICurrentUserProvider currentUser, ILogger<PlacementContextQueryService> logger) : IPlacementContextQueryService
{
    private static readonly JsonSerializerOptions _json = new() { PropertyNameCaseInsensitive = true };

    /// <inheritdoc />
    public async Task<ServiceResult<PlacementContextResult>> GetContextAsync(GetPlacementContextInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0) { return ServiceProblem.Validation(errors); }
        if (currentUser.ClubId is not long club || currentUser.UserId is not long actor)
        {
            return ServiceProblem.Forbidden("You must belong to this club to read placements.");
        }
        try
        {
            await using var strategyDb = await factory.CreateDbContextAsync(cancellationToken);
            return await strategyDb.Database.CreateExecutionStrategy().ExecuteAsync(async token =>
            {
                await using var db = await factory.CreateDbContextAsync(token);
                await using var transaction = await db.Database.BeginTransactionAsync(
                    db.Database.IsNpgsql() ? IsolationLevel.RepeatableRead : IsolationLevel.Serializable, token);
                var result = await ReadAsync(db, input, club, actor, token);
                await transaction.CommitAsync(token);
                return result;
            }, cancellationToken);
        }
        catch (Exception exception) when (!cancellationToken.IsCancellationRequested)
        {
            LogReadFailed(exception, actor, club, input.GetType().Name, input.CampaignId, input.PlayerCampaignAssignmentId);
            return ServiceProblem.ServerError("Placement history could not be loaded.");
        }
    }

    private static async Task<ServiceResult<PlacementContextResult>> ReadAsync(NovaReadDbContext db,
        GetPlacementContextInput input, long club, long actor, CancellationToken token)
    {
        if (!await db.Users.AnyAsync(u => u.Id == actor && u.ClubId == club, token))
        {
            return ServiceProblem.Forbidden("You must currently belong to this club to read placements.");
        }
        var participant = await db.PlayerCampaignAssignments.Include(a => a.Campaign).Include(a => a.Player)
            .SingleOrDefaultAsync(a => a.PlayerCampaignAssignmentId == input.PlayerCampaignAssignmentId
                && a.CampaignId == input.CampaignId && a.ClubId == club, token);
        if (participant is null || participant.Campaign.Status == CampaignStatus.Draft)
        {
            return ServiceProblem.NotFound();
        }
        if (input.RequireClosed is true && participant.Campaign.Status != CampaignStatus.Closed)
        {
            return ServiceProblem.Conflict("The campaign has reopened. Refresh to read its current state.");
        }
        var seasonId = participant.Campaign.SeasonId;
        var latest = await db.PlayerCampaignAssignments.Where(a => a.PlayerId == participant.PlayerId
                && a.Campaign.SeasonId == seasonId && a.Campaign.Status != CampaignStatus.Draft
                && a.PlacementOutcome != PlacementOutcome.Undecided)
            .OrderByDescending(a => a.Campaign.SeasonOpeningSequence).ThenByDescending(a => a.PlayerCampaignAssignmentId)
            .Select(a => new { a.PlacementOutcome, a.CampaignId }).FirstOrDefaultAsync(token);
        var writable = participant.Campaign.Status == CampaignStatus.Active
            && participant.Player.LifecycleStatus == LifecycleStatus.Active
            && await db.Clubs.AnyAsync(c => c.ClubId == club && c.CurrentSeasonId == seasonId, token);
        var administratorRole = Roles.ClubAdmin.ToUpperInvariant();
        var administrator = await (from ur in db.UserRoles
                                   join r in db.Roles on ur.RoleId equals r.Id
                                   where ur.UserId == actor && r.NormalizedName == administratorRole
                                   select ur.UserId).AnyAsync(token);

        var previous = await ReadPreviousAsync(db, club, seasonId, participant.PlayerId,
            participant.Player.GraduationYear, writable && latest is null, token);

        var events = db.ActivityEvents.Where(e => e.ClubId == club && e.PlayerId == participant.PlayerId
            && !e.IsAdminOnly);
        // A Closed sheet owns only its immutable campaign record; later changes belong to later sheets.
        if (participant.Campaign.Status == CampaignStatus.Closed) { events = events.Where(e => e.CampaignId == input.CampaignId); }
        if (input.BeforeEventId is long cursor) { events = events.Where(e => e.ActivityEventId < cursor); }
        var page = await events.OrderByDescending(e => e.ActivityEventId).Take(21).ToListAsync(token);
        return new PlacementContextResult(input.PlayerCampaignAssignmentId, previous, ProjectHistory(page.Take(20), participant.PlayerId),
            page.Count > 20 ? page[19].ActivityEventId : null,
            writable && administrator && latest is { PlacementOutcome: PlacementOutcome.Withdrawn }
                && latest.CampaignId != input.CampaignId);
    }

    private static List<PlacementHistoryItem> ProjectHistory(IEnumerable<ActivityEventEntity> rows, long playerId)
    {
        var history = new List<PlacementHistoryItem>();
        foreach (var row in rows)
        {
            try
            {
                if (JsonSerializer.Deserialize<ClubActivityContext>(row.PayloadJson, _json) is PlacementContext context
                    && context.PlayerId == playerId && context.PlayerCampaignAssignmentId > 0
                    && ActivityEventPolicy.ContextMatchesKind(row.EventKind, context)
                    && row.CampaignId == context.CampaignId)
                {
                    var item = new PlacementHistoryItem(row.ActivityEventId, context.CampaignId, context.CampaignName,
                        context.PreviousOutcome, context.PreviousTeamName, context.Outcome, context.TeamName,
                        row.ActorDisplayName, row.CreatedAt);
                    if (PlacementHistoryValidation.IsValid(item)) { history.Add(item); }
                }
            }
            catch (Exception exception) when (exception is JsonException or NotSupportedException)
            {
                // The continuation marks the raw page boundary even when a malformed event is omitted.
            }
        }
        return history;
    }

    private static async Task<PreviousSeasonPlacement?> ReadPreviousAsync(NovaReadDbContext db, long club,
        long seasonId, long playerId, int graduationYear, bool mayKeep, CancellationToken token)
    {
        // The chain is immutable: each advancement creates a new season pointing to its predecessor.
        // SQL follows the chain and orders by distance, never by user-editable dates or ID chronology.
        var ancestors = db.Database.SqlQuery<AncestorSeason>($"""
            WITH RECURSIVE ancestry("SeasonId", "Depth") AS (
                SELECT "CreationPreviousSeasonId", 1 FROM "Seasons"
                WHERE "SeasonId" = {seasonId} AND "ClubId" = {club}
                UNION ALL
                SELECT s."CreationPreviousSeasonId", a."Depth" + 1
                FROM "Seasons" s JOIN ancestry a ON s."SeasonId" = a."SeasonId"
                WHERE s."ClubId" = {club} AND s."CreationPreviousSeasonId" IS NOT NULL
            ) SELECT "SeasonId", "Depth" FROM ancestry WHERE "SeasonId" IS NOT NULL
            """);
        var previousId = await (from a in db.PlayerCampaignAssignments
                                join ancestor in ancestors on a.Campaign.SeasonId equals ancestor.SeasonId
                                where a.PlayerId == playerId && a.ClubId == club
                                    && a.Campaign.Status != CampaignStatus.Draft && a.PlacementOutcome == PlacementOutcome.Assigned
                                orderby ancestor.Depth, a.Campaign.SeasonOpeningSequence descending, a.PlayerCampaignAssignmentId descending
                                select (long?)a.PlayerCampaignAssignmentId).FirstOrDefaultAsync(token);
        PreviousSeasonPlacement? previous = null;
        if (previousId is not null)
        {
            var assignment = await db.PlayerCampaignAssignments.Include(a => a.Campaign).ThenInclude(c => c.Season)
                .SingleAsync(a => a.PlayerCampaignAssignmentId == previousId, token);
            var team = await db.Teams.SingleOrDefaultAsync(t => t.TeamId == assignment.TeamId && t.ClubId == club, token);
            var decision = assignment.ToSavedPlacementDecision()!;
            if (team is null) { decision = decision with { TeamId = null }; }
            previous = new(new(assignment.Campaign.SeasonId, assignment.Campaign.Season.Name),
                new(decision, assignment.Campaign.Name, team is null ? null : new(team.TeamId, team.Name)),
                mayKeep && team is { LifecycleStatus: LifecycleStatus.Active }
                    && graduationYear >= team.GraduationYear);
        }

        return previous;
    }

    private sealed record AncestorSeason(long SeasonId, int Depth);

    [LoggerMessage(Level = LogLevel.Error, Message = "Placement history read failed for UserId={UserId}, ClubId={ClubId}, Operation={Operation}, CampaignId={CampaignId}, ParticipantId={ParticipantId}.")]
    private partial void LogReadFailed(Exception exception, long userId, long clubId, string operation, long campaignId, long participantId);
}
