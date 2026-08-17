using Microsoft.AspNetCore.Diagnostics;

namespace Nova.Features.Shared;

/// <summary>
/// Converts framework-generated bad-request exceptions into correlated ProblemDetails responses.
/// </summary>
/// <param name="problemDetailsService">The service that writes the ProblemDetails response.</param>
internal sealed class BadHttpRequestExceptionHandler(IProblemDetailsService problemDetailsService) : IExceptionHandler
{
    /// <summary>
    /// Handles a <see cref="BadHttpRequestException"/> while preserving its framework-assigned status code.
    /// </summary>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="exception">The exception raised while processing the request.</param>
    /// <param name="cancellationToken">A token that cancels response writing.</param>
    /// <returns><see langword="true"/> when ProblemDetails were written; otherwise, <see langword="false"/>.</returns>
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not BadHttpRequestException badRequest)
        {
            return false;
        }

        httpContext.Response.StatusCode = badRequest.StatusCode;
        return await problemDetailsService.TryWriteAsync(
            new ProblemDetailsContext
            {
                HttpContext = httpContext,
                Exception = exception,
                ProblemDetails =
                {
                    Status = badRequest.StatusCode,
                    Detail = badRequest.Message
                }
            });
    }
}
