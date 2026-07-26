using Raylib_cs;

namespace Seto90;

/// <summary>
/// Ventanas de UI estilo JRPG 90s: caja con borde segun tema (biselado/redondeado/plano),
/// caja de nombre del que habla, cursor de seleccion y flecha de continuar que late.
///
/// Nota de diseno: la "ventana con borde" es EL elemento visual del genero: lo estreno el
/// primer JRPG de consola y lo adopto el resto. En SNES era un juego de tiles de borde en la capa de texto;
/// cada juego la reimplementaba. Aca es un componente unico parametrizado por tema, y los
/// detalles (esquina biselada, flecha que late con el tiempo) viven en un solo lugar.
/// </summary>
public static class WindowBox
{
    public static void Draw(UiTheme theme, int x, int y, int w, int h)
    {
        Raylib.DrawRectangle(x + 1, y + 1, w - 2, h - 2, theme.WindowBg);
        switch (theme.Style)
        {
            case "plain":
                Raylib.DrawRectangleLines(x, y, w, h, theme.WindowBorder);
                break;
            case "rounded":
                // Esquinas recortadas 2px en diagonal, el redondeo honesto a esta resolucion.
                Raylib.DrawRectangle(x + 2, y, w - 4, 1, theme.WindowBorder);
                Raylib.DrawRectangle(x + 2, y + h - 1, w - 4, 1, theme.WindowBorder);
                Raylib.DrawRectangle(x, y + 2, 1, h - 4, theme.WindowBorder);
                Raylib.DrawRectangle(x + w - 1, y + 2, 1, h - 4, theme.WindowBorder);
                Raylib.DrawPixel(x + 1, y + 1, theme.WindowBorder);
                Raylib.DrawPixel(x + w - 2, y + 1, theme.WindowBorder);
                Raylib.DrawPixel(x + 1, y + h - 2, theme.WindowBorder);
                Raylib.DrawPixel(x + w - 2, y + h - 2, theme.WindowBorder);
                break;
            default: // beveled: doble borde con esquina cortada 1px, el clasico del genero.
                Raylib.DrawRectangleLines(x + 1, y + 1, w - 2, h - 2, theme.WindowBorder);
                Raylib.DrawRectangle(x + 1, y, w - 2, 1, theme.WindowBorder);
                Raylib.DrawRectangle(x + 1, y + h - 1, w - 2, 1, theme.WindowBorder);
                Raylib.DrawRectangle(x, y + 1, 1, h - 2, theme.WindowBorder);
                Raylib.DrawRectangle(x + w - 1, y + 1, 1, h - 2, theme.WindowBorder);
                break;
        }
    }

    /// <summary>Caja chica con el nombre del que habla, montada sobre el borde superior de la ventana.</summary>
    public static void DrawNameTag(UiTheme theme, PixelFont font, string name, int x, int y)
    {
        if (string.IsNullOrWhiteSpace(name)) return;
        var w = font.Measure(name) + 10;
        Draw(theme, x, y, w, 14);
        font.DrawShadowed(name, x + 5, y + 3, theme.AccentColor, theme.ShadowColor);
    }

    /// <summary>Cursor de seleccion: flecha solida apuntando a la derecha.</summary>
    public static void DrawCursor(UiTheme theme, int x, int y)
    {
        for (var i = 0; i < 4; i++) Raylib.DrawRectangle(x + i, y + i, 1, 7 - i * 2, theme.AccentColor);
    }

    /// <summary>Flecha de "hay mas texto": triangulo hacia abajo que late.</summary>
    public static void DrawContinueArrow(UiTheme theme, int x, int y, float time)
    {
        var bounce = (int)(time * 2) % 2; // late a 2Hz, salto de 1px
        for (var i = 0; i < 3; i++) Raylib.DrawRectangle(x - 2 + i, y + bounce + i, 5 - i * 2, 1, theme.AccentColor);
    }
}
