# Rocket Stats

Rocket Stats is split into a local **agent** that listens to Rocket League's Stats API
WebSocket and a **web app** that displays the data. Matches, sessions, players, and the
live state are persisted in Neon Postgres.

## Structure

- `RocketStats.Agent` — .NET 8 Worker Service that listens on the local TCP/JSON Stats
  API socket, aggregates matches, and POSTs batches to the web server every ~10s
- `RocketStats.Infrastructure` — `StatsApiListener` and `RocketLeagueConfigService`
- `RocketStats.Application` — interfaces (`IRocketLeagueConfigService`,
  `IStatsRecorder`) and `StatsApiOptions`
- `RocketStats.Domain` — match, player, event, and live-state models
- `web/` — Next.js 15 (App Router) app with Drizzle + `@neondatabase/serverless` for
  Postgres, exposing both the ingest endpoint and the dashboard / players / games /
  connection / settings pages

## Prerequisites

- .NET 8 SDK
- Node.js 20+
- A Neon Postgres database connection string

## Setup

```powershell
# 1. Install web dependencies
cd web
npm install

# 2. Set DATABASE_URL (Neon connection string) in web/.env.local
#    DATABASE_URL=postgresql://...
#    INGEST_API_KEY=        # optional Bearer token; leave blank to disable auth

# 3. Run the migration
npm run db:migrate

# 4. From the repo root, build the .NET projects
cd ..
dotnet build RocketStats.sln
```

## Configure Rocket League's Stats API

Set `PacketSendRate` above 0 and pick a port (default `49123`) in
`TAGame\Config\DefaultStatsAPI.ini`. You can either hand-edit the file, or run the
agent's CLI helper:

```powershell
dotnet run --project RocketStats.Agent -- `
  --write-rl-config "<path to>\TAGame\Config\DefaultStatsAPI.ini" 60 49123
```

Restart Rocket League after changing the config.

## Run

In two terminals:

```powershell
# Terminal 1 — web
cd web
npm run dev   # http://localhost:3000
```

```powershell
# Terminal 2 — agent
dotnet run --project RocketStats.Agent
```

The agent reads `RocketStats.Agent/appsettings.json` (and any
`ROCKETSTATS_*` env vars). The defaults assume the web app is on
`http://localhost:3000` and the Stats API is at `ws://localhost:49123`.

Open the web UI at <http://localhost:3000> and start a match or replay in Rocket
League. The agent buffers updates in memory and POSTs a batch every 10 seconds, so
the dashboard lags reality by up to ~10s.

## Configuration reference

`web/.env.local`:

| Variable          | Purpose                                                     |
| ----------------- | ----------------------------------------------------------- |
| `DATABASE_URL`    | Neon Postgres connection string                             |
| `INGEST_API_KEY`  | Optional Bearer token for `POST /api/ingest`                |

`RocketStats.Agent/appsettings.json` (or `ROCKETSTATS_*` env vars):

| Path                              | Default                  | Purpose                                  |
| --------------------------------- | ------------------------ | ---------------------------------------- |
| `StatsApi:WebSocketUrl`           | `ws://localhost:49123`   | Rocket League local Stats API endpoint   |
| `StatsApi:ReconnectDelaySeconds`  | `5`                      | Delay before reconnecting on socket loss |
| `Agent:ServerUrl`                 | `http://localhost:3000`  | Web server base URL                      |
| `Agent:IngestApiKey`              | (none)                   | Bearer token sent on every ingest POST   |
| `Agent:FlushIntervalSeconds`      | `10`                     | How often to POST a batch                |

## How it works

```
Rocket League ──TCP/JSON──▶ RocketStats.Agent ──HTTP batch──▶ Next.js (Vercel-ready)
                                                                  │
                                                                  ▼
                                                          Neon Postgres
                                                                  │
                                                                  ▼
                                                              Browser
```

The agent owns everything that needs the local machine: the TCP listener, JSON
parsing, in-flight match aggregation, and `.ini` config writes. The server owns
persistence (`/api/ingest` is idempotent) and reads (per-mode averages, sessions,
head-to-head, paged match history). The browser polls `/api/dashboard`,
`/api/players`, etc. every two seconds.
