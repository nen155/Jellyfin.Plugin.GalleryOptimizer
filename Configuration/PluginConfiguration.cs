using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.GalleryOptimizer.Configuration;

/// <summary>
/// Configuración del plugin Gallery Optimizer.
/// </summary>
public class PluginConfiguration : BasePluginConfiguration
{
    /// <summary>
    /// Initializes a new instance of the <see cref="PluginConfiguration"/> class.
    /// </summary>
    public PluginConfiguration()
    {
        Enabled = true;
        MaxWidth = 1920;
        MaxHeight = 1920;
        Quality = 80;
        ClientFilter = string.Empty;
        CarouselCleanupEnabled = true;
        MaxLoadedImages = 30;
        TargetLibraries = new List<string>();
    }

    /// <summary>
    /// Gets or sets a value indicating whether el limitador está activo.
    /// </summary>
    public bool Enabled { get; set; }

    /// <summary>
    /// Gets or sets el ancho máximo en píxeles que se servirá.
    /// </summary>
    public int MaxWidth { get; set; }

    /// <summary>
    /// Gets or sets el alto máximo en píxeles que se servirá.
    /// </summary>
    public int MaxHeight { get; set; }

    /// <summary>
    /// Gets or sets la calidad máxima (1-100) de compresión servida.
    /// </summary>
    public int Quality { get; set; }

    /// <summary>
    /// Gets or sets un filtro de User-Agent (substring, insensible a mayúsculas).
    /// Vacío = se aplica a todos los clientes. Ej.: "WebOS" para solo la TV LG.
    /// </summary>
    public string ClientFilter { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether se inyecta el script que libera
    /// de memoria las imágenes del carrusel que salen del viewport.
    /// </summary>
    public bool CarouselCleanupEnabled { get; set; }

    /// <summary>
    /// Gets or sets el número máximo de imágenes con bitmap decodificado
    /// retenidas en memoria por el limpiador del carrusel.
    /// </summary>
    public int MaxLoadedImages { get; set; }

    /// <summary>
    /// Gets or sets the names of the libraries (virtual folders) afectadas.
    /// Vacío = todas las bibliotecas.
    /// </summary>
    public List<string> TargetLibraries { get; set; }
}
