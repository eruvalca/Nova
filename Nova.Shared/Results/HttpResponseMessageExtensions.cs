using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Nova.Shared.Results;

/// <summary>
/// Extension methods for converting HTTP responses to ServiceProblem.
/// </summary>
public static class HttpResponseMessageExtensions
{
    extension(HttpResponseMessage response)
    {
        /// <summary>
        /// Converts an unsuccessful HTTP response to a ServiceProblem,
        /// automatically extracting detail and validation errors from ProblemDetails if present.
        /// The response body is read exactly once.
        /// </summary>
        public async Task<ServiceProblem> ToServiceProblemAsync(CancellationToken cancellationToken = default)
        {
            var problemBody = await ReadProblemBodyAsync(response, cancellationToken);
            var detail = problemBody?.Detail;
            var errors = problemBody?.Errors;

            // If we have structured validation errors and a 400 status, it's a Validation problem.
            if (response.StatusCode == HttpStatusCode.BadRequest && errors is not null && errors.Count > 0)
            {
                return ServiceProblem.Validation(errors, detail);
            }

            // Otherwise, map the status code to a problem kind.
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound => ServiceProblem.NotFound(detail),
                HttpStatusCode.Forbidden => ServiceProblem.Forbidden(detail),
                HttpStatusCode.Conflict => ServiceProblem.Conflict(detail),
                HttpStatusCode.BadRequest => ServiceProblem.BadRequest(detail),
                _ => ServiceProblem.ServerError(detail)
            };
        }
    }

    /// <summary>
    /// Reads the response body once and deserializes it as ProblemDetails. The DTO is a superset
    /// of RFC 7807 ProblemDetails: <c>Errors</c> is simply <see langword="null"/> when the body
    /// is not a ValidationProblemDetails payload.
    /// </summary>
    /// <param name="response">The response whose body to read.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The deserialized problem body, or <see langword="null"/> when the body is empty or not JSON.</returns>
    private static async Task<ProblemDetailsDto?> ReadProblemBodyAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        try
        {
            return await response.Content.ReadFromJsonAsync<ProblemDetailsDto>(cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    /// <summary>
    /// Minimal representation of RFC 7807 ProblemDetails (and its ValidationProblemDetails
    /// superset) for deserializing API error responses with a single body read.
    /// </summary>
    /// <param name="Detail">The problem detail message, when present.</param>
    /// <param name="Errors">The structured validation errors, when the body is a ValidationProblemDetails payload.</param>
    private sealed record ProblemDetailsDto(
        string? Detail,
        IReadOnlyDictionary<string, string[]>? Errors);
}
