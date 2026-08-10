# Aspire 13.4 Upgrade Review

Review the Aspire 13.4 "what's new" changes against Nova's current Aspire setup,
apply the safe cleanups, and record recommendations for changes the project would
benefit from.

**Scope:** Aspire AppHost (`Nova.AppHost`), ServiceDefaults (`Nova.ServiceDefaults`),
integration-test harness (`Nova.Integration.Tests`), and the Aspire CLI workflow.

**Baseline:** Nova was already fully on Aspire **13.4.6** (CLI, SDK, and all Aspire
packages) before this review. `aspire doctor` reports: CLI 13.4.6, .NET 10.0.301,
Docker v29.7.2 running — all checks pass. The project already benefits automatically
from 13.4 runtime improvements (faster AppHost startup, direct launch, metadata
caching, dynamic dashboard ports, macOS/Linux dev cert).

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done,
set its status to `Complete` and write its **Phase Summary**; run the phase's
**Verification Plan** and record the result before moving on. When all phases are
done, fill in **Final Recap** and **Deployment Plan**.

## Phase 1: Remove hardcoded dashboard/OTLP ports from launchSettings.json

Status: Complete

Aspire 13.4 assigns the dashboard OTLP and resource-service endpoint ports
dynamically; they no longer belong in `launchSettings.json`. Nova's
`Nova.AppHost/Properties/launchSettings.json` still hardcoded
`ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL` and `ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL`
for both the `https` and `http` profiles.

- [x] Remove `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL` from both profiles in `Nova.AppHost/Properties/launchSettings.json`
- [x] Remove `ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL` from both profiles in `Nova.AppHost/Properties/launchSettings.json`
- [x] Confirm the file is still valid JSON

### Verification Plan

- `Get-Content Nova.AppHost\Properties\launchSettings.json -Raw | ConvertFrom-Json` — must succeed (JSON valid).
- Grep for `ASPIRE_DASHBOARD_OTLP_ENDPOINT_URL` / `ASPIRE_RESOURCE_SERVICE_ENDPOINT_URL` — no remaining references in the repo.
- Optional smoke check: `dotnet run --project Nova.AppHost` starts and the dashboard comes up on dynamically assigned ports.

### Phase Summary

Removed the four stale dashboard/OTLP endpoint environment variables from both the
`https` and `http` launch profiles. `launchSettings.json` remains valid JSON and the
only remaining env vars are `ASPNETCORE_ENVIRONMENT` and `DOTNET_ENVIRONMENT`.
Verified with `ConvertFrom-Json`; no other repo references to the removed variables
exist. No code change in AppHost.cs was needed.

## Phase 2: Consider pinning the Postgres image (floating "18")

Status: Not started

Aspire 13.4 bumped the default Postgres image from 17.6 to 18.3, and Nova already
pins `WithImageTag("18")` in `Nova.AppHost/AppHost.cs`, so the 17→18 on-disk data
layout change is already behind it. Postgres minor releases within major 18 are
storage-compatible, so a later 18.x image cannot recreate that layout break. The
real decision is a general image-pinning tradeoff: keeping the floating `"18"` tag
picks up bug/security fixes automatically but gives loose reproducibility; pinning
`"18.3"` (or a digest) improves reproducibility but stalls updates until manually
bumped, and exact reproducibility requires a digest.

- [ ] Decide whether to pin a specific tag (e.g. `"18.3"`) or digest in `Nova.AppHost/AppHost.cs` for reproducibility, or keep floating `"18"` for automatic bug/security updates
- [ ] If pinning, verify the chosen 18.x image still boots against the existing dev volume (storage-compatible within major 18)
- [ ] Run `Nova.Integration.Tests` against the chosen tag; `NovaAppHostFixture` strips volumes, so persisted-volume compatibility is covered by the AppHost smoke check, not the tests

### Verification Plan

- `aspire run` (or `dotnet run --project Nova.AppHost`) — Postgres 18 container starts and `novadb` is reachable; the existing dev volume mounts without a startup error.
- `Nova.Integration.Tests` — `NovaAppHostFixture` still boots the AppHost and applies migrations (volumes are stripped in tests, so they are unaffected by the tag choice).

### Phase Summary

_(write when phase completes)_

## Phase 3: Adopt `--search` CLI diagnostics in the monitoring workflow

Status: Complete

