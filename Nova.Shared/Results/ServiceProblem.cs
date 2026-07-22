namespace Nova.Shared.Results;

/// <summary>
/// Represents a known problem from a service operation.
/// Maps directly to HTTP status codes and ProblemDetails for API responses.
/// </summary>
public readonly record struct ServiceProblem(
    ServiceProblemKind Kind,
    string? Detail = null,
    IReadOnlyDictionary<string, string[]>? Errors = null,
    IReadOnlyDictionary<string, object?>? Extensions = null)
{
    /// <summary>
    /// Gets the HTTP status code corresponding to this problem kind.
    /// </summary>
    public int StatusCode => Kind switch
    {
        ServiceProblemKind.NotFound => 404,
        ServiceProblemKind.Forbidden => 403,
        ServiceProblemKind.Conflict => 409,
        ServiceProblemKind.BadRequest => 400,
        ServiceProblemKind.Validation => 400,
        _ => 500
    };

    /// <summary>
    /// Creates a NotFound problem (HTTP 404).
    /// </summary>
    public static ServiceProblem NotFound(
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        => new(ServiceProblemKind.NotFound, detail, null, extensions);

    /// <summary>
    /// Creates a Forbidden problem (HTTP 403).
    /// Use this when the user is authenticated but not authorized for the operation.
    /// </summary>
    public static ServiceProblem Forbidden(
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        => new(ServiceProblemKind.Forbidden, detail, null, extensions);

    /// <summary>
    /// Creates a Conflict problem (HTTP 409).
    /// </summary>
    public static ServiceProblem Conflict(
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        => new(ServiceProblemKind.Conflict, detail, null, extensions);

    /// <summary>
    /// Creates a BadRequest problem (HTTP 400).
    /// Use this for single-message business-rule rejections where the client input is structurally valid
    /// but semantically rejected (e.g. invalid state transition).
    /// For structured validation errors, use <see cref="Validation(IReadOnlyDictionary{string, string[]}, string?)"/> instead.
    /// </summary>
    public static ServiceProblem BadRequest(
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        => new(ServiceProblemKind.BadRequest, detail, null, extensions);

    /// <summary>
    /// Creates a Validation problem (HTTP 400) with structured field errors.
    /// Use this for validation errors where the client input is structurally invalid or violates constraints
    /// (e.g. field value out of range, required field missing).
    /// </summary>
    public static ServiceProblem Validation(
        IReadOnlyDictionary<string, string[]> errors,
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        => new(ServiceProblemKind.Validation, detail, errors, extensions);

    /// <summary>
    /// Creates a Validation problem (HTTP 400) for a single field.
    /// Convenience overload for common case of a single-field validation error.
    /// </summary>
    public static ServiceProblem Validation(string fieldName, params string[] messages)
        => Validation(new Dictionary<string, string[]> { [fieldName] = messages }, null, null);

    /// <summary>
    /// Creates a ServerError problem (HTTP 500).
    /// </summary>
    public static ServiceProblem ServerError(
        string? detail = null,
        IReadOnlyDictionary<string, object?>? extensions = null)
        => new(ServiceProblemKind.ServerError, detail, null, extensions);
}
