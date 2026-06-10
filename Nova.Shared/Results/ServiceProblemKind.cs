namespace Nova.Shared.Results;

/// <summary>
/// Defines the kinds of problems that can occur in service operations.
/// Note: 401 Unauthorized is not included because authentication failures are handled
/// at the middleware/endpoint authorization layer, not in service operations.
/// </summary>
public enum ServiceProblemKind
{
    /// <summary>The requested resource was not found.</summary>
    NotFound,

    /// <summary>The user is authenticated but not authorized for this operation.</summary>
    Forbidden,

    /// <summary>The operation conflicts with the current state of the resource.</summary>
    Conflict,

    /// <summary>The request was invalid or malformed.</summary>
    BadRequest,

    /// <summary>The request contained validation errors in structured form.</summary>
    Validation,

    /// <summary>An unexpected server error occurred.</summary>
    ServerError
}
