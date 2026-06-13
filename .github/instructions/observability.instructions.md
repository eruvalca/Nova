---
applyTo: "Nova.ServiceDefaults/**/*.cs,Nova.Client/Telemetry/**/*.cs,**/Program.cs"
description: "Observability conventions for OpenTelemetry wiring, tracing correlation, Blazor instrumentation, and HTTP trace propagation."
---

# Observability

- Use W3C trace context as the correlation standard. Correlate work via `Activity.Current` and propagated trace context; do not introduce a custom `correlationId` header system.
- Keep OpenTelemetry pipeline wiring and `ActivitySource` registration in `Nova.ServiceDefaults`. Application projects should consume that setup instead of duplicating tracing configuration.
- Include the `Microsoft.AspNetCore.Components` source in tracing configuration so Blazor component activity is captured alongside HTTP and application spans.
- For WebAssembly outbound calls, use DI-registered `HttpClient` instances configured with trace propagation handlers (`traceparent`/`tracestate`). Avoid ad hoc `new HttpClient()` creation.
- When returning `ProblemDetails`, surface the current trace identifier (`traceId`) from the active activity/HTTP trace context so failures can be correlated in logs and traces.
