using Raylib_cs;

namespace Seto90;

/// <summary>
/// Tintes: un multiplicador de color por tile-tipo o por objeto colocado, para re-teñir el arte
/// sin editar los PNG. Sirve para bajar packs de colores vivos a la paleta oscura y melancolica
/// de un mundo apagado. La tecla T del editor cicla estos presets.
/// </summary>
public static class Tints
{
    /// <summary>Presets que cicla T: nombre + multiplicador (#RRGGBB); "" = normal (blanco).</summary>
    public static readonly (string Name, string Hex)[] Presets =
    [
        ("normal", ""),
        ("oscuro", "#b4b4b4"),
        ("muy oscuro", "#787878"),
        ("noche", "#8494c8"),
        ("sepia", "#c8a878"),
        ("frio", "#9ab0c4"),
        ("calido", "#d0b488"),
    ];

    /// <summary>Color multiplicador de un tint hex ("" = blanco, sin efecto).</summary>
    public static Color Parse(string? hex) => string.IsNullOrWhiteSpace(hex) ? Color.White : SpriteRaster.ParseColor(hex);

    /// <summary>Siguiente preset en el ciclo, a partir del actual.</summary>
    public static string Next(string? current)
    {
        var i = System.Array.FindIndex(Presets, p => p.Hex.Equals(current ?? "", System.StringComparison.OrdinalIgnoreCase));
        return Presets[(i + 1) % Presets.Length].Hex;
    }

    /// <summary>Nombre del preset de un tint hex (para mostrar en el editor).</summary>
    public static string Name(string? hex)
    {
        var p = System.Array.Find(Presets, x => x.Hex.Equals(hex ?? "", System.StringComparison.OrdinalIgnoreCase));
        return p.Name ?? "custom";
    }

    /// <summary>Multiplica dos colores canal a canal (para el color plano de fallback teñido).</summary>
    public static Color Multiply(Color c, Color t) =>
        new((byte)(c.R * t.R / 255), (byte)(c.G * t.G / 255), (byte)(c.B * t.B / 255), c.A);
}
