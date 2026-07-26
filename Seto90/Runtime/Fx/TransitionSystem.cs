using Raylib_cs;

namespace Seto90;

/// <summary>
/// Transiciones de pantalla: fade a negro con accion en el punto ciego, y el battle swirl
/// clasico (el frame del mundo se retuerce por scanlines y funde a negro al entrar a combate).
///
/// Nota de diseno: el swirl de la epoca se hacia con el hardware de la SNES desplazando
/// cada scanline con una tabla por frame. Sin shaders y sin drivers caprichosos, la version
/// honesta es identica en espiritu: el snapshot del mundo dibujado en 224 tiras de 1px con
/// offset sinusoidal creciente. 224 draw calls por frame es nada, y es determinista en
/// cualquier GPU. Mientras una transicion esta activa, bloquea el update del juego.
/// </summary>
public sealed class TransitionSystem
{
    enum Phase { None, FadeOut, FadeIn, Swirl }
    enum Wipe { Fade, Iris, Spiral }

    Phase phase = Phase.None;
    Wipe wipe = Wipe.Fade;
    float t;
    float duration;
    float focusX;
    float focusY;
    Action? onMidpoint;
    Texture2D snapshot;
    bool hasSnapshot;
    List<(int X, int Y)>? spiralCells;
    int spiralCols;
    int spiralRows;

    public bool Blocking => phase != Phase.None;

    /// <summary>Funde a negro, ejecuta la accion en negro total y vuelve a abrir.</summary>
    public void FadeToBlack(float seconds, Action onBlack) => WipeToBlack("fade", seconds, onBlack, 0, 0);

    /// <summary>Cierra la pantalla con el efecto pedido ("fade", "iris" o "spiral"), ejecuta la accion
    /// en negro total y reabre con el mismo efecto. El iris se cierra sobre (focusX, focusY) — el
    /// jugador entrando a la puerta — y reabre desde el centro de la escena nueva.</summary>
    public void WipeToBlack(string style, float seconds, Action onBlack, float focusX, float focusY)
    {
        wipe = style switch { "iris" => Wipe.Iris, "spiral" => Wipe.Spiral, _ => Wipe.Fade };
        this.focusX = focusX;
        this.focusY = focusY;
        phase = Phase.FadeOut;
        t = 0;
        duration = MathF.Max(0.05f, seconds / 2f);
        onMidpoint = onBlack;
    }

    /// <summary>Retuerce el snapshot del mundo hasta fundir a negro y ejecuta la accion (arrancar el combate).</summary>
    public void BattleSwirl(Texture2D worldSnapshot, float seconds, Action onDone)
    {
        DropSnapshot();
        snapshot = worldSnapshot;
        hasSnapshot = true;
        phase = Phase.Swirl;
        t = 0;
        duration = MathF.Max(0.2f, seconds);
        onMidpoint = onDone;
    }

    public void Update(float dt)
    {
        if (phase == Phase.None) return;
        t += dt;
        if (t < duration) return;
        t = 0;
        switch (phase)
        {
            case Phase.FadeOut:
                onMidpoint?.Invoke();
                onMidpoint = null;
                phase = Phase.FadeIn;
                break;
            case Phase.Swirl:
                DropSnapshot();
                onMidpoint?.Invoke();
                onMidpoint = null;
                duration = 0.25f; // apertura rapida sobre la escena nueva
                wipe = Wipe.Fade; // el combate siempre abre con fade simple
                phase = Phase.FadeIn;
                break;
            case Phase.FadeIn:
                phase = Phase.None;
                break;
        }
    }

