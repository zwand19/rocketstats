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

  Task<IReadOnlyList<MatchSession>> GetSessionsAsync(
    CancellationToken cancellationToken = default);

  Task<MatchSession?> GetCurrentSessionAsync(
    CancellationToken cancellationToken = default);

  Task<IReadOnlyList<SessionAverages>> GetCurrentSessionAveragesAsync(
    IEnumerable<string> primaryIds,
    CancellationToken cancellationToken = default);

  Task<IReadOnlyList<HeadToHeadRecord>> GetHeadToHeadAsync(
    string myPrimaryId,
    IEnumerable<string> opponentIds,
    int? gameMode = null,
    CancellationToken cancellationToken = default);

  Task<TeamHeadToHeadRecord?> GetTeamHeadToHeadAsync(
    IReadOnlyList<string> myTeamIds,
    IReadOnlyList<string> opponentIds,
    int gameMode,
    CancellationToken cancellationToken = default);

  Task<MatchesPage> GetMatchesPagedAsync(
    int page,
    int pageSize,
    CancellationToken cancellationToken = default);

  Task DeleteMatchAsync(
    string matchGuid,
    CancellationToken cancellationToken = default);
}

public sealed record MatchesPage(
  IReadOnlyList<MatchSummary> Items,
  int Page,
  int PageSize,
  int TotalCount);
