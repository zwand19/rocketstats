import { sql } from "drizzle-orm";
import { db } from "@/db/client";
import type {
  HeadToHeadRecord,
  LiveMatchStateView,
  LivePlayerStatsView,
  MatchSessionView,
  PlayerAverages,
  PlayerStreak,
  SessionAverages,
  TeamRematchSummary,
} from "../api-types";

export async function getLiveMatchState(): Promise<LiveMatchStateView | null> {
  const result = await db.execute<{
    match_guid: string | null;
    arena: string | null;
    time_seconds: number;
    is_overtime: boolean;
    has_winner: boolean;
    winner: string | null;
    players: LivePlayerStatsView[];
    updated_at: string;
  }>(sql`
    SELECT match_guid, arena, time_seconds, is_overtime, has_winner, winner, players, updated_at
    FROM live_match_state WHERE id = 1
  `);

  const row = result.rows[0];
  if (!row) {
    return null;
  }

  return {
    matchGuid: row.match_guid,
    arena: row.arena,
    timeSeconds: row.time_seconds,
    isOvertime: row.is_overtime,
    hasWinner: row.has_winner,
    winner: row.winner,
    players: Array.isArray(row.players) ? row.players : [],
    updatedAt: toIso(row.updated_at),
  };
}

export async function getCurrentSession(): Promise<MatchSessionView | null> {
  const result = await db.execute<{
    id: string;
    started_at: string;
    ended_at: string;
    game_mode: number;
    match_guids: string[];
  }>(sql`
    SELECT id, started_at, ended_at, game_mode, match_guids
    FROM match_sessions
    ORDER BY ended_at DESC
    LIMIT 1
  `);

  const row = result.rows[0];
  if (!row) {
    return null;
  }

  return {
    id: row.id,
    startedAt: toIso(row.started_at),
    endedAt: toIso(row.ended_at),
    gameMode: row.game_mode,
    matchGuids: row.match_guids ?? [],
  };
}

export async function getPlayerAverages(
  primaryIds: readonly string[] | null,
  gameMode: number | null,
): Promise<PlayerAverages[]> {
  const idsLiteral = toPgArray(primaryIds && primaryIds.length > 0 ? primaryIds : null);

  const result = await db.execute<AveragesRow>(sql`
    WITH scoped AS (
      SELECT
        primary_id, name, match_guid, ended_at,
        score, goals, assists, saves, shots, touches, demos,
        average_boost, average_speed_kph, supersonic_percent, times_demoed,
        MAX(score) OVER (PARTITION BY match_guid) AS max_score
      FROM player_match_results
      WHERE (${gameMode}::int IS NULL OR game_mode = ${gameMode})
    )
    SELECT
      primary_id,
      (array_agg(name ORDER BY ended_at DESC))[1] AS name,
      COUNT(*)::int AS games_played,
      AVG(score)::float AS score,
      AVG(goals)::float AS goals,
      AVG(assists)::float AS assists,
      AVG(saves)::float AS saves,
      AVG(shots)::float AS shots,
      AVG(touches)::float AS touches,
      AVG(demos)::float AS demos,
      AVG(average_boost)::float AS boost,
      COUNT(*) FILTER (WHERE score = max_score)::int AS mvps,
      AVG(average_speed_kph)::float AS average_speed_kph,
      AVG(supersonic_percent)::float AS supersonic_percent,
      AVG(times_demoed)::float AS demoed
    FROM scoped
    WHERE (${idsLiteral}::text[] IS NULL OR primary_id = ANY(${idsLiteral}::text[]))
    GROUP BY primary_id
    ORDER BY AVG(goals) DESC
  `);

  return result.rows.map(toPlayerAverages);
}

