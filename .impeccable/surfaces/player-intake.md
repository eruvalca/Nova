# Player intake

Status: Confirmed

Issues: [#168](https://github.com/eruvalca/Nova/issues/168), child of [#163](https://github.com/eruvalca/Nova/issues/163)

Visitor mode: Operate

## Job and audience

Nova must give approved club staff a dependable way to turn player information into a usable club roster without returning to spreadsheet reconciliation. Every approved club member may manually add, correct, archive, and restore player records. Club administrators additionally own bulk CSV intake because a bulk commitment changes the shared roster at operational scale. Players, parents, and guardians do not use Nova and receive no registration, invitation, review, or correction surface.

Manual intake serves the staff member handling a walk-up addition or correcting a small number of records, often from a phone or while other campaign work is already underway. Bulk intake serves an administrator bringing a prepared tryout spreadsheet from a desktop. The first-value moment is not merely a successful upload: it is seeing the spreadsheet become a searchable Players directory, with an exact account of which players entered the currently Active campaign or are ready for the next one.

## Outcome and proof

- The Players area offers two truthful entry paths: a focused manual workflow for every approved club member and an administrator-only CSV workflow for bulk intake. Import never moves into the Club area.
- Manual creation and bulk commit produce the same durable player record and the same lifecycle consequence. A player created while a campaign is Active is enrolled in that campaign with an Undecided placement outcome; a player created when no campaign is Active remains an active club player and joins the roster when the next campaign opens.
- The CSV workflow is a complete route: **Template → Upload → Review → Finish**. Review distinguishes valid, invalid, and duplicate rows before any commitment and supports committing valid rows without hiding the skipped work.
- Success is proved with concrete counts and destinations: players created, rows skipped, duplicates blocked, players enrolled in the Active campaign, and players left ready for a future campaign.
- Intake remains fast and comprehensible from one row through the 1,000-row limit. Upload, validation, review, and commit must not freeze the interface, render an unbounded table, or slow unrelated application work through per-row request or query patterns.

## Selected direction

The Players area is the club's enrollment desk: one flat operational directory with visible entry routes, not a CRUD card collection. The empty roster teaches through real work. Administrators see **Import player list** as the primary first-roster action and **Add one player** as the supporting action; other members see manual addition as their available path without a disabled or leaked bulk-import control. Once players exist, manual add and administrator import remain discoverable directory actions without competing with search and roster scanning.

Manual intake uses a dedicated, URL-backed working board inside the Players area rather than a modal or an expanding form buried in the directory. It supports consecutive walk-up additions while preserving a stable destination for campaign-readiness links. Bulk intake uses a sequential route because each commitment depends on the previous artifact, but it never disguises errors or forces administrators through explanatory ceremony. The uploaded spreadsheet remains the source of truth: Nova previews and diagnoses it but does not become an inline spreadsheet editor.

Fieldhouse Wayfinding supplies the interaction language. Connected, fully labeled route stops orient the import; a bounded field sheet holds manual entry; compact roster rows and precise status language hold review. Wayfinding Teal marks the current route and primary commitment. Signal Amber identifies rows requiring attention, Copper identifies blocking errors, and every state is written in words and counts rather than color alone.

## Authority and lifecycle contract

- Every approved club member may view the Players directory and player records, manually create a player, correct an active player's profile fields, archive a player, and restore an archived player.
- Only club administrators may download the import template, upload a bulk file, review its contents, or commit a bulk import. Import routes and counts are absent for members rather than rendered as unusable controls.
- Authorization is enforced at every destination and mutation. A role change during a manual or bulk flow reconciles to the new authority without committing hidden work.
- Archive and restore remain explicit lifecycle actions. Archiving preserves campaign history and must present any blockers established by the player-lifecycle domain. Restoring reactivates the club record but does not backfill campaigns missed while the player was archived.
- Intake never creates a player, parent, or guardian account. It never sends invitations or asks staff to supply contact credentials.

## Manual intake

The manual route is a focused player-entry board with the permanent profile fields: required first name, required last name, required date of birth, required graduation year, optional gender, optional jersey number, and optional player photo. The interface identifies required and optional fields directly, preserves valid input across validation or network failure, and keeps server-side field errors next to their owning controls.

Photo is optional and must not block the roster from becoming useful. Staff may add it during the manual flow when the supporting photo capability is available or from the created player's record afterward. A failed photo upload does not roll back an otherwise successful player creation; the completion state names the missing photo and offers a local retry.

Before commitment, the board states the authoritative enrollment consequence in plain language:

- With an Active campaign: this player will join **{Campaign name}** immediately.
- With no Active campaign: this player will remain in the club directory and join the roster when the next campaign opens.

Submission prevents duplicate actions and reports the authoritative result rather than relying on the earlier preview. On success, the working board becomes a compact receipt naming the player and enrollment result, then offers **Add another**, **View player**, and **Return to players**. **Add another** resets only player-specific fields and returns focus to the first field; it does not replay a prior photo or retain stale errors. The Players directory preserves a success message when the staff member returns.

Manual intake supports pristine, invalid, checking, saving, saved, photo-pending, recoverable failure, ambiguous-result recovery, possible duplicate, role-changed, Active-campaign-changed, and cancellation states. A possible duplicate is never silently merged or overwritten; staff return to the directory or existing record to resolve it.

## Bulk import route

### 1. Template

The administrator begins from the Players directory and downloads Nova's current CSV template. The template contains these columns and no hidden required metadata:

1. First name — required.
2. Last name — required.
3. Date of birth — required, with one documented date format.
4. Gender — optional, with the accepted values shown in the template guidance.
5. Jersey number — optional.
6. Graduation year — required.

Player photos are excluded because a CSV cannot carry the image safely or reviewably. Photos remain an optional follow-up on individual player records. The template identifies required fields, accepted formats, the 1,000-row upload limit, and the fact that the uploaded file remains the correction source.

### 2. Upload

The upload step accepts the documented CSV file type through a conventional file picker and an equivalent drop target where supported. It names the chosen file, its size, and detected row count before validation. Replacing or removing the file is explicit. Upload and parsing progress remain perceivable, cancellation is available before commitment, and the interface never appears frozen while a larger file is processed.

Files with missing required headers, incompatible encoding, no data rows, malformed CSV structure, unsupported type, or more than 1,000 data rows stop here with a specific correction and retry path. A rejected file creates no players. Nova does not guess column mappings or accept a structurally different spreadsheet through a hidden best-effort parser; administrators correct or export against the current template.

### 3. Review

Review is read-only and begins with an exact reconciliation:

- total uploaded rows;
- rows ready to import;
- rows with blocking validation errors;
- rows blocked as duplicates;
- the current Active campaign and projected enrollment count, or the no-Active-campaign consequence.

Each row retains its source row number. Field-level messages identify the exact column and correction, and filters move among **Ready**, **Needs correction**, and **Duplicate** without losing the overall counts. Large previews are paged or otherwise windowed so the browser never renders all rows at once. Keyboard users can reach the summary, filters, error list, row details, and commitment without traversing hundreds of off-screen records.

Nova blocks a duplicate when normalized first name, normalized last name, and date of birth match an existing player in the club or an earlier row in the same file. The preview identifies whether the match is an existing active player, an archived player, or another uploaded row and links to the existing record when one exists. Import never updates, restores, merges, or overwrites a matched record automatically.

Invalid and duplicate rows are not eligible for commitment. Administrators correct the source spreadsheet and upload it again rather than editing cells inside Nova. They may download a row-numbered error CSV containing the original row values and validation messages to support that correction. Uploading a corrected file creates a fresh preview; it does not append invisibly to the prior candidate set.

If no rows are ready, there is no commitment action. If some rows are ready, the primary action states the exact effect: **Import {N} valid players**. Nearby text states how many rows will remain unimported. The administrator can return to replace the file without losing the diagnosis until they explicitly do so.

### 4. Commit and finish

Commit revalidates authority, duplicates, player fields, roster state, and the Active campaign against current server truth. All still-valid eligible rows are committed as one idempotent batch operation; rows that became blocked after preview are skipped and reported rather than overwritten. The client never creates a bulk roster through one request per player.

The commitment prevents duplicate submission and presents continuing status without requiring the administrator to keep a control focused or repeatedly retry. An interrupted or ambiguous request verifies the authoritative result before offering retry. Retrying the same logical import cannot create a second copy of a player that the earlier attempt already committed.

Finish is a durable receipt, not a transient toast. It reports:

- players created;
- players enrolled in the named Active campaign, when applicable;
- players ready for the next campaign when none is Active;
- invalid rows skipped;
- duplicate rows skipped;
- rows that became blocked during final revalidation;
- whether an error file remains available.

The administrator may **View imported players**, **Import corrected rows**, or **Return to players**. The Players directory carries the same compact reconciliation after return. A partial success is named plainly: committed players remain committed, skipped rows remain visible in the receipt, and retry operates only on a corrected or explicitly selected remainder rather than replaying the successful set.

## Performance contract

Performance is part of correctness for bulk intake. The experience must remain responsive while importing the maximum supported file and must not degrade normal directory, campaign, or evaluation work for other staff.

- Support 1–1,000 data rows per upload. Typical club imports are expected to contain roughly 20–200 rows; manual intake remains optimized for one or a few players.
- Acknowledge file selection immediately and expose continuing upload, validation, and commit status. Long work may continue asynchronously from the interaction, but it may not leave the administrator with an inert page or an unsafe second commitment path.
- Parse and validate the file in one bounded preview operation. Detect existing-player duplicates and validate shared facts with set-based reads; do not issue one network request or one independent database lookup per row.
- Commit through a bounded batch pipeline with set-based persistence and campaign enrollment behavior. Avoid an N+1 query pattern, repeated full-roster reloads, and a long chain of client-driven player mutations.
- Page or window preview rows and error results, retaining authoritative aggregate counts separately. Filtering and error navigation must not require loading or rendering the full 1,000-row set into the DOM.
- Bound uploaded bytes, parsed values, retained preview lifetime, and concurrent import work in addition to the row limit. Reject an oversized file before expensive parsing when possible and release abandoned preview state.
- Keep progress, cancellation, permission changes, timeout, service-busy, and retry states explicit. A throttled or temporarily unavailable import service must fail locally without making the rest of the Players directory unusable.
- Validation for issue #182 and the Players build must include representative 200-row and maximum 1,000-row files, duplicate-heavy and error-heavy files, idempotent retry, and evidence that query/request counts do not grow through a per-row N+1 pattern.

## First-use, empty, and return states

- **No players, administrator:** explain that imported or manually added players become the club's shared roster. Lead with **Import player list** and support with **Add one player** and template guidance. Do not show a product tour or sample roster disconnected from real work.
- **No players, member:** explain the shared roster and offer **Add player**. Do not mention a hidden administrator import capability as though the member is blocked from ordinary intake.
- **No search or filter results:** preserve the real roster count, identify the active filters, and offer to clear them. Do not reuse first-run copy or suggest importing duplicates.
- **All players archived:** distinguish the archived roster from a never-used club and provide the authorized restore path.
- **Returning after manual creation or import:** preserve the completion reconciliation until the staff member dismisses it or begins another intake action. The new players are searchable immediately.
- **Active campaign present:** name it wherever intake consequences are summarized and link to its Roster after success.
- **No Active campaign:** state that active club players will be enrolled when the next campaign opens; do not describe them as already enrolled or stranded.

## Interaction and layout

- The Players directory remains one broad flat field with entry actions, search/filter controls, roster scale, and compact directory rows. Intake does not introduce generic cards, ambient shadows, or a second dashboard.
- The import route preserves all four labels at every viewport. On narrow screens, the route may scroll horizontally, while the selected stop and focus ring remain visible. Review rows collapse into labeled records or remain in a deliberate responsive scroll region; neither field names nor errors disappear.
- Primary actions use exact consequences rather than generic **Continue** or **Submit** labels. Destructive archive actions use inline confirmation and name the player and historical consequence.
- Every target is at least 2.75rem. File selection has an accessible conventional input, status changes are announced, error summaries link to row or field details, and focus moves to the first error, the review heading, or the finish heading after transitions.
- Progress never depends on animation, and reduced-motion users receive the same state information. Color is always paired with a label, icon, count, or message.
- Navigation away from an uncommitted manual form or reviewed import warns about losing the local work. Navigation after a committed or partially committed import never implies that successful rows can be rolled back from the wizard.

## States and ranges

The design covers one manual player, repeated manual additions, empty and populated clubs, a typical 20–200-row import, and a maximum 1,000-row import. It also covers clean files, mixed valid/error files, duplicate-only files, all-invalid files, corrected re-uploads, active and archived duplicates, a campaign opening or closing between preview and commit, role loss, connectivity loss, timeout, service throttling, cancellation, ambiguous commit recovery, partial success, and safe idempotent retry.

Player and result collections remain bounded or paged with explicit total counts. Intake cannot assume short names, a single gender value, sequential jersey numbers, one graduation year, or that every player has a photo. Date, enum, and number parsing follows documented, locale-safe template rules rather than browser display conventions.

## Scope and boundaries

This brief covers the Players-directory intake entry points, the manual player-creation flow, the administrator CSV wizard, immediate player-record handoff, archive/restore authority, first-roster onboarding, auto-enrollment feedback, and production-ready responsive/error/performance behavior. It defines design and implementation consequences but writes no production code or visual comp.

Explicit anti-goals:

- No player, parent, or guardian account, invitation, signup, approval, or self-service correction flow.
- No CSV import in the Club area and no bulk-import capability for non-administrator members.
- No photos, image URLs, team assignments, evaluation notes, tags, placement outcomes, or campaign selection columns in the CSV template.
- No manual campaign-roster picker. Active player intake follows the authoritative Active campaign automatically.
- No inline spreadsheet editor, guessed column mapping, silent coercion, silent duplicate merge, overwrite, or restore.
- No all-or-nothing requirement that forces clean rows to wait for unrelated bad rows; equally, no hidden commitment of rows the administrator did not review as eligible.
- No one-request-per-row client loop, unbounded preview table, decorative upload celebration, generic CRUD cards, or CSS-level visual specification.
- No redesign of evaluation, placement, campaign closeout, team composition, or final roster export. Their owning briefs define those workflows.

## Constraints and implementation consequences

- Preserve Fieldhouse Wayfinding, SSR-first Blazor architecture, club-scoped authorization, semantic state language, keyboard access, touch-safe behavior, bounded data, and meaningful loading/error feedback.
- Existing Players and PlayerForm screens are behavioral evidence only. Their inline Bootstrap CRUD form and `card shadow-sm` composition are not design authority.
- Issue [#182](https://github.com/eruvalca/Nova/issues/182) owns the template, preview, commit, idempotency, validation, and import-performance backend pipeline. Issue [#180](https://github.com/eruvalca/Nova/issues/180) owns the production Players surfaces and wizard.
- Current player-management and lifecycle services restrict manual mutations to administrators. The build must expand manual create, edit, archive, and restore authorization to every approved club member while preserving administrator-only bulk endpoints; hiding controls alone is insufficient.
- Current manual creation already uses a retry-safe creation operation and auto-enrolls against Active campaigns. Bulk import must preserve the same authoritative lifecycle result without invoking that service once per row.
- The domain contains player-photo persistence, but current player inputs and forms do not expose an end-to-end photo workflow. Builders must implement or separately track the optional photo handoff rather than pretending CSV supplies it or making roster creation depend on it.
- Preserve tenant integrity, lifecycle mutation ordering, transaction safety, idempotent ambiguous-commit recovery, and explicit total counts. Preview is advisory; the locked commit owns the final eligible set and enrollment result.
- Performance validation is a release condition for the import pipeline, not a post-build optimization pass.

## Decision record

- Permit every approved club member to manually add, edit, archive, and restore players; reserve CSV template, preview, and commit for club administrators.
- Keep players, parents, and guardians entirely outside the application for this phase.
- Use a dedicated URL-backed manual intake board with **Add another**, **View player**, and **Return to players** completion paths.
- Make photo optional, exclude it from CSV, and allow player creation to succeed independently of photo upload.
- Use a read-only **Template → Upload → Review → Finish** import route; corrections happen in the source spreadsheet and return through upload.
- Allow administrators to commit reviewed valid rows while invalid and duplicate rows remain skipped and retryable.
- Block normalized first-name + last-name + date-of-birth duplicates within the club or upload; never merge or overwrite automatically.
- Limit each upload to 1,000 data rows, page or window large previews, and require set-based preview and commit pipelines with no per-row client/server loop or N+1 data access.
- Treat the finish state as an exact reconciliation and make partial success durable, explicit, and idempotently retryable.
- Apply auto-enrollment from authoritative campaign state at commitment: Active campaign players join immediately; otherwise they wait for the next campaign opening.