Aspire 13.4 adds `--search` to `aspire logs` and the `aspire otel` commands
(logs/traces), plus field filters, and `aspire doctor` now reports CLI + AppHost SDK
versions. These make agent-driven diagnostics faster (search applied server-side
before streaming). No code change was required — the repo's skill references already
document the `--search` forms, so this phase was verification-only.

- [x] Review `.agents/skills/aspire-monitoring/` and `.github/instructions/` docs for log/trace lookup commands that should mention `--search`
- [x] Update doc examples to use `aspire logs --search "..."`, `aspire otel logs --search`, `aspire otel traces --search` where applicable
- [ ] Optional: surface `aspire doctor` version output in the CI docs / onboarding notes so CLI↔SDK version mismatches are caught early

### Verification Plan

- `aspire doctor` reports the Aspire CLI version (13.4.6) — confirmed 4/4 checks pass.
- Docs reference the `--search` forms — confirmed already present in `.agents/skills/aspire-monitoring/references/monitoring.md:31-42` and `.agents/skills/aspire-orchestration/references/agent-workflows.md:65-67`.

### Phase Summary

The `--search` adoption was already complete before this review: the repo's
`aspire-monitoring` and `aspire-orchestration` skill references document `--search`
for OTEL logs/spans, console logs, and traces. Verified during PR review; no doc
updates were needed. Only the optional `aspire doctor` onboarding follow-up remains,
deferred as a nice-to-have.

## Phase 4: Evaluate typed resource commands / WithProcessCommand (optional)

Status: Not started

Aspire 13.4 ships typed resource-command arguments (named CLI options, visibility
controls, immediate result display) and the experimental `WithProcessCommand` API.
Nova currently defines no custom resource commands. A natural candidate is a
dev-only command that e.g. seeds or resets the `novadb` database, or shows the
running .NET version, without shelling out manually.

- [ ] Decide whether a resource command adds value (e.g. "seed-demo-data" or "reset-novadb" on the postgres or nova resource)
- [ ] If yes, add the command via `WithCommand`/`WithProcessCommand` in `Nova.AppHost/AppHost.cs` (gate behind dev conditionals; use `#pragma` for ASPIREPROCESSCOMMAND001 if experimental)
- [ ] Verify the command appears in `aspire resource <name> --help` and runs via named options

### Verification Plan

- `aspire run` then `aspire resource novadb --help` lists the new command (if added).
- `aspire resource novadb <command> --<option> <value>` executes and shows the result in the dashboard/CLI.

### Phase Summary

_(write when phase completes)_

## Phase 5: Review other 13.4 features (assessed, not applicable / deferred)

Status: Not started

13.4 items reviewed and found **not applicable** to Nova's current architecture:

- **TypeScript AppHost GA, ATS GA** — Nova uses a C# AppHost; no `apphost.mts`/`.aspire/modules`. No action.
- **Go / Bun / JavaScript hosting** — no Go/Bun apps in the repo.
- **Blazor WebAssembly hosting integration (`AddBlazorWasmProject`/`AddBlazorGateway`)** — Nova.Client is a server-hosted WASM interactive component project inside `Nova`, not a standalone WASM app with a gateway. Deferred unless the client is ever split out.
- **Kubernetes / AKS / cert-manager / AGC / Helm / `WithAcrPullIdentity`** — no K8s or Azure Container Apps deployment in this repo (CI is build + unit tests only).
- **`WithPersistentLifetime()` on executables/projects** — Nova's web project should restart with the AppHost; Postgres data already persists via `WithDataVolume()`. No current need.
- **`WithHidden()` / `WithHiddenOnCompletion()`** — no setup-helper resources that need hiding from the dashboard.
- **NATS, RabbitMQ, Azure Front Door, Foundry hosted agents, `PublishAsPackageScript`** — not used.
- **`AspireUseCliBundle` preview opt-in** — not needed until AppHost orchestration deps move to the CLI bundle by default.
- **MCP AI-tool telemetry limits (800/2000)** — already in effect; no code change.

This phase is informational — confirm the "not applicable" list is still accurate at
review time and add checkboxes only if a future change makes one of these relevant.

- [ ] Confirm none of the deferred features apply to a planned Nova change; revisit this list when the app model changes (e.g. adding a separate service, a K8s deployment, or a standalone client)

### Verification Plan

- Re-read this section against the current AppHost model and any new repo plans; mark individual features applicable or confirm the list stays "not applicable".

### Phase Summary

_(write when phase completes)_

## Final Recap

_(write when all phases complete: summary of the entire piece of work)_

## Deployment Plan

_(write when all phases complete: step-by-step deployment instructions)_
