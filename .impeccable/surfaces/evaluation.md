# Evaluation flow

Status: Confirmed

Issues: [#165](https://github.com/eruvalca/Nova/issues/165), child of [#163](https://github.com/eruvalca/Nova/issues/163)

Visitor mode: Operate

## Job and audience

Nova must let any approved club member capture a trustworthy observation in the few seconds available when a player appears on the field. The evaluator is commonly standing or walking with a phone in one hand, looking up more often than looking at the screen, and may encounter players in no planned order. Evaluation is therefore a **find-player, verify, capture, move-on** loop rather than a queue to complete.

The same surface also serves a staff member reviewing shared observations at a desk. Every member may find players, add notes, apply existing trait tags, and create a missing trait tag inline. Notes and tag applications are shared within the club and retain author attribution. Evaluation may inform placement, but it does not require a placement decision and does not manufacture an “evaluated” completion state from the presence of notes or tags.

The activation moment is finding the randomly encountered player quickly, verifying the identity from field-recognizable context, and committing a note or tag without losing sight of the session.

## Outcome and proof

- The Evaluate destination opens on one prominent lookup action that searches the current campaign roster by player name or tryout number. It does not ask the evaluator to choose or resume a prescribed order.
- Search results make mistaken identity difficult: tryout number leads when present, followed by name, graduation year, and current placement outcome or team. An exact match still requires one deliberate tap before capture opens.
- A selected player exposes quick tag application, a compact note composer, and shared history while preserving enough identity context to prevent observations landing on the wrong player.
- Saving is explicit. **Save note** commits the observation; **Find another player** returns focus to a cleared lookup. Previous and Next remain optional conveniences within the current result sequence, never workflow requirements.
- Inline tag creation is a create-and-apply action with case- and whitespace-insensitive deduplication. Evaluators enter only the trait label; administrative presentation and lifecycle controls stay out of the field workflow.
- The flow proves its product specificity through campaign-scoped lookup, visible tryout numbers, shared club observations, placement context, close-time freezing, and a phone-first capture loop—not a generic profile drawer or ratings form.

## Selected direction

The Evaluate stop is a field-side lookup station within the campaign route. Its first view is not a roster dashboard or progress summary; it is a fast finder with enough recent search context to recover from interruption. Selecting a result opens a dedicated player working sheet. On a phone, lookup/results and the player sheet occupy one focused stage at a time. On wider screens they may become adjacent finder and working fields, provided the player identity and primary capture action retain clear priority.

Fieldhouse Wayfinding supplies the visual authority: one flat working board, legible route context, precise hairlines, restrained status color, and written state. The player sheet behaves like an operational field record rather than a modal card. Trait tags are compact semantic labels; the note composer is a deliberate commitment surface. No score, rating, rubric, completion meter, or ornamental sports treatment appears.

## Evaluation route and entry

- Evaluate remains one URL-backed Campaign Route Marker beside Roster, Place, and Close. Evaluation and placement may overlap while the campaign is Active; the marker does not claim completion based on visits, note counts, or tag counts.
- The default entry is a blank, focused lookup prompt such as **Find by name or tryout number**. Search must work well with a phone keyboard and accept either text or digits without changing modes.
- The surface does not persist or promote a “last player evaluated” resume action in this phase. A browser back/forward operation or responsive reflow may preserve current in-session lookup and selection, but Nova does not treat the last opened player as progress.
- Search is campaign-scoped and case-insensitive. Name matching supports first name, last name, and full name. A numeric term matches tryout numbers; exact number matches rank ahead of partial name matches but never open automatically.
- Results are bounded and report their count. Typical phone use should expose the best matches without an unbounded roster download; large campaign rosters remain paged or progressively fetched with an explicit continuation.
- Each result provides one whole-row touch target and enough disambiguation to verify the player: tryout number when present, full name, graduation year, and current outcome/team. Missing tryout numbers are stated or omitted cleanly rather than rendered as a misleading zero.
- Zero results keep the term, explain that no player in this campaign matches it, and offer a one-action clear/retry path. A player absent from the campaign is not silently pulled in from the club directory.

## Player working sheet

The selected-player state keeps the campaign and player unmistakable while making capture immediate.

- The identity header leads with tryout number when present and full name, followed by graduation year and current written placement outcome/team. Placement context is informative; it does not dominate or imply that placement is required before leaving.
- **Back to results** returns to the same result set and scroll position. **Find another player** clears the prior term, returns to lookup, and places focus in the search field. Both are distinct from closing or leaving the Evaluate route.
- Previous and Next move only within the currently loaded search or roster sequence and state the player's position when meaningful. They are labeled buttons with touch-safe targets, not swipe-only gestures. At a boundary they become unavailable without dropping focus or changing the selected player.
- Changing players while a note draft contains unsaved text requires an explicit keep-working or discard choice. A pending mutation prevents player movement until its authoritative outcome is known.
- Selected-player state may be represented in the URL with a stable campaign-assignment identifier so refresh, placement handoff, and browser history restore the correct player. Authorization and campaign membership are rechecked on every load; a stale or foreign identifier reveals no player data.

## Fast note capture

- The player sheet provides an immediately available compact note composer rather than hiding it behind an **Add note** disclosure. The field supports short on-field observations and the existing maximum of 4,000 characters without encouraging long-form entry.
- **Save note** is the primary commitment. Success leaves the evaluator on the verified player, announces that the note was saved, clears only the committed draft, and exposes **Find another player** prominently. The flow does not auto-advance to an arbitrary roster neighbor.
- Explicit save is preferred over background autosave because notes are shared, author-attributed records. Submission prevents duplicates, keeps status perceivable, and verifies ambiguous outcomes before offering retry.
- Validation stays beside the composer and preserves the draft. Network or server failure preserves the text and offers retry. Navigation, rotation, prerender attach, or an unrelated shared-history refresh must not erase an in-progress draft.
- Shared notes appear newest first beneath quick capture, with author display name, created time, and an edited indicator. The current member may edit or delete only their own note. Editing is explicit and retains the same 4,000-character validation; deletion uses an inline consequence statement rather than a detached modal.
- Other members' notes remain readable before and after the evaluator writes. The interface does not hide them to simulate independent scoring, because Nova's confirmed model is shared collaboration rather than blinded evaluation.

## Fast tag capture and inline creation

- Applied tags are visible near the top of the working sheet so the evaluator can recognize existing shared traits before adding another. A compact treatment may defer actor and timestamp details, but those details remain available and accessible.
- One typeahead control searches every active club tag while the evaluator types. Selecting an existing suggestion applies it in one action; a duplicate already applied to this player is unavailable and explained rather than submitted again.
- When no active tag matches, the evaluator may choose **Create and apply “{label}”**. Creation and application behave as one user commitment: success leaves one club-wide definition and one application on the selected player; partial or ambiguous failure is reconciled before retry so neither record duplicates.
- Names are trimmed, internal whitespace runs collapse to one space, and deduplication is case-insensitive. The first creator's casing becomes the display casing. Punctuation is not silently rewritten or treated as equivalent; similar names are suggested before creation so the member can choose deliberately.
- Trait labels retain the existing 100-character maximum, while prompt copy and examples encourage concise observations such as “tall,” “strong,” or “good awareness.” Blank or whitespace-only labels are invalid.
- An archived-name match cannot be recreated under different casing or spacing. Explain that an administrator must restore it; do not expose restore or archive controls inline.
- Evaluators do not choose a color during inline creation. The system assigns the product's centralized, accessible collaborative tag treatment; color carries no trait meaning. Administrator tag management may later change presentation, rename, archive, or restore definitions without becoming part of this capture flow.
- If the club has reached the complete active-tag limit, existing tags remain searchable and applicable while creation explains why it is unavailable and points administrators toward tag housekeeping. The evaluator never sees a silently truncated choice set.
- A member may remove their own tag application; a club administrator may remove any application as housekeeping. Definition rename, archive, and restore remain administrator-only. Archived applied tags remain visible in history with written archived state and cannot be newly applied.

## Placement handoff

- Current outcome and team appear as context on the player sheet. A member who is ready to decide receives a deliberate **Place player** action.
- **Place player** opens the campaign's Place route with the same campaign assignment preselected. It does not reveal or duplicate placement mutation controls inside Evaluate.
- Returning from Place restores the player working sheet with authoritative outcome/team data and the prior lookup context when still valid. The placement brief owns eligibility, team choices, outcome transitions, conflicts, and placement-specific confirmation.
- Evaluation remains useful without placement. Nova never marks a player incomplete, blocks **Find another player**, or prompts for a team merely because notes or tags were added.

## Collaboration, ownership, and freshness

- Notes are shared club-wide with author attribution. Authors edit and delete their own notes; no member, including an administrator, receives silent authorship-changing edit controls. Administrative note moderation is outside this brief.
- Tag applications retain actor and time attribution. Definition-level rename/archive/restore remains visibly separate from applying a trait to a player.
- Opening a player and returning from a mutation refreshes shared history and placement context. Nova does not claim real-time presence or live co-editing unless the implementation provides it.
- Concurrent additions are additive. A stale edit/delete, duplicate tag application, archived tag, permission change, or lifecycle change reconciles to the authoritative player state and keeps a local retry or recovery path next to the affected action.
- Feedback names the subject and result—such as **Note saved for #42 Jordan Lee**—so a delayed response cannot be mistaken for the next player.

## Campaign lifecycle and read-only history

- Evaluation mutations are available only while the campaign is Active. A Closed campaign preserves lookup, player identity, shared notes, tag applications, authorship, timestamps, and placement context as read-only history.
- Closing removes note add/edit/delete, tag apply/create/remove, and placement-handoff mutation affordances. The surface states **Read-only — campaign is closed** in words and does not rely on disabled controls alone.
- If the campaign closes while an evaluator is composing or submitting, the server's lifecycle result is authoritative. A note committed before the close appears in history; an uncommitted draft remains available to copy, but retry is not offered until the campaign is Active again.
- Reopening restores mutation capabilities after an authoritative refresh. It does not resurrect discarded drafts, invent progress, or change note and tag attribution.

## States and ranges

The design must account for:

- an empty campaign roster, one player, a typical tryout roster, and a campaign populated from the bounded 1,000-row intake path;
- blank lookup, typing/debouncing, loading, one match, many matches, zero matches, page/continuation, failed retrieval, and stale result selection;
- players with and without tryout numbers, teams, placements, notes, or tags; duplicate names must remain distinguishable;
- no active tag definitions, many active definitions up to the complete club limit, no match, similar match, exact active match, exact archived match, already-applied match, and limit reached;
- pristine, invalid, saving, saved, ambiguous, offline/recoverable failure, conflict, permission-changed, campaign-closed, and campaign-reopened mutation states;
- shared histories from empty through long enough to require progressive disclosure or bounded loading without pushing capture below an unbounded feed.

## Interaction and layout

- Phone portrait is the primary usage context. Lookup/results and the player sheet use one focused column, preserve the application's complete navigation, respect safe areas and the on-screen keyboard, and keep the next primary action within comfortable thumb reach.
- The player sheet is a full working stage on narrow screens, not a squeezed desktop side drawer. Wider screens may keep finder results beside the selected record to reduce back-and-forth, while tablet portrait and landscape reflow according to available content width rather than device labels.
- Every interactive target is at least 2.75rem. No operation depends on hover, precision tapping, or a swipe gesture. Focus remains visible and is restored deliberately after result selection, save, validation failure, discard confirmation, player change, and return from Place.
- Search status, mutation status, and close-time changes are announced without stealing focus. Labels and written state accompany semantic color; actor names, tag text, and placement status remain readable at outdoor-friendly contrast.
- Loading preserves the known player or query context instead of replacing the entire surface with an unanchored spinner. Reduced-motion users receive the same orientation without animated transitions.
- Search, notes, and tag choices stay bounded. The UI never silently truncates the campaign roster, shared history, or active tag catalog.

## Scope and boundaries

This brief covers the Active and Closed campaign Evaluate destination: campaign-scoped player lookup, selected-player evaluation context, shared note capture and ownership, quick tag application, member inline tag creation, close-time freezing, and the handoff to Place. It defines production-ready responsive behavior but no visual comp or implementation code.

Explicit anti-goals:

- No required evaluation order, remembered-last-player workflow, evaluator queue, checklist, or “evaluated” completion status.
- No structured ratings, scores, rubrics, rankings, private notes, blinded observations, or per-evaluator completion metrics.
- No automatic player opening from search and no automatic advance after saving.
- No player creation, roster enrollment, team placement controls, or tag-definition administration embedded in Evaluate.
- No evaluator-selected tag colors, fuzzy automatic merging, or silent recreation of archived labels.
- No camera recognition, jersey OCR, barcode/QR scanning, voice transcription, offline-first synchronization, or background autosave in this phase.
- No generic profile drawer, modal maze, card grid, ornamental sports imagery, or CSS-level visual prescription.

## Constraints and implementation consequences

- Preserve Fieldhouse Wayfinding, the campaign Route Markers, SSR-first Blazor architecture, club-scoped authorization, semantic written state, keyboard access, touch-safe controls, bounded queries, and meaningful error recovery.
- Existing `CampaignParticipantDrawer` behavior is evidence only. Its Previous/Next mechanics, focus restoration, attribution, and close-time conflict recovery are useful; its narrow drawer composition, hidden add-note form, select-and-Apply tag flow, and desktop-derived touch sizing are not design authority.
- The current roster query searches names only even though the URL-state description mentions name or tryout number. The campaign-loop build must add tenant-safe tryout-number matching and ranking while preserving deterministic paging and literal name-search semantics.
- The existing 4,000-character note contract remains authoritative. Note services already freeze on Closed campaigns, but current administrator edit/delete authority must be reconciled with the confirmed author-owns-note rule rather than exposed through UI capability flags.
- Foundation issue [#175](https://github.com/eruvalca/Nova/issues/175) opens tag creation and placement to club members while retaining administrator rename/archive/restore authority. The build must provide a member-safe inline create path that does not require field-side color choice and must preserve normalized club-wide uniqueness, the complete 100-active-definition bound, transaction safety, and duplicate-application protection.
- Inline create-and-apply crosses two existing mutations. Implement it as an idempotent compound operation or a reconciled sequence whose UI can prove the final definition/application state after retries and ambiguous commits; do not leave an unexplained orphan definition or duplicate application.
- Selected-player deep links, Place handoff, and return state use campaign-assignment identity, never a caller-supplied club scope. Closed/stale/deleted assignments and cross-tenant identifiers return non-disclosing outcomes.
- Preserve lifecycle mutation locks, optimistic concurrency, traceable `ProblemDetails`, author/actor attribution, and authoritative refresh after conflicts. Do not infer mutation success from local optimistic display.
- Detailed placement behavior remains owned by [#167](https://github.com/eruvalca/Nova/issues/167); campaign lifecycle and route topology remain owned by the confirmed [campaign-spine brief](campaign-spine.md). The later campaign-loop build composes them without duplicating their contracts.

## Decision record

- Organize evaluation around random field-side player lookup, not a prescribed sequence or progress model.
- Search name and tryout number from one prominent field; require a verification tap even for an exact match.
- Do not persist or promote a last-player resume state in this phase.
- Keep Previous/Next only as optional movement through the current result sequence.
- Use explicit **Save note**, then offer **Find another player**; never auto-advance.
- Preserve unsaved text through validation and recoverable failure, and confirm before discarding it on player change.
- Share notes with author and time attribution; authors alone edit/delete their notes.
- Apply existing tags in one action and create-and-apply missing tags inline without a color decision.
- Deduplicate tag names by collapsed whitespace and case; preserve first-created casing and block archived-name recreation.
- Show placement context in Evaluate, but hand off to Place with the player preselected for any mutation.
- Freeze the complete evaluation flow when Closed while preserving read-only shared history.
