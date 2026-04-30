namespace RocketStats.Application.Abstractions;

public interface ILocalStorageService
{
  Task<T?> ReadAsync<T>(
    string key,
    CancellationToken cancellationToken = default);

  Task WriteAsync<T>(
    string key,
    T value,
    CancellationToken cancellationToken = default);

  Task RemoveAsync(
    string key,
    CancellationToken cancellationToken = default);
}
