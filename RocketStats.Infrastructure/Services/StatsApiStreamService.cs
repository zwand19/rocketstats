using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RocketStats.Application.Abstractions;
using RocketStats.Application.Options;
using RocketStats.Domain.Models;

namespace RocketStats.Infrastructure.Services;

public sealed class StatsApiStreamService : IStatsApiStreamService, IAsyncDisposable
{
  private const string ObservedPlayersKey = "stats-api-observed-players";
  private const string DashboardPlayerIdsKey = "stats-api-dashboard-player-ids";
  private const string MePrimaryIdKey = "stats-api-me-primary-id";
  private const string EventLogKey = "stats-api-event-log";
  private const string MatchResultsKey = "stats-api-match-results";
  private const string SessionsKey = "stats-api-sessions";

  private const int RecentlyFlushedCapacity = 16;

  private static readonly TimeSpan SessionGap = TimeSpan.FromHours(1);

  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly SemaphoreSlim _gate = new(1, 1);
  private readonly SemaphoreSlim _sessionGate = new(1, 1);
  private readonly Dictionary<string, MatchAggregate> _matchAggregates = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> _recentlyFlushedSet = new(StringComparer.OrdinalIgnoreCase);
  private readonly Queue<string> _recentlyFlushedQueue = new();
  private readonly IAppendOnlyStorageService _appendStorage;
  private readonly ILocalStorageService _localStorage;
  private readonly ILogger<StatsApiStreamService> _logger;
  private readonly StatsApiOptions _options;

  private CancellationTokenSource? _listenerCancellation;
  private Task? _listenerTask;
  private LiveMatchState? _liveMatchState;
  private string? _activeMatchGuid;

  public StatsApiStreamService(
    ILocalStorageService localStorage,
    IAppendOnlyStorageService appendStorage,
    IOptions<StatsApiOptions> options,
    ILogger<StatsApiStreamService> logger)
  {
    _localStorage = localStorage;
    _appendStorage = appendStorage;
    _options = options.Value;
    _logger = logger;
  }

  public StatsApiConnectionState ConnectionState { get; private set; } = StatsApiConnectionState.Stopped;

  public string? LastError { get; private set; }

  public Task StartAsync(CancellationToken cancellationToken = default)
  {
    if (_listenerTask is { IsCompleted: false })
    {
      _logger.LogInformation("Stats API listener is already running with state {ConnectionState}.", ConnectionState);
      return Task.CompletedTask;
    }

    LastError = null;
    _listenerCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
    _listenerTask = Task.Run(() => ListenAsync(_listenerCancellation.Token), CancellationToken.None);
    _logger.LogInformation("Stats API listener starting for {Endpoint}.", _options.WebSocketUrl);

    return Task.CompletedTask;
  }

  public async Task StopAsync(CancellationToken cancellationToken = default)
  {
    if (_listenerCancellation is null)
    {
      ConnectionState = StatsApiConnectionState.Stopped;
      return;
    }

    await _listenerCancellation.CancelAsync();

    if (_listenerTask is not null)
    {
      try
      {
        await _listenerTask.WaitAsync(TimeSpan.FromSeconds(2), cancellationToken);
      }
      catch (TimeoutException)
      {
        _logger.LogWarning("Stats API listener did not stop within the expected timeout.");
      }
      catch (OperationCanceledException)
      {
      }
    }

    _listenerCancellation.Dispose();
    _listenerCancellation = null;
    _listenerTask = null;
    ConnectionState = StatsApiConnectionState.Stopped;
    _logger.LogInformation("Stats API listener stopped.");
  }

  public async Task<IReadOnlyList<ObservedPlayer>> GetObservedPlayersAsync(
    CancellationToken cancellationToken = default)
  {
    var players = await ReadObservedPlayersAsync(cancellationToken);
    var dashboardPlayerIds = await ReadDashboardPlayerIdsAsync(cancellationToken);
    var mePrimaryId = await ReadMePrimaryIdAsync(cancellationToken);

    return ApplyPlayerFlags(players, dashboardPlayerIds, mePrimaryId);
  }

  public async Task<IReadOnlyList<ObservedPlayer>> GetDashboardPlayersAsync(
    CancellationToken cancellationToken = default)
  {
    var players = await GetObservedPlayersAsync(cancellationToken);
    var dashboardPlayers = players
      .Where(player => player.ShowOnDashboard)
      .OrderByDescending(player => player.LastSeenAt)
      .ToArray();

    _logger.LogInformation(
      "Resolved {DashboardPlayerCount} dashboard players from {ObservedPlayerCount} observed players.",
      dashboardPlayers.Length,
      players.Count);

    return dashboardPlayers;
  }

  public async Task SetDashboardVisibilityAsync(
    string primaryId,
    bool showOnDashboard,
    CancellationToken cancellationToken = default)
  {
    _logger.LogInformation(
      "Setting dashboard visibility for {PrimaryId} to {ShowOnDashboard}.",
      primaryId,
      showOnDashboard);

    await _gate.WaitAsync(cancellationToken);

    try
    {
      var dashboardPlayerIds = await ReadDashboardPlayerIdsAsync(cancellationToken);
      var hadDashboardPlayerId = dashboardPlayerIds.Contains(primaryId);

      if (showOnDashboard)
      {
        dashboardPlayerIds.Add(primaryId);
      }
      else
      {
        dashboardPlayerIds.Remove(primaryId);
      }

      await _localStorage.WriteAsync(DashboardPlayerIdsKey, dashboardPlayerIds.ToArray(), cancellationToken);

      var players = await ReadObservedPlayersAsync(cancellationToken);
      var matchingPlayerCount = players.Count(player =>
        string.Equals(player.PrimaryId, primaryId, StringComparison.OrdinalIgnoreCase));
      var updated = players
        .Select(player => string.Equals(player.PrimaryId, primaryId, StringComparison.OrdinalIgnoreCase)
          ? player with { ShowOnDashboard = showOnDashboard }
          : player)
        .ToArray();

      await _localStorage.WriteAsync(ObservedPlayersKey, updated, cancellationToken);

      if (matchingPlayerCount == 0)
      {
        _logger.LogWarning(
          "Dashboard visibility was persisted for {PrimaryId}, but no observed player currently matches that id.",
          primaryId);
      }

      _logger.LogInformation(
        "Dashboard visibility persisted for {PrimaryId}. WasSelected={WasSelected}; IsSelected={IsSelected}; SelectedIdCount={SelectedIdCount}; MatchingPlayerCount={MatchingPlayerCount}.",
        primaryId,
        hadDashboardPlayerId,
        dashboardPlayerIds.Contains(primaryId),
        dashboardPlayerIds.Count,
        matchingPlayerCount);
    }
    finally
    {
      _gate.Release();
    }
  }

