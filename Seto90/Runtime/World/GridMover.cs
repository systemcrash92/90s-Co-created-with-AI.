namespace Seto90;

/// <summary>
/// Movimiento por grilla con interpolacion: la posicion logica salta de tile en tile
/// (como en los 90) pero el dibujo desliza pixel a pixel entre casillas.
///
/// Nota de diseno: los JRPG de la epoca nunca movian al personaje "libremente";
/// la logica era una grilla dura (colision, triggers, NPCs) y solo la presentacion
/// interpolaba. Ese doble nivel es lo que hace que un JRPG se "sienta" bien sin
/// romper el determinismo de eventos. Lo que estaba mal era que cada juego mezclaba
/// ambas capas en el mismo codigo; aca la interpolacion vive en esta clase y el resto
/// del motor solo ve tiles enteros.
/// </summary>
public sealed class GridMover
{
    /// <summary>Tile logico actual. Al empezar un paso ya apunta al tile destino (reserva la casilla).</summary>
    public int TileX { get; private set; }
    public int TileY { get; private set; }
    public Facing Facing { get; set; } = Facing.Down;
    public bool Moving { get; private set; }
    public float SecondsPerTile { get; set; } = 0.18f;

    int fromX;
    int fromY;
    float progress; // 0..1 dentro del paso actual

    public void Teleport(int x, int y)
    {
        TileX = x; TileY = y;
        fromX = x; fromY = y;
        Moving = false;
        progress = 0;
    }

    /// <summary>Intenta empezar un paso. Siempre gira hacia la direccion pedida, camine o no.</summary>
    public bool TryStep(int dx, int dy, Func<int, int, bool> canOccupy)
    {
        if (Moving) return false;
        Facing = ToFacing(dx, dy);
        var nx = TileX + dx;
        var ny = TileY + dy;
        if (!canOccupy(nx, ny)) return false;
        fromX = TileX; fromY = TileY;
        TileX = nx; TileY = ny; // la logica reserva el destino ya mismo; el dibujo llega despues
        progress = 0;
        Moving = true;
        return true;
    }

    /// <summary>Avanza la interpolacion. Devuelve true en el frame exacto en que se completa un paso.</summary>
    public bool Update(float dt)
    {
        if (!Moving) return false;
        progress += dt / MathF.Max(0.01f, SecondsPerTile);
        if (progress < 1f) return false;
        progress = 0;
        fromX = TileX; fromY = TileY;
        Moving = false;
        return true;
    }

    public float PixelX(int tileSize) => Lerp(fromX, TileX) * tileSize;
    public float PixelY(int tileSize) => Lerp(fromY, TileY) * tileSize;

    /// <summary>Frame de animacion de caminata (una zancada completa por casilla, 0 quieto).
    /// Un ciclo por tile (no dos): a 0.3s/tile son ~10 fps de cambio de frame, cadencia de
    /// caminata de SNES; con 2 ciclos se veia acelerado, sobre todo con 3+ frames.</summary>
    public int AnimFrame(int frameCount)
    {
        if (frameCount <= 1 || !Moving) return 0;
        return (int)(progress * frameCount) % frameCount;
    }

    float Lerp(int from, int to) => Moving ? from + (to - from) * progress : to;

    static Facing ToFacing(int dx, int dy) => (dx, dy) switch
    {
        (> 0, _) => Facing.Right,
        (< 0, _) => Facing.Left,
        (_, < 0) => Facing.Up,
        _ => Facing.Down
    };
}
