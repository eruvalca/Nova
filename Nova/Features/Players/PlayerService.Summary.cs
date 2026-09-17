using Microsoft.EntityFrameworkCore;
using Nova.SharedKernel.Enums;
using Nova.SharedKernel.Features.Players;
using Nova.SharedKernel.Results;
using Nova.SharedKernel.Validation;

namespace Nova.Features.Players;

internal sealed partial class PlayerService
{
    /// <inheritdoc />
    public async Task<ServiceResult<PlayerDirectorySummary>> GetPlayerDirectorySummaryAsync(
        GetPlayerDirectorySummaryInput input,
        CancellationToken cancellationToken = default)
    {
        var errors = InputValidator.Validate(input);
        if (errors.Count > 0) { return ServiceProblem.Validation(errors); }
        if (currentUserProvider.UserId is not long
            || currentUserProvider.ClubId is not long clubId || input.ClubId != clubId)
        {
            LogForbiddenRosterAccess(input.ClubId, currentUserProvider.UserId ?? 0);
            return ServiceProblem.Forbidden("You do not have permission to view this club roster.");
        }

        await using var db = await readDbContextFactory.CreateDbContextAsync(cancellationToken);
        var players = db.Players.Where(player => player.ClubId == clubId);
        var counts = await players.GroupBy(player => 1)
            .Select(group => new
            {
                Active = group.Count(player => player.LifecycleStatus == LifecycleStatus.Active),
                Archived = group.Count(player => player.LifecycleStatus == LifecycleStatus.Archived)
            }).SingleOrDefaultAsync(cancellationToken);
        var years = await players.Select(player => player.GraduationYear)
            .Distinct().OrderBy(year => year).Take(101).ToListAsync(cancellationToken);

        return new PlayerDirectorySummary
        {
            ActiveCount = counts?.Active ?? 0,
            ArchivedCount = counts?.Archived ?? 0,
            GraduationYears = years.AsReadOnly()
        };
    }
}
