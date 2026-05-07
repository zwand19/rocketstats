import { listObservedPlayers } from "./players";
import {
  getCurrentSession,
  getCurrentSessionAverages,
  getHeadToHead,
  getLiveMatchState,
  getPlayerAverages,
  getPlayerStreak,
  getTeamRematchSummary,
} from "./dashboard";
import { getMePrimaryId } from "../settings";
import type { DashboardPayload } from "../api-types";

export async function getDashboard(selectedGameMode: number | null): Promise<DashboardPayload> {
  const [allObserved, mePrimaryId, live, currentSession] = await Promise.all([
    listObservedPlayers(),
    getMePrimaryId(),
    getLiveMatchState(),
    getCurrentSession(),
  ]);

  const dashboardPlayers = allObserved.filter((p) => p.showOnDashboard);
  const trackedIds = dashboardPlayers.map((p) => p.primaryId);

  const [careerAverages, careerForSessionCompare, sessionAverages] = await Promise.all([
    getPlayerAverages(trackedIds, selectedGameMode),
    currentSession ? getPlayerAverages(trackedIds, currentSession.gameMode) : Promise.resolve([]),
    currentSession ? getCurrentSessionAverages(currentSession, trackedIds) : Promise.resolve([]),
  ]);

  const me = mePrimaryId ? allObserved.find((p) => p.isMe) ?? null : null;
  const meStreak = me ? await getPlayerStreak(me.primaryId) : null;

  let headToHead: DashboardPayload["headToHead"] = [];
  let rematch: DashboardPayload["rematch"] = null;

  if (live && live.players.length > 0 && trackedIds.length > 0) {
    const trackedSet = new Set(trackedIds.map((s) => s.toLowerCase()));
    const trackedInMatch = live.players.filter((p) => trackedSet.has(p.primaryId.toLowerCase()));

    if (trackedInMatch.length > 0) {
      const myTeamNum = trackedInMatch[0].teamNum;
      const myTeamIds = live.players
        .filter((p) => p.teamNum === myTeamNum)
        .map((p) => p.primaryId);
      const opponentIds = live.players
        .filter((p) => p.teamNum !== myTeamNum)
        .map((p) => p.primaryId);

      if (opponentIds.length > 0) {
        const perspective = trackedInMatch[0].primaryId;
        headToHead = await getHeadToHead(perspective, opponentIds, null);

        const liveGameMode = live.players.length;
        rematch = await getTeamRematchSummary(myTeamIds, opponentIds, liveGameMode);
      }
    }
  }

  return {
    live,
    dashboardPlayers,
    mePrimaryId,
    meStreak,
    currentSession,
    sessionAverages,
    careerAverages,
    careerForSessionCompare,
    headToHead,
    rematch,
    selectedGameMode,
  };
}
