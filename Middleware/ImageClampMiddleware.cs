using System.Collections.Concurrent;
using System.Text;
using Jellyfin.Plugin.GalleryOptimizer.Client;
using Jellyfin.Plugin.GalleryOptimizer.Configuration;
using Jellyfin.Plugin.GalleryOptimizer.Imaging;
using MediaBrowser.Controller.Library;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.GalleryOptimizer.Middleware;

/// <summary>
/// Middleware con tres funciones:
/// 1. Reescribe las peticiones de imagen para que el servidor nunca entregue
///    el original sin redimensionar, restringido opcionalmente a las
///    bibliotecas y clientes seleccionados.
/// 2. Sirve <c>/GalleryOptimizer/carousel-cleanup.js</c>: script que acota los
///    bitmaps que el cliente retiene en memoria.
/// 3. Inyecta ese script en las páginas de jellyfin-web (la app webOS carga
///    la interfaz servida por Jellyfin).
/// </summary>
public class ImageClampMiddleware
{
    private const string CleanupScriptPath = "/GalleryOptimizer/carousel-cleanup.js";
    private const string ScriptTag = "<script src=\"" + CleanupScriptPath + "\" defer></script>";
    private const int MaxCachedItems = 5000;

    private static readonly TimeSpan ItemCacheTtl = TimeSpan.FromMinutes(10);

    private readonly RequestDelegate _next;
    private readonly ILibraryManager _libraryManager;
    private readonly ConcurrentDictionary<Guid, CachedItem> _itemCache = new();

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
        if (config is null || !config.Enabled || !HttpMethods.IsGet(context.Request.Method))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        var path = context.Request.Path;

        if (path.Equals(CleanupScriptPath, StringComparison.OrdinalIgnoreCase))
        {
            await ServeCleanupScript(context, config).ConfigureAwait(false);
            return;
        }

        if (!ClientMatches(context.Request, config))
        {
            await _next(context).ConfigureAwait(false);
            return;
        }

        if (config.CarouselCleanupEnabled && HtmlScriptInjector.IsSpaDocument(path))
        {
            await HtmlScriptInjector.InjectAsync(context, _next, ScriptTag).ConfigureAwait(false);
            return;
        }

        OptimizeImageRequest(context, config);

        await _next(context).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------
    // 1) Reescritura de peticiones de imagen
    // ------------------------------------------------------------------

    private void OptimizeImageRequest(HttpContext context, PluginConfiguration config)
    {
        var request = context.Request;
        var limits = ImageLimits.From(config);

        if (config.RewritePhotoDownloads
            && IsImageAccept(request)
            && ImageRequestOptimizer.TryGetPrimaryImagePath(request.Path, out var imagePath)
            && ItemMatches(request, config, mustBePhoto: true))
        {
            request.Path = imagePath;
            request.QueryString = ImageRequestOptimizer.Clamp(request.Query, limits);
            return;
        }

        if (ImageRequestOptimizer.IsImagePath(request.Path)
            && ItemMatches(request, config, mustBePhoto: false))
        {
            request.QueryString = ImageRequestOptimizer.Clamp(request.Query, limits);
        }
    }

    /// <summary>
    /// Una petición de descarga real (navegador, gestor de descargas) no pide
    /// imágenes en el Accept; un &lt;img&gt; sí. Así se evita romper las
    /// descargas de ficheros originales.
    /// </summary>
    private static bool IsImageAccept(HttpRequest request)
        => request.Headers.Accept.ToString().Contains("image/", StringComparison.OrdinalIgnoreCase);

    private static bool ClientMatches(HttpRequest request, PluginConfiguration config)
        => string.IsNullOrWhiteSpace(config.ClientFilter)
            || request.Headers.UserAgent.ToString()
                .Contains(config.ClientFilter, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Comprueba si la petición pertenece a una biblioteca seleccionada y, si
    /// se exige, si el item es una foto. Lista vacía = todas las bibliotecas.
    /// </summary>
    private bool ItemMatches(HttpRequest request, PluginConfiguration config, bool mustBePhoto)
    {
        if (!mustBePhoto && config.TargetLibraries.Count == 0)
        {
            return true;
        }

        if (!ImageRequestOptimizer.TryGetItemId(request.Path.Value, out var itemId))
        {
            return false;
        }

        var item = ResolveItem(itemId);
        if (mustBePhoto && !item.IsPhoto)
        {
            return false;
        }

        return config.TargetLibraries.Count == 0
            || (item.LibraryName is not null
                && config.TargetLibraries.Contains(item.LibraryName, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Resuelve biblioteca y tipo de un item, con caché para no consultar el
    /// LibraryManager en cada petición de imagen.
    /// </summary>
    private CachedItem ResolveItem(Guid itemId)
    {
        if (_itemCache.TryGetValue(itemId, out var cached) && cached.Expires > DateTime.UtcNow)
        {
            return cached;
        }

        var item = _libraryManager.GetItemById(itemId);
        cached = new CachedItem(
            item?.GetTopParent()?.Name,
            ImageRequestOptimizer.IsPhotoFile(item?.Path),
            DateTime.UtcNow.Add(ItemCacheTtl));

        if (_itemCache.Count >= MaxCachedItems)
        {
            _itemCache.Clear();
        }

        _itemCache[itemId] = cached;
        return cached;
    }

    // ------------------------------------------------------------------
    // 2) Script de limpieza de carrusel (libera RAM en el cliente)
    // ------------------------------------------------------------------

    private static async Task ServeCleanupScript(HttpContext context, PluginConfiguration config)
    {
        var bytes = Encoding.UTF8.GetBytes(CarouselScriptBuilder.Build(config.MaxLoadedImages));

        context.Response.ContentType = "application/javascript; charset=utf-8";
        context.Response.ContentLength = bytes.Length;
        context.Response.Headers.CacheControl = "no-cache";
        await context.Response.Body.WriteAsync(bytes, context.RequestAborted).ConfigureAwait(false);
    }

    private readonly record struct CachedItem(string? LibraryName, bool IsPhoto, DateTime Expires);
}
