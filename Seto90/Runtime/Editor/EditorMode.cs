using System.Numerics;
using Raylib_cs;

namespace Seto90;

/// <summary>Contexto que el runtime arma por frame para el editor: mundo visible + sesion de comandos.
/// Adopt(fresh, mensaje, nota): el mensaje es para la UI (incluye errores); la nota es la entrada
/// corta de la bitacora [vos] y va SOLO en exitos (null = no anotar, la bitacora no se ensucia).</summary>
public readonly record struct EditorContext(
    GameProject Project, MapDef Map, TilesetDef Tileset, GameCamera Camera, PixelFont Font,
    UiTheme Theme, VirtualScreen Screen, Dictionary<string, bool> Flags, GridMover Player,
    int TileSize, CommandSession? Session, Action<GameProject, string?, string?> Adopt, Action<string> Message, SfxPlayer? Sfx, TileBank? Tiles, SpriteBank? Sprites,
    Action<int, int> MoveCamera, Action<string> Scrub);

/// <summary>
/// Editor visual dentro del runtime (F1): pintar tiles con el mouse, colocar/mover/crear eventos
/// e inspeccionar flags, todo sobre la MISMA CommandSession que usa el MCP. Pintar con el mouse
/// y pintar via IA son literalmente la misma operacion validada, con un solo undo/redo (Ctrl+Z
/// deshace tanto lo tuyo como lo que la IA escribio por hot reload).
///
/// Dibujo en DOS capas (la UI del editor era ilegible a resolucion de lienzo): los overlays
/// ESPACIALES (grilla, cursor, seleccion, fantasmas) van en Draw() DENTRO del lienzo 256x224,
/// pixel-perfect con el mundo; toda la UI con TEXTO (barra, paleta, inspectores, flags, log,
/// ayuda, minimapa) va en DrawUi() a resolucion NATIVA de ventana con la fuente de consola de
/// raylib. El juego sigue retro; la herramienta se lee como una herramienta.
///
/// Nota de diseno: en los 90 el editor corria en una SGI o un PC-98 aparte y "probar" era
/// flashear un devkit; el que editaba nunca veia el juego real. Aca el juego corriendo ES el
/// editor: camina con las flechas, pinta lo que ves, y el contenido queda validado y guardado.
/// Sin sesion (run-pack / capturas) el editor abre en modo solo lectura.
/// </summary>
public sealed class EditorMode
{
    enum Tool { Tiles, Objetos, Eventos, Warps, Dialogo, Historia, Flags, Historial }
    const int ToolCount = 8;

    public bool Visible { get; private set; }

    Tool tool = Tool.Tiles;
    int selectedTile;
    int selectedTileFlags; // orientacion del pincel: bits 0-1 rotacion, bit 2 espejo (0 = normal)
    int selectedSprite;
    int paletteScroll;
    bool paletteOpen = true; // Space la oculta para pintar sin que tape el mapa
    string currentTint = ""; // tinte que se aplica a los objetos que coloco (T lo cicla)
    bool currentSolid;       // si los objetos que coloco bloquean el paso (B lo alterna)
    string? objDragId;       // objeto que estoy arrastrando para mover en OBJETOS
    string? selectedEventId;
    bool draggingEvent;
    bool pickingEventSprite; // en EVENTOS: selector visual de sprite abierto (S), con opcion "sin sprite"
    int selectedWarp = -1;   // indice del warp elegido en la lista del mapa actual
    bool draggingWarp;       // arrastrando un warp para mover su casilla origen

    // Herramienta DIALOGO: editar textos de nodos en el lugar, sin salir del juego.
    int dlgIndex;            // dialogo elegido (indice en project.Dialogues)
    int dlgScroll;           // scroll de la lista de dialogos
    int nodeIndex;           // nodo elegido dentro del dialogo
    int nodeScroll;          // scroll de la lista de nodos
    string? editField;       // null = no editando; "text" o "speaker"
    string editBuffer = "";

    // Herramienta HISTORIA: tablero del Libro Espejo. Muestra capitulos/escenas, palabras y
    // deriva juego<->prosa; S confirma una reconciliacion y E genera el manuscrito MD+DOCX.
    int storyChapterIndex;
    int storyChapterScroll;
    int storySceneIndex;
    int storySceneScroll;
    /// <summary>true mientras se tipea un texto: el runtime no debe robar F1/flechas/ESC.</summary>
    public bool CapturingText => editField != null;
    int cursorTileX = -1;
    int cursorTileY = -1;
    readonly HashSet<(int X, int Y)> stroke = [];

    // Navegacion y seleccion: zoom out del mapa, minimapa, grilla y ayuda; en TILES,
    // seleccion rectangular con Shift+arrastrar y portapapeles de tiles (Ctrl+C/X/V).
    int zoomLevel;                       // 0 = 1x, 1 = 1/2, 2 = 1/4 (Z cicla)
    public int ZoomDiv => 1 << zoomLevel;
    bool gridVisible = true;             // G alterna
    bool helpOpen;                       // H: overlay de atajos de la herramienta activa
    bool minimapOpen = true;             // V alterna
    (int X, int Y)? selAnchor;           // esquina inicial del Shift+arrastrar
    (int X, int Y, int W, int H)? selRect; // seleccion vigente en tiles (normalizada)
    MapOps.TileStamp? clipboard;         // portable entre mapas del mismo tileset
    bool pasting;                        // modo stamp: el portapapeles sigue al cursor
    // Cache de colores del minimapa: color plano del TileDef x su tinte, por referencia de tileset
    // (cada edicion adopta un proyecto nuevo, asi que la referencia cambia sola).
    TilesetDef? minimapRef;
    readonly Dictionary<int, Color> minimapColors = [];

    static readonly string[] Routines = ["idle", "pace_horizontal", "pace_vertical", "look_around", "guard"];
    static readonly string[] Transitions = ["", "fade", "iris", "spiral"]; // "" = default del proyecto

    public void Toggle() => Visible = !Visible;

    /// <summary>Reabre el editor (lo usa el runtime al salir del scrubber: volves a EVENTOS, no al juego).</summary>
    public void Open() => Visible = true;

    /// <summary>Fija el zoom por divisor (1, 2 o 4); lo usan las capturas (--editor-zoom).</summary>
    public void SetZoom(int div) => zoomLevel = div >= 4 ? 2 : div >= 2 ? 1 : 0;

    /// <summary>Abre una herramienta por indice (0=TILES..7=LOG); lo usan las capturas (--editor-tool).</summary>
    public void SetTool(int index) => tool = (Tool)Math.Clamp(index, 0, ToolCount - 1);

    int Vts(EditorContext ctx) => Math.Max(1, ctx.TileSize / ZoomDiv);
    (int X, int Y) ToScreen(EditorContext ctx, int wx, int wy) => ((wx - ctx.Camera.X) / ZoomDiv, (wy - ctx.Camera.Y) / ZoomDiv);

    // ---- Capa de UI a resolucion de ventana: fuente de consola de raylib, paneles limpios ----

    const int UiSize = 20;   // la fuente default de raylib es de 10px: 20 = escala entera nitida
    const int UiRow = 24;
    const int BarH = 32;
    static readonly Color UiBg = new(13, 15, 23, 244);
    static readonly Color UiBorder = new(82, 92, 122, 255);
    static readonly Color UiGray = new(158, 162, 178, 255);
    static readonly Color UiGreen = new(120, 255, 160, 255);

    static void UiText(string text, int x, int y, Color color) => Raylib.DrawTextEx(Raylib.GetFontDefault(), text, new Vector2(x, y), UiSize, 2, color);
    static int UiMeasure(string text) => (int)Raylib.MeasureTextEx(Raylib.GetFontDefault(), text, UiSize, 2).X;
    static void UiPanel(int x, int y, int w, int h)
    {
        Raylib.DrawRectangle(x, y, w, h, UiBg);
        Raylib.DrawRectangleLines(x, y, w, h, UiBorder);
    }

    /// <summary>Mundo -> ventana real (para etiquetas de UI ancladas al mapa: ids de eventos, warps).</summary>
    (int X, int Y) WinFromWorld(EditorContext ctx, int wx, int wy)
    {
        var (scale, offX, offY) = ctx.Screen.Metrics();
        return (offX + (wx - ctx.Camera.X) * scale / ZoomDiv, offY + (wy - ctx.Camera.Y) * scale / ZoomDiv);
    }

