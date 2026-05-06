using RocketStats.Domain.Models;

namespace RocketStats.Application.Abstractions;

public interface IRocketLeagueConfigService
{
  Task<string?> GetSavedPathAsync(CancellationToken cancellationToken = default);

  Task SetSavedPathAsync(string? path, CancellationToken cancellationToken = default);

  IReadOnlyList<string> GetCandidatePaths();

  Task<RocketLeagueStatsApiConfig?> ReadAsync(CancellationToken cancellationToken = default);

  Task<RocketLeagueStatsApiConfig?> ReadAsync(string path, CancellationToken cancellationToken = default);

  Task<ConfigWriteResult> WriteAsync(
    string path,
    double packetSendRate,
    int port,
    CancellationToken cancellationToken = default);
}

public sealed record ConfigWriteResult(bool Success, string? Error);
