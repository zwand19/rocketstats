namespace RocketStats.Application.Abstractions;

public interface IAppendOnlyStorageService
{
  Task AppendAsync<T>(
    string key,
    T value,
    CancellationToken cancellationToken = default);

  Task<IReadOnlyList<T>> ReadAllAsync<T>(
    string key,
    CancellationToken cancellationToken = default);

  Task TrimAsync(
    string key,
    int keepLast,
    CancellationToken cancellationToken = default);

  Task RewriteAsync<T>(
    string key,
    IEnumerable<T> rows,
    CancellationToken cancellationToken = default);

  Task UpdateLastAsync<T>(
    string key,
    T row,
    CancellationToken cancellationToken = default);
}
