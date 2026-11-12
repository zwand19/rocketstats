using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RocketStats.Agent.Options;

namespace RocketStats.Agent.Ingest;

public enum IngestResult
{
  Sent,

  /// <summary>The server could not be reached or had a transient problem; try the same data again.</summary>
  RetryLater,

  /// <summary>The server refused this payload itself; sending it again can never succeed.</summary>
  Rejected,
}

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

  public async Task<IngestResult> SendAsync(IngestPayload payload, CancellationToken cancellationToken)
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

      if (response.IsSuccessStatusCode)
      {
        return IngestResult.Sent;
      }

      var body = await response.Content.ReadAsStringAsync(cancellationToken);
      var statusCode = (int)response.StatusCode;
      var rejected = statusCode is 400 or 413 or 422;

      _logger.Log(
        rejected ? LogLevel.Warning : LogLevel.Debug,
        "Ingest POST returned {StatusCode}: {Body}",
        statusCode,
        body.Length > 500 ? body[..500] : body);

      return rejected ? IngestResult.Rejected : IngestResult.RetryLater;
    }
    catch (Exception exception)
    {
      _logger.LogDebug(exception, "Ingest POST to {Url} failed.", url);
      return IngestResult.RetryLater;
    }
  }
}
