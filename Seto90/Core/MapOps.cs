namespace Seto90;

/// <summary>
/// Operaciones de tiles compartidas entre el editor visual (F1), el MCP y los smokes.
/// Logica pura sobre MapDef (sin raylib, sin sesion): el flood fill que dispara la tecla F
/// y el que expone map.flood_fill via MCP son literalmente el mismo algoritmo. Quien llama
/// decide la transaccion (CommandSession.Mutate); aca solo se calculan y aplican celdas.
/// </summary>
public static class MapOps
{
    /// <summary>Flood fill 4-conexo sobre el tile bajo (x,y). Devuelve las celdas a pintar,
    /// vacio si (x,y) cae fuera del mapa o el tile origen ya es newTile (no-op). El limite
    /// natural es W*H con visitados; los mapas del motor son chicos, no hace falta cap extra.</summary>
    public static List<(int X, int Y)> FloodFill(MapDef m, int x, int y, int newTile)
    {
        var cells = new List<(int X, int Y)>();
        if (x < 0 || y < 0 || x >= m.Width || y >= m.Height) return cells;
        var origin = m.Tiles[y * m.Width + x];
        if (origin == newTile) return cells;
        var visited = new HashSet<(int, int)>();
        var pending = new Stack<(int X, int Y)>();
        pending.Push((x, y));
        visited.Add((x, y));
        while (pending.Count > 0)
        {
            var (cx, cy) = pending.Pop();
            cells.Add((cx, cy));
            foreach (var (nx, ny) in new[] { (cx + 1, cy), (cx - 1, cy), (cx, cy + 1), (cx, cy - 1) })
            {
                if (nx < 0 || ny < 0 || nx >= m.Width || ny >= m.Height) continue;
                if (m.Tiles[ny * m.Width + nx] != origin) continue;
                if (visited.Add((nx, ny))) pending.Push((nx, ny));
            }
        }
        return cells;
    }

    /// <summary>Orientacion de la celda index (bits 0-1 rotacion, bit 2 espejo); 0 si el mapa no
    /// materializo TileFlags. Lectura tolerante que usan render, copia y gotero.</summary>
    public static int FlagsAt(MapDef m, int index) => index >= 0 && index < m.TileFlags.Count ? m.TileFlags[index] : 0;

    /// <summary>Escribe tile y orientacion en una celda manteniendo Tiles y TileFlags en lockstep.
    /// Un mapa sin rotaciones (TileFlags vacio) que recibe flags 0 NO materializa el array (cero
    /// bloat en el JSON); apenas aparece una rotacion, TileFlags crece a lo largo de Tiles.</summary>
    static void SetCell(MapDef m, int index, int tile, int flags)
    {
        m.Tiles[index] = tile;
        if (flags == 0 && m.TileFlags.Count == 0) return;
        while (m.TileFlags.Count < m.Tiles.Count) m.TileFlags.Add(0);
        if (m.TileFlags.Count > m.Tiles.Count) m.TileFlags.RemoveRange(m.Tiles.Count, m.TileFlags.Count - m.Tiles.Count);
        m.TileFlags[index] = flags;
    }

    /// <summary>Pinta celdas arbitrarias con una orientacion comun (flags 0-7; el editor pinta un
    /// stroke entero con la orientacion del pincel). strict=true lanza si alguna cae fuera del mapa
    /// (el camino MCP: la IA debe ser precisa y el error estructurado dice la celda exacta);
    /// strict=false las saltea (tolerancia del editor con el mouse en el borde).</summary>
    public static void PaintCells(MapDef m, IEnumerable<(int X, int Y, int Tile)> cells, bool strict, int flags = 0)
    {
        foreach (var (x, y, tile) in cells)
        {
            if (x < 0 || y < 0 || x >= m.Width || y >= m.Height)
            {
                if (strict) throw new ArgumentOutOfRangeException(nameof(cells), $"Celda ({x},{y}) fuera del mapa {m.Width}x{m.Height}.");
                continue;
            }
            SetCell(m, y * m.Width + x, tile, flags);
        }
    }

    /// <summary>Rectangulo de tiles copiado del mapa: el portapapeles del editor (Ctrl+C) y el
    /// pincel-stamp de Ctrl+V. Portable entre mapas del mismo tileset.</summary>
    public sealed record TileStamp(int W, int H, int[] Tiles, int[] Flags);

    /// <summary>Copia la region (x,y,w,h) clampeada a los limites del mapa, con su orientacion.
    /// Devuelve null si la interseccion es vacia.</summary>
    public static TileStamp? Copy(MapDef m, int x, int y, int w, int h)
    {
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(m.Width, x + w);
        var y1 = Math.Min(m.Height, y + h);
        if (x1 <= x0 || y1 <= y0) return null;
        var sw = x1 - x0;
        var sh = y1 - y0;
        var tiles = new int[sw * sh];
        var flags = new int[sw * sh];
        for (var yy = 0; yy < sh; yy++)
            for (var xx = 0; xx < sw; xx++)
            {
                var idx = (y0 + yy) * m.Width + x0 + xx;
                tiles[yy * sw + xx] = m.Tiles[idx];
                flags[yy * sw + xx] = FlagsAt(m, idx);
            }
        return new TileStamp(sw, sh, tiles, flags);
    }

    /// <summary>Pega el stamp con su esquina superior izquierda en (x,y); lo que se sale del
    /// mapa se recorta (stampear contra un borde es valido y predecible).</summary>
    public static void Paste(MapDef m, TileStamp s, int x, int y)
    {
        for (var yy = 0; yy < s.H; yy++)
            for (var xx = 0; xx < s.W; xx++)
            {
                var dx = x + xx;
                var dy = y + yy;
                if (dx < 0 || dy < 0 || dx >= m.Width || dy >= m.Height) continue;
                SetCell(m, dy * m.Width + dx, s.Tiles[yy * s.W + xx], s.Flags[yy * s.W + xx]);
            }
    }

    /// <summary>Rellena el rectangulo (clampeado) con un tile y orientacion: el "cortar" del editor
    /// (Ctrl+X) deja este relleno donde estaba la seleccion.</summary>
    public static void Fill(MapDef m, int x, int y, int w, int h, int tile, int flags = 0)
    {
        var x0 = Math.Max(0, x);
        var y0 = Math.Max(0, y);
        var x1 = Math.Min(m.Width, x + w);
        var y1 = Math.Min(m.Height, y + h);
        for (var yy = y0; yy < y1; yy++)
            for (var xx = x0; xx < x1; xx++)
                SetCell(m, yy * m.Width + xx, tile, flags);
    }
}
