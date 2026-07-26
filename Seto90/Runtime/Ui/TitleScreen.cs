using Raylib_cs;

namespace Seto90;

/// <summary>
/// Pantalla de titulo: nombre del juego con la fuente pixel escalada, menu Nueva partida /
/// Continuar (si existe slot 0) y el latido clasico de "empuja Enter".
///
/// Nota de diseno: el titulo de los 90 era la primera promesa del juego — logo, musica del
/// pueblo y dos opciones. Sin logo importado todavia, la tipografia ES el logo: titulo grande
/// con sombra, marco del tema, y nada mas. Cuando exista import de PNG para pantallas,
/// este modulo lo adopta sin cambiar su contrato.
/// </summary>
public sealed class TitleScreen(string gameTitle, bool hasSave)
{
    public int Selected { get; private set; }
    public bool Confirmed { get; private set; }
    public bool WantsContinue => hasSave && Selected == 1;

    public void Update(SfxPlayer? sfx)
    {
        var optionCount = hasSave ? 2 : 1;
        if (Raylib.IsKeyPressed(KeyboardKey.Down)) { Selected = Math.Min(optionCount - 1, Selected + 1); sfx?.Play("sfx.cursor"); }
        if (Raylib.IsKeyPressed(KeyboardKey.Up)) { Selected = Math.Max(0, Selected - 1); sfx?.Play("sfx.cursor"); }
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            Confirmed = true;
            sfx?.Play("sfx.confirm");
        }
    }

    public void Draw(PixelFont font, UiTheme theme, int width, int height, float time, Texture2D? background = null, bool vfxBackdrop = false)
    {
        if (vfxBackdrop)
        {
            // El fondo VFX (render.titleVfxId) ya lo dibujo el runtime detras: aca solo el velo
            // de legibilidad que se ahonda hacia abajo, para que menu y firma se lean sobre la luz.
            Raylib.DrawRectangleGradientV(0, height / 2, width, height - height / 2, new Color(0, 0, 0, 0), new Color(0, 0, 0, 190));
        }
        else if (background is { } bg && bg.Width > 0)
        {
            Raylib.ClearBackground(new Color(12, 12, 20, 255));
            // Portada a sangre: escalado "cover" (llena la pantalla y recorta el sobrante),
            // como una pantalla de titulo de SNES. Un velo oscuro que se ahonda hacia abajo
            // mantiene legibles menu y firma sobre el arte.
            var srcAspect = (float)bg.Width / bg.Height;
            var dstAspect = (float)width / height;
            Rectangle src;
            if (srcAspect > dstAspect) { var w = bg.Height * dstAspect; src = new Rectangle((bg.Width - w) / 2f, 0, w, bg.Height); }
            else { var h = bg.Width / dstAspect; src = new Rectangle(0, (bg.Height - h) / 2f, bg.Width, h); }
            Raylib.DrawTexturePro(bg, src, new Rectangle(0, 0, width, height), new System.Numerics.Vector2(0, 0), 0f, Color.White);
            Raylib.DrawRectangleGradientV(0, height / 2, width, height - height / 2, new Color(0, 0, 0, 0), new Color(0, 0, 0, 190));
        }
        else
        {
            Raylib.ClearBackground(new Color(12, 12, 20, 255));
            // Marco sobrio: dos lineas horizontales del tema, arriba y abajo.
            Raylib.DrawRectangle(0, 24, width, 1, theme.WindowBorder);
            Raylib.DrawRectangle(0, height - 25, width, 1, theme.WindowBorder);
        }

        var scale = font.Measure(gameTitle) * 2 <= width - 16 ? 2 : 1;
        var titleX = (width - font.Measure(gameTitle) * scale) / 2;
        font.DrawShadowed(gameTitle, titleX, 64, theme.AccentColor, theme.ShadowColor, scale);

        var options = hasSave ? new[] { UiStrings.NewGame, UiStrings.Continue } : [UiStrings.NewGame];
        var menuY = 128;
        for (var i = 0; i < options.Length; i++)
        {
            var x = (width - font.Measure(options[i])) / 2;
            var color = i == Selected ? theme.AccentColor : theme.TextColor;
            if (i == Selected) WindowBox.DrawCursor(theme, x - 10, menuY + i * 14 + 1);
            font.DrawShadowed(options[i], x, menuY + i * 14, color, theme.ShadowColor);
        }

        // Latido a 1Hz, la respiracion de todo titulo de la era.
        if ((int)(time * 2) % 2 == 0)
        {
            var hint = "Enter para empezar";
            font.Draw(hint, (width - font.Measure(hint)) / 2, height - 44, new Color(150, 150, 165, 255));
        }
        var engine = "Made with 90s Engine";
        font.Draw(engine, (width - font.Measure(engine)) / 2, height - 18, new Color(90, 90, 105, 255));
    }
}