export async function getCurrentSessionAverages(
  session: MatchSessionView,
  primaryIds: readonly string[],
): Promise<SessionAverages[]> {
  if (primaryIds.length === 0 || session.matchGuids.length === 0) {
    return [];
  }

  const matchGuidsLiteral = toPgArray(session.matchGuids);
  const idsLiteral = toPgArray(primaryIds);

  const result = await db.execute<SessionAveragesRow>(sql`
    WITH scoped AS (
      SELECT
        primary_id, name, match_guid, ended_at, team_num, winning_team,
        score, goals, assists, saves, shots, touches, demos,
        average_boost, average_speed_kph, supersonic_percent, times_demoed,
        MAX(score) OVER (PARTITION BY match_guid) AS max_score
      FROM player_match_results
      WHERE match_guid = ANY(${matchGuidsLiteral}::text[])
    )
    SELECT
      primary_id,
      (array_agg(name ORDER BY ended_at DESC))[1] AS name,
      COUNT(*)::int AS games_played,
      COUNT(*) FILTER (WHERE winning_team IS NOT NULL AND team_num = winning_team)::int AS wins,
      COUNT(*) FILTER (WHERE winning_team IS NOT NULL AND team_num <> winning_team)::int AS losses,
      AVG(score)::float AS score,
      AVG(goals)::float AS goals,
      AVG(assists)::float AS assists,
      AVG(saves)::float AS saves,
      AVG(shots)::float AS shots,
      AVG(touches)::float AS touches,
      AVG(demos)::float AS demos,
      AVG(average_boost)::float AS boost,
      COUNT(*) FILTER (WHERE score = max_score)::int AS mvps,
      AVG(average_speed_kph)::float AS average_speed_kph,
      AVG(supersonic_percent)::float AS supersonic_percent,
      AVG(times_demoed)::float AS demoed
    FROM scoped
    WHERE primary_id = ANY(${idsLiteral}::text[])
    GROUP BY primary_id
  `);

  return result.rows.map((row) => ({
    sessionId: session.id,
    primaryId: row.primary_id,
    name: row.name,
    gamesPlayed: row.games_played,
    wins: row.wins,
    losses: row.losses,
    score: numericOrZero(row.score),
    goals: numericOrZero(row.goals),
    assists: numericOrZero(row.assists),
    saves: numericOrZero(row.saves),
    shots: numericOrZero(row.shots),
    touches: numericOrZero(row.touches),
    demos: numericOrZero(row.demos),
    boost: numericOrZero(row.boost),
    mvps: row.mvps,
    mvpPercent: row.games_played === 0 ? 0 : (row.mvps * 100) / row.games_played,
    averageSpeedKph: row.average_speed_kph,
    supersonicPercent: row.supersonic_percent,
    demoed: row.demoed,
  }));
}

export async function getPlayerStreak(primaryId: string): Promise<PlayerStreak | null> {
  if (!primaryId) {
    return null;
  }

  const result = await db.execute<{ won: boolean; ended_at: string }>(sql`
    SELECT (team_num = winning_team) AS won, ended_at
    FROM player_match_results
    WHERE primary_id = ${primaryId}
      AND winning_team IS NOT NULL
    ORDER BY ended_at DESC
  `);

  if (result.rows.length === 0) {
    return null;
  }

  const isWinning = result.rows[0].won;
  let count = 0;
  for (const row of result.rows) {
    if (row.won === isWinning) {
      count++;
    } else {
      break;
    }
  }

  return { primaryId, count, isWinning };
}

export async function getHeadToHead(
  myPrimaryId: string,
  opponentIds: readonly string[],
  gameMode: number | null,
): Promise<HeadToHeadRecord[]> {
  if (!myPrimaryId || opponentIds.length === 0) {
    return [];
  }

  const opponentLiteral = toPgArray(opponentIds);

  const result = await db.execute<{
    primary_id: string;
    name: string;
    wins: number;
    losses: number;
    games_played: number;
  }>(sql`
    SELECT
      opp.primary_id,
      (array_agg(opp.name ORDER BY opp.ended_at DESC))[1] AS name,
      COUNT(*) FILTER (WHERE me.team_num = me.winning_team)::int AS wins,
      COUNT(*) FILTER (WHERE me.team_num <> me.winning_team)::int AS losses,
      COUNT(*)::int AS games_played
    FROM player_match_results me
    JOIN player_match_results opp
      ON opp.match_guid = me.match_guid
     AND opp.team_num <> me.team_num
    WHERE me.primary_id = ${myPrimaryId}
      AND me.winning_team IS NOT NULL
      AND opp.primary_id = ANY(${opponentLiteral}::text[])
      AND (${gameMode}::int IS NULL OR me.game_mode = ${gameMode})
    GROUP BY opp.primary_id
    HAVING COUNT(*) > 0
  `);

  return result.rows.map((row) => ({
    opponentId: row.primary_id,
    name: row.name,
    wins: row.wins,
    losses: row.losses,
    gamesPlayed: row.games_played,
  }));
}

