using System.Text.Json;
using RocketStats.Application.Abstractions;

namespace RocketStats.Infrastructure.Storage;

public sealed class JsonLocalStorageService : ILocalStorageService
{
  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
  {
    WriteIndented = true
  };

  private readonly SemaphoreSlim _gate = new(1, 1);
  private readonly string _storagePath;

  public JsonLocalStorageService()
  {
    _storagePath = Path.Combine(
      Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
      "RocketStats");

    Directory.CreateDirectory(_storagePath);
  }

  public async Task<T?> ReadAsync<T>(
    string key,
    CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);

    try
    {
      var path = GetPath(key);

      if (!File.Exists(path))
      {
        return default;
      }

      await using var stream = File.OpenRead(path);
      return await JsonSerializer.DeserializeAsync<T>(stream, JsonOptions, cancellationToken);
    }
    finally
    {
      _gate.Release();
    }
  }

  public async Task WriteAsync<T>(
    string key,
    T value,
    CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);

    try
    {
      var path = GetPath(key);
      await using var stream = File.Create(path);
      await JsonSerializer.SerializeAsync(stream, value, JsonOptions, cancellationToken);
    }
    finally
    {
      _gate.Release();
    }
  }

  public async Task RemoveAsync(
    string key,
    CancellationToken cancellationToken = default)
  {
    await _gate.WaitAsync(cancellationToken);

    try
    {
      var path = GetPath(key);

      if (File.Exists(path))
      {
        File.Delete(path);
      }
    }
    finally
    {
      _gate.Release();
    }
  }

  private string GetPath(string key)
  {
    var safeKey = string.Join(
      "_",
      key.Split(Path.GetInvalidFileNameChars(), StringSplitOptions.RemoveEmptyEntries));

    return Path.Combine(_storagePath, $"{safeKey}.json");
  }
}
