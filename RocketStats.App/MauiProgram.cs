using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using RocketStats.Infrastructure;

namespace RocketStats.App;

public static class MauiProgram
{
  public static MauiApp CreateMauiApp()
  {
    var builder = MauiApp.CreateBuilder();

    builder.UseMauiApp<App>();

    var environment = Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT") ?? "Production";

    builder.Configuration
      .AddJsonFile("appsettings.json", optional: false, reloadOnChange: false)
      .AddJsonFile($"appsettings.{environment}.json", optional: true, reloadOnChange: false)
      .AddEnvironmentVariables(prefix: "ROCKETSTATS_");

    builder.Services.AddMauiBlazorWebView();
    builder.Services.AddRocketStatsInfrastructure(builder.Configuration);
    builder.Logging.SetMinimumLevel(LogLevel.Information);
    builder.Logging.AddConsole();

#if DEBUG
    builder.Services.AddBlazorWebViewDeveloperTools();
    builder.Logging.AddDebug();
#endif

    return builder.Build();
  }
}
