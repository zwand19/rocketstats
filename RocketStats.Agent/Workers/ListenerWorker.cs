using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RocketStats.Infrastructure.Services;

namespace RocketStats.Agent.Workers;

public sealed class ListenerWorker : BackgroundService
{
  private readonly StatsApiListener _listener;
  private readonly ILogger<ListenerWorker> _logger;

  public ListenerWorker(StatsApiListener listener, ILogger<ListenerWorker> logger)
  {
    _listener = listener;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    _logger.LogInformation("Starting Stats API listener.");
    await _listener.StartAsync(stoppingToken);

    try
    {
      await Task.Delay(Timeout.Infinite, stoppingToken);
    }
    catch (OperationCanceledException)
    {
    }

    _logger.LogInformation("Stopping Stats API listener.");
    await _listener.StopAsync(CancellationToken.None);
  }
}
