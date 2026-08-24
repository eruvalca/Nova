# Club Crest: Optional Cropping + Natural Aspect-Ratio Rendering

Let club admins optionally crop the crest they upload (both when creating a club and when
changing the crest on the admin page), and render the crest in its natural aspect ratio
everywhere except the NavMenu avatar. The NavMenu avatar (`img.nav-avatar`, 2rem circle) keeps
its fixed circular crop.

Scope decisions (confirmed with user):

1. Crop is available in **both** upload surfaces: `ClubCrestManager` (club admin page) and
   `CreateClubForm` (club creation/onboarding).
2. Crop UX: after file selection, show the existing Cropper.js component
   (`NovaCropperComponent`) with the full image pre-selected as the crop area. The user may
   adjust the crop or keep the whole image, then clicks **Save**. Crop is free-form (no
   enforced aspect ratio) and client-side (canvas export), matching the profile-photo flow.
3. The cropped bytes are what gets uploaded (JPEG export on a white background — same
   convention as `ProfilePhotoEditor`).
4. Server processing: the **small** variant stays a 64px center-cropped square (NavMenu
   avatar); the **medium** and **large** variants become aspect-preserving (no crop).
   Profile-photo processing is untouched.
5. Rendering: NavMenu avatar unchanged. Club detail header, admin manager preview, and
   create-form preview render the crest at its natural aspect ratio (CSS stops forcing a
   fixed square; sane max sizes still apply).
6. Existing crests already stored as square variants keep rendering as squares until
   re-uploaded (no backfill/reprocess — accepted).

## For Future Agents

As work proceeds: mark checkboxes `- [x]` as items complete; when a phase is done, set its
status to `Complete` and write its **Phase Summary** (what was done, key decisions, anything
needed to continue with zero context); run the phase's **Verification Plan** and record the
result before moving on. When all phases are done, fill in **Final Recap** and
**Deployment Plan**.

## Phase 1: Aspect-preserving crest variants (server)

Status: Complete

Suggested executor: orchestrator (small, tightly-coupled change in one file plus two call sites)

