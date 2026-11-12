export const STORED_EVENT_LIMIT = 5000;
export const STORED_MATCH_LIMIT = 500;
export const SESSION_GAP_MS = 60 * 60 * 1000;

// The agent posts every ~10s. Anything older than this is no longer "live".
export const LIVE_STALE_MS = 45 * 1000;
export const AGENT_OFFLINE_MS = 45 * 1000;
