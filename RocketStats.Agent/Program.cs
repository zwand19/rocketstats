using System.Globalization;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RocketStats.Agent.Buffer;
using RocketStats.Agent.Ingest;
using RocketStats.Agent.Options;
using RocketStats.Agent.Workers;
using RocketStats.Application.Abstractions;
using RocketStats.Infrastructure;
using Velopack;
using Velopack.Sources;

static class Program
{
  static void Main(string[] args)
  {
    VelopackApp.Build().Run();
    RunAsync(args).GetAwaiter().GetResult();
  }

  static async Task<int> RunAsync(string[] args)
  {
    if (TryHandleCliCommand(args, out var exitCode))
    {
      return exitCode;
    }

    var builder = Host.CreateApplicationBuilder(args);

    builder.Configuration.AddEnvironmentVariables(prefix: "ROCKETSTATS_");

    builder.Services.AddRocketStatsInfrastructure(builder.Configuration);
    builder.Services.Configure<AgentOptions>(builder.Configuration.GetSection(AgentOptions.SectionName));

    builder.Services.AddSingleton<StatsApiBuffer>();
    builder.Services.AddSingleton<IStatsRecorder>(sp => sp.GetRequiredService<StatsApiBuffer>());

    builder.Services.AddHttpClient<IngestClient>(client =>
    {
      client.Timeout = TimeSpan.FromSeconds(15);
    });

    builder.Services.AddHostedService<ListenerWorker>();
    builder.Services.AddHostedService<BatchSenderWorker>();

    var host = builder.Build();

    var updateUrl = builder.Configuration["Agent:UpdateUrl"];
    if (!string.IsNullOrWhiteSpace(updateUrl))
    {
      await CheckForUpdatesAsync(updateUrl);
    }

    await host.RunAsync();
    return 0;
  }

  static bool TryHandleCliCommand(string[] args, out int exitCode)
  {
    exitCode = 0;

    if (args.Length == 0)
    {
      return false;
    }

    if (string.Equals(args[0], "--help", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(args[0], "-h", StringComparison.OrdinalIgnoreCase))
    {
      PrintHelp();
      return true;
    }

    if (string.Equals(args[0], "--write-rl-config", StringComparison.OrdinalIgnoreCase))
    {
      if (args.Length != 4)
      {
        Console.Error.WriteLine("Usage: --write-rl-config <path-to-DefaultStatsAPI.ini> <packet-rate> <port>");
        exitCode = 1;
        return true;
      }

      if (!double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var packetRate))
      {
        Console.Error.WriteLine("packet-rate must be a number.");
        exitCode = 1;
        return true;
      }

      if (!int.TryParse(args[3], NumberStyles.Integer, CultureInfo.InvariantCulture, out var port))
      {
        Console.Error.WriteLine("port must be an integer.");
        exitCode = 1;
        return true;
      }

      var services = new ServiceCollection();
      services.AddLogging(b => b.AddConsole());
      var fakeConfig = new ConfigurationBuilder().Build();
      services.AddRocketStatsInfrastructure(fakeConfig);
      using var provider = services.BuildServiceProvider();
      var configService = provider.GetRequiredService<IRocketLeagueConfigService>();
      var result = configService.WriteAsync(args[1], packetRate, port).GetAwaiter().GetResult();

      if (!result.Success)
      {
        Console.Error.WriteLine($"Failed to write config: {result.Error}");
        exitCode = 1;
        return true;
      }

      Console.WriteLine($"Wrote {args[1]}: PacketSendRate={packetRate}, Port={port}.");
      Console.WriteLine("Restart Rocket League to apply.");
      return true;
    }

    return false;
  }

  static async Task CheckForUpdatesAsync(string updateUrl)
  {
    try
    {
      var mgr = new UpdateManager(new GithubSource(updateUrl, null, false));
      var newVersion = await mgr.CheckForUpdatesAsync();
      if (newVersion == null)
      {
        return;
      }

      await mgr.DownloadUpdatesAsync(newVersion);
      mgr.ApplyUpdatesAndRestart(newVersion);
    }
    catch
    {
      // update failures should never prevent the agent from starting
    }
  }

  static void PrintHelp()
  {
    Console.WriteLine("RocketStats.Agent");
    Console.WriteLine();
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run --project RocketStats.Agent");
    Console.WriteLine("    Run the agent (listens to Rocket League's local Stats API and POSTs batches).");
    Console.WriteLine();
    Console.WriteLine("  dotnet run --project RocketStats.Agent -- --write-rl-config <path> <packet-rate> <port>");
    Console.WriteLine("    Write Rocket League's DefaultStatsAPI.ini and exit.");
    Console.WriteLine();
    Console.WriteLine("Configuration (appsettings.json or ROCKETSTATS_ env vars):");
    Console.WriteLine("  StatsApi:WebSocketUrl   Rocket League local Stats API endpoint (default ws://localhost:49123)");
    Console.WriteLine("  Agent:ServerUrl         Web server base URL (default http://localhost:3000)");
    Console.WriteLine("  Agent:IngestApiKey      Optional Bearer token for /api/ingest");
    Console.WriteLine("  Agent:FlushIntervalSeconds  How often to POST a batch (default 10)");
  }
}
