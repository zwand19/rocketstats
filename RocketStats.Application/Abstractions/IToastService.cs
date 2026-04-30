namespace RocketStats.Application.Abstractions;

public interface IToastService
{
  string? CurrentMessage { get; }

  event Action? Changed;

  void Show(string message);

  void Clear();
}