  public async Task SetMeAsync(
    string? primaryId,
    CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);

    try
    {
      if (string.IsNullOrWhiteSpace(primaryId))
      {
        await _localStorage.RemoveAsync(MePrimaryIdKey, cancellationToken);
        _logger.LogInformation("Cleared 'me' player.");
      }
      else
      {
        await _localStorage.WriteAsync(MePrimaryIdKey, primaryId, cancellationToken);
        _logger.LogInformation("Set 'me' player to {PrimaryId}.", primaryId);
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  public async Task<PlayerStreak?> GetCurrentStreakAsync(
    string primaryId,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(primaryId))
    {
      return null;
    }

    var results = await _appendStorage.ReadAllAsync<PlayerMatchResult>(MatchResultsKey, cancellationToken);
    var ordered = results
      .Where(result =>
        string.Equals(result.PrimaryId, primaryId, StringComparison.OrdinalIgnoreCase) &&
        result.Won.HasValue)
      .OrderByDescending(result => result.EndedAt)
      .ToArray();

    if (ordered.Length == 0)
    {
      return null;
    }

    var isWinning = ordered[0].Won!.Value;
    var count = 0;

    foreach (var result in ordered)
    {
      if (result.Won == isWinning)
      {
        count++;
      }
      else
      {
        break;
      }
    }

    return new PlayerStreak(primaryId, count, isWinning);
  }

  public Task<LiveMatchState?> GetLiveMatchStateAsync(
    CancellationToken cancellationToken = default) =>
    Task.FromResult(_liveMatchState);

  public async Task<IReadOnlyList<StatsApiEventLogEntry>> GetRecentEventsAsync(
    CancellationToken cancellationToken = default)
  {
    var events = await _appendStorage.ReadAllAsync<StatsApiEventLogEntry>(EventLogKey, cancellationToken);

    return events
      .Where(entry => !string.Equals(entry.EventName, "UpdateState", StringComparison.OrdinalIgnoreCase))
      .OrderByDescending(entry => entry.ReceivedAt)
      .ToArray();
  }

  public async Task<IReadOnlyList<PlayerAverages>> GetPlayerAveragesAsync(
    IEnumerable<string>? primaryIds = null,
    int? gameMode = null,
    CancellationToken cancellationToken = default)
  {
    var allResults = await _appendStorage.ReadAllAsync<PlayerMatchResult>(MatchResultsKey, cancellationToken);
    var modeFiltered = gameMode is null
      ? allResults
      : allResults.Where(result => result.GameMode == gameMode).ToArray();
    var maxScoreByMatch = ComputeMaxScoreByMatch(modeFiltered);
    var filter = primaryIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var filtered = filter is null
      ? modeFiltered
      : modeFiltered.Where(result => filter.Contains(result.PrimaryId)).ToArray();

    return filtered
      .GroupBy(result => result.PrimaryId, StringComparer.OrdinalIgnoreCase)
      .Select(group =>
      {
        var games = group.Count();
        var latestName = group.OrderByDescending(result => result.EndedAt).First().Name;
        var mvps = group.Count(result => IsMvp(result, maxScoreByMatch));

        return new PlayerAverages(
          group.Key,
          latestName,
          games,
          group.Average(result => (double)result.Score),
          group.Average(result => (double)result.Goals),
          group.Average(result => (double)result.Assists),
          group.Average(result => (double)result.Saves),
          group.Average(result => (double)result.Shots),
          group.Average(result => (double)result.Touches),
          group.Average(result => (double)result.Demos),
          group.Average(result => result.AverageBoost),
          mvps,
          AverageNullable(group, result => result.AverageSpeedKph),
          AverageNullable(group, result => result.SupersonicPercent),
          AverageNullable(group, result => (double?)result.TimesDemoed));
      })
      .OrderByDescending(averages => averages.Goals)
      .ToArray();
  }

  private static double? AverageNullable<T>(IEnumerable<T> source, Func<T, double?> selector)
  {
    var values = source.Select(selector).Where(value => value.HasValue).Select(value => value!.Value).ToArray();
    return values.Length == 0 ? null : values.Average();
  }

  private static Dictionary<string, int> ComputeMaxScoreByMatch(IReadOnlyList<PlayerMatchResult> results) =>
    results
      .GroupBy(result => result.MatchGuid, StringComparer.OrdinalIgnoreCase)
      .ToDictionary(
        group => group.Key,
        group => group.Max(result => result.Score),
        StringComparer.OrdinalIgnoreCase);

  private static bool IsMvp(PlayerMatchResult result, IReadOnlyDictionary<string, int> maxScoreByMatch) =>
    maxScoreByMatch.TryGetValue(result.MatchGuid, out var max) && result.Score == max;

  public async Task<IReadOnlyList<MatchSession>> GetSessionsAsync(
    CancellationToken cancellationToken = default)
  {
    var sessions = await _appendStorage.ReadAllAsync<MatchSession>(SessionsKey, cancellationToken);

    return sessions
      .OrderByDescending(session => session.EndedAt)
      .ToArray();
  }

  public async Task<MatchSession?> GetCurrentSessionAsync(
    CancellationToken cancellationToken = default)
  {
    var sessions = await _appendStorage.ReadAllAsync<MatchSession>(SessionsKey, cancellationToken);

    if (sessions.Count == 0)
    {
      return null;
    }

    return sessions[^1];
  }

  public async Task<IReadOnlyList<SessionAverages>> GetCurrentSessionAveragesAsync(
    IEnumerable<string> primaryIds,
    CancellationToken cancellationToken = default)
  {
    var session = await GetCurrentSessionAsync(cancellationToken);

    if (session is null)
    {
      return [];
    }

    var matchGuidSet = session.MatchGuids.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var primaryIdSet = primaryIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var allResults = await _appendStorage.ReadAllAsync<PlayerMatchResult>(MatchResultsKey, cancellationToken);
    var sessionResults = allResults
      .Where(result => matchGuidSet.Contains(result.MatchGuid))
      .ToArray();
    var maxScoreByMatch = ComputeMaxScoreByMatch(sessionResults);
    var inSession = sessionResults
      .Where(result => primaryIdSet.Contains(result.PrimaryId))
      .ToArray();

    return inSession
      .GroupBy(result => result.PrimaryId, StringComparer.OrdinalIgnoreCase)
      .Select(group =>
      {
        var games = group.Count();
        var latest = group.OrderByDescending(result => result.EndedAt).First();
        var wins = group.Count(result => result.Won == true);
        var losses = group.Count(result => result.Won == false);
        var mvps = group.Count(result => IsMvp(result, maxScoreByMatch));

        return new SessionAverages(
          session.Id,
          group.Key,
          latest.Name,
          games,
          wins,
          losses,
          group.Average(result => (double)result.Score),
          group.Average(result => (double)result.Goals),
          group.Average(result => (double)result.Assists),
          group.Average(result => (double)result.Saves),
          group.Average(result => (double)result.Shots),
          group.Average(result => (double)result.Touches),
          group.Average(result => (double)result.Demos),
          group.Average(result => result.AverageBoost),
          mvps,
          AverageNullable(group, result => result.AverageSpeedKph),
          AverageNullable(group, result => result.SupersonicPercent),
          AverageNullable(group, result => (double?)result.TimesDemoed));
      })
      .ToArray();
  }

  public async Task<IReadOnlyList<HeadToHeadRecord>> GetHeadToHeadAsync(
    string myPrimaryId,
    IEnumerable<string> opponentIds,
    int? gameMode = null,
    CancellationToken cancellationToken = default)
  {
    var opponentSet = opponentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);

    if (string.IsNullOrWhiteSpace(myPrimaryId) || opponentSet.Count == 0)
    {
      return [];
    }

    var allResults = await _appendStorage.ReadAllAsync<PlayerMatchResult>(MatchResultsKey, cancellationToken);
    var matchGroups = allResults
      .Where(result => result.WinningTeam is not null && (gameMode is null || result.GameMode == gameMode))
      .GroupBy(result => result.MatchGuid, StringComparer.OrdinalIgnoreCase);

    var records = opponentSet.ToDictionary(
      id => id,
      id => (Wins: 0, Losses: 0, Games: 0, Name: id),
      StringComparer.OrdinalIgnoreCase);

    foreach (var match in matchGroups)
    {
      var me = match.FirstOrDefault(player =>
        string.Equals(player.PrimaryId, myPrimaryId, StringComparison.OrdinalIgnoreCase));

      if (me is null)
      {
        continue;
      }

      var iWon = me.Won == true;

      foreach (var opponent in match)
      {
        if (opponent.TeamNum == me.TeamNum)
        {
          continue;
        }

        if (!opponentSet.Contains(opponent.PrimaryId))
        {
          continue;
        }

        var current = records[opponent.PrimaryId];
        records[opponent.PrimaryId] = (
          current.Wins + (iWon ? 1 : 0),
          current.Losses + (iWon ? 0 : 1),
          current.Games + 1,
          opponent.Name);
      }
    }

    return records
      .Where(entry => entry.Value.Games > 0)
      .Select(entry => new HeadToHeadRecord(
        entry.Key,
        entry.Value.Name,
        entry.Value.Wins,
        entry.Value.Losses,
        entry.Value.Games))
      .ToArray();
  }

