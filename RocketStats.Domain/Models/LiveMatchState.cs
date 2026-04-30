namespace RocketStats.Domain.Models;

public sealed record LiveMatchState(
  string? MatchGuid,
  string? Arena,
  int TimeSeconds,
  bool IsOvertime,
  bool HasWinner,
  string? Winner,
  IReadOnlyList<LivePlayerStats> Players,
  DateTimeOffset UpdatedAt);
