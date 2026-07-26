using Raylib_cs;

namespace Seto90;

/// <summary>
/// Sprite rasterizado en CPU: buffers de pixeles por direccion y frame, con los fallbacks
/// clasicos resueltos (up cae a down; left/right se espejan entre si).
///
/// Nota de diseno: en la SNES el espejado horizontal era un bit del atributo del sprite —
/// gratis en hardware — y por eso casi ningun juego dibujaba la caminata izquierda Y derecha.
/// Conservamos esa economia como regla de datos. Esta clase es 100% CPU (Color es solo un
/// struct): la usan la validacion y los smokes sin GPU; subirla a textura es trabajo de SpriteBank.
/// </summary>
public sealed class RasterSprite
{
    public required int Width { get; init; }
    public required int Height { get; init; }
    public required Dictionary<Facing, List<Color[]>> Poses { get; init; }

    public List<Color[]> FramesFor(Facing facing) => Poses[facing];
}

public static class SpriteRaster
{
    /// <summary>Rasteriza un sprite procedural (filas de indices de paleta) a buffers RGBA.</summary>
    public static RasterSprite Rasterize(SpriteDef def)
    {
        var poses = new Dictionary<Facing, List<Color[]>>();
        foreach (var pose in def.Poses)
        {
            poses[pose.Direction] = pose.Frames.Select(f => RasterizeFrame(def, f)).ToList();
        }
        return WithFallbacks(def.Width, def.Height, poses);
    }

    /// <summary>Completa las 4 direcciones aplicando los fallbacks de la era.</summary>
    public static RasterSprite WithFallbacks(int width, int height, Dictionary<Facing, List<Color[]>> poses)
    {
        if (!poses.TryGetValue(Facing.Down, out var down) || down.Count == 0)
        {
            down = [new Color[width * height]];
            poses[Facing.Down] = down;
        }
        if (!poses.ContainsKey(Facing.Up)) poses[Facing.Up] = down;
        var hasLeft = poses.ContainsKey(Facing.Left);
        var hasRight = poses.ContainsKey(Facing.Right);
        if (hasRight && !hasLeft) poses[Facing.Left] = poses[Facing.Right].Select(f => Mirror(f, width, height)).ToList();
        else if (hasLeft && !hasRight) poses[Facing.Right] = poses[Facing.Left].Select(f => Mirror(f, width, height)).ToList();
        else if (!hasLeft && !hasRight) { poses[Facing.Left] = down; poses[Facing.Right] = down; }
        return new RasterSprite { Width = width, Height = height, Poses = poses };
    }

    static Color[] RasterizeFrame(SpriteDef def, SpriteFrame frame)
    {
        var buffer = new Color[def.Width * def.Height];
        for (var y = 0; y < def.Height && y < frame.Rows.Count; y++)
        {
            var row = frame.Rows[y];
            for (var x = 0; x < def.Width && x < row.Length; x++)
            {
                var ch = row[x];
                if (ch == '.') continue;
                var index = Convert.ToInt32(ch.ToString(), 16);
                if (index < def.Palette.Count) buffer[y * def.Width + x] = ParseColor(def.Palette[index]);
            }
        }
        return buffer;
    }

    static Color[] Mirror(Color[] source, int width, int height)
    {
        var mirrored = new Color[source.Length];
        for (var y = 0; y < height; y++)
            for (var x = 0; x < width; x++)
                mirrored[y * width + x] = source[y * width + (width - 1 - x)];
        return mirrored;
    }

    public static Color ParseColor(string hex)
    {
        if (hex.Length == 7 && hex[0] == '#') return new Color(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16), (byte)255);
        return Color.Magenta;
    }
}
