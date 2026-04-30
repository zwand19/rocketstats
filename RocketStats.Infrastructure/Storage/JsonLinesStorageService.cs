using System.Text;
using System.Text.Json;
using RocketStats.Application.Abstractions;

namespace RocketStats.Infrastructure.Storage;

public sealed class JsonLinesStorageService : IAppendOnlyStorageService
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  private readonly Dictionary<string, SemaphoreSlim> _gates = new(StringComparer.OrdinalIgnoreCase);
  private readonly object _gatesLock = new();
  private readonly string _storagePath;

  public JsonLinesStorageService()
  {
    _storagePath = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "RocketStats");

    Directory.CreateDirectory(_storagePath);
  }

  public async Task AppendAsync<T>(
    string key,
    T value,
    CancellationToken cancellationToken = default)
  {
    var gate = GetGate(key);
    await gate.WaitAsync(cancellationToken);

    try
    {
      var line = JsonSerializer.Serialize(value, JsonOptions);
      await File.AppendAllTextAsync(GetPath(key), line + Environment.NewLine, Encoding.UTF8, cancellationToken);
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task<IReadOnlyList<T>> ReadAllAsync<T>(
    string key,
    CancellationToken cancellationToken = default)
  {
    var gate = GetGate(key);
    await gate.WaitAsync(cancellationToken);

    try
    {
      var path = GetPath(key);

      if (!File.Exists(path))
      {
        return [];
      }

      var results = new List<T>();
      var lineNumber = 0;

      await foreach (var line in File.ReadLinesAsync(path, Encoding.UTF8, cancellationToken))
      {
        lineNumber++;

        if (string.IsNullOrWhiteSpace(line))
        {
          continue;
        }

        try
        {
          var value = JsonSerializer.Deserialize<T>(line, JsonOptions);

          if (value is not null)
          {
            results.Add(value);
          }
        }
        catch (JsonException)
        {
          // Skip torn or otherwise unparseable lines (e.g., crash mid-write).
        }
      }

      return results;
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task TrimAsync(
    string key,
    int keepLast,
    CancellationToken cancellationToken = default)
  {
    if (keepLast <= 0)
    {
      throw new ArgumentOutOfRangeException(nameof(keepLast));
    }

    var gate = GetGate(key);
    await gate.WaitAsync(cancellationToken);

    try
    {
      var path = GetPath(key);

      if (!File.Exists(path))
      {
        return;
      }

      var allLines = await File.ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken);

      if (allLines.Length <= keepLast)
      {
        return;
      }

      var keep = allLines[^keepLast..];
      var tempPath = path + ".tmp";
      await File.WriteAllLinesAsync(tempPath, keep, Encoding.UTF8, cancellationToken);
      File.Move(tempPath, path, overwrite: true);
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task RewriteAsync<T>(
    string key,
    IEnumerable<T> rows,
    CancellationToken cancellationToken = default)
  {
    var gate = GetGate(key);
    await gate.WaitAsync(cancellationToken);

    try
    {
      var path = GetPath(key);
      var lines = rows.Select(row => JsonSerializer.Serialize(row, JsonOptions));
      var tempPath = path + ".tmp";
      await File.WriteAllLinesAsync(tempPath, lines, Encoding.UTF8, cancellationToken);
      File.Move(tempPath, path, overwrite: true);
    }
    finally
    {
      gate.Release();
    }
  }

  public async Task UpdateLastAsync<T>(
    string key,
    T row,
    CancellationToken cancellationToken = default)
  {
    var gate = GetGate(key);
    await gate.WaitAsync(cancellationToken);

    try
    {
      var path = GetPath(key);
      var serialized = JsonSerializer.Serialize(row, JsonOptions);

      if (!File.Exists(path))
      {
        await File.WriteAllTextAsync(path, serialized + Environment.NewLine, Encoding.UTF8, cancellationToken);
        return;
      }

      var allLines = await File.ReadAllLinesAsync(path, Encoding.UTF8, cancellationToken);
      var nonEmpty = allLines.Where(line => !string.IsNullOrWhiteSpace(line)).ToList();

      if (nonEmpty.Count == 0)
      {
        nonEmpty.Add(serialized);
      }
      else
      {
        nonEmpty[^1] = serialized;
      }

      var tempPath = path + ".tmp";
      await File.WriteAllLinesAsync(tempPath, nonEmpty, Encoding.UTF8, cancellationToken);
      File.Move(tempPath, path, overwrite: true);
    }
    finally
    {
      gate.Release();
    }
  }

  private SemaphoreSlim GetGate(string key)
  {
    lock (_gatesLock)
    {
      if (!_gates.TryGetValue(key, out var gate))
      {
        gate = new SemaphoreSlim(1, 1);
        _gates[key] = gate;
      }

      return gate;
    }
  }

  private string GetPath(string key)
  {
    var safeKey = string.Join(
      "_",
      key.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

    return Path.Combine(_storagePath, $"{safeKey}.jsonl");
  }
}
