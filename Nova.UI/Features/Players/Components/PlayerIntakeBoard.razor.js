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
            const current = validate(existing.json);
            if (current.payload.operationId.toLowerCase() !== value.payload.operationId.toLowerCase()) {
                throw new Error("Recover the existing player creation before starting another.");
            }
        }

        localStorage.setItem(ownerKey(actorUserId, clubId), json);
        return json;
    });
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

    // Known gap, verified in the browser: Back/Forward (history traversal) is not intercepted, so it is
    // the one departure path that can discard typed input without this prompt. A traversal of the
    // board's own entry leaves the route, so the router disposes the board and aborts these listeners
    // before popstate is delivered (`runs: 0` with the guard still live and dirty at the last moment
    // before the traversal), and a page-level NavigationLock's OnBeforeInternalNavigation is not
    // consulted for the traversal either. Protecting it therefore needs the traversal handled where the
    // owner outlives it — the evaluated surfaces' rollback/approval path, which their persistent panel
    // makes possible — and is not part of this board's contract, which is document unload and
    // same-origin link departure.
    document.addEventListener("click", event => {
        if (activeGuard !== state || !state.dirty || !state.root.isConnected || event.defaultPrevented
            || event.button !== 0 || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const anchor = event.target instanceof Element ? event.target.closest("a[href]") : null;
        if (!anchor || anchor.hasAttribute("download") || (anchor.target && anchor.target !== "_self")) return;
        // The board's own controls are intentional, board-mediated transitions: Cancel returns to the
        // directory, and the duplicate/receipt actions are the resolutions the board offers. Only a
        // departure outside the board abandons input the member did not choose to leave.
        if (state.root.contains(anchor)) return;
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
// commit, and the blocking panel when entry is refused.
export function focusFirstField(root) {
    const field = root.querySelector("input:not([type=hidden]), select, textarea, button");
    field?.focus();
}

export function focusRegion(root, selector) {
    const region = root.querySelector(selector);
    if (!region) return;
    if (!region.hasAttribute("tabindex")) region.setAttribute("tabindex", "-1");
    region.focus();
}

export function detachDepartureGuard(lease) {
    if (activeGuard?.lease === lease) detachActiveGuard();
}
