namespace RocketStats.Domain.Models;

public sealed record SessionAverages(
  string SessionId,
  string PrimaryId,
  string Name,
  int GamesPlayed,
  int Wins,
  int Losses,
  double Score,
  double Goals,
  double Assists,
  double Saves,
  double Shots,
  double Touches,
  double Demos,
  double Boost,
  int MvpCount = 0,
  double? AverageSpeedKph = null,
  double? SupersonicPercent = null,
  double? Demoed = null)
{
  public double MvpPercent => GamesPlayed == 0 ? 0d : (double)MvpCount / GamesPlayed * 100d;
}
