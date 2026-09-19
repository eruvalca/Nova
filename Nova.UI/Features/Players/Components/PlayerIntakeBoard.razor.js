// Durable, owner-scoped storage for one logical manual player creation, plus the uncommitted
// departure guard. Retained bytes are untrusted: invalid evidence is preserved for an explicit
// discard, but it is never dispatched as a command.
const prefix = "nova:player-creation:";
const operationIdPattern = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const isoDatePattern = /^\d{4}-\d{2}-\d{2}$/;
const lifetimeMs = 24 * 60 * 60 * 1000;

function ownerKey(actorUserId, clubId) {
    return prefix + actorUserId + ":" + clubId;
}

function validate(json) {
    let value;
    try {
        value = JSON.parse(json);
    } catch {
        throw new Error("Stored player creation is not readable JSON.");
    }

    const payload = value?.payload;
    if (!value || typeof value !== "object" || Array.isArray(value)
        || !Number.isSafeInteger(value.actorUserId) || value.actorUserId <= 0
        || typeof value.recoveryExpiresAt !== "string"
        || !payload || typeof payload !== "object" || Array.isArray(payload)
        || typeof payload.operationId !== "string" || !operationIdPattern.test(payload.operationId)
        || !Number.isSafeInteger(payload.clubId) || payload.clubId <= 0
        || typeof payload.firstName !== "string" || payload.firstName.trim().length === 0 || payload.firstName.length > 100
        || typeof payload.lastName !== "string" || payload.lastName.trim().length === 0 || payload.lastName.length > 100
        || typeof payload.dateOfBirth !== "string" || !isoDatePattern.test(payload.dateOfBirth)
        || !Number.isSafeInteger(payload.graduationYear) || payload.graduationYear < 2000 || payload.graduationYear > 2100
        || (payload.gender !== null && ![0, 1, 2].includes(payload.gender))
        || (payload.jerseyNumber !== null
            && (!Number.isSafeInteger(payload.jerseyNumber) || payload.jerseyNumber < 0 || payload.jerseyNumber > 9999))) {
        throw new Error("Stored player creation is invalid; it cannot be dispatched.");
    }

    // The deadline is the operation's own creation time plus the fixed lifetime; a record whose
    // deadline disagrees is contradictory evidence and must not become a command.
    const createdMs = Number.parseInt(payload.operationId.replaceAll("-", "").slice(0, 12), 16);
    const expectedDeadline = new Date(createdMs + lifetimeMs).getTime();
    if (!Number.isFinite(expectedDeadline) || new Date(value.recoveryExpiresAt).getTime() !== expectedDeadline) {
        throw new Error("Stored player creation has an inconsistent recovery deadline.");
    }

    return value;
}

export function readRecovery(actorUserId, clubId) {
    const key = ownerKey(actorUserId, clubId);
    const raw = localStorage.getItem(key);
    if (raw === null) return { json: null, invalidValue: null };
    try {
        const value = validate(raw);
        // The record must describe the same owner the caller asked about.
        return value.actorUserId === actorUserId && value.payload.clubId === clubId
            ? { json: raw, invalidValue: null }
            : { json: null, invalidValue: raw };
    } catch {
        return { json: null, invalidValue: raw };
    }
}

// localStorage has no compare-and-swap, so every read-then-write pair here is a cross-tab race: two
// same-owner tabs can both observe an empty record and each persist a different command (the loser's
// acknowledgement then has no recoverable record), and a removal that validated one operation can
// delete a newer one written in between. Web Locks makes each read/validate/write one critical section
// per owner, which is what the reservation and both removals need. Where the API is absent the work
// runs unguarded: the boundary stays usable and the single-tab contract is unchanged.
function withOwnerLock(actorUserId, clubId, work) {
    const locks = globalThis.navigator?.locks;
    return locks?.request ? locks.request(ownerKey(actorUserId, clubId), work) : work();
}

