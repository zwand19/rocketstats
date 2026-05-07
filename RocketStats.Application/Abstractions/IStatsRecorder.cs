using RocketStats.Domain.Models;

namespace RocketStats.Application.Abstractions;

public interface IStatsRecorder
{
  void RecordObservedPlayers(IReadOnlyList<ObservedPlayer> players);

  void RecordMatchResult(PlayerMatchResult result);

  void RecordEvent(StatsApiEventLogEntry entry);

  void RecordLiveMatchState(LiveMatchState state);

  void RecordConnectionState(StatsApiConnectionState state, string? error);
}