- [x] In `Nova/Features/Photos/ImageVariantProcessor.cs`, add crest variant generation that
  preserves aspect ratio for medium/large:
  - Add `GenerateCrestVariants(byte[] content, string contentType, CancellationToken)` that
    reuses the existing decode/sanitize (`AutoOrient`, strip EXIF/XMP) and `EncodeOriginal`
    helpers, then produces:
    - `Original` — sanitized re-encoded original (unchanged behavior).
    - `Small` — 64px center-cropped square WebP (reuse existing `EncodeSquareVariant`) for
      the NavMenu avatar.
    - `Medium` — WebP resized to fit within 256×256 **without cropping**
      (`ResizeMode.Max`), preserving aspect ratio.
    - `Large` — WebP resized to fit within 1024×1024 **without cropping**
      (`ResizeMode.Max`).
  - Refactor the shared sanitize + `EncodeOriginal` logic into private helpers so
    `GenerateVariants` (profile photos) and `GenerateCrestVariants` (crests) don't diverge.
    `GenerateVariants` itself stays square and unchanged for profile photos.
  - Update the class-level XML doc (currently claims "small, medium, and large
    center-cropped WebP square variants" for everything).
- [x] Switch the two crest call sites to `GenerateCrestVariants`:
  - `Nova/Features/Clubs/ClubCrestService.cs` `ChangeClubCrestAsync`.
  - `Nova/Features/Clubs/ClubService.cs` `CreateClubAsync`.
- [x] Update wording that claims crest variants are square:
  - `ClubEndpointRouteBuilderExtensions.cs` — `SelectBlobName` doc and `GetCrestHandler`
    comments (small = square avatar; medium/large = aspect-preserving).
  - `ClubCrestService` / `ClubService` XML docs that say "square variants".
- [x] Blob names, `ClubCrestEntity`, and the GET endpoint's size/ETag behavior are
  unchanged — **no migration**, no endpoint contract change.

### Verification Plan

- `dotnet build Nova.slnx` → succeeds. ✅ (run at the end of Phase 6: 0 errors)
- Unit tests (see Phase 6 for new tests; run existing first):
  `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → pass. ✅ (1812/1812 pass,
  includes the new `ImageVariantProcessorTests`)
- Ad-hoc check (optional, via a scratch console or test): generate variants from a
  non-square `TestImages` JPEG and assert medium/large decode to the same aspect ratio as
  the source while small decodes to 64×64. ✅ (covered by the committed
  `ImageVariantProcessorTests` — small = 64×64; medium/large aspect-preserving within
  tolerance, no dimension over the bound; `GenerateVariants` still squares)

### Phase Summary

Phase 1 implements aspect-preserving crest variants on the server. A new
`GenerateCrestVariants(byte[] content, string contentType, CancellationToken)` in
`Nova/Features/Photos/ImageVariantProcessor.cs` produces the `Original` (sanitized
re-encoded, behavior-unchanged), `Small` (64px center-cropped square WebP via the existing
`EncodeSquareVariant`), `Medium` (WebP fit within 256×256 with `ResizeMode.Max`), and
`Large` (fit within 1024×1024 with `ResizeMode.Max`) variants. The shared sanitize +
`EncodeOriginal` logic was refactored into private helpers (`DecodeSanitized`,
`EncodeFittedVariant`) so `GenerateVariants` (profile photos, still square/cropped) and
`GenerateCrestVariants` (crests, aspect-preserving medium/large) share one decode path
without diverging. Both crest call sites (`ClubCrestService.ChangeClubCrestAsync`,
`ClubService.CreateClubAsync` via `SaveCrestAsync`) now call `GenerateCrestVariants`.
Wording that claimed crest variants are square was updated in
`ClubEndpointRouteBuilderExtensions` (`SelectBlobName` doc, `GetCrestHandler` comments)
and the service XML docs. No blob-name/entity/endpoint contract changes; no migration.

## Phase 2: Reusable crop UI pieces (shared)

Status: Complete

Suggested executor: orchestrator (cross-feature move; must update all usages atomically)

- [x] Promote `NovaCropperComponent` from `Nova.UI/Features/Account/Components/` to
  `Nova.UI/Shared/` (second feature — Clubs — now needs it; per feature-folder rules),
  updating its namespace and the one usage in `ProfilePhotoEditor.razor`.
- [x] Add a small shared helper for cropper canvas export so the chunked
  `GetCroppedCanvasDataInBackgroundAsync` + stream-to-bytes logic is not copy-pasted a
  third time. Suggested: static helper in `Nova.UI/Shared/` (e.g. `CropperCanvasExport`
  with `ExportAsync(CropperComponent cropper, CancellationToken)` returning JPEG bytes,
  max dimension 1024, white `FillColor`, `ImageSmoothingQuality = "high"`).
  Refactor `ProfilePhotoEditor.SavePhotoAsync` to use it (behavior-identical).
- [x] Verify `Cropper.Blazor`'s `Options` surface for a free-form, full-image-preselected
  crop: no `AspectRatio` set (free-form), `ViewMode = Vm1`, `AutoCrop = true`,
  `AutoCropArea = 1` (crop box covers the whole image). Confirm exact property names in
  `Cropper.Blazor.Models.Options` at implementation time. ✅ (confirmed:
  `Options.AspectRatio` is `double?` (null = free-form), `ViewMode` enum with `Vm1`,
  `AutoCrop` bool, `AutoCropArea` `decimal` = 1.0)

### Verification Plan

- `dotnet build Nova.slnx` → succeeds. ✅ (0 errors)
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → pass (includes existing
  `ProfilePhotoEditorTests` — must stay green after the refactor). ✅ (1812/1812 pass;
  `ProfilePhotoEditorTests` unittestified at Phase 6 run, still green)

### Phase Summary

Phase 2 promotes the cropper to a shared piece of UI. `NovaCropperComponent` moved from
`Nova.UI/Features/Account/Components/` to `Nova.UI/Shared/` with the namespace updated to
`Nova.UI.Shared`; the single usage in `ProfilePhotoEditor.razor` was updated. A shared
export helper was added as `Nova.UI/Shared/CropperCanvasExport.cs`: the
`ICropperCanvasExporter` interface + `CropperCanvasExporter` implementation (max dimension
1024, white `#ffffff` `FillColor`, `ImageSmoothingQuality = "high"`, `image/jpeg` at 0.9f
quality, chunked `GetCroppedCanvasDataInBackgroundAsync` + stream-to-bytes) and a static
`CropperCanvasExport.ExportAsync` entry point for call sites that don't need substitution.
`ProfilePhotoEditor.SavePhotoAsync` was refactored onto the shared exporter
(behavior-identical). The interface exists so component tests can substitute the export
without a browser. Verified the free-form `Options` surface: no `AspectRatio` (free-form),
`ViewMode = Vm1`, `AutoCrop = true`, `AutoCropArea = 1m`.

