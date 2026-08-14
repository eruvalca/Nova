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
