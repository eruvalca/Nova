using Microsoft.EntityFrameworkCore;
using Nova.Data;
using Nova.Data.Tenancy;
using Nova.Features.Common;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.Features.Players;

/// <summary>
/// Server-side implementation of <see cref="IPlayerIntakeContextService"/> that reports the
/// club's current Active campaign for manual-intake preview.
/// </summary>
/// <param name="readDbContextFactory">The read-only tenant-scoped context factory.</param>
/// <param name="currentUserProvider">The current user provider used for authorization checks.</param>
/// <param name="logger">The logger for expected authorization failures.</param>
internal sealed partial class PlayerIntakeContextService(
    IDbContextFactory<NovaReadDbContext> readDbContextFactory,
    ICurrentUserProvider currentUserProvider,
    ILogger<PlayerIntakeContextService> logger) : IPlayerIntakeContextService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlayerIntakeContext>> GetPlayerIntakeContextAsync(
        GetPlayerIntakeContextInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0)
        {
            return ServiceProblem.Validation(errors);
        }

        if (currentUserProvider.UserId is not long currentUserId)
        {
            return ServiceProblem.Forbidden("You must be signed in to view player intake context.");
        }

        if (currentUserProvider.ClubId is not long currentUserClubId || currentUserClubId != input.ClubId)
        {
            LogForbiddenIntakeContextAccess(input.ClubId, currentUserId);
            return ServiceProblem.Forbidden("You do not have permission to view this club's player intake context.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        // Manual creation reads this same fact at commitment; at most one campaign is Active per club.
        var activeCampaign = await db.Campaigns
            .Where(campaign => campaign.Status == CampaignStatus.Active)
            .Select(campaign => new { campaign.CampaignId, campaign.Name })
            .SingleOrDefaultAsync(cancellationToken);

        return new PlayerIntakeContext
        {
            CampaignId = activeCampaign?.CampaignId,
            CampaignName = activeCampaign?.Name
        };
    }

    /// <summary>
    /// Logs a rejected intake-context read from a caller outside the requested club.
    /// </summary>
    /// <param name="clubId">The requested club identifier.</param>
    /// <param name="userId">The current user identifier.</param>
    [LoggerMessage(Level = LogLevel.Warning, Message = "Player intake context forbidden for ClubId={ClubId} by UserId={UserId}.")]
    private partial void LogForbiddenIntakeContextAccess(long clubId, long userId);
}
