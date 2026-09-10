import { attachGuard, detachGuard, markPending } from '../../../js/evaluationNavigationGuard.js';
export { resumeHistory, releaseNavigation } from '../../../js/evaluationNavigationGuard.js';
export function protectNavigation(dialog, owner, lease, receiver) { attachGuard(dialog, owner, lease, receiver); }
let keydownListener = null;
let previouslyFocused = null;
let activeDialog = null;
let resizeListener = null;
const mobileContext = window.matchMedia('(max-width: 1199px)');

const focusableSelector = [
    'a[href]',
    'button:not([disabled])',
    'input:not([disabled])',
    'select:not([disabled])',
    'textarea:not([disabled])',
    '[tabindex]:not([tabindex="-1"])'
].join(', ');

export function focus(element) {
    if (element instanceof Element) {
        element.focus();
    }
}

// Opens the drawer: captures the activating roster row/card, installs a document-level Tab trap
// that cycles focus through the dialog, and focuses the close button. Re-opening keeps the
// originally captured element so closing restores focus to the roster row/card that opened the
// drawer, not to a mid-dialog control.
export function open(dialog, closeButton) {
    if (!(dialog instanceof Element)) {
        return;
    }

    if (!keydownListener || activeDialog !== dialog) {
        previouslyFocused = document.activeElement;
    }
    if (keydownListener) {
        document.removeEventListener('keydown', keydownListener, true);
    }
    if (resizeListener) mobileContext.removeEventListener('change', resizeListener);
    activeDialog = dialog;
    resizeListener = () => {
        dialog.setAttribute('role', mobileContext.matches ? 'dialog' : 'region');
        if (mobileContext.matches) dialog.setAttribute('aria-modal', 'true');
        else dialog.removeAttribute('aria-modal');
    };
    resizeListener();
    mobileContext.addEventListener('change', resizeListener);
    keydownListener = (event) => {
        if (event.key !== 'Tab' || !mobileContext.matches || activeDialog !== dialog) {
            return;
        }

        const focusable = getFocusableElements(dialog);
        if (focusable.length === 0) {
            event.preventDefault();
            return;
        }

        const first = focusable[0];
        const last = focusable[focusable.length - 1];
        const active = document.activeElement;

        if (event.shiftKey) {
            if (active === first || !dialog.contains(active)) {
                event.preventDefault();
                last.focus();
            }
        } else if (active === last || !dialog.contains(active)) {
            event.preventDefault();
            first.focus();
        }
    };
    document.addEventListener('keydown', keydownListener, true);

    focus(closeButton instanceof Element ? closeButton : getFocusableElements(dialog)[0]);
}

// Pulls focus back into the dialog when it lands outside of it. A boundary move renders the
// focused prev/next button disabled, which drops focus to <body>; restoring it keeps the Tab
// trap and Escape handling alive. In-dialog focus is left untouched.
export function restoreFocus(dialog, closeButton) {
    if (!(dialog instanceof Element) || dialog.contains(document.activeElement)) {
        return;
    }

    focus(closeButton instanceof Element ? closeButton : getFocusableElements(dialog)[0]);
}

export function close(restoreFallbackId, dialog) {
    detachGuard(dialog);
    if (activeDialog !== dialog) return;
    const state = takeDownTrap();
    if (!state) {
        return;
    }

    const candidates = [];
    if (restoreFallbackId) {
        candidates.push(document.getElementById(restoreFallbackId));
        if (restoreFallbackId.startsWith('roster-row-')) {
            candidates.push(document.getElementById(restoreFallbackId.replace('roster-row-', 'roster-card-')));
        }
    }
    candidates.push(state);

    for (const candidate of candidates) {
        if (candidate && candidate.isConnected && isElementVisible(candidate)) {
            candidate.focus();
            return;
        }
    }
}

export function detach() {
    takeDownTrap();
}

function takeDownTrap() {
    const state = previouslyFocused;
    if (keydownListener) {
        document.removeEventListener('keydown', keydownListener, true);
        keydownListener = null;
    }
    previouslyFocused = null;
    activeDialog = null;
    if (resizeListener) mobileContext.removeEventListener('change', resizeListener);
    resizeListener = null;
    return state;
}

function getFocusableElements(container) {
    return Array.from(container.querySelectorAll(focusableSelector)).filter(isElementVisible);
}

function isElementVisible(element) {
    return element.offsetWidth > 0 || element.offsetHeight > 0 || element.getClientRects().length > 0;
}

const operationKey = scope => `nova:evaluation-drawer:v1:${scope}`;
export function readOperation(dialog, scope) {
    if (activeDialog !== dialog) throw new Error('Participant owner changed.');
    const payload = sessionStorage.getItem(operationKey(scope));
    markPending(dialog, payload !== null);
    return payload;
}
export function writeOperation(dialog, scope, payload) {
    if (activeDialog !== dialog) throw new Error('Participant owner changed.');
    const existing = sessionStorage.getItem(operationKey(scope));
    if (existing !== null && existing !== payload) throw new Error('Recover the existing operation first.');
    sessionStorage.setItem(operationKey(scope), payload);
    if (sessionStorage.getItem(operationKey(scope)) !== payload) throw new Error('Recovery storage verification failed.');
    markPending(dialog, true);
}
export function clearOperation(dialog, scope, expected) {
    if (activeDialog !== dialog) throw new Error('Participant owner changed.');
    if (sessionStorage.getItem(operationKey(scope)) === expected) sessionStorage.removeItem(operationKey(scope));
    markPending(dialog, sessionStorage.getItem(operationKey(scope)) !== null);
}
