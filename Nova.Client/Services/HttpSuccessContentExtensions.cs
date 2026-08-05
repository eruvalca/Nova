using System.Net.Http.Json;
using System.Text.Json;
using Nova.Shared.Results;

namespace Nova.Client.Services;

/// <summary>
/// Provides consistent deserialization for required successful HTTP response bodies.
/// </summary>
internal static class HttpSuccessContentExtensions
{
    extension(HttpContent content)
    {
        /// <summary>
        /// Deserializes and validates a required JSON success payload.
        /// </summary>
        /// <typeparam name="T">The expected payload type.</typeparam>
        /// <param name="errorDetail">The detail returned when the payload violates the contract.</param>
        /// <param name="validator">An optional predicate for payload invariants.</param>
        /// <param name="cancellationToken">A token to cancel deserialization.</param>
        /// <returns>The payload or a server problem for an invalid successful response.</returns>
        public async Task<ServiceResult<T>> ReadRequiredJsonAsync<T>(
            string errorDetail,
            Func<T, bool>? validator = null,
            CancellationToken cancellationToken = default)
        {
            try
            {
                var value = await content.ReadFromJsonAsync<T>(cancellationToken);
                return value is not null && (validator?.Invoke(value) ?? true)
                    ? value
                    : ServiceProblem.ServerError(errorDetail);
            }
            catch (JsonException)
            {
                return ServiceProblem.ServerError(errorDetail);
            }
        }
    }
}