  public async Task<TeamHeadToHeadRecord?> GetTeamHeadToHeadAsync(
    IReadOnlyList<string> myTeamIds,
    IReadOnlyList<string> opponentIds,
    int gameMode,
    CancellationToken cancellationToken = default)
  {
    if (myTeamIds.Count == 0 || opponentIds.Count == 0)
    {
      return null;
    }

    var myTeamSet = myTeamIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var opponentSet = opponentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var allResults = await _appendStorage.ReadAllAsync<PlayerMatchResult>(MatchResultsKey, cancellationToken);

    var wins = 0;
    var losses = 0;

    foreach (var match in allResults
      .Where(result => result.GameMode == gameMode && result.WinningTeam is not null)
      .GroupBy(result => result.MatchGuid, StringComparer.OrdinalIgnoreCase))
    {
      var teamGroups = match
        .GroupBy(result => result.TeamNum)
        .Select(group => new
        {
          group.Key,
          Players = group.Select(player => player.PrimaryId).ToHashSet(StringComparer.OrdinalIgnoreCase),
          Won = group.First().Won == true
        })
        .ToArray();

      if (teamGroups.Length != 2)
      {
        continue;
      }

      var myTeam = teamGroups.FirstOrDefault(team => team.Players.SetEquals(myTeamSet));
      var opponentTeam = teamGroups.FirstOrDefault(team => team.Players.SetEquals(opponentSet));

      if (myTeam is null || opponentTeam is null || myTeam.Key == opponentTeam.Key)
      {
        continue;
      }

      if (myTeam.Won)
      {
        wins++;
      }
      else
      {
        losses++;
      }
    }

    if (wins + losses == 0)
    {
      return null;
    }

    return new TeamHeadToHeadRecord(myTeamIds, opponentIds, gameMode, wins, losses);
  }

