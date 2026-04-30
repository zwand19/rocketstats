namespace RocketStats.Domain.Models;

public sealed record HeadToHeadRecord(
  string OpponentId,
  string OpponentName,
  int Wins,
  int Losses,
  int GamesPlayed);
