using System.Collections.Concurrent;
using System.Text;
using Jellyfin.Plugin.GalleryOptimizer.Configuration;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.GalleryOptimizer.Middleware;

/// <summary>
/// Middleware con tres funciones:
/// 1. Limita tamaño/calidad de las peticiones de imagen (<c>*/Images/*</c>),
///    restringido opcionalmente a las bibliotecas seleccionadas.
/// 2. Sirve <c>/GalleryOptimizer/carousel-cleanup.js</c>: script que libera
///    de la memoria del cliente los bitmaps de imágenes fuera del viewport.
/// 3. Inyecta ese script en las páginas <c>index.html</c> de jellyfin-web
///    (cliente usado por la app webOS).
/// </summary>
public class ImageClampMiddleware
{
    private const string CleanupScriptPath = "/GalleryOptimizer/carousel-cleanup.js";
    private const string ScriptTag =
        "<script src=\"" + CleanupScriptPath + "\" defer></script>";

    private const string PlaceholderPixel =
        "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    private static readonly string[] WidthParams = { "width", "maxwidth", "fillwidth" };
    private static readonly string[] HeightParams = { "height", "maxheight", "fillheight" };
    private static readonly TimeSpan LibraryCacheTtl = TimeSpan.FromMinutes(10);

    private readonly RequestDelegate _next;
    private readonly ILibraryManager _libraryManager;
    private readonly ConcurrentDictionary<Guid, (string? Name, DateTime Expires)> _libraryCache = new();

    /// <summary>
    /// Initializes a new instance of the <see cref="ImageClampMiddleware"/> class.
    /// </summary>
    /// <param name="next">Siguiente middleware.</param>
    /// <param name="libraryManager">Gestor de bibliotecas.</param>
    public ImageClampMiddleware(RequestDelegate next, ILibraryManager libraryManager)
    {
        _next = next;
        _libraryManager = libraryManager;
    }

    /// <summary>
    /// Procesa la petición.
    /// </summary>
    /// <param name="context">Contexto HTTP.</param>
    /// <returns>Task.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.Enabled)
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var path = context.Request.Path;

        if (HttpMethods.IsGet(context.Request.Method)
            && path.Equals(CleanupScriptPath, StringComparison.OrdinalIgnoreCase))
        {
            await ServeCleanupScript(context, config).ConfigureAwait(false);
            return;
        }

        if (HttpMethods.IsGet(context.Request.Method)
            && config.CarouselCleanupEnabled
            && IsHtmlIndex(path)
            && ClientMatches(context.Request, config))
        {
            await InjectScriptIntoHtml(context).ConfigureAwait(false);
            return;
        }

        if (HttpMethods.IsGet(context.Request.Method)
            && IsImageRequest(path)
            && ClientMatches(context.Request, config)
            && LibraryMatches(context.Request, config))
        {
            ClampQuery(context.Request, config);
        }

        await _next(context).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // 1) Limitación de tamaño/calidad de imágenes
    // ------------------------------------------------------------------

    private static bool IsImageRequest(PathString path)
        => path.Value?.Contains("/Images/", StringComparison.OrdinalIgnoreCase) == true;