## Phase 3: `ClubCrestManager` crop flow + preview aspect ratio

Status: Complete

Suggested executor: orchestrator (component behavior + tests are tightly coupled)

- [x] `ClubCrestManager.razor` / `.razor.cs`:
  - After `OnCrestSelectedAsync` validates the file, enter a crop step instead of the plain
    preview: render `NovaCropperComponent` (free-form, full image preselected) fed by the
    data URL; replace the `img.club-crest-preview` block for the in-progress selection.
  - Add buttons: **Save crest**, **Choose a different image** (reset selection), keep
    existing **Cancel** semantics consistent (`ClearSelection`).
  - On save: export the cropped canvas via the Phase 2 helper, then upload the JPEG bytes
    through `IClubCrestService.ChangeClubCrestAsync` with content type `image/jpeg`
    (server sniff validates it — JPEG is in `AllowedContentTypes`).
  - Handle export/empty-content errors with the existing `_crestErrors` pattern.
  - `CanSubmit` and the existing remove/change/forbidden flows keep working; after a
    successful change, clear crop state and set `CrestPresent = true`.
- [x] `ClubCrestManager.razor.css`:
  - Change `.club-crest-preview` from a fixed `8rem × 8rem` box with `object-fit: contain`
    to natural aspect ratio: `max-width: 8rem; max-height: 8rem; width: auto; height: auto`
    (keep border/radius/background). The current-crest (medium variant) preview now shows
    uncropped.
  - The placeholder box may stay square.

### Verification Plan

- `dotnet build Nova.slnx` → succeeds. ✅ (0 errors)
- Browser check (see Phase 6 for committed tests; manual spot check now): admin page →
  pick a wide image → cropper appears with full image selected → adjust crop → Save →
  "Club crest updated." and preview renders at the cropped aspect ratio. ⚠️ Not run
  manually; covered by the committed CC2 (replace + remove, crop step) and CC3
  (non-square crop → aspect-preserving rendering) using the Aspire AppHost if available.
- Existing bUnit `ClubCrestManagerComponentTests` may fail until Phase 6 updates them;
  note which fail and fix in Phase 6 (do not leave the suite red). ✅ (rewritten in Phase
  6: 9 tests, all green)

### Phase Summary

