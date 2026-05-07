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

public sealed class StatsApiListener : IAsyncDisposable
{
  private const int RecentlyFlushedCapacity = 16;

  private readonly Dictionary<string, MatchAggregate> _matchAggregates = new(StringComparer.OrdinalIgnoreCase);
  private readonly Dictionary<string, DateTimeOffset> _firstSeenAt = new(StringComparer.OrdinalIgnoreCase);
  private readonly HashSet<string> _recentlyFlushedSet = new(StringComparer.OrdinalIgnoreCase);
  private readonly Queue<string> _recentlyFlushedQueue = new();
  private readonly IStatsRecorder _recorder;
  private readonly ILogger<StatsApiListener> _logger;
  private readonly StatsApiOptions _options;

  private CancellationTokenSource? _listenerCancellation;
  private Task? _listenerTask;
  private string? _activeMatchGuid;

  public StatsApiListener(
    IStatsRecorder recorder,
    IOptions<StatsApiOptions> options,
    ILogger<StatsApiListener> logger)
  {
    _recorder = recorder;
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
      _recorder.RecordConnectionState(ConnectionState, LastError);
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
    _recorder.RecordConnectionState(ConnectionState, LastError);
    _logger.LogInformation("Stats API listener stopped.");
  }

  public async ValueTask DisposeAsync()
  {
    await StopAsync();
  }

  private async Task ListenAsync(CancellationToken cancellationToken)
  {
    while (!cancellationToken.IsCancellationRequested)
    {
      ConnectionState = StatsApiConnectionState.Connecting;
      LastError = null;
      _recorder.RecordConnectionState(ConnectionState, LastError);

      try
      {
        var endpoint = GetEndpoint(_options.WebSocketUrl);
        using var client = new TcpClient();
        await client.ConnectAsync(endpoint.Host, endpoint.Port, cancellationToken);
        ConnectionState = StatsApiConnectionState.Connected;
        _recorder.RecordConnectionState(ConnectionState, LastError);
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
        _recorder.RecordConnectionState(ConnectionState, LastError);
        _logger.LogInformation("Stats API listener canceled.");
        return;
      }
      catch (SocketException socketException) when (socketException.SocketErrorCode == SocketError.ConnectionRefused)
      {
        ConnectionState = StatsApiConnectionState.Stopped;
        _recorder.RecordConnectionState(ConnectionState, LastError);
        _logger.LogInformation(
          "Stats API listener could not reach {Endpoint}; Rocket League is not running or the Stats API is not enabled.",
          _options.WebSocketUrl);
      }
      catch (Exception exception)
      {
        LastError = exception.Message;
        ConnectionState = StatsApiConnectionState.Faulted;
        _recorder.RecordConnectionState(ConnectionState, LastError);
        _logger.LogWarning(exception, "Stats API listener stopped unexpectedly.");
      }

      try
      {
        await Task.Delay(TimeSpan.FromSeconds(_options.ReconnectDelaySeconds), cancellationToken);
      }
      catch (OperationCanceledException)
      {
        return;
      }
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

  private Task HandleMessageAsync(string rawJson, CancellationToken cancellationToken)
  {
    using var document = JsonDocument.Parse(rawJson);
    var root = document.RootElement;
    var eventName = GetString(root, "Event") ?? "Unknown";
    var data = GetData(root, out var dataDocument);
    using var parsedDataDocument = dataDocument;
    var matchGuid = GetString(data, "MatchGuid");

    if (string.Equals(eventName, "UpdateState", StringComparison.OrdinalIgnoreCase))
    {
      HandleUpdateState(data);
      return Task.CompletedTask;
    }

    if (string.Equals(eventName, "MatchEnded", StringComparison.OrdinalIgnoreCase))
    {
      FlushMatch(matchGuid ?? _activeMatchGuid);
    }
    else if (string.Equals(eventName, "StatfeedEvent", StringComparison.OrdinalIgnoreCase))
    {
      HandleStatfeedEvent(matchGuid ?? _activeMatchGuid, data);
    }

    _recorder.RecordEvent(new StatsApiEventLogEntry(
      Guid.NewGuid().ToString(),
      eventName,
      matchGuid,
      DateTimeOffset.UtcNow,
      rawJson));

    return Task.CompletedTask;
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

  private void HandleUpdateState(JsonElement data)
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

    RecordObservedPlayers(livePlayers, now);

    var matchState = CreateLiveMatchState(data, livePlayers, now);
    var matchGuid = matchState.MatchGuid;

    if (!string.IsNullOrWhiteSpace(matchGuid) && _recentlyFlushedSet.Contains(matchGuid))
    {
      return;
    }

    _recorder.RecordLiveMatchState(matchState);

    if (!string.IsNullOrWhiteSpace(_activeMatchGuid) &&
        !string.Equals(_activeMatchGuid, matchGuid, StringComparison.OrdinalIgnoreCase))
    {
      FlushMatch(_activeMatchGuid);
    }

    if (string.IsNullOrWhiteSpace(matchGuid))
    {
      return;
    }

    _activeMatchGuid = matchGuid;
    SampleMatchAggregate(matchGuid, matchState, livePlayers);
  }

  private void RecordObservedPlayers(IReadOnlyList<LivePlayerStats> livePlayers, DateTimeOffset now)
  {
    if (livePlayers.Count == 0)
    {
      return;
    }

    var observed = new ObservedPlayer[livePlayers.Count];

    for (var i = 0; i < livePlayers.Count; i++)
    {
      var player = livePlayers[i];

      if (!_firstSeenAt.TryGetValue(player.PrimaryId, out var firstSeen))
      {
        firstSeen = now;
        _firstSeenAt[player.PrimaryId] = firstSeen;
      }

      observed[i] = new ObservedPlayer(
        player.PrimaryId,
        player.Name,
        player.TeamNum,
        firstSeen,
        now);
    }

    _recorder.RecordObservedPlayers(observed);
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

  private void FlushMatch(string? matchGuid)
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

      _recorder.RecordMatchResult(result);
    }

    _logger.LogInformation(
      "Recorded {PlayerCount} player results for match {MatchGuid}.",
      aggregate.Players.Count,
      aggregate.MatchGuid);
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

  private static LiveMatchState CreateLiveMatchState(
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
