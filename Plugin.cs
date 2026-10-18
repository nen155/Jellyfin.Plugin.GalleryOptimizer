using Jellyfin.Plugin.GalleryOptimizer.Configuration;
using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.GalleryOptimizer;

/// <summary>
/// Plugin principal: limita el tamaño/calidad de las imágenes servidas y
/// pre-genera miniaturas para reducir el consumo de RAM en clientes limitados
/// (p. ej. smart TVs webOS).
/// </summary>
public class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Plugin"/> class.
    /// </summary>
    /// <param name="applicationPaths">Rutas de la aplicación.</param>
    /// <param name="xmlSerializer">Serializador XML de configuración.</param>
    public Plugin(IApplicationPaths applicationPaths, IXmlSerializer xmlSerializer)
        : base(applicationPaths, xmlSerializer)
    {
        Instance = this;
    }

    /// <summary>
    /// Gets the current plugin instance.
    /// </summary>
    public static Plugin? Instance { get; private set; }

    /// <inheritdoc />
    public override string Name => "Gallery Optimizer";

    /// <inheritdoc />
    public override string Description =>
        "Limita las dimensiones de las imágenes servidas y pre-genera miniaturas "
        + "optimizadas para evitar que clientes con poca RAM (webOS) se queden sin memoria.";

    /// <inheritdoc />
    public override Guid Id => Guid.Parse("a41d7f2c-3e8b-4c6a-9d1e-2f7a5b8c4d09");

    /// <inheritdoc />
    public IEnumerable<PluginPageInfo> GetPages()
    {
        return new[]
        {
            new PluginPageInfo
            {
                Name = "GalleryOptimizer",
                EmbeddedResourcePath = $"{GetType().Namespace}.Configuration.configPage.html",
                EnableInMainMenu = true,
                MenuSection = "server",
                MenuIcon = "photo_size_select_small"
            }
        };
    }
}
