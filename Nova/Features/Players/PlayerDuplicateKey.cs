namespace Nova.Features.Players;

/// <summary>Represents the intake normalized natural identity.</summary>
/// <param name="FirstName">The normalized first name.</param>
/// <param name="LastName">The normalized last name.</param>
/// <param name="DateOfBirth">The unchanged birth date.</param>
internal readonly record struct PlayerDuplicateKey(string FirstName, string LastName, DateOnly DateOfBirth)
{
    /// <summary>Applies the intake contract's trim and invariant-case normalization.</summary>
    /// <param name="firstName">The original first name.</param>
    /// <param name="lastName">The original last name.</param>
    /// <param name="dateOfBirth">The original birth date.</param>
    /// <returns>The comparison key.</returns>
    public static PlayerDuplicateKey Create(string firstName, string lastName, DateOnly dateOfBirth) => new(
        firstName.Trim().ToUpperInvariant(),
        lastName.Trim().ToUpperInvariant(),
        dateOfBirth);
}
