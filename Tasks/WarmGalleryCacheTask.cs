using Jellyfin.Data.Enums;
using Jellyfin.Plugin.GalleryOptimizer.Configuration;
using MediaBrowser.Controller.Drawing;
using MediaBrowser.Controller.Entities;
using MediaBrowser.Controller.Library;
using MediaBrowser.Model.Entities;
using MediaBrowser.Model.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.GalleryOptimizer.Tasks;

/// <summary>
/// Tarea programada que recorre las fotos de las bibliotecas seleccionadas y
/// pre-genera en la caché de imágenes de Jellyfin la versión reducida que el
/// middleware servirá a los clientes, evitando redimensionar en caliente.
/// </summary>
public class WarmGalleryCacheTask : IScheduledTask
{
    private static readonly StringComparison PathComparison =
        OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

    private readonly ILibraryManager _libraryManager;
    private readonly IImageProcessor _imageProcessor;
    private readonly ILogger<WarmGalleryCacheTask> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="WarmGalleryCacheTask"/> class.
    /// </summary>
    /// <param name="libraryManager">Gestor de bibliotecas.</param>
    /// <param name="imageProcessor">Procesador de imágenes.</param>
    /// <param name="logger">Logger.</param>
    public WarmGalleryCacheTask(
        ILibraryManager libraryManager,
        IImageProcessor imageProcessor,
        ILogger<WarmGalleryCacheTask> logger)
    {
        _libraryManager = libraryManager;
        _imageProcessor = imageProcessor;
        _logger = logger;
    }

    /// <inheritdoc />
    public string Name => "Pre-generar imágenes optimizadas de galería";

    /// <inheritdoc />
    public string Key => "GalleryOptimizerWarmCache";

    /// <inheritdoc />
    public string Description =>
        "Genera en caché las versiones reducidas de las fotos de las bibliotecas "
        + "seleccionadas en Gallery Optimizer, para que los clientes (p. ej. webOS) "
        + "no disparen un redimensionado en caliente.";

    /// <inheritdoc />
    public string Category => "Gallery Optimizer";

    /// <inheritdoc />
    public IEnumerable<TaskTriggerInfo> GetDefaultTriggers()
    {
        return new[]
        {
            new TaskTriggerInfo
            {
                Type = TaskTriggerInfoType.IntervalTrigger,
                IntervalTicks = TimeSpan.FromHours(24).Ticks
            }
        };
    }

    /// <inheritdoc />
    public async Task ExecuteAsync(IProgress<double> progress, CancellationToken cancellationToken)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.Enabled || config.TargetLibraries.Count == 0)
        {
            _logger.LogInformation("Gallery Optimizer desactivado o sin bibliotecas seleccionadas; tarea omitida.");
            progress.Report(100);
            return;
        }

        var roots = ResolveTargetRoots(config);
        if (roots.Length == 0)
        {
            progress.Report(100);
            return;
        }

        var maxW = Math.Max(64, config.MaxWidth);
        var maxH = Math.Max(64, config.MaxHeight);
        var formats = _imageProcessor.GetSupportedImageOutputFormats();

        var photos = _libraryManager
            .GetItemList(new InternalItemsQuery
            {
                IncludeItemTypes = new[] { BaseItemKind.Photo },
                Recursive = true
            })
            .Where(item => !item.IsVirtualItem
                && !string.IsNullOrEmpty(item.Path)
                && roots.Any(root => IsPathInside(item.Path, root)))
            .ToList();

        _logger.LogInformation("Gallery Optimizer: procesando {Count} fotos.", photos.Count);

        for (var i = 0; i < photos.Count; i++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var photo = photos[i];
            try
            {
                var imageInfo = photo.GetImageInfo(ImageType.Primary, 0);
                if (imageInfo is not null)
                {
                    await _imageProcessor.ProcessImage(new ImageProcessingOptions
                    {
                        Item = photo,
                        ItemId = photo.Id,
                        Image = imageInfo,
                        ImageIndex = 0,
                        MaxWidth = maxW,
                        MaxHeight = maxH,
                        Quality = Math.Clamp(config.Quality, 1, 100),
                        SupportedOutputFormats = formats,
                        RequiresAutoOrientation = true
                    }).ConfigureAwait(false);
                }
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _logger.LogWarning(ex, "No se pudo optimizar {Path}", photo.Path);
            }

            progress.Report((i + 1) * 100.0 / photos.Count);
        }
    }

    private string[] ResolveTargetRoots(PluginConfiguration config)
    {
        return _libraryManager.GetVirtualFolders()
            .Where(vf => config.TargetLibraries.Contains(vf.Name, StringComparer.OrdinalIgnoreCase))
            .SelectMany(vf => vf.Locations)
            .Where(loc => !string.IsNullOrWhiteSpace(loc))
            .ToArray();
    }

    private static bool IsPathInside(string path, string root)
    {
        if (!path.StartsWith(root, PathComparison))
        {
            return false;
        }

        return path.Length == root.Length || path[root.Length] is '/' or '\\';
    }
}
