using Raylib_cs;

namespace Seto90;

/// <summary>
/// Tema de UI resuelto: colores parseados y listos para dibujar, con el default del motor
/// cuando el proyecto no define uno.
///
/// Nota de diseno: en los 90 los colores de las ventanas vivian hardcodeados en el codigo
/// (o, en los pocos que lo permitian, en un menu de config que solo cambiaba el fondo). Aca el tema
/// es contenido: UiThemeDef viaja en project.json via uitheme.set y este resolver lo convierte
/// una sola vez. CPU puro — usable en smokes headless.
/// </summary>
public sealed class UiTheme
{
    public required Color WindowBg { get; init; }
    public required Color WindowBorder { get; init; }
    public required Color TextColor { get; init; }
    public required Color AccentColor { get; init; }
    public required Color ShadowColor { get; init; }
    public required string Style { get; init; }
    public required float TextSpeedCps { get; init; }

    public static UiTheme Resolve(GameProject project)
    {
        var def = project.UiThemes.FirstOrDefault(t => t.Id == project.UiThemeId) ?? new UiThemeDef();
        return FromDef(def);
    }

    public static UiTheme FromDef(UiThemeDef def) => new()
    {
        WindowBg = SpriteRaster.ParseColor(def.WindowBg),
        WindowBorder = SpriteRaster.ParseColor(def.WindowBorder),
        TextColor = SpriteRaster.ParseColor(def.TextColor),
        AccentColor = SpriteRaster.ParseColor(def.AccentColor),
        ShadowColor = SpriteRaster.ParseColor(def.ShadowColor),
        Style = def.Style,
        TextSpeedCps = def.TextSpeedCps
    };
}