    public void Update(EditorContext ctx)
    {
        if (!Visible) return;
        // Tipeando un texto de dialogo, TODO el input es del campo de edicion.
        if (editField != null) { UpdateTextCapture(ctx); return; }
        // H: overlay de ayuda; abierto, se traga el resto del input (H/ESC lo cierran, F1 sale).
        if (Raylib.IsKeyPressed(KeyboardKey.H)) { helpOpen = !helpOpen; ctx.Sfx?.Play("sfx.cursor"); }
        if (helpOpen) { if (Raylib.IsKeyPressed(KeyboardKey.Escape)) helpOpen = false; return; }

        if (Raylib.IsKeyPressed(KeyboardKey.Tab)) { tool = (Tool)(((int)tool + 1) % ToolCount); paletteScroll = 0; pickingEventSprite = false; ctx.Sfx?.Play("sfx.cursor"); }
        // Space oculta/muestra la paleta: escondida no tapa el mapa y pintas/colocas libre.
        if ((tool == Tool.Tiles || tool == Tool.Objetos) && Raylib.IsKeyPressed(KeyboardKey.Space)) { paletteOpen = !paletteOpen; ctx.Sfx?.Play("sfx.cursor"); }
        if (Raylib.IsKeyPressed(KeyboardKey.G)) { gridVisible = !gridVisible; ctx.Sfx?.Play("sfx.cursor"); }
        if (Raylib.IsKeyPressed(KeyboardKey.V)) { minimapOpen = !minimapOpen; ctx.Sfx?.Play("sfx.cursor"); }

        // Undo/redo compartido con la IA: el mismo TransactionLog que usa el MCP.
        var ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        if (ctrl && ctx.Session != null)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Z)) { var r = ctx.Session.Undo(); ctx.Adopt(ctx.Session.Project, r.Ok ? "Undo aplicado." : r.Error!.Message, r.Ok ? "undo" : null); return; }
            if (Raylib.IsKeyPressed(KeyboardKey.Y)) { var r = ctx.Session.Redo(); ctx.Adopt(ctx.Session.Project, r.Ok ? "Redo aplicado." : r.Error!.Message, r.Ok ? "redo" : null); return; }
        }
        // Z (sin Ctrl): zoom out del mapa 1x -> 1/2 -> 1/4 -> 1x. La camara libre ve mas mundo.
        if (!ctrl && Raylib.IsKeyPressed(KeyboardKey.Z)) { zoomLevel = (zoomLevel + 1) % 3; ctx.Sfx?.Play("sfx.cursor"); }
        // ESC: cancelar lo mas "caliente" primero (pegado -> seleccion -> eleccion). No cierra el editor.
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) HandleEscape(ctx);

        var (mx, my) = ctx.Screen.ToVirtual(Raylib.GetMouseX(), Raylib.GetMouseY());
        // pantalla -> mundo con zoom: screen = (world - cam) / zoomDiv  =>  world = screen*zoomDiv + cam.
        cursorTileX = FloorDiv(mx * ZoomDiv + ctx.Camera.X, ctx.TileSize);
        cursorTileY = FloorDiv(my * ZoomDiv + ctx.Camera.Y, ctx.TileSize);
        var inMap = cursorTileX >= 0 && cursorTileY >= 0 && cursorTileX < ctx.Map.Width && cursorTileY < ctx.Map.Height;

        // El minimapa consume el mouse encima de el: click/arrastrar centra la camara ahi.
        var mwx = Raylib.GetMouseX();
        var mwy = Raylib.GetMouseY();
        if (tool is not (Tool.Dialogo or Tool.Historia) && minimapOpen && MinimapLayout(ctx) is { } mm && mwx >= mm.X && mwx < mm.X + mm.W && mwy >= mm.Y && mwy < mm.Y + mm.H)
        {
            if (Raylib.IsMouseButtonDown(MouseButton.Left))
            {
                var tileX = mm.OffX + (mwx - mm.X) / mm.P;
                var tileY = mm.OffY + (mwy - mm.Y) / mm.P;
                ctx.MoveCamera(tileX * ctx.TileSize + ctx.TileSize / 2 - ctx.Screen.Width * ZoomDiv / 2,
                               tileY * ctx.TileSize + ctx.TileSize / 2 - ctx.Screen.Height * ZoomDiv / 2);
            }
            return;
        }

        switch (tool)
        {
            case Tool.Tiles: UpdateTiles(ctx, inMap); break;
            case Tool.Objetos: UpdateObjetos(ctx, mx, my, inMap); break;
            case Tool.Eventos: UpdateEventos(ctx, inMap); break;
            case Tool.Warps: UpdateWarps(ctx, inMap); break;
            case Tool.Dialogo: UpdateDialogo(ctx); break;
            case Tool.Historia: UpdateHistoria(ctx); break;
            case Tool.Flags: UpdateFlags(ctx, inMap); break;
        }
    }

    void HandleEscape(EditorContext ctx)
    {
        if (pasting) { pasting = false; ctx.Sfx?.Play("sfx.cancel"); return; }
        if (selRect != null || selAnchor != null) { selRect = null; selAnchor = null; ctx.Sfx?.Play("sfx.cancel"); return; }
        if (selectedEventId != null) { selectedEventId = null; ctx.Sfx?.Play("sfx.cancel"); return; }
        if (selectedWarp >= 0) { selectedWarp = -1; ctx.Sfx?.Play("sfx.cancel"); }
    }

    /// <summary>true si el mouse (en coords de ventana) esta sobre la UI del editor: barra,
    /// paleta abierta (+su linea de hint) o inspector lateral. Ahi el mapa no se toca.</summary>
    bool MouseOverUi()
    {
        var w = Raylib.GetScreenWidth();
        var h = Raylib.GetScreenHeight();
        var mx = Raylib.GetMouseX();
        var my = Raylib.GetMouseY();
        if (my < BarH) return true;
        if (paletteOpen && tool is Tool.Tiles or Tool.Objetos && my >= PaletteTop(h) - UiRow - 4) return true;
        if (tool is Tool.Eventos or Tool.Warps && mx >= w - InspW - 10 && my >= BarH + 8 && my < BarH + 8 + InspH) return true;
        return false;
    }

    // ---- Paleta scrolleable generica (compartida por TILES y OBJETOS), en coords de VENTANA ----
    // Grilla de thumbnails 36x36 al pie de la ventana; rueda del mouse scrollea; click elige.

    const int PalCell = 40;   // 36px de thumb + 4 de aire
    const int PalRows = 3;    // filas visibles
    static int PalPanelH => PalRows * PalCell + 12;
    static int PaletteCols(int w) => Math.Max(1, (w - 32) / PalCell);
    int PaletteTop(int h) => h - PalPanelH - 8;

    /// <summary>Rect en ventana del item `index` con el scroll actual, o null si esta fuera de vista.</summary>
    Rectangle? PaletteCellRect(int index, int w, int h)
    {
        var cols = PaletteCols(w);
        var visRow = index / cols - paletteScroll;
        if (visRow < 0 || visRow >= PalRows) return null;
        return new Rectangle(16 + index % cols * PalCell, PaletteTop(h) + 8 + visRow * PalCell, 36, 36);
    }

    /// <summary>Maneja rueda (scroll) y click en la paleta. Devuelve el indice elegido o -1.</summary>
    int PaletteUpdate(int itemCount)
    {
        if (!paletteOpen) return -1; // cerrada: no captura nada, el mapa es todo editable
        var w = Raylib.GetScreenWidth();
        var h = Raylib.GetScreenHeight();
        var mx = Raylib.GetMouseX();
        var my = Raylib.GetMouseY();
        var cols = PaletteCols(w);
        var totalRows = (itemCount + cols - 1) / cols;
        var maxScroll = Math.Max(0, totalRows - PalRows);
        if (my >= PaletteTop(h))
        {
            var wheel = Raylib.GetMouseWheelMove();
            if (wheel != 0) paletteScroll = Math.Clamp(paletteScroll - (int)wheel, 0, maxScroll);
        }
        paletteScroll = Math.Clamp(paletteScroll, 0, maxScroll);
        if (my >= PaletteTop(h) && Raylib.IsMouseButtonPressed(MouseButton.Left))
            for (var i = 0; i < itemCount; i++)
                if (PaletteCellRect(i, w, h) is { } r && mx >= r.X && mx < r.X + r.Width && my >= r.Y && my < r.Y + r.Height)
                    return i;
        return -1;
    }

    /// <summary>Dibuja el panel + los thumbnails visibles (drawThumb) + el resaltado y la barra de scroll.
    /// Cerrada, solo un indicador chico del item elegido en la esquina (no tapa el mapa).</summary>
    void PaletteDraw(EditorContext ctx, int itemCount, int selected, Action<int, Rectangle> drawThumb)
    {
        var w = Raylib.GetScreenWidth();
        var h = Raylib.GetScreenHeight();
        if (!paletteOpen)
        {
            // Indicador compacto abajo a la izquierda: el thumbnail elegido + "Space: paleta".
            UiPanel(10, h - 54, 44, 44);
            if (selected >= 0 && selected < itemCount) drawThumb(selected, new Rectangle(14, h - 50, 36, 36));
            UiText("Space: paleta", 62, h - 42, UiGray);
            return;
        }
        UiPanel(10, PaletteTop(h), w - 20, PalPanelH);
        for (var i = 0; i < itemCount; i++)
        {
            if (PaletteCellRect(i, w, h) is not { } r) continue;
            drawThumb(i, r);
            if (i == selected) Raylib.DrawRectangleLines((int)r.X - 2, (int)r.Y - 2, 40, 40, ctx.Theme.AccentColor);
        }
        var cols = PaletteCols(w);
        var totalRows = (itemCount + cols - 1) / cols;
        if (totalRows > PalRows)
        {
            var barH = Math.Max(10, PalPanelH * PalRows / totalRows);
            var barY = PaletteTop(h) + 4 + (PalPanelH - barH - 8) * paletteScroll / (totalRows - PalRows);
            Raylib.DrawRectangle(w - 16, barY, 4, barH, ctx.Theme.AccentColor);
        }
    }

    // ---- Herramienta TILES: pincel por casilla (un stroke completo = una transaccion),
    //      F flood fill, Shift+arrastrar seleccion, Ctrl+C/X/V portapapeles-stamp ----

    void UpdateTiles(EditorContext ctx, bool inMap)
    {
        var picked = PaletteUpdate(ctx.Tileset.Tiles.Count);
        if (picked >= 0) { selectedTile = ctx.Tileset.Tiles[picked].Id; ctx.Sfx?.Play("sfx.cursor"); return; }

        var ctrl = Raylib.IsKeyDown(KeyboardKey.LeftControl) || Raylib.IsKeyDown(KeyboardKey.RightControl);
        var shift = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift);

        // Portapapeles de tiles: Ctrl+C copia la seleccion, Ctrl+X la corta (rellena con el tile
        // elegido), Ctrl+V entra en modo stamp. Por eso C y T sueltos llevan guardia !ctrl.
        if (ctrl && ctx.Session != null)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.C) && selRect is { } rc)
            {
                clipboard = MapOps.Copy(ctx.Map, rc.X, rc.Y, rc.W, rc.H);
                if (clipboard != null) { ctx.Sfx?.Play("sfx.cursor"); ctx.Message($"Copiado {clipboard.W}x{clipboard.H}. Ctrl+V pega (stamp repetible)."); }
                return;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.X) && selRect is { } rx)
            {
                clipboard = MapOps.Copy(ctx.Map, rx.X, rx.Y, rx.W, rx.H);
                var mapId = ctx.Map.Id;
                var tile = selectedTile;
                var flags = selectedTileFlags;
                var result = ctx.Session.Mutate(p => MapOps.Fill(p.Maps.First(x => x.Id == mapId), rx.X, rx.Y, rx.W, rx.H, tile, flags));
                if (result.Ok) { selRect = null; ctx.Sfx?.Play("sfx.confirm"); }
                ctx.Adopt(ctx.Session.Project, result.Ok ? $"Cortado {rx.W}x{rx.H} (relleno con tile {tile}). Ctrl+V pega." : result.Error!.Message, result.Ok ? UiStrings.NoteCut(rx.W, rx.H, Short(mapId)) : null);
                return;
            }
            if (Raylib.IsKeyPressed(KeyboardKey.V) && clipboard != null)
            {
                pasting = true;
                selRect = null;
                ctx.Sfx?.Play("sfx.cursor");
                ctx.Message($"Stamp {clipboard.W}x{clipboard.H}: click estampa (repetible), ESC/click der sale.");
                return;
            }
        }

        // R: rota el PINCEL 90 en sentido horario; Shift+R lo espeja. El mismo tile en las 8
        // orientaciones del grupo diedrico, como cualquier editor de tiles — una esquina sirve para las 4.
        // Es estado local del pincel (no toca datos): funciona tambien en solo lectura.
        if (!ctrl && Raylib.IsKeyPressed(KeyboardKey.R))
        {
            if (shift) selectedTileFlags ^= 4;
            else selectedTileFlags = (selectedTileFlags & 4) | ((selectedTileFlags + 1) & 3);
            ctx.Sfx?.Play("sfx.cursor");
            ctx.Message($"Pincel: {OrientName(selectedTileFlags)}  (R rota, Shift+R espeja)");
            return;
        }
        // T: cicla el TINTE del tile elegido (oscurecer/teñir para bajarlo a la paleta del juego).
        if (!ctrl && Raylib.IsKeyPressed(KeyboardKey.T) && ctx.Session != null)
        {
            var tid = selectedTile;
            var tsId = ctx.Tileset.Id;
            var next = Tints.Next(ctx.Tileset.Tiles.FirstOrDefault(t => t.Id == tid)?.Tint ?? "");
            var result = ctx.Session.Mutate(p => { var t = p.Tilesets.First(x => x.Id == tsId).Tiles.First(x => x.Id == tid); t.Tint = next; });
            if (result.Ok) ctx.Sfx?.Play("sfx.cursor");
            ctx.Adopt(ctx.Session.Project, result.Ok ? $"Tile {tid}: tinte {Tints.Name(next)}." : result.Error!.Message, result.Ok ? $"tile {tid}: tinte {Tints.Name(next)}" : null);
            return;
        }
        // C: alterna la colision del tile elegido (marcar paredes solidas al retexturar).
        if (!ctrl && Raylib.IsKeyPressed(KeyboardKey.C) && ctx.Session != null)
        {
            var tid = selectedTile;
            var tsId = ctx.Tileset.Id;
            var wasSolid = ctx.Tileset.Tiles.FirstOrDefault(t => t.Id == tid)?.Solid ?? false;
            var result = ctx.Session.Mutate(p => { var t = p.Tilesets.First(x => x.Id == tsId).Tiles.First(x => x.Id == tid); t.Solid = !t.Solid; });
            if (result.Ok) ctx.Sfx?.Play("sfx.cursor");
            ctx.Adopt(ctx.Session.Project, result.Ok ? $"Tile {tid}: colision {(!wasSolid ? "ON (bloquea)" : "OFF (se pisa)")}." : result.Error!.Message, result.Ok ? $"tile {tid}: colision {(!wasSolid ? "ON" : "OFF")}" : null);
            return;
        }
        // F: flood fill 4-conexo desde el cursor con el tile elegido, UNA transaccion (MapOps,
        // el mismo algoritmo que expone map.flood_fill via MCP).
        if (Raylib.IsKeyPressed(KeyboardKey.F) && inMap && ctx.Session != null)
        {
            var mapId = ctx.Map.Id;
            var tile = selectedTile;
            var flags = selectedTileFlags;
            var cells = MapOps.FloodFill(ctx.Map, cursorTileX, cursorTileY, tile);
            if (cells.Count == 0) { ctx.Message("Nada que rellenar (ya es ese tile)."); return; }
            var result = ctx.Session.Mutate(p => MapOps.PaintCells(p.Maps.First(x => x.Id == mapId), cells.Select(c => (c.X, c.Y, tile)), strict: false, flags));
            if (result.Ok) ctx.Sfx?.Play("sfx.confirm");
            ctx.Adopt(ctx.Session.Project, result.Ok ? $"Flood: {cells.Count} tiles. Ctrl+Z deshace." : result.Error!.Message, result.Ok ? $"flood {cells.Count} tiles en {Short(mapId)}" : null);
            return;
        }

        if (MouseOverUi()) return; // sobre la UI no se pinta

        // Modo stamp: el portapapeles sigue al cursor; click estampa (y SE QUEDA en modo pegado:
        // esto es tambien el "pincel grande"); ESC o click derecho salen.
        if (pasting && clipboard != null)
        {
            if (Raylib.IsMouseButtonPressed(MouseButton.Right)) { pasting = false; ctx.Sfx?.Play("sfx.cancel"); return; }
            if (Raylib.IsMouseButtonPressed(MouseButton.Left) && inMap && ctx.Session != null)
            {
                var mapId = ctx.Map.Id;
                var st = clipboard;
                var (px, py) = (cursorTileX, cursorTileY);
                var result = ctx.Session.Mutate(p => MapOps.Paste(p.Maps.First(x => x.Id == mapId), st, px, py));
                if (result.Ok) ctx.Sfx?.Play("sfx.confirm");
                ctx.Adopt(ctx.Session.Project, result.Ok ? $"Stamp {st.W}x{st.H} en ({px},{py}). Click estampa otra vez, ESC sale." : result.Error!.Message, result.Ok ? UiStrings.NotePaste(st.W, st.H, Short(mapId)) : null);
            }
            return;
        }

        // Seleccion rectangular: Shift+arrastrar (el stroke normal no acumula con Shift).
        if (shift && Raylib.IsMouseButtonPressed(MouseButton.Left) && inMap) { selAnchor = (cursorTileX, cursorTileY); selRect = null; stroke.Clear(); }
        if (selAnchor is { } anchor && Raylib.IsMouseButtonDown(MouseButton.Left))
        {
            var cx = Math.Clamp(cursorTileX, 0, ctx.Map.Width - 1);
            var cy = Math.Clamp(cursorTileY, 0, ctx.Map.Height - 1);
            selRect = (Math.Min(anchor.X, cx), Math.Min(anchor.Y, cy), Math.Abs(anchor.X - cx) + 1, Math.Abs(anchor.Y - cy) + 1);
        }
        if (selAnchor != null && Raylib.IsMouseButtonReleased(MouseButton.Left)) { selAnchor = null; return; }

        if (Raylib.IsMouseButtonPressed(MouseButton.Right) && inMap)
        {
            var gi = cursorTileY * ctx.Map.Width + cursorTileX; // gotero: toma tile Y orientacion
            selectedTile = ctx.Map.Tiles[gi];
            selectedTileFlags = MapOps.FlagsAt(ctx.Map, gi);
            ctx.Sfx?.Play("sfx.cursor");
        }

        if (!shift && selAnchor == null && Raylib.IsMouseButtonDown(MouseButton.Left) && inMap) stroke.Add((cursorTileX, cursorTileY));

        if (Raylib.IsMouseButtonReleased(MouseButton.Left) && stroke.Count > 0)
        {
            var cells = stroke.ToArray();
            stroke.Clear();
            if (ctx.Session == null) { ctx.Message("Editor en solo lectura: sin project.json editable."); return; }
            var mapId = ctx.Map.Id;
            var tile = selectedTile;
            var flags = selectedTileFlags;
            var result = ctx.Session.Mutate(p => MapOps.PaintCells(p.Maps.First(x => x.Id == mapId), cells.Select(c => (c.X, c.Y, tile)), strict: false, flags));
            if (result.Ok) ctx.Sfx?.Play("sfx.confirm");
            ctx.Adopt(ctx.Session.Project, result.Ok ? $"{cells.Length} tiles pintados{(flags != 0 ? $" ({OrientName(flags)})" : "")}. Ctrl+Z deshace." : result.Error!.Message, result.Ok ? UiStrings.NotePaint(cells.Length, Short(mapId)) : null);
        }
    }

    // ---- Herramienta OBJETOS: menu de sprites (elegir del panel) + click en el mapa = colocar ----
    // El reparto de trabajo: el autor edita el mapa, el motor da el menu para elegir y posicionar sprites.

    void UpdateObjetos(EditorContext ctx, int mx, int my, bool inMap)
    {
        var sprites = ctx.Project.Sprites;
        var picked = PaletteUpdate(sprites.Count);
        if (picked >= 0) { selectedSprite = picked; ctx.Sfx?.Play("sfx.cursor"); return; }
        if (MouseOverUi() || ctx.Session == null) return;

        // Rueda del mouse sobre un prop (en el mapa) lo REDIMENSIONA (0.25x a 3x).
        var wheel = Raylib.GetMouseWheelMove();
        if (wheel != 0 && inMap)
        {
            var hit = PropAtCursor(ctx);
            if (hit != null && !string.IsNullOrEmpty(hit.Sprite))
            {
                var id = hit.Id;
                var next = Math.Clamp((hit.Scale > 0.01f ? hit.Scale : 1f) + wheel * 0.1f, 0.25f, 3f);
                var result = ctx.Session.Mutate(p => p.Events.First(e => e.Id == id).Scale = next);
                if (result.Ok) ctx.Sfx?.Play("sfx.cursor");
                ctx.Adopt(ctx.Session.Project, result.Ok ? $"{Short(id)}: escala {next:0.0}x." : result.Error!.Message, result.Ok ? $"{Short(id)}: escala {next:0.0}x" : null);
                return;
            }
        }

        // I: coloca un BLOQUEO INVISIBLE (Object sin sprite, solido) - tapa el paso donde un
        // prop se escapa de su casilla (la copa de un arbol, un techo). Invisible en el juego.
        if (Raylib.IsKeyPressed(KeyboardKey.I) && inMap)
        {
            var id = NextObjectId(ctx.Project);
            var (bx, by) = (cursorTileX, cursorTileY);
            var result = ctx.Session.Mutate(p =>
            {
                p.Events.Add(new EventDef { Id = id, MapId = ctx.Map.Id, Name = "Bloqueo", Kind = EventKind.Object, X = bx, Y = by, Sprite = "", Solid = true, RoutineId = "idle", Pages = [new EventPage()] });
                var m = p.Maps.First(x => x.Id == ctx.Map.Id);
                if (!m.EventIds.Contains(id)) m.EventIds.Add(id);
            });
            if (result.Ok) ctx.Sfx?.Play("sfx.confirm");
            ctx.Adopt(ctx.Session.Project, result.Ok ? $"Bloqueo invisible en ({bx},{by}). Click der: borrar." : result.Error!.Message, result.Ok ? $"bloqueo invisible en ({bx},{by})" : null);
            return;
        }

        if (sprites.Count == 0) return;

        // T: sobre un objeto existente re-tiñe ESE objeto; si no, cambia el tinte de los nuevos.
        if (Raylib.IsKeyPressed(KeyboardKey.T))
        {
            var hit = inMap ? PropAtCursor(ctx) : null;
            if (hit != null)
            {
                var id = hit.Id;
                var next = Tints.Next(hit.Tint);
                var result = ctx.Session.Mutate(p => p.Events.First(e => e.Id == id).Tint = next);
                if (result.Ok) ctx.Sfx?.Play("sfx.cursor");
                ctx.Adopt(ctx.Session.Project, result.Ok ? $"{Short(id)}: tinte {Tints.Name(next)}." : result.Error!.Message, result.Ok ? $"{Short(id)}: tinte {Tints.Name(next)}" : null);
            }
            else { currentTint = Tints.Next(currentTint); ctx.Sfx?.Play("sfx.cursor"); ctx.Message($"Tinte de objetos nuevos: {Tints.Name(currentTint)}"); }
            return;
        }

        // P: condicion de presencia del objeto bajo el cursor (existe siempre / si tal flag).
        if (Raylib.IsKeyPressed(KeyboardKey.P) && inMap)
        {
            var hit = PropAtCursor(ctx);
            if (hit != null) CyclePresence(ctx, hit);
            else ctx.Message("P: apunta a un objeto para condicionar su presencia a una flag.");
            return;
        }

        // B: sobre un objeto existente alterna SU colision; si no, la de los nuevos que coloque.
        if (Raylib.IsKeyPressed(KeyboardKey.B))
        {
            var hit = inMap ? PropAtCursor(ctx) : null;
            if (hit != null)
            {
                var id = hit.Id;
                var result = ctx.Session.Mutate(p => { var e = p.Events.First(x => x.Id == id); e.Solid = !e.Solid; });
                if (result.Ok) ctx.Sfx?.Play("sfx.cursor");
                ctx.Adopt(ctx.Session.Project, result.Ok ? $"{Short(id)}: {(hit.Solid ? "ya no bloquea" : "ahora bloquea")}." : result.Error!.Message, result.Ok ? $"{Short(id)}: {(hit.Solid ? "no bloquea" : "bloquea")}" : null);
            }
            else { currentSolid = !currentSolid; ctx.Sfx?.Play("sfx.cursor"); ctx.Message($"Objetos nuevos {(currentSolid ? "BLOQUEAN" : "no bloquean")}."); }
            return;
        }

        // Click izquierdo: sobre un prop existente lo ARRASTRA (mover); en vacio COLOCA uno nuevo.
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && inMap)
        {
            var onObj = PropAtCursor(ctx);
            if (onObj != null) { objDragId = onObj.Id; ctx.Sfx?.Play("sfx.cursor"); } // empezar a mover
            else
            {
                var spriteId = sprites[Math.Clamp(selectedSprite, 0, sprites.Count - 1)].Id;
                var (tx, ty, ox, oy) = FreePlace(mx, my, ctx, currentSolid);
                var id = NextObjectId(ctx.Project);
                var tint = currentTint; var solid = currentSolid;
                var result = ctx.Session.Mutate(p =>
                {
                    p.Events.Add(new EventDef { Id = id, MapId = ctx.Map.Id, Name = "Objeto", Kind = EventKind.Object, X = tx, Y = ty, OffsetX = ox, OffsetY = oy, Sprite = spriteId, RoutineId = "idle", Pages = [new EventPage()], Tint = tint, Solid = solid });
                    var m = p.Maps.First(x => x.Id == ctx.Map.Id);
                    if (!m.EventIds.Contains(id)) m.EventIds.Add(id);
                });
                if (result.Ok) ctx.Sfx?.Play("sfx.confirm");
                ctx.Adopt(ctx.Session.Project, result.Ok ? $"Coloque {Short(spriteId)}. Arrastralo para mover, click der borra." : result.Error!.Message, result.Ok ? UiStrings.NotePlace(Short(spriteId), Short(ctx.Map.Id), tx, ty) : null);
            }
        }

        // Soltar el arrastre: reubica el prop en el cursor (libre si es decorativo, grilla si bloquea).
        if (Raylib.IsMouseButtonReleased(MouseButton.Left) && objDragId != null)
        {
            var id = objDragId; objDragId = null;
            var cur = ctx.Project.Events.FirstOrDefault(e => e.Id == id);
            if (cur != null && inMap)
            {
                var (tx, ty, ox, oy) = FreePlace(mx, my, ctx, cur.Solid);
                var result = ctx.Session.Mutate(p => { var e = p.Events.First(x => x.Id == id); e.X = tx; e.Y = ty; e.OffsetX = ox; e.OffsetY = oy; });
                if (result.Ok) ctx.Sfx?.Play("sfx.confirm");
                ctx.Adopt(ctx.Session.Project, result.Ok ? $"{Short(id)} movido." : result.Error!.Message, result.Ok ? UiStrings.NoteMove(Short(id), tx, ty) : null);
            }
        }

        // Borra el objeto bajo el cursor: click DERECHO, o tecla Supr/X (el resaltado te muestra cual).
        if ((Raylib.IsMouseButtonPressed(MouseButton.Right) || Raylib.IsKeyPressed(KeyboardKey.Delete) || Raylib.IsKeyPressed(KeyboardKey.X)) && inMap)
        {
            var hit = PropAtCursor(ctx);
            if (hit != null)
            {
                var id = hit.Id;
                var result = ctx.Session.Mutate(p => { p.Events.RemoveAll(e => e.Id == id); foreach (var m in p.Maps) m.EventIds.Remove(id); });
                if (result.Ok) ctx.Sfx?.Play("sfx.cancel");
                ctx.Adopt(ctx.Session.Project, result.Ok ? $"Borre {Short(id)}. Ctrl+Z lo revive." : result.Error!.Message, result.Ok ? UiStrings.NoteDelete(Short(id)) : null);
            }
        }
    }

    /// <summary>Del pixel bajo el mouse saca tile + offset. Solido = pegado a la grilla (offset 0);
    /// decorativo = posicion LIBRE (offset = sub-pixel del cursor), sin afectar la colision.</summary>
    (int Tx, int Ty, int Ox, int Oy) FreePlace(int mx, int my, EditorContext ctx, bool solid)
    {
        var ts = ctx.TileSize;
        var wpx = mx * ZoomDiv + ctx.Camera.X; // con zoom out, 1px de pantalla = ZoomDiv px de mundo
        var wpy = my * ZoomDiv + ctx.Camera.Y;
        var tx = FloorDiv(wpx, ts);
        var ty = FloorDiv(wpy, ts);
        return solid ? (tx, ty, 0, 0) : (tx, ty, wpx - tx * ts, wpy - ty * ts);
    }

    /// <summary>El prop (Object) cuyo FOOTPRINT VISUAL contiene el pixel del cursor: tile + offset,
    /// tamano del sprite x escala, anclado a los pies — lo MISMO que se dibuja. Asi se agarra el prop
    /// DONDE SE VE, no en su celda de tile: un prop con offset grande (una cama corrida hacia abajo,
    /// un libro empujado a la derecha) caia lejos de su celda y no se podia seleccionar ni mover.
    /// Prioriza el de pies mas bajos (dibujado ultimo, el que queda visualmente ENCIMA). Los props
    /// sin sprite (bloqueos invisibles) caen a su celda de tile.</summary>
    EventDef? PropAtCursor(EditorContext ctx)
    {
        var ts = ctx.TileSize;
        var (vmx, vmy) = ctx.Screen.ToVirtual(Raylib.GetMouseX(), Raylib.GetMouseY());
        var wpx = vmx * ZoomDiv + ctx.Camera.X;
        var wpy = vmy * ZoomDiv + ctx.Camera.Y;
        EventDef? best = null; var bestFeet = int.MinValue;
        foreach (var e in ctx.Project.Events)
        {
            if (e.MapId != ctx.Map.Id || e.Kind != EventKind.Object) continue;
            var (sw, sh) = !string.IsNullOrEmpty(e.Sprite) && ctx.Sprites != null ? ctx.Sprites.SizeOf(e.Sprite) : (0, 0);
            int x0, y0, w, h;
            if (sw > 0)
            {
                var sc = e.Scale > 0.01f ? e.Scale : 1f;
                w = (int)(sw * sc); h = (int)(sh * sc);
                x0 = e.X * ts + e.OffsetX + (ts - w) / 2;
                y0 = e.Y * ts + e.OffsetY + ts - h;
            }
            else { x0 = e.X * ts; y0 = e.Y * ts; w = ts; h = ts; }
            if (wpx < x0 || wpx >= x0 + w || wpy < y0 || wpy >= y0 + h) continue;
            var feet = e.Y * ts + e.OffsetY + ts;
            if (feet >= bestFeet) { bestFeet = feet; best = e; }
        }
        return best;
    }

    static string NextObjectId(GameProject p)
    {
        for (var n = 1; ; n++)
        {
            var id = $"event.obj_{n}";
            if (p.Events.All(e => e.Id != id)) return id;
        }
    }

    // ---- Herramienta EVENTOS: click elige, arrastrar mueve, N nuevo, S sprite, R rutina, X borra ----

    void UpdateEventos(EditorContext ctx, bool inMap)
    {
        var mapId = ctx.Map.Id;
        var overUi = MouseOverUi();

        // Selector de sprite del evento (modal): la paleta scrolleable con una celda "sin sprite"
        // al frente. Reemplaza el viejo ciclado con S (impracticable con miles de sprites) y da la
        // respuesta a "no quiero ningun sprite" en un click. Un evento sin sprite es un disparador
        // invisible (se activa mirandolo). Enter en el picker no aplica; se elige con click.
        if (pickingEventSprite)
        {
            if (ctx.Session == null || selectedEventId == null || Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.S))
            { pickingEventSprite = false; ctx.Sfx?.Play("sfx.cancel"); return; }
            var picked = PaletteUpdate(ctx.Project.Sprites.Count + 1); // indice 0 = sin sprite
            if (picked >= 0)
            {
                var id = selectedEventId;
                var sprite = picked == 0 ? "" : ctx.Project.Sprites[picked - 1].Id;
                var result = ctx.Session.Mutate(p => p.Events.First(e => e.Id == id).Sprite = sprite);
                if (result.Ok) { ctx.Sfx?.Play("sfx.confirm"); pickingEventSprite = false; }
                ctx.Adopt(ctx.Session.Project, result.Ok ? $"{Short(id)}: {(sprite == "" ? "sin sprite" : "sprite " + Short(sprite))}." : result.Error!.Message, result.Ok ? $"{Short(id)}: sprite {(sprite == "" ? "ninguno" : Short(sprite))}" : null);
            }
            return; // modal: nada mas se procesa mientras se elige
        }

        if (!overUi && Raylib.IsMouseButtonPressed(MouseButton.Left) && inMap)
        {
            // Solo eventos de LOGICA: los props decorativos se agarran en OBJETOS (separacion clara).
            var hit = ctx.Project.Events.FirstOrDefault(e => e.MapId == mapId && IsInteractive(e) && e.X == cursorTileX && e.Y == cursorTileY);
            selectedEventId = hit?.Id ?? selectedEventId;
            draggingEvent = hit != null;
            if (hit != null) ctx.Sfx?.Play("sfx.cursor");
        }

        if (Raylib.IsMouseButtonReleased(MouseButton.Left))
        {
            if (!overUi && draggingEvent && selectedEventId != null && inMap && ctx.Session != null)
            {
                var id = selectedEventId;
                var current = ctx.Project.Events.FirstOrDefault(e => e.Id == id);
                if (current != null && (current.X != cursorTileX || current.Y != cursorTileY))
                {
                    var (nx, ny) = (cursorTileX, cursorTileY);
                    var result = ctx.Session.Mutate(p => { var ev = p.Events.First(e => e.Id == id); ev.X = nx; ev.Y = ny; });
                    if (result.Ok) ctx.Sfx?.Play("sfx.confirm");
                    ctx.Adopt(ctx.Session.Project, result.Ok ? $"{Short(id)} movido a ({nx},{ny})." : result.Error!.Message, result.Ok ? UiStrings.NoteMove(Short(id), nx, ny) : null);
                }
            }
            draggingEvent = false;
        }

        // Enter: reproducir la cutscene del evento elegido en el scrubber (paso a paso, sin
        // rejugar hasta ahi). Funciona tambien en solo lectura: es playback, no escritura.
        if (selectedEventId != null && Raylib.IsKeyPressed(KeyboardKey.Enter))
        {
            ctx.Scrub(selectedEventId);
            return;
        }

        if (ctx.Session == null) return;

        if (Raylib.IsKeyPressed(KeyboardKey.N) && inMap)
        {
            var id = NextEventId(ctx.Project);
            var (nx, ny) = (cursorTileX, cursorTileY);
            var result = ctx.Session.Mutate(p =>
            {
                p.Events.Add(new EventDef { Id = id, MapId = mapId, Name = "Nuevo NPC", Kind = EventKind.Npc, X = nx, Y = ny, RoutineId = "idle", Pages = [new EventPage()] });
                var m = p.Maps.First(x => x.Id == mapId);
                if (!m.EventIds.Contains(id)) m.EventIds.Add(id);
            });
            if (result.Ok) { selectedEventId = id; ctx.Sfx?.Play("sfx.confirm"); }
            ctx.Adopt(ctx.Session.Project, result.Ok ? $"{Short(id)} creado. S: sprite  R: rutina  X: borrar." : result.Error!.Message, result.Ok ? UiStrings.NoteCreate(Short(id), Short(mapId), nx, ny) : null);
            return;
        }

        if (selectedEventId == null) return;
        // S: abre el selector visual de sprite (con celda "sin sprite"), en vez de ciclar de a uno.
        if (Raylib.IsKeyPressed(KeyboardKey.S)) { pickingEventSprite = true; paletteOpen = true; paletteScroll = 0; ctx.Sfx?.Play("sfx.cursor"); return; }
        // C: DUPLICA el evento (con toda su logica) en la casilla de al lado, y lo deja elegido para
        // arrastrar. Asi un mismo evento cubre varias casillas: una cama/mesa grande se activa desde
        // cualquiera de sus celdas (cada clon con sus comandos, gracias a la prioridad de interaccion).
        if (Raylib.IsKeyPressed(KeyboardKey.C))
        {
            var src = ctx.Project.Events.FirstOrDefault(e => e.Id == selectedEventId);
            if (src != null)
            {
                var newId = CloneEventId(ctx.Project, src.Id);
                var (nx, ny) = src.Y + 1 < ctx.Map.Height ? (src.X, src.Y + 1) : (Math.Min(ctx.Map.Width - 1, src.X + 1), src.Y);
                var srcId = src.Id;
                var result = ctx.Session.Mutate(p =>
                {
                    p.Events.Add(CloneEvent(p.Events.First(e => e.Id == srcId), newId, nx, ny));
                    var m = p.Maps.First(x => x.Id == mapId);
                    if (!m.EventIds.Contains(newId)) m.EventIds.Add(newId);
                });
                if (result.Ok) { selectedEventId = newId; ctx.Sfx?.Play("sfx.confirm"); }
                ctx.Adopt(ctx.Session.Project, result.Ok ? $"{Short(srcId)} duplicado en ({nx},{ny}). Arrastralo a su casilla." : result.Error!.Message, result.Ok ? $"duplica {Short(srcId)} -> {Short(newId)}" : null);
            }
            return;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.R)) CycleField(ctx, ev => ev.RoutineId, (ev, v) => ev.RoutineId = v, Routines, "rutina");
        // D: dialogo del evento; P: condicion de presencia por flag (la logica basica, con el mouse).
        if (Raylib.IsKeyPressed(KeyboardKey.D) && ctx.Project.Events.FirstOrDefault(e => e.Id == selectedEventId) is { } evD) CycleDialogue(ctx, evD);
        if (Raylib.IsKeyPressed(KeyboardKey.P) && ctx.Project.Events.FirstOrDefault(e => e.Id == selectedEventId) is { } evP) CyclePresence(ctx, evP);
        if (Raylib.IsKeyPressed(KeyboardKey.X) || Raylib.IsKeyPressed(KeyboardKey.Delete)) DeleteSelected(ctx);
    }

    void CycleField(EditorContext ctx, Func<EventDef, string> get, Action<EventDef, string> set, string[] options, string label)
    {
        var id = selectedEventId!;
        var current = ctx.Project.Events.FirstOrDefault(e => e.Id == id);
        if (current == null || options.Length == 0) return;
        var next = options[(Array.IndexOf(options, get(current)) + 1 + options.Length) % options.Length];
        var result = ctx.Session!.Mutate(p => set(p.Events.First(e => e.Id == id), next));
        if (result.Ok) ctx.Sfx?.Play("sfx.cursor");
        ctx.Adopt(ctx.Session.Project, result.Ok ? $"{Short(id)}: {label} = {(next == "" ? "(ninguno)" : Short(next))}." : result.Error!.Message, result.Ok ? $"{Short(id)}: {label} = {(next == "" ? "(ninguno)" : Short(next))}" : null);
    }

    void DeleteSelected(EditorContext ctx)
    {
        var id = selectedEventId!;
        var result = ctx.Session!.Mutate(p =>
        {
            p.Events.RemoveAll(e => e.Id == id);
            foreach (var m in p.Maps) m.EventIds.Remove(id);
        });
        if (result.Ok) { selectedEventId = null; ctx.Sfx?.Play("sfx.cancel"); }
        ctx.Adopt(ctx.Session.Project, result.Ok ? $"{Short(id)} borrado. Ctrl+Z lo revive." : result.Error!.Message, result.Ok ? UiStrings.NoteDelete(Short(id)) : null);
    }

    static string NextEventId(GameProject p)
    {
        for (var n = 1; ; n++)
        {
            var id = $"event.npc_{n}";
            if (p.Events.All(e => e.Id != id)) return id;
        }
    }

    /// <summary>Id libre para un clon, derivado del original: event.cama -> event.cama_2, _3...</summary>
    static string CloneEventId(GameProject p, string baseId)
    {
        for (var n = 2; ; n++) { var id = $"{baseId}_{n}"; if (p.Events.All(e => e.Id != id)) return id; }
    }

    /// <summary>Copia PROFUNDA de un evento (paginas, condiciones y comandos nuevos, sin compartir
    /// referencias) con id y casilla nuevos. Lo demas es identico: misma logica, sprite y estilo.</summary>
    static EventDef CloneEvent(EventDef s, string newId, int x, int y) => new()
    {
        Id = newId, MapId = s.MapId, Name = s.Name, Kind = s.Kind, X = x, Y = y,
        Sprite = s.Sprite, RoutineId = s.RoutineId, Tint = s.Tint, Solid = s.Solid,
        OffsetX = s.OffsetX, OffsetY = s.OffsetY, Scale = s.Scale,
        Pages = s.Pages.Select(pg => new EventPage
        {
            Id = pg.Id,
            Conditions = pg.Conditions.Select(c => new ConditionDef { VariableId = c.VariableId, EqualsValue = c.EqualsValue }).ToList(),
            Commands = pg.Commands.Select(c => new EventCommand { Kind = c.Kind, TargetId = c.TargetId, Value = c.Value }).ToList()
        }).ToList()
    };

    static string NextFlagId(GameProject p)
    {
        for (var n = 1; ; n++)
        {
            var id = $"flag.nueva_{n}";
            if (p.Variables.All(v => v.Id != id)) return id;
        }
    }

    // ---- Logica con el mouse: presencia por flag (P) y dialogo (D) sin pasar por MCP.
    // Cubre el caso simple (1 pagina, 0-1 condicion, 0-1 comando Dialogue): el patron
    // farol-apagado/farol-encendido se arma entero en el editor. Lo complejo sigue via MCP. ----

    /// <summary>P: cicla la condicion de presencia del evento: siempre -> flagA=true -> flagA=false
    /// -> flagB=true -> ... (Shift+P retrocede). Solo eventos de UNA pagina con 0-1 condicion.</summary>
    void CyclePresence(EditorContext ctx, EventDef ev)
    {
        if (ctx.Session == null) return;
        if (ev.Pages.Count > 1) { ctx.Message($"{Short(ev.Id)} tiene {ev.Pages.Count} paginas: esa presencia fina se edita via MCP (event.set_pages)."); return; }
        var page = ev.Pages.Count == 1 ? ev.Pages[0] : null;
        if (page != null && page.Conditions.Count > 1) { ctx.Message($"{Short(ev.Id)} tiene varias condiciones: editar via MCP."); return; }
        var flags = ctx.Project.Variables.Where(v => v.Kind == VariableKind.Flag).Select(v => v.Id).OrderBy(x => x).ToList();
        if (flags.Count == 0) { ctx.Message("No hay flags en el proyecto: crea una en FLAGS con N."); return; }
        // Opciones: "existe siempre" + (flag=true / flag=false) por cada flag.
        var options = new List<(string Var, string Val)> { ("", "") };
        foreach (var f in flags) { options.Add((f, "true")); options.Add((f, "false")); }
        var cur = page != null && page.Conditions.Count == 1 ? (page.Conditions[0].VariableId, page.Conditions[0].EqualsValue.ToLowerInvariant()) : ("", "");
        var idx = options.FindIndex(o => o == cur);
        var dir = Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift) ? -1 : 1;
        var next = options[((idx < 0 ? 0 : idx) + dir + options.Count) % options.Count];
        var id = ev.Id;
        var result = ctx.Session.Mutate(p =>
        {
            var e = p.Events.First(x => x.Id == id);
            if (e.Pages.Count == 0) e.Pages.Add(new EventPage());
            e.Pages[0].Conditions = next.Var == "" ? [] : [new ConditionDef { VariableId = next.Var, EqualsValue = next.Val }];
        });
        if (result.Ok) ctx.Sfx?.Play("sfx.cursor");
        var label = next.Var == "" ? "siempre" : $"si {next.Var} = {next.Val}";
        ctx.Adopt(ctx.Session.Project, result.Ok ? $"{Short(id)} existe: {label}. (P cicla, Shift+P retrocede)" : result.Error!.Message, result.Ok ? $"{Short(id)}: existe {label}" : null);
    }

    /// <summary>D en EVENTOS: cicla el dialogo del evento (ninguno -> cada dialogue del proyecto).
    /// Solo si la pagina es simple (0 comandos, o exactamente un Dialogue): una cutscene no se pisa.</summary>
    void CycleDialogue(EditorContext ctx, EventDef ev)
    {
        if (ctx.Session == null) return;
        if (ev.Pages.Count > 1) { ctx.Message($"{Short(ev.Id)} tiene {ev.Pages.Count} paginas: editar via MCP."); return; }
        var page = ev.Pages.Count == 1 ? ev.Pages[0] : null;
        var simple = page == null || page.Commands.Count == 0 || (page.Commands.Count == 1 && page.Commands[0].Kind == CommandKind.Dialogue);
        if (!simple) { ctx.Message($"{Short(ev.Id)} tiene una cutscene ({page!.Commands.Count} comandos): editala via MCP para no pisarla."); return; }
        if (ctx.Project.Dialogues.Count == 0) { ctx.Message("No hay dialogos en el proyecto (dialogue.create via MCP)."); return; }
        var options = new[] { "" }.Concat(ctx.Project.Dialogues.Select(d => d.Id)).ToArray();
        var cur = page != null && page.Commands.Count == 1 ? page.Commands[0].TargetId : "";
        var next = options[(Array.IndexOf(options, cur) + 1 + options.Length) % options.Length];
        var id = ev.Id;
        var result = ctx.Session.Mutate(p =>
        {
            var e = p.Events.First(x => x.Id == id);
            if (e.Pages.Count == 0) e.Pages.Add(new EventPage());
            e.Pages[0].Commands = next == "" ? [] : [new EventCommand { Kind = CommandKind.Dialogue, TargetId = next }];
        });
        if (result.Ok) ctx.Sfx?.Play("sfx.cursor");
        ctx.Adopt(ctx.Session.Project, result.Ok ? $"{Short(id)} habla: {(next == "" ? "(nada)" : Short(next))}." : result.Error!.Message, result.Ok ? $"{Short(id)}: dialogo = {(next == "" ? "(ninguno)" : Short(next))}" : null);
    }

    /// <summary>true si el evento existe con las FLAGS actuales (aprox del editor: las condiciones
    /// time.* se asumen cumplidas — el editor no simula el reloj). Para pintar etiquetas grises.</summary>
    bool PresentByFlags(EditorContext ctx, EventDef ev) =>
        ev.Pages.Count == 0 || ev.Pages.Any(pg => pg.Conditions.All(c => CondOk(ctx, c)));

    static bool CondOk(EditorContext ctx, ConditionDef c)
    {
        if (c.VariableId.StartsWith("time.")) return true;
        var actual = ctx.Flags.TryGetValue(c.VariableId, out var v) && v;
        return actual == c.EqualsValue.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Resumen corto de la presencia del evento para el inspector.</summary>
    static string PresenceLabel(EventDef ev)
    {
        if (ev.Pages.Count > 1) return $"paginas: {ev.Pages.Count} (MCP)";
        var page = ev.Pages.Count == 1 ? ev.Pages[0] : null;
        if (page == null || page.Conditions.Count == 0) return "siempre";
        if (page.Conditions.Count > 1) return $"{page.Conditions.Count} condiciones (MCP)";
        return $"si {page.Conditions[0].VariableId} = {page.Conditions[0].EqualsValue}";
    }

    /// <summary>Resumen corto del contenido de la pagina para el inspector.</summary>
    static string CommandsLabel(EventDef ev)
    {
        var page = ev.Pages.Count >= 1 ? ev.Pages[0] : null;
        if (page == null || page.Commands.Count == 0) return "(nada)";
        if (page.Commands.Count == 1 && page.Commands[0].Kind == CommandKind.Dialogue) return Short(page.Commands[0].TargetId);
        return $"cutscene: {page.Commands.Count} comandos";
    }

    // ---- Herramienta WARPS: click crea/elige, arrastrar mueve la casilla origen, der/X borra,
    //      M cicla mapa destino, T cicla transicion, D fija el destino en la casilla del cursor.
    //      Los warps viven en map.Warps (tabla, no eventos); se editan por la MISMA CommandSession. ----

    void UpdateWarps(EditorContext ctx, bool inMap)
    {
        if (ctx.Session == null) return;
        var mapId = ctx.Map.Id;
        if (selectedWarp >= ctx.Map.Warps.Count) selectedWarp = -1;
        var overUi = MouseOverUi();

        // Click: sobre un warp lo elige + empieza a arrastrar; en vacio crea uno nuevo.
        if (!overUi && Raylib.IsMouseButtonPressed(MouseButton.Left) && inMap)
        {
            var idx = ctx.Map.Warps.FindIndex(w => w.X == cursorTileX && w.Y == cursorTileY);
            if (idx >= 0) { selectedWarp = idx; draggingWarp = true; ctx.Sfx?.Play("sfx.cursor"); }
            else
            {
                var (sx, sy) = (cursorTileX, cursorTileY);
                var destMap = ctx.Project.Maps.FirstOrDefault(x => x.Id != mapId) ?? ctx.Map;
                var destId = destMap.Id;
                var (dx, dy) = (Math.Clamp(sx, 0, destMap.Width - 1), Math.Clamp(sy, 0, destMap.Height - 1));
                var result = ctx.Session.Mutate(p => p.Maps.First(x => x.Id == mapId).Warps.Add(new WarpDef { X = sx, Y = sy, ToMapId = destId, ToX = dx, ToY = dy, Transition = "" }));
                if (result.Ok) { selectedWarp = ctx.Session.Project.Maps.First(x => x.Id == mapId).Warps.Count - 1; ctx.Sfx?.Play("sfx.confirm"); }
                ctx.Adopt(ctx.Session.Project, result.Ok ? $"Warp en ({sx},{sy}) -> {Short(destId)}. M:mapa T:transicion D:destino X:borrar" : result.Error!.Message, result.Ok ? $"warp ({sx},{sy}) -> {Short(destId)}" : null);
            }
        }

        // Soltar el arrastre: mueve la casilla origen del warp elegido al cursor.
        if (Raylib.IsMouseButtonReleased(MouseButton.Left) && draggingWarp)
        {
            draggingWarp = false;
            if (!overUi && inMap && selectedWarp >= 0 && selectedWarp < ctx.Map.Warps.Count)
            {
                var i = selectedWarp; var (nx, ny) = (cursorTileX, cursorTileY);
                var cur = ctx.Map.Warps[i];
                if (cur.X != nx || cur.Y != ny)
                {
                    var result = ctx.Session.Mutate(p => { var w = p.Maps.First(x => x.Id == mapId).Warps[i]; w.X = nx; w.Y = ny; });
                    if (result.Ok) ctx.Sfx?.Play("sfx.confirm");
                    ctx.Adopt(ctx.Session.Project, result.Ok ? $"Warp movido a ({nx},{ny})." : result.Error!.Message, result.Ok ? UiStrings.NoteMoveWarp(nx, ny) : null);
                }
            }
        }

        // Borrar el warp bajo el cursor: click derecho, Supr o X.
        if ((!overUi && Raylib.IsMouseButtonPressed(MouseButton.Right) || Raylib.IsKeyPressed(KeyboardKey.Delete) || Raylib.IsKeyPressed(KeyboardKey.X)) && inMap)
        {
            var idx = ctx.Map.Warps.FindIndex(w => w.X == cursorTileX && w.Y == cursorTileY);
            if (idx >= 0)
            {
                var result = ctx.Session.Mutate(p => p.Maps.First(x => x.Id == mapId).Warps.RemoveAt(idx));
                if (result.Ok) { selectedWarp = -1; ctx.Sfx?.Play("sfx.cancel"); }
                ctx.Adopt(ctx.Session.Project, result.Ok ? "Warp borrado. Ctrl+Z lo revive." : result.Error!.Message, result.Ok ? UiStrings.NoteDeleteWarp(Short(mapId)) : null);
            }
            return;
        }

        if (selectedWarp < 0 || selectedWarp >= ctx.Map.Warps.Count) return;
        var si = selectedWarp;

        // M: cicla el mapa destino (y clampa las coords destino a los limites del nuevo mapa).
        if (Raylib.IsKeyPressed(KeyboardKey.M))
        {
            var maps = ctx.Project.Maps.Select(m => m.Id).ToArray();
            var next = maps[(Array.IndexOf(maps, ctx.Map.Warps[si].ToMapId) + 1 + maps.Length) % maps.Length];
            var nd = ctx.Project.Maps.First(m => m.Id == next);
            var result = ctx.Session.Mutate(p => { var w = p.Maps.First(x => x.Id == mapId).Warps[si]; w.ToMapId = next; w.ToX = Math.Clamp(w.ToX, 0, nd.Width - 1); w.ToY = Math.Clamp(w.ToY, 0, nd.Height - 1); });
            if (result.Ok) ctx.Sfx?.Play("sfx.cursor");
            ctx.Adopt(ctx.Session.Project, result.Ok ? $"Warp -> mapa {Short(next)}." : result.Error!.Message, result.Ok ? $"warp -> {Short(next)}" : null);
            return;
        }
        // T: cicla la transicion (default/fade/iris/spiral).
        if (Raylib.IsKeyPressed(KeyboardKey.T))
        {
            var next = Transitions[(Array.IndexOf(Transitions, ctx.Map.Warps[si].Transition) + 1 + Transitions.Length) % Transitions.Length];
            var result = ctx.Session.Mutate(p => p.Maps.First(x => x.Id == mapId).Warps[si].Transition = next);
            if (result.Ok) ctx.Sfx?.Play("sfx.cursor");
            ctx.Adopt(ctx.Session.Project, result.Ok ? $"Warp transicion: {(next == "" ? "(default)" : next)}." : result.Error!.Message, result.Ok ? $"warp: transicion {(next == "" ? "default" : next)}" : null);
            return;
        }
        // D: fija el destino en la casilla del cursor DEL MAPA MOSTRADO (warp dentro del mismo mapa,
        // o cuando estas parado en el mapa de llegada). El destino cross-map fino: elegir mapa con M.
        if (Raylib.IsKeyPressed(KeyboardKey.D) && inMap)
        {
            var (dx, dy) = (cursorTileX, cursorTileY);
            var result = ctx.Session.Mutate(p => { var w = p.Maps.First(x => x.Id == mapId).Warps[si]; w.ToMapId = mapId; w.ToX = dx; w.ToY = dy; });
            if (result.Ok) ctx.Sfx?.Play("sfx.confirm");
            ctx.Adopt(ctx.Session.Project, result.Ok ? $"Destino del warp = {Short(mapId)} ({dx},{dy})." : result.Error!.Message, result.Ok ? $"warp: destino {Short(mapId)} ({dx},{dy})" : null);
            return;
        }
    }

    // ---- Herramienta FLAGS: panel en vivo (click alterna) + click en el mapa = telepuerto ----

    (int X, int Y, int W, int H, int RowsTop) FlagsLayout(EditorContext ctx)
    {
        var w = Raylib.GetScreenWidth();
        var keys = ctx.Flags.Count;
        var pw = Math.Min(660, w - 20);
        var rowsTop = BarH + 8 + 2 * UiRow + 8;
        return (10, BarH + 8, pw, 2 * UiRow + 12 + keys * UiRow + 8, rowsTop);
    }

    void UpdateFlags(EditorContext ctx, bool inMap)
    {
        // N: crea una flag nueva (flag.nueva_N, default false) sin pasar por el MCP — despues
        // se le condiciona presencia a objetos/eventos con la tecla P.
        if (Raylib.IsKeyPressed(KeyboardKey.N) && ctx.Session != null)
        {
            var id = NextFlagId(ctx.Project);
            var result = ctx.Session.Mutate(p => p.Variables.Add(new GameVariable { Id = id, Kind = VariableKind.Flag, Default = "false" }));
            if (result.Ok) ctx.Sfx?.Play("sfx.confirm");
            ctx.Adopt(ctx.Session.Project, result.Ok ? $"Flag {id} creada (false). Click la alterna; P en OBJETOS/EVENTOS condiciona presencia." : result.Error!.Message, result.Ok ? UiStrings.NoteCreateId(id) : null);
            return;
        }
        if (!Raylib.IsMouseButtonPressed(MouseButton.Left)) return;
        var keys = ctx.Flags.Keys.OrderBy(k => k).ToList();
        var (px, py, pw, ph, rowsTop) = FlagsLayout(ctx);
        var mx = Raylib.GetMouseX();
        var my = Raylib.GetMouseY();
        if (mx >= px && mx < px + pw && my >= rowsTop && my < rowsTop + keys.Count * UiRow)
        {
            var key = keys[(my - rowsTop) / UiRow];
            ctx.Flags[key] = !ctx.Flags[key];
            ctx.Sfx?.Play("sfx.cursor");
            ctx.Message($"{key} = {(ctx.Flags[key] ? "true" : "false")} (solo runtime, no se guarda solo).");
            return;
        }
        if (mx >= px && mx < px + pw && my >= py && my < py + ph) return; // el resto del panel no telepuerta
        if (my < BarH) return;
        if (inMap)
        {
            ctx.Player.Teleport(cursorTileX, cursorTileY);
            ctx.Message($"Telepuerto a ({cursorTileX},{cursorTileY}).");
        }
    }

    // ---- Capa LIENZO (Draw): solo overlays espaciales, pixel-perfect con el mundo ----

    public void Draw(EditorContext ctx)
    {
        if (!Visible) return;
        var ts = ctx.TileSize;
        DrawGrid(ctx);
        switch (tool)
        {
            case Tool.Tiles: DrawTilesCanvas(ctx, ts); break;
            case Tool.Objetos: DrawObjetosCanvas(ctx, ts); break;
            case Tool.Eventos: DrawEventosCanvas(ctx, ts); break;
            case Tool.Warps: DrawWarpsCanvas(ctx, ts); break;
            case Tool.Flags: DrawCursorCell(ctx, ts, ctx.Theme.AccentColor); break;
        }
    }

    void DrawGrid(EditorContext ctx)
    {
        if (!gridVisible) return;
        var ts = ctx.TileSize;
        var vts = Vts(ctx);
        var solid = ctx.Tileset.Tiles.Where(t => t.Solid).Select(t => t.Id).ToHashSet();
        var step = vts < 6 ? 2 : 1; // a 1/4 la grilla se dibuja cada 2 celdas para no empastar
        for (var y = 0; y < ctx.Map.Height; y++) for (var x = 0; x < ctx.Map.Width; x++)
        {
            var (px, py) = ToScreen(ctx, x * ts, y * ts);
            if (px < -vts * step || py < -vts * step || px > ctx.Screen.Width || py > ctx.Screen.Height) continue;
            if (x % step == 0 && y % step == 0) Raylib.DrawRectangleLines(px, py, vts * step, vts * step, new Color(255, 255, 255, 40));
            if (solid.Contains(ctx.Map.Tiles[y * ctx.Map.Width + x])) Raylib.DrawLine(px, py, px + vts, py + vts, new Color(255, 80, 80, 130));
        }
    }

    void DrawTilesCanvas(EditorContext ctx, int ts)
    {
        var vts = Vts(ctx);
        // Vista previa del stroke en curso: el tile REAL con su orientacion (WYSIWYG del pincel);
        // sin atlas cae al color plano. Igual que en el juego, asi la rotacion se ve antes de soltar.
        var tileDef = ctx.Tileset.Tiles.FirstOrDefault(t => t.Id == selectedTile);
        var brushTint = Tints.Parse(tileDef?.Tint);
        foreach (var (cx, cy) in stroke)
        {
            var (px, py) = ToScreen(ctx, cx * ts, cy * ts);
            var rect = new Rectangle(px, py, vts, vts);
            if (ctx.Tiles == null || !ctx.Tiles.TryDraw(ctx.Tileset, selectedTile, rect, new Color(brushTint.R, brushTint.G, brushTint.B, (byte)190), selectedTileFlags))
                TileFallback.Draw(tileDef, rect, new Color(brushTint.R, brushTint.G, brushTint.B, (byte)170));
        }
        // Fantasma del pincel bajo el cursor cuando no hay stroke/seleccion/stamp: muestra el tile
        // en su orientacion actual, para ver como cae la rotacion antes de pintar.
        if (!pasting && selRect == null && stroke.Count == 0 && cursorTileX >= 0 && cursorTileY >= 0 && cursorTileX < ctx.Map.Width && cursorTileY < ctx.Map.Height)
        {
            var (px, py) = ToScreen(ctx, cursorTileX * ts, cursorTileY * ts);
            var rect = new Rectangle(px, py, vts, vts);
            if (ctx.Tiles == null || !ctx.Tiles.TryDraw(ctx.Tileset, selectedTile, rect, new Color(255, 255, 255, 140), selectedTileFlags))
                TileFallback.Draw(tileDef, rect, new Color(brushTint.R, brushTint.G, brushTint.B, (byte)140));
        }

        // Seleccion vigente (Shift+arrastrar): outline de acento + velo.
        if (selRect is { } sr)
        {
            var (sx, sy) = ToScreen(ctx, sr.X * ts, sr.Y * ts);
            Raylib.DrawRectangle(sx, sy, sr.W * vts, sr.H * vts, new Color(120, 200, 255, 50));
            Raylib.DrawRectangleLines(sx, sy, sr.W * vts, sr.H * vts, ctx.Theme.AccentColor);
        }

        // Fantasma del stamp en modo pegado: el portapapeles sigue al cursor, semitransparente.
        if (pasting && clipboard != null && cursorTileX >= 0 && cursorTileY >= 0)
        {
            var ghost = new Color(255, 255, 255, 150);
            for (var y = 0; y < clipboard.H; y++) for (var x = 0; x < clipboard.W; x++)
            {
                var dx = cursorTileX + x;
                var dy = cursorTileY + y;
                if (dx < 0 || dy < 0 || dx >= ctx.Map.Width || dy >= ctx.Map.Height) continue;
                var (px, py) = ToScreen(ctx, dx * ts, dy * ts);
                var id = clipboard.Tiles[y * clipboard.W + x];
                var def = ctx.Tileset.Tiles.FirstOrDefault(t => t.Id == id);
                var rect = new Rectangle(px, py, vts, vts);
                if (ctx.Tiles == null || !ctx.Tiles.TryDraw(ctx.Tileset, id, rect, ghost, clipboard.Flags[y * clipboard.W + x]))
                {
                    var fallbackTint = Tints.Parse(def?.Tint);
                    TileFallback.Draw(def, rect, new Color(fallbackTint.R, fallbackTint.G, fallbackTint.B, (byte)150));
                }
            }
            var (gx, gy) = ToScreen(ctx, cursorTileX * ts, cursorTileY * ts);
            Raylib.DrawRectangleLines(gx, gy, clipboard.W * vts, clipboard.H * vts, UiGreen);
        }

        DrawCursorCell(ctx, ts, ctx.Theme.AccentColor);
    }

    void DrawObjetosCanvas(EditorContext ctx, int ts)
    {
        DrawCursorCell(ctx, ts, ctx.Theme.AccentColor);
        var vts = Vts(ctx);
        // Resalta el prop bajo el cursor (por su footprint visual, no su celda): asi sabes CUAL vas
        // a mover/borrar/escalar/teñir, y coincide con lo que efectivamente vas a agarrar al hacer click.
        var hover = PropAtCursor(ctx);
        if (hover != null && ctx.Sprites != null)
        {
            var (hw, hh) = string.IsNullOrEmpty(hover.Sprite) ? (0, 0) : ctx.Sprites.SizeOf(hover.Sprite);
            if (hw > 0)
            {
                var sc = hover.Scale > 0.01f ? hover.Scale : 1f;
                var dw = (int)(hw * sc); var dh = (int)(hh * sc);
                var (hx, hy) = ToScreen(ctx, hover.X * ts + hover.OffsetX + (ts - dw) / 2, hover.Y * ts + hover.OffsetY + ts - dh);
                Raylib.DrawRectangleLines(hx - 1, hy - 1, dw / ZoomDiv + 2, dh / ZoomDiv + 2, UiGreen);
            }
            else { var (hx, hy) = ToScreen(ctx, hover.X * ts, hover.Y * ts); Raylib.DrawRectangleLines(hx, hy, vts, vts, UiGreen); }
        }
        var sprites = ctx.Project.Sprites;
        // Fantasma: si arrastro un prop lo muestro siguiendo el cursor (posicion libre); si no,
        // el sprite elegido para colocar. Sobre un prop existente NO se muestra (ahi se arrastra,
        // no se coloca): solo el resaltado. Ambos anclados a los pies, en la posicion final.
        // Solo a zoom 1x: con zoom out el sprite nativo no coincide con la escala del mundo.
        if (zoomLevel == 0 && ctx.Sprites != null && (hover == null || objDragId != null) && cursorTileX >= 0 && cursorTileY >= 0 && cursorTileX < ctx.Map.Width && cursorTileY < ctx.Map.Height && !MouseOverUi())
        {
            var dragged = objDragId != null ? ctx.Project.Events.FirstOrDefault(e => e.Id == objDragId) : null;
            var spriteId = dragged?.Sprite ?? (sprites.Count > 0 ? sprites[Math.Clamp(selectedSprite, 0, sprites.Count - 1)].Id : "");
            var solid = dragged?.Solid ?? currentSolid;
            var tintHex = dragged?.Tint ?? currentTint;
            if (!string.IsNullOrWhiteSpace(spriteId))
            {
                var (sw, sh) = ctx.Sprites.SizeOf(spriteId);
                if (sw > 0)
                {
                    var (gmx, gmy) = ctx.Screen.ToVirtual(Raylib.GetMouseX(), Raylib.GetMouseY());
                    var (tx, ty, ox, oy) = FreePlace(gmx, gmy, ctx, solid);
                    var px = tx * ts + ox - ctx.Camera.X + (ts - sw) / 2;
                    var py = ty * ts + oy - ctx.Camera.Y + ts - sh;
                    var gt = Tints.Parse(tintHex);
                    ctx.Sprites.TryDraw(spriteId, Facing.Down, 0, px, py, new Color(gt.R, gt.G, gt.B, (byte)150));
                }
            }
        }
    }

    /// <summary>Un evento "interactivo" HACE algo: NPC, Trigger o Cutscene siempre, o un Object con
    /// comandos (un prop que se examina/usa). Los props decorativos y los bloqueos invisibles (Object
    /// sin comandos) son escenografia/colision, no logica — se administran en la herramienta OBJETOS.</summary>
    static bool IsInteractive(EventDef e) => e.Kind != EventKind.Object || e.Pages.Any(pg => pg.Commands.Count > 0);

    static readonly Color EventPink = new(255, 90, 190, 255);

    void DrawEventosCanvas(EditorContext ctx, int ts)
    {
        var vts = Vts(ctx);
        // Cada evento de LOGICA = celda ROSA rellena (el analogo del celeste de los warps): asi se ven
        // de un vistazo y se distinguen de los props decorativos, que aca no se marcan (son de OBJETOS).
        foreach (var ev in ctx.Project.Events.Where(e => e.MapId == ctx.Map.Id && IsInteractive(e)))
        {
            var (px, py) = ToScreen(ctx, ev.X * ts, ev.Y * ts);
            var present = PresentByFlags(ctx, ev);
            Raylib.DrawRectangle(px, py, vts, vts, new Color(255, 60, 170, present ? 70 : 30));
            Raylib.DrawRectangleLines(px, py, vts, vts, ev.Id == selectedEventId ? ctx.Theme.AccentColor : (present ? EventPink : new Color(180, 120, 160, 255)));
        }
        if (draggingEvent && selectedEventId != null) { var (dx, dy) = ToScreen(ctx, cursorTileX * ts, cursorTileY * ts); Raylib.DrawRectangleLines(dx, dy, vts, vts, UiGreen); }
        DrawCursorCell(ctx, ts, ctx.Theme.AccentColor);
    }

    void DrawWarpsCanvas(EditorContext ctx, int ts)
    {
        // Cada warp = celda celeste; el elegido en color de acento (la etiqueta va en la capa de UI).
        var vts = Vts(ctx);
        for (var i = 0; i < ctx.Map.Warps.Count; i++)
        {
            var wp = ctx.Map.Warps[i];
            var (px, py) = ToScreen(ctx, wp.X * ts, wp.Y * ts);
            var col = i == selectedWarp ? ctx.Theme.AccentColor : new Color(0, 190, 255, 255);
            Raylib.DrawRectangle(px, py, vts, vts, new Color(0, 150, 255, 70));
            Raylib.DrawRectangleLines(px, py, vts, vts, col);
        }
        if (draggingWarp && cursorTileX >= 0 && cursorTileY >= 0)
        {
            var (dx, dy) = ToScreen(ctx, cursorTileX * ts, cursorTileY * ts);
            Raylib.DrawRectangleLines(dx, dy, vts, vts, UiGreen);
        }
        DrawCursorCell(ctx, ts, ctx.Theme.AccentColor);
    }

    void DrawCursorCell(EditorContext ctx, int ts, Color color)
    {
        if (cursorTileX < 0 || cursorTileY < 0 || cursorTileX >= ctx.Map.Width || cursorTileY >= ctx.Map.Height) return;
        var (px, py) = ToScreen(ctx, cursorTileX * ts, cursorTileY * ts);
        Raylib.DrawRectangleLines(px, py, Vts(ctx), Vts(ctx), color);
    }

    // ---- Capa VENTANA (DrawUi): toda la UI con texto, a resolucion nativa ----

    const int InspW = 300;
    const int InspH = 236;

    public void DrawUi(EditorContext ctx)
    {
        if (!Visible) return;
        var w = Raylib.GetScreenWidth();
        var h = Raylib.GetScreenHeight();

        DrawWorldLabels(ctx);
        DrawBar(ctx, w);

        switch (tool)
        {
            case Tool.Tiles: DrawTilesUi(ctx, w, h); break;
            case Tool.Objetos: DrawObjetosUi(ctx, w, h); break;
            case Tool.Eventos: if (pickingEventSprite) DrawEventSpritePicker(ctx, w, h); else DrawEventosUi(ctx, w); break;
            case Tool.Warps: DrawWarpsUi(ctx, w); break;
            case Tool.Dialogo: DrawDialogoUi(ctx, w, h); break;
            case Tool.Historia: DrawHistoriaUi(ctx, w, h); break;
            case Tool.Flags: DrawFlagsUi(ctx); break;
            case Tool.Historial: DrawHistorialUi(ctx, w, h); break;
        }

        DrawMinimap(ctx);
        if (helpOpen) DrawHelp(ctx, w, h);
    }

    void DrawBar(EditorContext ctx, int w)
    {
        Raylib.DrawRectangle(0, 0, w, BarH, UiBg);
        Raylib.DrawRectangle(0, BarH - 1, w, 1, UiBorder);
        UiText("EDITOR", 10, 6, ctx.Theme.AccentColor);
        var x = 10 + UiMeasure("EDITOR") + 26;
        var tabs = w < 900
            ? new[] { (Tool.Tiles, "MAPA"), (Tool.Objetos, "OBJ"), (Tool.Eventos, "EVENT"), (Tool.Warps, "WARP"), (Tool.Dialogo, "DIAL"), (Tool.Historia, "LIBRO"), (Tool.Flags, "FLAGS"), (Tool.Historial, "LOG") }
            : new[] { (Tool.Tiles, "TILES"), (Tool.Objetos, "OBJETOS"), (Tool.Eventos, "EVENTOS"), (Tool.Warps, "WARPS"), (Tool.Dialogo, "DIALOGO"), (Tool.Historia, "LIBRO"), (Tool.Flags, "FLAGS"), (Tool.Historial, "LOG") };
        foreach (var (t, label) in tabs)
        {
            var tw = UiMeasure(label);
            UiText(label, x, 6, t == tool ? ctx.Theme.AccentColor : UiGray);
            if (t == tool) Raylib.DrawRectangle(x, BarH - 4, tw, 2, ctx.Theme.AccentColor);
            x += tw + 12;
        }
        // Lado derecho: estado del cursor/zoom, o el aviso de solo-lectura (capturas/run-pack).
        var zoomTag = zoomLevel == 0 ? "1x" : zoomLevel == 1 ? "1/2" : "1/4";
        var status = ctx.Session == null ? "SOLO LECTURA" : $"({cursorTileX},{cursorTileY})  z:{zoomTag}  H: ayuda";
        var sw = UiMeasure(status);
        if (w - sw - 10 > x + 10) UiText(status, w - sw - 10, 6, ctx.Session == null ? new Color(255, 110, 110, 255) : UiGray);
    }

    /// <summary>Etiquetas ancladas al mundo, en texto nitido de ventana: ids de eventos sobre su
    /// casilla autorada (todas las herramientas) y destino de cada warp (solo en WARPS).</summary>
    void DrawWorldLabels(EditorContext ctx)
    {
        var ts = ctx.TileSize;
        var h = Raylib.GetScreenHeight();
        foreach (var ev in ctx.Project.Events.Where(e => e.MapId == ctx.Map.Id))
        {
            // Solo se etiquetan los eventos de LOGICA (rosa): los props decorativos empapelaban el
            // mapa con obj_N y confundian; se administran en OBJETOS, no aca.
            if (!IsInteractive(ev)) continue;
            // Un Object interactivo (prop que se examina) se etiqueta solo en EVENTOS; NPC/trigger/cutscene siempre.
            if (ev.Kind == EventKind.Object && tool != Tool.Eventos) continue;
            var (px, py) = WinFromWorld(ctx, ev.X * ts, ev.Y * ts);
            if (py < BarH + 2 || py > h) continue;
            // Gris = con las FLAGS actuales este evento NO existe en el mundo (presencia por paginas).
            UiText(Short(ev.Id), px, py - UiSize - 2, PresentByFlags(ctx, ev) ? EventPink : new Color(110, 114, 128, 255));
        }
        if (tool != Tool.Warps) return;
        for (var i = 0; i < ctx.Map.Warps.Count; i++)
        {
            var wp = ctx.Map.Warps[i];
            var (px, py) = WinFromWorld(ctx, wp.X * ts, wp.Y * ts);
            if (py < BarH + 2 || py > h) continue;
            UiText($">{Short(wp.ToMapId)}", px, py - UiSize - 2, i == selectedWarp ? ctx.Theme.AccentColor : new Color(0, 190, 255, 255));
        }
    }

    void DrawTilesUi(EditorContext ctx, int w, int h)
    {
        var tileDef = ctx.Tileset.Tiles.FirstOrDefault(t => t.Id == selectedTile);
        var selIndex = ctx.Tileset.Tiles.FindIndex(t => t.Id == selectedTile);
        PaletteDraw(ctx, ctx.Tileset.Tiles.Count, selIndex, (i, r) =>
        {
            var tile = ctx.Tileset.Tiles[i];
            var tint = Tints.Parse(tile.Tint); // el thumbnail muestra el tile ya teñido (y animado si tiene Frames)
            if (ctx.Tiles == null || !ctx.Tiles.TryDraw(ctx.Tileset, TileBank.AnimCell(tile, tile.Id, ctx.Tileset.AnimMs), r, tint))
                TileFallback.Draw(tile, r, tint);
            if (tile.Solid) Raylib.DrawLine((int)r.X, (int)r.Y, (int)(r.X + r.Width), (int)(r.Y + r.Height), new Color(255, 80, 80, 200));
        });
        if (!paletteOpen) return;
        var animName = tileDef == null || tileDef.Frames.Count == 0 ? "" : $" [anim {tileDef.Frames.Count}]";
        var tintName = string.IsNullOrEmpty(tileDef?.Tint) ? "" : $" [{Tints.Name(tileDef?.Tint)}]";
        var mode = pasting ? "  STAMP: click estampa, ESC sale" : selRect != null ? "  Ctrl+C/X copia/corta" : "  R: rota  F: flood  Shift: seleccion";
        var orientName = selectedTileFlags == 0 ? "" : $" [{OrientName(selectedTileFlags)}]";
        UiText($"{selectedTile}: {tileDef?.Name ?? "?"}{(tileDef?.Solid == true ? " [solido]" : "")}{tintName}{animName}{orientName}{mode}", 14, PaletteTop(h) - UiRow, ctx.Theme.TextColor);
    }

    void DrawObjetosUi(EditorContext ctx, int w, int h)
    {
        var sprites = ctx.Project.Sprites;
        PaletteDraw(ctx, sprites.Count, Math.Clamp(selectedSprite, 0, Math.Max(0, sprites.Count - 1)), (i, r) =>
        {
            if (ctx.Sprites == null || !ctx.Sprites.TryDrawFit(sprites[i].Id, (int)r.X, (int)r.Y, (int)r.Width, Color.White))
                Raylib.DrawRectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height, new Color(70, 70, 90, 255));
        });
        if (!paletteOpen) return;
        var sel = sprites.Count > 0 ? Short(sprites[Math.Clamp(selectedSprite, 0, sprites.Count - 1)].Id) : "(sin sprites)";
        UiText($"{sel}   der: borrar  rueda: escala  T: tinte  B: bloquea({(currentSolid ? "si" : "no")})  I: bloqueo invisible", 14, PaletteTop(h) - UiRow, ctx.Theme.TextColor);
    }

    /// <summary>Selector visual de sprite del evento elegido: la misma paleta scrolleable de OBJETOS
    /// con una celda "sin sprite" (X) al frente. Un click asigna; ESC o S cancela.</summary>
    void DrawEventSpritePicker(EditorContext ctx, int w, int h)
    {
        var sprites = ctx.Project.Sprites;
        var sel = selectedEventId == null ? null : ctx.Project.Events.FirstOrDefault(e => e.Id == selectedEventId);
        var curIdx = sel == null || string.IsNullOrEmpty(sel.Sprite) ? 0 : sprites.FindIndex(s => s.Id == sel.Sprite) + 1;
        PaletteDraw(ctx, sprites.Count + 1, curIdx, (i, r) =>
        {
            if (i == 0)
            {
                Raylib.DrawRectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height, new Color(48, 40, 52, 255));
                Raylib.DrawLine((int)r.X + 5, (int)r.Y + 5, (int)(r.X + r.Width - 5), (int)(r.Y + r.Height - 5), EventPink);
                Raylib.DrawLine((int)(r.X + r.Width - 5), (int)r.Y + 5, (int)r.X + 5, (int)(r.Y + r.Height - 5), EventPink);
            }
            else if (ctx.Sprites == null || !ctx.Sprites.TryDrawFit(sprites[i - 1].Id, (int)r.X, (int)r.Y, (int)r.Width, Color.White))
                Raylib.DrawRectangle((int)r.X, (int)r.Y, (int)r.Width, (int)r.Height, new Color(70, 70, 90, 255));
        });
        if (paletteOpen) UiText("Sprite del evento:  X = sin sprite (invisible)   click elige   ESC/S cancela", 14, PaletteTop(h) - UiRow, ctx.Theme.TextColor);
    }

    void DrawEventosUi(EditorContext ctx, int w)
    {
        var selected = selectedEventId == null ? null : ctx.Project.Events.FirstOrDefault(e => e.Id == selectedEventId && e.MapId == ctx.Map.Id);
        UiPanel(w - InspW - 10, BarH + 8, InspW, InspH);
        var lines = selected == null
            ? new[] { "Sin seleccion", "", "ROSA = evento de logica", "(props decorativos: OBJETOS)", "", "click: elegir  arrastrar: mover", "N: nuevo NPC" }
            : new[]
            {
                Short(selected.Id),
                $"{selected.Kind}  ({selected.X},{selected.Y})",
                $"sprite: {(selected.Sprite == "" ? "(no)" : Short(selected.Sprite))}",
                $"rutina: {selected.RoutineId}",
                $"habla: {CommandsLabel(selected)}",
                $"existe: {PresenceLabel(selected)}",
                "S: sprite  R: rutina  D: dialogo",
                "C: duplica  P: presencia  X: borrar"
            };
        for (var i = 0; i < lines.Length; i++)
            UiText(lines[i], w - InspW, BarH + 18 + i * UiRow, i == 0 ? ctx.Theme.AccentColor : ctx.Theme.TextColor);
    }

    void DrawWarpsUi(EditorContext ctx, int w)
    {
        UiPanel(w - InspW - 10, BarH + 8, InspW, InspH);
        string[] lines = selectedWarp >= 0 && selectedWarp < ctx.Map.Warps.Count
            ? new[]
            {
                $"Warp ({ctx.Map.Warps[selectedWarp].X},{ctx.Map.Warps[selectedWarp].Y})",
                $"-> {Short(ctx.Map.Warps[selectedWarp].ToMapId)} ({ctx.Map.Warps[selectedWarp].ToX},{ctx.Map.Warps[selectedWarp].ToY})",
                $"transicion: {(ctx.Map.Warps[selectedWarp].Transition == "" ? "(default)" : ctx.Map.Warps[selectedWarp].Transition)}",
                "M: mapa   T: transicion",
                "D: destino en el cursor",
                "X: borrar",
            }
            : new[] { "Sin warp elegido", "", "click vacio: crear", "click warp: elegir", "arrastrar: mover", "der/X: borrar" };
        for (var i = 0; i < lines.Length; i++)
            UiText(lines[i], w - InspW, BarH + 18 + i * UiRow, i == 0 ? ctx.Theme.AccentColor : ctx.Theme.TextColor);
    }

    void DrawFlagsUi(EditorContext ctx)
    {
        var keys = ctx.Flags.Keys.OrderBy(k => k).ToList();
        var (px, py, pw, ph, rowsTop) = FlagsLayout(ctx);
        UiPanel(px, py, pw, ph);
        UiText($"mapa: {Short(ctx.Map.Id)}   jugador: ({ctx.Player.TileX},{ctx.Player.TileY})", px + 10, py + 8, UiGreen);
        UiText("click flag: alternar   N: nueva flag   click mapa: telepuerto", px + 10, py + 8 + UiRow, UiGray);
        for (var i = 0; i < keys.Count; i++)
        {
            var value = ctx.Flags[keys[i]];
            UiText($"{keys[i]} = {(value ? "true" : "false")}", px + 10, rowsTop + i * UiRow, value ? ctx.Theme.AccentColor : Color.White);
        }
    }

    /// <summary>Bitacora de co-autoria: las operaciones del humano [vos] y de la IA [ia] en una sola
    /// linea de tiempo, la mas nueva arriba. Ctrl+Z las deshace en ese mismo orden.</summary>
    void DrawHistorialUi(EditorContext ctx, int w, int h)
    {
        UiPanel(10, BarH + 8, w - 20, h - BarH - 20);
        UiText("HISTORIAL de la sesion  (Ctrl+Z deshace, Ctrl+Y rehace)", 20, BarH + 16, UiGreen);
        if (ctx.Session == null) { UiText("Sin sesion: solo lectura.", 20, BarH + 16 + UiRow, ctx.Theme.TextColor); return; }
        if (ctx.Session.History.Count == 0) { UiText("Todavia no hay operaciones.", 20, BarH + 16 + UiRow, ctx.Theme.TextColor); return; }
        var visible = Math.Min((h - BarH - 40 - UiRow) / UiRow, ctx.Session.History.Count);
        for (var i = 0; i < visible; i++)
        {
            var entry = ctx.Session.History[ctx.Session.History.Count - 1 - i];
            var color = entry.StartsWith("[ia]") ? ctx.Theme.AccentColor : ctx.Theme.TextColor;
            UiText(entry, 20, BarH + 16 + (i + 1) * UiRow, i == 0 ? color : Dim(color));
        }
    }

    static Color Dim(Color c) => new((byte)(c.R * 3 / 4), (byte)(c.G * 3 / 4), (byte)(c.B * 3 / 4), c.A);

    // ---- Minimapa: el mapa entero en un vistazo, con viewport clickeable (coords de ventana) ----

    readonly record struct MinimapInfo(int X, int Y, int W, int H, int P, int OffX, int OffY, int VisW, int VisH);

    /// <summary>Geometria del minimapa (null si esta herramienta no lo muestra): 2-8 px por tile,
    /// anclado arriba a la derecha (baja bajo el inspector en EVENTOS/WARPS); mapas mas grandes
    /// que el panel se recortan centrados en la camara.</summary>
    MinimapInfo? MinimapLayout(EditorContext ctx)
    {
        if (tool is Tool.Flags or Tool.Historia or Tool.Historial) return null; // esas herramientas usan paneles grandes
        var mapW = ctx.Map.Width;
        var mapH = ctx.Map.Height;
        if (mapW <= 0 || mapH <= 0) return null;
        var p = Math.Clamp(Math.Min(180 / mapW, 140 / mapH), 2, 8);
        var visW = Math.Min(mapW, Math.Max(1, 240 / p));
        var visH = Math.Min(mapH, Math.Max(1, 180 / p));
        var ts = ctx.TileSize;
        var camCx = (ctx.Camera.X + ctx.Screen.Width * ZoomDiv / 2) / ts;
        var camCy = (ctx.Camera.Y + ctx.Screen.Height * ZoomDiv / 2) / ts;
        var offX = Math.Clamp(camCx - visW / 2, 0, mapW - visW);
        var offY = Math.Clamp(camCy - visH / 2, 0, mapH - visH);
        var y = tool is Tool.Eventos or Tool.Warps ? BarH + 8 + InspH + 10 : BarH + 10;
        return new MinimapInfo(Raylib.GetScreenWidth() - visW * p - 12, y, visW * p, visH * p, p, offX, offY, visW, visH);
    }

    void DrawMinimap(EditorContext ctx)
    {
        if (tool is Tool.Dialogo or Tool.Historia) return; // usan la pantalla entera: el minimapa taparia el panel
        if (!minimapOpen || MinimapLayout(ctx) is not { } mm) return;
        // Cache de colores planos del tileset (TileDef.Color x Tint): honesto y barato, sin
        // muestrear el atlas PNG. La referencia del tileset cambia con cada edicion/hot reload.
        if (!ReferenceEquals(minimapRef, ctx.Tileset))
        {
            minimapRef = ctx.Tileset;
            minimapColors.Clear();
            foreach (var t in ctx.Tileset.Tiles) minimapColors[t.Id] = Tints.Multiply(SpriteRaster.ParseColor(t.Color), Tints.Parse(t.Tint));
        }
        Raylib.DrawRectangle(mm.X - 3, mm.Y - 3, mm.W + 6, mm.H + 6, UiBg);
        for (var y = 0; y < mm.VisH; y++) for (var x = 0; x < mm.VisW; x++)
        {
            var id = ctx.Map.Tiles[(mm.OffY + y) * ctx.Map.Width + mm.OffX + x];
            Raylib.DrawRectangle(mm.X + x * mm.P, mm.Y + y * mm.P, mm.P, mm.P, minimapColors.TryGetValue(id, out var c) ? c : new Color(40, 40, 52, 255));
        }
        void Dot(int tx, int ty, Color c, int size)
        {
            if (tx < mm.OffX || ty < mm.OffY || tx >= mm.OffX + mm.VisW || ty >= mm.OffY + mm.VisH) return;
            Raylib.DrawRectangle(mm.X + (tx - mm.OffX) * mm.P, mm.Y + (ty - mm.OffY) * mm.P, Math.Max(size, mm.P), Math.Max(size, mm.P), c);
        }
        foreach (var ev in ctx.Project.Events.Where(e => e.MapId == ctx.Map.Id)) Dot(ev.X, ev.Y, UiGreen, 2);
        foreach (var wp in ctx.Map.Warps) Dot(wp.X, wp.Y, new Color(0, 190, 255, 255), 2);
        Dot(ctx.Player.TileX, ctx.Player.TileY, new Color(245, 225, 90, 255), 3);
        // Viewport de la camara (con el zoom actual), recortado al panel.
        var ts = ctx.TileSize;
        var vx = mm.X + (ctx.Camera.X / ts - mm.OffX) * mm.P;
        var vy = mm.Y + (ctx.Camera.Y / ts - mm.OffY) * mm.P;
        var vw = ctx.Screen.Width * ZoomDiv / ts * mm.P;
        var vh = ctx.Screen.Height * ZoomDiv / ts * mm.P;
        var rx0 = Math.Max(vx, mm.X); var ry0 = Math.Max(vy, mm.Y);
        var rx1 = Math.Min(vx + vw, mm.X + mm.W); var ry1 = Math.Min(vy + vh, mm.Y + mm.H);
        if (rx1 > rx0 && ry1 > ry0) Raylib.DrawRectangleLines(rx0, ry0, rx1 - rx0, ry1 - ry0, new Color(255, 255, 255, 210));
        Raylib.DrawRectangleLines(mm.X - 3, mm.Y - 3, mm.W + 6, mm.H + 6, ctx.Theme.AccentColor);
    }

    // ---- Overlay de ayuda (H): atajos globales + los de la herramienta activa ----

    static readonly string[] HelpGlobal =
    [
        "Tab: herramienta    F1: salir",
        "Z: zoom 1x/2/4    V: minimapa    G: grilla",
        "flechas: camara (Shift = rapido)",
        "Ctrl+Z / Ctrl+Y: deshacer / rehacer",
        "ESC: cancelar (pegado/seleccion/eleccion)",
    ];

    static string[] HelpFor(Tool t) => t switch
    {
        Tool.Tiles =>
        [
            "click/arrastrar: pintar (1 deshacer)",
            "click der: gotero    F: flood fill",
            "Shift+arrastrar: seleccion",
            "Ctrl+C/X/V: copiar/cortar/pegar (stamp)",
            "C: colision    T: tinte    Space: paleta",
        ],
        Tool.Objetos =>
        [
            "click: colocar    arrastrar: mover",
            "click der / X / Supr: borrar",
            "rueda sobre prop: escala (0.25x-3x)",
            "T: tinte    B: bloquea si/no",
            "P: existe si tal flag (Shift+P atras)",
            "I: bloqueo invisible    Space: paleta",
        ],
        Tool.Eventos =>
        [
            "click: elegir    arrastrar: mover",
            "N: nuevo NPC    S: sprite    R: rutina",
            "D: dialogo    P: existe si tal flag",
            "X / Supr: borrar",
            "Enter: reproducir cutscene (scrubber)",
        ],
        Tool.Warps =>
        [
            "click vacio: crear    click warp: elegir",
            "arrastrar: mover origen",
            "M: mapa destino    T: transicion",
            "D: destino en el cursor    X: borrar",
        ],
        Tool.Dialogo =>
        [
            "click izq: elegir dialogo    click der: elegir nodo",
            "rueda: scroll (cada mitad la suya)",
            "Enter / T: editar texto del nodo",
            "S: editar speaker",
            "tipeando: Enter guarda, ESC cancela",
        ],
        Tool.Historia =>
        [
            "click izq: capitulo    click der: escena",
            "flechas izq/der: capitulo",
            "flechas arriba/abajo: escena",
            "S: confirmar juego/libro reconciliados",
            "E: exportar manuscrito Markdown + DOCX",
            "la escritura y adaptacion profunda se hace via IA/MCP",
        ],
        Tool.Flags => ["click flag: alternar (solo runtime)", "N: crear flag nueva", "click mapa: telepuerto del jugador"],
        _ => ["Bitacora [vos]/[ia] de la sesion", "Ctrl+Z la deshace en este orden"],
    };

    void DrawHelp(EditorContext ctx, int w, int h)
    {
        var tools = HelpFor(tool);
        var lines = HelpGlobal.Length + tools.Length + 2;
        var bh = 50 + lines * UiRow;
        var bw = Math.Min(640, w - 40);
        var bx = (w - bw) / 2;
        var by = Math.Max(BarH + 8, (h - bh) / 2);
        UiPanel(bx, by, bw, bh);
        UiText($"ATAJOS - {ToolName(tool)}   (H cierra)", bx + 16, by + 12, ctx.Theme.AccentColor);
        var yy = by + 16 + UiRow + 6;
        foreach (var line in tools) { UiText(line, bx + 16, yy, ctx.Theme.TextColor); yy += UiRow; }
        yy += 10;
        foreach (var line in HelpGlobal) { UiText(line, bx + 16, yy, UiGray); yy += UiRow; }
    }

    static string ToolName(Tool t) => t switch
    {
        Tool.Tiles => "TILES", Tool.Objetos => "OBJETOS", Tool.Eventos => "EVENTOS",
        Tool.Warps => "WARPS", Tool.Dialogo => "DIALOGO", Tool.Historia => "HISTORIA", Tool.Flags => "FLAGS", _ => "LOG",
    };

    static string Short(string id) => id.Replace("event.", "").Replace("sprite.", "").Replace("map.", "").Replace("dialogue.", "");

    // Nombre ASCII de la orientacion del pincel para la UI del editor (fuente de consola, sin acentos).
    static string OrientName(int flags) => flags == 0 ? "normal" : $"rot{(flags & 3) * 90}{((flags & 4) != 0 ? "+esp" : "")}";

    static int FloorDiv(int a, int b) => (int)Math.Floor((double)a / b);

    // ---- Herramienta DIALOGO: editar textos de nodos EN EL LUGAR. Dos paneles
    // (dialogos | nodos del elegido); Enter/T edita el texto del nodo, S el speaker, con
    // captura de teclado (GetCharPressed: acentos y enie entran de verdad). Guardar es la
    // misma Mutate validada de siempre, con undo y nota [vos]. La fuente de consola es
    // ASCII, asi que el PANEL translitera acentos solo para mostrar (el dato queda intacto:
    // la verdad se ve en el juego, que esta corriendo al lado). ----

    DialogueDef? CurrentDialogue(EditorContext ctx) =>
        ctx.Project.Dialogues.Count == 0 ? null : ctx.Project.Dialogues[Math.Clamp(dlgIndex, 0, ctx.Project.Dialogues.Count - 1)];

    const int DlgListW = 310;

    void UpdateDialogo(EditorContext ctx)
    {
        var dialogues = ctx.Project.Dialogues;
        if (dialogues.Count == 0) return;
        dlgIndex = Math.Clamp(dlgIndex, 0, dialogues.Count - 1);
        var dlg = dialogues[dlgIndex];
        nodeIndex = Math.Clamp(nodeIndex, 0, Math.Max(0, dlg.Nodes.Count - 1));

        var mx = Raylib.GetMouseX();
        var my = Raylib.GetMouseY();
        var wheel = (int)Raylib.GetMouseWheelMove();
        if (wheel != 0)
        {
            if (mx < DlgListW + 10) dlgScroll = Math.Clamp(dlgScroll - wheel, 0, Math.Max(0, dialogues.Count - 1));
            else nodeScroll = Math.Clamp(nodeScroll - wheel, 0, Math.Max(0, dlg.Nodes.Count - 1));
        }
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && my > BarH + 8)
        {
            var row = (my - BarH - 8 - UiRow - 6) / UiRow;
            if (mx < DlgListW + 10)
            {
                var i = dlgScroll + row;
                if (row >= 0 && i >= 0 && i < dialogues.Count && i != dlgIndex) { dlgIndex = i; nodeIndex = 0; nodeScroll = 0; ctx.Sfx?.Play("sfx.cursor"); }
            }
            else
            {
                // Cada nodo ocupa 3 filas en el panel derecho (header + 2 de texto).
                var i = nodeScroll + row / 3;
                if (row >= 0 && i >= 0 && i < dlg.Nodes.Count && i != nodeIndex) { nodeIndex = i; ctx.Sfx?.Play("sfx.cursor"); }
            }
        }

        if (dlg.Nodes.Count == 0) return;
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.T)) StartDialogEdit(ctx, "text", dlg.Nodes[nodeIndex].Text);
        if (Raylib.IsKeyPressed(KeyboardKey.S)) StartDialogEdit(ctx, "speaker", dlg.Nodes[nodeIndex].Speaker);
    }

    void StartDialogEdit(EditorContext ctx, string field, string current)
    {
        if (ctx.Session == null) { ctx.Message("Editor en solo lectura: los textos se editan con una sesion (run --project)."); return; }
        editField = field;
        editBuffer = current;
        ctx.Sfx?.Play("sfx.confirm");
    }

    /// <summary>Captura de teclado del campo en edicion: GetCharPressed trae los codepoints
    /// reales (acentos incluidos), Backspace borra (con repeticion), Enter guarda via la
    /// CommandSession (validado + undo + [vos]) y ESC cancela sin tocar nada.</summary>
    void UpdateTextCapture(EditorContext ctx)
    {
        for (var cp = Raylib.GetCharPressed(); cp > 0; cp = Raylib.GetCharPressed())
            if (cp >= 32) editBuffer += char.ConvertFromUtf32(cp);
        if ((Raylib.IsKeyPressed(KeyboardKey.Backspace) || Raylib.IsKeyPressedRepeat(KeyboardKey.Backspace)) && editBuffer.Length > 0)
            editBuffer = editBuffer[..^(char.IsLowSurrogate(editBuffer[^1]) ? 2 : 1)];
        if (Raylib.IsKeyPressed(KeyboardKey.Escape)) { editField = null; ctx.Sfx?.Play("sfx.cancel"); return; }
        if (!Raylib.IsKeyPressed(KeyboardKey.Enter)) return;

        var dlg = CurrentDialogue(ctx);
        if (dlg == null || dlg.Nodes.Count == 0 || ctx.Session == null) { editField = null; return; }
        var dlgId = dlg.Id;
        var nodeId = dlg.Nodes[Math.Clamp(nodeIndex, 0, dlg.Nodes.Count - 1)].Id;
        var field = editField;
        var value = editBuffer;
        editField = null;
        var result = ctx.Session.Mutate(p =>
        {
            var node = p.Dialogues.First(d => d.Id == dlgId).Nodes.First(n => n.Id == nodeId);
            if (field == "speaker") node.Speaker = value;
            else node.Text = value;
        });
        if (result.Ok) ctx.Sfx?.Play("sfx.confirm");
        ctx.Adopt(ctx.Session.Project,
            result.Ok ? UiStrings.FieldSaved(Short(dlgId), nodeId, field) : result.Error!.Message,
            result.Ok ? UiStrings.NoteEdit(field, Short(dlgId), nodeId) : null);
    }

    /// <summary>La fuente default de raylib es ASCII: transliteramos SOLO para mostrar en el
    /// panel (a la par, 1 a 1: el largo no cambia). El texto real conserva acentos y enie.</summary>
    static string Ascii(string s)
    {
        var chars = s.ToCharArray();
        for (var i = 0; i < chars.Length; i++)
            chars[i] = chars[i] switch
            {
                'á' => 'a', 'é' => 'e', 'í' => 'i', 'ó' => 'o', 'ú' => 'u', 'ü' => 'u', 'ñ' => 'n',
                'Á' => 'A', 'É' => 'E', 'Í' => 'I', 'Ó' => 'O', 'Ú' => 'U', 'Ü' => 'U', 'Ñ' => 'N',
                '¡' => '!', '¿' => '?', '—' => '-',
                > '\u007f' => '?',
                _ => chars[i],
            };
        return new string(chars);
    }

    /// <summary>Corta un texto para que entre en `width` px de UiText, con "..." si sobra.</summary>
    static string Fit(string s, int width)
    {
        if (UiMeasure(s) <= width) return s;
        while (s.Length > 1 && UiMeasure(s + "...") > width) s = s[..^1];
        return s + "...";
    }

    void DrawDialogoUi(EditorContext ctx, int w, int h)
    {
        var dialogues = ctx.Project.Dialogues;
        var top = BarH + 8;
        var panelH = h - top - 10;
        UiPanel(10, top, DlgListW, panelH);
        UiText($"DIALOGOS ({dialogues.Count})", 24, top + 8, ctx.Theme.AccentColor);
        var maxRows = Math.Max(1, (panelH - UiRow - 16) / UiRow);
        dlgScroll = Math.Clamp(dlgScroll, 0, Math.Max(0, dialogues.Count - maxRows));
        var y = top + UiRow + 6;
        for (var i = dlgScroll; i < dialogues.Count && i < dlgScroll + maxRows; i++)
        {
            UiText(Fit((i == dlgIndex ? "> " : "  ") + Short(dialogues[i].Id), DlgListW - 28), 24, y, i == dlgIndex ? UiGreen : ctx.Theme.TextColor);
            y += UiRow;
        }
        if (dialogues.Count == 0) { UiText("(no hay dialogos: crear via MCP)", 24, y, UiGray); return; }

        var dlg = dialogues[Math.Clamp(dlgIndex, 0, dialogues.Count - 1)];
        var rx = DlgListW + 20;
        var rw = w - rx - 10;
        UiPanel(rx, top, rw, panelH);
        UiText(Fit($"{Short(dlg.Id)}  ({dlg.Nodes.Count} nodos, inicio {dlg.StartNodeId})", rw - 28), rx + 14, top + 8, ctx.Theme.AccentColor);
        var nodeRows = Math.Max(1, (panelH - UiRow - 16) / UiRow / 3);
        nodeScroll = Math.Clamp(nodeScroll, 0, Math.Max(0, dlg.Nodes.Count - nodeRows));
        y = top + UiRow + 6;
        for (var i = nodeScroll; i < dlg.Nodes.Count && i < nodeScroll + nodeRows; i++)
        {
            var node = dlg.Nodes[i];
            var sel = i == nodeIndex;
            var flow = node.Choices.Count > 0 ? $"{node.Choices.Count} elecciones"
                : string.IsNullOrWhiteSpace(node.NextNodeId) ? "fin" : "-> " + node.NextNodeId;
            var fx = node.Effects.Count > 0 ? $"  [{node.Effects.Count} efectos]" : "";
            UiText(Fit($"{(sel ? ">" : " ")} {node.Id}  [{Ascii(node.Speaker)}]  ({flow}){fx}", rw - 28), rx + 14, y, sel ? UiGreen : ctx.Theme.TextColor);
            y += UiRow;
            var body = Ascii(node.Text);
            var line1 = Fit(body, rw - 48);
            UiText(line1, rx + 34, y, sel ? Color.RayWhite : UiGray);
            y += UiRow;
            var rest = line1.EndsWith("...") ? Fit(body[(line1.Length - 3)..], rw - 48) : "";
            if (rest != "") UiText(rest, rx + 34, y, sel ? Color.RayWhite : UiGray);
            y += UiRow;
        }

        // Pie: modo edicion (buffer con cursor parpadeante) o los atajos.
        var footY = top + panelH - UiRow - 10;
        if (editField != null)
        {
            var label = editField == "speaker" ? "SPEAKER" : "TEXTO";
            var caret = (int)(Raylib.GetTime() * 3) % 2 == 0 ? "|" : " ";
            Raylib.DrawRectangle(rx + 6, footY - 6, rw - 12, UiRow + 10, new Color(10, 30, 16, 250));
            Raylib.DrawRectangleLines(rx + 6, footY - 6, rw - 12, UiRow + 10, UiGreen);
            var shown = Ascii(editBuffer);
            while (shown.Length > 1 && UiMeasure($"{label}: {shown}{caret}") > rw - 32) shown = shown[1..]; // se ve la cola
            UiText($"{label}: {shown}{caret}", rx + 14, footY, UiGreen);
        }
        else
        {
            UiText("Enter/T: editar texto   S: speaker   rueda: scroll   (guardado = validado + undo)", rx + 14, footY, UiGray);
        }
    }

    // ---- Herramienta HISTORIA: el tablero humano del Libro Espejo. La IA puede leer y
    // escribir la prosa/escenas con story.*; aca el autor ve inmediatamente que lado cambio,
    // confirma una adaptacion consciente y genera el manuscrito editorial sin salir del juego. ----

    const int StoryListW = 310;

    void UpdateHistoria(EditorContext ctx)
    {
        var chapters = ctx.Project.StoryBook.Chapters;
        if (chapters.Count == 0)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.E)) ExportStory(ctx);
            return;
        }
        storyChapterIndex = Math.Clamp(storyChapterIndex, 0, chapters.Count - 1);
        var chapter = chapters[storyChapterIndex];
        storySceneIndex = Math.Clamp(storySceneIndex, 0, Math.Max(0, chapter.Scenes.Count - 1));

        var mx = Raylib.GetMouseX();
        var my = Raylib.GetMouseY();
        var top = BarH + 8;
        var chapterTop = top + UiRow * 3 + 12;
        var sceneTop = top + UiRow + 8;
        var wheel = (int)Raylib.GetMouseWheelMove();
        if (wheel != 0)
        {
            if (mx < StoryListW + 10) storyChapterScroll = Math.Clamp(storyChapterScroll - wheel, 0, Math.Max(0, chapters.Count - 1));
            else storySceneScroll = Math.Clamp(storySceneScroll - wheel, 0, Math.Max(0, chapter.Scenes.Count - 1));
        }
        if (Raylib.IsMouseButtonPressed(MouseButton.Left) && my > BarH)
        {
            if (mx < StoryListW + 10)
            {
                var row = (my - chapterTop) / UiRow;
                var i = storyChapterScroll + row;
                if (row >= 0 && i >= 0 && i < chapters.Count && i != storyChapterIndex)
                {
                    storyChapterIndex = i;
                    storySceneIndex = 0;
                    storySceneScroll = 0;
                    ctx.Sfx?.Play("sfx.cursor");
                }
            }
            else
            {
                var row = (my - sceneTop) / (UiRow * 2);
                var i = storySceneScroll + row;
                if (row >= 0 && i >= 0 && i < chapter.Scenes.Count && i != storySceneIndex)
                {
                    storySceneIndex = i;
                    ctx.Sfx?.Play("sfx.cursor");
                }
            }
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Right) && storyChapterIndex < chapters.Count - 1)
        {
            storyChapterIndex++;
            storySceneIndex = 0;
            storySceneScroll = 0;
            ctx.Sfx?.Play("sfx.cursor");
            chapter = chapters[storyChapterIndex];
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Left) && storyChapterIndex > 0)
        {
            storyChapterIndex--;
            storySceneIndex = 0;
            storySceneScroll = 0;
            ctx.Sfx?.Play("sfx.cursor");
            chapter = chapters[storyChapterIndex];
        }
        if (chapter.Scenes.Count > 0)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Down)) { storySceneIndex = Math.Min(chapter.Scenes.Count - 1, storySceneIndex + 1); ctx.Sfx?.Play("sfx.cursor"); }
            if (Raylib.IsKeyPressed(KeyboardKey.Up)) { storySceneIndex = Math.Max(0, storySceneIndex - 1); ctx.Sfx?.Play("sfx.cursor"); }
            if (Raylib.IsKeyPressed(KeyboardKey.S)) SyncStoryScene(ctx, chapter.Scenes[storySceneIndex]);
        }
        if (Raylib.IsKeyPressed(KeyboardKey.E)) ExportStory(ctx);
    }

    void SyncStoryScene(EditorContext ctx, StorySceneDef scene)
    {
        if (ctx.Session == null) { ctx.Message("Editor en solo lectura: no se puede sincronizar el Libro Espejo."); return; }
        var state = NarrativeTwin.State(ctx.Project, scene);
        if (!state.HasGameLinks) { ctx.Message($"{scene.Id}: falta enlazar contenido del juego via story.scene.set."); return; }
        if (state.MissingLinks.Count > 0) { ctx.Message($"{scene.Id}: links rotos: {string.Join(", ", state.MissingLinks)}."); return; }
        var id = scene.Id;
        var result = ctx.Session.Mutate(p => NarrativeTwin.Sync(p, NarrativeTwin.FindScene(p, id)!));
        if (result.Ok) ctx.Sfx?.Play("sfx.confirm");
        ctx.Adopt(ctx.Session.Project,
            result.Ok ? $"{Short(id)}: juego y libro marcados al dia." : result.Error!.Message,
            result.Ok ? $"reconcilia Libro Espejo {Short(id)}" : null);
    }

    void ExportStory(EditorContext ctx)
    {
        if (ctx.Session == null) { ctx.Message("Abrir con run --project para exportar el manuscrito."); return; }
        try
        {
            var report = StoryBookExporter.Export(ctx.Project, ctx.Session.ProjectRoot);
            ctx.Sfx?.Play("sfx.confirm");
            ctx.Message($"Libro exportado: {report.Words} palabras, MD + DOCX en build/book ({report.Warnings.Count} avisos).");
        }
        catch (Exception ex) { ctx.Sfx?.Play("sfx.cancel"); ctx.Message("No se pudo exportar el libro: " + ex.Message); }
    }

    void DrawHistoriaUi(EditorContext ctx, int w, int h)
    {
        var book = ctx.Project.StoryBook;
        var chapters = book.Chapters;
        var top = BarH + 8;
        var panelH = h - top - 10;
        UiPanel(10, top, StoryListW, panelH);
        UiText("LIBRO ESPEJO", 24, top + 8, ctx.Theme.AccentColor);
        UiText(Fit(Ascii(string.IsNullOrWhiteSpace(book.Title) ? "(sin titulo)" : book.Title), StoryListW - 28), 24, top + 8 + UiRow, Color.RayWhite);
        UiText(Fit($"{Ascii(book.Author)}  {NarrativeTwin.WordCount(book)} palabras", StoryListW - 28), 24, top + 8 + UiRow * 2, UiGray);
        var chapterTop = top + UiRow * 3 + 12;
        var maxChapters = Math.Max(1, (panelH - (chapterTop - top) - 10) / UiRow);
        storyChapterScroll = Math.Clamp(storyChapterScroll, 0, Math.Max(0, chapters.Count - maxChapters));
        var y = chapterTop;
        for (var i = storyChapterScroll; i < chapters.Count && i < storyChapterScroll + maxChapters; i++)
        {
            var ch = chapters[i];
            var words = ch.Scenes.Sum(scene => NarrativeTwin.WordCount(scene.Prose));
            UiText(Fit($"{(i == storyChapterIndex ? ">" : " ")} {i + 1}. {Ascii(ch.Title)} ({words})", StoryListW - 28), 24, y,
                i == storyChapterIndex ? UiGreen : ctx.Theme.TextColor);
            y += UiRow;
        }
        if (chapters.Count == 0)
        {
            UiText("Todavia no hay capitulos.", 24, chapterTop, UiGray);
            UiText("La IA los crea con story.chapter.set", 24, chapterTop + UiRow, ctx.Theme.TextColor);
            UiText("y enlaza juego/prosa con story.scene.set.", 24, chapterTop + UiRow * 2, ctx.Theme.TextColor);
        }

        var rx = StoryListW + 20;
        var rw = w - rx - 10;
        UiPanel(rx, top, rw, panelH);
        if (chapters.Count == 0)
        {
            UiText("UN JUEGO Y UN LIBRO, LA MISMA HISTORIA", rx + 14, top + 8, ctx.Theme.AccentColor);
            UiText("E: exportar borrador   H: ayuda", rx + 14, top + 8 + UiRow * 2, UiGray);
            return;
        }

        storyChapterIndex = Math.Clamp(storyChapterIndex, 0, chapters.Count - 1);
        var chapter = chapters[storyChapterIndex];
        storySceneIndex = Math.Clamp(storySceneIndex, 0, Math.Max(0, chapter.Scenes.Count - 1));
        UiText(Fit($"CAPITULO {storyChapterIndex + 1}: {Ascii(chapter.Title)}", rw - 28), rx + 14, top + 8, ctx.Theme.AccentColor);
        var sceneTop = top + UiRow + 8;
        var sceneRows = Math.Clamp((panelH / 2 - UiRow) / (UiRow * 2), 1, 6);
        storySceneScroll = Math.Clamp(storySceneScroll, 0, Math.Max(0, chapter.Scenes.Count - sceneRows));
        y = sceneTop;
        for (var i = storySceneScroll; i < chapter.Scenes.Count && i < storySceneScroll + sceneRows; i++)
        {
            var scene = chapter.Scenes[i];
            var (label, color) = StoryState(ctx.Project, scene, ctx.Theme.AccentColor);
            var selected = i == storySceneIndex;
            UiText(Fit($"{(selected ? ">" : " ")} {Ascii(scene.Title)}", rw - 190), rx + 14, y, selected ? UiGreen : ctx.Theme.TextColor);
            var lw = UiMeasure(label);
            UiText(label, rx + rw - lw - 14, y, color);
            y += UiRow;
            UiText(Fit($"   {scene.Status.ToUpperInvariant()} | {NarrativeTwin.WordCount(scene.Prose)} palabras | {scene.Links.Count} links", rw - 28), rx + 14, y, UiGray);
            y += UiRow;
        }
        if (chapter.Scenes.Count == 0) UiText("(capitulo sin escenas)", rx + 14, y, UiGray);

        var detailY = sceneTop + sceneRows * UiRow * 2 + 8;
        Raylib.DrawRectangle(rx + 8, detailY, rw - 16, 1, UiBorder);
        detailY += 10;
        if (chapter.Scenes.Count > 0)
        {
            var scene = chapter.Scenes[storySceneIndex];
            var (label, color) = StoryState(ctx.Project, scene, ctx.Theme.AccentColor);
            UiText(Fit($"{Ascii(scene.Title)}  [{label}]", rw - 28), rx + 14, detailY, color);
            detailY += UiRow;
            UiText(Fit($"POV: {Ascii(scene.Pov)}   LUGAR: {Ascii(scene.Location)}   TIEMPO: {Ascii(scene.Time)}", rw - 28), rx + 14, detailY, UiGray);
            detailY += UiRow;
            foreach (var line in WrapUi("SINOPSIS: " + scene.Synopsis, rw - 28, 2)) { UiText(line, rx + 14, detailY, ctx.Theme.TextColor); detailY += UiRow; }
            detailY += 4;
            var available = Math.Max(1, (top + panelH - UiRow * 2 - detailY) / UiRow);
            foreach (var line in WrapUi(scene.Prose, rw - 28, available)) { UiText(line, rx + 14, detailY, Color.RayWhite); detailY += UiRow; }
        }
        var warnings = NarrativeTwin.ExportWarnings(ctx.Project).Count;
        UiText($"S: AL DIA   E: MD+DOCX   AVISOS: {warnings}", rx + 14, top + panelH - UiRow - 10, UiGray);
    }

    static (string Label, Color Color) StoryState(GameProject p, StorySceneDef scene, Color accent)
    {
        var state = NarrativeTwin.State(p, scene);
        if (!state.HasGameLinks) return ("SIN JUEGO", new Color(255, 175, 80, 255));
        if (state.MissingLinks.Count > 0) return ("LINK ROTO", new Color(255, 90, 90, 255));
        if (string.IsNullOrEmpty(scene.SyncedGameHash) || string.IsNullOrEmpty(scene.SyncedProseHash)) return ("PENDIENTE", new Color(255, 210, 90, 255));
        if (state.InSync) return (UiStrings.SyncInSync, UiGreen);
        if (state.GameChanged && state.BookChanged) return (UiStrings.SyncBothChanged, new Color(255, 130, 180, 255));
        if (state.GameChanged) return (UiStrings.SyncGameChanged, accent);
        return (UiStrings.SyncBookChanged, new Color(190, 145, 255, 255));
    }

    static List<string> WrapUi(string text, int width, int maxLines)
    {
        var words = Ascii(text ?? "").Replace("\r", " ").Replace("\n", " ")
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var lines = new List<string>();
        var current = "";
        foreach (var word in words)
        {
            var candidate = current.Length == 0 ? word : current + " " + word;
            if (UiMeasure(candidate) <= width) { current = candidate; continue; }
            if (current.Length > 0) lines.Add(current);
            current = word;
            if (lines.Count >= maxLines) break;
        }
        if (lines.Count < maxLines && current.Length > 0) lines.Add(Fit(current, width));
        if (lines.Count == 0) lines.Add("(sin prosa)");
        if (lines.Count > maxLines) lines = lines.Take(maxLines).ToList();
        return lines;
    }

    // ---- HUD del scrubber de cutscenes: el runtime lo dibuja como overlay de
    // Present mientras el scrubber esta activo. Misma capa de UI nativa que el editor:
    // fuente de consola nitida, panel limpio; el juego sigue retro debajo. ----

    /// <summary>Foto por frame del scrubber para el HUD: cola del evento, cuantos comandos ya
    /// corrieron (Done), cual resaltar (el en curso, o el proximo si esta en pausa) y el estado.</summary>
    public readonly record struct ScrubView(string EventId, int Page, int PageCount, IReadOnlyList<string> Commands, int Done, int Highlight, string Status);

    public static void DrawScrubUi(ScrubView v)
    {
        var w = Raylib.GetScreenWidth();
        var bw = Math.Min(640, w - 24);
        var bx = w - bw - 12;
        const int by = 12;
        const int maxLines = 10; // ventana deslizante alrededor del comando resaltado
        var total = v.Commands.Count;
        var first = Math.Clamp(v.Highlight - maxLines / 2, 0, Math.Max(0, total - maxLines));
        var shown = Math.Min(maxLines, total - first);
        var extra = total > maxLines ? 1 : 0;
        UiPanel(bx, by, bw, 20 + UiRow * (shown + extra + 3));
        UiText($"SCRUBBER  {v.EventId}  (pag {v.Page}/{v.PageCount})", bx + 14, by + 10, UiGreen);
        var yy = by + 10 + UiRow + 4;
        for (var i = first; i < first + shown; i++)
        {
            var color = i == v.Highlight ? UiGreen : i < v.Done ? UiGray : Color.RayWhite;
            var line = $"{(i == v.Highlight ? ">" : " ")}{i + 1,3} {v.Commands[i]}";
            while (line.Length > 8 && UiMeasure(line) > bw - 28) line = line[..^4] + "...";
            UiText(line, bx + 14, yy, color);
            yy += UiRow;
        }
        if (extra > 0) { UiText($"    ({total} comandos en total)", bx + 14, yy, UiGray); yy += UiRow; }
        yy += 6;
        UiText(v.Status, bx + 14, yy, Color.RayWhite);
        UiText("Enter: paso   Space: todo   R: reinicia   Esc: salir", bx + 14, yy + UiRow, UiGray);
    }
}
