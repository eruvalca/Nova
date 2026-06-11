namespace Nova.Components.Account;

/// <summary>
/// Defines the type of passkey operation to perform.
/// </summary>
public enum PasskeyOperation
{
    /// <summary>
    /// Create a new passkey for the user.
    /// </summary>
    Create = 0,

    /// <summary>
    /// Request authentication using an existing passkey.
    /// </summary>
    Request = 1,
}
