namespace RocketStats.Domain.Models;

public sealed record PlayerStreak(string PrimaryId, int Count, bool IsWinning)
{
  public string Display => $"{(IsWinning ? "W" : "L")}{Count}";
}
