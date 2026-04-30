# Rocket Stats

Rocket Stats is a .NET 8 Blazor Hybrid MAUI desktop companion app for Rocket League's local Stats API WebSocket.

## Structure

- `RocketStats.App`: MAUI Blazor desktop UI
- `RocketStats.Application`: service interfaces, DTOs, and options
- `RocketStats.Infrastructure`: local Stats API listener, JSON storage, API client scaffold, and background monitor stub
- `RocketStats.Domain`: core player, live match, event log, rank, stat, match, preference, and recent-player models

## Configuration

Enable the Stats API before launching Rocket League by editing `TAGame\Config\DefaultStatsAPI.ini`:

```ini
PacketSendRate=10
Port=49123
```

The app connects to the local WebSocket configured in `RocketStats.App/appsettings.json`.

```json
{
  "StatsApi": {
    "WebSocketUrl": "ws://localhost:49123",
    "StoredEventLimit": 500
  }
}
```

The app collects every player seen in `UpdateState` messages. Players default to hidden on the dashboard until enabled on the Players screen.

Environment-specific files such as `appsettings.Development.json` are supported, along with environment variables prefixed with `ROCKETSTATS_`.

## Run

Install the .NET MAUI workload, then run the Windows target:

```powershell
dotnet workload install maui
dotnet build RocketStats.sln
dotnet run --project RocketStats.App/RocketStats.App.csproj -f net8.0-windows10.0.19041.0
```

## How to Use the App

1. Enable Rocket League's Stats API in `TAGame\Config\DefaultStatsAPI.ini`.
2. Restart Rocket League after changing the config.
3. Launch Rocket Stats.
4. Start a private, casual, ranked, or replay match in Rocket League.
5. Leave Rocket Stats open on Dashboard or Players. The listener starts automatically and connects to `ws://localhost:49123`.
6. Open Players and toggle `Show` for any observed players you want on the Dashboard.

You should not need to click `Start listener` during normal use. That button is now only a manual retry/control if the listener was stopped or faulted. If nothing appears, check:

- Rocket League was restarted after enabling `PacketSendRate`.
- The configured port matches `RocketStats.App/appsettings.json`.
- A match or replay is active; players are collected from `UpdateState` messages.
- The Dashboard connection card is `Connected` or `Connecting`.

## Logging

In Debug builds, logs are sent to both the debugger output and the console. Rider should show listener startup, connection, stop, and incoming Stats API event messages in the run/debug console.
