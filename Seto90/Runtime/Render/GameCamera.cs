namespace Seto90;

/// <summary>
/// Camara de mapa: sigue un punto de interes (el jugador) y se detiene en los bordes del mundo.
///
/// Nota de diseno: los JRPGs de los 90 no tenian "camara" como objeto; era un registro de scroll
/// de la PPU que el codigo del juego clampeaba a mano, y cada juego repetia (y rompia) esa cuenta.
/// Aca la regla vive en un solo lugar: centrar el foco, clamp a [0, mundo-vista], y si el mapa es
/// mas chico que la pantalla, centrarlo entero. Offsets siempre enteros para no partir pixeles.
/// </summary>
public sealed class GameCamera
{
    /// <summary>Offset del mundo en pixeles: dibujar todo restando (X, Y).</summary>
    public int X { get; private set; }
    public int Y { get; private set; }

    public void Follow(float focusX, float focusY, int worldW, int worldH, int viewW, int viewH)
    {
        X = Axis(focusX, worldW, viewW);
        Y = Axis(focusY, worldH, viewH);
    }

    /// <summary>Posiciona la esquina de la camara directamente (pan libre del editor), clampeada
    /// a los bordes del mundo. Un mapa mas chico que la vista se centra igual que en Follow.
    /// snap > 1 alinea la esquina a ese multiplo (el zoom out del editor dibuja el mundo a
    /// 1/snap: una esquina no alineada partiria pixeles entre el mundo y el overlay).</summary>
    public void SetCorner(int px, int py, int worldW, int worldH, int viewW, int viewH, int snap = 1)
    {
        X = worldW <= viewW ? -((viewW - worldW) / 2) : Math.Clamp(px, 0, worldW - viewW);
        Y = worldH <= viewH ? -((viewH - worldH) / 2) : Math.Clamp(py, 0, worldH - viewH);
        if (snap > 1)
        {
            X = X >= 0 ? X / snap * snap : -(-X / snap * snap);
            Y = Y >= 0 ? Y / snap * snap : -(-Y / snap * snap);
        }
    }

    static int Axis(float focus, int world, int view)
    {
        if (world <= view) return -((view - world) / 2); // mapa mas chico que la vista: centrarlo
        var cam = (int)MathF.Round(focus - view / 2f);
        return Math.Clamp(cam, 0, world - view);
    }
}
