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
  private const string EventLogKey = "stats-api-event-log";
  private const string MatchResultsKey = "stats-api-match-results";

  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly SemaphoreSlim _gate = new(1, 1);
  private readonly Dictionary<string, MatchAggregate> _matchAggregates = new(StringComparer.OrdinalIgnoreCase);
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

    return ApplyDashboardVisibility(players, dashboardPlayerIds);
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
    CancellationToken cancellationToken = default)
  {
    var results = await _appendStorage.ReadAllAsync<PlayerMatchResult>(MatchResultsKey, cancellationToken);
    var filter = primaryIds?.ToHashSet(StringComparer.OrdinalIgnoreCase);
    var filtered = filter is null
      ? results
      : results.Where(result => filter.Contains(result.PrimaryId)).ToArray();

    return filtered
      .GroupBy(result => result.PrimaryId, StringComparer.OrdinalIgnoreCase)
      .Select(group =>
      {
        var games = group.Count();
        var latestName = group.OrderByDescending(result => result.EndedAt).First().Name;

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
          group.Average(result => result.AverageBoost));
      })
      .OrderByDescending(averages => averages.Goals)
      .ToArray();
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync();
    _gate.Dispose();
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
        now))
      .Where(player => !string.IsNullOrWhiteSpace(player.PrimaryId))
      .ToArray();

    await UpsertObservedPlayersAsync(livePlayers, cancellationToken);
    var matchState = CreateLiveMatchState(data, livePlayers, now);
    _liveMatchState = matchState;

    var matchGuid = matchState.MatchGuid;

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

    foreach (var player in livePlayers)
    {
      if (!aggregate.Players.TryGetValue(player.PrimaryId, out var playerAgg))
      {
        playerAgg = new PlayerMatchAggregate(player.PrimaryId, player.Name);
        aggregate.Players[player.PrimaryId] = playerAgg;
      }

      playerAgg.Name = player.Name;
      playerAgg.LatestScore = player.Score;
      playerAgg.LatestGoals = player.Goals;
      playerAgg.LatestAssists = player.Assists;
      playerAgg.LatestSaves = player.Saves;
      playerAgg.LatestShots = player.Shots;
      playerAgg.LatestTouches = player.Touches;
      playerAgg.LatestDemos = player.Demos;
      playerAgg.BoostSum += player.Boost;
      playerAgg.BoostSamples++;
    }
  }

  private async Task FlushMatchAsync(string? matchGuid, CancellationToken cancellationToken)
  {
    if (string.IsNullOrWhiteSpace(matchGuid))
    {
      return;
    }

    if (!_matchAggregates.Remove(matchGuid, out var aggregate))
    {
      return;
    }

    if (string.Equals(_activeMatchGuid, matchGuid, StringComparison.OrdinalIgnoreCase))
    {
      _activeMatchGuid = null;
    }

    var endedAt = aggregate.UpdatedAt == default ? DateTimeOffset.UtcNow : aggregate.UpdatedAt;

    foreach (var player in aggregate.Players.Values)
    {
      var averageBoost = player.BoostSamples > 0
        ? (double)player.BoostSum / player.BoostSamples
        : 0d;

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
        averageBoost);

      await _appendStorage.AppendAsync(MatchResultsKey, result, cancellationToken);
    }

    _logger.LogInformation(
      "Persisted {PlayerCount} player results for match {MatchGuid}.",
      aggregate.Players.Count,
      aggregate.MatchGuid);

    await _appendStorage.TrimAsync(MatchResultsKey, _options.StoredMatchLimit, cancellationToken);
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

    public int LatestScore { get; set; }

    public int LatestGoals { get; set; }

    public int LatestAssists { get; set; }

    public int LatestSaves { get; set; }

    public int LatestShots { get; set; }

    public int LatestTouches { get; set; }

    public int LatestDemos { get; set; }

    public long BoostSum { get; set; }

    public int BoostSamples { get; set; }
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

  private static IReadOnlyList<ObservedPlayer> ApplyDashboardVisibility(
    IReadOnlyList<ObservedPlayer> players,
    HashSet<string> dashboardPlayerIds) =>
    players
      .Select(player => player with { ShowOnDashboard = dashboardPlayerIds.Contains(player.PrimaryId) })
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
