using System.Diagnostics;

namespace Nova.Client.Telemetry;

/// <summary>
/// Provides telemetry constants and activity sources for the Blazor WebAssembly client.
/// </summary>
public static class ClientTelemetry
{
    /// <summary>
    /// The activity name used for outbound HTTP requests initiated by the client.
    /// </summary>
    public const string OutboundHttpActivityName = "Nova.Client.Http.Outbound";

    /// <summary>
    /// The activity source used to create client-side tracing activities.
    /// </summary>
    public static readonly ActivitySource ActivitySource = new("Nova.Client");
}