export async function getTeamRematchSummary(
  myTeamIds: readonly string[],
  opponentIds: readonly string[],
  gameMode: number,
): Promise<TeamRematchSummary | null> {
  if (myTeamIds.length === 0 || opponentIds.length === 0) {
    return null;
  }

  const myTeamSet = new Set(myTeamIds.map((s) => s.toLowerCase()));
  const opponentSet = new Set(opponentIds.map((s) => s.toLowerCase()));

  const result = await db.execute<{
    match_guid: string;
    team_num: number;
    primary_id: string;
    won: boolean;
    ended_at: string;
  }>(sql`
    SELECT match_guid, team_num, primary_id,
      (team_num = winning_team) AS won, ended_at
    FROM player_match_results
    WHERE winning_team IS NOT NULL AND game_mode = ${gameMode}
  `);

  type Team = { players: Set<string>; won: boolean };
  type Match = { matchGuid: string; endedAt: Date; teams: Map<number, Team> };

  const matchMap = new Map<string, Match>();
  for (const row of result.rows) {
    let match = matchMap.get(row.match_guid);
    if (!match) {
      match = { matchGuid: row.match_guid, endedAt: new Date(row.ended_at), teams: new Map() };
      matchMap.set(row.match_guid, match);
    }
    const endedAt = new Date(row.ended_at);
    if (endedAt > match.endedAt) {
      match.endedAt = endedAt;
    }
    let team = match.teams.get(row.team_num);
    if (!team) {
      team = { players: new Set(), won: row.won };
      match.teams.set(row.team_num, team);
    }
    team.players.add(row.primary_id.toLowerCase());
    team.won = row.won;
  }

  const ordered = Array.from(matchMap.values()).sort((a, b) => b.endedAt.getTime() - a.endedAt.getTime());

  const session = await getCurrentSession();
  const sessionGuids = new Set((session?.matchGuids ?? []).map((g) => g.toLowerCase()));

  let allTimeWins = 0;
  let allTimeLosses = 0;
  let sessionWins = 0;
  let sessionLosses = 0;
  let streakWins = 0;
  let streakLosses = 0;
  let streakActive = true;

  for (const match of ordered) {
    if (match.teams.size !== 2) {
      streakActive = false;
      continue;
    }
    const teamArr = Array.from(match.teams.values());
    const myTeam = teamArr.find((t) => setEquals(t.players, myTeamSet));
    const oppTeam = teamArr.find((t) => setEquals(t.players, opponentSet));
    const matched = myTeam != null && oppTeam != null;

    if (matched) {
      if (myTeam!.won) {
        allTimeWins++;
      } else {
        allTimeLosses++;
      }
      if (sessionGuids.has(match.matchGuid.toLowerCase())) {
        if (myTeam!.won) {
          sessionWins++;
        } else {
          sessionLosses++;
        }
      }
      if (streakActive) {
        if (myTeam!.won) {
          streakWins++;
        } else {
          streakLosses++;
        }
      }
    } else {
      streakActive = false;
    }
  }

  const allTimeGames = allTimeWins + allTimeLosses;
  if (allTimeGames === 0) {
    return null;
  }

  return {
    gameMode,
    streakGames: streakWins + streakLosses,
    streakWins,
    streakLosses,
    allTimeGames,
    allTimeWins,
    allTimeLosses,
    sessionGames: sessionWins + sessionLosses,
    sessionWins,
    sessionLosses,
  };
}

function setEquals(a: Set<string>, b: Set<string>): boolean {
  if (a.size !== b.size) {
    return false;
  }
  for (const value of a) {
    if (!b.has(value)) {
      return false;
    }
  }
  return true;
}

type AveragesRow = {
  primary_id: string;
  name: string;
  games_played: number;
  score: number;
  goals: number;
  assists: number;
  saves: number;
  shots: number;
  touches: number;
  demos: number;
  boost: number;
  mvps: number;
  average_speed_kph: number | null;
  supersonic_percent: number | null;
  demoed: number | null;
};

type SessionAveragesRow = AveragesRow & {
  wins: number;
  losses: number;
};

function toPlayerAverages(row: AveragesRow): PlayerAverages {
  return {
    primaryId: row.primary_id,
    name: row.name,
    gamesPlayed: row.games_played,
    score: numericOrZero(row.score),
    goals: numericOrZero(row.goals),
    assists: numericOrZero(row.assists),
    saves: numericOrZero(row.saves),
    shots: numericOrZero(row.shots),
    touches: numericOrZero(row.touches),
    demos: numericOrZero(row.demos),
    boost: numericOrZero(row.boost),
    mvps: row.mvps,
    mvpPercent: row.games_played === 0 ? 0 : (row.mvps * 100) / row.games_played,
    averageSpeedKph: row.average_speed_kph,
    supersonicPercent: row.supersonic_percent,
    demoed: row.demoed,
  };
}

function numericOrZero(value: number | null | undefined): number {
  return value == null ? 0 : Number(value);
}

function toIso(value: string | Date): string {
  return typeof value === "string" ? new Date(value).toISOString() : value.toISOString();
}

function toPgArray(values: readonly string[] | null): string | null {
  if (!values) {
    return null;
  }
  const escaped = values.map((v) => `"${v.replace(/\\/g, "\\\\").replace(/"/g, '\\"')}"`);
  return `{${escaped.join(",")}}`;
}
