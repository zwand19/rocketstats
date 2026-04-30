using RocketStats.Application.Abstractions;

namespace RocketStats.Infrastructure.Services;

public sealed class ToastService : IToastService, IDisposable
{
  private static readonly TimeSpan DefaultDuration = TimeSpan.FromSeconds(3);

  private readonly object _lock = new();
  private CancellationTokenSource? _cts;

  public string? CurrentMessage { get; private set; }

  public event Action? Changed;

  public void Show(string message)
  {
    CancellationToken token;

    lock (_lock)
    {
      CurrentMessage = message;
      _cts?.Cancel();
      _cts?.Dispose();
      _cts = new CancellationTokenSource();
      token = _cts.Token;
    }

    Changed?.Invoke();
    _ = DismissAfterDelayAsync(message, token);
  }

  public void Clear()
  {
    lock (_lock)
    {
      _cts?.Cancel();
      _cts?.Dispose();
      _cts = null;
      CurrentMessage = null;
    }

    Changed?.Invoke();
  }

  public void Dispose()
  {
    lock (_lock)
    {
      _cts?.Cancel();
      _cts?.Dispose();
      _cts = null;
    }
  }

  private async Task DismissAfterDelayAsync(string message, CancellationToken cancellationToken)
  {
    try
    {
      await Task.Delay(DefaultDuration, cancellationToken);

      lock (_lock)
      {
        if (CurrentMessage != message)
        {
          return;
        }

        CurrentMessage = null;
      }

      Changed?.Invoke();
    }
    catch (OperationCanceledException)
    {
    }
  }
}
