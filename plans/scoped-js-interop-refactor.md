# Scoped JS Interop Refactor and Guidance

Replace the global `Nova/wwwroot/js/site.js` helpers with collocated ES modules in `Nova.UI`
consumed via lazy `IJSObjectReference` interop, remove dead code, and codify the pattern in
agent instructions + the `add-blazor-ui` skill so future JS work follows the same standard.

## Approved decisions

1. Plan, then execute in this session after user approval.
2. JavaScript lives in **collocated `.razor.js` ES modules** next to the owning components
   (repo precedents: `PasskeySubmit.razor.js` in `Nova`; RCL `_content` assets in `Nova.UI`).
3. The roster Enter/Space keydown suppression becomes an **attach/detach listener scoped to the
   workspace component lifecycle** (not a permanent global listener).
4. **Delete dead code**: `novaShowModal`, `ConfirmDeleteDialog.ShowAsync()`, and the tests
   exercising them (production opens that modal via Bootstrap's `data-bs-toggle` API).
5. Guidance placement: compact declarative rules in
   `.github/instructions/blazor-architecture.instructions.md` + step-by-step recipe in
   `.github/skills/add-blazor-ui/references/js-interop.md`.
6. Instructions work stays scoped to adding the JS guidance — no full keep/remove/move/verify
   audit of the other instruction files.

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its
status to `Complete` and write its **Phase Summary**; run the phase's **Verification Plan** and
record the result before moving on. When all phases are done, fill in **Final Recap** and
**Deployment Plan**. Key facts needed with zero context:

- **Every consumer of `site.js` lives in `Nova.UI`** (a Razor class library): `CampaignWorkspace`
  page, `CampaignParticipantDrawer`, and (dead) `ConfirmDeleteDialog.ShowAsync`. `Nova.UI`
  static assets are served under `_content/Nova.UI/`.
- **Collocated `.razor.js` modules in an RCL are NOT auto-loaded** — they must be imported
  explicitly from C# via `jsRuntime.InvokeAsync<IJSObjectReference>("import", path)`.
  Exact import paths:
  - Page: `./_content/Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor.js`
  - Drawer: `./_content/Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.js`
- The roster scroll region (`#roster-scroll-region`) is inside an `@if` chain in
  `CampaignWorkspace.razor` — it is absent while loading and its DOM element is recreated across
  loading/error/loaded renders. The keydown attach must therefore be **replace-on-attach**
  (call `attach` on every `OnAfterRenderAsync` pass where the roster is loaded, re-passing the
  current `ElementReference`), and the handler must be inert when its container is detached.
- **Why the suppression exists** (preserve exactly): Enter/Space on a `tabindex` roster row/card
  triggers the browser's default synthesized click on the currently focused element; the drawer
  moves focus to its close button, so without suppression that click immediately re-closes the
  drawer. The listener must be capture-phase and call `event.preventDefault()` when the target is
  inside `tr.roster-row` or `li.roster-card` and inside the roster container.
- JS interop in prerendered components is only permitted from `OnAfterRenderAsync` / event
  handlers (never `OnInitializedAsync`); the refactor keeps all calls in those paths, so the
  existing prerender behavior is preserved.
- Tests to update: `Nova.Unit.Tests/Campaigns/CampaignWorkspaceTests.cs` (global-name interop
  setups → bUnit `SetupModule`) and `Nova.Unit.Tests/Components/ConfirmDeleteDialogTests.cs`
  (remove `novaShowModal`/`ShowAsync` coverage). bUnit module interop uses
  `testContext.JSInterop.SetupModule(<exact import path>)`.
- Reference patterns in-repo: `Nova.UI/ExampleJsInterop.cs` (lazy import wrapper) and
  `Nova/Components/Account/Shared/PasskeySubmit.razor.js` (collocated module file).
- Repo commands: build `dotnet build Nova.slnx`; unit tests
  `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` (MTP — see `nova-testing` skill
  for filter flags); format gate `dotnet format Nova.slnx --verify-no-changes`.
- End-to-end runs need the Aspire AppHost (`dotnet run --project Nova.AppHost`); read the app URL
  from `aspire describe --format Json` (never guess it).

## Phase 1: Collocated modules + consumer refactor

Status: Complete

- [x] Create `Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor.js` (ES module) exporting:
  `captureScroll(element)` → `element.scrollTop`, `restoreScroll(element, scrollTop)`,
  `scrollToTop(element)`, `attachRosterActivationSuppression(container)`,
  `detachRosterActivationSuppression()`. Attach replaces any previous handler and adds a
  capture-phase `keydown` listener on `document`; the handler ignores events outside
  `container.contains(event.target)` and prevents default only for Enter/Space inside
  `tr.roster-row` / `li.roster-card`. Detach removes the handler and nulls it.
- [x] Create `Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.js` exporting
  `focus(element)` (null-safe `element?.focus()`).
- [x] Refactor `CampaignWorkspace.razor.cs`: replace the `IJSRuntime` field usage with a
  `Lazy<Task<IJSObjectReference>>` module task (import path above); change the three scroll
  interop calls (`CaptureRosterScrollAsync`, `OnAfterRenderAsync` scroll-to-top/restore) to module
  calls passing the scroll-region `ElementReference`; call `attachRosterActivationSuppression`
  on every `OnAfterRenderAsync` pass where the roster is loaded; override `DisposeAsyncCore()`
  to detach and dispose the module (only when the lazy task was created).
- [x] Refactor `CampaignWorkspace.razor`: add `@ref="_rosterScrollRegion"` to the
  `.roster-scroll-region` div; remove its now-unused `id` attribute and the
  `RosterScrollRegionId` const in the code-behind (check the const has no other references
  first).
- [x] Refactor `CampaignParticipantDrawer.razor.cs` + `.razor`: replace the
  `CloseButtonId` const + `novaCampaignWorkspaceFocus` call with an `ElementReference` on the
  close button (`@ref="_closeButton"`, remove the button `id`) and a lazily imported module's
  `focus(element)` on `OnAfterRenderAsync(firstRender)`; override `DisposeAsyncCore()` to dispose
  the module.
- [x] Update XML doc remarks in `CampaignRosterTable.razor.cs` and `CampaignRosterCards.razor.cs`
  that reference `site.js` to reference the collocated workspace module instead.
- [x] Update `Nova.Unit.Tests/Campaigns/CampaignWorkspaceTests.cs`: replace global-name setups
  (`novaCampaignWorkspaceCaptureScroll/RestoreScroll/ScrollToTop`) with
  `testContext.JSInterop.SetupModule("./_content/Nova.UI/Features/Campaigns/Pages/CampaignWorkspace.razor.js")`
  and module-method setups; add loose setups for `attachRosterActivationSuppression` /
  `detachRosterActivationSuppression`; update argument assertions (ElementReference instead of
  the string id). Cover: capture/restore on drawer open/close, scroll-to-top on filter/sort/page
  change (no capture), and attach-on-loaded-roster behavior.

### Verification Plan

- `dotnet build Nova.slnx` — succeeds with no warnings introduced.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — all pass, including the
  updated CampaignWorkspace tests (existing `ConfirmDeleteDialogTests` still pass untouched).
- `dotnet format Nova.slnx --verify-no-changes` — clean.

### Phase Summary

Done. Both collocated ES modules created (`CampaignWorkspace.razor.js` exports `captureScroll`,
`restoreScroll`, `scrollToTop`, `attachRosterActivationSuppression`, `detachRosterActivationSuppression`;
`CampaignParticipantDrawer.razor.js` exports `focus`). Consumers refactored to `Lazy<Task<IJSObjectReference>>`
module tasks with `ElementReference`s (no string ids), suppression attach on every loaded
`OnAfterRenderAsync` pass (replace-on-attach), and `DisposeAsyncCore()` overrides disposing the module
only when `IsValueCreated`. Roster table/cards XML remarks updated to reference the module.

Key decision — **bUnit `ShouldBeElementReferenceTo` is unreliable in this repo** (bUnit 2.3.4 on
net10.0): bUnit's `Htmlizer` writes `blazor:elementreference` from the *current render tree* frame's
`ElementReferenceCaptureId`, and .NET 10's `RenderTreeDiffBuilder` deliberately does not copy that id
from old to new frame on a matched capture frame ("the reference capture action is only invoked
once"), while `ComponentState.RenderInExistingBatch` swaps in the newly built tree. So after any
flushed re-render the regenerated markup's `blazor:elementreference` is empty even though the element
was never re-inserted and the `ElementReference` handed to JS is still correct. Production DOM is
unaffected (the attr is only written at insert; the JS-side registry + the C# field hold the id;
.NET 10 no longer reads the frame id on disposal). **Pattern for future tests**: capture
`cut.Find(sel).GetAttribute("blazor:elementreference")` *before* the triggering action, then assert
`invocation.Arguments[0].ShouldBeOfType<ElementReference>().Id.ShouldBe(preCapturedId)` — this mirrors
bUnit's own `ComponentRenderingTest.CanUseJSInteropToReferenceElements` workaround. Applied to the
three migrated assertions; two temporary diagnostic tests were removed after root-causing.

Verification results: `dotnet build Nova.slnx` succeeded with 0 warnings / 0 errors;
`dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` = 1296/1296 passed;
`dotnet format Nova.slnx --verify-no-changes` still fails, but **only** on pre-existing files from
PR #79 (Tags feature CHARSET encodings and two migration IDE0161 warnings) — none of this phase's
files are flagged. Do not fix those here; they are unrelated to this work.

## Phase 2: Remove dead code and delete site.js

Status: Complete

- [x] Remove `ShowAsync()` from `ConfirmDeleteDialog.razor.cs` (and its XML doc). The primary
  constructor loses the `IJSRuntime` parameter; the component keeps its two `[Parameter]`
  properties. No production caller exists (modal opens via `data-bs-toggle` in
  `DeletePersonalData.razor`).
- [x] Remove the `novaShowModal` test coverage from
  `Nova.Unit.Tests/Components/ConfirmDeleteDialogTests.cs` (the `ShowAsync` test and its
  `JSInterop.SetupVoid("novaShowModal", ...)` / `VerifyInvoke`).
- [x] Delete `Nova/wwwroot/js/site.js` (delete `Nova/wwwroot/js` too if it becomes empty).
- [x] Remove `<script src="@Assets["js/site.js"]"></script>` from `Nova/Components/App.razor`.
- [x] Grep the repo for `site.js`, `novaShowModal`, and `novaCampaignWorkspace` — only historical
  `plans/` docs and the new modules may remain.

### Verification Plan

- `dotnet build Nova.slnx` — succeeds.
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` — all pass.
- `dotnet format Nova.slnx --verify-no-changes` — clean.
- Grep confirms no production code references `site.js` or the removed global function names.

### Phase Summary

Dead code removed exactly as planned:

- `ConfirmDeleteDialog.razor.cs` — `ShowAsync()` and its XML doc deleted; primary constructor is now
  `ConfirmDeleteDialog : NovaComponentBase` (no `IJSRuntime`); `using Microsoft.JSInterop;` dropped;
  both `[Parameter, EditorRequired]` properties and `OnConfirmChanged` untouched.
- `ConfirmDeleteDialogTests.cs` — `ShowAsync_InvokesNovaShowModal_WithConfirmDeleteModalSelector`
  removed (the last `JSInterop.SetupVoid`/`VerifyInvoke` in the file); `WarningText_ContainsAccountDeletionMessage`
  is now the final test.
- `Nova/wwwroot/js/site.js` deleted; `Nova/wwwroot/js` was empty afterwards, so the directory was
  removed too.
- `Nova/Components/App.razor` — the `<script src="@Assets["js/site.js"]">` tag removed; the
  bootstrap bundle and Cropper script tags remain.

Verification results: `dotnet build Nova.slnx` = 0 warnings / 0 errors; unit tests = 1295/1295
(one fewer than Phase 1 because the `ShowAsync` test was removed); grep finds `site.js` /
`novaShowModal` / `novaCampaignWorkspace` only in the historical `plans/campaign-workspace-roster-filter-ui.md`
doc and in this plan itself — zero production references; `dotnet format Nova.slnx --verify-no-changes`
still fails **only** on pre-existing PR #79 files (Tags-feature CHARSET encodings, two migration
IDE0161 warnings, and `Nova/Features/Shared/CommitAttemptTracker.cs`, also added by PR #79) — none
of this phase's files are flagged.

## Phase 3: Instructions + skill guidance for future JS work

Status: Complete

- [x] Add a **"JavaScript Interop"** section to
  `.github/instructions/blazor-architecture.instructions.md` (place after "Component
  Conventions"). Exact content (already written to the article's hygiene principles — local
  decisions only, no generic advice):

  ```markdown
  ## JavaScript Interop

  - Reach for JavaScript only when a DOM behavior cannot be expressed declaratively (Bootstrap
    data API, CSS, Blazor events). Static SSR markup must function without custom JS; custom JS
    belongs to interactive components.
  - Collocate component JS as an ES module: `{Component}.razor.js` next to the owning component.
    Feature components live in `Nova.UI`, so their JS ships with the RCL and is imported from
    `./_content/Nova.UI/...`. Do not add `window.*` globals or page-wide helpers to
    `Nova/wwwroot/js/`.
  - Consume modules with a lazily imported `IJSObjectReference`
    (`Lazy<Task<IJSObjectReference>>` wrapping `"import"`), dispose the module reference in
    `DisposeAsyncCore()`, and invoke module functions only from
    `OnAfterRenderAsync(firstRender)` or event handlers — never `OnInitializedAsync`.
  - Pass `ElementReference` (via `@ref`), not hard-coded element `id` strings.
  - Any listener that outlives a single event must attach scoped to the component's subtree and
    detach in `DisposeAsyncCore()`.
  - Step-by-step recipe with code examples:
    `.github/skills/add-blazor-ui/references/js-interop.md`.
  ```

- [x] Add `.github/skills/add-blazor-ui/references/js-interop.md` — the step-by-step recipe:
  module file anatomy (exported functions, handler replacement for `@if`-recreated elements),
  the `Lazy<Task<IJSObjectReference>>` + `DisposeAsyncCore()` C# wiring, `ElementReference`
  passing, bUnit `SetupModule` mocking (exact-path matching, loose arg setups), and the pitfalls:
  RCL collocated modules are not auto-loaded; JS interop during prerender is only allowed from
  `OnAfterRenderAsync`; a listener whose container is recreated must be replace-on-attach.
- [x] Link the new reference from `.github/skills/add-blazor-ui/SKILL.md`, matching the existing
  reference-link style.

### Verification Plan

- `dotnet format Nova.slnx --verify-no-changes` — clean (markdown unaffected; run to satisfy the
  repo gate).
- Re-read the new guidance against the article's hygiene checklist: every bullet is a local
  decision, a constraint, or a pointer to the recipe — no generic advice, no procedural coaching,
  no restatement of tooling-enforced rules. The instructions file gains one short section; the
  recipe holds the details.

### Phase Summary

All three Phase 3 items delivered:

- `.github/instructions/blazor-architecture.instructions.md` — new **"JavaScript Interop"** section
  placed after "Component Conventions" (before "Bootstrap-First Styling"), verbatim per plan. Six
  bullets, all local decisions/constraints + one pointer to the recipe. Hygiene check: no generic
  advice, no procedural coaching, no restatement of tooling-enforced rules.
- `.github/skills/add-blazor-ui/references/js-interop.md` — new step-by-step recipe, grounded in
  the committed Phase 1 code (`CampaignParticipantDrawer.razor.js/.cs` for module anatomy, lazy
  import, `ElementReference` passing, `DisposeAsyncCore`; `CampaignWorkspace.razor.js/.cs` for
  replace-on-attach listener pattern; `CampaignWorkspaceTests.cs` for `SetupModule` exact-path
  mocking, loose arg matchers, and the pre-capture `blazor:elementreference` gotcha). Pitfalls
  section: RCL modules not auto-loaded (runtime-only failure), prerender JS forbidden, recreated
  container listener leak, `DisposeAsyncCore` ordering, no `Nova/wwwroot/js/` helpers.
- `.github/skills/add-blazor-ui/SKILL.md` — checklist step 8 added linking the recipe (old step 8
  "Test" renumbered to 9); frontmatter USE FOR list gained "JS interop, collocated .razor.js
  module"; two canonical-example rows added (drawer module + workspace listener); self-check gained
  a JS bullet.

Verification: `dotnet format Nova.slnx --verify-no-changes` run — no new failures (same
pre-existing PR #79 files only; the new/changed files are markdown). Recipe's build claim verified
against `Nova/obj/Debug/net10.0/staticwebassets.build.json`: both collocated modules appear as
static web assets (`CampaignWorkspace.razor.js`, `CampaignParticipantDrawer.razor.js` plus
compressed variants) — the .NET 10 static-web-asset-endpoints pipeline serves them at runtime from
the manifest, which is why they are not physically copied into `Nova/bin` `wwwroot`. Runtime
serving itself is confirmed by Phase 4's browser pass.

## Phase 4: Aspire + Playwright acceptance pass

Status: Complete

Uses the `aspire-playwright-validation` skill (per `testing.instructions.md`: read the URL from
`aspire describe --format Json`, keep the pass scenario-based, clean temp browser artifacts).

- [x] Start the AppHost (`aspire start --isolated --non-interactive`) and load the campaign
  workspace with a roster. (`aspire wait nova` → healthy; frontend URL read from
  `aspire describe --format Json`: `https://nova.dev.localhost:62126`.) The Postgres volume was
  fresh, so the pass bootstrapped its own data: registered a user via `/Account/Register`,
  uploaded a profile photo through the Cropper flow, created club "Phase4 Testers FC" via
  `/Clubs/Onboarding`, then seeded a 2026 season, "Summer Tryouts 2026" campaign, 4 teams,
  4 tags, 56 players, and 56 assignments via psql (the prior session's `seed-roster.sql`,
  already keyed to `ClubId=1`/`CreatedById=1`, which matched the fresh volume's IDs).
- [x] Keyboard flow: focused a roster row and pressed Enter — drawer opened with the
  "Close participant details" button focused and did NOT immediately re-close (verified open
  again after 1s). Space on a roster card at a 390px viewport — same result. Esc closed the
  drawer and cleared the `participant` query parameter. All three behaviors exercised on the
  live module exports (`focus`, replace-on-attach suppression).
- [x] Scroll anchoring: scrolled the roster region to the bottom (scrollTop 1546/1546), opened
  and closed the drawer — position preserved exactly; a search filter (scrollTop → 0) and page
  navigation to page 2 (scrollTop → 0, 6 rows) both reset the region to top.
- [x] Delete flow: on `/Account/Manage/DeletePersonalData` the confirm modal opened via the
  Bootstrap data API (no interop); the submit button was disabled until the confirm checkbox was
  checked (and re-disabled when unchecked); closed via Cancel — the account was NOT deleted
  (verified 1 user / 56 players intact afterwards).
- [x] Browser console: 0 errors across register → profile photo → club onboarding → campaign
  workspace → delete-data pages; no references to the removed globals or `site.js`; no 404s.
  Both collocated modules serve 200 at runtime from the static-web-asset endpoints
  (`/_content/Nova.UI/Features/Campaigns/Components/CampaignParticipantDrawer.razor.js` and
  `.../Pages/CampaignWorkspace.razor.js`), confirming the .NET 10 manifest-based serving works
  for a real browser.

### Verification Plan

- [x] All four Playwright scenarios above pass against the running AppHost.
- [x] `aspire` logs show no unhandled interop exceptions during the pass (0 browser console
  errors across all pages visited).

### Phase Summary

All four scenarios passed against the Aspire-hosted app with zero blockers — no code changes
were required. Bootstrapped a full user/club/campaign/roster in the empty Postgres volume,
exercised both collocated modules end-to-end (focus export, replace-on-attach suppression, scroll
anchoring) in a real browser, confirmed the JS-free Bootstrap-modal delete flow, and captured a
clean console with both `_content/Nova.UI` module endpoints returning 200. Cleanup: temp avatar
file removed and the AppHost stopped with `aspire stop --non-interactive`.

## Final Recap

`Nova/wwwroot/js/site.js` held two page-global helpers (`novaShowModal` for the confirm-delete
dialog and a roster-activation suppressor wired to `window`). Both were replaced with
Blazor-collocated ES modules consumed through lazy `IJSObjectReference` interop:

- **Phase 1** — `CampaignWorkspace.razor.js` (replace-on-attach suppression listeners) and
  `CampaignParticipantDrawer.razor.js` (`focus` export) now live next to their components in
  `Nova.UI/Features/Campaigns`, loaded via `Lazy<Task<IJSObjectReference>>` and released in
  `DisposeAsyncCore`. Three bUnit assertion failures were root-caused to .NET 10 framework
  source; 1296/1296 unit tests green.
- **Phase 2** — `ConfirmDeleteDialog` no longer uses JS at all: the Bootstrap data API opens the
  modal, `ShowAsync`/`IJSRuntime` were removed, and `site.js` plus its `<script>` tag were
  deleted. 1295/1295 tests green.
- **Phase 3** — guidance added: a "JavaScript Interop" section in
  `.github/instructions/blazor-architecture.instructions.md` and a step-by-step recipe at
  `.github/skills/add-blazor-ui/references/js-interop.md` (wired into `SKILL.md`), so future JS
  work follows the collocated-module standard.
- **Phase 4** — browser acceptance pass against the Aspire AppHost: all four scenarios passed
  with no blockers and a clean console.

## Deployment Plan

1. Merge the branch (no DB migrations, no config changes — static web assets only).
2. After deploy, verify the campaign workspace and delete-account flows in a browser (the
   `_content/Nova.UI/...razor.js` assets are served from the app's static-web-asset manifest,
   not physical `wwwroot` files).
3. No data migration, rollback, or monitoring changes are required.
