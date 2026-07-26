using Raylib_cs;

namespace Seto90;

/// <summary>
/// Tile procedural para tilesets "generated"/sin PNG. El color plano era un buen seguro tecnico,
/// pero hacia que un mapa temprano pareciera un bloque de debug. Este fallback conserva la paleta
/// autorada y agrega patrones pixel-art deterministas segun el vocabulario JRPG del tile.
/// Cero texturas, shaders o azar: tambien sirve en prototipos creados enteramente por IA.
/// </summary>
public static class TileFallback
{
    public static void Draw(TileDef? tile, Rectangle dest, Color tint)
    {
        var color = Tints.Multiply(SpriteRaster.ParseColor(tile?.Color ?? "#202020"), tint);
        Raylib.DrawRectangle((int)dest.X, (int)dest.Y, Math.Max(1, (int)dest.Width), Math.Max(1, (int)dest.Height), color);
        if (dest.Width < 8 || dest.Height < 8) return;

        var scale = Math.Max(1, (int)MathF.Floor(MathF.Min(dest.Width, dest.Height) / 16f));
        var ox = (int)(dest.X + (dest.Width - 16 * scale) / 2f);
        var oy = (int)(dest.Y + (dest.Height - 16 * scale) / 2f);
        var dark = Shade(color, 0.55f);
        var deep = Shade(color, 0.34f);
        var light = Shade(color, 1.28f);
        var soft = Shade(color, 0.82f);
        var name = (tile?.Name ?? "").ToLowerInvariant();

        void Px(int x, int y, Color c, int w = 1, int h = 1) =>
            Raylib.DrawRectangle(ox + x * scale, oy + y * scale, Math.Max(1, w * scale), Math.Max(1, h * scale), c);
        void H(int y, Color c, int x = 0, int w = 16) => Px(x, y, c, w);
        void V(int x, Color c, int y = 0, int h = 16) => Px(x, y, c, 1, h);

        if (Has(name, "muro", "wall", "pared"))
        {
            H(0, deep); H(5, dark); H(10, dark); H(15, deep);
            foreach (var y in new[] { 1, 6, 11 })
            {
                var offset = y == 6 ? 4 : 0;
                for (var x = offset; x < 16; x += 8) V(x, dark, y, 4);
                H(y, light, 1, 6);
            }
            return;
        }
        if (Has(name, "piso", "stone", "piedra", "floor"))
        {
            H(7, dark); H(15, deep);
            V(5, dark, 0, 7); V(13, dark, 0, 7);
            V(1, dark, 8, 7); V(9, dark, 8, 7);
            H(0, light, 1, 4); H(8, light, 2, 6);
            // Tres motas fijas rompen la repeticion sin introducir RNG ni ruido por frame.
            Px(3, 4, soft); Px(11, 11, light); Px(15, 5, deep);
            return;
        }
        if (Has(name, "alfombra", "carpet", "tapiz"))
        {
            V(0, deep); V(15, deep); V(1, light); V(14, light);
            H(0, deep); H(15, deep);
            for (var i = 0; i < 4; i++)
            {
                Px(7 - i, 4 + i, i == 3 ? light : dark);
                Px(8 + i, 4 + i, i == 3 ? light : dark);
                Px(7 - i, 11 - i, i == 3 ? light : dark);
                Px(8 + i, 11 - i, i == 3 ? light : dark);
            }
            Px(7, 7, light, 2, 2);
            return;
        }
        if (Has(name, "escalera", "stair", "stairs"))
        {
            for (var y = 1; y < 16; y += 3) { H(y, light, 2, 12); H(y + 1, deep, 2, 12); }
            V(1, deep); V(14, deep);
            return;
        }
        if (Has(name, "pilar", "pillar", "columna"))
        {
            Px(2, 0, deep, 12, 16);
            V(3, light); V(12, dark);
            Px(1, 0, deep, 14, 2); Px(1, 14, deep, 14, 2);
            Px(6, 2, soft, 2, 12);
            return;
        }
        if (Has(name, "dosel", "canopy", "cortina", "fabric"))
        {
            V(0, deep); V(15, deep);
            for (var y = 0; y < 16; y++)
            {
                Px((y + 2) % 8 + 3, y, y % 4 == 0 ? light : dark);
                Px((13 - y % 8), y, soft);
            }
            H(15, deep);
            return;
        }
        if (Has(name, "escritorio", "desk", "madera", "wood", "mesa"))
        {
            H(0, light); H(5, dark); H(10, dark); H(15, deep);
            V(4, soft); V(11, deep);
            Px(7, 7, deep, 2); Px(8, 12, light);
            return;
        }
        if (Has(name, "costura", "seam", "borde", "border"))
        {
            H(7, deep); H(8, light);
            for (var x = 1; x < 16; x += 4) Px(x, 7, soft, 2);
            return;
        }

        // Generico: marco y cuatro pixeles cuya posicion depende solo del id.
        H(0, light); H(15, deep); V(0, dark); V(15, dark);
        var seed = Math.Abs(tile?.Id ?? 0);
        for (var i = 0; i < 4; i++) Px(2 + (seed * 5 + i * 7) % 12, 2 + (seed * 3 + i * 5) % 12, i % 2 == 0 ? soft : dark);
    }

    static bool Has(string value, params string[] words) => words.Any(value.Contains);

    static Color Shade(Color color, float factor) => new(
        (byte)Math.Clamp((int)MathF.Round(color.R * factor), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(color.G * factor), 0, 255),
        (byte)Math.Clamp((int)MathF.Round(color.B * factor), 0, 255),
        color.A);
}
