using Raylib_cs;

namespace Seto90;

/// <summary>
/// Banco de sprites en GPU: convierte cada SpriteDef (procedural o PNG) en una unica textura
/// tira-horizontal con todos los frames, construida perezosamente en el primer dibujo.
///
/// Nota de diseno: la SNES subia los sprites a la OAM/VRAM por DMA y el juego solo elegia
/// tile + atributos por frame. El equivalente honesto: una textura por sprite, un rectangulo
/// fuente por frame, quads batcheados. Regla dura del motor: esta clase es la UNICA que
/// convierte sprites a texturas, y solo existe en el camino visual (tras InitWindow).
/// </summary>
public sealed class SpriteBank(GameProject project, string? projectRoot) : IDisposable
{
    sealed class Entry
    {
        public Texture2D Texture;
        public int Width;
        public int Height;
        public bool Valid;
        public Dictionary<Facing, (int Start, int Count)> Poses = [];
    }

    static readonly Facing[] SheetOrder = [Facing.Down, Facing.Up, Facing.Left, Facing.Right];
    readonly Dictionary<string, Entry> cache = [];

    public int FrameCount(string spriteId, Facing facing)
    {
        var entry = Load(spriteId);
        return entry.Valid && entry.Poses.TryGetValue(facing, out var pose) ? pose.Count : 1;
    }

    /// <summary>Dibuja un frame del sprite. Devuelve false si el sprite no existe o su PNG no cargo (el caller dibuja placeholder).</summary>
    public bool TryDraw(string spriteId, Facing facing, int frame, int x, int y, Color tint)
    {
        var entry = Load(spriteId);
        if (!entry.Valid || !entry.Poses.TryGetValue(facing, out var pose)) return false;
        var index = pose.Start + Math.Clamp(frame, 0, pose.Count - 1);
        Raylib.DrawTextureRec(entry.Texture, new Rectangle(index * entry.Width, 0, entry.Width, entry.Height), new System.Numerics.Vector2(x, y), tint);
        return true;
    }

    /// <summary>Como TryDraw pero escalado por enteros (enemigos grandes en combate, retratos).</summary>
    public bool TryDrawScaled(string spriteId, Facing facing, int frame, int x, int y, int scale, Color tint)
    {
        var entry = Load(spriteId);
        if (!entry.Valid || !entry.Poses.TryGetValue(facing, out var pose)) return false;
        var index = pose.Start + Math.Clamp(frame, 0, pose.Count - 1);
        Raylib.DrawTexturePro(entry.Texture,
            new Rectangle(index * entry.Width, 0, entry.Width, entry.Height),
            new Rectangle(x, y, entry.Width * scale, entry.Height * scale),
            new System.Numerics.Vector2(0, 0), 0f, tint);
        return true;
    }

    public (int Width, int Height) SizeOf(string spriteId)
    {
        var entry = Load(spriteId);
        return entry.Valid ? (entry.Width, entry.Height) : (0, 0);
    }

    /// <summary>Dibuja un frame con escala FRACCIONAL (props redimensionados con la rueda del editor).</summary>
    public bool TryDrawScaledF(string spriteId, Facing facing, int frame, float x, float y, float scale, Color tint)
    {
        var entry = Load(spriteId);
        if (!entry.Valid || !entry.Poses.TryGetValue(facing, out var pose)) return false;
        var index = pose.Start + Math.Clamp(frame, 0, pose.Count - 1);
        Raylib.DrawTexturePro(entry.Texture,
            new Rectangle(index * entry.Width, 0, entry.Width, entry.Height),
            new Rectangle(x, y, entry.Width * scale, entry.Height * scale),
            new System.Numerics.Vector2(0, 0), 0f, tint);
        return true;
    }

