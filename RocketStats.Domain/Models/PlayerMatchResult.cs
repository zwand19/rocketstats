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
  double AverageBoost);
