namespace RocketStats.Domain.Models;

public sealed record PlayerAverages(
  string PrimaryId,
  string Name,
  int GamesPlayed,
  double Score,
  double Goals,
  double Assists,
  double Saves,
  double Shots,
  double Touches,
  double Demos,
  double Boost);
