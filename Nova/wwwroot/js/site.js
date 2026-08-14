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

window.novaCampaignWorkspaceFocus = function (elementId) {
    document.getElementById(elementId)?.focus();
};

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
