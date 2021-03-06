﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Wrido.Logging;

namespace Wrido.Plugin.StackExchange.Common
{
  public interface IStackExchangeClient
  {
    Task<IList<Question>> SearchAsync(SearchQuery query, CancellationToken ct = default);
  }

  public class StackExchangeClient : IStackExchangeClient
  {
    private readonly HttpClient _httpClient;
    private readonly IQueryStringBuilder _queryStringBuilder;
    private readonly JsonSerializer _serializer;
    private readonly ILogger _logger;

    public StackExchangeClient(HttpClient httpClient, IQueryStringBuilder queryStringBuilder, JsonSerializer serializer, ILogger logger)
    {
      _httpClient = httpClient;
      _queryStringBuilder = queryStringBuilder;
      _serializer = serializer;
      _logger = logger;
    }

    public async Task<IList<Question>> SearchAsync(SearchQuery query, CancellationToken ct = default)
    {
      if (string.IsNullOrEmpty(query?.InTitle) && (!query?.Tagged?.Any() ?? true))
      {
        throw new ArgumentException("'InTitle' and 'Tagged' can not both be unassigned.", nameof(query));
      }

      var queryString = _queryStringBuilder.Build(query);

      ct.ThrowIfCancellationRequested();
      var response = await _httpClient.GetAsync(queryString, ct);
      ct.ThrowIfCancellationRequested();

      if (!response.IsSuccessStatusCode)
      {
        _logger.Error($"StackExchange returned status code {response.StatusCode}. Reason: {response.ReasonPhrase}.");
        throw new HttpRequestException($"StackExchange returned status code {response.StatusCode}. Reason: {response.ReasonPhrase}.");
      }

      var jsonPayload = await response.Content.ReadAsStringAsync();

      using (var textReader = new StringReader(jsonPayload))
      using (var jsonReader = new JsonTextReader(textReader))
      {
        try
        {
          var questions = _serializer.Deserialize<PaginationResult<Question>>(jsonReader);
          _logger.Information("The search phrase {inTitle} resulted in {questionCount} found questions.", query.InTitle, questions?.Items.Count);
          return questions?.Items;
        }
        catch (Exception e)
        {
          _logger.Error(e, "An unhandled error occured when deserializing response.");
          throw;
        }
        finally
        {
          response.Dispose();
        }
      }
    }
  }
}
