using System.Diagnostics;

namespace Nova.Client.Telemetry;

/// <summary>
/// Adds W3C trace context headers to outbound HTTP requests from the Blazor WebAssembly client.
/// </summary>
public sealed class TraceParentPropagatingHandler : DelegatingHandler
{
    /// <summary>
    /// Sends an HTTP request and ensures a <c>traceparent</c> header is present for correlation.
    /// </summary>
    /// <param name="request">The outgoing request message.</param>
    /// <param name="cancellationToken">A token to cancel the operation.</param>
    /// <returns>The HTTP response message.</returns>
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        Activity? activity = null;

        try
        {
            activity = ClientTelemetry.ActivitySource.StartActivity(
                ClientTelemetry.OutboundHttpActivityName,
                ActivityKind.Client);

            if (!request.Headers.Contains("traceparent"))
            {
                string traceParent = activity is not null
                    ? CreateTraceParentValue(activity.TraceId, activity.SpanId, "01")
                    : CreateFallbackTraceParentValue();

                request.Headers.TryAddWithoutValidation("traceparent", traceParent);
            }

            return await base.SendAsync(request, cancellationToken);
        }
        finally
        {
            activity?.Stop();
            activity?.Dispose();
        }
    }

    /// <summary>
    /// Creates a W3C <c>traceparent</c> header value from the provided identifiers.
    /// </summary>
    /// <param name="traceId">The W3C trace identifier.</param>
    /// <param name="spanId">The W3C span identifier.</param>
    /// <param name="flags">The W3C trace flags value.</param>
    /// <returns>A formatted W3C <c>traceparent</c> header value.</returns>
    private static string CreateTraceParentValue(ActivityTraceId traceId, ActivitySpanId spanId, string flags) => $"00-{traceId.ToHexString()}-{spanId.ToHexString()}-{flags}";

    /// <summary>
    /// Creates a fallback W3C <c>traceparent</c> value when no activity can be created.
    /// </summary>
    /// <returns>A generated W3C <c>traceparent</c> header value with sampled flag.</returns>
    private static string CreateFallbackTraceParentValue()
    {
        return CreateTraceParentValue(
            ActivityTraceId.CreateRandom(),
            ActivitySpanId.CreateRandom(),
            "01");
    }
}
