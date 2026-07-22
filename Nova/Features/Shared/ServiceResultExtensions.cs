using Nova.Shared.Results;
using OneOf;
using System.Diagnostics;

namespace Nova.Features.Shared;

/// <summary>
/// Extension methods for converting ServiceResult instances to HTTP responses.
/// Handles the round-trip from service result to ProblemDetails/TypedResults and back.
/// </summary>
internal static class ServiceResultExtensions
{
    extension<TSuccess>(ServiceResult<TSuccess> result)
    {
        /// <summary>
        /// Converts a <see cref="ServiceResult{TSuccess}"/> to an ASP.NET Core TypedResults response.
        /// Success converts to the success value; problems convert to appropriate ProblemDetails with traceId.
        /// </summary>
        public IResult ToHttpResult(Func<TSuccess, IResult>? onSuccess = null)
        {
            return result.Match(
                success => onSuccess?.Invoke(success) ?? TypedResults.Ok(success),
                problem => problem.ToHttpResult());
        }
    }

    extension(ServiceProblem problem)
    {
        /// <summary>
        /// Converts a <see cref="ServiceProblem"/> to an ASP.NET Core ProblemDetails response.
        /// The response includes the W3C trace ID from Activity.Current if available.
        /// </summary>
        public IResult ToHttpResult()
        {
            var traceId = Activity.Current?.TraceId.ToString() ?? string.Empty;
            var extensions = problem.Extensions is null
                ? new Dictionary<string, object?>()
                : new Dictionary<string, object?>(problem.Extensions);
            if (!string.IsNullOrEmpty(traceId))
            {
                extensions["traceId"] = traceId;
            }

            var problemExtensions = extensions.Count == 0 ? null : extensions;

            return problem.Kind switch
            {
                ServiceProblemKind.NotFound =>
                    TypedResults.Problem(
                        detail: problem.Detail,
                        statusCode: StatusCodes.Status404NotFound,
                        extensions: problemExtensions),

                ServiceProblemKind.Forbidden =>
                    TypedResults.Problem(
                        detail: problem.Detail,
                        statusCode: StatusCodes.Status403Forbidden,
                        extensions: problemExtensions),

                ServiceProblemKind.Conflict when problem.Errors is { } conflictErrors =>
                    TypedResults.Problem(
                        detail: problem.Detail,
                        statusCode: StatusCodes.Status409Conflict,
                        extensions: new Dictionary<string, object?>(extensions ?? []) { ["errors"] = conflictErrors }),

                ServiceProblemKind.Conflict =>
                    TypedResults.Problem(
                        detail: problem.Detail,
                        statusCode: StatusCodes.Status409Conflict,
                        extensions: problemExtensions),

                ServiceProblemKind.Validation =>
                    TypedResults.ValidationProblem(
                        errors: problem.Errors?.ToDictionary(e => e.Key, e => e.Value) ?? [],
                        detail: problem.Detail,
                        extensions: problemExtensions),

                ServiceProblemKind.BadRequest =>
                    TypedResults.Problem(
                        detail: problem.Detail,
                        statusCode: StatusCodes.Status400BadRequest,
                        extensions: problemExtensions),

                _ => TypedResults.Problem(
                    detail: problem.Detail,
                    statusCode: StatusCodes.Status500InternalServerError,
                    extensions: problemExtensions)
            };
        }
    }

    extension<T0, T1>(OneOf<T0, ServiceResult<T1>> result)
    {
        /// <summary>
        /// Converts a OneOf result where one case is a ServiceResult, extracting the result and converting to HTTP.
        /// This is useful for service operations that can emit either a result or multiple outcome types.
        /// </summary>
        public IResult ToHttpResult(
            Func<T0, IResult>? onFirst = null,
            Func<T1, IResult>? onSuccess = null)
        {
            return result.Match(
                first => onFirst?.Invoke(first) ?? TypedResults.BadRequest(),
                serviceResult => serviceResult.ToHttpResult(onSuccess));
        }
    }
}