  public async Task<TeamRematchSummary?> GetTeamRematchSummaryAsync(
    IReadOnlyList<string> myTeamIds,
    IReadOnlyList<string> opponentIds,
    int gameMode,
    CancellationToken cancellationToken = default)
  {
    if (myTeamIds.Count == 0 || opponentIds.Count == 0)
    {
      return null;
    }

    var myTeamSet = myTeamIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var opponentSet = opponentIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var allResults = await _appendStorage.ReadAllAsync<PlayerMatchResult>(MatchResultsKey, cancellationToken);
    var session = await GetCurrentSessionAsync(cancellationToken);
    var sessionGuids = session?.MatchGuids.ToHashSet(StringComparer.OrdinalIgnoreCase)
      ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    var orderedMatches = allResults
      .Where(result => result.GameMode == gameMode && result.WinningTeam is not null)
      .GroupBy(result => result.MatchGuid, StringComparer.OrdinalIgnoreCase)
      .Select(group =>
      {
        var teamGroups = group
          .GroupBy(result => result.TeamNum)
          .Select(team => new
          {
            Players = team.Select(player => player.PrimaryId).ToHashSet(StringComparer.OrdinalIgnoreCase),
            Won = team.First().Won == true
          })
          .ToArray();

        return new
        {
          MatchGuid = group.Key,
          EndedAt = group.Max(player => player.EndedAt),
          TeamGroups = teamGroups
        };
      })
      .OrderByDescending(match => match.EndedAt)
      .ToArray();

    var allTimeWins = 0;
    var allTimeLosses = 0;
    var sessionWins = 0;
    var sessionLosses = 0;
    var streakWins = 0;
    var streakLosses = 0;
    var streakActive = true;

    foreach (var match in orderedMatches)
    {
      if (match.TeamGroups.Length != 2)
      {
        streakActive = false;
        continue;
      }

      var myTeam = match.TeamGroups.FirstOrDefault(team => team.Players.SetEquals(myTeamSet));
      var opponentTeam = match.TeamGroups.FirstOrDefault(team => team.Players.SetEquals(opponentSet));
      var matched = myTeam is not null && opponentTeam is not null;

      if (matched)
      {
        if (myTeam!.Won)
        {
          allTimeWins++;
        }
        else
        {
          allTimeLosses++;
        }

        if (sessionGuids.Contains(match.MatchGuid))
        {
          if (myTeam.Won)
          {
            sessionWins++;
          }
          else
          {
            sessionLosses++;
          }
        }

        if (streakActive)
        {
          if (myTeam.Won)
          {
            streakWins++;
          }
          else
          {
            streakLosses++;
          }
        }
      }
      else
      {
        streakActive = false;
      }
    }

    var allTimeGames = allTimeWins + allTimeLosses;

    if (allTimeGames == 0)
    {
      return null;
    }

    return new TeamRematchSummary(
      gameMode,
      streakWins + streakLosses,
      streakWins,
      streakLosses,
      allTimeGames,
      allTimeWins,
      allTimeLosses,
      sessionWins + sessionLosses,
      sessionWins,
      sessionLosses);
  }

  public async Task<MatchesPage> GetMatchesPagedAsync(
    int page,
    int pageSize,
    CancellationToken cancellationToken = default)
  {
    if (page < 1)
    {
      page = 1;
    }

    if (pageSize < 1)
    {
      pageSize = 20;
    }

    var allResults = await _appendStorage.ReadAllAsync<PlayerMatchResult>(MatchResultsKey, cancellationToken);
    var matches = allResults
      .GroupBy(result => result.MatchGuid, StringComparer.OrdinalIgnoreCase)
      .Select(group =>
      {
        var first = group.First();
        var players = group
          .Select(player => new MatchSummaryPlayer(
            player.PrimaryId,
            player.Name,
            player.TeamNum,
            player.Score,
            player.Goals,
            player.Assists,
            player.Saves))
          .OrderBy(player => player.TeamNum)
          .ThenByDescending(player => player.Score)
          .ToArray();

        return new MatchSummary(
          group.Key,
          group.Max(player => player.EndedAt),
          first.Arena,
          first.GameMode,
          first.WinningTeam,
          players);
      })
      .OrderByDescending(match => match.EndedAt)
      .ToArray();

    var pageItems = matches
      .Skip((page - 1) * pageSize)
      .Take(pageSize)
      .ToArray();

    return new MatchesPage(pageItems, page, pageSize, matches.Length);
  }

  public async Task DeleteMatchAsync(
    string matchGuid,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(matchGuid))
    {
      return;
    }

    var allResults = await _appendStorage.ReadAllAsync<PlayerMatchResult>(MatchResultsKey, cancellationToken);
    var remaining = allResults
      .Where(result => !string.Equals(result.MatchGuid, matchGuid, StringComparison.OrdinalIgnoreCase))
      .ToArray();

    if (remaining.Length != allResults.Count)
    {
      await _appendStorage.RewriteAsync(MatchResultsKey, remaining, cancellationToken);
    }

    await _sessionGate.WaitAsync(cancellationToken);

