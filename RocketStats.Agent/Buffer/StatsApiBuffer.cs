using RocketStats.Agent.Ingest;
using RocketStats.Application.Abstractions;
using RocketStats.Domain.Models;

namespace RocketStats.Agent.Buffer;

public sealed class StatsApiBuffer : IStatsRecorder
{
  private readonly object _lock = new();
  private readonly Dictionary<string, ObservedPlayerInput> _observedPlayers =
    new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<(string MatchGuid, string PrimaryId), PlayerMatchResultInput> _playerMatchResults =
    new();
  private readonly Dictionary<string, EventLogEntryInput> _events = new();
  private LiveMatchStateInput? _liveMatchState;
  private StatsApiConnectionState _connectionState = StatsApiConnectionState.Stopped;
  private string? _lastError;

  public void RecordObservedPlayers(IReadOnlyList<ObservedPlayer> players)
  {
    lock (_lock)
    {
      foreach (var player in players)
      {
        if (_observedPlayers.TryGetValue(player.PrimaryId, out var existing))
        {
          var firstSeen = existing.FirstSeenAt < player.FirstSeenAt ? existing.FirstSeenAt : player.FirstSeenAt;
          var lastSeen = existing.LastSeenAt > player.LastSeenAt ? existing.LastSeenAt : player.LastSeenAt;
          _observedPlayers[player.PrimaryId] = existing with
          {
            Name = player.Name,
            TeamNum = player.TeamNum,
            FirstSeenAt = firstSeen,
            LastSeenAt = lastSeen,
          };
        }
        else
        {
          _observedPlayers[player.PrimaryId] = new ObservedPlayerInput(
            player.PrimaryId,
            player.Name,
            player.TeamNum,
            player.FirstSeenAt,
            player.LastSeenAt);
        }
      }
    }
  }

  public void RecordMatchResult(PlayerMatchResult result)
  {
    var input = new PlayerMatchResultInput(
      result.MatchGuid,
      result.PrimaryId,
      result.Name,
      result.Arena,
      result.EndedAt,
      result.Score,
      result.Goals,
      result.Assists,
      result.Saves,
      result.Shots,
      result.Touches,
      result.Demos,
      result.AverageBoost,
      result.TeamNum,
      result.WinningTeam,
      result.GameMode,
      result.AverageSpeedKph,
      result.SupersonicPercent,
      result.TimesDemoed);

    lock (_lock)
    {
      _playerMatchResults[(result.MatchGuid, result.PrimaryId)] = input;
    }
  }

  public void RecordEvent(StatsApiEventLogEntry entry)
  {
    var input = new EventLogEntryInput(
      entry.Id,
      entry.EventName,
      entry.MatchGuid,
      entry.ReceivedAt,
      entry.RawJson);

    lock (_lock)
    {
      _events[entry.Id] = input;
    }
  }

  public void RecordLiveMatchState(LiveMatchState state)
  {
    var input = new LiveMatchStateInput(
      state.MatchGuid,
      state.Arena,
      state.TimeSeconds,
      state.IsOvertime,
      state.HasWinner,
      state.Winner,
      state.Players.Select(p => new LivePlayerInput(
        p.PrimaryId,
        p.Name,
        p.TeamNum,
        p.Score,
        p.Goals,
        p.Shots,
        p.Assists,
        p.Saves,
        p.Touches,
        p.Demos,
        p.Boost,
        p.UpdatedAt,
        p.Speed,
        p.IsSupersonic,
        p.HasCar)).ToArray(),
      state.UpdatedAt);

    lock (_lock)
    {
      _liveMatchState = input;
    }
  }

  public void RecordConnectionState(StatsApiConnectionState state, string? error)
  {
    lock (_lock)
    {
      _connectionState = state;
      _lastError = error;
    }
  }

  public IngestSnapshot Drain()
  {
    lock (_lock)
    {
      var snapshot = new IngestSnapshot(
        _observedPlayers.Values.ToArray(),
        _playerMatchResults.Values.ToArray(),
        _events.Values.ToArray(),
        _liveMatchState,
        _connectionState,
        _lastError);

      _observedPlayers.Clear();
      _playerMatchResults.Clear();
      _events.Clear();
      _liveMatchState = null;

      return snapshot;
    }
  }

  public void Restore(IngestSnapshot snapshot)
  {
    lock (_lock)
    {
      foreach (var player in snapshot.ObservedPlayers)
      {
        if (_observedPlayers.TryGetValue(player.PrimaryId, out var existing))
        {
          var firstSeen = existing.FirstSeenAt < player.FirstSeenAt ? existing.FirstSeenAt : player.FirstSeenAt;
          var lastSeen = existing.LastSeenAt > player.LastSeenAt ? existing.LastSeenAt : player.LastSeenAt;
          _observedPlayers[player.PrimaryId] = existing with
          {
            FirstSeenAt = firstSeen,
            LastSeenAt = lastSeen,
          };
        }
        else
        {
          _observedPlayers[player.PrimaryId] = player;
        }
      }

      foreach (var result in snapshot.PlayerMatchResults)
      {
        _playerMatchResults.TryAdd((result.MatchGuid, result.PrimaryId), result);
      }

      foreach (var entry in snapshot.Events)
      {
        _events.TryAdd(entry.Id, entry);
      }

      _liveMatchState ??= snapshot.LiveMatchState;
    }
  }
}

public sealed record IngestSnapshot(
  IReadOnlyList<ObservedPlayerInput> ObservedPlayers,
  IReadOnlyList<PlayerMatchResultInput> PlayerMatchResults,
  IReadOnlyList<EventLogEntryInput> Events,
  LiveMatchStateInput? LiveMatchState,
  StatsApiConnectionState ConnectionState,
  string? LastError)
{
  public bool IsEmpty =>
    ObservedPlayers.Count == 0 &&
    PlayerMatchResults.Count == 0 &&
    Events.Count == 0 &&
    LiveMatchState is null;
}
