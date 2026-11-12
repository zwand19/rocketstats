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
  private int _consecutiveFailures;

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

    var result = await _client.SendAsync(payload, cancellationToken);

    switch (result)
    {
      case IngestResult.Sent:
        if (_consecutiveFailures > 0)
        {
          _logger.LogInformation("Batch send recovered after {Failures} failed attempts.", _consecutiveFailures);
          _consecutiveFailures = 0;
        }

        break;

      case IngestResult.Rejected:
        // Retrying a payload the server refuses would wedge the agent forever, so let it go.
        _logger.LogWarning(
          "Server rejected the batch; discarding it ({Matches} match results, {Events} events).",
          snapshot.PlayerMatchResults.Count,
          snapshot.Events.Count);
        break;

      default:
        _buffer.Restore(snapshot);

        // Warn when an outage starts and then every ~5 minutes, rather than on every tick.
        if (_consecutiveFailures++ % 30 == 0)
        {
          _logger.LogWarning(
            "Batch send to {ServerUrl} is failing; buffered data is kept and will be retried.",
            _options.ServerUrl);
        }

        break;
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
