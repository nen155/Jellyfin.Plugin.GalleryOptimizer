using Jellyfin.Plugin.GalleryOptimizer.Configuration;

namespace Jellyfin.Plugin.GalleryOptimizer.Imaging;

/// <summary>
/// Límites efectivos aplicados a una petición de imagen, ya saneados.
/// </summary>
/// <param name="MaxWidth">Ancho máximo en píxeles.</param>
/// <param name="MaxHeight">Alto máximo en píxeles.</param>
/// <param name="Quality">Calidad máxima de compresión (1-100).</param>
public readonly record struct ImageLimits(int MaxWidth, int MaxHeight, int Quality)
{
    /// <summary>
    /// Construye los límites a partir de la configuración del plugin,
    /// descartando valores fuera de rango.
    /// </summary>
    /// <param name="config">Configuración del plugin.</param>
    /// <returns>Límites saneados.</returns>
    public static ImageLimits From(PluginConfiguration config) => new(
        Math.Max(64, config.MaxWidth),
        Math.Max(64, config.MaxHeight),
        Math.Clamp(config.Quality, 1, 100));
}
