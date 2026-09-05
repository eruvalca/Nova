// The installed skill is shared. Native adapters can identify the invocation
// syntax without changing script paths or duplicating executable behavior.
const provider = process.env.IMPECCABLE_HOOK_HARNESS || process.env.IMPECCABLE_PROVIDER;
export const IMPECCABLE_COMMAND_PREFIX = ['github', 'copilot', 'vscode'].includes(provider) ? "/" : "$";
export const IMPECCABLE_PROVIDER_ID = "agents";
export const IMPECCABLE_COMMAND = `${IMPECCABLE_COMMAND_PREFIX}impeccable`;
