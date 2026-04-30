namespace RocketStats.Domain.Models;

public sealed record PlayerMatchResult(
  string PrimaryId,
  string Name,
  string MatchGuid,
  string? Arena,
  DateTimeOffset EndedAt,
  int Score,
  int Goals,
  int Assists,
  int Saves,
  int Shots,
  int Touches,
  int Demos,
  double AverageBoost,
  int TeamNum = 0,
  int? WinningTeam = null,
  int GameMode = 0)
{
  public bool? Won => WinningTeam is null ? null : WinningTeam == TeamNum;
}