    try
    {
      var sessions = await _appendStorage.ReadAllAsync<MatchSession>(SessionsKey, cancellationToken);
      var changed = false;
      var rebuilt = new List<MatchSession>(sessions.Count);

      foreach (var session in sessions)
      {
        var keptGuids = session.MatchGuids
          .Where(guid => !string.Equals(guid, matchGuid, StringComparison.OrdinalIgnoreCase))
          .ToArray();

        if (keptGuids.Length == session.MatchGuids.Count)
        {
          rebuilt.Add(session);
          continue;
        }

        changed = true;

        if (keptGuids.Length == 0)
        {
          continue;
        }

        rebuilt.Add(session with { MatchGuids = keptGuids });
      }

      if (changed)
      {
        await _appendStorage.RewriteAsync(SessionsKey, rebuilt, cancellationToken);
      }
    }
    finally
    {
      _sessionGate.Release();
    }

    _logger.LogInformation("Deleted match {MatchGuid} from match results and sessions.", matchGuid);
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync();
    _gate.Dispose();
    _sessionGate.Dispose();
  }

  private async Task UpsertSessionForMatchAsync(
    string matchGuid,
    DateTimeOffset endedAt,
    int gameMode,
    CancellationToken cancellationToken)
  {
    await _sessionGate.WaitAsync(cancellationToken);

    try
    {
      var sessions = await _appendStorage.ReadAllAsync<MatchSession>(SessionsKey, cancellationToken);
      var latest = sessions.Count == 0 ? null : sessions[^1];

      if (latest is not null &&
          endedAt - latest.EndedAt <= SessionGap &&
          latest.GameMode == gameMode)
      {
        if (latest.MatchGuids.Any(existing =>
          string.Equals(existing, matchGuid, StringComparison.OrdinalIgnoreCase)))
        {
          return;
        }

        var updatedGuids = latest.MatchGuids.Append(matchGuid).ToArray();
        var updated = latest with { EndedAt = endedAt, MatchGuids = updatedGuids };
        await _appendStorage.UpdateLastAsync(SessionsKey, updated, cancellationToken);
        return;
      }

      var session = new MatchSession(
        Guid.NewGuid().ToString("N"),
        endedAt,
        endedAt,
        new[] { matchGuid },
        gameMode);

      await _appendStorage.AppendAsync(SessionsKey, session, cancellationToken);
    }
    finally
    {
      _sessionGate.Release();
    }
  }

  private async Task ListenAsync(CancellationToken cancellationToken)
  {
    ConnectionState = StatsApiConnectionState.Connecting;
    LastError = null;

    try
    {
      var endpoint = GetEndpoint(_options.WebSocketUrl);
      using var client = new TcpClient();
      await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
      ConnectionState = StatsApiConnectionState.Connected;
      _logger.LogInformation("Stats API listener connected to {Endpoint}.", _options.WebSocketUrl);

      await foreach (var message in ReceiveMessagesAsync(client.GetStream(), cancellationToken))
      {
        if (!string.IsNullOrWhiteSpace(message))
        {
          await HandleMessageAsync(message, cancellationToken);
        }
      }
    }
    catch (OperationCanceledException)
    {
      ConnectionState = StatsApiConnectionState.Stopped;
      _logger.LogInformation("Stats API listener canceled.");
    }
    catch (SocketException socketException) when (socketException.SocketErrorCode == SocketError.ConnectionRefused)
    {
      ConnectionState = StatsApiConnectionState.Stopped;
      _logger.LogInformation(
        "Stats API listener could not reach {Endpoint}; Rocket League is not running or the Stats API is not enabled.",
        _options.WebSocketUrl);
    }
    catch (Exception exception)
    {
      LastError = exception.Message;
      ConnectionState = StatsApiConnectionState.Faulted;
      _logger.LogWarning(exception, "Stats API listener stopped unexpectedly.");
    }
  }

  private static (string Host, int Port) GetEndpoint(string endpoint)
  {
    var uri = new Uri(endpoint);

    return (uri.Host, uri.Port);
  }

  private static async IAsyncEnumerable<string> ReceiveMessagesAsync(
    NetworkStream stream,
    [EnumeratorCancellation] CancellationToken cancellationToken)
  {
    var buffer = new byte[8192];
    var builder = new StringBuilder();
    var depth = 0;
    var hasStarted = false;
    var inString = false;
    var isEscaped = false;

    while (!cancellationToken.IsCancellationRequested)
    {
      var count = await stream.ReadAsync(buffer, cancellationToken);

      if (count == 0)
      {
        yield break;
      }

      var text = Encoding.UTF8.GetString(buffer, 0, count);

      foreach (var character in text)
      {
        if (!hasStarted)
        {
          if (char.IsWhiteSpace(character))
          {
            continue;
          }

          hasStarted = character == '{';
        }

        if (!hasStarted)
        {
          continue;
        }

        builder.Append(character);

        if (isEscaped)
        {
          isEscaped = false;
          continue;
        }

        if (character == '\\' && inString)
        {
          isEscaped = true;
          continue;
        }

        if (character == '"')
        {
          inString = !inString;
          continue;
        }

        if (inString)
        {
          continue;
        }

        if (character == '{')
        {
          depth++;
        }
        else if (character == '}')
        {
          depth--;
        }

        if (depth == 0)
        {
          yield return builder.ToString();
          builder.Clear();
          hasStarted = false;
        }
      }
    }
  }

  private async Task HandleMessageAsync(
    string rawJson,
    CancellationToken cancellationToken)
  {
    using var document = JsonDocument.Parse(rawJson);
    var root = document.RootElement;
    var eventName = GetString(root, "Event") ?? "Unknown";
    var data = GetData(root, out var dataDocument);
    using var parsedDataDocument = dataDocument;
    var matchGuid = GetString(data, "MatchGuid");
    _logger.LogInformation("Stats API event received: {EventName}.", eventName);

    if (string.Equals(eventName, "UpdateState", StringComparison.OrdinalIgnoreCase))
    {
      await HandleUpdateStateAsync(data, cancellationToken);
      return;
    }

    if (string.Equals(eventName, "MatchEnded", StringComparison.OrdinalIgnoreCase))
    {
      await FlushMatchAsync(matchGuid ?? _activeMatchGuid, cancellationToken);
    }
    else if (string.Equals(eventName, "StatfeedEvent", StringComparison.OrdinalIgnoreCase))
    {
      HandleStatfeedEvent(matchGuid ?? _activeMatchGuid, data);
    }

    await AppendEventAsync(
      new StatsApiEventLogEntry(Guid.NewGuid().ToString("N"), eventName, matchGuid, DateTimeOffset.UtcNow, rawJson),
      cancellationToken);
  }

  private static JsonElement GetData(JsonElement root, out JsonDocument? dataDocument)
  {
    dataDocument = null;

    if (!TryGetProperty(root, "Data", out var data))
    {
      return root;
    }

    if (data.ValueKind != JsonValueKind.String)
    {
      return data;
    }

    var rawData = data.GetString();

    if (string.IsNullOrWhiteSpace(rawData))
    {
      return data;
    }

    dataDocument = JsonDocument.Parse(rawData);

    return dataDocument.RootElement;
  }

  private async Task HandleUpdateStateAsync(
    JsonElement data,
    CancellationToken cancellationToken)
  {
    if (!TryGetProperty(data, "Players", out var playersElement) ||
        playersElement.ValueKind != JsonValueKind.Array)
    {
      return;
    }

    var now = DateTimeOffset.UtcNow;
    var livePlayers = playersElement.EnumerateArray()
      .Select(player => new LivePlayerStats(
        GetString(player, "PrimaryId") ?? GetString(player, "Name") ?? "Unknown",
        GetString(player, "Name") ?? "Unknown",
        GetInt(player, "TeamNum"),
        GetInt(player, "Score"),
        GetInt(player, "Goals"),
        GetInt(player, "Shots"),
        GetInt(player, "Assists"),
        GetInt(player, "Saves"),
        GetInt(player, "Touches"),
        GetInt(player, "Demos"),
        GetInt(player, "Boost"),
        now,
        GetDouble(player, "Speed"),
        GetBool(player, "bSupersonic"),
        GetBool(player, "bHasCar")))
      .Where(player => !string.IsNullOrWhiteSpace(player.PrimaryId))
      .ToArray();

    await UpsertObservedPlayersAsync(livePlayers, cancellationToken);
    var matchState = CreateLiveMatchState(data, livePlayers, now);
    var matchGuid = matchState.MatchGuid;

    // Late frames for a match we've already finalized would otherwise resurrect the aggregate
    // and produce a duplicate result on the next match-guid switch.
    if (!string.IsNullOrWhiteSpace(matchGuid) && _recentlyFlushedSet.Contains(matchGuid))
    {
      return;
    }

    _liveMatchState = matchState;

    // A new MatchGuid means the previous match ended without a MatchEnded event being seen yet — flush it now.
    if (!string.IsNullOrWhiteSpace(_activeMatchGuid) &&
        !string.Equals(_activeMatchGuid, matchGuid, StringComparison.OrdinalIgnoreCase))
    {
      await FlushMatchAsync(_activeMatchGuid, cancellationToken);
    }

    if (string.IsNullOrWhiteSpace(matchGuid))
    {
      return;
    }

    _activeMatchGuid = matchGuid;
    SampleMatchAggregate(matchGuid, matchState, livePlayers);
  }

  private void SampleMatchAggregate(
    string matchGuid,
    LiveMatchState matchState,
    IReadOnlyList<LivePlayerStats> livePlayers)
  {
    if (!_matchAggregates.TryGetValue(matchGuid, out var aggregate))
    {
      aggregate = new MatchAggregate(matchGuid, matchState.Arena);
      _matchAggregates[matchGuid] = aggregate;
    }

    aggregate.UpdatedAt = matchState.UpdatedAt;
    aggregate.Arena = matchState.Arena ?? aggregate.Arena;

    if (matchState.HasWinner && TryParseWinningTeam(matchState.Winner, out var explicitWinner))
    {
      aggregate.WinningTeam = explicitWinner;
    }

    foreach (var player in livePlayers)
    {
      if (!aggregate.Players.TryGetValue(player.PrimaryId, out var playerAgg))
      {
        playerAgg = new PlayerMatchAggregate(player.PrimaryId, player.Name);
        aggregate.Players[player.PrimaryId] = playerAgg;
      }

      playerAgg.Name = player.Name;
      playerAgg.TeamNum = player.TeamNum;
      playerAgg.LatestScore = player.Score;
      playerAgg.LatestGoals = player.Goals;
      playerAgg.LatestAssists = player.Assists;
      playerAgg.LatestSaves = player.Saves;
      playerAgg.LatestShots = player.Shots;
      playerAgg.LatestTouches = player.Touches;
      playerAgg.LatestDemos = player.Demos;
      playerAgg.BoostSum += player.Boost;
      playerAgg.BoostSamples++;

      if (player.HasCar)
      {
        playerAgg.SpeedSumUu += player.Speed;
        playerAgg.SpeedSamples++;

        if (player.IsSupersonic)
        {
          playerAgg.SupersonicSamples++;
        }
      }
    }
  }

  private void HandleStatfeedEvent(string? matchGuid, JsonElement data)
  {
    if (string.IsNullOrWhiteSpace(matchGuid) ||
        !_matchAggregates.TryGetValue(matchGuid, out var aggregate))
    {
      return;
    }

    var statEventName = GetString(data, "EventName");

    if (!string.Equals(statEventName, "Demolish", StringComparison.OrdinalIgnoreCase))
    {
      return;
    }

    if (!TryGetProperty(data, "SecondaryTarget", out var target) ||
        target.ValueKind != JsonValueKind.Object)
    {
      return;
    }

    var name = GetString(target, "Name");

    if (string.IsNullOrWhiteSpace(name))
    {
      return;
    }

    var teamNum = GetInt(target, "TeamNum");
    var demoed = aggregate.Players.Values.FirstOrDefault(player =>
      string.Equals(player.Name, name, StringComparison.OrdinalIgnoreCase) &&
      player.TeamNum == teamNum);

    if (demoed is null)
    {
      return;
    }

    demoed.TimesDemoed++;
  }

  private static bool TryParseWinningTeam(string? winner, out int team)
  {
    team = 0;

    if (string.IsNullOrWhiteSpace(winner))
    {
      return false;
    }

    if (int.TryParse(winner, out var parsed) && parsed is 0 or 1)
    {
      team = parsed;
      return true;
    }

    if (winner.Equals("blue", StringComparison.OrdinalIgnoreCase))
    {
      team = 0;
      return true;
    }

    if (winner.Equals("orange", StringComparison.OrdinalIgnoreCase))
    {
      team = 1;
      return true;
    }

    return false;
  }

  private async Task FlushMatchAsync(string? matchGuid, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(matchGuid))
    {
      return;
    }

    RememberFlushedMatch(matchGuid);

    if (!_matchAggregates.Remove(matchGuid, out var aggregate))
    {
      return;
    }

    if (string.Equals(_activeMatchGuid, matchGuid, StringComparison.OrdinalIgnoreCase))
    {
      _activeMatchGuid = null;
    }

    var endedAt = aggregate.UpdatedAt == default ? DateTimeOffset.UtcNow : aggregate.UpdatedAt;
    var gameMode = aggregate.Players.Count;
    var winningTeam = aggregate.WinningTeam ?? DeriveWinningTeamFromGoals(aggregate.Players.Values);

    foreach (var player in aggregate.Players.Values)
    {
      var averageBoost = player.BoostSamples > 0
        ? (double)player.BoostSum / player.BoostSamples
        : 0d;

      double? averageSpeedKph = player.SpeedSamples > 0
        ? player.SpeedSumUu / player.SpeedSamples * 0.036d
        : null;
      double? supersonicPercent = player.SpeedSamples > 0
        ? (double)player.SupersonicSamples / player.SpeedSamples * 100d
        : null;

      var result = new PlayerMatchResult(
        player.PrimaryId,
        player.Name,
        aggregate.MatchGuid,
        aggregate.Arena,
        endedAt,
        player.LatestScore,
        player.LatestGoals,
        player.LatestAssists,
        player.LatestSaves,
        player.LatestShots,
        player.LatestTouches,
        player.LatestDemos,
        averageBoost,
        player.TeamNum,
        winningTeam,
        gameMode,
        averageSpeedKph,
        supersonicPercent,
        player.TimesDemoed);

      await _appendStorage.AppendAsync(MatchResultsKey, result, cancellationToken);
    }

    _logger.LogInformation(
      "Persisted {PlayerCount} player results for match {MatchGuid}.",
      aggregate.Players.Count,
      aggregate.MatchGuid);

    await _appendStorage.TrimAsync(MatchResultsKey, _options.StoredMatchLimit, cancellationToken);

    await UpsertSessionForMatchAsync(aggregate.MatchGuid, endedAt, gameMode, cancellationToken);
  }

  private void RememberFlushedMatch(string matchGuid)
  {
    if (!_recentlyFlushedSet.Add(matchGuid))
    {
      return;
    }

    _recentlyFlushedQueue.Enqueue(matchGuid);

    while (_recentlyFlushedQueue.Count > RecentlyFlushedCapacity)
    {
      var evicted = _recentlyFlushedQueue.Dequeue();
      _recentlyFlushedSet.Remove(evicted);
    }
  }

  private static int? DeriveWinningTeamFromGoals(IEnumerable<PlayerMatchAggregate> players)
  {
    var goalsByTeam = players
      .GroupBy(player => player.TeamNum)
      .Select(group => new { Team = group.Key, Goals = group.Sum(player => player.LatestGoals) })
      .ToArray();

    if (goalsByTeam.Length < 2)
    {
      return null;
    }

    var ordered = goalsByTeam.OrderByDescending(entry => entry.Goals).ToArray();

    if (ordered[0].Goals == ordered[1].Goals)
    {
      return null;
    }

    return ordered[0].Team;
  }

  private sealed class MatchAggregate
  {
    public MatchAggregate(string matchGuid, string? arena)
    {
      MatchGuid = matchGuid;
      Arena = arena;
    }

    public string MatchGuid { get; }

    public string? Arena { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    public int? WinningTeam { get; set; }

    public Dictionary<string, PlayerMatchAggregate> Players { get; } =
      new(StringComparer.OrdinalIgnoreCase);
  }

  private sealed class PlayerMatchAggregate
  {
    public PlayerMatchAggregate(string primaryId, string name)
    {
      PrimaryId = primaryId;
      Name = name;
    }

    public string PrimaryId { get; }

    public string Name { get; set; }

    public int TeamNum { get; set; }

    public int LatestScore { get; set; }

    public int LatestGoals { get; set; }

    public int LatestAssists { get; set; }

    public int LatestSaves { get; set; }

    public int LatestShots { get; set; }

    public int LatestTouches { get; set; }

    public int LatestDemos { get; set; }

    public long BoostSum { get; set; }

    public int BoostSamples { get; set; }

    public double SpeedSumUu { get; set; }

    public int SpeedSamples { get; set; }

    public int SupersonicSamples { get; set; }

    public int TimesDemoed { get; set; }
  }

  private async Task UpsertObservedPlayersAsync(
    IReadOnlyList<LivePlayerStats> livePlayers,
    CancellationToken cancellationToken)
  {
    await _gate.WaitAsync(cancellationToken);

    try
    {
      var existing = (await ReadObservedPlayersAsync(cancellationToken))
        .ToDictionary(player => player.PrimaryId, StringComparer.OrdinalIgnoreCase);
      var dashboardPlayerIds = await ReadDashboardPlayerIdsAsync(cancellationToken);

      foreach (var livePlayer in livePlayers)
      {
        var showOnDashboard = dashboardPlayerIds.Contains(livePlayer.PrimaryId);

        if (existing.TryGetValue(livePlayer.PrimaryId, out var player))
        {
          existing[livePlayer.PrimaryId] = player with
          {
            Name = livePlayer.Name,
            TeamNum = livePlayer.TeamNum,
            LastSeenAt = livePlayer.UpdatedAt,
            ShowOnDashboard = showOnDashboard
          };
        }
        else
        {
          existing[livePlayer.PrimaryId] = new ObservedPlayer(
            livePlayer.PrimaryId,
            livePlayer.Name,
            livePlayer.TeamNum,
            livePlayer.UpdatedAt,
            livePlayer.UpdatedAt,
            showOnDashboard);
        }
      }

      var updated = existing.Values
        .OrderByDescending(player => player.LastSeenAt)
        .ToArray();

      await _localStorage.WriteAsync(ObservedPlayersKey, updated, cancellationToken);
    }
    finally
    {
      _gate.Release();
    }
  }

  private async Task AppendEventAsync(
    StatsApiEventLogEntry entry,
    CancellationToken cancellationToken)
  {
    await _appendStorage.AppendAsync(EventLogKey, entry, cancellationToken);

    // Trim lazily: only roughly every Nth append, to avoid rewriting the whole file frequently.
    if (Random.Shared.Next(_options.StoredEventLimit / 10 + 1) == 0)
    {
      await _appendStorage.TrimAsync(EventLogKey, _options.StoredEventLimit, cancellationToken);
    }
  }

  private LiveMatchState CreateLiveMatchState(
    JsonElement data,
    IReadOnlyList<LivePlayerStats> livePlayers,
    DateTimeOffset updatedAt)
  {
    var game = TryGetProperty(data, "Game", out var gameElement) ? gameElement : data;

    return new LiveMatchState(
      GetString(data, "MatchGuid"),
      GetString(game, "Arena"),
      GetInt(game, "TimeSeconds"),
      GetBool(game, "bOvertime"),
      GetBool(game, "bHasWinner"),
      GetString(game, "Winner"),
      livePlayers,
      updatedAt);
  }

  private async Task<IReadOnlyList<ObservedPlayer>> ReadObservedPlayersAsync(
    CancellationToken cancellationToken) =>
    await _localStorage.ReadAsync<IReadOnlyList<ObservedPlayer>>(ObservedPlayersKey, cancellationToken) ?? [];

  private async Task<HashSet<string>> ReadDashboardPlayerIdsAsync(
    CancellationToken cancellationToken)
  {
    var playerIds = await _localStorage.ReadAsync<IReadOnlyList<string>>(DashboardPlayerIdsKey, cancellationToken) ?? [];

    return playerIds.ToHashSet(StringComparer.OrdinalIgnoreCase);
  }

  private async Task<string?> ReadMePrimaryIdAsync(CancellationToken cancellationToken) =>
    await _localStorage.ReadAsync<string>(MePrimaryIdKey, cancellationToken);

  private static IReadOnlyList<ObservedPlayer> ApplyPlayerFlags(
    IReadOnlyList<ObservedPlayer> players,
    HashSet<string> dashboardPlayerIds,
    string? mePrimaryId) =>
    players
      .Select(player => player with
      {
        ShowOnDashboard = dashboardPlayerIds.Contains(player.PrimaryId),
        IsMe = !string.IsNullOrWhiteSpace(mePrimaryId) &&
          string.Equals(mePrimaryId, player.PrimaryId, StringComparison.OrdinalIgnoreCase)
      })
      .ToArray();

  private static string? GetString(JsonElement element, string name)
  {
    if (TryGetProperty(element, name, out var value) && value.ValueKind == JsonValueKind.String)
    {
      return value.GetString();
    }

    return null;
  }

  private static int GetInt(JsonElement element, string name)
  {
    if (!TryGetProperty(element, name, out var value))
    {
      return 0;
    }

    if (value.ValueKind == JsonValueKind.Number && value.TryGetInt32(out var number))
    {
      return number;
    }

    return value.ValueKind == JsonValueKind.String && int.TryParse(value.GetString(), out number)
      ? number
      : 0;
  }

  private static double GetDouble(JsonElement element, string name)
  {
    if (!TryGetProperty(element, name, out var value))
    {
      return 0d;
    }

    if (value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number))
    {
      return number;
    }

    return value.ValueKind == JsonValueKind.String &&
        double.TryParse(value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out number)
      ? number
      : 0d;
  }

  private static bool GetBool(JsonElement element, string name) =>
    TryGetProperty(element, name, out var value) &&
    value.ValueKind is JsonValueKind.True or JsonValueKind.False &&
    value.GetBoolean();

  private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
  {
    if (element.ValueKind != JsonValueKind.Object)
    {
      value = default;
      return false;
    }

    foreach (var property in element.EnumerateObject())
    {
      if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
      {
        value = property.Value;
        return true;
      }
    }

    value = default;
    return false;
  }
}
