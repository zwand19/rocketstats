using RocketStats.Domain.Models;

namespace RocketStats.Application.Abstractions;

public interface IStatsApiStreamService
{
  StatsApiConnectionState ConnectionState { get; }

  string? LastError { get; }

  Task StartAsync(CancellationToken cancellationToken = default);

  Task StopAsync(CancellationToken cancellationToken = default);

  Task<IReadOnlyList<ObservedPlayer>> GetObservedPlayersAsync(
    CancellationToken cancellationToken = default);

  Task<IReadOnlyList<ObservedPlayer>> GetDashboardPlayersAsync(
    CancellationToken cancellationToken = default);

  Task SetDashboardVisibilityAsync(
    string primaryId,
    bool showOnDashboard,
    CancellationToken cancellationToken = default);

  Task<LiveMatchState?> GetLiveMatchStateAsync(
    CancellationToken cancellationToken = default);

  Task<IReadOnlyList<StatsApiEventLogEntry>> GetRecentEventsAsync(
    CancellationToken cancellationToken = default);

  Task<IReadOnlyList<PlayerAverages>> GetPlayerAveragesAsync(
    IEnumerable<string>? primaryIds = null,
    CancellationToken cancellationToken = default);
}
