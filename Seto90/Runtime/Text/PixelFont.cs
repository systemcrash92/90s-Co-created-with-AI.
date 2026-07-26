using System.Numerics;
using System.Text;
using Raylib_cs;

namespace Seto90;

/// <summary>
/// Renderizador de la fuente pixel: metricas y wrap en CPU puro (usables en smokes headless)
/// y un atlas de textura que se construye perezosamente solo cuando hay ventana.
///
/// Nota de diseno: los juegos de los 90 dibujaban texto copiando tiles 1bpp a la VRAM, un tile
/// por caracter, y el ancho proporcional se lograba con tablas de ancho
/// a mano. Aca es igual pero honesto: una tabla de glifos, una tabla de anchos calculada, y un
/// solo atlas en GPU con todos los caracteres para que cada texto sean quads batcheados y no
/// miles de rectangulos. La regla dura del motor: nada de GPU fuera del camino visual, por eso
/// Measure/Wrap no tocan raylib.
/// </summary>
public sealed class PixelFont
{
    public const int GlyphHeight = 8;
    public int LineHeight => 10;

    sealed record Glyph(byte[] Rows, int Width, int AtlasX);

    readonly Dictionary<char, Glyph> glyphs = [];
    readonly int atlasWidth;
    Texture2D atlas;
    bool atlasReady;

    PixelFont(IReadOnlyDictionary<char, string> data)
    {
        var cursor = 0;
        foreach (var (ch, raw) in data)
        {
            var rows = new byte[GlyphHeight];
            var width = 0;
            var lines = string.IsNullOrEmpty(raw) ? [] : raw.Split('/');
            for (var y = 0; y < lines.Length && y < GlyphHeight; y++)
            {
                var line = lines[y];
                for (var x = 0; x < line.Length && x < 8; x++)
                {
                    if (line[x] == '.') continue;
                    rows[y] |= (byte)(1 << x);
                    width = Math.Max(width, x + 1);
                }
            }
            if (width == 0) width = 3; // espacio y glifos vacios
            glyphs[ch] = new Glyph(rows, width, cursor);
            cursor += width + 1; // 1px de separacion dentro del atlas
        }
        atlasWidth = Math.Max(1, cursor);
    }

    public static PixelFont Embedded() => new(FontData.Glyphs);

    public bool Has(char ch) => glyphs.ContainsKey(ch);

    /// <summary>Ancho en pixeles de un solo glifo (sin el pixel de separacion).</summary>
    public int WidthOf(char ch) => Resolve(ch).Width;

    /// <summary>Consulta CPU de un pixel de glifo, para efectos que redibujan letras a mano
    /// (como el logo tubular del splash). No toca raylib.</summary>
    public bool PixelOn(char ch, int x, int y)
    {
        var g = Resolve(ch);
        return x >= 0 && x < g.Width && y >= 0 && y < GlyphHeight && (g.Rows[y] & (1 << x)) != 0;
    }

    Glyph Resolve(char ch) => glyphs.TryGetValue(ch, out var g) ? g : glyphs['?'];

    /// <summary>Ancho en pixeles de un texto (sin contar saltos de linea; toma la linea mas ancha).</summary>
    public int Measure(string text)
    {
        var best = 0;
        var current = 0;
        foreach (var ch in text)
        {
            if (ch == '\r') continue;
            if (ch == '\n') { best = Math.Max(best, current); current = 0; continue; }
            current += Resolve(ch).Width + 1;
        }
        return Math.Max(best, current) is var total && total > 0 ? total - 1 : 0;
    }

    /// <summary>Word-wrap por pixeles reales. Respeta saltos de linea existentes.</summary>
    public string WrapPixels(string text, int maxPx)
    {
        var output = new StringBuilder();
        foreach (var paragraph in text.Split('\n'))
        {
            var line = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = line.Length == 0 ? word : line + " " + word;
                if (Measure(candidate) > maxPx && line.Length > 0)
                {
                    output.Append(line).Append('\n');
                    line.Clear();
                    line.Append(word);
                }
                else
                {
                    line.Clear();
                    line.Append(candidate);
                }
            }
            output.Append(line).Append('\n');
        }
        return output.ToString().TrimEnd('\n');
    }

    /// <summary>Dibuja texto en coordenadas virtuales. Requiere ventana; sin ella es un no-op seguro.</summary>
    public void Draw(string text, int x, int y, Color tint, int scale = 1)
    {
        if (!Raylib.IsWindowReady()) return;
        EnsureAtlas();
        var cx = x;
        var cy = y;
        foreach (var ch in text)
        {
            if (ch == '\n') { cx = x; cy += LineHeight * scale; continue; }
            if (ch == '\r') continue;
            var glyph = Resolve(ch);
            var src = new Rectangle(glyph.AtlasX, 0, glyph.Width, GlyphHeight);
            if (scale == 1)
            {
                Raylib.DrawTextureRec(atlas, src, new Vector2(cx, cy), tint);
            }
            else
            {
                Raylib.DrawTexturePro(atlas, src, new Rectangle(cx, cy, glyph.Width * scale, GlyphHeight * scale), new Vector2(0, 0), 0f, tint);
            }
            cx += (glyph.Width + 1) * scale;
        }
    }

    /// <summary>Texto con sombra de 1px, el toque tipico de las cajas de dialogo de la era.</summary>
    public void DrawShadowed(string text, int x, int y, Color tint, Color shadow, int scale = 1)
    {
        Draw(text, x + scale, y + scale, shadow, scale);
        Draw(text, x, y, tint, scale);
    }

    void EnsureAtlas()
    {
        if (atlasReady) return;
        var image = Raylib.GenImageColor(atlasWidth, GlyphHeight, Color.Blank);
        foreach (var glyph in glyphs.Values)
        {
            for (var y = 0; y < GlyphHeight; y++)
            {
                for (var x = 0; x < glyph.Width; x++)
                {
                    if ((glyph.Rows[y] & (1 << x)) != 0) Raylib.ImageDrawPixel(ref image, glyph.AtlasX + x, y, Color.White);
                }
            }
        }
        atlas = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        atlasReady = true;
    }
}
