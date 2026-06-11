namespace Nova.Components.Account;

/// <summary>
/// Model for capturing passkey operation input from HTML forms.
/// </summary>
public class PasskeyInputModel
{
    /// <summary>
    /// Gets or sets the serialized credential JSON from the passkey operation.
    /// </summary>
    public string? CredentialJson { get; set; }

    /// <summary>
    /// Gets or sets an error message if the passkey operation fails.
    /// </summary>
    public string? Error { get; set; }
}
