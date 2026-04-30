namespace RocketStats.Domain.Models;

public sealed record TeamHeadToHeadRecord(
  IReadOnlyList<string> MyTeamIds,
  IReadOnlyList<string> OpponentIds,
  int GameMode,
  int Wins,
  int Losses);
