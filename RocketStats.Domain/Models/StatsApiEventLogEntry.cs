namespace RocketStats.Domain.Models;

public sealed record StatsApiEventLogEntry(
  string Id,
  string EventName,
  string? MatchGuid,
  DateTimeOffset ReceivedAt,
  string RawJson);
