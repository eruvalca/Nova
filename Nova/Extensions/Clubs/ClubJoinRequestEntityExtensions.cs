using Nova.Entities;
using Nova.Shared.Clubs;

namespace Nova.Extensions.Clubs;

/// <summary>
/// Provides mapping extension members for <see cref="ClubJoinRequestEntity"/>.
/// </summary>
internal static class ClubJoinRequestEntityExtensions
{
    extension(ClubJoinRequestEntity request)
    {
        /// <summary>
        /// Maps this <see cref="ClubJoinRequestEntity"/> to a <see cref="ClubJoinRequestDto"/>.
        /// The <see cref="ClubJoinRequestEntity.Club"/> navigation must be loaded before calling this method.
        /// </summary>
        /// <returns>A <see cref="ClubJoinRequestDto"/> populated from this entity's fields.</returns>
        public ClubJoinRequestDto ToClubJoinRequestDto()
            => new(
                request.ClubJoinRequestId,
                request.ClubId,
                request.Club.Name,
                request.RequestingUserId,
                request.RequestingUser.FullName,
                request.Status,
                request.CreatedAt);
    }
}
