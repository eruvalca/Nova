import { attachGuard, detachGuard, markPending } from '../../../js/evaluationNavigationGuard.js';
export { resumeHistory, releaseNavigation, cancelNavigation } from '../../../js/evaluationNavigationGuard.js';
const owners = new WeakMap();
const storageKey = owner => `nova:evaluation:v1:${owner}`;
const scrollKey = owner => `nova:evaluation-scroll:v1:${owner}`;

export function attach(root, owner, lease, finderOwner, captureOwner, receiver) {
    const previous = owners.get(root);
    if (previous) root.removeEventListener('click', previous.click);
    const sameCapture = previous?.owner === owner && previous?.lease === lease && previous?.captureOwner === captureOwner;
    const state = { owner, lease, revision: sameCapture ? previous.revision : -1,
        pending: sameCapture ? previous.pending : false, finderOwner, captureOwner };
    state.click = event => {
        if (!event.target.closest('[data-eval-result]')) return;
        try { sessionStorage.setItem(scrollKey(finderOwner), String(window.scrollY)); } catch { /* Scroll is optional; mutation storage is not. */ }
    };
    owners.set(root, state);
    root.addEventListener('click', state.click);
    attachGuard(root, owner, lease, receiver);
    markPending(root, state.pending);
}

function requireOwner(root, owner, lease) {
    const state = owners.get(root);
    if (!root?.isConnected || state?.owner !== owner || state?.lease !== lease) throw new Error('Evaluation owner changed.');
    return state;
}

export function read(root, owner, lease) {
    const state = requireOwner(root, owner, lease);
    state.pending = true;
    markPending(root, true);
    const raw = sessionStorage.getItem(storageKey(state.captureOwner));
    if (raw === null) { state.pending = false; markPending(root, false); return null; }
    const value = JSON.parse(raw);
    validateSnapshot(value);
    state.revision = value.revision;
    state.pending = value.pending !== null;
    markPending(root, state.pending);
    return value;
}

function validateSnapshot(value) {
    if (!value || !Number.isSafeInteger(value.revision) || value.revision < 0 || typeof value.draft !== 'string' || typeof value.traitSearch !== 'string'
        || typeof value.editContent !== 'string' || typeof value.editOriginal !== 'string'
        || !isGuid(value.editVersion) || (value.editingNoteId !== null && (!Number.isSafeInteger(value.editingNoteId) || value.editingNoteId <= 0))) throw new Error('Invalid retained evaluation draft.');
    if (value.pending !== null && (!value.pending || typeof value.pending !== 'object'
        || !['add', 'edit', 'delete', 'apply', 'create', 'remove'].includes(value.pending.kind)
        || !isOperationId(value.pending.operationId) || !isGuid(value.pending.version)
        || !Number.isSafeInteger(value.pending.assignmentId) || value.pending.assignmentId <= 0
        || (value.pending.subjectId !== null && (!Number.isSafeInteger(value.pending.subjectId) || value.pending.subjectId <= 0))
        || (value.pending.text !== null && typeof value.pending.text !== 'string'))) throw new Error('Invalid retained evaluation operation.');
}

function isOperationId(value) {
    return typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i.test(value);
}

function isGuid(value) {
    return typeof value === 'string' && /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i.test(value);
}

export function write(root, owner, lease, snapshot) {
    const state = requireOwner(root, owner, lease);
    if (snapshot.revision < state.revision) return false;
    validateSnapshot(snapshot);
    const json = JSON.stringify(snapshot);
    sessionStorage.setItem(storageKey(state.captureOwner), json);
    if (sessionStorage.getItem(storageKey(state.captureOwner)) !== json) throw new Error('Evaluation storage verification failed.');
    state.revision = snapshot.revision;
    state.pending = snapshot.pending !== null;
    markPending(root, state.pending);
    return true;
}

export function focusSheet(root, owner, lease) {
    const state = owners.get(root);
    if (!root?.isConnected || state?.owner !== owner || state?.lease !== lease) return false;
    const heading = root.querySelector('#evaluation-player-heading');
    if (!heading) return false;
    heading.focus({ preventScroll: true });
    return true;
}

export function restoreFinder(root, finderOwner, input) {
    if (!root?.isConnected) return;
    // Delayed attachment must not steal focus from a control the user already chose.
    const active = document.activeElement;
    if (active !== input && active instanceof HTMLElement && root.contains(active)
        && active.matches('a[href], button, input, select, textarea, summary, [tabindex]')) return;
    let saved = 0;
    try { saved = Number(sessionStorage.getItem(scrollKey(finderOwner))) || 0; } catch { /* Optional scroll restoration. */ }
    input?.focus({ preventScroll: true });
    window.scrollTo({ top: saved, behavior: 'instant' });
}

export function detach(root, lease) {
    const state = owners.get(root);
    if (!state || state.lease !== lease) return;
    root.removeEventListener('click', state.click);
    detachGuard(root);
    owners.delete(root);
}
