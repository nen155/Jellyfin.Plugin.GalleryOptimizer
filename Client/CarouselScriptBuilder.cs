using System.Globalization;
using System.Text;

namespace Jellyfin.Plugin.GalleryOptimizer.Client;

/// <summary>
/// Genera el script de limpieza de carrusel a partir de la plantilla
/// embebida <c>Client/carousel-cleanup.js</c>.
/// </summary>
public static class CarouselScriptBuilder
{
    private const string ResourceName =
        "Jellyfin.Plugin.GalleryOptimizer.Client.carousel-cleanup.js";

    private const string LimitToken = "__LIMIT__";
    private const string PlaceholderToken = "__PLACEHOLDER__";

    private const string PlaceholderPixel =
        "data:image/gif;base64,R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7";

    /// <summary>
    /// Techo de seguridad: por encima de este número de bitmaps retenidos
    /// (a 1080p, ~8 MB cada uno) el cliente vuelve a quedarse sin memoria,
    /// así que no se permite configurar más.
    /// </summary>
    private const int MaxRetainedCeiling = 12;

    private static readonly Lazy<string> Template = new(LoadTemplate, isThreadSafe: true);

    /// <summary>
    /// Construye el script con el límite de imágenes retenidas indicado.
    /// </summary>
    /// <param name="maxLoadedImages">Imágenes adicionales que se conservan fuera de pantalla.</param>
    /// <returns>Código JavaScript listo para servir.</returns>
    public static string Build(int maxLoadedImages)
    {
        var limit = Math.Clamp(maxLoadedImages, 1, MaxRetainedCeiling).ToString(CultureInfo.InvariantCulture);

        return Template.Value
            .Replace(LimitToken, limit, StringComparison.Ordinal)
            .Replace(PlaceholderToken, PlaceholderPixel, StringComparison.Ordinal);
    }

    private static string LoadTemplate()
    {
        var assembly = typeof(CarouselScriptBuilder).Assembly;
        using var stream = assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException(
                $"No se encontró el recurso embebido '{ResourceName}'.");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        return reader.ReadToEnd();
    }
}
