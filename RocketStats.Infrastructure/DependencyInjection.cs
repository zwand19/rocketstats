using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using RocketStats.Application.Abstractions;
using RocketStats.Application.Options;
using RocketStats.Infrastructure.Services;

namespace RocketStats.Infrastructure;

public static class DependencyInjection
{
  public static IServiceCollection AddRocketStatsInfrastructure(
    this IServiceCollection services,
    IConfiguration configuration)
  {
    services.Configure<StatsApiOptions>(
      configuration.GetSection(StatsApiOptions.SectionName));

    services.AddSingleton<StatsApiListener>();
    services.AddSingleton<IRocketLeagueConfigService, RocketLeagueConfigService>();

    return services;
  }
}
