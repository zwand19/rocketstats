using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RocketStats.Agent.Buffer;
using RocketStats.Agent.Ingest;
using RocketStats.Agent.Options;

namespace RocketStats.Agent.Workers;

public sealed class BatchSenderWorker : BackgroundService
{
  private readonly StatsApiBuffer _buffer;
  private readonly IngestClient _client;
  private readonly AgentOptions _options;
  private readonly ILogger<BatchSenderWorker> _logger;

  public BatchSenderWorker(
    StatsApiBuffer buffer,
    IngestClient client,
    IOptions<AgentOptions> options,
    ILogger<BatchSenderWorker> logger)
  {
    _buffer = buffer;
    _client = client;
    _options = options.Value;
    _logger = logger;
  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var interval = TimeSpan.FromSeconds(Math.Max(1, _options.FlushIntervalSeconds));
    using var timer = new PeriodicTimer(interval);

    _logger.LogInformation(
      "Batch sender flushing to {ServerUrl} every {IntervalSeconds}s.",
      _options.ServerUrl,
      interval.TotalSeconds);

    while (await SafeWaitForNextTickAsync(timer, stoppingToken))
    {
      await FlushAsync(stoppingToken);
    }

    await FlushAsync(CancellationToken.None);
  }

  private async Task FlushAsync(CancellationToken cancellationToken)
  {
    var snapshot = _buffer.Drain();

    var payload = new IngestPayload(
      snapshot.ObservedPlayers,
      snapshot.PlayerMatchResults,
      snapshot.Events,
      snapshot.LiveMatchState,
      snapshot.ConnectionState.ToString(),
      snapshot.LastError);

    var ok = await _client.SendAsync(payload, cancellationToken);

    if (!ok)
    {
      _logger.LogWarning(
        "Batch send failed; retaining buffered data ({ObservedPlayers} players, {Matches} match results, {Events} events).",
        snapshot.ObservedPlayers.Count,
        snapshot.PlayerMatchResults.Count,
        snapshot.Events.Count);
      _buffer.Restore(snapshot);
    }
  }

  private static async Task<bool> SafeWaitForNextTickAsync(PeriodicTimer timer, CancellationToken cancellationToken)
  {
    try
    {
      return await timer.WaitForNextTickAsync(cancellationToken);
    }
    catch (OperationCanceledException)
    {
      return false;
    }
  }
}
