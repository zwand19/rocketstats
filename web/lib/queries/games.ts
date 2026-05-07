import { sql } from "drizzle-orm";
import { db } from "@/db/client";
import { matchSessions, playerMatchResults } from "@/db/schema";
import type { MatchesPage, MatchSummary } from "../api-types";

export async function getMatchesPaged(page: number, pageSize: number): Promise<MatchesPage> {
  const safePage = Math.max(1, Math.floor(page) || 1);
  const safePageSize = Math.max(1, Math.min(100, Math.floor(pageSize) || 20));

  const totalResult = await db.execute<{ count: number }>(sql`
    SELECT COUNT(DISTINCT match_guid)::int AS count FROM player_match_results
  `);
  const totalCount = totalResult.rows[0]?.count ?? 0;

  const offset = (safePage - 1) * safePageSize;

  const matchesResult = await db.execute<{
    match_guid: string;
    ended_at: string;
    arena: string | null;
    game_mode: number;
    winning_team: number | null;
  }>(sql`
    SELECT
      match_guid,
      MAX(ended_at) AS ended_at,
      (array_agg(arena) FILTER (WHERE arena IS NOT NULL))[1] AS arena,
      MAX(game_mode) AS game_mode,
      (array_agg(winning_team) FILTER (WHERE winning_team IS NOT NULL))[1] AS winning_team
    FROM player_match_results
    GROUP BY match_guid
    ORDER BY MAX(ended_at) DESC
    LIMIT ${safePageSize} OFFSET ${offset}
  `);

  const matchGuids = matchesResult.rows.map((m) => m.match_guid);
  const matchGuidsLiteral = toPgArrayLiteral(matchGuids);
  const playersResult = matchGuids.length === 0
    ? { rows: [] as PlayerRow[] }
    : await db.execute<PlayerRow>(sql`
        SELECT match_guid, primary_id, name, team_num, score, goals, assists, saves
        FROM player_match_results
        WHERE match_guid = ANY(${matchGuidsLiteral}::text[])
      `);

  const playersByMatch = new Map<string, PlayerRow[]>();
  for (const row of playersResult.rows) {
    const list = playersByMatch.get(row.match_guid) ?? [];
    list.push(row);
    playersByMatch.set(row.match_guid, list);
  }

  const items: MatchSummary[] = matchesResult.rows.map((row) => {
    const players = (playersByMatch.get(row.match_guid) ?? [])
      .sort((a, b) => a.team_num - b.team_num || b.score - a.score)
      .map((p) => ({
        primaryId: p.primary_id,
        name: p.name,
        teamNum: p.team_num,
        score: p.score,
        goals: p.goals,
        assists: p.assists,
        saves: p.saves,
      }));

    return {
      matchGuid: row.match_guid,
      endedAt: new Date(row.ended_at).toISOString(),
      arena: row.arena,
      gameMode: row.game_mode,
      winningTeam: row.winning_team,
      players,
    };
  });

  return {
    items,
    page: safePage,
    pageSize: safePageSize,
    totalCount,
  };
}

export async function deleteMatch(matchGuid: string): Promise<void> {
  if (!matchGuid) {
    return;
  }

  await db.execute(sql`
    DELETE FROM ${playerMatchResults} WHERE match_guid = ${matchGuid}
  `);

  await db.execute(sql`
    UPDATE ${matchSessions}
    SET match_guids = array_remove(match_guids, ${matchGuid})
    WHERE ${matchGuid} = ANY(match_guids)
  `);

  await db.execute(sql`
    DELETE FROM ${matchSessions} WHERE cardinality(match_guids) = 0
  `);
}

type PlayerRow = {
  match_guid: string;
  primary_id: string;
  name: string;
  team_num: number;
  score: number;
  goals: number;
  assists: number;
  saves: number;
};

function toPgArrayLiteral(values: readonly string[]): string {
  const escaped = values.map((v) => `"${v.replace(/\\/g, "\\\\").replace(/"/g, '\\"')}"`);
  return `{${escaped.join(",")}}`;
}
