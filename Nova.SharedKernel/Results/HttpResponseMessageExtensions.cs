using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Nova.SharedKernel.Results;

/// <summary>
/// Extension methods for converting HTTP responses to ServiceProblem.
/// </summary>
public static class HttpResponseMessageExtensions
{
#pragma warning disable CA1034 // Nested types should not be visible
    extension(HttpResponseMessage response)
#pragma warning restore CA1034 // Nested types should not be visible
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
            var extensions = problemBody?.Extensions?
                .Where(static entry => entry.Key is not ("type" or "title" or "status" or "instance"))
                .ToDictionary(entry => entry.Key, entry => (object?)entry.Value, StringComparer.Ordinal);

            // If we have structured validation errors and a 400/422 status, it's a Validation problem.
            // Minimal APIs return 422 UnprocessableEntity for TypedResults.ValidationProblem.
            if (response.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity
                && errors?.Count > 0)
            {
                return ServiceProblem.Validation(errors, detail, extensions);
            }

            // Otherwise, map the status code to a problem kind.
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound => ServiceProblem.NotFound(detail, extensions),
                HttpStatusCode.Forbidden => ServiceProblem.Forbidden(detail, extensions),
                HttpStatusCode.Conflict when errors is { Count: > 0 } =>
                    ServiceProblem.Conflict(detail, errors, extensions),
                HttpStatusCode.Conflict => ServiceProblem.Conflict(detail, extensions),
                HttpStatusCode.UnprocessableEntity =>
                    ServiceProblem.Validation(new Dictionary<string, string[]>(StringComparer.Ordinal), detail, extensions),
                HttpStatusCode.BadRequest => ServiceProblem.BadRequest(detail, extensions),
                _ => ServiceProblem.ServerError(detail, extensions)
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
#pragma warning disable CA1812 // System.Text.Json constructs this private wire DTO through reflection.
    private sealed record ProblemDetailsDto(string? Detail, IReadOnlyDictionary<string, string[]>? Errors)
#pragma warning restore CA1812
    {
        /// <summary>
        /// Gets or sets all additional ProblemDetails extension members.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extensions { get; set; }
    }
}
