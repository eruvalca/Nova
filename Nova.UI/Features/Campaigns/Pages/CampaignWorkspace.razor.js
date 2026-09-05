let activeContainer = null;
import { read } from './CampaignEntry.razor.js';
export { acknowledgeOpeningReceipt, focus } from './CampaignEntry.razor.js';
export function readOpeningReceipt(scope, campaignId) {
    return read(scope, 'receipt:' + campaignId);
}
let keydownListener = null;

// Suppresses the browser's default keyboard activation click for roster rows and cards.
// Enter/Space on a tabindex element synthesizes a click on the currently focused element as
// the key's default action. The participant drawer moves focus to its close button when it
// opens, so without this suppression the synthesized click lands on the close button and
// immediately re-closes the drawer for keyboard users.
function suppressActivationDefault(event) {
    if (event.key !== 'Enter' && event.key !== ' ') {
        return;
    }
    const target = event.target;
    if (!(target instanceof Element) || !activeContainer || !activeContainer.contains(target)) {
        return;
    }
    if (target.closest('tr.roster-row') || target.closest('li.roster-card')) {
        event.preventDefault();
    }
}

export function captureScroll(element) {
    return element ? element.scrollTop : 0;
}

export function restoreScroll(element, scrollTop) {
    if (element) {
        element.scrollTop = scrollTop;
    }
}

export function scrollToTop(element) {
    if (element) {
        element.scrollTop = 0;
    }
}

// Attaches the keydown suppression on the document in the capture phase. The roster region
// element is recreated across loading/error/loaded renders, so callers re-invoke this on every
// render pass where the roster is visible; replace-on-attach keeps exactly one active listener.
// A container that is not an Element (for example, an unset ElementReference serialized as a
// plain object) installs nothing — its contains() check would throw on every keydown.
export function attachRosterActivationSuppression(container) {
    detachRosterActivationSuppression();
    if (!(container instanceof Element)) {
        return;
    }
    activeContainer = container;
    keydownListener = suppressActivationDefault;
    document.addEventListener('keydown', keydownListener, true);
}

export function detachRosterActivationSuppression() {
    if (keydownListener) {
        document.removeEventListener('keydown', keydownListener, true);
        keydownListener = null;
    }
    activeContainer = null;
}
