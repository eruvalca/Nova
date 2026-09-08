using System.Net.Http.Json;
using System.Text.Json;

namespace Nova.SharedKernel.Results;

/// <summary>
/// Provides consistent deserialization for required successful HTTP response bodies.
/// </summary>
public static class HttpSuccessContentExtensions
{
    /// <summary>
    /// Strict web defaults: every positional record constructor parameter is required and
    /// non-nullable reference-type members reject JSON nulls, so a contract-violating success
    /// payload fails loudly instead of silently defaulting.
    /// </summary>
    private static readonly JsonSerializerOptions _strictWebOptions = new(JsonSerializerOptions.Web)
    {
        RespectRequiredConstructorParameters = true,
        RespectNullableAnnotations = true
    };

#pragma warning disable CA1034 // The extension block generates nested marker types, as in the other HTTP helper.
    extension(HttpContent content)
#pragma warning restore CA1034
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
                var json = await content.ReadFromJsonAsync<JsonElement>(cancellationToken);
                if (json.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                {
                    return ServiceProblem.ServerError(errorDetail);
                }

                var value = json.Deserialize<T>(_strictWebOptions);
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