    /// <summary>Dibuja el frame down[0] escalado para CABER en una caja cuadrada (thumbnails del
    /// editor): un sprite chico se ve a tamano real, uno grande se achica manteniendo proporcion.</summary>
    public bool TryDrawFit(string spriteId, int boxX, int boxY, int boxSize, Color tint)
    {
        var entry = Load(spriteId);
        if (!entry.Valid || !entry.Poses.TryGetValue(Facing.Down, out var pose) || entry.Width <= 0) return false;
        var scale = MathF.Min((float)boxSize / entry.Width, (float)boxSize / entry.Height);
        var w = entry.Width * scale;
        var h = entry.Height * scale;
        Raylib.DrawTexturePro(entry.Texture,
            new Rectangle(pose.Start * entry.Width, 0, entry.Width, entry.Height),
            new Rectangle(boxX + (boxSize - w) / 2f, boxY + (boxSize - h) / 2f, w, h),
            new System.Numerics.Vector2(0, 0), 0f, tint);
        return true;
    }

    Entry Load(string spriteId)
    {
        if (cache.TryGetValue(spriteId, out var cached)) return cached;
        var entry = new Entry();
        cache[spriteId] = entry;

        var def = spriteId == DefaultAssets.PlayerSpriteId
            ? DefaultAssets.PlayerSprite()
            : project.Sprites.FirstOrDefault(s => s.Id == spriteId);
        if (def == null) return entry;

        RasterSprite raster;
        if (!string.IsNullOrWhiteSpace(def.Image))
        {
            var fromSheet = LoadSheet(def);
            if (fromSheet == null) return entry;
            raster = fromSheet;
        }
        else
        {
            raster = SpriteRaster.Rasterize(def);
        }

        // Tira horizontal: [down.. up.. left.. right..], un rect fuente por frame.
        var totalFrames = SheetOrder.Sum(f => raster.FramesFor(f).Count);
        var image = Raylib.GenImageColor(Math.Max(1, totalFrames * raster.Width), Math.Max(1, raster.Height), Color.Blank);
        var cursor = 0;
        foreach (var facing in SheetOrder)
        {
            var frames = raster.FramesFor(facing);
            entry.Poses[facing] = (cursor, frames.Count);
            foreach (var buffer in frames)
            {
                for (var y = 0; y < raster.Height; y++)
                    for (var x = 0; x < raster.Width; x++)
                    {
                        var color = buffer[y * raster.Width + x];
                        if (color.A > 0) Raylib.ImageDrawPixel(ref image, cursor * raster.Width + x, y, color);
                    }
                cursor++;
            }
        }
        entry.Texture = Raylib.LoadTextureFromImage(image);
        Raylib.UnloadImage(image);
        entry.Width = raster.Width;
        entry.Height = raster.Height;
        entry.Valid = true;
        return entry;
    }

    /// <summary>Spritesheet PNG: 4 filas en orden down, up, left, right; N frames por fila.</summary>
    RasterSprite? LoadSheet(SpriteDef def)
    {
        var bytes = ImageAssets.LoadBytes(project, projectRoot, def.Image);
        if (bytes == null) return null;
        var image = Raylib.LoadImageFromMemory(".png", bytes);
        if (image.Width <= 0 || image.Height <= 0) return null;
        try
        {
            var rows = Math.Min(SheetOrder.Length, Math.Max(1, image.Height / def.Height));
            var framesPerRow = Math.Max(1, Math.Min(def.SheetFramesPerDirection, image.Width / def.Width));
            var poses = new Dictionary<Facing, List<Color[]>>();
            for (var row = 0; row < rows; row++)
            {
                var frames = new List<Color[]>();
                for (var frame = 0; frame < framesPerRow; frame++)
                {
                    var buffer = new Color[def.Width * def.Height];
                    for (var y = 0; y < def.Height; y++)
                        for (var x = 0; x < def.Width; x++)
                            buffer[y * def.Width + x] = Raylib.GetImageColor(image, frame * def.Width + x, row * def.Height + y);
                    frames.Add(buffer);
                }
                poses[SheetOrder[row]] = frames;
            }
            return SpriteRaster.WithFallbacks(def.Width, def.Height, poses);
        }
        finally { Raylib.UnloadImage(image); }
    }

    public void Dispose()
    {
        foreach (var entry in cache.Values.Where(e => e.Valid)) Raylib.UnloadTexture(entry.Texture);
        cache.Clear();
    }
}
