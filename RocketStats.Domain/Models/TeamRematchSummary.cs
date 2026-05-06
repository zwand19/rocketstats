namespace RocketStats.Domain.Models;

public sealed record TeamRematchSummary(
  int GameMode,
  int StreakGames,
  int StreakWins,
  int StreakLosses,
  int AllTimeGames,
  int AllTimeWins,
  int AllTimeLosses,
  int SessionGames,
  int SessionWins,
  int SessionLosses);
