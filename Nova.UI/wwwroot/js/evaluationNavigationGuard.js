// The static Router's enhanced links/popstate do not run NavigationLock callbacks.
// Keep existing history entries intact; protect only the currently attached evidence owner.
const guards = new WeakMap();

function revokeReplay(state) {
    state.released = false;
    state.permitted = null;
    if (state.replay) {
        state.replay.revoked = true;
        state.replay.revoke(false);
    }
}

export function attachGuard(root, owner, lease, receiver) {
    const existing = guards.get(root);
    if (existing?.owner === owner && existing.lease === lease) { existing.receiver = receiver; return; }
    detachGuard(root);
    if (!window.navigation?.currentEntry?.key || typeof navigation.traverseTo !== 'function') {
        throw new Error('This browser cannot protect evaluation history. Update the browser to enable capture.');
    }
    const state = { owner, lease, receiver, pending: false, origin: navigation.currentEntry.key,
        controller: new AbortController(), returning: null, destination: null, request: 0, permitted: null, replay: null, released: false };
    guards.set(root, state);
    const owned = () => root.isConnected && guards.get(root) === state;
    const protectedWork = () => !state.released && (state.pending || root.dataset.evidenceProtected === 'true'
        || Array.from(root.querySelectorAll('textarea[data-evidence-original]'))
            .some(input => input.value !== input.dataset.evidenceOriginal));
    const options = { capture: true, signal: state.controller.signal };
    root.addEventListener('input', () => revokeReplay(state), options);
    window.addEventListener('beforeunload', event => {
        if (!owned() || !protectedWork()) return;
        event.preventDefault();
        event.returnValue = '';
    }, options);
    const notify = (url, key, request) => {
        if (owned() && request === state.request) {
            void state.receiver.invokeMethodAsync('ProtectNativeNavigationAsync', owner, lease, url, key)
                .catch(error => { if (owned()) console.error('Evaluation navigation remains protected; the departure prompt could not be shown.', error); });
        }
    };
    document.addEventListener('click', event => {
        if (!owned() || !protectedWork() || event.defaultPrevented || event.button !== 0
            || event.ctrlKey || event.metaKey || event.shiftKey || event.altKey) return;
        const anchor = event.target instanceof Element ? event.target.closest('a[href]') : null;
        if (!anchor || anchor.hasAttribute('download') || (anchor.target && anchor.target !== '_self')) return;
        const target = new URL(anchor.href, location.href);
        if (target.origin !== location.origin || !/^https?:$/.test(target.protocol)) return;
        event.preventDefault();
        event.stopImmediatePropagation();
        notify(target.href, null, ++state.request);
    }, options);
    navigation.addEventListener('currententrychange', () => {
        if (owned() && !protectedWork() && !state.returning && !state.replay) state.origin = navigation.currentEntry.key;
    }, { signal: state.controller.signal });
    window.addEventListener('popstate', event => {
        if (!owned()) return;
        const key = navigation.currentEntry.key;
        if (state.permitted?.key === key) {
            state.permitted.accept();
            state.permitted = null;
            state.origin = key;
            return;
        }
        if (state.returning === key) {
            event.stopImmediatePropagation();
            state.returning = null;
            const destination = state.destination;
            state.destination = null;
            if (destination) notify(destination.url, destination.key, destination.request);
            return;
        }
        if (!protectedWork() && !state.returning) { state.origin = key; return; }
        event.stopImmediatePropagation();
        if (key === state.origin) return;
        const target = location.href;
        const request = ++state.request;
        state.returning = state.origin;
        state.destination = { url: target, key, request };
        // Canceling and accepting traverse existing entry keys: no extra Back/Forward entries.
        // The matching popstate proves restoration. A concurrent enhanced fetch may abort
        // Navigation API's finished promise even though that history entry is restored.
        void navigation.traverseTo(state.origin).finished
            .catch(() => { /* A newer traversal or document departure superseded this owner. */ });
    }, options);
}

export function markPending(root, pending) {
    const state = guards.get(root);
    if (!state || !root.isConnected) throw new Error('Evaluation navigation protection is not ready.');
    state.pending = pending;
    if (pending) revokeReplay(state);
}

export function releaseNavigation(root, owner, lease) {
    const state = guards.get(root);
    if (!root.isConnected || state?.owner !== owner || state?.lease !== lease || state.pending || state.returning) {
        throw new Error('Evaluation navigation cannot be released.');
    }
    state.released = true;
}

export function cancelNavigation(root, owner, lease) {
    const state = guards.get(root);
    if (state?.owner === owner && state.lease === lease) revokeReplay(state);
}

export async function resumeHistory(root, owner, lease, key) {
    const state = guards.get(root);
    if (!root.isConnected || state?.owner !== owner || state?.lease !== lease || state.returning
        || state.pending || !state.released || state.replay) return false;
    if (navigation.currentEntry.key === key) return true;
    let accept, revoke;
    const accepted = new Promise(resolve => { accept = resolve; });
    const revoked = new Promise(resolve => { revoke = resolve; });
    const replay = { key, accept, revoke, revoked: false };
    state.replay = state.permitted = replay;
    try {
        const traversal = navigation.traverseTo(key);
        // Enhanced navigation can reject finished after the history entry committed.
        void traversal.finished.catch(() => {});
        const committed = Promise.all([traversal.committed, accepted]).then(() => true);
        const completed = await Promise.race([committed, revoked]);
        return completed && !replay.revoked && root.isConnected && guards.get(root) === state;
    } catch {
        revokeReplay(state);
        return false;
    } finally {
        if (state.permitted === replay) state.permitted = null;
        if (state.replay === replay) state.replay = null;
    }
}

export function detachGuard(root) {
    const state = guards.get(root);
    if (!state) return;
    revokeReplay(state);
    state.controller.abort();
    guards.delete(root);
}
