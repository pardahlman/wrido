﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Wrido.Configuration;
using Wrido.Logging;
using Wrido.Queries;
using Wrido.Queries.Events;
using IQueryProvider = Wrido.Queries.IQueryProvider;

namespace Wrido.Plugin.Wikipedia
{
  public class WikipediaProvider : IQueryProvider
  {
    private readonly HttpClient _httpClient;
    private readonly JsonSerializer _serializer;
    private readonly ILogger _logger;
    private readonly WikipediaPluginConfiguration _config;
    private const string CommonUrlPath = "/w/api.php?action=opensearch&profile=fuzzy&limit=5&search=";

    public WikipediaProvider(IConfigurationProvider configProvider, HttpClient httpClient, JsonSerializer serializer, ILogger logger)
    {
      _httpClient = httpClient;
      _serializer = serializer;
      _logger = logger;
      _config = configProvider.GetConfiguration<WikipediaPluginConfiguration>() ?? WikipediaPluginConfiguration.Fallback;
    }

    public bool CanHandle(Query query)
    {
      return string.Equals(query.Command, _config.Keyword, StringComparison.InvariantCultureIgnoreCase);
    }

    public async Task QueryAsync(Query query, IObserver<QueryEvent> observer, CancellationToken ct)
    {
      if (string.IsNullOrWhiteSpace(query.Argument))
      {
        foreach (var fallback in WikipediaResult.CreateFallback(_config.BaseUrls))
        {
          observer.OnNext(new ResultAvailable(fallback));
        }
        return;
      }

      var queryTasks = _config.BaseUrls
        .Select(url => QueryWikipediaAsync(url, query.Argument, ct))
        .ToList();
      await Task.WhenAll(queryTasks);

      var results = queryTasks.SelectMany(q => q.Result).ToList();
      if (!results.Any())
      {
        foreach (var searchResult in WikipediaResult.CreateSearch(Encode(query.Argument), _config.BaseUrls))
        {
          observer.OnNext(new ResultAvailable(searchResult));
        }
      }
      foreach (var queryResult in results)
      {
        observer.OnNext(new ResultAvailable(queryResult));
      }
    }

    private async Task<IEnumerable<QueryResult>> QueryWikipediaAsync(string baseUrl, string searchTerm, CancellationToken ct)
    {
      var requestUrl = $"{baseUrl}{CommonUrlPath}{Encode(searchTerm)}";
      var response = await _httpClient.GetAsync(requestUrl, ct);
      ct.ThrowIfCancellationRequested();

      if (!response.IsSuccessStatusCode)
      {
        return Enumerable.Empty<QueryResult>();
      }

      var jsonContent = await response.Content.ReadAsStringAsync();
      using (var textReader = new StringReader(jsonContent))
      using (var jsonReader = new JsonTextReader(textReader))
      {
        try
        {
          var wikiRespose = _serializer.Deserialize<WikipediaResponse>(jsonReader);
          _logger.Information("The search phrase {term} resulted in {suggestionCount} suggestions.", wikiRespose.Term, wikiRespose.Suggestions.Count);
          return WikipediaResult.Create(wikiRespose);
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

    private static string Encode(string term)
    {
      return WebUtility.UrlEncode(
        term.Replace(" ", "_")
          .Replace("#", "♯"));
    }
  }
}