    /// <summary>Dibujar al final del frame virtual, por encima de la escena.</summary>
    public void DrawOverlay(int width, int height)
    {
        var progress = Math.Clamp(t / duration, 0f, 1f);
        switch (phase)
        {
            case Phase.FadeOut when wipe == Wipe.Iris:
                DrawIris(width, height, focusX, focusY, 1f - progress);
                break;
            case Phase.FadeIn when wipe == Wipe.Iris:
                DrawIris(width, height, width / 2f, height / 2f, progress);
                break;
            case Phase.FadeOut when wipe == Wipe.Spiral:
                DrawSpiral(width, height, progress);
                break;
            case Phase.FadeIn when wipe == Wipe.Spiral:
                DrawSpiral(width, height, 1f - progress);
                break;
            case Phase.FadeOut:
                Raylib.DrawRectangle(0, 0, width, height, new Color(0, 0, 0, (int)(progress * 255)));
                break;
            case Phase.FadeIn:
                Raylib.DrawRectangle(0, 0, width, height, new Color(0, 0, 0, (int)((1f - progress) * 255)));
                break;
            case Phase.Swirl when hasSnapshot:
                // Fondo negro y encima el mundo retorciendose scanline a scanline.
                Raylib.DrawRectangle(0, 0, width, height, Color.Black);
                var amplitude = progress * progress * 70f; // acelera: arranca suave, termina violento
                var fade = (byte)(255 * (1f - progress));
                var tint = new Color(fade, fade, fade, (byte)255);
                for (var y = 0; y < height; y++)
                {
                    var offset = MathF.Sin(y * 0.09f + t * 14f) * amplitude;
                    Raylib.DrawTexturePro(snapshot,
                        new Rectangle(0, y, width, 1),
                        new Rectangle(offset, y, width, 1),
                        new System.Numerics.Vector2(0, 0), 0f, tint);
                }
                break;
        }
    }

    /// <summary>Iris clasico: el negro come la pantalla dejando un circulo que se cierra sobre el foco.
    /// Sin shaders: un DrawRing negro cuyo radio interior es el ojo del iris.</summary>
    static void DrawIris(int width, int height, float cx, float cy, float openRatio)
    {
        // Radio maximo = distancia del foco a la esquina mas lejana (el circulo nace cubriendo todo).
        var maxR = MathF.Sqrt(MathF.Max(cx, width - cx) * MathF.Max(cx, width - cx) + MathF.Max(cy, height - cy) * MathF.Max(cy, height - cy));
        var radius = openRatio * maxR;
        if (radius <= 1f) { Raylib.DrawRectangle(0, 0, width, height, Color.Black); return; }
        Raylib.DrawRing(new System.Numerics.Vector2(cx, cy), radius, maxR + 4f, 0f, 360f, 96, Color.Black);
    }

    /// <summary>Espiral de bloques estilo serpiente: celdas de 16px se apagan recorriendo el borde
    /// hacia el centro, como los wipes de area de los JRPG de SNES.</summary>
    void DrawSpiral(int width, int height, float coverRatio)
    {
        var cols = (width + 15) / 16;
        var rows = (height + 15) / 16;
        if (spiralCells == null || spiralCols != cols || spiralRows != rows)
        {
            spiralCells = SpiralOrder(cols, rows);
            spiralCols = cols;
            spiralRows = rows;
        }
        var covered = (int)MathF.Round(Math.Clamp(coverRatio, 0f, 1f) * spiralCells.Count);
        for (var i = 0; i < covered; i++)
        {
            var (x, y) = spiralCells[i];
            Raylib.DrawRectangle(x * 16, y * 16, 16, 16, Color.Black);
        }
    }

    static List<(int X, int Y)> SpiralOrder(int cols, int rows)
    {
        var cells = new List<(int X, int Y)>(cols * rows);
        int left = 0, top = 0, right = cols - 1, bottom = rows - 1;
        while (left <= right && top <= bottom)
        {
            for (var x = left; x <= right; x++) cells.Add((x, top));
            for (var y = top + 1; y <= bottom; y++) cells.Add((right, y));
            if (top < bottom) for (var x = right - 1; x >= left; x--) cells.Add((x, bottom));
            if (left < right) for (var y = bottom - 1; y > top; y--) cells.Add((left, y));
            left++; top++; right--; bottom--;
        }
        return cells;
    }

    void DropSnapshot()
    {
        if (!hasSnapshot) return;
        Raylib.UnloadTexture(snapshot);
        hasSnapshot = false;
    }
}
