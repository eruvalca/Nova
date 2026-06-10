namespace Nova.Data.Tenancy;

using Nova.Shared.Security;

/// <summary>
/// An <see cref="ICurrentUserProvider"/> that represents no user. Used at design time
/// (EF migrations) and in tests that do not require an authenticated user.
/// </summary>
public sealed class NullCurrentUserProvider : ICurrentUserProvider
{
    /// <inheritdoc />
    public long? UserId => null;

    /// <inheritdoc />
    public long? ClubId => null;

    /// <inheritdoc />
    public bool IsClubAdmin => false;

    /// <inheritdoc />
    public CurrentUserState GetCurrentUserState() => new Anonymous();
}
