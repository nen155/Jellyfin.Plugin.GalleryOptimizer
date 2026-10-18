# Jellyfin.Plugin.GalleryOptimizer

Plugin para **Jellyfin** que mejora la eficiencia de las galerías de fotos para
clientes con poca RAM (smart TVs **webOS**, LG NetCast, etc.).

## Problema

Cuando el cliente webOS pide fotos, Jellyfin puede servir la imagen original
(p. ej. 6000×4000, HEIC/RAW de varios MB). La TV decodifica el bitmap completo
en memoria y la galería se queda sin RAM.

## Qué hace

1. **Middleware de limitación** (`ImageClampMiddleware`, registrado vía
   `IStartupFilter`): en cada petición `*/Images/*` ajusta la query string:

   - `width`/`fillWidth`/`maxWidth` → se limitan a `MaxWidth`;
     `height`/`fillHeight`/`maxHeight` → a `MaxHeight`.
   - Si el cliente **no** pide tamaño (imagen original) → se inyectan
     `maxWidth`/`maxHeight` con el tope.
   - `quality` se limita al máximo configurado.
   - **Ámbito por biblioteca**: el middleware resuelve el `itemId` de la URL
     hasta su biblioteca raíz (con caché) y solo limita las de las bibliotecas
     seleccionadas. Lista vacía = todas.

2. **Limpieza de carrusel en el cliente** (`carousel-cleanup.js`): script
   inyectado en `index.html` de jellyfin-web (la app webOS lo carga igual) que
   observa los `<img>`: los que salen del viewport se sustituyen por un píxel
   transparente (liberando el bitmap decodificado) y se restauran al volver.
   Solo libera cuando hay más de `MaxLoadedImages` imágenes retenidas.

3. **Tarea programada** "Pre-generar imágenes optimizadas de galería":
   recorre las fotos (`BaseItemKind.Photo`) de las bibliotecas seleccionadas y
   llama a `IImageProcessor.ProcessImage` con el tope configurado, llenando la
   caché de imágenes (`cache/images/resized-images`) por adelantado.

## Configuración

**Panel → Plugins → Gallery Optimizer**:

- **Dimensión máxima** (def. `1920`) — tope de ancho/alto servido.
- **Calidad máxima** (def. `80`).
- **Filtro de cliente** — substring del User-Agent; vacío = todos.
  `WebOS` para limitar solo a la TV LG.
- **Bibliotecas** — alcance de la tarea de calentado de caché
  (el middleware aplica a todas las imágenes, de cualquier biblioteca).

Tras guardar, ejecuta manualmente la tarea en **Panel → Tareas programadas**
para pre-calentar la caché.

## Limitaciones conocidas

- El script de limpieza actúa sobre elementos `<img>`; si alguna vista usa
  `background-image` CSS, esos bitmaps no se liberan.
- Sustituir `src` por un píxel provoca una recarga al volver a la imagen
  (normalmente instantánea, la caché del servidor la tiene ya reducida).
- Los ficheros originales en disco no se tocan.

## Compilación e instalación

Se compila contra las DLLs del servidor (`D:\Jellyfin\Server`, Jellyfin 12.1):

```powershell
C:\Users\NeN\.dotnet10\dotnet.exe publish -c Release
```

Copiar `bin\Release\net10.0\publish\Jellyfin.Plugin.GalleryOptimizer.dll` a
`D:\Jellyfin\Data\plugins\GalleryOptimizer\` y reiniciar Jellyfin.
