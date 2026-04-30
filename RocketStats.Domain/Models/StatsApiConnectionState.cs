namespace RocketStats.Domain.Models;

public enum StatsApiConnectionState
{
  Stopped,
  Connecting,
  Connected,
  Reconnecting,
  Faulted
}
