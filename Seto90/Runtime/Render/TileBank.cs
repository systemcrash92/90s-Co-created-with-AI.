using Raylib_cs;

namespace Seto90;

/// <summary>
/// Banco de atlas de tiles en GPU: si el tileset referencia un PNG (`image` terminado en .png),
/// ese PNG es una grilla de celdas de tileSize px en orden de lectura y cada tile id es su
/// indice en el atlas — la misma convencion de metatiles con la que se armaban los pueblos de
/// la epoca. Sin PNG, o si no carga, el caller cae al
/// color plano del tile: el motor nunca se rompe por un atlas ausente.
/// </summary>
public sealed class TileBank(GameProject project, string? projectRoot) : IDisposable
{
    sealed class Entry
    {
        public Texture2D Texture;
        public int Cols;
        public int Rows;
        public int CellSize;
        public bool Valid;
    }

    readonly Dictionary<string, Entry> cache = [];

    /// <summary>Celda del atlas a dibujar este frame: sin Frames es el propio id; con Frames, el
    /// reloj del tileset (AnimMs por paso) cicla la lista — el agua de todo el mapa late junta,
    /// como en SNES. Pura respecto del tiempo recibido (timeMs null = reloj de raylib).</summary>
    public static int AnimCell(TileDef? tile, int tileId, int animMs, double? timeMs = null)
    {
        if (tile == null || tile.Frames.Count == 0) return tileId;
        var t = timeMs ?? Raylib.GetTime() * 1000.0;
        var step = Math.Max(50, animMs);
        return tile.Frames[(int)(t / step) % tile.Frames.Count];
    }

    /// <summary>Dibuja el tile del atlas en el rectangulo destino. false = usar el color plano.
    /// flags (0 = normal): bits 0-1 = rotacion 0/90/180/270 horaria, bit 2 = espejo horizontal —
    /// las 8 orientaciones del grupo diedrico. Rotar pivota en el centro del destino (el tile
    /// queda en su lugar); el espejo usa ancho de source negativo, como en cualquier tile-editor.</summary>
    public bool TryDraw(TilesetDef tileset, int tileId, Rectangle dest, Color tint, int flags = 0)
    {
        var entry = Load(tileset);
        if (!entry.Valid || tileId < 0 || tileId >= entry.Cols * entry.Rows) return false;
        var src = new Rectangle(tileId % entry.Cols * entry.CellSize, tileId / entry.Cols * entry.CellSize, entry.CellSize, entry.CellSize);
        if (flags == 0)
        {
            Raylib.DrawTexturePro(entry.Texture, src, dest, new System.Numerics.Vector2(0, 0), 0f, tint);
            return true;
        }
        if ((flags & 4) != 0) src.Width = -src.Width;
        var centered = new Rectangle(dest.X + dest.Width / 2f, dest.Y + dest.Height / 2f, dest.Width, dest.Height);
        Raylib.DrawTexturePro(entry.Texture, src, centered, new System.Numerics.Vector2(dest.Width / 2f, dest.Height / 2f), (flags & 3) * 90f, tint);
        return true;
    }

    Entry Load(TilesetDef tileset)
    {
        if (cache.TryGetValue(tileset.Id, out var cached)) return cached;
        var entry = new Entry();
        cache[tileset.Id] = entry;
        if (!tileset.Image.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) return entry;
        var bytes = ImageAssets.LoadBytes(project, projectRoot, tileset.Image);
        if (bytes == null) return entry;
        var image = Raylib.LoadImageFromMemory(".png", bytes);
        if (image.Width < tileset.TileSize || image.Height < tileset.TileSize)
        {
            if (image.Width > 0) Raylib.UnloadImage(image);
            return entry;
        }
        entry.Cols = image.Width / tileset.TileSize;
        entry.Rows = image.Height / tileset.TileSize;
        entry.CellSize = tileset.TileSize;
        entry.Texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        entry.Valid = true;
        return entry;
    }

    public void Dispose()
    {
        foreach (var entry in cache.Values.Where(e => e.Valid)) Raylib.UnloadTexture(entry.Texture);
        cache.Clear();
    }
}
