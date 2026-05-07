using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RocketStats.Agent.Options;

namespace RocketStats.Agent.Ingest;

public sealed class IngestClient
{
  private readonly HttpClient _http;
  private readonly AgentOptions _options;
  private readonly ILogger<IngestClient> _logger;

  public IngestClient(HttpClient http, IOptions<AgentOptions> options, ILogger<IngestClient> logger)
  {
    _http = http;
    _options = options.Value;
    _logger = logger;
  }

  public async Task<bool> SendAsync(IngestPayload payload, CancellationToken cancellationToken)
  {
    var url = $"{_options.ServerUrl.TrimEnd('/')}/api/ingest";

    using var request = new HttpRequestMessage(HttpMethod.Post, url)
    {
      Content = JsonContent.Create(payload, options: IngestJsonOptions.Default),
    };

    if (!string.IsNullOrWhiteSpace(_options.IngestApiKey))
    {
      request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.IngestApiKey);
    }

    try
    {
      using var response = await _http.SendAsync(request, cancellationToken);

      if (!response.IsSuccessStatusCode)
      {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        _logger.LogWarning(
          "Ingest POST returned {StatusCode}: {Body}",
          (int)response.StatusCode,
          body);
        return false;
      }

      return true;
    }
    catch (Exception exception)
    {
      _logger.LogWarning(exception, "Ingest POST to {Url} failed.", url);
      return false;
    }
  }
}
