import { eq, desc } from "drizzle-orm";
import { db } from "@/db/client";
import { observedPlayers } from "@/db/schema";
import { getMePrimaryId } from "../settings";
import type { ObservedPlayerView } from "../api-types";

export async function listObservedPlayers(): Promise<ObservedPlayerView[]> {
  const me = await getMePrimaryId();

  const rows = await db
    .select()
    .from(observedPlayers)
    .orderBy(desc(observedPlayers.lastSeenAt));

  return rows.map((row) => ({
    primaryId: row.primaryId,
    name: row.name,
    teamNum: row.teamNum,
    firstSeenAt: row.firstSeenAt.toISOString(),
    lastSeenAt: row.lastSeenAt.toISOString(),
    showOnDashboard: row.showOnDashboard,
    isMe: me != null && me.toLowerCase() === row.primaryId.toLowerCase(),
  }));
}

export async function listDashboardPlayers(): Promise<ObservedPlayerView[]> {
  const players = await listObservedPlayers();
  return players.filter((p) => p.showOnDashboard);
}

export async function setShowOnDashboard(primaryId: string, show: boolean): Promise<void> {
  await db
    .update(observedPlayers)
    .set({ showOnDashboard: show })
    .where(eq(observedPlayers.primaryId, primaryId));
}
