let keydownListener = null;
let previouslyFocused = null;

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

    if (!keydownListener) {
        previouslyFocused = document.activeElement;
    } else {
        document.removeEventListener('keydown', keydownListener, true);
    }
    keydownListener = (event) => {
        if (event.key !== 'Tab') {
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

export function close(restoreFallbackId) {
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
    return state;
}

function getFocusableElements(container) {
    return Array.from(container.querySelectorAll(focusableSelector)).filter(isElementVisible);
}

function isElementVisible(element) {
    return element.offsetWidth > 0 || element.offsetHeight > 0 || element.getClientRects().length > 0;
}
