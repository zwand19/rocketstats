CREATE TABLE "agent_heartbeat" (
	"id" integer PRIMARY KEY DEFAULT 1 NOT NULL,
	"last_ingest_at" timestamp with time zone NOT NULL,
	"connection_state" text NOT NULL,
	"last_error" text
);
--> statement-breakpoint
CREATE TABLE "app_settings" (
	"key" text PRIMARY KEY NOT NULL,
	"value" text
);
--> statement-breakpoint
CREATE TABLE "event_log" (
	"id" uuid PRIMARY KEY DEFAULT gen_random_uuid() NOT NULL,
	"event_name" text NOT NULL,
	"match_guid" text,
	"received_at" timestamp with time zone NOT NULL,
	"raw_json" jsonb NOT NULL
);
--> statement-breakpoint
CREATE TABLE "live_match_state" (
	"id" integer PRIMARY KEY DEFAULT 1 NOT NULL,
	"match_guid" text,
	"arena" text,
	"time_seconds" integer DEFAULT 0 NOT NULL,
	"is_overtime" boolean DEFAULT false NOT NULL,
	"has_winner" boolean DEFAULT false NOT NULL,
	"winner" text,
	"players" jsonb DEFAULT '[]'::jsonb NOT NULL,
	"updated_at" timestamp with time zone NOT NULL
);
--> statement-breakpoint
CREATE TABLE "match_sessions" (
	"id" text PRIMARY KEY NOT NULL,
	"started_at" timestamp with time zone NOT NULL,
	"ended_at" timestamp with time zone NOT NULL,
	"game_mode" integer DEFAULT 0 NOT NULL,
	"match_guids" text[] DEFAULT ARRAY[]::text[] NOT NULL
);
--> statement-breakpoint
CREATE TABLE "observed_players" (
	"primary_id" text PRIMARY KEY NOT NULL,
	"name" text NOT NULL,
	"team_num" integer DEFAULT 0 NOT NULL,
	"first_seen_at" timestamp with time zone NOT NULL,
	"last_seen_at" timestamp with time zone NOT NULL,
	"show_on_dashboard" boolean DEFAULT false NOT NULL
);
--> statement-breakpoint
CREATE TABLE "player_match_results" (
	"match_guid" text NOT NULL,
	"primary_id" text NOT NULL,
	"name" text NOT NULL,
	"arena" text,
	"ended_at" timestamp with time zone NOT NULL,
	"score" integer DEFAULT 0 NOT NULL,
	"goals" integer DEFAULT 0 NOT NULL,
	"assists" integer DEFAULT 0 NOT NULL,
	"saves" integer DEFAULT 0 NOT NULL,
	"shots" integer DEFAULT 0 NOT NULL,
	"touches" integer DEFAULT 0 NOT NULL,
	"demos" integer DEFAULT 0 NOT NULL,
	"average_boost" double precision DEFAULT 0 NOT NULL,
	"team_num" integer DEFAULT 0 NOT NULL,
	"winning_team" integer,
	"game_mode" integer DEFAULT 0 NOT NULL,
	"average_speed_kph" double precision,
	"supersonic_percent" double precision,
	"times_demoed" integer,
	CONSTRAINT "player_match_results_match_guid_primary_id_pk" PRIMARY KEY("match_guid","primary_id")
);
--> statement-breakpoint
CREATE INDEX "event_log_received_at_idx" ON "event_log" USING btree ("received_at" DESC);--> statement-breakpoint
CREATE INDEX "player_match_results_ended_at_idx" ON "player_match_results" USING btree ("ended_at" DESC);--> statement-breakpoint
CREATE INDEX "player_match_results_primary_id_ended_at_idx" ON "player_match_results" USING btree ("primary_id","ended_at" DESC);--> statement-breakpoint
CREATE INDEX "player_match_results_game_mode_ended_at_idx" ON "player_match_results" USING btree ("game_mode","ended_at" DESC);