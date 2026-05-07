namespace RocketStats.Agent.Options;

public sealed class AgentOptions
{
  public const string SectionName = "Agent";

  public string ServerUrl { get; set; } = "http://localhost:3000";

  public string? IngestApiKey { get; set; }

  public int FlushIntervalSeconds { get; set; } = 10;
}
