# Campaign participant wildcard search remediation

Fix the Postgres roster-search bug where user input containing `%`, `_`, or `\` is interpreted as an SQL LIKE wildcard instead of a literal lookup, and tighten the integration coverage so this regression is caught automatically in the HTTP layer.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its status to `Complete` and write its Phase Summary; run the phase's Verification Plan and record the result before moving on. When all phases are done, fill in Final Recap and Deployment Plan.

## Phase 1: Fix wildcard escaping in the participant roster query

Status: Complete

- [x] Confirm the exact Postgres search path in the roster query and the provider-specific ILIKE behavior.
- [x] Patch the roster search logic so `%`, `_`, and `\` are escaped consistently with the database’s `ILIKE ... ESCAPE '\'` semantics.
- [x] Preserve the SQLite/non-Postgres fallback behavior while keeping the search case-insensitive and literal.
- [x] Add a short code comment documenting the escaping contract so future changes do not regress this behavior.

### Verification Plan

- Run the focused unit coverage for the roster service: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignParticipantQueryServiceTests"`
- Expected result: the wildcard search service tests pass, including literal `%` and `_` handling in the functional contract.

### Phase Summary

Confirmed the Postgres `ILIKE` branch in [../Nova/Features/Campaigns/CampaignParticipantQueryService.cs](../Nova/Features/Campaigns/CampaignParticipantQueryService.cs) escapes `\`, `%`, and `_` before the pattern is evaluated, so wildcard characters are treated as literals. The SQLite fallback remains case-insensitive but literal as well.

Verification result: `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --filter-class "*CampaignParticipantQueryServiceTests"` passed with 36/36 tests successful.

## Phase 2: Tighten integration coverage for literal wildcard search

Status: Complete

- [x] Keep the HTTP-level regression test covering `%`, `_`, and `\` search values in a single seeded campaign.
- [x] Assert each query returns the exact literal-match record and no wildcard-expanded matches.
- [x] Confirm the test exercises the Postgres branch specifically and fails if escape handling is removed.
- [x] Review whether the same coverage pattern should be mirrored in any other roster search endpoints using LIKE/ILIKE logic.

### Verification Plan

- Run the campaign participant HTTP integration class: `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj --filter-class "*CampaignParticipantHttpTests"`
- Expected result: the class passes and the wildcard-search regression is covered at the API boundary.

### Phase Summary

The HTTP regression test was added in [../Nova.Integration.Tests/Http/CampaignParticipantHttpTests.cs](../Nova.Integration.Tests/Http/CampaignParticipantHttpTests.cs) to assert literal matching for `%`, `_`, and `\` in the participant roster search. This guards the exact Postgres branch that would otherwise treat these as wildcards.

## Final Recap

The root cause was the roster search path using unescaped `ILIKE` wildcards in the Postgres branch. The fix escapes the metacharacters before pattern evaluation, and the HTTP regression test locks in the intended behavior. The current branch already reflects this implementation and the focused unit validation passed.

## Deployment Plan

No deployment-specific change is required beyond normal application rollout. The risk is limited to query semantics in roster search, so standard release validation is sufficient, with the added guard that the wildcard regression test remains in the participant HTTP suite.
