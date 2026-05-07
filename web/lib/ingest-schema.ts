import { z } from "zod";

const isoDate = z.string().datetime({ offset: true });

export const observedPlayerInputSchema = z.object({
  primaryId: z.string().min(1),
  name: z.string(),
  teamNum: z.number().int(),
  firstSeenAt: isoDate,
  lastSeenAt: isoDate,
});

export const playerMatchResultInputSchema = z.object({
  matchGuid: z.string().min(1),
  primaryId: z.string().min(1),
  name: z.string(),
  arena: z.string().nullable(),
  endedAt: isoDate,
  score: z.number().int(),
  goals: z.number().int(),
  assists: z.number().int(),
  saves: z.number().int(),
  shots: z.number().int(),
  touches: z.number().int(),
  demos: z.number().int(),
  averageBoost: z.number(),
  teamNum: z.number().int(),
  winningTeam: z.number().int().nullable(),
  gameMode: z.number().int(),
  averageSpeedKph: z.number().nullable(),
  supersonicPercent: z.number().nullable(),
  timesDemoed: z.number().int().nullable(),
});

export const livePlayerStatsSchema = z.object({
  primaryId: z.string(),
  name: z.string(),
  teamNum: z.number().int(),
  score: z.number().int(),
  goals: z.number().int(),
  shots: z.number().int(),
  assists: z.number().int(),
  saves: z.number().int(),
  touches: z.number().int(),
  demos: z.number().int(),
  boost: z.number().int(),
  updatedAt: isoDate,
  speed: z.number(),
  isSupersonic: z.boolean(),
  hasCar: z.boolean(),
});

export const liveMatchStateInputSchema = z.object({
  matchGuid: z.string().nullable(),
  arena: z.string().nullable(),
  timeSeconds: z.number().int(),
  isOvertime: z.boolean(),
  hasWinner: z.boolean(),
  winner: z.string().nullable(),
  players: z.array(livePlayerStatsSchema),
  updatedAt: isoDate,
});

const uuidRegex = /^[0-9a-f]{8}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{4}-[0-9a-f]{12}$/i;

export const eventLogEntryInputSchema = z.object({
  id: z.string().regex(uuidRegex, "Expected UUID"),
  eventName: z.string(),
  matchGuid: z.string().nullable(),
  receivedAt: isoDate,
  rawJson: z.string(),
});

export const ingestPayloadSchema = z.object({
  observedPlayers: z.array(observedPlayerInputSchema).default([]),
  playerMatchResults: z.array(playerMatchResultInputSchema).default([]),
  events: z.array(eventLogEntryInputSchema).default([]),
  liveMatchState: liveMatchStateInputSchema.nullish(),
  connectionState: z.string(),
  lastError: z.string().nullable().optional(),
});

export type IngestPayload = z.infer<typeof ingestPayloadSchema>;
export type PlayerMatchResultInput = z.infer<typeof playerMatchResultInputSchema>;
export type ObservedPlayerInput = z.infer<typeof observedPlayerInputSchema>;
export type LiveMatchStateInput = z.infer<typeof liveMatchStateInputSchema>;
export type EventLogEntryInput = z.infer<typeof eventLogEntryInputSchema>;