Phase 3 adds the crop step to `ClubCrestManager`. After `OnCrestSelectedAsync` validates
the file, the component enters a crop step (`IsCropping` = `_crestFile is not null`):
`NovaCropperComponent` (free-form options — no `AspectRatio`, `ViewMode = Vm1`,
`AutoCrop = true`, `AutoCropArea = 1m`) renders fed by the data URL,
`img.club-crest-preview` is replaced for the in-progress selection, and **Save crest** /
**Choose a different image** buttons appear. `SaveCrestAsync` exports via the injected
`ICropperCanvasExporter` and uploads JPEG bytes as `image/jpeg` through
`IClubCrestService.ChangeClubCrestAsync`; export/empty-content failures surface through
the existing `_crestErrors` ("The cropped image could not be processed. Please try
again."). On success the crop state is cleared (`ClearSelection`), `CrestPresent = true`,
and "Club crest updated." is shown; the Forbidden path still redirects to access denied.
`CanSubmit` = validated file && no errors; remove/confirm/forbidden flows unchanged.
CSS: `.club-crest-preview` now uses `max-width: 8rem; max-height: 8rem; width: auto;
height: auto` (border/radius/background kept), showing the medium variant uncropped.

## Phase 4: `CreateClubForm` crop flow + preview aspect ratio

Status: Complete

Suggested executor: orchestrator (same cropper integration as Phase 3, second surface)

- [x] `CreateClubForm.razor` / `.razor.cs`:
  - Same crop step as Phase 3 after crest file selection (cropper with full image
    preselected, **Save crest**/**Choose a different image**).
  - On form submit, upload the cropped JPEG bytes (with `image/jpeg` content type) in
    `CreateClubInput.CrestContent`/`CrestContentType` instead of the raw file bytes.
  - Keep the "crest required" validation: a crop must be saved before submit (or treat an
    unsaved selection as an error prompting the user to save the crop).
- [x] `CreateClubForm.razor.css`: same `.club-crest-preview` natural-aspect change as
  Phase 3.

### Verification Plan

- `dotnet build Nova.slnx` → succeeds. ✅ (0 errors)
- Browser check: onboarding form → pick image → cropper appears → save crop → create
  club → nav avatar + club detail show the crest. ✅ (covered by committed CC1/CC3 —
  running only if the Aspire AppHost is available)
- Existing unit tests for create-club validation (`CreateClubInputValidationTests`,
  `ClubComponentsTests`) stay green; browser test CC1 may need the Phase 6 update. ✅
  (`ClubComponentsTests` updated for the crop step; 1812/1812 pass, includes
  `CreateClubForm_SendsCroppedJpegBytes_AfterCropStep`)

### Phase Summary

Phase 4 adds the same crop step to `CreateClubForm`. After crest file selection the
component enters `IsCropping`: `NovaCropperComponent` (same free-form options) renders fed
by the data URL with **Save crest**/**Choose a different image** buttons. `SaveCrestAsync`
exports via the injected `ICropperCanvasExporter`, stores the JPEG bytes in
`_croppedCrestContent`, nulls `_crestFile`, and rebuilds the preview URL as
`data:image/jpeg;base64,...`. On submit `HandleSubmitAsync` uploads
`_croppedCrestContent` in `CreateClubInput.CrestContent` with `CrestContentType =
"image/jpeg"`. The "crest required" validation is kept: the submit button is disabled
while `IsCropping`, and submitting without a saved crop produces "A crest image is
required." `ClearSelection` resets the crop state. `.club-crest-preview` got the same
natural-aspect CSS (`max-width: 8rem; max-height: 8rem; width: auto; height: auto`).

## Phase 5: Natural aspect ratio on `ClubDetail` (and any other rendering surface)

Status: Complete

Suggested executor: orchestrator (trivial CSS change; fold into Phase 3/4 pass if convenient)

- [x] `ClubDetail.razor.css` `.club-detail-crest`: drop the fixed `5rem × 5rem` box;
  render naturally with sane caps, e.g. `max-width: 8rem; max-height: 5rem; width: auto;
  height: auto` (keep border/radius/background/flex-shrink).
- [x] `NavMenu.razor` / `NavMenu.razor.css`: **no changes** — the avatar stays the fixed
  2rem circle (`object-fit: cover` on the 64px square small variant). Confirm nothing else
  renders the crest (grep `GetCrestUrl` / `club-crest-preview` / `club-detail-crest`). ✅
  (grep confirms only `ClubDetail`, `ClubCrestManager`, `CreateClubForm`, and the NavMenu
  render the crest; `NavMenu` unchanged — 2rem circle avatar on the 64px square small
  variant)

### Verification Plan

- Grep confirms the only fixed-crop crest surface left is `NavMenu`'s `.nav-avatar`. ✅
- `dotnet build Nova.slnx` → succeeds. ✅ (0 errors)

### Phase Summary

Phase 5 renders the crest in its natural aspect ratio on the club detail page.
`ClubDetail.razor.css` `.club-detail-crest` dropped the fixed `5rem × 5rem` box and now
uses `max-width: 8rem; max-height: 5rem; width: auto; height: auto` (border, radius,
background, and `flex-shrink: 0` kept). `NavMenu` is unchanged: the nav avatar keeps the
2rem circle rendered with `object-fit: cover` on the 64px square small variant. A grep of
`GetCrestUrl` / `club-crest-preview` / `club-detail-crest` / `nav-avatar` confirms the
only fixed-crop crest surface left is the NavMenu avatar; the admin manager preview and
the create-form preview show the medium variant uncropped.

## Phase 6: Test coverage

Status: Complete

Suggested executor: builder/delegated (tests are well-specified and mostly independent; can
run in one agent)

- [x] **Unit — `Nova.Unit.Tests`**:
  - New `Features/Photos/ImageVariantProcessorTests` (or fold into existing photo/crest
    test files): from a non-square source (use `TestImages` helpers / create a
    non-square JPEG):
    - `GenerateCrestVariants`: small decodes to 64×64; medium and large decode to the
      source's aspect ratio (within rounding) and neither dimension exceeds the bound.
    - `GenerateVariants` (profile photos) still produces squares (regression guard).
    ✅ (4 tests: crest small square, medium/large aspect-preserving with bounds, and
    `GenerateVariants` squares regression)
  - Update `ClubCrestManagerComponentTests`: file selection now shows the crop step.
    Stub/mock the cropper JS interop in bUnit where feasible (JSInterop setup for the
    Cropper.Blazor module) or — if the cropper cannot be driven reliably in bUnit —
    structure the component so the post-export upload path is testable (e.g. inject the
    export helper) and cover: save-with-crop uploads JPEG payload, choose-different
    resets, remove/forbidden flows unchanged. ✅ (rewrote the tests: the export helper is
    injected as `ICropperCanvasExporter` and substituted; `ICropperJsInterop` is
    registered as a bUnit service so `NovaCropperComponent` renders; 9 tests cover the
    crop step save/choose-different/reset plus the unchanged validation/remove/forbidden
    flows)
  - `ProfilePhotoEditorTests` remain green after the Phase 2 refactor. ✅
- [x] **Integration — `Nova.Integration.Tests`** (`ClubCrestHttpTests` or adjacent):
  - After a POST change-crest with a non-square image, GET `?size=medium` and
    `?size=large` return WebP whose decoded dimensions preserve the source aspect ratio;
    GET `?size=small` returns a 64×64 square. (Decode bytes with ImageSharp in the test.)
    ✅ (new `ChangeCrest_NonSquareSource_ServesAspectPreservingVariants` — 300×200 source:
    small = 64×64 WebP; medium/large WebP preserve 1.5 aspect within 0.02 tolerance, no
    dimension over the bound)
  - Existing club-create and crest HTTP tests stay green. ✅
- [x] **Browser — `Nova.Browser.Tests/ClubCrestBrowserTests.cs`**:
  - Update CC1/CC2 for the crop step: after `SetInputFilesAsync`, wait for the cropper
    (e.g. `img.cropper-*` or the component's root) and click the save button instead of
    asserting `img.club-crest-preview` immediately. ✅ (CC1/CC2 now wait for
    `div.club-crest-cropper-frame` + the "Save crest" button and click it before
    proceeding. The crop-step save is exercised in both; CC2's later remove step fails on
    a pre-existing `[PersistentState]` island bug — see Final Recap.)
  - Add a scenario (e.g. CC3): upload a non-square image, crop to a non-square region,
    save; then assert the club detail crest `img` renders with unequal natural dimensions
    (`naturalWidth != naturalHeight` via `EvaluateAsync`) while the NavMenu
    `img.nav-avatar` is still a square/circle. ✅ (CC3: 300×200 upload saved from the
    crop step; nav avatar natural size is 64×64; club detail crest natural aspect is
    1.5 ± 0.02 with width != height)
- [x] Keep the full unit + integration + browser suites green (browser suites run
  locally against the Aspire AppHost). ⚠️ Unit suite green (1812/1812). Integration ran
  against the AppHost: 368/372 pass with 4 pre-existing failures (proven identical on the
  clean baseline at `696088d` before these changes) — see Final Recap. Browser: CC1/CC3
  pass; **CC2 fails** at the remove step on a pre-existing `[PersistentState]` island bug
  (verified identical on the clean baseline at `696088d`; not caused by these changes) —
  see Final Recap. The CC2 failure is documented in the test, not hidden.

### Verification Plan

- `dotnet build Nova.slnx` → 0 errors. ✅ (Build succeeded; 0 errors. Warnings are
  pre-existing in unit test files.)
- `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → pass. ✅ (Passed:
  1812 total, 0 failed)
- `dotnet test --project Nova.Integration.Tests/Nova.Integration.Tests.csproj` against the
  running Aspire AppHost → pass. ⚠️ (Ran locally with the AppHost up: 368/372 pass. The 4
  failures are pre-existing —
  `CreateClub_WithoutCrest_ReturnsValidationProblem`,
  `CreateClub_WithCrest_PersistsRowAndServesVariants`,
  `ChangeCrest_WithoutFile_ReturnsValidationProblem`, and
  `TraceCorrelationHttpTests.MalformedJson_ReturnsTraceIdMatchingSentTraceparent` —
  each of which is a stale-data / `ToServiceProblemAsync` kind-mapping / multipart-vs-JSON
  issue proven identical on the clean baseline at
  `696088d` (main, without these changes). See Final Recap.)
- Browser: `dotnet test --project Nova.Browser.Tests/Nova.Browser.Tests.csproj` against
  the Aspire AppHost → CC1–CC3 (and full suite) pass. Requires
  `Nova.Browser.Tests\bin\Debug\net10.0\playwright.ps1 install chromium` once per machine.
  ⚠️ (Ran locally against the AppHost: **CC1 and CC3 pass**; **CC2 fails** at the remove step
  on a pre-existing `[PersistentState]` island bug — see Final Recap. The full suite was
  not run because the AppHost is per-fixture and CC2's failure is deterministic.)
- `dotnet format Nova.slnx --verify-no-changes` → clean. ✅ (clean — no changes needed)
- No `scss/` changes expected → `npm run check:contrast` not required (run only if
  `scss/` or `package.json` touched). ✅ (no scss/package.json changes)

### Phase Summary

Phase 6 adds and updates test coverage. **Unit**: new
`Nova.Unit.Tests/Features/Photos/ImageVariantProcessorTests.cs` — 4 tests: crest small
decodes 64×64, medium/large preserve the source aspect (300×200 source) with no dimension
over the bound, and `GenerateVariants` still squares (regression). Rewrote
`Nova.Unit.Tests/Features/Clubs/ClubCrestManagerComponentTests.cs` — 9 tests: the crop
step appears after selection with a free-form cropper; save-with-crop uploads the bytes
from the injected `ICropperCanvasExporter` as `image/jpeg`; choose-different clears the
selection; validation (`_crestErrors`), remove/confirm, and forbidden flows are unchanged.
Added `CreateClubForm_SendsCroppedJpegBytes_AfterCropStep` to `ClubComponentsTests` and
updated the two create-club component tests to save the crop before submit; bUnit now
registers `ICropperJsInterop` (for `CropperComponent`'s injected interop) and
`ICropperCanvasExporter` substitutes. `ProfilePhotoEditorTests` are untouched and green.
**Integration**: `ChangeCrest_NonSquareSource_ServesAspectPreservingVariants` in
`ClubCrestHttpTests` (POST change-crest with a 300×200 JPEG → GET small = 64×64 WebP,
medium/large = aspect-preserving WebP within a 0.02 aspect tolerance, decoded with
ImageSharp). **Browser**: `ClubCrestBrowserTests` CC1/CC2 updated to drive the crop step
(wait for `div.club-crest-cropper-frame` + "Save crest", click save), and new CC3 asserts
a 300×200 source uploaded through the crop step renders the club-detail crest with
natural width != height (aspect 1.5) while the nav avatar stays a 64×64 square/circle.
CC1 and CC3 pass against the local AppHost; CC2's crop-step replacement passes but its
remove step fails on the pre-existing `[PersistentState]` island bug described in the
Final Recap.

### Verification result

Build: ✅ `dotnet build Nova.slnx` → 0 errors.
Unit: ✅ `dotnet test --project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → 1812/1812 pass.
Format: ✅ `dotnet format Nova.slnx --verify-no-changes` → clean.
Integration: ✅ ran locally against the Aspire AppHost — 368/372 pass; the 4 failures are
pre-existing on the clean baseline at `696088d` (stale seeding data, `ToServiceProblemAsync`
kind-mapping, and a stale JSON-body trace test), not caused by these changes; the new
aspect-preserving test passes.
Browser: ⚠️ ran locally against the Aspire AppHost — **CC1 and CC3 pass**; **CC2 fails** at
the remove step on a pre-existing `[PersistentState]` island bug (see Final Recap). The full
browser suite was not run; the CC tests were run individually (each starts its own AppHost
fixture).

## Final Recap

All 6 phases are implemented.

**What was delivered:**

1. **Server (Phase 1)** — `ImageVariantProcessor` gained `GenerateCrestVariants(...)`:
   `Original` (sanitized re-encode, unchanged), `Small` (64px center-cropped square WebP
   for the NavMenu avatar), `Medium` (WebP fit within 256×256, `ResizeMode.Max`), `Large`
   (WebP fit within 1024×1024, `ResizeMode.Max`). Shared sanitize/`EncodeOriginal` logic
   refactored into private helpers (`DecodeSanitized`, `EncodeFittedVariant`) so profile
   photos (`GenerateVariants`, still square) and crests don't diverge. Both crest call
   sites (`ClubCrestService.ChangeClubCrestAsync`, `ClubService.CreateClubAsync`) switch
   to it. Wording updated in `ClubEndpointRouteBuilderExtensions` + service XML docs. No
   migration; no endpoint/blob-name contract change.
2. **Shared UI (Phase 2)** — `NovaCropperComponent` promoted to `Nova.UI/Shared/`
   (namespace `Nova.UI.Shared`); shared `CropperCanvasExport` helper added
   (`ICropperCanvasExporter`/`CropperCanvasExporter` + static entry point; max dimension
   1024, white background, high smoothing, JPEG 0.9). `ProfilePhotoEditor.SavePhotoAsync`
   refactored onto it (behavior-identical). Free-form options verified against
   `Cropper.Blazor.Models.Options` (no `AspectRatio`, `ViewMode = Vm1`, `AutoCrop = true`,
   `AutoCropArea = 1`).
3. **ClubCrestManager (Phase 3)** — file selection now enters a crop step: free-form
   cropper rendered from the data URL, **Save crest**/**Choose a different image** buttons;
   save exports via the injected exporter and uploads `image/jpeg` bytes through
   `ChangeClubCrestAsync`; errors go through `_crestErrors`; on success crop state clears
   and `CrestPresent = true`; remove/forbidden flows unchanged. Preview CSS → natural
   aspect (`max-width: 8rem; max-height: 8rem; width: auto; height: auto`).
4. **CreateClubForm (Phase 4)** — same crop step after selection; submit uploads the
   saved JPEG bytes as `CreateClubInput.CrestContent`/`CrestContentType = image/jpeg`;
   "crest required" gating kept (submit disabled while cropping; "A crest image is
   required." otherwise). Same natural-aspect preview CSS.
5. **Rendering (Phase 5)** — `ClubDetail.razor.css` `.club-detail-crest` natural aspect
   (`max-width: 8rem; max-height: 5rem; width: auto; height: auto`); `NavMenu` unchanged
   (2rem circle avatar on the 64px square small variant). Grep confirms no other
   fixed-crop crest surface remains.
6. **Tests (Phase 6)** — See the Phase 6 summary for the unit/integration/browser
   coverage added and updated. ⚠️ One browser test (CC2) fails on a **pre-existing**
   `[PersistentState]` island bug (proven identical at `696088d`); it is documented in the
   test and here rather than hidden.

**Validation:** `dotnet build Nova.slnx` → 0 errors; `dotnet test
--project Nova.Unit.Tests/Nova.Unit.Tests.csproj` → 1812/1812 pass; `dotnet format
Nova.slnx --verify-no-changes` → clean. Integration (`Nova.Integration.Tests`) and
browser (`Nova.Browser.Tests`) suites require the Aspire AppHost (Postgres + Azurite)
and were **executed locally** against it:

- **Integration**: 368/372 pass. The 4 failures are **pre-existing** on the clean baseline
  at `696088d` (main, without these changes) and are unrelated to crest cropping:
  - `CreateClub_WithoutCrest_ReturnsValidationProblem` and
    `ChangeCrest_WithoutFile_ReturnsValidationProblem` — assert a `Validation`-kind
    `ServiceProblem` for a 400 response with an empty body, but
    `ToServiceProblemAsync` maps a 400-without-`errors` to `BadRequest`, so the tests
    fail on the kind mismatch.
  - `CreateClub_WithCrest_PersistsRowAndServesVariants` — asserts blob prefixes
    `clubs/{clubId}/...`, but blobs are stored under `clubs/{userId}/{batchId}/...`, so
    the prefix check fails (stale-data/ID-sequence dependent).
  - `TraceCorrelationHttpTests.MalformedJson_ReturnsTraceIdMatchingSentTraceparent` —
    sends `application/json` to `ClubEndpoints.Create`, which (since `696088d`) is a
    multipart-form `[FromForm]` endpoint, so the server returns 415 instead of the
    expected 400 problem+json. Pre-existing; unrelated to this change.
  - The new `ChangeCrest_NonSquareSource_ServesAspectPreservingVariants` passes.
- **Browser**: The CC tests were run individually (each starts its own AppHost fixture;
  the full suite was not run as one batch). **CC1 and CC3 pass.** **CC2 fails** at the
  remove step on a **pre-existing app-level bug** — confirmed against the clean baseline
  at `696088d`, where the unmodified CC2 test fails identically (at the same island-state
  root cause, manifesting slightly earlier in that run because the baseline test does not
  use the WASM reload). The bug: `ClubCrestManager`'s `[PersistentState]`
  `HasCrestInitialized`/`CrestPresent` capture the crest presence on the island's *first*
  render pass — when the host page's `_summary` has not loaded yet, so
  `HasCrest` is `false` — and `OnInitialized`'s guard (`if (!HasCrestInitialized)`)
  prevents a later recapture once the summary arrives (`HasCrest = true`). The state is
  then persisted across prerender → interactive attach, so the WASM island keeps
  rendering "no crest" (no `Remove crest` button) even though the nav (claim-driven)
  shows the crest and the crest blob returns 200. The `parameterValues` in the SSR
  descriptor is `[1,true]` (correct) while the SSR-rendered section shows the placeholder
  — a first-pass capture. Fixing this is **out of scope** for the crest-cropping change
  (the persistent-state fields and `OnInitialized` are byte-identical to HEAD; the crop
  feature only touches file selection/save). CC2's failure point is documented in the
  test and Final Recap: it blocks the remove portion of CC2 but does not affect CC1/CC3
  or the new crop-step coverage.

**Accepted scope details:** existing crests stored as square variants keep rendering
squares until re-uploaded (no backfill/reprocess); existing crest blobs are never
rewritten by a GET. Clients that upload now always send cropped JPEG (white background)
from the shared exporter; crests uploaded via the raw HTTP API still go through the same
server-side `GenerateCrestVariants`.

**Review remediation (PR #143, after review):** two review findings were addressed:
- *Low (Possible)* — "Save crest" could be clicked before the Cropper.js instance was
  ready (async JS boot), causing the export to fail with a misleading error. Fixed by
  adding an `OnReady` ready-signal to the shared `NovaCropperComponent` (wired from
  Cropper.Blazor's `OnReadyEvent` JS-invokable) and gating `CanSubmit` on `_cropperReady`
  in both `ClubCrestManager` and `CreateClubForm`; unit tests use the internal
  `SimulateReady()` escape hatch and new gating tests were added for both components.
- *Nit (Verified)* — `_cropperOptions` duplicated byte-for-byte across the two club
  components. Fixed by extracting a shared `CropperOptionsFactory.CreateCrestOptions()`
  in `Nova.UI/Shared/`. The `.club-crest-preview`/`.club-crest-cropper-frame` CSS remains
  knowingly duplicated: Razor CSS isolation scopes each `*.razor.css` to its component and
  `@import` is not affected by scoping, so sharing the rules would leak them globally or
  break the `::deep` selector; both files carry cross-reference comments. The reviewer
  sanctioned "accept the duplication knowingly". `ProfilePhotoEditor` was left untouched
  (pre-existing pattern, out of scope).
- The browser tests now also wait for the Save crest button to be enabled (ready gate)
  before clicking; CC2's documented pre-existing limitation is unchanged.

## Deployment Plan

1. **Deploy the new build** (`Nova` web app). No database migration: `ClubCrestEntity`
   columns and blob names are unchanged; only the image processing behavior and the
   rendered CSS changed.
2. **No configuration or environment variable changes** are required. The
   `ICropperCanvasExporter` service is registered scoped in both the server
   (`Nova/Program.cs`) and WASM client (`Nova.Client/Program.cs`) `Program.cs` files, so
   it resolves on server circuits and in WASM without extra config.
3. **No static asset changes** beyond the existing `_content` scoped CSS (component
   razor.css bundles ship with the app). No `scss/` rebuild or contrast check needed.
4. **Existing crests remain square** until a club admin re-uploads (accepted behavior —
   no backfill/reprocess). After re-upload, the crest renders at its natural aspect ratio
   in the club detail header and the admin/create-form previews; the NavMenu avatar
   stays the 64px square circle.
5. **Rollback**: revert the deployable build only — no data or config to roll back.
   Newly uploaded crests will have been stored with aspect-preserving medium/large
   variants and will keep rendering naturally even under the old code (the old GET code
   serves whatever blobs exist), so a rollback is safe for existing blobs.