export function writePending(actorUserId, clubId, json) {
    const value = validate(json);
    if (value.actorUserId !== actorUserId || value.payload.clubId !== clubId) {
        throw new Error("Stored player creation belongs to another owner.");
    }

    return withOwnerLock(actorUserId, clubId, () => {
        const existing = readRecovery(actorUserId, clubId);
        if (existing.invalidValue !== null) {
            throw new Error("Set aside the retained player creation before starting another.");
        }
        if (existing.json !== null) {
            // One operation identity carries one command: a replay writes the retained command back, so a
            // different command under that identity is refused rather than allowed to replace the one the
            // member's dispatch is accounted for by. Its *representation* may differ, because a C# deserialize
            // and serialize round trip normalizes property order and GUID casing — refusing that would strand a
            // valid record behind a retry that can never succeed.
            if (!sameCommand(validate(existing.json), value)) {
                throw new Error("Recover the retained player creation before replacing its command.");
            }
        }

        localStorage.setItem(ownerKey(actorUserId, clubId), json);
        return json;
    });
}

// Whether two validated records describe the same command: the identity, the owner, the deadline and every
// dispatched value must agree, and only their representation may differ.
function sameCommand(left, right) {
    const retained = left.payload;
    const offered = right.payload;
    return left.actorUserId === right.actorUserId
        && new Date(left.recoveryExpiresAt).getTime() === new Date(right.recoveryExpiresAt).getTime()
        && retained.clubId === offered.clubId
        && retained.operationId.toLowerCase() === offered.operationId.toLowerCase()
        && retained.firstName === offered.firstName
        && retained.lastName === offered.lastName
        && retained.dateOfBirth === offered.dateOfBirth
        && retained.graduationYear === offered.graduationYear
        && retained.gender === offered.gender
        && retained.jerseyNumber === offered.jerseyNumber;
}

export function clearPending(actorUserId, clubId, operationId) {
    return withOwnerLock(actorUserId, clubId, () => {
        const current = readRecovery(actorUserId, clubId);
        if (current.json === null || !operationId) return false;
        if (validate(current.json).payload.operationId.toLowerCase() !== operationId.toLowerCase()) return false;
        localStorage.removeItem(ownerKey(actorUserId, clubId));
        return true;
    });
}

export function discardInvalidPending(actorUserId, clubId, expectedValue) {
    return withOwnerLock(actorUserId, clubId, () => {
        const key = ownerKey(actorUserId, clubId);
        const raw = localStorage.getItem(key);
        // Compare the exact inspected bytes; repaired or replaced evidence stays recoverable.
        if (raw === null || raw !== expectedValue) return false;
        localStorage.removeItem(key);
        return true;
    });
}

// One board owns the guard at a time. It is keyed by the caller's lease, not by an element
// reference: JS interop hands over a fresh proxy per call, so an element-keyed map would miss and
// silently leave the guard un-dirtied and un-detached.
let activeGuard = null;

function detachActiveGuard() {
    if (!activeGuard) return;
    activeGuard.controller.abort();
    activeGuard = null;
}

