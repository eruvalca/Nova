const prefix = "nova:placement:";

export function readPending(scope) {
    const raw = sessionStorage.getItem(prefix + scope);
    if (raw === null) return null;
    const value = JSON.parse(raw);
    if (!value || typeof value.operationId !== "string" || !value.expectedConcurrencyToken
        || !Number.isSafeInteger(value.playerCampaignAssignmentId) || value.playerCampaignAssignmentId <= 0) {
        throw new Error("Stored placement is invalid; it cannot be dispatched.");
    }
    return value;
}

export function writePending(scope, input) {
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
