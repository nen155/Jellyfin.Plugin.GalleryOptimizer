using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.Net.Http.Headers;

namespace Jellyfin.Plugin.GalleryOptimizer.Client;

/// <summary>
/// Inyecta un script en el documento SPA de jellyfin-web. La app de webOS es
/// un envoltorio que carga la interfaz servida por Jellyfin, así que este es
/// el punto donde se puede modificar el cliente.
/// </summary>
public static class HtmlScriptInjector
{
    /// <summary>
    /// Indica si la ruta corresponde al documento principal de jellyfin-web.
    /// </summary>
    /// <param name="path">Ruta de la petición.</param>
    /// <returns><c>true</c> si es el documento SPA.</returns>
    public static bool IsSpaDocument(PathString path)
    {
        var value = path.Value;
        if (value is null)
        {
            return false;
        }

        return value.Equals("/", StringComparison.OrdinalIgnoreCase)
            || value.Equals("/web", StringComparison.OrdinalIgnoreCase)
            || value.Equals("/web/", StringComparison.OrdinalIgnoreCase)
            || value.EndsWith("/index.html", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Ejecuta el resto del pipeline y reescribe el HTML para incluir
    /// <paramref name="scriptTag"/> justo antes de <c>&lt;/body&gt;</c>.
    /// </summary>
    /// <param name="context">Contexto HTTP.</param>
    /// <param name="next">Siguiente middleware.</param>
    /// <param name="scriptTag">Etiqueta del script a inyectar.</param>
    /// <returns>Task.</returns>
    public static async Task InjectAsync(HttpContext context, RequestDelegate next, string scriptTag)
    {
        var originalBody = context.Response.Body;

        // Sin Accept-Encoding la compresión de respuestas no actúa y el HTML
        // llega en texto plano, listo para reescribir.
        context.Request.Headers.Remove(HeaderNames.AcceptEncoding);

        await using var buffer = new MemoryStream();
        context.Response.Body = buffer;

        await next(context).ConfigureAwait(false);

        var isHtml = context.Response.StatusCode == StatusCodes.Status200OK
            && context.Response.ContentType?.Contains("text/html", StringComparison.OrdinalIgnoreCase) == true;

        byte[]? rewritten = null;
        if (isHtml)
        {
            buffer.Seek(0, SeekOrigin.Begin);
            var html = await new StreamReader(buffer, Encoding.UTF8)
                .ReadToEndAsync()
                .ConfigureAwait(false);

            if (!html.Contains(scriptTag, StringComparison.Ordinal))
            {
                html = html.Replace("</body>", scriptTag + "</body>", StringComparison.OrdinalIgnoreCase);
            }

            rewritten = Encoding.UTF8.GetBytes(html);
        }

        context.Response.Body = originalBody;

        if (rewritten is null)
        {
            buffer.Seek(0, SeekOrigin.Begin);
            await buffer.CopyToAsync(context.Response.Body, context.RequestAborted).ConfigureAwait(false);
            return;
        }

        context.Response.ContentLength = rewritten.Length;
        await context.Response.Body.WriteAsync(rewritten, context.RequestAborted).ConfigureAwait(false);
    }
}