export function attachDepartureGuard(root, receiver, lease) {
    detachActiveGuard();
    const state = { root, receiver, lease, dirty: false, controller: new AbortController() };
    activeGuard = state;
    const options = { capture: true, signal: state.controller.signal };

    // Uncommitted input is observed where it actually happens, inside the board, so the protection
    // never depends on a cross-boundary flag arriving in time. `markDirty(lease, false)` clears it
    // once the work is committed or deliberately set aside.
    const markDirtyFromInput = () => { state.dirty = true; };
    root.addEventListener("input", markDirtyFromInput, options);
    root.addEventListener("change", markDirtyFromInput, options);

    window.addEventListener("beforeunload", event => {
        // A guard whose board has left the document must not speak for it, even if a teardown was
        // missed: nothing on the current page can be lost by leaving.
        if (activeGuard !== state || !state.dirty || !state.root.isConnected) return;
        event.preventDefault();
        event.returnValue = "";
    }, options);

    // Back/Forward (history traversal) is asked about through the Navigation API, whose `navigate` event
    // fires before the traversal commits and while this board is still mounted — measured in the browser:
    // the listener ran with the board connected, `cancelable` true, and cancelling it left the URL on
    // `/players/new` with the typed value intact. `popstate` cannot do this, because the router has already
    // taken the route by the time it is delivered, and a `NavigationLock` is not consulted for a traversal.
    // A cross-document traversal is not cancelable here and stays with the unload prompt above.
    window.navigation?.addEventListener("navigate", event => {
        if (activeGuard !== state || !state.dirty || !state.root.isConnected) return;
        if (event.navigationType !== "traverse" || !event.cancelable || !event.destination) return;
        const target = new URL(event.destination.url, location.href);
        if (target.origin !== location.origin || !/^https?:$/.test(target.protocol)) return;
        // A traversal to where the board already is loses nothing.
        if (target.pathname + target.search === location.pathname + location.search) return;
        // Cancelling here is what keeps the input: the member stays on the board until this ask is answered,
        // and consent navigates to the destination the traversal was headed for.
        event.preventDefault();
        void state.receiver.invokeMethodAsync("OnBoardDepartureAttemptAsync", state.lease, target.pathname + target.search)
            .catch(() => { /* The traversal is cancelled either way, so the input stays rather than going silently. */ });
    }, options);

    document.addEventListener("click", event => {
        if (activeGuard !== state || !state.dirty || !state.root.isConnected || event.defaultPrevented
            || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const anchor = event.target instanceof Element ? event.target.closest("a[href]") : null;
        if (!anchor || anchor.hasAttribute("download") || (anchor.target && anchor.target !== "_self")) return;
        // Every same-origin link a dirty board holds departs from it, the board's own included: the links the
        // board offers as resolutions navigate away like any other, so the prompt is what tells the member
        // their typing goes with them. A board with nothing to lose is not dirty at all, so its receipt and
        // frozen links never reach this listener with a prompt to give.
        const target = new URL(anchor.href, location.href);
        if (target.origin !== location.origin || !/^https?:$/.test(target.protocol)) return;
        // Same-destination clicks never discard input.
        if (target.pathname + target.search === location.pathname + location.search) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        void state.receiver.invokeMethodAsync("OnBoardDepartureAttemptAsync", state.lease, target.pathname + target.search)
            .catch(() => { /* The click stays blocked rather than silently discarding retyped input. */ });
    }, options);
}

export function markDirty(lease, dirty) {
    if (activeGuard?.lease !== lease) return;
    activeGuard.dirty = dirty === true;
}

// Focus follows the board's transitions: the first field after "Add another", the receipt after a
// commit, the first field needing correction after a refusal, and the blocking panel when entry is
// refused.
export function focusFirstField(root) {
    const field = root.querySelector("input:not([type=hidden]), select, textarea, button");
    field?.focus();
}

export function focusRegion(root, selector) {
    const region = root.querySelector(selector);
    if (!region) return;
    // A control that cannot take focus takes its description with it: a frozen board's fields are disabled
    // while its retained addition is unresolved, so the region the control names is what focus reaches and the
    // feedback is read rather than skipped. Every other target keeps its place in the tab order; only an
    // element that cannot be focused at all needs the attribute that makes programmatic focus possible, as a
    // heading does.
    const described = region.matches(":disabled") ? (region.getAttribute("aria-describedby") ?? "").split(/\s+/)[0] : "";
    const target = (described && root.querySelector(`#${CSS.escape(described)}`)) || region;
    if (!target.hasAttribute("tabindex") && target.tabIndex < 0) target.setAttribute("tabindex", "-1");
    target.focus();
}

export function detachDepartureGuard(lease) {
    if (activeGuard?.lease === lease) detachActiveGuard();
}
