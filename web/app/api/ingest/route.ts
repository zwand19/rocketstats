import { NextResponse } from "next/server";
import { sql } from "drizzle-orm";
import { db } from "@/db/client";
import {
  agentHeartbeat,
  eventLog,
  liveMatchState,
  matchSessions,
  observedPlayers,
  playerMatchResults,
} from "@/db/schema";
import { requireIngestAuth } from "@/lib/auth";
import { ingestPayloadSchema, type PlayerMatchResultInput } from "@/lib/ingest-schema";
import { SESSION_GAP_MS, STORED_EVENT_LIMIT, STORED_MATCH_LIMIT } from "@/lib/options";

export const runtime = "nodejs";

export async function POST(request: Request) {
  const authError = requireIngestAuth(request);

  if (authError) {
    return authError;
  }

  const json = await request.json().catch(() => null);
  const parsed = ingestPayloadSchema.safeParse(json);

  if (!parsed.success) {
    return NextResponse.json(
      { error: "Invalid payload", issues: parsed.error.issues },
      { status: 400 },
    );
  }

  const payload = parsed.data;
  const now = new Date();

  if (payload.observedPlayers.length > 0) {
    await db
      .insert(observedPlayers)
      .values(
        payload.observedPlayers.map((p) => ({
          primaryId: p.primaryId,
          name: p.name,
          teamNum: p.teamNum,
          firstSeenAt: new Date(p.firstSeenAt),
          lastSeenAt: new Date(p.lastSeenAt),
        })),
      )
      .onConflictDoUpdate({
        target: observedPlayers.primaryId,
        set: {
          name: sql`excluded.name`,
          teamNum: sql`excluded.team_num`,
          lastSeenAt: sql`GREATEST(${observedPlayers.lastSeenAt}, excluded.last_seen_at)`,
        },
      });
  }

  if (payload.playerMatchResults.length > 0) {
    await db
      .insert(playerMatchResults)
      .values(
        payload.playerMatchResults.map((r) => ({
          matchGuid: r.matchGuid,
          primaryId: r.primaryId,
          name: r.name,
          arena: r.arena,
          endedAt: new Date(r.endedAt),
          score: r.score,
          goals: r.goals,
          assists: r.assists,
          saves: r.saves,
          shots: r.shots,
          touches: r.touches,
          demos: r.demos,
          averageBoost: r.averageBoost,
          teamNum: r.teamNum,
          winningTeam: r.winningTeam,
          gameMode: r.gameMode,
          averageSpeedKph: r.averageSpeedKph,
          supersonicPercent: r.supersonicPercent,
          timesDemoed: r.timesDemoed,
        })),
      )
      .onConflictDoNothing();

    await applySessionUpdates(payload.playerMatchResults);
    await trimMatches();
  }

  if (payload.events.length > 0) {
    await db
      .insert(eventLog)
      .values(
        payload.events.map((e) => ({
          id: e.id,
          eventName: e.eventName,
          matchGuid: e.matchGuid,
          receivedAt: new Date(e.receivedAt),
          rawJson: tryParseJson(e.rawJson) ?? e.rawJson,
        })),
      )
      .onConflictDoNothing();

    if (Math.random() < 0.1) {
      await trimEvents();
    }
  }

  if (payload.liveMatchState) {
    const live = payload.liveMatchState;
    await db
      .insert(liveMatchState)
      .values({
        id: 1,
        matchGuid: live.matchGuid,
        arena: live.arena,
        timeSeconds: live.timeSeconds,
        isOvertime: live.isOvertime,
        hasWinner: live.hasWinner,
        winner: live.winner,
        players: live.players,
        updatedAt: new Date(live.updatedAt),
      })
      .onConflictDoUpdate({
        target: liveMatchState.id,
        set: {
          matchGuid: sql`excluded.match_guid`,
          arena: sql`excluded.arena`,
          timeSeconds: sql`excluded.time_seconds`,
          isOvertime: sql`excluded.is_overtime`,
          hasWinner: sql`excluded.has_winner`,
          winner: sql`excluded.winner`,
          players: sql`excluded.players`,
          updatedAt: sql`excluded.updated_at`,
        },
      });
  }

  await db
    .insert(agentHeartbeat)
    .values({
      id: 1,
      lastIngestAt: now,
      connectionState: payload.connectionState,
      lastError: payload.lastError ?? null,
    })
    .onConflictDoUpdate({
      target: agentHeartbeat.id,
      set: {
        lastIngestAt: sql`excluded.last_ingest_at`,
        connectionState: sql`excluded.connection_state`,
        lastError: sql`excluded.last_error`,
      },
    });

  return NextResponse.json({ ok: true });
}

async function applySessionUpdates(results: readonly PlayerMatchResultInput[]) {
  const byMatchGuid = new Map<string, { endedAt: Date; gameMode: number }>();
  for (const r of results) {
    const endedAt = new Date(r.endedAt);
    const existing = byMatchGuid.get(r.matchGuid);
    if (!existing || endedAt > existing.endedAt) {
      byMatchGuid.set(r.matchGuid, { endedAt, gameMode: r.gameMode });
    }
  }

  const matches = [...byMatchGuid.entries()]
    .map(([matchGuid, m]) => ({ matchGuid, ...m }))
    .sort((a, b) => a.endedAt.getTime() - b.endedAt.getTime());

  for (const m of matches) {
    const [latest] = await db
      .select()
      .from(matchSessions)
      .orderBy(sql`${matchSessions.endedAt} DESC`)
      .limit(1);

    const within =
      latest != null &&
      latest.gameMode === m.gameMode &&
      m.endedAt.getTime() - new Date(latest.endedAt).getTime() <= SESSION_GAP_MS;

    if (within && latest) {
      if (latest.matchGuids.includes(m.matchGuid)) {
        continue;
      }

      await db
        .update(matchSessions)
        .set({
          endedAt: m.endedAt,
          matchGuids: [...latest.matchGuids, m.matchGuid],
        })
        .where(sql`${matchSessions.id} = ${latest.id}`);
    } else {
      await db.insert(matchSessions).values({
        id: crypto.randomUUID().replaceAll("-", ""),
        startedAt: m.endedAt,
        endedAt: m.endedAt,
        gameMode: m.gameMode,
        matchGuids: [m.matchGuid],
      });
    }
  }
}

async function trimEvents() {
  await db.execute(sql`
    DELETE FROM ${eventLog}
    WHERE ${eventLog.id} IN (
      SELECT ${eventLog.id} FROM ${eventLog}
      ORDER BY ${eventLog.receivedAt} DESC
      OFFSET ${STORED_EVENT_LIMIT}
    )
  `);
}

async function trimMatches() {
  await db.execute(sql`
    WITH ranked AS (
      SELECT match_guid,
             dense_rank() OVER (ORDER BY MAX(ended_at) DESC) AS rnk
      FROM player_match_results
      GROUP BY match_guid
    )
    DELETE FROM player_match_results
    WHERE match_guid IN (SELECT match_guid FROM ranked WHERE rnk > ${STORED_MATCH_LIMIT})
  `);
}

function tryParseJson(value: string): unknown {
  try {
    return JSON.parse(value);
  } catch {
    return null;
  }
}
