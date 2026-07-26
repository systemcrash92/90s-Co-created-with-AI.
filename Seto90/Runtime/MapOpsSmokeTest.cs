using System.Text;

namespace Seto90;

/// <summary>
/// Smoke headless de MapOps (flood fill / paint cells / stamp): la logica de tiles que
/// comparten el editor F1 y las herramientas MCP, verificada con mapas sinteticos exactos,
/// sin ventana ni proyecto. Determinista como todo el motor.
/// </summary>
public sealed class MapOpsSmokeTest
{
    public string Run()
    {
        var sb = new StringBuilder();

        // Mapa 6x5 de tile 0 con una "isla" cerrada de tile 1 (anillo) y agua 2 adentro:
        //   0 0 0 0 0 0
        //   0 1 1 1 0 0
        //   0 1 2 1 0 0
        //   0 1 1 1 0 0
        //   0 0 0 0 0 0
        var m = NewMap(6, 5, 0);
        foreach (var (x, y) in new[] { (1, 1), (2, 1), (3, 1), (1, 2), (3, 2), (1, 3), (2, 3), (3, 3) }) Set(m, x, y, 1);
        Set(m, 2, 2, 2);

        // Flood del interior (1 celda encerrada): pinta SOLO esa.
        var inner = MapOps.FloodFill(m, 2, 2, 7);
        Expect(inner.Count == 1 && inner[0] == (2, 2), $"flood interior: esperaba 1 celda, dio {inner.Count}");
        MapOps.PaintCells(m, inner.Select(c => (c.X, c.Y, 7)), strict: true);
        Expect(Get(m, 2, 2) == 7, "flood interior no pinto la celda encerrada");

        // Flood del exterior (tile 0): 30 - 8 (anillo) - 1 (interior) = 21 celdas; el anillo no se toca.
        var outer = MapOps.FloodFill(m, 0, 0, 5);
        Expect(outer.Count == 21, $"flood exterior: esperaba 21 celdas, dio {outer.Count}");
        MapOps.PaintCells(m, outer.Select(c => (c.X, c.Y, 5)), strict: true);
        Expect(Get(m, 1, 1) == 1 && Get(m, 2, 2) == 7, "flood exterior atraveso la isla cerrada");
        sb.AppendLine("Flood 4-conexo OK: interior encerrado 1 celda, exterior 21, la isla intacta.");

        // No-op: flood con el mismo tile de origen devuelve vacio; fuera del mapa tambien.
        Expect(MapOps.FloodFill(m, 0, 0, 5).Count == 0, "flood no-op (origen==destino) devolvio celdas");
        Expect(MapOps.FloodFill(m, -1, 0, 9).Count == 0 && MapOps.FloodFill(m, 6, 4, 9).Count == 0, "flood fuera del mapa devolvio celdas");

        // Mapa entero: sobre un mapa uniforme, el flood cubre W*H.
        var full = NewMap(4, 3, 0);
        Expect(MapOps.FloodFill(full, 3, 2, 1).Count == 12, "flood de mapa uniforme no cubrio W*H");
        sb.AppendLine("Flood no-op, fuera de rango y mapa entero OK.");

        // PaintCells strict: una celda fuera lanza (el camino MCP) y no pinta a medias... la
        // garantia transaccional real la da Mutate (snapshot+rollback); aca validamos el throw.
        var threw = false;
        try { MapOps.PaintCells(full, [(0, 0, 1), (4, 0, 1)], strict: true); }
        catch (ArgumentOutOfRangeException) { threw = true; }
        Expect(threw, "PaintCells strict no lanzo con celda fuera del mapa");
        // strict=false (editor): saltea la celda fuera y pinta la valida.
        MapOps.PaintCells(full, [(1, 1, 9), (-3, 7, 9)], strict: false);
        Expect(Get(full, 1, 1) == 9, "PaintCells tolerante no pinto la celda valida");
        sb.AppendLine("PaintCells OK: strict lanza con la celda exacta, tolerante saltea.");

        // Copy/Paste: stamp 2x2 conocido, pegado en las 4 esquinas (recorte en 3 de ellas).
        var src = NewMap(4, 4, 0);
        Set(src, 1, 1, 1); Set(src, 2, 1, 2); Set(src, 1, 2, 3); Set(src, 2, 2, 4);
        var stamp = MapOps.Copy(src, 1, 1, 2, 2)!;
        Expect(stamp.W == 2 && stamp.H == 2 && stamp.Tiles.SequenceEqual([1, 2, 3, 4]), "Copy no capturo el 2x2 exacto");
        // Copy clampeado: pedir 3x3 desde (-1,-1) devuelve el 2x2 que intersecta.
        var clamped = MapOps.Copy(src, -1, -1, 3, 3)!;
        Expect(clamped.W == 2 && clamped.H == 2, $"Copy clampeado: esperaba 2x2, dio {clamped.W}x{clamped.H}");
        Expect(MapOps.Copy(src, 10, 10, 2, 2) == null, "Copy sin interseccion no devolvio null");

        var dst = NewMap(3, 3, 0);
        MapOps.Paste(dst, stamp, 0, 0);                       // esquina sup-izq: entra entero
        Expect(Get(dst, 0, 0) == 1 && Get(dst, 1, 1) == 4, "Paste completo no coincide");
        dst = NewMap(3, 3, 0);
        MapOps.Paste(dst, stamp, 2, 2);                       // inf-der: solo entra el tile 1
        Expect(Get(dst, 2, 2) == 1 && dst.Tiles.Count(t => t != 0) == 1, "Paste recortado inf-der no dejo exactamente 1 tile");
        dst = NewMap(3, 3, 0);
        MapOps.Paste(dst, stamp, -1, -1);                     // sup-izq negativa: solo el tile 4
        Expect(Get(dst, 0, 0) == 4 && dst.Tiles.Count(t => t != 0) == 1, "Paste recortado sup-izq no dejo el tile 4");
        dst = NewMap(3, 3, 0);
        MapOps.Paste(dst, stamp, 2, -1);                      // sup-der: solo el tile 3
        Expect(Get(dst, 2, 0) == 3 && dst.Tiles.Count(t => t != 0) == 1, "Paste recortado sup-der no dejo el tile 3");
        sb.AppendLine("Copy/Paste OK: stamp exacto, clamp de copia y recorte en las 4 esquinas.");

        // Fill: rectangulo clampeado exacto (el "cortar" del editor).
        var f = NewMap(5, 4, 0);
        MapOps.Fill(f, 3, 2, 4, 4, 8); // se sale por der/abajo: pinta 2x2
        Expect(f.Tiles.Count(t => t == 8) == 4 && Get(f, 3, 2) == 8 && Get(f, 4, 3) == 8, "Fill clampeado no pinto el 2x2 exacto");
        sb.AppendLine("Fill OK: rectangulo clampeado al mapa.");

        // Rotacion de tiles (TileFlags paralelo): materializacion perezosa + round-trip copy/paste/fill.
        var rot = NewMap(3, 3, 0);
        MapOps.PaintCells(rot, [(0, 0, 5)], strict: false);                 // flags 0: NO materializa
        Expect(rot.TileFlags.Count == 0, "un mapa sin rotaciones materializo TileFlags de gusto");
        MapOps.PaintCells(rot, [(1, 1, 5)], strict: false, flags: 3);       // rot270: materializa entero
        Expect(rot.TileFlags.Count == 9 && MapOps.FlagsAt(rot, 1 * 3 + 1) == 3 && MapOps.FlagsAt(rot, 0) == 0, "TileFlags no se materializo a lo largo de Tiles con la celda rotada");
        var rstamp = MapOps.Copy(rot, 1, 1, 1, 1)!;                         // el stamp lleva la orientacion
        Expect(rstamp.Flags.Length == 1 && rstamp.Flags[0] == 3, "Copy no capturo la orientacion del tile");
        var rdst = NewMap(2, 2, 0);
        MapOps.Paste(rdst, rstamp, 0, 0);
        Expect(MapOps.FlagsAt(rdst, 0) == 3 && rdst.Tiles[0] == 5, "Paste no traslado tile+orientacion");
        MapOps.Fill(rdst, 0, 0, 2, 2, 7, flags: 6);                        // rot180 + espejo en todo el rect
        Expect(rdst.TileFlags.Count == 4 && rdst.TileFlags.All(fl => fl == 6), "Fill no aplico la orientacion al rectangulo");
        sb.Append("Rotacion OK: TileFlags perezoso, round-trip copy/paste y fill con orientacion.");
        return sb.ToString();
    }

    static MapDef NewMap(int w, int h, int tile) => new() { Id = "map.test", Width = w, Height = h, Tiles = [.. Enumerable.Repeat(tile, w * h)] };
    static void Set(MapDef m, int x, int y, int t) => m.Tiles[y * m.Width + x] = t;
    static int Get(MapDef m, int x, int y) => m.Tiles[y * m.Width + x];
    static void Expect(bool ok, string error) { if (!ok) throw new InvalidOperationException(error); }
}
