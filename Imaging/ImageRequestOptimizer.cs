using System.Globalization;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Primitives;

namespace Jellyfin.Plugin.GalleryOptimizer.Imaging;

/// <summary>
/// Reglas de reescritura de peticiones de imagen.
/// Es lógica pura (sin dependencias de Jellyfin ni de DI) para poder probarla
/// de forma aislada y para que el middleware solo se encargue de orquestar.
/// </summary>
public static class ImageRequestOptimizer
{
    private const string ItemsSegment = "/Items/";
    private const string ImagesSegment = "/Images/";
    private const string DownloadSuffix = "/Download";
    private const string QualityParam = "quality";

    private static readonly string[] WidthParams = { "width", "maxwidth", "fillwidth" };
    private static readonly string[] HeightParams = { "height", "maxheight", "fillheight" };

    private static readonly HashSet<string> PhotoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".gif", ".bmp", ".webp",
        ".heic", ".heif", ".tif", ".tiff", ".avif", ".jxl"
    };

    /// <summary>
    /// Indica si la ruta corresponde al endpoint de imágenes de Jellyfin
    /// (<c>*/Images/*</c>).
    /// </summary>
    /// <param name="path">Ruta de la petición.</param>
    /// <returns><c>true</c> si es una petición de imagen.</returns>
    public static bool IsImagePath(PathString path)
        => path.Value?.Contains(ImagesSegment, StringComparison.OrdinalIgnoreCase) == true;

    /// <summary>
    /// Indica si el fichero de un item es una imagen, a partir de su extensión.
    /// </summary>
    /// <param name="itemPath">Ruta en disco del item.</param>
    /// <returns><c>true</c> si la extensión es de imagen.</returns>
    public static bool IsPhotoFile(string? itemPath)
        => !string.IsNullOrEmpty(itemPath)
            && PhotoExtensions.Contains(Path.GetExtension(itemPath));

    /// <summary>
    /// Extrae el identificador del item presente en la ruta
    /// (<c>.../Items/{id}/...</c>), admitiendo un prefijo de subruta.
    /// </summary>
    /// <param name="path">Ruta de la petición.</param>
    /// <param name="itemId">Identificador encontrado.</param>
    /// <returns><c>true</c> si la ruta contiene un identificador válido.</returns>
    public static bool TryGetItemId(string? path, out Guid itemId)
    {
        itemId = Guid.Empty;
        if (string.IsNullOrEmpty(path))
        {
            return false;
        }

        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i + 1 < segments.Length; i++)
        {
            if (segments[i].Equals("Items", StringComparison.OrdinalIgnoreCase)
                && Guid.TryParse(segments[i + 1], out itemId))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Traduce <c>/Items/{id}/Download</c> a <c>/Items/{id}/Images/Primary</c>.
    /// La descarga devuelve el fichero original (p. ej. 6000×4000): el cliente
    /// lo decodifica entero y agota la RAM. La ruta de imagen sí pasa por el
    /// redimensionado de Jellyfin.
    /// </summary>
    /// <param name="path">Ruta de la petición.</param>
    /// <param name="imagePath">Ruta equivalente dentro del pipeline de imágenes.</param>
    /// <returns><c>true</c> si la ruta era una descarga de item.</returns>
    public static bool TryGetPrimaryImagePath(PathString path, out PathString imagePath)
    {
        imagePath = default;

        var value = path.Value;
        if (string.IsNullOrEmpty(value)
            || !value.EndsWith(DownloadSuffix, StringComparison.OrdinalIgnoreCase)
            || !TryGetItemId(value, out var itemId))
        {
            return false;
        }

        var prefixIndex = value.IndexOf(ItemsSegment, StringComparison.OrdinalIgnoreCase);
        if (prefixIndex < 0)
        {
            return false;
        }

        imagePath = new PathString(
            string.Concat(value.AsSpan(0, prefixIndex), ItemsSegment, itemId.ToString("D"), "/Images/Primary"));
        return true;
    }

    /// <summary>
    /// Acota los parámetros de tamaño y calidad, y garantiza que la petición
    /// nunca se resuelva contra el original sin redimensionar.
    /// </summary>
    /// <param name="query">Query string original.</param>
    /// <param name="limits">Límites a aplicar.</param>
    /// <returns>Query string reescrita.</returns>
    public static QueryString Clamp(IQueryCollection query, ImageLimits limits)
    {
        var values = new Dictionary<string, StringValues>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in query)
        {
            values[pair.Key] = pair.Value;
        }

        ClampParams(values, WidthParams, limits.MaxWidth);
        ClampParams(values, HeightParams, limits.MaxHeight);
        ClampParams(values, new[] { QualityParam }, limits.Quality);

        AddIfMissing(values, "maxWidth", limits.MaxWidth);
        AddIfMissing(values, "maxHeight", limits.MaxHeight);
        AddIfMissing(values, QualityParam, limits.Quality);

        return QueryString.Create(
            values.Select(pair => new KeyValuePair<string, string?>(pair.Key, pair.Value.ToString())));
    }

    private static void AddIfMissing(IDictionary<string, StringValues> values, string name, int value)
    {
        if (!values.ContainsKey(name))
        {
            values[name] = value.ToString(CultureInfo.InvariantCulture);
        }
    }

    private static void ClampParams(IDictionary<string, StringValues> values, string[] names, int cap)
    {
        foreach (var name in names)
        {
            if (values.TryGetValue(name, out var raw)
                && int.TryParse(raw.ToString(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var size)
                && size > cap)
            {
                values[name] = cap.ToString(CultureInfo.InvariantCulture);
            }
        }
    }
}
