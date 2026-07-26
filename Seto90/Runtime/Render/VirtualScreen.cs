using System.Numerics;
using Raylib_cs;

namespace Seto90;

/// <summary>
/// Lienzo virtual retro: todo el juego se dibuja en 256x224 (u otra resolucion declarada)
/// y despues se presenta escalado por enteros con letterbox.
///
/// Nota de diseno: en los 90 la SNES emitia una senal fija de 256x224 y el televisor hacia el resto;
/// el "integer scaling" era fisico. Lo malo llego despues: los ports modernos estiran ese frame con
/// filtrado bilineal y el pixel art se convierte en sopa. Seto90 hace lo que hacia el hardware:
/// un framebuffer chico (RenderTexture), filtro Point y escala entera calculada por frame,
/// asi la ventana puede tener cualquier tamano sin deformar ni emborronar un solo pixel.
/// </summary>
public sealed class VirtualScreen : IDisposable
{
    readonly RenderTexture2D target;

    public int Width { get; }
    public int Height { get; }

    public VirtualScreen(int width, int height)
    {
        Width = width;
        Height = height;
        target = Raylib.LoadRenderTexture(width, height);
        Raylib.SetTextureFilter(target.Texture, TextureFilter.Point);
    }

    /// <summary>Empieza a dibujar en coordenadas virtuales (0,0 .. Width,Height).</summary>
    public void BeginVirtual() => Raylib.BeginTextureMode(target);

    public void EndVirtual() => Raylib.EndTextureMode();

    /// <summary>Presenta el lienzo virtual en la ventana real: escala entera maxima + letterbox negro.
    /// Con un CrtFilter activo, el frame pasa por el vidrio (el filtro necesita muestreo bilinear;
    /// su shader recupera la nitidez pixel-perfect con antialias subpixel). El overlay opcional se
    /// dibuja DESPUES del lienzo, en coordenadas de VENTANA real: es la UI del editor, que asi usa
    /// la resolucion nativa (texto legible) mientras el juego sigue retro.</summary>
    public void Present(CrtFilter? crt = null, Action? overlay = null)
    {
        var screenW = Raylib.GetScreenWidth();
        var screenH = Raylib.GetScreenHeight();
        var scale = Math.Max(1, Math.Min(screenW / Width, screenH / Height));
        var destW = Width * scale;
        var destH = Height * scale;
        var offX = (screenW - destW) / 2;
        var offY = (screenH - destH) / 2;

        Raylib.BeginDrawing();
        Raylib.ClearBackground(Color.Black);
        DrawTarget(crt, new Rectangle(offX, offY, destW, destH));
        overlay?.Invoke();
        Raylib.EndDrawing();
    }

    /// <summary>Escala entera y offsets de letterbox del frame actual (para mapear lienzo <-> ventana).</summary>
    public (int Scale, int OffX, int OffY) Metrics()
    {
        var screenW = Raylib.GetScreenWidth();
        var screenH = Raylib.GetScreenHeight();
        var scale = Math.Max(1, Math.Min(screenW / Width, screenH / Height));
        return (scale, (screenW - Width * scale) / 2, (screenH - Height * scale) / 2);
    }

    /// <summary>Exporta la VENTANA completa (lienzo escalado + overlay de UI en resolucion nativa):
    /// las capturas del editor muestran lo mismo que ve el autor, con su UI legible.</summary>
    public void ExportWindowPng(string path, Action? overlay)
    {
        var w = Raylib.GetScreenWidth();
        var h = Raylib.GetScreenHeight();
        var (scale, offX, offY) = Metrics();
        var big = Raylib.LoadRenderTexture(w, h);
        Raylib.BeginTextureMode(big);
        Raylib.ClearBackground(Color.Black);
        DrawTarget(null, new Rectangle(offX, offY, Width * scale, Height * scale));
        overlay?.Invoke();
        Raylib.EndTextureMode();
        var image = Raylib.LoadImageFromTexture(big.Texture);
        Raylib.ImageFlipVertical(ref image); // mismo Y invertido de RenderTexture que compensa Present()
        Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
        Raylib.UnloadRenderTexture(big);
    }

    void DrawTarget(CrtFilter? crt, Rectangle dest)
    {
        var useCrt = crt?.Active == true;
        Raylib.SetTextureFilter(target.Texture, useCrt ? TextureFilter.Bilinear : TextureFilter.Point);
        if (useCrt) crt!.Begin(Width, Height);
        // La altura negativa del rectangulo fuente compensa el eje Y invertido de las RenderTexture de OpenGL.
        Raylib.DrawTexturePro(
            target.Texture,
            new Rectangle(0, 0, Width, -Height),
            dest,
            new Vector2(0, 0), 0f, Color.White);
        if (useCrt) crt!.End();
    }

    /// <summary>Convierte coordenadas de ventana real a coordenadas del lienzo virtual (mouse del editor).</summary>
    public (int X, int Y) ToVirtual(int screenX, int screenY)
    {
        var screenW = Raylib.GetScreenWidth();
        var screenH = Raylib.GetScreenHeight();
        var scale = Math.Max(1, Math.Min(screenW / Width, screenH / Height));
        var offX = (screenW - Width * scale) / 2;
        var offY = (screenH - Height * scale) / 2;
        return ((screenX - offX) / scale, (screenY - offY) / scale);
    }

    /// <summary>Copia el ultimo frame renderizado a una textura independiente (para el battle swirl). El caller la descarga.</summary>
    public Texture2D Snapshot()
    {
        var image = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref image);
        var texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        return texture;
    }

    /// <summary>Exporta el frame como se veria en el tubo: escalado (default 3x) y con el filtro CRT
    /// aplicado, para que la IA/autor puedan ver el vidrio ademas del lienzo limpio.</summary>
    public void ExportPresentedPng(string path, CrtFilter crt, int scale = 3)
    {
        var big = Raylib.LoadRenderTexture(Width * scale, Height * scale);
        Raylib.BeginTextureMode(big);
        Raylib.ClearBackground(Color.Black);
        DrawTarget(crt, new Rectangle(0, 0, Width * scale, Height * scale));
        Raylib.EndTextureMode();
        var image = Raylib.LoadImageFromTexture(big.Texture);
        Raylib.ImageFlipVertical(ref image); // mismo Y invertido de RenderTexture que compensa Present()
        Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
        Raylib.UnloadRenderTexture(big);
    }

    /// <summary>Exporta el lienzo virtual tal cual (256x224, sin escalar) a un PNG. Para autoria/IA.</summary>
    public void ExportPng(string path)
    {
        var image = Raylib.LoadImageFromTexture(target.Texture);
        Raylib.ImageFlipVertical(ref image); // mismo Y invertido de RenderTexture que compensa Present()
        Raylib.ExportImage(image, path);
        Raylib.UnloadImage(image);
    }

    public void Dispose() => Raylib.UnloadRenderTexture(target);
}
