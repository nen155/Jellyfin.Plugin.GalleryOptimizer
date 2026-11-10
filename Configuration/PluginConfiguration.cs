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
        MaxHeight = 1080;
        Quality = 80;
        ClientFilter = string.Empty;
        RewritePhotoDownloads = true;
        CarouselCleanupEnabled = true;
        MaxLoadedImages = 2;
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
    /// Vacío = se aplica a todos los clientes. Ej.: "Web0S" para solo la TV LG.
    /// </summary>
    public string ClientFilter { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether las peticiones de descarga de una
    /// foto (<c>/Items/{id}/Download</c>) se redirigen al pipeline de imágenes,
    /// para que nunca se sirva el original sin redimensionar.
    /// </summary>
    public bool RewritePhotoDownloads { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether se inyecta el script que libera
    /// de memoria las imágenes del carrusel que salen del viewport.
    /// </summary>
    public bool CarouselCleanupEnabled { get; set; }

    /// <summary>
    /// Gets or sets el número máximo de imágenes adicionales (fuera de pantalla)
    /// que el cliente puede mantener decodificadas en memoria.
    /// </summary>
    public int MaxLoadedImages { get; set; }

    /// <summary>
    /// Gets or sets the names of the libraries (virtual folders) afectadas.
    /// Vacío = todas las bibliotecas.
    /// </summary>
    public List<string> TargetLibraries { get; set; }
}
