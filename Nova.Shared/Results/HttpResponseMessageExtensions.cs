using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

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
            var extensions = problemBody?.Extensions?
                .Where(static entry => entry.Key is not ("type" or "title" or "status" or "instance"))
                .ToDictionary(entry => entry.Key, entry => (object?)entry.Value);

            // If we have structured validation errors and a 400 status, it's a Validation problem.
            if (response.StatusCode == HttpStatusCode.BadRequest && errors is not null && errors.Count > 0)
            {
                return ServiceProblem.Validation(errors, detail, extensions);
            }

            // Otherwise, map the status code to a problem kind.
            return response.StatusCode switch
            {
                HttpStatusCode.NotFound => ServiceProblem.NotFound(detail, extensions),
                HttpStatusCode.Forbidden => ServiceProblem.Forbidden(detail, extensions),
                HttpStatusCode.Conflict => ServiceProblem.Conflict(detail, extensions),
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
    private sealed class ProblemDetailsDto
    {
        /// <summary>
        /// Gets or sets the problem detail message.
        /// </summary>
        public string? Detail { get; set; }

        /// <summary>
        /// Gets or sets structured validation errors when present.
        /// </summary>
        public IReadOnlyDictionary<string, string[]>? Errors { get; set; }

        /// <summary>
        /// Gets or sets all additional ProblemDetails extension members.
        /// </summary>
        [JsonExtensionData]
        public Dictionary<string, JsonElement>? Extensions { get; set; }
    }
}
