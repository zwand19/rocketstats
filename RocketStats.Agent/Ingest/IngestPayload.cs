using System.Text.Json.Serialization;

namespace RocketStats.Agent.Ingest;

public sealed record IngestPayload(
  IReadOnlyList<ObservedPlayerInput> ObservedPlayers,
  IReadOnlyList<PlayerMatchResultInput> PlayerMatchResults,
  IReadOnlyList<EventLogEntryInput> Events,
  LiveMatchStateInput? LiveMatchState,
  string ConnectionState,
  string? LastError);

public sealed record ObservedPlayerInput(
  string PrimaryId,
  string Name,
  int TeamNum,
  DateTimeOffset FirstSeenAt,
  DateTimeOffset LastSeenAt);

public sealed record PlayerMatchResultInput(
  string MatchGuid,
  string PrimaryId,
  string Name,
  string? Arena,
  DateTimeOffset EndedAt,
  int Score,
  int Goals,
  int Assists,
  int Saves,
  int Shots,
  int Touches,
  int Demos,
  double AverageBoost,
  int TeamNum,
  int? WinningTeam,
  int GameMode,
  double? AverageSpeedKph,
  double? SupersonicPercent,
  int? TimesDemoed);

public sealed record EventLogEntryInput(
  string Id,
  string EventName,
  string? MatchGuid,
  DateTimeOffset ReceivedAt,
  string RawJson);

public sealed record LiveMatchStateInput(
  string? MatchGuid,
  string? Arena,
  int TimeSeconds,
  bool IsOvertime,
  bool HasWinner,
  string? Winner,
  IReadOnlyList<LivePlayerInput> Players,
  DateTimeOffset UpdatedAt);

public sealed record LivePlayerInput(
  string PrimaryId,
  string Name,
  int TeamNum,
  int Score,
  int Goals,
  int Shots,
  int Assists,
  int Saves,
  int Touches,
  int Demos,
  int Boost,
  DateTimeOffset UpdatedAt,
  double Speed,
  bool IsSupersonic,
  bool HasCar);

public static class IngestJsonOptions
{
  public static readonly System.Text.Json.JsonSerializerOptions Default = new(System.Text.Json.JsonSerializerDefaults.Web)
  {
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
  };
}
