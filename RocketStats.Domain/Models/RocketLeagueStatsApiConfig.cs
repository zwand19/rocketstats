namespace RocketStats.Domain.Models;

public sealed record RocketLeagueStatsApiConfig(
  string Path,
  bool Exists,
  bool Writable,
  double PacketSendRate,
  int Port);
