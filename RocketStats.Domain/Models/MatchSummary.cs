namespace RocketStats.Domain.Models;

public sealed record MatchSummary(
  string MatchGuid,
  DateTimeOffset EndedAt,
  string? Arena,
  int GameMode,
  int? WinningTeam,
  IReadOnlyList<MatchSummaryPlayer> Players);

public sealed record MatchSummaryPlayer(
  string PrimaryId,
  string Name,
  int TeamNum,
  int Score,
  int Goals,
  int Assists,
  int Saves);
