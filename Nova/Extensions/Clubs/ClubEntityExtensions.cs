using Nova.Entities;
using Nova.Shared.Clubs;

namespace Nova.Extensions.Clubs;

/// <summary>
/// Provides mapping extension members for <see cref="ClubEntity"/>.
/// </summary>
internal static class ClubEntityExtensions
{
    extension(ClubEntity club)
    {
        /// <summary>
        /// Maps this <see cref="ClubEntity"/> to a <see cref="ClubDto"/>.
        /// </summary>
        /// <returns>A <see cref="ClubDto"/> populated from this entity's fields.</returns>
        public ClubDto ToClubDto()
            => new(club.ClubId, club.Name, club.City, club.State);
    }
}
