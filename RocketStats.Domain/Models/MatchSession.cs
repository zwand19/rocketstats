namespace RocketStats.Domain.Models;

public sealed record MatchSession(
  string Id,
  DateTimeOffset StartedAt,
  DateTimeOffset EndedAt,
  IReadOnlyList<string> MatchGuids,
  int GameMode = 0);
