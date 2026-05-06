using System.Globalization;
using Microsoft.Extensions.Logging;
using RocketStats.Application.Abstractions;
using RocketStats.Domain.Models;

namespace RocketStats.Infrastructure.Services;

public sealed class RocketLeagueConfigService : IRocketLeagueConfigService
{
  private const string SavedPathKey = "rocket-league-stats-api-ini-path";
  private const string ConfigSubPath = @"TAGame\Config\DefaultStatsAPI.ini";

  private static readonly string[] CandidateRoots =
  [
    @"C:\Program Files (x86)\Steam\steamapps\common\rocketleague",
    @"C:\Program Files\Epic Games\rocketleague",
    @"D:\SteamLibrary\steamapps\common\rocketleague",
    @"D:\Steam\steamapps\common\rocketleague",
    @"E:\SteamLibrary\steamapps\common\rocketleague",
    @"E:\Steam\steamapps\common\rocketleague"
  ];

  private readonly ILocalStorageService _localStorage;
  private readonly ILogger<RocketLeagueConfigService> _logger;

  public RocketLeagueConfigService(
    ILocalStorageService localStorage,
    ILogger<RocketLeagueConfigService> logger)
  {
    _localStorage = localStorage;
    _logger = logger;
  }

  public async Task<string?> GetSavedPathAsync(CancellationToken cancellationToken = default) =>
    await _localStorage.ReadAsync<string>(SavedPathKey, cancellationToken);

  public async Task SetSavedPathAsync(string? path, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      await _localStorage.RemoveAsync(SavedPathKey, cancellationToken);
      return;
    }

    await _localStorage.WriteAsync(SavedPathKey, path, cancellationToken);
  }

  public IReadOnlyList<string> GetCandidatePaths() =>
    CandidateRoots
      .Select(root => Path.Combine(root, ConfigSubPath))
      .Where(File.Exists)
      .ToArray();

  public async Task<RocketLeagueStatsApiConfig?> ReadAsync(CancellationToken cancellationToken = default)
  {
    var saved = await GetSavedPathAsync(cancellationToken);

    if (!string.IsNullOrWhiteSpace(saved))
    {
      return await ReadAsync(saved, cancellationToken);
    }

    var candidate = GetCandidatePaths().FirstOrDefault();
    return candidate is null ? null : await ReadAsync(candidate, cancellationToken);
  }

  public async Task<RocketLeagueStatsApiConfig?> ReadAsync(string path, CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return null;
    }

    if (!File.Exists(path))
    {
      return new RocketLeagueStatsApiConfig(path, false, false, 0, 0);
    }

    var rate = 0d;
    var port = 0;

    try
    {
      var lines = await File.ReadAllLinesAsync(path, cancellationToken);

      foreach (var line in lines)
      {
        var (key, value) = SplitKeyValue(line);

        if (key is null)
        {
          continue;
        }

        if (string.Equals(key, "PacketSendRate", StringComparison.OrdinalIgnoreCase) &&
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsedRate))
        {
          rate = parsedRate;
        }
        else if (string.Equals(key, "Port", StringComparison.OrdinalIgnoreCase) &&
                 int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsedPort))
        {
          port = parsedPort;
        }
      }

      var writable = TestWritable(path);
      return new RocketLeagueStatsApiConfig(path, true, writable, rate, port);
    }
    catch (Exception exception)
    {
      _logger.LogWarning(exception, "Failed to read Rocket League stats API config at {Path}.", path);
      return new RocketLeagueStatsApiConfig(path, true, false, 0, 0);
    }
  }

  public async Task<ConfigWriteResult> WriteAsync(
    string path,
    double packetSendRate,
    int port,
    CancellationToken cancellationToken = default)
  {
    if (string.IsNullOrWhiteSpace(path))
    {
      return new ConfigWriteResult(false, "No path specified.");
    }

    try
    {
      var existing = File.Exists(path)
        ? await File.ReadAllLinesAsync(path, cancellationToken)
        : Array.Empty<string>();

      var updated = ApplyKeyValues(existing, packetSendRate, port);
      await File.WriteAllLinesAsync(path, updated, cancellationToken);
      _logger.LogInformation(
        "Wrote Rocket League stats API config to {Path}: PacketSendRate={Rate}, Port={Port}.",
        path,
        packetSendRate,
        port);

      return new ConfigWriteResult(true, null);
    }
    catch (UnauthorizedAccessException)
    {
      return new ConfigWriteResult(
        false,
        "Permission denied writing to that path. Try running Rocket Stats as administrator, or edit the .ini file manually.");
    }
    catch (DirectoryNotFoundException)
    {
      return new ConfigWriteResult(false, "Directory not found. Verify the path points to a valid Rocket League install.");
    }
    catch (Exception exception)
    {
      _logger.LogWarning(exception, "Failed to write Rocket League stats API config at {Path}.", path);
      return new ConfigWriteResult(false, exception.Message);
    }
  }

  private static (string? Key, string? Value) SplitKeyValue(string line)
  {
    var trimmed = line.TrimStart();

    if (trimmed.Length == 0 || trimmed.StartsWith(';') || trimmed.StartsWith('[') || trimmed.StartsWith("//"))
    {
      return (null, null);
    }

    var eq = trimmed.IndexOf('=');
    return eq <= 0
      ? (null, null)
      : (trimmed[..eq].Trim(), trimmed[(eq + 1)..].Trim());
  }

  private static IEnumerable<string> ApplyKeyValues(
    IReadOnlyList<string> existing,
    double packetSendRate,
    int port)
  {
    var sawRate = false;
    var sawPort = false;
    var rateLine = $"PacketSendRate={packetSendRate.ToString(CultureInfo.InvariantCulture)}";
    var portLine = $"Port={port.ToString(CultureInfo.InvariantCulture)}";
    var output = new List<string>(existing.Count + 2);

    foreach (var line in existing)
    {
      var (key, _) = SplitKeyValue(line);

      if (key is null)
      {
        output.Add(line);
        continue;
      }

      if (string.Equals(key, "PacketSendRate", StringComparison.OrdinalIgnoreCase))
      {
        output.Add(rateLine);
        sawRate = true;
        continue;
      }

      if (string.Equals(key, "Port", StringComparison.OrdinalIgnoreCase))
      {
        output.Add(portLine);
        sawPort = true;
        continue;
      }

      output.Add(line);
    }

    if (!sawRate)
    {
      output.Add(rateLine);
    }

    if (!sawPort)
    {
      output.Add(portLine);
    }

    return output;
  }

  private static bool TestWritable(string path)
  {
    try
    {
      using var stream = File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
      return true;
    }
    catch
    {
      return false;
    }
  }
}
