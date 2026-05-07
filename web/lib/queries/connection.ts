import { sql, desc } from "drizzle-orm";
import { db } from "@/db/client";
import { agentHeartbeat, eventLog, observedPlayers } from "@/db/schema";
import type { ConnectionPayload } from "../api-types";

export async function getConnection(): Promise<ConnectionPayload> {
  const [hbRow] = await db.select().from(agentHeartbeat).limit(1);

  const events = await db
    .select({
      id: eventLog.id,
      eventName: eventLog.eventName,
      matchGuid: eventLog.matchGuid,
      receivedAt: eventLog.receivedAt,
    })
    .from(eventLog)
    .orderBy(desc(eventLog.receivedAt))
    .limit(10);

  const observedCount = await db.execute<{ count: number }>(sql`
    SELECT COUNT(*)::int AS count FROM observed_players
  `);
  const dashCount = await db.execute<{ count: number }>(sql`
    SELECT COUNT(*)::int AS count FROM observed_players WHERE show_on_dashboard = true
  `);

  const live = await db.execute<{ updated_at: string | null }>(sql`
    SELECT updated_at FROM live_match_state WHERE id = 1
  `);

  void observedPlayers;

  return {
    state: hbRow?.connectionState ?? "Stopped",
    lastError: hbRow?.lastError ?? null,
    lastIngestAt: hbRow ? hbRow.lastIngestAt.toISOString() : null,
    observedPlayerCount: observedCount.rows[0]?.count ?? 0,
    dashboardPlayerCount: dashCount.rows[0]?.count ?? 0,
    events: events.map((e) => ({
      id: e.id,
      eventName: e.eventName,
      matchGuid: e.matchGuid,
      receivedAt: e.receivedAt.toISOString(),
    })),
    liveUpdatedAt: live.rows[0]?.updated_at ? new Date(live.rows[0].updated_at).toISOString() : null,
  };
}
