namespace RocketStats.Application.Options;

public sealed class StatsApiOptions
{
  public const string SectionName = "StatsApi";

  public string WebSocketUrl { get; set; } = "ws://localhost:49123";

  public int StoredEventLimit { get; set; } = 5000;

  public int StoredMatchLimit { get; set; } = 500;
}
