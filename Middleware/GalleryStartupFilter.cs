using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.GalleryOptimizer.Middleware;

/// <summary>
/// Inserta <see cref="ImageClampMiddleware"/> al inicio del pipeline HTTP de
/// Jellyfin mediante <see cref="IStartupFilter"/>.
/// </summary>
public class GalleryStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return app =>
        {
            app.UseMiddleware<ImageClampMiddleware>();
            next(app);
        };
    }
}
