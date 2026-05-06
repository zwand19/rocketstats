namespace RocketStats.Domain.Models;

public sealed record LivePlayerStats(
  string PrimaryId,
  string Name,
  int TeamNum,
  int Score,
  int Goals,
  int Shots,
  int Assists,
  int Saves,
  int Touches,
  int Demos,
  int Boost,
  DateTimeOffset UpdatedAt,
  double Speed = 0d,
  bool IsSupersonic = false,
  bool HasCar = false);
