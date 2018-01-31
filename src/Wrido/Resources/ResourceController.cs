﻿using System;
using System.Binary;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.StaticFiles;
using Quobject.EngineIoClientDotNet.Modules;

namespace Wrido.Resources
{
  public class ResourceController : Controller
  {
    private readonly IContentTypeProvider _contentTypeProvider;
    private readonly Dictionary<string, EmbeddedResource> _resources;

    public ResourceController(IEnumerable<EmbeddedResource> resources, IContentTypeProvider contentTypeProvider)
    {
      _contentTypeProvider = contentTypeProvider;
      _resources = resources.ToDictionary(r => r.ResourcePath, r => r, StringComparer.OrdinalIgnoreCase);
    }

    [HttpGet("resources/{*resourcePath}")]
    public ActionResult GetResource(string resourcePath)
    {
      if (!_resources.ContainsKey(resourcePath))
      {
        return NotFound();
      }
      var resource = _resources[resourcePath];

      if (_contentTypeProvider.TryGetContentType(resourcePath, out var contentType))
      {

        // temp fix to remove null character
        if (contentType.EndsWith("script"))
        {
          var dataAsString = Encoding.UTF8.GetString(resource.Data).Replace("\0", string.Empty);
          var dataAsBytes = Encoding.UTF8.GetBytes(dataAsString);
          return File(dataAsBytes, contentType);
        }

        return File(resource.Data, contentType);
      }

      return File(resource.Data, "text/plain");
    }

    [HttpGet("preview/{*filePath}")]
    public ActionResult GetFilePreview(string filePath)
    {
      if (!System.IO.File.Exists(filePath))
      {
        return NotFound();
      }

      var fileBytes = System.IO.File.ReadAllBytes(filePath);
      if (_contentTypeProvider.TryGetContentType(filePath, out var contentType))
      {
        return File(fileBytes, contentType);
      }

      return File(fileBytes, "text/plain");
    }
  }
}
