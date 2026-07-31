using Nova.Entities;
using Nova.Shared.Teams;

namespace Nova.Extensions.Teams;

/// <summary>
/// Provides mapping extension members for <see cref="TeamEntity"/>.
/// </summary>
internal static class TeamEntityExtensions
{
    extension(TeamEntity team)
    {
        /// <summary>
        /// Maps this <see cref="TeamEntity"/> to a <see cref="TeamDto"/>.
        /// </summary>
        /// <returns>A <see cref="TeamDto"/> populated from the team's permanent profile.</returns>
        public TeamDto ToTeamDto()
            => new()
            {
                TeamId = team.TeamId,
                ClubId = team.ClubId,
                Name = team.Name,
                GraduationYear = team.GraduationYear,
                LifecycleStatus = team.LifecycleStatus
            };
    }
}
