const prefix = "nova:placement:";

const guid = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;
const operationId = /^[0-9a-f]{8}-[0-9a-f]{4}-7[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i;
const emptyGuid = "00000000-0000-0000-0000-000000000000";

function validatePending(value) {
    // Retained data is untrusted. Preserve invalid evidence, but never dispatch it as a command.
    if (!value || typeof value !== "object" || Array.isArray(value)
        || typeof value.operationId !== "string" || !operationId.test(value.operationId)
        || typeof value.expectedConcurrencyToken !== "string" || !guid.test(value.expectedConcurrencyToken)
        || value.expectedConcurrencyToken === emptyGuid
        || !Number.isSafeInteger(value.playerCampaignAssignmentId) || value.playerCampaignAssignmentId <= 0
        || ![1, 2, 3].includes(value.outcome)
        || (value.outcome === 1 ? !Number.isSafeInteger(value.teamId) || value.teamId <= 0 : value.teamId !== null)) {
        throw new Error("Stored placement is invalid; it cannot be dispatched.");
    }
    return value;
}

export function readPending(scope) {
    const raw = sessionStorage.getItem(prefix + scope);
    if (raw === null) return null;
    return validatePending(JSON.parse(raw));
}

export function writePending(scope, input) {
    validatePending(input);
    const current = readPending(scope);
    if (current && JSON.stringify(current) !== JSON.stringify(input)) {
        throw new Error("Recover the existing placement before replacing it.");
    }
    sessionStorage.setItem(prefix + scope, JSON.stringify(input));
}

export function clearPending(scope, operationId) {
    const current = readPending(scope);
    if (current?.operationId.toLowerCase() === operationId.toLowerCase()) sessionStorage.removeItem(prefix + scope);
}

// The rail's scroll belongs to the queue context, independent of the selected mobile stage.
class PlacementScroll extends HTMLElement {
    static observedAttributes = ["data-key"];
    connectedCallback() {
        this.scroller = this.closest(".place-queue");
        this.onScroll = () => {
            try { sessionStorage.setItem("nova:placement-scroll:" + this.dataset.key, String(this.scroller.scrollTop)); }
            catch { /* Scroll restoration is optional; operation storage has its own blocking gate. */ }
        };
        this.scroller?.addEventListener("scroll", this.onScroll);
        this.restore();
    }
    attributeChangedCallback() { if (this.isConnected) this.restore(); }
    restore() {
        cancelAnimationFrame(this.frame);
        this.frame = requestAnimationFrame(() => {
            try {
                const top = Number(sessionStorage.getItem("nova:placement-scroll:" + this.dataset.key));
                if (this.scroller && Number.isFinite(top)) this.scroller.scrollTop = Math.max(0, top);
            } catch { /* Optional browsing state never blocks placement. */ }
        });
    }
    disconnectedCallback() {
        cancelAnimationFrame(this.frame);
        this.scroller?.removeEventListener("scroll", this.onScroll);
    }
}
if (!customElements.get("nova-place-scroll")) customElements.define("nova-place-scroll", PlacementScroll);
