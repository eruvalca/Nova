using Nova.Entities;
using Nova.Shared.Account;

namespace Nova.Extensions.Account;

/// <summary>Provides mapping extension members for <see cref="NovaUserEntity"/> to account DTOs.</summary>
internal static class NovaUserEntityExtensions
{
    extension(NovaUserEntity user)
    {
        /// <summary>
        /// Maps this <see cref="NovaUserEntity"/> to a <see cref="ClubMemberDto"/>.
        /// </summary>
        /// <returns>A <see cref="ClubMemberDto"/> populated from this entity's fields.</returns>
        public ClubMemberDto ToClubMemberDto() => new(user.Id, user.FullName);
    }
}