    private static bool ClientMatches(HttpRequest request, PluginConfiguration config)
        => string.IsNullOrWhiteSpace(config.ClientFilter)
        || request.Headers.UserAgent.ToString()
            .Contains(config.ClientFilter, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Comprueba si la petición pertenece a una biblioteca seleccionada.
    /// Lista vacía = todas. Para rutas no basadas en Items no se filtra.
    /// </summary>
    private bool LibraryMatches(HttpRequest request, PluginConfiguration config)
    {
        if (config.TargetLibraries.Count == 0)
        {
            return true;
        }

        var segments = request.Path.Value?.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments is null || segments.Length < 3
            || !segments[0].Equals("Items", StringComparison.OrdinalIgnoreCase)
            || !Guid.TryParse(segments[1], out var itemId))
        {
            return false;
        }

        var libraryName = GetLibraryName(itemId);
        return libraryName is not null
            && config.TargetLibraries.Contains(libraryName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resuelve la biblioteca raíz de un item, con caché para no consultar
    /// el LibraryManager en cada petición de imagen.
    /// </summary>
    private string? GetLibraryName(Guid itemId)
    {
        if (_libraryCache.TryGetValue(itemId, out var cached) && cached.Expires > DateTime.UtcNow)
        {
            return cached.Name;
        }

        var name = _libraryManager.GetItemById(itemId)?.GetTopParent()?.Name;
        _libraryCache[itemId] = (name, DateTime.UtcNow.Add(LibraryCacheTtl));
        return name;
    }

    private static void ClampQuery(HttpRequest request, PluginConfiguration config)
    {
        var maxW = Math.Max(64, config.MaxWidth);
        var maxH = Math.Max(64, config.MaxHeight);
        var qualityCap = Math.Clamp(config.Quality, 1, 100);

        var values = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var kv in request.Query)
        {
            values[kv.Key] = kv.Value;
        }

        var changed = false;
        changed |= ClampParams(values, WidthParams, maxW);
        changed |= ClampParams(values, HeightParams, maxH);
        changed |= ClampParams(values, new[] { "quality" }, qualityCap);

        // Sin tamaño explícito => el cliente pediría la imagen original.
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (!values.ContainsKey("maxWidth"))
        {
            values["maxWidth"] = maxW.ToString(inv);
            changed = true;
        }

        if (!values.ContainsKey("maxHeight"))
        {
            values["maxHeight"] = maxH.ToString(inv);
            changed = true;
        }

        if (!values.ContainsKey("quality"))
        {
            values["quality"] = qualityCap.ToString(inv);
            changed = true;
        }

        if (changed)
        {
            request.QueryString = QueryString.Create(
                values.Select(kv => new KeyValuePair<string, string?>(kv.Key, kv.Value.ToString())));
        }
    }

    private static bool ClampParams(Dictionary<string, StringValues> values, string[] names, int cap)
    {
        var changed = false;
        foreach (var param in names)
        {
            if (values.TryGetValue(param, out var raw)
                && int.TryParse(raw.ToString(), out var size)
                && size > cap)
            {
                values[param] = cap.ToString(System.Globalization.CultureInfo.InvariantCulture);
                changed = true;
            }
        }

        return changed;
    }

    // ------------------------------------------------------------------
    // 2) Script de limpieza de carrusel (libera RAM en el cliente)
    // ------------------------------------------------------------------

    private static async Task ServeCleanupScript(HttpContext context, PluginConfiguration config)
    {
        var script = BuildCleanupScript(Math.Max(5, config.MaxLoadedImages));
        var bytes = Encoding.UTF8.GetBytes(script);
        context.Response.ContentType = "application/javascript; charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    private static string BuildCleanupScript(int maxLoadedImages)
    {
        return $$"""
        /* GalleryOptimizer carousel cleanup: libera los bitmaps de las imágenes
           que salen del viewport para no agotar la RAM del cliente (webOS). */
        (function () {
          'use strict';
          var LIMIT = {{maxLoadedImages}};
          var PX = '{{PlaceholderPixel}}';
          var tracked = new Set();

          function isReleasable(el) {
            return el && !el.dataset.goSrc && el.src
              && el.src !== PX && el.src.indexOf('blob:') !== 0;
          }

          function release(img) {
            if (!isReleasable(img)) return;
            img.dataset.goSrc = img.src;
            img.src = PX;
          }

          function restore(img) {
            if (img && img.dataset.goSrc) {
              img.src = img.dataset.goSrc;
              delete img.dataset.goSrc;
            }
          }

          function retainedCount() {
            var n = 0;
            tracked.forEach(function (t) { if (t.isConnected && !t.dataset.goSrc) n++; });
            return n;
          }

          var observer = new IntersectionObserver(function (entries) {
            entries.forEach(function (entry) {
              var img = entry.target;
              if (entry.isIntersecting) {
                restore(img);
              } else if (retainedCount() > LIMIT) {
                release(img);
              }
            });
          }, { rootMargin: '150%' });

          function watch(node) {
            if (!node || tracked.has(node)) return;
            tracked.add(node);
            observer.observe(node);
          }

          function scan(root) {
            if (!root) return;
            if (root.tagName === 'IMG') watch(root);
            if (root.querySelectorAll) root.querySelectorAll('img').forEach(watch);
          }

          new MutationObserver(function (mutations) {
            mutations.forEach(function (m) { m.addedNodes.forEach(scan); });
          }).observe(document.documentElement, { childList: true, subtree: true });

          document.querySelectorAll('img').forEach(watch);

          /* Barrido periódico: purga nodos muertos y libera los más antiguos
             no visibles cuando se supera el límite. */
          setInterval(function () {
            tracked.forEach(function (t) { if (!t.isConnected) tracked.delete(t); });
            var retained = [];
            tracked.forEach(function (t) { if (!t.dataset.goSrc) retained.push(t); });
            if (retained.length <= LIMIT) return;
            var excess = retained.length - LIMIT;
            for (var i = 0; i < retained.length && excess > 0; i++) {
              var img = retained[i];
              var r = img.getBoundingClientRect();
              var nearView = r.bottom > -innerHeight && r.top < 2 * innerHeight;
              if (!nearView && isReleasable(img)) { release(img); excess--; }
            }
          }, 5000);
        })();
        """;
    }

    // ------------------------------------------------------------------
    // 3) Inyección del script en index.html de jellyfin-web
    // ------------------------------------------------------------------

    private static bool IsHtmlIndex(PathString path)
    {
        var value = path.Value;
        return value is not null
            && (value.Equals("/", StringComparison.OrdinalIgnoreCase)
                || value.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase));
    }

    private async Task InjectScriptIntoHtml(HttpContext context)
    {
        var originalBody = context.Response.Body;

        // Sin Accept-Encoding, la compresión de respuestas de Jellyfin no actúa
        // y podemos reescribir el HTML en texto plano.
        context.Request.Headers.Remove(HeaderNames.AcceptEncoding);

        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await _next(context).ConfigureAwait(false);

        var contentType = context.Response.ContentType;
        if (context.Response.StatusCode == StatusCodes.Status200OK
            && contentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true)
        {
            buffer.Seek(0, SeekOrigin.Begin);
            var html = await new StreamReader(buffer, Encoding.UTF8).ReadToEndAsync()
                .ConfigureAwait(false);

            if (!html.Contains(CleanupScriptPath, StringComparison.Ordinal))
            {
                html = html.Replace("</body>", ScriptTag + "</body>", StringComparison.OrdinalIgnoreCase);
            }

            var bytes = Encoding.UTF8.GetBytes(html);
            context.Response.Body = originalBody;
            context.Response.ContentLength = bytes.Length;
            await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        context.Response.Body = originalBody;
        buffer.Seek(0, SeekOrigin.Begin);
        await buffer.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
    }
}
