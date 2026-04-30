namespace RocketStats.Domain.Models;

public sealed record ObservedPlayer(
  string PrimaryId,
  string Name,
  int TeamNum,
  DateTimeOffset FirstSeenAt,
  DateTimeOffset LastSeenAt,
  bool ShowOnDashboard = false);
