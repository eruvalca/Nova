// Enhanced navigation focuses the prerendered heading. Interactive attachment can replace
// that element and return focus to the body. Restore it only if no control now owns focus.
export function restoreHeadingFocusAfterAttach(hall) {
    if (!(hall instanceof HTMLElement) || !hall.isConnected) {
        return;
    }

    const document = hall.ownerDocument;
    if (document.activeElement && document.activeElement !== document.body) {
        return;
    }

    const heading = hall.querySelector('h1');
    if (heading instanceof HTMLElement) {
        heading.tabIndex = -1;
        heading.focus({ preventScroll: true });
    }
}
