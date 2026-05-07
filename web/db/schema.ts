import { sql } from "drizzle-orm";
import {
  boolean,
  doublePrecision,
  index,
  integer,
  jsonb,
  pgTable,
  primaryKey,
  text,
  timestamp,
  uuid,
} from "drizzle-orm/pg-core";

export const observedPlayers = pgTable("observed_players", {
  primaryId: text("primary_id").primaryKey(),
  name: text("name").notNull(),
  teamNum: integer("team_num").notNull().default(0),
  firstSeenAt: timestamp("first_seen_at", { withTimezone: true }).notNull(),
  lastSeenAt: timestamp("last_seen_at", { withTimezone: true }).notNull(),
  showOnDashboard: boolean("show_on_dashboard").notNull().default(false),
});

export const playerMatchResults = pgTable(
  "player_match_results",
  {
    matchGuid: text("match_guid").notNull(),
    primaryId: text("primary_id").notNull(),
    name: text("name").notNull(),
    arena: text("arena"),
    endedAt: timestamp("ended_at", { withTimezone: true }).notNull(),
    score: integer("score").notNull().default(0),
    goals: integer("goals").notNull().default(0),
    assists: integer("assists").notNull().default(0),
    saves: integer("saves").notNull().default(0),
    shots: integer("shots").notNull().default(0),
    touches: integer("touches").notNull().default(0),
    demos: integer("demos").notNull().default(0),
    averageBoost: doublePrecision("average_boost").notNull().default(0),
    teamNum: integer("team_num").notNull().default(0),
    winningTeam: integer("winning_team"),
    gameMode: integer("game_mode").notNull().default(0),
    averageSpeedKph: doublePrecision("average_speed_kph"),
    supersonicPercent: doublePrecision("supersonic_percent"),
    timesDemoed: integer("times_demoed"),
  },
  (table) => ({
    pk: primaryKey({ columns: [table.matchGuid, table.primaryId] }),
    endedAtIdx: index("player_match_results_ended_at_idx").on(sql`${table.endedAt} DESC`),
    primaryIdEndedAtIdx: index("player_match_results_primary_id_ended_at_idx").on(
      table.primaryId,
      sql`${table.endedAt} DESC`,
    ),
    gameModeEndedAtIdx: index("player_match_results_game_mode_ended_at_idx").on(
      table.gameMode,
      sql`${table.endedAt} DESC`,
    ),
  }),
);

export const matchSessions = pgTable("match_sessions", {
  id: text("id").primaryKey(),
  startedAt: timestamp("started_at", { withTimezone: true }).notNull(),
  endedAt: timestamp("ended_at", { withTimezone: true }).notNull(),
  gameMode: integer("game_mode").notNull().default(0),
  matchGuids: text("match_guids").array().notNull().default(sql`ARRAY[]::text[]`),
});

export const eventLog = pgTable(
  "event_log",
  {
    id: uuid("id").primaryKey().defaultRandom(),
    eventName: text("event_name").notNull(),
    matchGuid: text("match_guid"),
    receivedAt: timestamp("received_at", { withTimezone: true }).notNull(),
    rawJson: jsonb("raw_json").notNull(),
  },
  (table) => ({
    receivedAtIdx: index("event_log_received_at_idx").on(sql`${table.receivedAt} DESC`),
  }),
);

export const liveMatchState = pgTable("live_match_state", {
  id: integer("id").primaryKey().default(1),
  matchGuid: text("match_guid"),
  arena: text("arena"),
  timeSeconds: integer("time_seconds").notNull().default(0),
  isOvertime: boolean("is_overtime").notNull().default(false),
  hasWinner: boolean("has_winner").notNull().default(false),
  winner: text("winner"),
  players: jsonb("players").notNull().default(sql`'[]'::jsonb`),
  updatedAt: timestamp("updated_at", { withTimezone: true }).notNull(),
});

export const appSettings = pgTable("app_settings", {
  key: text("key").primaryKey(),
  value: text("value"),
});

export const agentHeartbeat = pgTable("agent_heartbeat", {
  id: integer("id").primaryKey().default(1),
  lastIngestAt: timestamp("last_ingest_at", { withTimezone: true }).notNull(),
  connectionState: text("connection_state").notNull(),
  lastError: text("last_error"),
});

export type ObservedPlayer = typeof observedPlayers.$inferSelect;
export type NewObservedPlayer = typeof observedPlayers.$inferInsert;
export type PlayerMatchResult = typeof playerMatchResults.$inferSelect;
export type NewPlayerMatchResult = typeof playerMatchResults.$inferInsert;
export type MatchSession = typeof matchSessions.$inferSelect;
export type NewMatchSession = typeof matchSessions.$inferInsert;
export type EventLogEntry = typeof eventLog.$inferSelect;
export type NewEventLogEntry = typeof eventLog.$inferInsert;
export type LiveMatchState = typeof liveMatchState.$inferSelect;
export type NewLiveMatchState = typeof liveMatchState.$inferInsert;
export type AgentHeartbeat = typeof agentHeartbeat.$inferSelect;
export type NewAgentHeartbeat = typeof agentHeartbeat.$inferInsert;
