namespace Nova.Features.Clubs;

/// <summary>Provides source-generated structured logging for static club endpoint handlers.</summary>
/// <param name="logger">The typed logger that owns the endpoint log category.</param>
internal sealed partial class ClubEndpointLogger(ILogger<ClubEndpointLogger> logger)
{
    /// <summary>Logs a post-commit failure to refresh the club creator's authentication cookie.</summary>
    /// <param name="exception">The exception raised while loading the user or refreshing the cookie.</param>
    /// <param name="userId">The acting user's identifier when available.</param>
    [LoggerMessage(
        EventId = 3010,
        Level = LogLevel.Error,
        Message = "Club creation committed, but the authentication cookie could not be refreshed for user {UserId}.")]
    internal partial void LogClubCreationCookieRefreshFailed(Exception exception, long? userId);
}
