window.novaShowModal = function (selector) {
    bootstrap.Modal.getOrCreateInstance(document.querySelector(selector)).show();
};

window.novaCampaignWorkspaceCaptureScroll = function (elementId) {
    const element = document.getElementById(elementId);
    return element ? element.scrollTop : 0;
};

window.novaCampaignWorkspaceRestoreScroll = function (elementId, scrollTop) {
    const element = document.getElementById(elementId);
    if (element) {
        element.scrollTop = scrollTop;
    }
};

window.novaCampaignWorkspaceScrollToTop = function (elementId) {
    const element = document.getElementById(elementId);
    if (element) {
        element.scrollTop = 0;
    }
};

// ── Participant drawer focus management ─────────────────────────────────────────
// Opening the drawer captures the activating roster row/card, installs a document-level
// Tab trap that cycles focus through the dialog, and focuses the close button. Closing
// removes the trap and restores focus to the selected participant's visible row/card,
// falling back to the captured element, then to nothing. Dispose removes the trap only.

window.novaCampaignParticipantDrawerOpen = function (dialogSelector, closeButtonId) {
    const dialog = document.querySelector(dialogSelector);
    if (!dialog) {
        return;
    }

    const existing = window.__novaParticipantDrawerFocusState;
    const previouslyFocused = existing ? existing.previouslyFocused : document.activeElement;
    if (existing) {
        document.removeEventListener('keydown', existing.keydownHandler, true);
    }

    const keydownHandler = function (event) {
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

    document.addEventListener('keydown', keydownHandler, true);
    window.__novaParticipantDrawerFocusState = { previouslyFocused: previouslyFocused, keydownHandler: keydownHandler };

    const closeButton = document.getElementById(closeButtonId);
    (closeButton || getFocusableElements(dialog)[0])?.focus();
};

window.novaCampaignParticipantDrawerClose = function (restoreFallbackId) {
    const state = removeParticipantDrawerTrap();
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
    candidates.push(state.previouslyFocused);

    for (const candidate of candidates) {
        if (candidate && candidate.isConnected && isElementVisible(candidate)) {
            candidate.focus();
            return;
        }
    }
};

window.novaCampaignParticipantDrawerRestoreFocus = function (dialogSelector, closeButtonId) {
    const dialog = document.querySelector(dialogSelector);
    if (!dialog || dialog.contains(document.activeElement)) {
        return false;
    }

    // A boundary move renders the focused prev/next button disabled, which drops focus to
    // <body>; pull it back into the dialog so the Tab trap and Escape keep working.
    const closeButton = document.getElementById(closeButtonId);
    (closeButton || getFocusableElements(dialog)[0])?.focus();
    return true;
};

window.novaCampaignParticipantDrawerDispose = function () {
    removeParticipantDrawerTrap();
};

function removeParticipantDrawerTrap() {
    const state = window.__novaParticipantDrawerFocusState;
    if (!state) {
        return null;
    }

    document.removeEventListener('keydown', state.keydownHandler, true);
    window.__novaParticipantDrawerFocusState = null;
    return state;
}

function getFocusableElements(container) {
    const selector = [
        'a[href]',
        'button:not([disabled])',
        'input:not([disabled])',
        'select:not([disabled])',
        'textarea:not([disabled])',
        '[tabindex]:not([tabindex="-1"])'
    ].join(', ');
    return Array.from(container.querySelectorAll(selector)).filter(isElementVisible);
}

function isElementVisible(element) {
    return element.offsetWidth > 0 || element.offsetHeight > 0 || element.getClientRects().length > 0;
}

// Suppresses the browser's default keyboard activation click for roster rows and cards.
// Enter/Space on a tabindex element synthesizes a click on the currently focused element as
// the key's default action. The participant drawer moves focus to its close button when it
// opens, so without this suppression the synthesized click lands on the close button and
// immediately re-closes the drawer for keyboard users.
(function () {
    function suppressRosterActivationDefault(event) {
        if (event.key !== 'Enter' && event.key !== ' ') {
            return;
        }
        const target = event.target;
        if (target && target.closest && (target.closest('tr.roster-row') || target.closest('li.roster-card'))) {
            event.preventDefault();
        }
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', function () {
            document.addEventListener('keydown', suppressRosterActivationDefault, true);
        });
    } else {
        document.addEventListener('keydown', suppressRosterActivationDefault, true);
    }
})();
