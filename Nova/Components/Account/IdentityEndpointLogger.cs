namespace Nova.Components.Account;

/// <summary>Provides source-generated logging for the Identity HTTP handlers.</summary>
internal sealed partial class IdentityEndpointLogger(ILogger<IdentityEndpointLogger> logger)
{
    [LoggerMessage(Level = LogLevel.Information, Message = "User with ID '{UserId}' asked for their personal data.")]
    public partial void LogPersonalDataRequested(string userId);
}
