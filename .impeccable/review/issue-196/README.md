# Issue 196 implementation evidence

The user selected **Details and readiness** (`readiness-split`) from the three served comps. The approved comp and provenance are in `../../mocks/decision/issue-196-readiness-split.{png,json}`. The direction contract is `../../surfaces/campaign-spine.md`; Fieldhouse Wayfinding remains defined by the repository's DESIGN.md and PRODUCT.md.

## Surface and scope

Campaigns uses one season-grouped, role-filtered operational board with 20-row paging and contextual actions. Creation saves an administrator-only Draft in the authoritative current season. Preparation pairs campaign details and durable teams with readiness. The URL-backed opening review refreshes before commitment; retries retain the original operation and show the immutable receipt's enrollment count on Roster. Draft deletion preserves teams. Players/Teams correction routes retain a validated local return context.

The existing Active/Closed workspace supplies the minimal Roster destination. The Route Markers and workspace redesign remains #197. Eligibility/effective-placement semantics remain #213/#214. No schema migration or new lifecycle mutation was added.

## Captures

All images were captured from the real Aspire-hosted app by CampaignDraftBrowserTests. The `hero.png` frame is 1505×1045, matching the comp. `desktop.png` is 1440 wide. Mobile images are 390 wide. Warning and blocker desktop scenarios use 1280. Full-page mobile captures include the existing fixed bottom navigation at its viewport position.

| State | Desktop | Mobile |
|---|---|---|
| Draft opening review | [Comp viewport](hero.png), [1440](desktop.png) | [390](preparation-mobile.png) |
| Current-season/first-season creation | [Desktop](creation-desktop.png) | [Mobile](creation-mobile.png) |
| Campaign directory | [Desktop](directory-desktop.png) | [Mobile](directory-mobile.png) |
| No campaigns after deletion | [Feedback](directory-empty.png) | Covered by responsive directory layout |
| Ordinary member with no visible campaigns | [Member](directory-member-empty.png) | Covered by responsive directory layout |
| Zero-player blocker | [Blocked](preparation-blocked.png) | Shared readiness stack |
| No-team warning and long name | [Long/warning](preparation-warning-long.png) | [Long/warning mobile](preparation-warning-long-mobile.png) |
| Receipt and existing roster machinery | [Roster](roster-desktop.png) | [Roster mobile](roster-mobile.png) |

## Fidelity and review

The measured hero and responsive gates passed at approximately 89%. State/spec are in `../../build/`; region comparisons are in `../diff/hero/`, `../diff/desktop/`, and `../diff/final/`. The fresh review and bounded verdict are recorded in `finish-review.md`: the reviewer scored all four material fixes resolved and returned **ship** for that fix list.

The detector reported eight advisory type-ramp differences and no failures. The approved comp uses larger local type; desktop spacing/type uses a bounded fluid unit, while mobile retains rem sizing. The existing shell's dimensions, native focus rings, and semantic danger color are preserved. These differences are explicit; no force override was used.

## Verification

Solution build: passed with zero warnings. Unit suite: 2,273 passed. PostgreSQL/HTTP integration suite: 474 passed. Full browser suite: 120 passed, seven existing optional screenshot-only checks skipped; the three new journey tests execute without flags. Format verification and theme contrast/token checks passed.

Provider tests verify visibility before counts/paging, current-season ordering, closure ordering, bounded team preview and existing lifecycle/activity contracts. Component tests cover readiness freshness, replay and immutable counts, persisted creation recovery and identity invalidation, and late-route response suppression. Browser tests cover first-season creation, correction returns, opening by keyboard and focused Roster feedback, inline team creation, team-preserving deletion, long/mobile content, and member direct-link exclusion.

The approved hero checkpoint is also persisted at `../hero-repro.png`: the 1505×1045 `hero.png` capture associated with the passed 89% hero gate in `.impeccable/build/state.json`. The final packet refresh preserves this checkpoint association.

## Code-review corrections

Metadata edits clear contextual server validation on input changes without reintroducing the unchanged error snapshot. Opening confirmation now persists the retained operation ID before every submission, so a failed storage write cannot be bypassed by recovery. These are behavior corrections; the approved composition is unchanged.

| Requirement | Regression evidence in `CampaignEntryTests` |
|---|---|
| Correct a server-rejected date and submit again, including after a parent render | `CampaignEntry_ResubmitsMetadata_AfterCorrectingServerValidation` |
| Block initial and recovery opening calls while storage fails; then submit the same operation once | `CampaignEntry_RetriesStorageBeforeOpening_WithTheSameOperation` |

The focused command `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj --no-build --filter-class '*CampaignEntryTests'` passed all 14 cases. The full unit and browser suites and format verification were rerun successfully after the corrections. Provider contracts and persistence services are unchanged.
