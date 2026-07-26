using System.Numerics;
using System.Text;
using Raylib_cs;

namespace Seto90;

/// <summary>
/// Runtime visual del motor: maquina de estados (combate > dialogo > cola de comandos > mundo)
/// dibujada sobre un lienzo virtual retro con camara y movimiento interpolado.
///
/// Nota de diseno: en los 90 esta clase era "el juego entero" — render, input, eventos y combate
/// entrelazados en el mismo loop de ensamblador. Conservamos lo bueno (un solo loop determinista,
/// estados con prioridad clara) y separamos lo que ellos no podian: lienzo (VirtualScreen),
/// camara (GameCamera) y fisica de grilla (GridMover) son piezas propias y testeables.
/// </summary>
public sealed partial class VisualRuntime
{
    GameProject project;
    MapDef map;
    readonly SaveSystem saveSystem;
    readonly Dictionary<string, bool> flags = [];
    readonly List<string> inventory = [];
    TilesetDef tileset;
    readonly Queue<EventCommand> commandQueue = [];
    // Scrubber de cutscenes: reproduce la cola de un evento paso a paso sin tener
    // que rejugar hasta ahi. Un snapshot congela el mundo al entrar (flags, inventario, party,
    // posiciones); salir o reiniciar lo restauran exacto — probar una cutscene no deja marca.
    bool scrubActive;
    bool scrubAuto;      // Space: reproducir todo (la cola corre sola hasta el final)
    bool scrubStep;      // Enter: un paso armado — ContinueCommandQueue ejecuta UN comando y re-pausa
    string scrubEventId = "";
    int scrubPage;
    int scrubPageCount;
    List<EventCommand> scrubCommands = [];
    SaveGame? scrubSave;
    Facing scrubFacing;
    readonly Dictionary<string, (int X, int Y, Facing F)> scrubNpcs = [];
    string? pendingScrubEventId; // captura --scrub: abre el scrubber con la ventana ya creada
    int pendingScrubSteps;
    int pendingScrubPageIndex = -1;
    // Accion bloqueante de cutscene en curso: la cola espera a que termine (espera o caminata dirigida).
    float cutsceneWait;
    GridMover? cutsceneMover;
    string cutsceneMoverEventId = "";
    Queue<string> cutsceneSteps = [];
    // Pan de camara de cutscene: el foco desliza (suavizado) del punto actual al blanco.
    bool camPanning;
    bool camOverride;
    bool camReturning;
    float camPanElapsed;
    float camPanDuration = 1f;
    float camFromX, camFromY, camToX, camToY;
    readonly Dictionary<string, int> routineDirection = [];
    readonly Dictionary<string, GridMover> npcMovers = [];
    readonly GridMover player = new();
    // Cadena de followers de party: un mover por miembro no-lider.
    // Cada uno sigue el camino YA validado del lider, un tile por detras del anterior; no
    // tienen colision propia (jamas chocan ni se traban). lastLeader detecta el paso nuevo.
    readonly List<GridMover> followerMovers = [];
    int lastLeaderX, lastLeaderY;
    readonly GameCamera camera = new();
    readonly PixelFont font = PixelFont.Embedded();
    UiTheme theme;
    readonly string? projectRoot;
    readonly CommandSession? session;
    readonly EditorMode editor = new();
    int editorCamX, editorCamY; // esquina (px) de la camara libre del editor: recorre todo el mapa
    HotReload? hotReload;
    VirtualScreen? screen;
    SpriteBank? spriteBank;
    TileBank? tileBank;
    float routineTimer;
    float time;
    string message = "";
    float messageTimer;
    BattleEngine? activeBattle;
    DialogueSession? activeDialogue;
    ShopSession? activeShop;
    int money;
    PartyState party;
    int enemyFlashIndex = -1;
    bool paused;
    int pauseSection;
    int pauseRow;
    bool pauseInPanel;
    bool quitRequested;
    // Claves internas de las secciones (los switch dependen de ellas). Lo que se MUESTRA sale de
    // UiStrings.PauseLabels, en el idioma del proyecto: nunca mezclar clave con etiqueta.
    static readonly string[] PauseSections = ["ITEMS", "ESTADO", "EQUIPO", "OPCIONES", "GUARDAR", "CARGAR", "SALIR"];
    readonly PlayerSettings settings = PlayerSettings.Load();
    MusicPlayer? music;
    ChiptunePlayer? legacyMusic; // fallback si el streaming fallara en algun entorno
    SfxPlayer? sfx;
    int blipCounter;
    readonly TransitionSystem transition = new();
    readonly ScreenShake shake = new();
    readonly RollingNumber rollingHp = new();
    float enemyFlashTimer;
    // Presentacion de jefe (BattleDef.Boss): placa de nombre que se desvanece al entrar; el
    // aura pulsante detras del enemigo vive mientras dura el combate.
    float bossIntroTimer;
    // Numero flotante de dano (estilo JRPG clasico): sube y se desvanece sobre el enemigo golpeado.
    float dmgPopTimer;
    int dmgPopValue;
    int dmgPopX;
    const float DmgPopSeconds = 0.9f;
    TitleScreen? title;
    Texture2D? titleBg;
    bool titleBgLoaded;
    EngineSplash? splash;
    // Emotes activos: "player" o id de evento -> (icono, tiempo restante). El globo de
    // El globo es senal instantanea, sin idioma, sin frenar la escena.
    readonly Dictionary<string, (string Icon, float Timer)> emotes = [];
    // VFX declarativos (VfxDef): los impactos activos del combate (lista: un paso puede traer
    // el golpe del jugador Y el contraataque enemigo — AdvanceUntilPlayer corre los turnos
    // enemigos en el mismo paso) y los del mundo, anclados por clave como los emotes.
    readonly List<(VfxDef Def, float T, int X, int Y)> battleVfx = [];
    readonly Dictionary<string, (VfxDef Def, float T)> worldVfx = [];
    // Texto flotante del mundo (el "+6 HP" tiene que verse en alguna parte): el
    // "+1 Cafe" clasico que sube y parpadea sobre su ancla ("player" o id de evento, como
    // los emotes). Sin alpha: parpadea al morir (la leccion del render texture).
    readonly List<(string Text, Color Color, float T, string Anchor)> worldFloats = [];
    const float FloatSeconds = 1.4f;
    // Pops de combate: "+N" verde sobre el panel del curado, "-N" rojo sobre el del golpeado.
    readonly List<(string Text, Color Color, float T, int X, int Y)> battlePops = [];
    static readonly Color FloatGold = new(255, 224, 130, 255);
    static readonly Color FloatGreen = new(130, 240, 160, 255);
    static readonly Color FloatRed = new(255, 120, 120, 255);
    // Clima del mundo: el mapa lo autorea (map.WeatherVfxId) y el comando
    // SetWeather lo pisa en runtime para el mapa actual (null = usar el autorado; "" =
    // despejado por cutscene). Cambiar de mapa vuelve al clima autorado del destino.
    string? weatherOverride;
    // Tiempo del mundo por franjas: narrativo, no de reloj. Las paginas
    // condicionan con los ids reservados time.dia / time.franja.
    int day = 1;
    string dayPhase = "manana";
    float dayCardTimer;
    string dayCardText = "";
    // Ceremonia del item clave (ShowItemGet): el mundo se detiene alrededor del objeto,
    // el gesto clasico de entrega de un objeto de trama. Enter la cierra y sigue la cola.
    ItemDef? itemGetItem;
    float itemGetTime;
    int itemGetCount = 1;
    bool forceTitle;
    bool forceSplash;
    bool gameOver;
    CrtFilter? crt;
    /// <summary>Las capturas salen con el vidrio de tubo (--crt): vale para la final y para las del guion.</summary>
    bool captureCrt;

    const int WindowScale = 3; // tamano inicial de ventana; el usuario puede redimensionar libremente

    public VisualRuntime(GameProject project, string? projectRoot = null, CommandSession? session = null)
    {
        this.project = project;
        this.projectRoot = projectRoot;
        this.session = session;
        UiStrings.Use(project.Render.Language); // la UI del motor habla el idioma del proyecto
        theme = UiTheme.Resolve(project);
        saveSystem = new SaveSystem(project.Id);
        money = Math.Max(0, project.StartMoney);
        party = PartyState.Create(project);
        foreach (var variable in project.Variables.Where(v => v.Kind == VariableKind.Flag)) flags[variable.Id] = variable.Default.Equals("true", StringComparison.OrdinalIgnoreCase);
        map = project.Maps.First(x => x.Id == project.StartMapId);
        tileset = project.Tilesets.First(x => x.Id == map.TilesetId);
        TeleportPlayerToStart();
        foreach (var ev in project.Events)
        {
            routineDirection[ev.Id] = 1;
            var mover = new GridMover { SecondsPerTile = 0.3f };
            mover.Teleport(ev.X, ev.Y);
            npcMovers[ev.Id] = mover;
        }
        RebuildFollowers();
        SetMessage(UiStrings.ControlsHint, 5f);
    }

    string? pendingDebugEventId;
    int pendingDebugEventPageIndex = -1;
    string? pendingStartMapId;
    int pendingStartX = 1;
    int pendingStartY = 1;

    /// <summary>Dispara un evento por id al arrancar (para capturas de autoria: ver dialogo/combate sin input).
    /// Se ejecuta recien con la ventana creada, asi las transiciones (swirl) tambien son capturables.</summary>
    public void DebugStartEvent(string eventId, int pageIndex = -1)
    {
        pendingDebugEventId = eventId;
        pendingDebugEventPageIndex = pageIndex;
    }

    /// <summary>Arranca en otro mapa/posicion (para capturas de autoria: ver cualquier mapa sin caminar hasta ahi).</summary>
    public void DebugStartAt(string mapId, int x, int y) { pendingStartMapId = mapId; pendingStartX = x; pendingStartY = y; }

    /// <summary>Muestra la pantalla de titulo aunque se corra en modo oculto (para capturarla).</summary>
    public void ForceTitle() => forceTitle = true;

    /// <summary>Muestra la placa "MADE WITH 90s ENGINE" (para capturarla en modo oculto).</summary>
    public void ForceSplash() => forceSplash = true;

    bool forceEditor;
    int forceEditorZoom = 1;
    int forceEditorTool;
    bool forcePause;
    int forcePauseSection = 1; // ESTADO: la captura mas informativa por default
    bool debugAutoAttack;

    /// <summary>Al entrar al primer combate, ataca solo al primer blanco una vez (para capturas:
    /// ver el numero flotante de dano, el flash y el log sin input).</summary>
    public void DebugAutoAttack() => debugAutoAttack = true;

    /// <summary>Abre el scrubber de cutscenes sobre un evento al arrancar y ejecuta N comandos
    /// (para capturas: ver el HUD con la cola pausada en el punto exacto, sin input).</summary>
    public void DebugScrub(string eventId, int steps = 0, int pageIndex = -1)
    {
        pendingScrubEventId = eventId;
        pendingScrubSteps = Math.Max(0, steps);
        pendingScrubPageIndex = pageIndex;
    }

    /// <summary>Abre el modo editor al arrancar (para capturar la UI del editor sin input).
    /// zoomDiv 2 o 4 lo abre con el zoom out del mapa; tool = herramienta (0=TILES..5=LOG).</summary>
    public void ForceEditor(int zoomDiv = 1, int tool = 0) { forceEditor = true; forceEditorZoom = zoomDiv; forceEditorTool = tool; }

    /// <summary>Abre el menu de pausa al arrancar (para capturarlo sin input), opcionalmente en una seccion.</summary>
    public void ForcePause(int section = 1) { forcePause = true; forcePauseSection = Math.Clamp(section, 0, PauseSections.Length - 1); }

    public void Run(int? maxFrames = null, string? screenshotPath = null, bool hidden = false, bool crtCapture = false)
    {
        captureCrt = crtCapture; // tambien lo usan las capturas de un guion de playtest
        var flags = ConfigFlags.VSyncHint | ConfigFlags.ResizableWindow;
        // En modo oculto raylib no debe escribir a stdout: el servidor MCP habla JSON-RPC por ahi.
        if (hidden) { flags |= ConfigFlags.HiddenWindow; Raylib.SetTraceLogLevel(TraceLogLevel.None); }
        Raylib.SetConfigFlags(flags);
        Raylib.InitWindow(project.Render.VirtualWidth * WindowScale, project.Render.VirtualHeight * WindowScale, project.Title);
        Raylib.SetWindowMinSize(project.Render.VirtualWidth, project.Render.VirtualHeight);
        Raylib.SetTargetFPS(60);
        Raylib.SetExitKey(KeyboardKey.Null); // Esc abre el menu de pausa; salir es una opcion del menu
        screen = new VirtualScreen(project.Render.VirtualWidth, project.Render.VirtualHeight);
        crt = CrtFilter.Create(project.Render.CrtFilter);
        spriteBank = new SpriteBank(project, projectRoot);
        tileBank = new TileBank(project, projectRoot);
        // Con placa de arranque la musica del mapa espera: el boot suena sobre silencio,
        // como en las consolas de verdad, y el tema entra recien al llegar al titulo.
        var showSplash = (!hidden || forceSplash) && pendingDebugEventId == null;
        music = MusicPlayer.TryStart(project, showSplash ? "" : map.SongId);
        if (music == null) legacyMusic = ChiptunePlayer.TryStart(project, showSplash ? "" : map.SongId);
        sfx = SfxPlayer.TryCreate(project);
        music?.SetVolume(settings.MusicVolume);
        sfx?.SetVolume(settings.SfxVolume);
        if (projectRoot != null && !hidden) hotReload = new HotReload(projectRoot);
        try
        {
            var explicitDebugStart = pendingStartMapId != null;
            if (pendingStartMapId != null) ApplyTransfer(pendingStartMapId, pendingStartX, pendingStartY);
            // Una captura --event/--scrub debe VER la escena en su escenario real, no ejecutar
            // una cutscene de la torre encima del dormitorio inicial. --map explicito conserva
            // prioridad para los casos donde la IA quiere probar otro encuadre deliberadamente.
            var debugSceneId = pendingScrubEventId ?? pendingDebugEventId;
            if (!explicitDebugStart && debugSceneId != null) PrepareDebugScene(debugSceneId);
            var debugPageIndex = pendingScrubEventId != null ? pendingScrubPageIndex : pendingDebugEventPageIndex;
            if (debugSceneId != null && debugPageIndex >= 0 &&
                project.Events.FirstOrDefault(x => x.Id == debugSceneId) is { } debugEvent &&
                debugPageIndex < debugEvent.Pages.Count)
                ApplyDebugPageConditions(debugEvent.Pages[debugPageIndex]);
            if (forceEditor) { editor.Toggle(); editor.SetZoom(forceEditorZoom); editor.SetTool(forceEditorTool); messageTimer = 0; } // sin mensaje inicial: la captura muestra el editor limpio
            if (forcePause) { paused = true; pauseSection = forcePauseSection; }
            // El titulo aparece en modo interactivo; los smokes/capturas ocultas van directo al mundo
            // (salvo ForceTitle), y --event lo saltea porque quiere ver el evento.
            if ((!hidden || forceTitle) && pendingDebugEventId == null)
            {
                title = new TitleScreen(project.Title, saveSystem.MostRecentSlot() >= 0);
            }
            // La placa del motor abre toda sesion interactiva (salteable con Enter); en modo
            // oculto solo aparece si la captura la pide explicitamente.
            if (showSplash)
            {
                splash = new EngineSplash();
                sfx?.Play("sfx.boot");
            }
            if (pendingDebugEventId != null)
            {
                Draw(); // un frame del mundo para que el snapshot del swirl tenga contenido
                var ev = project.Events.FirstOrDefault(x => x.Id == pendingDebugEventId);
                if (ev != null) StartEvent(ev, pendingDebugEventPageIndex);
            }
            if (pendingScrubEventId != null)
            {
                Draw(); // un frame para que el mundo exista antes de congelar el snapshot
                StartScrub(pendingScrubEventId, pendingScrubPageIndex);
            }
            var frames = 0;
            while (!Raylib.WindowShouldClose() && !quitRequested && (maxFrames is null || frames++ < maxFrames.Value))
            {
                Update(hidden ? 1f / 60f : Raylib.GetFrameTime());
                Draw();
            }
            if (screenshotPath != null)
            {
                screen.BeginVirtual();
                DrawVirtual();
                screen.EndVirtual();
                Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(screenshotPath))!);
                // Default: lienzo limpio 256x224 (la IA lee pixeles exactos). Con --crt: el vidrio a 3x.
                // Con editor: la VENTANA completa (lienzo escalado + UI del editor en resolucion nativa).
                if (editor.Visible) screen.ExportWindowPng(screenshotPath, () => editor.DrawUi(EditorCtx()));
                else if (scrubActive) screen.ExportWindowPng(screenshotPath, () => EditorMode.DrawScrubUi(ScrubHud())); // ventana completa: juego + HUD nitido
                else if (crtCapture && crt is { Ready: true }) { crt.Enabled = true; screen.ExportPresentedPng(screenshotPath, crt); }
                else screen.ExportPng(screenshotPath);
            }
        }
        finally
        {
            hotReload?.Dispose();
            if (titleBg is { } tb) Raylib.UnloadTexture(tb);
            spriteBank?.Dispose();
            tileBank?.Dispose();
            crt?.Dispose();
            screen?.Dispose();
            music?.Dispose();
            legacyMusic?.Dispose();
            sfx?.Dispose();
            if (Raylib.IsAudioDeviceReady()) Raylib.CloseAudioDevice();
            Raylib.CloseWindow();
        }
    }

    void Update(float dt)
    {
        time += dt;
        music?.Update();
        legacyMusic?.Update();
        transition.Update(dt);
        shake.Update(dt);
        if (enemyFlashTimer > 0) enemyFlashTimer -= dt;
        if (bossIntroTimer > 0) bossIntroTimer -= dt;
        if (dmgPopTimer > 0) dmgPopTimer -= dt;
        if (messageTimer > 0) messageTimer -= dt;
        foreach (var key in emotes.Keys.ToList())
        {
            var (icon, timer) = emotes[key];
            if (timer - dt <= 0) emotes.Remove(key);
            else emotes[key] = (icon, timer - dt);
        }
        for (var i = battleVfx.Count - 1; i >= 0; i--)
        {
            var bv = battleVfx[i];
            if (bv.T + dt >= bv.Def.DurationMs / 1000f) battleVfx.RemoveAt(i);
            else battleVfx[i] = bv with { T = bv.T + dt };
        }
        for (var i = worldFloats.Count - 1; i >= 0; i--)
        {
            var wf = worldFloats[i];
            if (wf.T + dt >= FloatSeconds) worldFloats.RemoveAt(i);
            else worldFloats[i] = wf with { T = wf.T + dt };
        }
        for (var i = battlePops.Count - 1; i >= 0; i--)
        {
            var bp = battlePops[i];
            if (bp.T + dt >= FloatSeconds) battlePops.RemoveAt(i);
            else battlePops[i] = bp with { T = bp.T + dt };
        }
        foreach (var key in worldVfx.Keys.ToList())
        {
            var (def, t) = worldVfx[key];
            if (t + dt >= def.DurationMs / 1000f) worldVfx.Remove(key);
            else worldVfx[key] = (def, t + dt);
        }
        if (dayCardTimer > 0) dayCardTimer -= dt;

        // El vidrio se alterna en cualquier estado: es presentacion, no logica de juego.
        if (crt is { Ready: true } && Raylib.IsKeyPressed(KeyboardKey.F2))
        {
            crt.Enabled = !crt.Enabled;
            SetMessage(crt.Enabled ? "Filtro CRT activado (F2 alterna)." : "Filtro CRT desactivado (F2 alterna).");
        }

        // El guion de playtest.run inyecta sus teclas sinteticas antes de que los estados lean input.
        if (scriptQueue != null) UpdateScriptDriver(dt);

        // Una transicion activa congela el juego: nada se mueve mientras la pantalla funde o gira.
        if (transition.Blocking) return;

        // La placa del motor bloquea todo lo demas: es lo primero que existe.
        if (splash != null)
        {
            splash.Update(dt);
            if (splash.Done)
            {
                splash = null;
                // Recien ahora entra el tema del mapa, que sono el titulo de toda la vida.
                if (!string.IsNullOrWhiteSpace(map.SongId)) { music?.Play(map.SongId); legacyMusic?.Play(map.SongId); }
            }
            return;
        }

        if (gameOver)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space))
            {
                transition.FadeToBlack(0.8f, () =>
                {
                    ResetWorld();
                    gameOver = false;
                    title = new TitleScreen(project.Title, saveSystem.MostRecentSlot() >= 0);
                });
            }
            return;
        }

        if (title != null)
        {
            title.Update(sfx);
            if (title.Confirmed)
            {
                var wantsContinue = title.WantsContinue;
                transition.FadeToBlack(0.8f, () =>
                {
                    if (wantsContinue) ApplyLoad(Math.Max(0, saveSystem.MostRecentSlot())); // el slot mas nuevo
                    title = null;
                });
            }
            return;
        }

        if (paused)
        {
            UpdatePause();
            return;
        }

        // Con el scrubber activo no hay guardado/carga rapidos: protegen el snapshot (guardar
        // un mundo a mitad de una cutscene de prueba seria un save mentiroso).
        if (!scrubActive && Raylib.IsKeyPressed(KeyboardKey.F5)) SaveSlot(0);
        if (!scrubActive && Raylib.IsKeyPressed(KeyboardKey.F9)) LoadSlot(0);

        // --scrub-steps de una captura auto-confirma SOLO los dialogos/ceremonias que debe
        // atravesar para llegar a un comando posterior. Si el contador termina justo en ese
        // dialogo, queda abierto: la captura muestra el beat pedido. Movimiento, pan y waits
        // siguen usando sus duraciones reales, por eso el resultado conserva el ritmo autorado.
        if (scrubActive && pendingScrubSteps > 0 && (activeDialogue != null || itemGetItem != null))
            synthConfirm = true;

        if (activeBattle != null)
        {
            UpdateBattle();
            return;
        }

        if (activeDialogue != null)
        {
            UpdateDialogue(dt);
            return;
        }

        if (activeShop != null)
        {
            UpdateShop();
            return;
        }

        if (itemGetItem != null)
        {
            UpdateItemGet(dt);
            return;
        }

        if (camPanning)
        {
            camPanElapsed += dt;
            if (camPanElapsed >= camPanDuration)
            {
                camPanning = false;
                if (camReturning) camOverride = false; // el foco vuelve a ser el jugador
                ContinueCommandQueue();
            }
            return;
        }

        if (cutsceneWait > 0 || cutsceneMover != null)
        {
            UpdateCutsceneAction(dt);
            return;
        }

        // Scrubber sin accion bloqueante en curso: la cola esta en pausa entre comandos (o
        // termino) y el input es del scrubber, no del mundo.
        if (scrubActive)
        {
            UpdateScrub();
            return;
        }

        if (commandQueue.Count > 0)
        {
            ContinueCommandQueue();
            return;
        }

        UpdateWorld(dt);
    }

    void UpdateWorld(float dt)
    {
        UpdateFollowers(dt); // la cadena de party sigue al lider en cualquier caminata libre

        // Si una cutscene dejo la camara lejos y la cola ya termino, vuelve sola al jugador
        // (un autor que olvida el pan de regreso no puede dejar la camara clavada).
        if (camOverride && !camPanning)
        {
            StartCameraPan("", 0.6f);
            return;
        }

        // Estado "mundo" = el unico momento seguro para adoptar contenido recargado.
        if (hotReload != null && hotReload.TryConsume(out var fresh, out var reloadError))
        {
            if (fresh != null)
            {
                // Con sesion en proceso, el watcher tambien ve NUESTRAS escrituras: ignorar el eco.
                if (session != null && session.Matches(fresh)) { }
                else
                {
                    session?.Adopt(fresh);
                    session?.Note(UiStrings.LogAi, UiStrings.ExternalChangeAdopted);
                    Reload(fresh, session != null ? "Cambio externo adoptado (Ctrl+Z lo deshace)." : null);
                }
            }
            else if (reloadError != null) SetMessage(reloadError, 5f);
        }

        // Con el editor abierto, ESC es "cancelar" del editor (deseleccionar, salir del pegado),
        // no el menu de pausa: F1 sigue siendo la salida del editor.
        if (!editor.Visible && Raylib.IsKeyPressed(KeyboardKey.Escape))
        {
            paused = true;
            pauseSection = 0;
            pauseInPanel = false;
            pauseNote = "";
            sfx?.Play("sfx.confirm");
            return;
        }

        if (Raylib.IsKeyPressed(KeyboardKey.F1) && !editor.CapturingText)
        {
            editor.Toggle();
            if (editor.Visible) { editorCamX = camera.X; editorCamY = camera.Y; } // arranca donde estabas
            SetMessage(editor.Visible ? "Editor: flechas mueven la camara por todo el mapa (Shift = rapido). Tab cambia herramienta, F1 sale." : "Editor cerrado.");
        }
        if (editor.Visible && screen != null)
        {
            // Camara LIBRE del editor: las flechas recorren todo el mapa sin mover al jugador,
            // asi se edita cualquier casilla aunque el jugador este lejos. Shift = rapido.
            var ts = project.Render.TileSize;
            // El pan escala con el zoom out (misma velocidad percibida en pantalla).
            var pan = editor.CapturingText ? 0 : (Raylib.IsKeyDown(KeyboardKey.LeftShift) || Raylib.IsKeyDown(KeyboardKey.RightShift) ? 8 : 3) * editor.ZoomDiv;
            if (Raylib.IsKeyDown(KeyboardKey.Right)) editorCamX += pan;
            if (Raylib.IsKeyDown(KeyboardKey.Left)) editorCamX -= pan;
            if (Raylib.IsKeyDown(KeyboardKey.Down)) editorCamY += pan;
            if (Raylib.IsKeyDown(KeyboardKey.Up)) editorCamY -= pan;
            camera.SetCorner(editorCamX, editorCamY, map.Width * ts, map.Height * ts, screen.Width * editor.ZoomDiv, screen.Height * editor.ZoomDiv, editor.ZoomDiv);
            editorCamX = camera.X; editorCamY = camera.Y; // adoptar el clamp de la camara
            editor.Update(EditorCtx());
            return; // el editor toma el control: no mover al jugador ni activar eventos con flechas/Enter
        }

        UpdateNpcRoutines(dt);

        // Warps y triggers se disparan recien al terminar el paso: pisar la casilla, no rozarla.
        // El warp tiene prioridad: una casilla con puerta ES la puerta.
        if (player.Update(dt) && !TryWarpAt(player.TileX, player.TileY)) ActivateTriggerAt(player.TileX, player.TileY);

        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            ActivateFacingEvent();
            return;
        }

        if (player.Moving) return;
        var dx = 0; var dy = 0;
        if (Raylib.IsKeyDown(KeyboardKey.Right)) dx = 1;
        else if (Raylib.IsKeyDown(KeyboardKey.Left)) dx = -1;
        else if (Raylib.IsKeyDown(KeyboardKey.Down)) dy = 1;
        else if (Raylib.IsKeyDown(KeyboardKey.Up)) dy = -1;
        if (dx != 0 || dy != 0) player.TryStep(dx, dy, (x, y) => CanOccupy(x, y));
    }

    void UpdateDialogue(float dt)
    {
        var session = activeDialogue!;
        var node = session.Current;
        // Blip cada 2 caracteres imprimibles: la cadencia de texto del genero.
        blipCounter += session.UpdateTypewriter(dt, theme.TextSpeedCps);
        if (blipCounter >= 2) { blipCounter = 0; sfx?.Play("sfx.text_blip"); }
        var confirm = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space) || synthConfirm;

        // Mientras tipea, Enter completa el texto; recien despues navega/avanza.
        if (!session.TextComplete)
        {
            if (confirm) session.FastForward();
            return;
        }

        // Parrafo largo partido en paginas: Enter pasa a la siguiente antes de tocar el
        // grafo (las elecciones y el avance de nodo esperan a la ultima pagina).
        if (session.HasMorePages)
        {
            if (confirm) { sfx?.Play("sfx.confirm"); session.NextPage(); }
            return;
        }

        if (node.Choices.Count > 0)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Down) || synthDown) { session.SelectedChoice = Math.Min(node.Choices.Count - 1, session.SelectedChoice + 1); sfx?.Play("sfx.cursor"); }
            if (Raylib.IsKeyPressed(KeyboardKey.Up) || synthUp) { session.SelectedChoice = Math.Max(0, session.SelectedChoice - 1); sfx?.Play("sfx.cursor"); }
            if (confirm) { sfx?.Play("sfx.confirm"); EnterDialogueNode(session, node.Choices[session.SelectedChoice].NextNodeId); }
        }
        else if (confirm)
        {
            sfx?.Play("sfx.confirm");
            if (!string.IsNullOrWhiteSpace(node.NextNodeId)) EnterDialogueNode(session, node.NextNodeId);
            else EndDialogue();
        }

        if (Raylib.IsKeyPressed(KeyboardKey.Backspace)) { sfx?.Play("sfx.cancel"); EndDialogue(); }
    }

    void UpdateNpcRoutines(float dt)
    {
        foreach (var mover in npcMovers.Values) mover.Update(dt);

        routineTimer += dt;
        if (routineTimer < 0.55f) return;
        routineTimer = 0;
        foreach (var ev in project.Events.Where(e => e.MapId == map.Id && e.Kind == EventKind.Npc && EventPresent(e)))
        {
            if (ev.RoutineId == "pace_horizontal") TryMoveEvent(ev, routineDirection[ev.Id], 0);
            else if (ev.RoutineId == "pace_vertical") TryMoveEvent(ev, 0, routineDirection[ev.Id]);
        }
    }

    void TryMoveEvent(EventDef ev, int dx, int dy)
    {
        var mover = npcMovers[ev.Id];
        if (mover.Moving) return;
        // La posicion runtime vive SOLO en el mover: ev.X/Y son datos autorados y no se tocan.
        // Con una CommandSession en proceso, tocarlos persistiria el paseo del NPC al guardar.
        if (!mover.TryStep(dx, dy, (x, y) => CanOccupy(x, y, ignoreEventId: ev.Id))) routineDirection[ev.Id] *= -1;
    }

    /// <summary>Casilla runtime de un evento: su mover si existe (los NPC pasean), o la autorada.</summary>
    (int X, int Y) EventTile(EventDef ev) => npcMovers.TryGetValue(ev.Id, out var m) ? (m.TileX, m.TileY) : (ev.X, ev.Y);

    bool CanOccupy(int x, int y, string? ignoreEventId = null)
    {
        if (x < 0 || y < 0 || x >= map.Width || y >= map.Height) return false;
        var tileId = map.Tiles[y * map.Width + x];
        var tile = tileset.Tiles.FirstOrDefault(t => t.Id == tileId);
        if (tile?.Solid == true) return false;
        // Bloquean los NPCs y los objetos marcados solidos (un arbol/casa; los decorativos no).
        if (project.Events.Any(e => e.Id != ignoreEventId && e.MapId == map.Id && EventPresent(e) && EventTile(e) == (x, y) && (e.Kind == EventKind.Npc || (e.Kind == EventKind.Object && e.Solid)))) return false;
        if (ignoreEventId != null && player.TileX == x && player.TileY == y) return false; // los NPCs no pisan al jugador
        return true;
    }

    /// <summary>Activa el evento de la casilla que el jugador mira (comportamiento clasico de JRPG).</summary>
    void ActivateFacingEvent()
    {
        var (dx, dy) = player.Facing switch
        {
            Facing.Up => (0, -1),
            Facing.Down => (0, 1),
            Facing.Left => (-1, 0),
            _ => (1, 0)
        };
        var tx = player.TileX + dx;
        var ty = player.TileY + dy;
        // Solo cuentan los eventos con comandos ACTIVOS (segun flags/tiempo): en una casilla puede
        // convivir un prop decorativo (room_cama, sin comandos) con el evento interactivo (dormir),
        // y el decorativo no debe ganarle la interaccion ni disparar "sin comandos activos". Si no
        // hay nada interactivo mirando/al lado, Enter no hace nada (los props no se "examinan").
        bool Active(EventDef e) { var p = SelectActivePage(e); return p != null && p.Commands.Count > 0; }
        var ev = project.Events.FirstOrDefault(x => x.MapId == map.Id && x.Kind != EventKind.Trigger && EventPresent(x) && Active(x) && EventTile(x) == (tx, ty))
                 ?? project.Events.FirstOrDefault(x => x.MapId == map.Id && x.Kind != EventKind.Trigger && EventPresent(x) && Active(x) && Math.Abs(EventTile(x).X - player.TileX) + Math.Abs(EventTile(x).Y - player.TileY) <= 1);
        if (ev != null) StartEvent(ev);
    }

    void ActivateTriggerAt(int x, int y)
    {
        var trigger = project.Events.FirstOrDefault(e => e.MapId == map.Id && e.Kind == EventKind.Trigger && EventPresent(e) && e.X == x && e.Y == y);
        if (trigger != null) StartEvent(trigger);
    }

    void StartEvent(EventDef ev, int pageIndex = -1)
    {
        var page = pageIndex >= 0 && pageIndex < ev.Pages.Count ? ev.Pages[pageIndex] : SelectActivePage(ev);
        if (page == null || page.Commands.Count == 0)
        {
            SetMessage($"{ev.Name}: sin comandos activos.");
            return;
        }
        commandQueue.Clear();
        foreach (var command in page.Commands) commandQueue.Enqueue(command);
        ContinueCommandQueue();
    }

    EventPage? SelectActivePage(EventDef ev)
    {
        for (var i = ev.Pages.Count - 1; i >= 0; i--)
        {
            var page = ev.Pages[i];
            if (page.Conditions.All(Matches)) return page;
        }
        // Sin pagina activa el evento NO esta presente (la semantica clasica de paginas por
        // condicion): no se dibuja, no colisiona, no se activa. Una pagina sin condiciones
        // matchea siempre, asi que todo el contenido previo sigue visible igual que antes.
        return null;
    }

    /// <summary>Un evento existe en el mundo solo si alguna de sus paginas matchea las flags
    /// actuales. Asi "Lucia despierta" y "Lucia dormida" son dos eventos que se turnan.</summary>
    bool EventPresent(EventDef ev) => SelectActivePage(ev) != null;

    bool Matches(ConditionDef condition)
    {
        // Ids reservados del sistema de horario: comparan contra el tiempo del mundo.
        if (condition.VariableId == "time.dia") return day.ToString() == condition.EqualsValue.Trim();
        if (condition.VariableId == "time.franja") return dayPhase.Equals(condition.EqualsValue.Trim(), StringComparison.OrdinalIgnoreCase);
        var actual = flags.TryGetValue(condition.VariableId, out var value) && value;
        var expected = condition.EqualsValue.Equals("true", StringComparison.OrdinalIgnoreCase);
        return actual == expected;
    }

    /// <summary>Texto de la placa de dia ("DIA 2 — MAÑANA"), la cortina narrativa del tiempo.</summary>
    string DayCardText()
    {
        return UiStrings.DayPlate(day, UiStrings.PhaseLabel(dayPhase)); // guion simple: la fuente 8x8 no tiene em-dash
    }

    void ContinueCommandQueue()
    {
        while (commandQueue.Count > 0)
        {
            // Scrubber en pausa: la cola espera el paso (Enter) o el modo auto (Space). El gate
            // vive aca porque TODOS los caminos que reanudan la cola (fin de dialogo, combate,
            // pan, wait, ceremonia) pasan por este metodo: pausan solos entre comandos.
            if (scrubActive && !scrubAuto && !scrubStep) return;
            scrubStep = false;
            if (ExecuteCommand(commandQueue.Dequeue())) return;
        }
    }

    // ---- Scrubber de cutscenes: LA herramienta de la fase de pulido — reproducir
    // la cola de un evento comando a comando sin rejugar hasta ahi. Se lanza desde el editor
    // (EVENTOS + Enter) o desde una captura (--scrub); al salir, el mundo vuelve al snapshot. ----

    /// <summary>Arranca el scrubber sobre un evento: congela el snapshot del mundo y deja la
    /// cola PAUSADA antes del primer comando. La pagina activa se elige con las flags ACTUALES
    /// (la herramienta FLAGS del editor permite probar cada variante del evento).</summary>
    void StartScrub(string eventId, int pageIndex = -1)
    {
        var ev = project.Events.FirstOrDefault(x => x.Id == eventId);
        if (ev == null) { SetMessage($"Scrubber: {eventId} no existe."); return; }
        var page = pageIndex >= 0 && pageIndex < ev.Pages.Count ? ev.Pages[pageIndex] : SelectActivePage(ev);
        if (page == null) { SetMessage($"Scrubber: {eventId} no tiene pagina activa con las flags actuales (cambialas en FLAGS)."); return; }
        if (page.Commands.Count == 0) { SetMessage($"Scrubber: la pagina activa de {eventId} no tiene comandos."); return; }
        scrubEventId = eventId;
        scrubPage = ev.Pages.IndexOf(page);
        scrubPageCount = ev.Pages.Count;
        scrubCommands = [.. page.Commands];
        scrubSave = BuildSaveGame();
        scrubFacing = player.Facing;
        scrubNpcs.Clear();
        foreach (var (id, m) in npcMovers) scrubNpcs[id] = (m.TileX, m.TileY, m.Facing);
        scrubActive = true;
        scrubAuto = false;
        scrubStep = false;
        if (editor.Visible) editor.Toggle();
        commandQueue.Clear();
        foreach (var c in page.Commands) commandQueue.Enqueue(c);
        SetMessage("Scrubber: Enter avanza un comando, Space reproduce todo, R reinicia, Esc sale.", 5f);
    }

    /// <summary>Input del scrubber cuando la cola esta en pausa entre comandos o ya termino.
    /// Durante una accion bloqueante (dialogo, combate, wait) manda el input normal de esa
    /// accion; al terminar, la cola re-pausa sola y el control vuelve aca.</summary>
    void UpdateScrub()
    {
        // Pasos automaticos de una captura (--scrub-steps): uno por frame, deterministas.
        if (pendingScrubSteps > 0 && commandQueue.Count > 0)
        {
            pendingScrubSteps--;
            scrubStep = true;
            ContinueCommandQueue();
            return;
        }
        // En modo auto la cola corre sola entre acciones bloqueantes hasta vaciarse.
        if (scrubAuto)
        {
            if (commandQueue.Count > 0) { ContinueCommandQueue(); return; }
            scrubAuto = false;
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.F1)) { EndScrub(); return; }
        if (Raylib.IsKeyPressed(KeyboardKey.R)) { RestartScrub(); return; }
        if (commandQueue.Count == 0) return; // FIN: solo quedan R (reiniciar) y Esc (salir)
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Right))
        {
            sfx?.Play("sfx.cursor");
            scrubStep = true;
            ContinueCommandQueue();
        }
        else if (Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            sfx?.Play("sfx.confirm");
            scrubAuto = true;
            ContinueCommandQueue();
        }
    }

    /// <summary>Vuelve el mundo al snapshot y re-arma la MISMA cola capturada al entrar
    /// (replay determinista aunque un comando haya cambiado flags que elegirian otra pagina).</summary>
    void RestartScrub()
    {
        RestoreScrubSnapshot();
        scrubAuto = false;
        scrubStep = false;
        commandQueue.Clear();
        foreach (var c in scrubCommands) commandQueue.Enqueue(c);
        sfx?.Play("sfx.cancel");
        SetMessage("Scrubber reiniciado: el mundo volvio al snapshot.", 3f);
    }

    /// <summary>Sale del scrubber restaurando el mundo y reabre el editor donde estabas.</summary>
    void EndScrub()
    {
        RestoreScrubSnapshot();
        ClearScrubState();
        editor.Open();
        editorCamX = camera.X;
        editorCamY = camera.Y;
        SetMessage("Scrubber cerrado: el mundo volvio al estado previo.", 3f);
    }

    /// <summary>Limpieza sin restaurar (tambien la usa ResetWorld si un game over corta el scrub).</summary>
    void ClearScrubState()
    {
        scrubActive = false;
        scrubAuto = false;
        scrubStep = false;
        scrubSave = null;
        scrubNpcs.Clear();
        pendingScrubSteps = 0;
        pendingScrubPageIndex = -1;
    }

    void RestoreScrubSnapshot()
    {
        if (scrubSave == null) return;
        ApplyLoadedGame(scrubSave);
        player.Facing = scrubFacing;
        // Los NPCs vuelven a donde estaban al entrar (un MoveEvent de la cutscene los paseo).
        foreach (var (id, s) in scrubNpcs)
            if (npcMovers.TryGetValue(id, out var m)) { m.Teleport(s.X, s.Y); m.Facing = s.F; }
        messageTimer = 0;
    }

    /// <summary>Hay una accion bloqueante de la cola en curso? (el HUD muestra EN CURSO y el
    /// input es de esa accion, no del scrubber).</summary>
    bool ScrubBlocking() => activeDialogue != null || activeBattle != null || activeShop != null
        || itemGetItem != null || camPanning || cutsceneWait > 0 || cutsceneMover != null || transition.Blocking;

    /// <summary>Foto por frame para el HUD del scrubber (EditorMode.DrawScrubUi la dibuja).</summary>
    EditorMode.ScrubView ScrubHud()
    {
        var done = scrubCommands.Count - commandQueue.Count;
        var running = ScrubBlocking();
        var status = commandQueue.Count == 0 && !running
            ? "FIN  -  R reinicia desde el snapshot, Esc vuelve al editor"
            : running ? $"EN CURSO {done}/{scrubCommands.Count}: {CommandLabel(scrubCommands[Math.Max(0, done - 1)])}"
            : scrubAuto ? "REPRODUCIENDO..."
            : $"PAUSADO antes de {Math.Min(done + 1, scrubCommands.Count)}/{scrubCommands.Count}";
        var highlight = running ? done - 1 : Math.Min(done, scrubCommands.Count - 1);
        return new EditorMode.ScrubView(scrubEventId, scrubPage + 1, scrubPageCount,
            [.. scrubCommands.Select(CommandLabel)], done, Math.Max(0, highlight), status);
    }

    static string CommandLabel(EventCommand c)
    {
        var target = string.IsNullOrWhiteSpace(c.TargetId) ? "" : " " + c.TargetId;
        var value = string.IsNullOrWhiteSpace(c.Value) ? "" : $" \"{c.Value}\"";
        return $"{c.Kind}{target}{value}";
    }

    /// <summary>Avanza la accion bloqueante de cutscene (espera o caminata dirigida) y, al
    /// terminar, sigue con la cola. Un paso bloqueado por colision se saltea: una cutscene
    /// jamas congela el juego esperando una casilla que no se va a liberar.</summary>
    void UpdateCutsceneAction(float dt)
    {
        UpdateFollowers(dt); // los followers acompanan al lider tambien en un MovePlayer

        if (cutsceneWait > 0)
        {
            cutsceneWait -= dt;
            if (cutsceneWait > 0) return;
            cutsceneWait = 0;
        }
        if (cutsceneMover != null)
        {
            cutsceneMover.Update(dt);
            while (!cutsceneMover.Moving && cutsceneSteps.Count > 0)
            {
                var (dx, dy, facing, faceOnly) = CutsceneSteps.Decode(cutsceneSteps.Dequeue());
                if (faceOnly) cutsceneMover.Facing = facing;
                else cutsceneMover.TryStep(dx, dy, (x, y) => CanOccupy(x, y, ignoreEventId: cutsceneMoverEventId == "" ? null : cutsceneMoverEventId));
            }
            if (cutsceneMover.Moving) return;
            cutsceneMover = null;
            cutsceneMoverEventId = "";
        }
        ContinueCommandQueue();
    }

    bool ExecuteCommand(EventCommand command)
    {
        switch (command.Kind)
        {
            case CommandKind.Dialogue:
                StartDialogue(command.TargetId);
                return true;
            case CommandKind.Battle:
                var battleId = command.TargetId;
                sfx?.Play("sfx.encounter");
                if (screen != null)
                {
                    // El swirl retuerce el ultimo frame del mundo; el combate arranca al fundir a negro.
                    transition.BattleSwirl(screen.Snapshot(), 0.85f, () => StartBattle(battleId));
                }
                else
                {
                    StartBattle(battleId);
                }
                return true;
            case CommandKind.OpenShop:
                var shop = project.Shops.First(x => x.Id == command.TargetId);
                activeShop = new ShopSession(shop, project, money, inventory);
                sfx?.Play("sfx.confirm");
                return true;
            case CommandKind.SetVariable:
                flags[command.TargetId] = command.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
                return false;
            case CommandKind.GiveItem:
                GiveItem(command.TargetId, command.Value);
                SetMessage($"Recibiste {ItemName(command.TargetId)}.");
                // El "+1 Cafe" clasico sobre el jugador: game feel automatico de todo GiveItem
                // (comando o efecto de dialogo); la ceremonia ShowItemGet ya tiene el suyo.
                var giveCount = int.TryParse(command.Value, out var gc) ? Math.Max(1, gc) : 1;
                AddWorldFloat($"+{giveCount} {ItemName(command.TargetId)}", FloatGold);
                return false;
            case CommandKind.GiveMoney:
                var gained = int.TryParse(command.Value, out var gm) ? Math.Max(0, gm) : 0;
                money += gained;
                if (gained > 0) AddWorldFloat($"+${gained}", FloatGold);
                return false;
            case CommandKind.TakeMoney:
                // El cobro de los 90 (maquinas, peajes, posaderos duros): si alcanza, se cobra
                // y la cola SIGUE; si no, mensaje y la cola entera se corta (no hay ritual fiado).
                var cost = int.TryParse(command.Value, out var tm) ? Math.Max(0, tm) : 0;
                if (money < cost)
                {
                    SetMessage(UiStrings.CantAffordAmount(cost));
                    sfx?.Play("sfx.cancel");
                    commandQueue.Clear();
                    return true;
                }
                money -= cost;
                if (cost > 0) AddWorldFloat($"-${cost}", FloatRed);
                return false;
            case CommandKind.SetWeather:
                weatherOverride = command.TargetId.Trim(); // "" = despejar; no bloquea la cola
                return false;
            case CommandKind.ShowFloat:
                // Texto flotante autorado (targetId = evento ancla, ""/"player" = jugador;
                // value = "texto" o "texto:#RRGGBB"). NO bloquea la cola, como ShowEmote.
                var floatParts = command.Value.Split(':', 2);
                var floatColor = floatParts.Length > 1 && TryParseHexColor(floatParts[1], out var fc) ? fc : FloatGold;
                var floatAnchor = string.IsNullOrWhiteSpace(command.TargetId) || command.TargetId.Equals("player", StringComparison.OrdinalIgnoreCase) ? "player" : command.TargetId;
                if (!string.IsNullOrWhiteSpace(floatParts[0])) AddWorldFloat(floatParts[0], floatColor, floatAnchor);
                return false;
            case CommandKind.PlaySong:
                music?.Play(command.TargetId);
                legacyMusic?.Play(command.TargetId);
                return false;
            case CommandKind.PlaySfx:
                sfx?.Play(command.TargetId);
                return false;
            case CommandKind.PlayVfx:
                // NO bloquea la cola (como ShowEmote): acompana la escena; Wait la pausa si hace falta.
                if (FindVfx(command.TargetId) is { Kind: "impact" } worldFx)
                {
                    var vfxAnchor = string.IsNullOrWhiteSpace(command.Value) || command.Value.Trim().Equals("player", StringComparison.OrdinalIgnoreCase) ? "player" : command.Value.Trim();
                    worldVfx[vfxAnchor] = (worldFx, 0f);
                    if (worldFx.SfxId != "") sfx?.Play(worldFx.SfxId); // el sonido viaja con el efecto
                }
                return false;
            case CommandKind.TransferPlayer:
                var (toX, toY) = ParseXy(command.Value);
                sfx?.Play("sfx.door");
                StartTransfer("", command.TargetId, toX, toY);
                return true;
            case CommandKind.Wait:
                cutsceneWait = CutsceneSteps.TryParseWait(command.Value, out var seconds) ? seconds : 0.5f;
                return true;
            case CommandKind.MoveEvent:
                if (!npcMovers.TryGetValue(command.TargetId, out var eventMover)) return false;
                cutsceneMover = eventMover;
                cutsceneMoverEventId = command.TargetId;
                cutsceneSteps = new Queue<string>(CutsceneSteps.Parse(command.Value));
                return true;
            case CommandKind.MovePlayer:
                cutsceneMover = player;
                cutsceneMoverEventId = "";
                cutsceneSteps = new Queue<string>(CutsceneSteps.Parse(command.Value));
                return true;
            case CommandKind.PanCamera:
                return StartCameraPan(command.TargetId, CutsceneSteps.TryParseWait(command.Value, out var panSeconds) ? panSeconds : 1f);
            case CommandKind.AdvanceTime:
                var timeValue = command.Value.Trim().ToLowerInvariant();
                transition.FadeToBlack(0.8f, () =>
                {
                    // Cambiar de FRANJA conserva los emotes (el Zzz de sueno acompana el paso
                    // a la tarde y sobrevive por su timer); AMANECER (+dia) los limpia todos:
                    // dormir es el corte duro de la jornada y nadie amanece con el globo de
                    // anoche.
                    if (timeValue == "+dia") { day++; dayPhase = "manana"; emotes.Clear(); }
                    else if (timeValue is "manana" or "tarde" or "noche") dayPhase = timeValue;
                    dayCardText = DayCardText();
                    dayCardTimer = 2.4f;
                });
                return true; // la cola espera el fade; la placa acompana la vuelta de la luz
            case CommandKind.ShowItemGet:
                var itemGet = project.Items.FirstOrDefault(x => x.Id == command.TargetId);
                if (itemGet == null) return false; // item borrado en caliente: la cola sigue
                itemGetCount = int.TryParse(command.Value, out var igc) ? Math.Max(1, igc) : 1;
                GiveItem(itemGet.Id, itemGetCount.ToString());
                itemGetItem = itemGet;
                itemGetTime = 0; // la fanfarria suena cuando la ceremonia APARECE (UpdateItemGet)
                return true; // la cola espera la ceremonia: Enter la cierra
            case CommandKind.ShowEmote:
                if (CutsceneSteps.TryParseEmote(command.Value, out var emoteIcon, out var emoteSeconds))
                {
                    var key = string.IsNullOrWhiteSpace(command.TargetId) || command.TargetId.Equals("player", StringComparison.OrdinalIgnoreCase) ? "player" : command.TargetId;
                    emotes[key] = (emoteIcon, emoteSeconds);
                }
                return false;
            case CommandKind.AddPartyMember:
                var joined = party.AddMember(project, command.TargetId);
                if (joined != null) { SetMessage(joined, 4f); sfx?.Play("sfx.victory"); RebuildFollowers(); }
                return false;
            case CommandKind.RemovePartyMember:
                var left = party.RemoveMember(command.TargetId);
                if (left != null) { SetMessage(left, 4f); RebuildFollowers(); }
                return false;
            case CommandKind.OpenInn:
                var innPrice = int.TryParse(command.Value, out var parsedPrice) ? Math.Max(0, parsedPrice) : 0;
                if (money < innPrice)
                {
                    SetMessage(UiStrings.CantAffordRest(innPrice));
                    sfx?.Play("sfx.cancel");
                    return false;
                }
                money -= innPrice;
                sfx?.Play("sfx.save"); // el jingle de descanso clasico
                transition.FadeToBlack(0.9f, () =>
                {
                    party.RestAll();
                    SetMessage("La party descansa y amanece como nueva.", 4f);
                });
                return true;
            default:
                SetMessage($"Comando {command.Kind}: {command.TargetId}");
                return false;
        }
    }

    /// <summary>La ceremonia espera al jugador (clasico item clave): Enter la cierra recien
    /// pasado un respiro minimo, para que un Enter apurado del dialogo previo no la salte.</summary>
    void UpdateItemGet(float dt)
    {
        // Disparada como efecto de dialogo, la ceremonia espera a que el dialogo cierre:
        // recien aca (su primer frame como estado activo) suena la fanfarria.
        if (itemGetTime == 0) sfx?.Play("sfx.item_get");
        itemGetTime += dt;
        if (itemGetTime > 0.6f && (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space) || synthConfirm))
        {
            itemGetItem = null;
            sfx?.Play("sfx.confirm");
            ContinueCommandQueue();
        }
    }

    /// <summary>(Re)crea la cadena de followers: uno por miembro no-lider, apilados sobre el
    /// lider (se despliegan al caminar). Se llama cuando la party o el spawn cambian.</summary>
    void RebuildFollowers()
    {
        followerMovers.Clear();
        for (var i = 1; i < party.Members.Count; i++)
        {
            var m = new GridMover { SecondsPerTile = player.SecondsPerTile };
            m.Teleport(player.TileX, player.TileY);
            followerMovers.Add(m);
        }
        lastLeaderX = player.TileX;
        lastLeaderY = player.TileY;
    }

    /// <summary>Cadena de followers: cuando el lider reserva un tile nuevo (arranca un paso),
    /// cada follower avanza un tile hacia donde estaba el de adelante. Pasan por todo (siguen
    /// el camino ya validado del lider), asi que nunca chocan ni se traban.</summary>
    void UpdateFollowers(float dt)
    {
        if (followerMovers.Count > 0 && (player.TileX != lastLeaderX || player.TileY != lastLeaderY))
        {
            var aheadX = lastLeaderX;
            var aheadY = lastLeaderY;
            foreach (var f in followerMovers)
            {
                var oldX = f.TileX;
                var oldY = f.TileY;
                var fdx = Math.Sign(aheadX - f.TileX);
                var fdy = Math.Sign(aheadY - f.TileY);
                if (fdx != 0 || fdy != 0) f.TryStep(fdx, fdy, (_, _) => true);
                aheadX = oldX;
                aheadY = oldY;
            }
            lastLeaderX = player.TileX;
            lastLeaderY = player.TileY;
        }
        foreach (var f in followerMovers) f.Update(dt);
    }

    /// <summary>Sprite de un follower por convencion actor.X -> sprite.X (como los enemigos).
    /// Vacio si no existe: DrawCharacter cae al placeholder.</summary>
    string FollowerSpriteId(PartyMember m)
    {
        var candidate = "sprite." + m.Def.Id.Replace("actor.", "");
        return project.Sprites.Any(s => s.Id == candidate) ? candidate : "";
    }

    /// <summary>Empieza un pan de camara hacia un evento ("" o "player" = volver al jugador).
    /// Devuelve true si bloquea la cola (false = blanco inexistente, se saltea).</summary>
    bool StartCameraPan(string targetEventId, float seconds)
    {
        var ts = project.Render.TileSize;
        (camFromX, camFromY) = CameraFocus();
        if (string.IsNullOrWhiteSpace(targetEventId) || targetEventId.Equals("player", StringComparison.OrdinalIgnoreCase))
        {
            camToX = player.PixelX(ts) + ts / 2f;
            camToY = player.PixelY(ts) + ts / 2f;
            camReturning = true;
        }
        else
        {
            if (!npcMovers.TryGetValue(targetEventId, out var mover)) return false;
            camToX = mover.PixelX(ts) + ts / 2f;
            camToY = mover.PixelY(ts) + ts / 2f;
            camReturning = false;
        }
        camOverride = true;
        camPanning = true;
        camPanElapsed = 0;
        camPanDuration = Math.Max(0.05f, seconds);
        return true;
    }

    /// <summary>Punto del mundo que mira la camara: el jugador, o el pan de cutscene (suavizado).</summary>
    (float X, float Y) CameraFocus()
    {
        var ts = project.Render.TileSize;
        if (!camOverride) return (player.PixelX(ts) + ts / 2f, player.PixelY(ts) + ts / 2f);
        if (!camPanning) return (camToX, camToY);
        var t = Math.Clamp(camPanElapsed / camPanDuration, 0f, 1f);
        t = t * t * (3f - 2f * t); // smoothstep: arranca y frena suave, como los scrolls de SNES
        return (camFromX + (camToX - camFromX) * t, camFromY + (camToY - camFromY) * t);
    }

    /// <summary>Si la casilla tiene warp, transfiere con su efecto de transicion y devuelve true.</summary>
    bool TryWarpAt(int x, int y)
    {
        var warp = map.Warps.FirstOrDefault(w => w.X == x && w.Y == y);
        if (warp == null) return false;
        sfx?.Play("sfx.door");
        StartTransfer(warp.Transition, warp.ToMapId, warp.ToX, warp.ToY);
        return true;
    }

    /// <summary>Cierra la pantalla con el wipe pedido ("" = default del proyecto), centrado en el jugador.</summary>
    void StartTransfer(string style, string mapId, int x, int y)
    {
        var resolved = string.IsNullOrWhiteSpace(style) ? project.Render.WarpTransition : style;
        var ts = project.Render.TileSize;
        transition.WipeToBlack(resolved, 0.9f, () => ApplyTransfer(mapId, x, y),
            player.PixelX(ts) - camera.X + ts / 2f, player.PixelY(ts) - camera.Y + ts / 2f);
    }

    /// <summary>Cambio real de mapa: tileset, posicion, musica (solo si cambia) y cartel con el nombre.</summary>
    void ApplyTransfer(string mapId, int x, int y)
    {
        var dest = project.Maps.FirstOrDefault(m => m.Id == mapId);
        if (dest == null) { SetMessage($"TransferPlayer: el mapa {mapId} no existe."); return; }
        map = dest;
        tileset = project.Tilesets.First(t => t.Id == map.TilesetId);
        camPanning = false; camOverride = false; // el pan era del mapa anterior
        // Los globos de NPCs eran del mapa que dejamos; el del jugador VIAJA con el (el Zzz
        // de sueno tiene que sobrevivir el warp de la libreria a casa).
        var keepPlayerEmote = emotes.TryGetValue("player", out var pe) ? pe : default;
        var hadPlayerEmote = emotes.ContainsKey("player");
        emotes.Clear();
        if (hadPlayerEmote) emotes["player"] = keepPlayerEmote;
        worldVfx.Clear(); // los destellos son instantaneos: ninguno sobrevive un cambio de mapa
        worldFloats.Clear();
        weatherOverride = null; // el mapa destino manda con su propio clima autorado
        player.Teleport(Math.Clamp(x, 0, map.Width - 1), Math.Clamp(y, 0, map.Height - 1));
        RebuildFollowers(); // la fila regrupa sobre el lider al cruzar una puerta
        if (!string.IsNullOrWhiteSpace(map.SongId)) { music?.Play(map.SongId); legacyMusic?.Play(map.SongId); }
        SetMessage(string.IsNullOrWhiteSpace(map.Name) ? map.Id : map.Name, 2f);
    }

    /// <summary>Contexto automatico para capturas de escena: cambia al mapa del evento y ubica
    /// al jugador sobre el trigger o en una celda transitable vecina al NPC/objeto.</summary>
    void PrepareDebugScene(string eventId)
    {
        var ev = project.Events.FirstOrDefault(x => x.Id == eventId);
        var dest = ev == null ? null : project.Maps.FirstOrDefault(x => x.Id == ev.MapId);
        if (ev == null || dest == null) return;
        var destTileset = project.Tilesets.FirstOrDefault(x => x.Id == dest.TilesetId);
        var candidates = ev.Kind == EventKind.Trigger
            ? new[] { (ev.X, ev.Y), (ev.X, ev.Y + 1), (ev.X - 1, ev.Y), (ev.X + 1, ev.Y), (ev.X, ev.Y - 1) }
            : new[] { (ev.X, ev.Y + 1), (ev.X - 1, ev.Y), (ev.X + 1, ev.Y), (ev.X, ev.Y - 1), (ev.X, ev.Y) };
        var spawn = candidates.FirstOrDefault(cell =>
        {
            if (cell.Item1 < 0 || cell.Item2 < 0 || cell.Item1 >= dest.Width || cell.Item2 >= dest.Height) return false;
            var tileId = dest.Tiles[cell.Item2 * dest.Width + cell.Item1];
            return destTileset?.Tiles.FirstOrDefault(x => x.Id == tileId)?.Solid != true;
        });
        ApplyTransfer(dest.Id, spawn == default ? Math.Clamp(ev.X, 0, dest.Width - 1) : spawn.Item1,
            spawn == default ? Math.Clamp(ev.Y, 0, dest.Height - 1) : spawn.Item2);
    }

    /// <summary>Una pagina pedida explicitamente para captura trae su contexto minimo: flags y
    /// reloj se acomodan a sus condiciones para que tambien aparezcan coprotagonistas/props
    /// condicionados por el mismo estado. Solo vive en el runtime de prueba; nunca escribe disco.</summary>
    void ApplyDebugPageConditions(EventPage page)
    {
        foreach (var condition in page.Conditions)
        {
            if (condition.VariableId == "time.dia")
            {
                if (int.TryParse(condition.EqualsValue.Trim(), out var requestedDay)) day = Math.Max(1, requestedDay);
            }
            else if (condition.VariableId == "time.franja")
                dayPhase = condition.EqualsValue.Trim().ToLowerInvariant();
            else
                flags[condition.VariableId] = condition.EqualsValue.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }

    static (int X, int Y) ParseXy(string value)
    {
        var parts = value.Split(',');
        var x = parts.Length > 0 && int.TryParse(parts[0].Trim(), out var px) ? px : 1;
        var y = parts.Length > 1 && int.TryParse(parts[1].Trim(), out var py) ? py : 1;
        return (x, y);
    }

    void StartDialogue(string id)
    {
        var dialogue = project.Dialogues.First(x => x.Id == id);
        activeDialogue = new DialogueSession(dialogue);
        EnterDialogueNode(activeDialogue, dialogue.StartNodeId);
    }

    void EnterDialogueNode(DialogueSession session, string nodeId)
    {
        session.SetNode(nodeId);
        foreach (var effect in session.Current.Effects) ExecuteCommand(effect);
    }

    void EndDialogue()
    {
        activeDialogue = null;
        ContinueCommandQueue();
    }

    void UpdateShop()
    {
        var shop = activeShop!;
        if (Raylib.IsKeyPressed(KeyboardKey.Down)) { shop.SelectNext(); sfx?.Play("sfx.cursor"); }
        if (Raylib.IsKeyPressed(KeyboardKey.Up)) { shop.SelectPrevious(); sfx?.Play("sfx.cursor"); }
        if (Raylib.IsKeyPressed(KeyboardKey.Left) || Raylib.IsKeyPressed(KeyboardKey.Right)) { shop.ToggleMode(); sfx?.Play("sfx.cursor"); }
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space))
        {
            var buying = !shop.Selling;
            var boughtId = shop.SelectedItemId;
            // Equipo que no mejora al lider: avisar y NO comprar (no gastar al pedo). El
            // el equipo es sabor, no un sistema de min-maxing: el jugador nunca compra un downgrade.
            if (buying && boughtId != null && IsRedundantEquip(boughtId, out var blockMsg))
            {
                shop.SetLog(blockMsg);
                sfx?.Play("sfx.cancel");
                return;
            }
            var ok = shop.Confirm();
            sfx?.Play(ok ? "sfx.confirm" : "sfx.cancel");
            // Comprar equipo lo equipa al toque (lo que el jugador espera: "compre el arma,
            // ahora pego mas"); si habia algo peor puesto, sale al inventario.
            if (ok && buying && boughtId != null) TryAutoEquipPurchase(shop, boughtId);
        }
        if (Raylib.IsKeyPressed(KeyboardKey.Backspace))
        {
            money = shop.Money; // el dinero vuelve al mundo recien al cerrar la tienda
            activeShop = null;
            sfx?.Play("sfx.cancel");
            SetMessage(UiStrings.Money(money));
            ContinueCommandQueue();
        }
    }

    void StartBattle(string battleId)
    {
        var battle = project.Battles.First(x => x.Id == battleId);
        activeBattle = new BattleEngine(battle, project, party, inventory);
        rollingHp.Snap(activeBattle.Party[0].Hp);
        enemyFlashTimer = 0;
        enemyFlashIndex = -1;
        battleVfx.Clear();
        bossIntroTimer = battle.Boss ? 2.2f : 0f; // la placa de nombre del jefe se desvanece sola
        if (battle.Boss) shake.Kick(3f, 0.5f); // un temblor de presentacion al aparecer
        if (!string.IsNullOrWhiteSpace(battle.SongId)) { music?.Play(battle.SongId); legacyMusic?.Play(battle.SongId); }
    }

    void UpdateBattle()
    {
        var battle = activeBattle!;
        if (battle.Battle.RollingHp && battle.Party.Count > 0) rollingHp.Update(battle.Party[0].Hp, Raylib.GetFrameTime());

        var confirm = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space) || synthConfirm;
        var cancel = Raylib.IsKeyPressed(KeyboardKey.Backspace) || synthCancel;
        var down = Raylib.IsKeyPressed(KeyboardKey.Down) || Raylib.IsKeyPressed(KeyboardKey.Right) || synthDown;
        var up = Raylib.IsKeyPressed(KeyboardKey.Up) || Raylib.IsKeyPressed(KeyboardKey.Left) || synthUp;

        if (battle.Resolved)
        {
            if (confirm || cancel) FinishBattle(battle);
            return;
        }

        if (debugAutoAttack)
        {
            if (battle.Current == BattleEngine.Phase.Command) { battle.SelectedCommand = 0; battle.ConfirmCommand(); }
            if (battle.Current == BattleEngine.Phase.TargetSelect)
            {
                battle.SelectedTarget = 0;
                battle.ConfirmTarget();
                AfterBattleStep(battle);
                debugAutoAttack = false;
            }
            return;
        }

        switch (battle.Current)
        {
            case BattleEngine.Phase.Command:
                if (down) { battle.SelectedCommand = Math.Min(BattleEngine.Commands.Length - 1, battle.SelectedCommand + 1); sfx?.Play("sfx.cursor"); }
                if (up) { battle.SelectedCommand = Math.Max(0, battle.SelectedCommand - 1); sfx?.Play("sfx.cursor"); }
                if (confirm) { battle.ConfirmCommand(); AfterBattleStep(battle); }
                break;
            case BattleEngine.Phase.SkillSelect:
                var skills = battle.ActingSkills.Count;
                if (down) { battle.SelectedSkill = Math.Min(Math.Max(0, skills - 1), battle.SelectedSkill + 1); sfx?.Play("sfx.cursor"); }
                if (up) { battle.SelectedSkill = Math.Max(0, battle.SelectedSkill - 1); sfx?.Play("sfx.cursor"); }
                if (confirm) { battle.ConfirmSkill(); AfterBattleStep(battle); }
                if (cancel) { battle.Cancel(); sfx?.Play("sfx.cancel"); }
                break;
            case BattleEngine.Phase.TargetSelect:
                var targets = battle.TargetCount;
                if (down) { battle.SelectedTarget = (battle.SelectedTarget + 1) % Math.Max(1, targets); sfx?.Play("sfx.cursor"); }
                if (up) { battle.SelectedTarget = (battle.SelectedTarget - 1 + Math.Max(1, targets)) % Math.Max(1, targets); sfx?.Play("sfx.cursor"); }
                if (confirm) { battle.ConfirmTarget(); AfterBattleStep(battle); }
                if (cancel) { battle.Cancel(); sfx?.Play("sfx.cancel"); }
                break;
        }
    }

    /// <summary>Traduce el feedback del motor de combate a game feel: flash, numero flotante,
    /// shake, sonidos y el VFX de impacto (el de la skill, o los reservados vfx.hit/vfx.heal).</summary>
    void AfterBattleStep(BattleEngine battle)
    {
        var (hitEnemy, damage, playerHit, skillId, healedAlly, healAmount, hitAlly, hitAllyDamage) = battle.ConsumeFeedback();
        var skillVfxId = project.Skills.FirstOrDefault(s => s.Id == skillId)?.VfxId ?? "";
        if (battle.Victory) sfx?.Play("sfx.victory");
        if (hitEnemy >= 0)
        {
            enemyFlashTimer = 0.18f;
            enemyFlashIndex = hitEnemy;
            // Posicion con el layout PREVIO al golpe (si el enemigo cayo, ya no esta entre los vivos).
            var layout = battle.AliveEnemyIndexes;
            if (!layout.Contains(hitEnemy)) { layout.Add(hitEnemy); layout.Sort(); }
            var (spacing, startX) = EnemyLayout(layout.Count, screen!.Width);
            dmgPopX = startX + layout.IndexOf(hitEnemy) * spacing + spacing / 2;
            dmgPopValue = damage;
            dmgPopTimer = DmgPopSeconds;
            // El destello sobre el cuerpo del golpeado (baseline 106, centro ~22px arriba).
            // El sonido sale del propio VFX; el fallback garantiza que un golpe nunca sea mudo.
            if (FindVfx(skillVfxId != "" ? skillVfxId : "vfx.hit") is { Kind: "impact" } hitVfx)
                SpawnBattleVfx(hitVfx, dmgPopX, 84);
            else sfx?.Play("sfx.hit");
        }
        // Cura/revive (skill o item): el brillo sobre el panel del aliado (fila de party en
        // y=128) + el "+N" verde subiendo (game feel: los numeros tienen que verse).
        if (healedAlly >= 0 && healedAlly < 3)
        {
            if (FindVfx(skillVfxId != "" ? skillVfxId : "vfx.heal") is { Kind: "impact" } healVfx)
                SpawnBattleVfx(healVfx, 4 + healedAlly * 84 + 41, 145);
            if (healAmount > 0) battlePops.Add(($"+{healAmount}", FloatGreen, 0f, 4 + healedAlly * 84 + 41, 124));
        }
        // Golpe del jefe a la party: temblor mas fuerte y largo (el jefe tiene que pesar).
        if (playerHit) { var boss = battle.Battle.Boss; shake.Kick(boss ? 7f : 4f, boss ? 0.5f : 0.35f); }
        // El golpe enemigo tambien destella (antes solo habia
        // shake y sonido): vfx.hit_ally sobre el panel del golpeado + el "-N" rojo.
        // Es un VFX propio y no vfx.hit porque ahora el sonido viaja EN el efecto: recibir
        // un golpe tiene que sonar distinto de darlo.
        if (hitAlly >= 0 && hitAlly < 3)
        {
            if ((FindVfx("vfx.hit_ally") ?? FindVfx("vfx.hit")) is { Kind: "impact" } allyVfx)
                SpawnBattleVfx(allyVfx, 4 + hitAlly * 84 + 41, 145);
            else sfx?.Play("sfx.player_hit");
            if (hitAllyDamage > 0) battlePops.Add(($"-{hitAllyDamage}", FloatRed, 0f, 4 + hitAlly * 84 + 41, 124));
        }
        else if (playerHit) sfx?.Play("sfx.player_hit");
    }

    /// <summary>Spawnea un impacto de combate Y toca su sonido: un VFX es AUDIOVISUAL, asi que
    /// el par no se separa. Cualquiera que dispare el efecto suena bien sin repetir el id.</summary>
    void SpawnBattleVfx(VfxDef v, int x, int y)
    {
        battleVfx.Add((v, 0f, x, y));
        if (v.SfxId != "") sfx?.Play(v.SfxId);
    }

    /// <summary>Resuelve un VfxDef por id contra el proyecto VIGENTE (hot reload incluido):
    /// el proyecto puede pisar los reservados; si no, los defaults embebidos del motor.</summary>
    VfxDef? FindVfx(string id) => string.IsNullOrWhiteSpace(id) ? null : VfxEval.Find(project, id);

    /// <summary>Reparto horizontal de los enemigos en pantalla (compartido entre dibujo y feedback).</summary>
    static (int Spacing, int StartX) EnemyLayout(int count, int screenWidth)
    {
        var spacing = count == 0 ? 64 : Math.Min(72, (screenWidth - 16) / count);
        return (spacing, (screenWidth - spacing * count) / 2);
    }

    void FinishBattle(BattleEngine battle)
    {
        transition.FadeToBlack(0.6f, () =>
        {
            if (battle.Victory && !string.IsNullOrWhiteSpace(battle.Battle.VictoryFlag)) flags[battle.Battle.VictoryFlag] = true;
            // El HP/MP con que terminaste ES tu estado en el mundo; los caidos vuelven con 1
            // (no hay game over duro ni revivir todavia).
            foreach (var combatant in battle.Party.Where(c => c.Member != null))
            {
                combatant.Member!.Hp = Math.Max(1, combatant.Hp);
                combatant.Member.Mp = Math.Max(0, combatant.Mp);
            }
            if (battle.Victory)
            {
                money += Math.Max(0, battle.TotalMoney);
                var levelUps = party.GrantExp(Math.Max(0, battle.TotalExp));
                var text = UiStrings.Victory(battle.TotalExp, battle.TotalMoney).TrimStart();
                if (levelUps.Count > 0) text += " " + string.Join(" ", levelUps);
                SetMessage(text, 4f);
            }
            else if (battle.Fled)
            {
                SetMessage("Escaparon del combate.");
            }
            else
            {
                // Game over real: pantalla negra y vuelta al titulo (Continuar carga el save).
                gameOver = true;
                music?.Stop();
                legacyMusic?.Stop();
            }
            // El tema de batalla termina con el combate: vuelve la musica del mapa.
            if (!gameOver && !string.IsNullOrWhiteSpace(battle.Battle.SongId) && !string.IsNullOrWhiteSpace(map.SongId))
            {
                music?.Play(map.SongId);
                legacyMusic?.Play(map.SongId);
            }
            activeBattle = null;
            if (battle.Victory) ContinueCommandQueue();
            else commandQueue.Clear();
        });
    }

    void GiveItem(string itemId, string countText)
    {
        var count = int.TryParse(countText, out var parsed) ? Math.Max(1, parsed) : 1;
        for (var i = 0; i < count; i++) inventory.Add(itemId);
    }

    void AddWorldFloat(string text, Color color, string anchor = "player") => worldFloats.Add((text, color, 0f, anchor));

    static bool TryParseHexColor(string hex, out Color color)
    {
        color = Color.White;
        hex = hex.Trim();
        if (hex.Length != 7 || hex[0] != '#') return false;
        try
        {
            color = new Color(Convert.ToByte(hex.Substring(1, 2), 16), Convert.ToByte(hex.Substring(3, 2), 16), Convert.ToByte(hex.Substring(5, 2), 16), (byte)255);
            return true;
        }
        catch { return false; }
    }

    /// <summary>Textos flotantes del mundo: suben sobre su ancla y PARPADEAN al final (sin
    /// alpha: los semi-transparentes directos degradan el canal alfa del render texture).</summary>
    void DrawWorldFloats(int ts)
    {
        foreach (var (text, color, t, anchor) in worldFloats)
        {
            GridMover? mover = anchor == "player" ? player : npcMovers.TryGetValue(anchor, out var m) ? m : null;
            if (mover == null) continue;
            if (anchor != "player")
            {
                var ev = project.Events.FirstOrDefault(e => e.Id == anchor);
                if (ev == null || ev.MapId != map.Id || !EventPresent(ev)) continue;
            }
            if (t > FloatSeconds - 0.35f && ((int)(time * 16)) % 2 == 1) continue; // parpadeo de salida
            var fx = (int)mover.PixelX(ts) - camera.X + ts / 2 - font.Measure(text) / 2;
            var fy = (int)mover.PixelY(ts) - camera.Y - 14 - (int)(t * 11f);
            font.DrawShadowed(text, fx, fy, color, new Color(20, 20, 30, 255));
        }
    }

    /// <summary>Menu de pausa clasico: columna de secciones a la izquierda, panel a la derecha.
    /// GUARDAR/CARGAR manejan 3 slots; SALIR cierra el juego (Esc ya no mata la ventana).</summary>
    void UpdatePause()
    {
        var back = Raylib.IsKeyPressed(KeyboardKey.Escape) || Raylib.IsKeyPressed(KeyboardKey.Backspace);
        var confirm = Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space);

        if (!pauseInPanel)
        {
            if (Raylib.IsKeyPressed(KeyboardKey.Down)) { pauseSection = Math.Min(PauseSections.Length - 1, pauseSection + 1); sfx?.Play("sfx.cursor"); }
            if (Raylib.IsKeyPressed(KeyboardKey.Up)) { pauseSection = Math.Max(0, pauseSection - 1); sfx?.Play("sfx.cursor"); }
            if (back) { paused = false; sfx?.Play("sfx.cancel"); return; }
            if (!confirm) return;
            switch (PauseSections[pauseSection])
            {
                case "ITEMS" when GroupedInventory().Count == 0: sfx?.Play("sfx.cancel"); break;
                case "ITEMS" or "EQUIPO" or "OPCIONES" or "GUARDAR" or "CARGAR": pauseInPanel = true; pauseRow = 0; sfx?.Play("sfx.confirm"); break;
                case "SALIR": quitRequested = true; break;
            }
            return;
        }

        var rows = PauseSections[pauseSection] switch
        {
            "ITEMS" => GroupedInventory().Count,
            "EQUIPO" => Math.Min(3, party.Members.Count) * 2, // dos filas por miembro: arma y defensa
            "OPCIONES" => 2, // musica y sonidos
            _ => 3,
        };
        if (Raylib.IsKeyPressed(KeyboardKey.Down)) { pauseRow = Math.Min(Math.Max(0, rows - 1), pauseRow + 1); sfx?.Play("sfx.cursor"); }
        if (Raylib.IsKeyPressed(KeyboardKey.Up)) { pauseRow = Math.Max(0, pauseRow - 1); sfx?.Play("sfx.cursor"); }
        if (back) { pauseInPanel = false; sfx?.Play("sfx.cancel"); return; }

        // OPCIONES ajusta con Izquierda/Derecha (pasos de 10%) y persiste al toque.
        if (PauseSections[pauseSection] == "OPCIONES")
        {
            var delta = (Raylib.IsKeyPressed(KeyboardKey.Right) ? 0.1 : 0.0) - (Raylib.IsKeyPressed(KeyboardKey.Left) ? 0.1 : 0.0);
            if (delta != 0)
            {
                if (pauseRow == 0)
                {
                    settings.MusicVolume = Math.Clamp(Math.Round(settings.MusicVolume + delta, 1), 0.0, 1.0);
                    music?.SetVolume(settings.MusicVolume);
                }
                else
                {
                    settings.SfxVolume = Math.Clamp(Math.Round(settings.SfxVolume + delta, 1), 0.0, 1.0);
                    sfx?.SetVolume(settings.SfxVolume);
                }
                settings.Save();
                sfx?.Play("sfx.cursor"); // con SFX a 0 no suena: feedback honesto del ajuste
            }
            return;
        }

        if (!confirm) return;
        switch (PauseSections[pauseSection])
        {
            case "ITEMS":
                UsePauseItem();
                break;
            case "EQUIPO":
                CyclePauseEquip();
                break;
            case "GUARDAR":
                SaveSlot(pauseRow);
                pauseNote = UiStrings.SavedIn(pauseRow + 1);
                break;
            case "CARGAR":
                if (saveSystem.HasSlot(pauseRow)) { paused = false; LoadSlot(pauseRow); }
                else sfx?.Play("sfx.cancel");
                break;
        }
    }

    string pauseNote = "";

    /// <summary>Usar un item curativo desde el menu: cura al miembro vivo mas lastimado.</summary>
    void UsePauseItem()
    {
        var rows = GroupedInventory();
        if (pauseRow >= rows.Count) { sfx?.Play("sfx.cancel"); return; }
        var item = project.Items.FirstOrDefault(i => i.Id == rows[pauseRow].Id);
        var note = item == null ? null : party.UseHealItem(item);
        if (note == null) { pauseNote = "Ese item no hace nada aca."; sfx?.Play("sfx.cancel"); return; }
        inventory.Remove(item!.Id);
        pauseNote = note;
        sfx?.Play("sfx.confirm");
        pauseRow = Math.Clamp(pauseRow, 0, Math.Max(0, GroupedInventory().Count - 1));
    }

    List<(string Id, string Name, int Count)> GroupedInventory() =>
        [.. inventory.GroupBy(id => id).Select(g => (g.Key, ItemName(g.Key), g.Count())).OrderBy(x => x.Item2)];

    /// <summary>"Poder" de una pieza de equipo: suma de su bonus. Metrica simple para decidir
    /// si algo es mejora (el equipo es sabor, no min-maxing: no hace falta mas).</summary>
    static int EquipPower(ItemDef item) => item.Bonus is { } b ? b.Hp + b.Mp + b.Attack + b.Defense + b.Speed : 0;

    /// <summary>El arma/armadura a comprar NO mejora lo que el lider ya tiene puesto (para
    /// bloquear la compra y no gastar al pedo). Slot libre = nunca redundante (se auto-equipa).</summary>
    bool IsRedundantEquip(string itemId, out string message)
    {
        message = "";
        var item = project.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null || string.IsNullOrWhiteSpace(item.Slot) || party.Members.Count == 0) return false;
        var member = party.Members[0];
        var current = item.Slot.Equals("weapon", StringComparison.OrdinalIgnoreCase) ? member.Weapon : member.Armor;
        if (current == null || EquipPower(item) > EquipPower(current)) return false;
        message = $"{member.Def.Name} ya tiene algo igual o mejor: {current.Name}.";
        return true;
    }

    /// <summary>Compraste un arma/armadura: la equipa al lider al toque (la potencia se siente
    /// sin ir al menu). Lo que salga del slot vuelve al inventario. Solo llega aca si es
    /// mejora o el slot estaba libre (IsRedundantEquip filtra los downgrades antes de comprar).</summary>
    void TryAutoEquipPurchase(ShopSession shop, string itemId)
    {
        var item = project.Items.FirstOrDefault(i => i.Id == itemId);
        if (item == null || string.IsNullOrWhiteSpace(item.Slot) || party.Members.Count == 0) return;
        var member = party.Members[0];
        if (!inventory.Remove(itemId)) return; // la que se equipa sale del inventario
        var previous = member.Equip(item);
        if (previous != null) inventory.Add(previous.Id); // la vieja vuelve al inventario
        shop.SetLog(previous == null
            ? UiStrings.BoughtAndEquipped(item.Name, member.Def.Name)
            : UiStrings.BoughtUpgrade(item.Name, member.Def.Name, previous.Name));
    }

    /// <summary>Enter en una fila de EQUIPO cicla lo equipable del inventario para ese slot:
    /// nada -> primero -> segundo -> ... -> nada. Lo que sale vuelve al inventario.</summary>
    void CyclePauseEquip()
    {
        var member = party.Members[Math.Min(pauseRow / 2, party.Members.Count - 1)];
        var slot = pauseRow % 2 == 0 ? "weapon" : "armor";
        var options = inventory.Distinct()
            .Select(id => project.Items.FirstOrDefault(i => i.Id == id))
            .Where(i => i != null && i.Slot.Equals(slot, StringComparison.OrdinalIgnoreCase))
            .Cast<ItemDef>().OrderBy(i => i.Name).ToList();
        var current = slot == "weapon" ? member.Weapon : member.Armor;
        if (current == null && options.Count == 0)
        {
            pauseNote = slot == "weapon" ? "No tenes armas en el inventario." : "No tenes defensas en el inventario.";
            sfx?.Play("sfx.cancel");
            return;
        }
        var index = current == null ? -1 : options.FindIndex(i => i.Id == current.Id);
        var next = index + 1 < options.Count ? options[index + 1] : null;
        if (next != null) inventory.Remove(next.Id);
        var previous = next != null ? member.Equip(next) : member.Unequip(slot);
        if (previous != null) inventory.Add(previous.Id);
        pauseNote = next != null
            ? $"{member.Def.Name} equipa {next.Name}."
            : $"{member.Def.Name} se saca {previous?.Name ?? "todo"}.";
        sfx?.Play("sfx.confirm");
    }

    static string BonusText(ItemDef? item)
    {
        if (item?.Bonus is not { } b) return "";
        var parts = new List<string>();
        if (b.Attack != 0) parts.Add($"+{b.Attack}Atk");
        if (b.Defense != 0) parts.Add($"+{b.Defense}Def");
        if (b.Speed != 0) parts.Add($"+{b.Speed}Vel");
        if (b.Hp != 0) parts.Add($"+{b.Hp}HP");
        if (b.Mp != 0) parts.Add($"+{b.Mp}MP");
        return parts.Count == 0 ? "" : $" ({string.Join(" ", parts)})";
    }

    /// <summary>Estado completo del mundo como SaveGame: lo comparten el guardado en slots y
    /// el snapshot del scrubber (misma foto, distinto destino: disco o memoria).</summary>
    SaveGame BuildSaveGame() => new()
    {
        ProjectId = project.Id, MapId = map.Id, PlayerX = player.TileX, PlayerY = player.TileY,
        Flags = new Dictionary<string, bool>(flags), Inventory = [.. inventory], Money = money, Day = day, Phase = dayPhase,
        Party = [.. party.Members.Select(m => new PartyMemberSave { ActorId = m.Def.Id, Level = m.Level, Exp = m.Exp, Hp = m.Hp, Mp = m.Mp, WeaponId = m.Weapon?.Id ?? "", ArmorId = m.Armor?.Id ?? "" })]
    };

    void SaveSlot(int slot)
    {
        saveSystem.Save(slot, BuildSaveGame());
        sfx?.Play("sfx.save");
        SetMessage(UiStrings.SavedIn(slot + 1));
    }

    void LoadSlot(int slot) => transition.FadeToBlack(0.6f, () => ApplyLoad(slot));

    /// <summary>Mundo de fabrica tras un game over: flags default, sin items, party nueva, mapa inicial.
    /// "Nueva partida" desde el titulo arranca limpio de verdad; "Continuar" carga el save por encima.</summary>
    void ResetWorld()
    {
        flags.Clear();
        foreach (var variable in project.Variables.Where(v => v.Kind == VariableKind.Flag))
            flags[variable.Id] = variable.Default.Equals("true", StringComparison.OrdinalIgnoreCase);
        inventory.Clear();
        money = Math.Max(0, project.StartMoney);
        party = PartyState.Create(project);
        activeDialogue = null; activeBattle = null; activeShop = null; commandQueue.Clear();
        cutsceneWait = 0; cutsceneMover = null; cutsceneMoverEventId = ""; cutsceneSteps.Clear();
        camPanning = false; camOverride = false; emotes.Clear(); worldVfx.Clear(); battleVfx.Clear(); worldFloats.Clear(); battlePops.Clear(); weatherOverride = null;
        itemGetItem = null; itemGetTime = 0;
        ClearScrubState(); // un game over durante un scrub corta el scrub, sin restaurar (el mundo ya se resetea)
        day = 1; dayPhase = "manana"; dayCardTimer = 0;
        map = project.Maps.First(x => x.Id == project.StartMapId);
        tileset = project.Tilesets.First(x => x.Id == map.TilesetId);
        TeleportPlayerToStart();
        foreach (var ev in project.Events)
        {
            routineDirection[ev.Id] = 1;
            var mover = new GridMover { SecondsPerTile = 0.3f };
            mover.Teleport(ev.X, ev.Y);
            npcMovers[ev.Id] = mover;
        }
        RebuildFollowers();
        music?.Play(map.SongId);
        legacyMusic?.Play(map.SongId);
    }

    /// <summary>Spawn inicial: startX/startY exactos si el proyecto los define (>= 0);
    /// si no, el legado — al lado del evento de referencia StartEventId.</summary>
    void TeleportPlayerToStart()
    {
        if (project.StartX >= 0 && project.StartY >= 0)
        {
            player.Teleport(Math.Clamp(project.StartX, 0, map.Width - 1), Math.Clamp(project.StartY, 0, map.Height - 1));
            return;
        }
        var start = project.Events.FirstOrDefault(x => x.Id == project.StartEventId);
        player.Teleport(
            Math.Clamp((start?.X ?? 2) - 1, 1, Math.Max(1, map.Width - 2)),
            Math.Clamp(start?.Y ?? 2, 1, Math.Max(1, map.Height - 2)));
    }

    void ApplyLoad(int slot)
    {
        try
        {
            ApplyLoadedGame(saveSystem.Load(slot));
            SetMessage(UiStrings.LoadedFrom(slot + 1));
        }
        catch (Exception ex) { SetMessage("No se pudo cargar: " + ex.Message); }
    }

    /// <summary>Aplica un SaveGame al mundo (desde disco o desde el snapshot del scrubber):
    /// mapa, posicion, flags, inventario, dinero, party y tiempo, con la limpieza de estado
    /// transitorio (dialogo/combate/cola/emotes) que cualquier restauracion necesita.</summary>
    void ApplyLoadedGame(SaveGame save)
    {
        // Restaurar tambien el mapa: con warps el jugador puede haber guardado en cualquier lado.
        var savedMap = project.Maps.FirstOrDefault(m => m.Id == save.MapId);
        if (savedMap != null)
        {
            var mapChanged = !ReferenceEquals(savedMap, map);
            map = savedMap;
            tileset = project.Tilesets.First(t => t.Id == map.TilesetId);
            // La musica solo se reinicia si el mapa cambio: reiniciar el scrubber en el
            // mismo mapa no corta el tema que ya suena.
            if (mapChanged && !string.IsNullOrWhiteSpace(map.SongId)) { music?.Play(map.SongId); legacyMusic?.Play(map.SongId); }
        }
        player.Teleport(save.PlayerX, save.PlayerY);
        flags.Clear(); foreach (var pair in save.Flags) flags[pair.Key] = pair.Value;
        inventory.Clear(); inventory.AddRange(save.Inventory);
        money = Math.Max(0, save.Money);
        // La party se reconstruye DESDE el save (no desde partyActorIds): los miembros
        // sumados en runtime (AddPartyMember) tambien vuelven. Fallback al estado
        // inicial solo si el save no traia party (saves viejos).
        if (save.Party.Count > 0)
        {
            var restored = new PartyState();
            foreach (var saved in save.Party)
            {
                var def = project.Actors.FirstOrDefault(a => a.Id == saved.ActorId);
                if (def == null) continue; // el actor ya no existe en el proyecto
                var member = new PartyMember(def, saved.Level, saved.Exp, saved.Hp, saved.Mp);
                // El equipo se reengancha por id; si el item ya no existe en el proyecto, cae.
                var weapon = project.Items.FirstOrDefault(i => i.Id == saved.WeaponId && i.Slot.Equals("weapon", StringComparison.OrdinalIgnoreCase));
                var armor = project.Items.FirstOrDefault(i => i.Id == saved.ArmorId && i.Slot.Equals("armor", StringComparison.OrdinalIgnoreCase));
                if (weapon != null) member.Equip(weapon);
                if (armor != null) member.Equip(armor);
                member.Hp = Math.Clamp(saved.Hp, 0, member.MaxHp);
                member.Mp = Math.Clamp(saved.Mp, 0, member.Stats.Mp);
                restored.Members.Add(member);
            }
            if (restored.Members.Count > 0) party = restored;
            else party = PartyState.Create(project);
        }
        else party = PartyState.Create(project);
        day = Math.Max(1, save.Day);
        dayPhase = save.Phase is "tarde" or "noche" ? save.Phase : "manana";
        activeDialogue = null; activeBattle = null; activeShop = null; commandQueue.Clear();
        cutsceneWait = 0; cutsceneMover = null; cutsceneMoverEventId = ""; cutsceneSteps.Clear();
        camPanning = false; camOverride = false; emotes.Clear(); worldVfx.Clear(); battleVfx.Clear(); worldFloats.Clear(); battlePops.Clear(); weatherOverride = null; dayCardTimer = 0;
        itemGetItem = null; itemGetTime = 0;
        RebuildFollowers();
    }

    /// <summary>Contexto por frame para el editor visual: mundo visible + sesion de comandos.
    /// Cada adopcion del editor queda anotada en la bitacora de co-autoria como [vos].</summary>
    EditorContext EditorCtx() => new(project, map, tileset, camera, font, theme, screen!, flags, player,
        project.Render.TileSize, session,
        (fresh, msg, note) => { Reload(fresh, msg); if (note != null) session?.Note(UiStrings.LogHuman, note); },
        msg => SetMessage(msg, 4f), sfx, tileBank, spriteBank,
        (cx, cy) => { editorCamX = cx; editorCamY = cy; }, // click en el minimapa: saltar la camara
        id => StartScrub(id)); // EVENTOS + Enter: reproducir la cutscene del evento en el scrubber

    /// <summary>Adopta un proyecto recargado en caliente preservando posicion, flags e inventario.</summary>
    void Reload(GameProject fresh, string? message = null)
    {
        project = fresh;
        theme = UiTheme.Resolve(project);
        map = project.Maps.FirstOrDefault(x => x.Id == map.Id) ?? project.Maps.First(x => x.Id == project.StartMapId);
        tileset = project.Tilesets.First(x => x.Id == map.TilesetId);
        player.Teleport(Math.Clamp(player.TileX, 0, map.Width - 1), Math.Clamp(player.TileY, 0, map.Height - 1));

        // Flags: conservar valores actuales, sumar defaults de variables nuevas.
        foreach (var variable in project.Variables.Where(v => v.Kind == VariableKind.Flag))
            if (!flags.ContainsKey(variable.Id)) flags[variable.Id] = variable.Default.Equals("true", StringComparison.OrdinalIgnoreCase);

        routineDirection.Clear();
        npcMovers.Clear();
        foreach (var ev in project.Events)
        {
            routineDirection[ev.Id] = 1;
            var mover = new GridMover { SecondsPerTile = 0.3f };
            mover.Teleport(ev.X, ev.Y);
            npcMovers[ev.Id] = mover;
        }

        spriteBank?.Dispose();
        spriteBank = new SpriteBank(project, projectRoot);
        tileBank?.Dispose();
        tileBank = new TileBank(project, projectRoot);
        if (crt != null) crt.Enabled = fresh.Render.CrtFilter; // el contenido recargado manda tambien sobre el vidrio
        party.Rebind(fresh); // stats derivados frescos, nivel/EXP/HP intactos
        RebuildFollowers(); // la party pudo cambiar de tamano (un actor borrado sale de la fila)
        // El audio tambien adopta el proyecto fresco: sin esto, una cancion o un SFX
        // creados en vivo via MCP no existian para los reproductores (bug real encontrado jugando).
        music?.Rebind(fresh);
        legacyMusic?.Rebind(fresh);
        sfx?.Dispose();
        sfx = SfxPlayer.TryCreate(fresh);
        sfx?.SetVolume(settings.SfxVolume);
        music?.Play(map.SongId);
        legacyMusic?.Play(map.SongId);
        SetMessage(message ?? "Proyecto recargado en caliente.");
    }

    void SetMessage(string text, float seconds = 3f)
    {
        message = text;
        messageTimer = seconds;
    }

    string ItemName(string id) => project.Items.FirstOrDefault(x => x.Id == id)?.Name ?? id;

    void Draw()
    {
        var s = screen!;
        s.BeginVirtual();
        DrawVirtual();
        s.EndVirtual();
        // Editando se apaga el vidrio (pixeles y mouse exactos) y la UI del editor se dibuja como
        // overlay a resolucion NATIVA de ventana: el juego sigue retro, la herramienta se lee.
        // El HUD del scrubber usa la misma capa nativa, pero conserva el vidrio: es playback.
        s.Present(editor.Visible ? null : crt,
            editor.Visible ? () => editor.DrawUi(EditorCtx())
            : scrubActive ? () => EditorMode.DrawScrubUi(ScrubHud())
            : null);
    }

    /// <summary>Portada opcional del titulo (render.titleImage): se carga una vez, con filtro
    /// Point para que el pixel art quede nitido al escalar. Sin imagen (o si no carga), el
    /// titulo cae a su presentacion tipografica de siempre.</summary>
    Texture2D? EnsureTitleBg()
    {
        if (titleBgLoaded) return titleBg;
        titleBgLoaded = true;
        var bytes = ImageAssets.LoadBytes(project, projectRoot, project.Render.TitleImage);
        if (bytes == null) return titleBg;
        var img = Raylib.LoadImageFromMemory(".png", bytes);
        if (img.Width <= 0) return titleBg;
        var tex = Raylib.LoadTextureFromImage(img);
        Raylib.UnloadImage(img);
        Raylib.SetTextureFilter(tex, TextureFilter.Point);
        titleBg = tex;
        return titleBg;
    }

    void DrawVirtual()
    {
        Raylib.ClearBackground(new Color(24, 24, 32, 255));
        if (splash != null)
        {
            splash.Draw(font, screen!.Width, screen.Height);
        }
        else if (gameOver)
        {
            Raylib.ClearBackground(Color.Black);
            var text = UiStrings.GameOver;
            font.DrawShadowed(text, (screen!.Width - font.Measure(text)) / 2, screen.Height / 2 - 12, new Color(200, 40, 50, 255), theme.ShadowColor);
            if (((int)(time * 2)) % 2 == 0) // parpadeo lento clasico
            {
                var hint = "Enter: volver al titulo";
                font.DrawShadowed(hint, (screen.Width - font.Measure(hint)) / 2, screen.Height / 2 + 14, theme.TextColor, theme.ShadowColor);
            }
        }
        else if (title != null)
        {
            // Fondo vivo del titulo (render.titleVfxId, kind background): la luz del farol
            // latiendo detras del menu, en vez de la portada estatica. El VFX tiene prioridad
            // sobre titleImage; si no hay ninguno, TitleScreen cae al marco sobrio.
            if (FindVfx(project.Render.TitleVfxId) is { Kind: "background" } titleVfx)
            {
                Raylib.ClearBackground(new Color(8, 8, 14, 255));
                VfxRenderer.DrawBackground(titleVfx, time, screen!.Width, screen.Height);
                title.Draw(font, theme, screen.Width, screen.Height, time, null, vfxBackdrop: true);
            }
            else title.Draw(font, theme, screen!.Width, screen.Height, time, EnsureTitleBg());
        }
        else
        {
            var (sx, sy) = shake.Offset;
            if (shake.Active) Raylib.BeginMode2D(new Camera2D { Offset = new System.Numerics.Vector2(sx, sy), Target = new System.Numerics.Vector2(0, 0), Rotation = 0, Zoom = 1 });
            if (activeBattle != null) DrawBattle(activeBattle); else DrawWorld();
            if (shake.Active) Raylib.EndMode2D();
            // La ceremonia se dibuja solo cuando es EL estado activo: un ShowItemGet disparado
            // como efecto de dialogo espera (invisible) a que el dialogo cierre.
            if (activeBattle == null && activeDialogue == null && activeShop == null && itemGetItem != null) DrawItemGet(itemGetItem);
        }
        // Placa de dia (AdvanceTime): bandas negras y el titulo del momento, como un
        // intertitulo de novela. Se desvanece al final.
        if (dayCardTimer > 0 && title == null && splash == null && !gameOver)
        {
            var alpha = (byte)(255 * Math.Min(1f, dayCardTimer / 0.6f));
            var w = screen!.Width;
            var h = screen.Height;
            var amber = new Color((byte)240, (byte)214, (byte)130, alpha);
            var amberDim = new Color((byte)150, (byte)120, (byte)60, alpha);
            Raylib.DrawRectangle(0, h / 2 - 24, w, 48, new Color((byte)0, (byte)0, (byte)0, Math.Min((byte)210, alpha)));
            var tw = font.Measure(dayCardText) * 2;
            // Lineas ornamentales de capitulo, con diamantes en las puntas.
            Raylib.DrawRectangle((w - tw) / 2 - 26, h / 2 - 14, tw + 52, 1, amberDim);
            Raylib.DrawRectangle((w - tw) / 2 - 26, h / 2 + 13, tw + 52, 1, amberDim);
            foreach (var dx in new[] { (w - tw) / 2 - 30, (w + tw) / 2 + 28 })
            {
                Raylib.DrawRectangle(dx, h / 2 - 15, 1, 3, amberDim);
                Raylib.DrawRectangle(dx - 1, h / 2 - 14, 3, 1, amberDim);
                Raylib.DrawRectangle(dx, h / 2 + 12, 1, 3, amberDim);
                Raylib.DrawRectangle(dx - 1, h / 2 + 13, 3, 1, amberDim);
            }
            font.DrawShadowed(dayCardText, (w - tw) / 2, h / 2 - 8, amber, new Color((byte)0, (byte)0, (byte)0, alpha), 2);
        }
        transition.DrawOverlay(screen!.Width, screen.Height);
    }

    void DrawWorld()
    {
        var s = screen!;
        var ts = project.Render.TileSize;
        // Editando, la camara es libre (pan por todo el mapa) y el zoom out del editor agranda
        // la ventana al mundo (viewW/H * ZoomDiv); jugando, sigue al foco a 1x.
        var zd = editor.Visible ? editor.ZoomDiv : 1;
        if (editor.Visible) camera.SetCorner(editorCamX, editorCamY, map.Width * ts, map.Height * ts, s.Width * zd, s.Height * zd, zd);
        else { var (focusX, focusY) = CameraFocus(); camera.Follow(focusX, focusY, map.Width * ts, map.Height * ts, s.Width, s.Height); }

        // Solo se dibujan los tiles visibles: la camara define la ventana al mundo.
        var x0 = Math.Max(0, camera.X / ts);
        var y0 = Math.Max(0, camera.Y / ts);
        var x1 = Math.Min(map.Width - 1, (camera.X + s.Width * zd) / ts + 1);
        var y1 = Math.Min(map.Height - 1, (camera.Y + s.Height * zd) / ts + 1);
        // El mundo se dibuja igual que siempre (coords mundo - camara); el zoom out del editor
        // solo lo envuelve en una Camera2D que escala. Cero cambios en las llamadas de dibujo.
        if (zd > 1) Raylib.BeginMode2D(new Camera2D { Zoom = 1f / zd, Offset = Vector2.Zero, Target = Vector2.Zero, Rotation = 0 });
        for (var y = y0; y <= y1; y++) for (var x = x0; x <= x1; x++)
        {
            var tileId = map.Tiles[y * map.Width + x];
            var tileDef = tileset.Tiles.FirstOrDefault(t => t.Id == tileId);
            var tileTint = Tints.Parse(tileDef?.Tint); // re-teñido del tile (default blanco)
            // Atlas PNG si el tileset lo trae (metatiles estilo Onett); color plano como fallback.
            // AnimCell: los tiles con Frames ciclan celdas al reloj del tileset (agua SNES).
            if (tileBank != null && tileBank.TryDraw(tileset, TileBank.AnimCell(tileDef, tileId, tileset.AnimMs), new Rectangle(x * ts - camera.X, y * ts - camera.Y, ts, ts), tileTint, MapOps.FlagsAt(map, y * map.Width + x))) continue;
            TileFallback.Draw(tileDef, new Rectangle(x * ts - camera.X, y * ts - camera.Y, ts, ts), tileTint);
        }

        // Orden de dibujo por Y (profundidad clasica): quien esta mas abajo tapa a quien esta mas arriba.
        var drawables = new List<(float Depth, Action Draw)>();
        foreach (var ev in project.Events.Where(x => x.MapId == map.Id && (EventPresent(x) || editor.Visible)))
        {
            var mover = npcMovers[ev.Id];
            // Offset de pixel: posicion libre de props decorativos (0 = pegado a la grilla).
            var px = (int)mover.PixelX(ts) - camera.X + ev.OffsetX;
            var py = (int)mover.PixelY(ts) - camera.Y + ev.OffsetY;
            var evTint = Tints.Parse(ev.Tint); // re-teñido por objeto colocado (default blanco)
            var evScale = ev.Scale > 0.01f ? ev.Scale : 1f; // 0 = escala 1 (default)
            // Evento sin sprite: NADA en el juego; en el editor se marca la celda. Se distingue por
            // color para no confundir un EVENTO invisible con un MURO invisible: ROSA si hace logica
            // (tiene comandos, o es NPC/trigger/cutscene), ROJO si es solo un bloqueo de colision.
            // El placeholder de DrawCharacter queda para sprites que no cargan, no para el sprite vacio.
            if (string.IsNullOrEmpty(ev.Sprite))
            {
                // La celda marca el TILE del evento (su casilla de interaccion/colision), SIN el offset:
                // el offset posiciona un sprite libremente, pero un evento invisible no tiene sprite que
                // correr. Con offset se dibujaba una segunda celda corrida que se veia como "2 grillas".
                var bx = (int)mover.PixelX(ts) - camera.X;
                var by = (int)mover.PixelY(ts) - camera.Y;
                var logic = ev.Kind != EventKind.Object || ev.Pages.Any(pg => pg.Commands.Count > 0);
                var cell = logic ? new Color(255, 60, 170, 90) : new Color(200, 60, 70, 90);
                if (editor.Visible) drawables.Add((mover.PixelY(ts), () => Raylib.DrawRectangle(bx, by, ts, ts, cell)));
                continue;
            }
            drawables.Add((mover.PixelY(ts), () => DrawCharacter(ev.Sprite, mover, px, py, ts,
                ev.Kind == EventKind.Trigger ? new Color(180, 50, 70, 255) : new Color(45, 80, 180, 255), evTint, evScale)));
        }
        // Followers de party (la fila detras del lider): cada miembro no-lider con su
        // sprite por convencion actor.X -> sprite.X, apoyado en su mover del trail. El Y-sort
        // los intercala natural con NPCs y el lider.
        for (var i = 1; i < party.Members.Count && i - 1 < followerMovers.Count; i++)
        {
            var fm = followerMovers[i - 1];
            var followerSprite = FollowerSpriteId(party.Members[i]);
            var fpx = (int)fm.PixelX(ts) - camera.X;
            var fpy = (int)fm.PixelY(ts) - camera.Y;
            drawables.Add((fm.PixelY(ts), () => DrawCharacter(followerSprite, fm, fpx, fpy, ts, new Color(120, 180, 120, 255))));
        }
        var playerPx = (int)player.PixelX(ts) - camera.X;
        var playerPy = (int)player.PixelY(ts) - camera.Y;
        var playerSpriteId = string.IsNullOrWhiteSpace(project.PlayerSpriteId) ? DefaultAssets.PlayerSpriteId : project.PlayerSpriteId;
        drawables.Add((player.PixelY(ts), () => DrawCharacter(playerSpriteId, player, playerPx, playerPy, ts, new Color(245, 225, 90, 255))));
        foreach (var (_, draw) in drawables.OrderBy(d => d.Depth)) draw();
        DrawEmotes(ts);
        DrawWorldFloats(ts);
        // El clima cae SOBRE el mundo y bajo la UI. Editando se apaga: la herramienta primero
        // (la lluvia sobre la grilla es ruido; se ve al cerrar el editor, o en una captura).
        // null = clima autorado del mapa; "" (SetWeather despejo) resuelve a nada.
        if (!editor.Visible && FindVfx(weatherOverride ?? map.WeatherVfxId) is { Kind: "weather" } weather)
            VfxRenderer.DrawWeather(weather, time, screen!.Width, screen.Height, camera.X, camera.Y);
        DrawWorldVfx(ts);
        if (zd > 1) Raylib.EndMode2D();

        // Tinte ambiental por franja: la tarde bana el mundo en ambar de atardecer, la
        // noche en azul. Multiplicativo (no ensucia el alfa del render texture) y ANTES
        // de la UI: las ventanas y el texto quedan limpios.
        if (dayPhase != "manana")
        {
            Raylib.BeginBlendMode(BlendMode.Multiplied);
            var ambient = dayPhase == "tarde" ? new Color(255, 202, 158, 255) : new Color(118, 130, 188, 255);
            Raylib.DrawRectangle(0, 0, s.Width, s.Height, ambient);
            Raylib.EndBlendMode();
        }

        // Reloj de mundo (si el proyecto lo activa): dia y franja, arriba a la derecha.
        if (project.Render.ShowDayClock && !editor.Visible)
        {
            var clock = UiStrings.DayPlate(day, UiStrings.PhaseLabel(dayPhase));
            var cw = font.Measure(clock);
            Raylib.DrawRectangle(s.Width - cw - 11, 4, cw + 8, 13, new Color(10, 12, 20, 255));
            Raylib.DrawRectangle(s.Width - cw - 11, 17, cw + 8, 1, new Color(120, 96, 40, 255));
            font.Draw(clock, s.Width - cw - 7, 7, new Color(232, 200, 110, 255)); // ambar de vela
        }

        editor.Draw(EditorCtx());

        if (paused) DrawPause();
        else if (activeShop != null) DrawShop(activeShop);
        else if (activeDialogue != null) DrawDialogueBox(activeDialogue);
        else if (messageTimer > 0) DrawMessageBox(message);
    }

    /// <summary>VFX de impacto disparados por PlayVfx en el mundo: anclados al jugador o a un
    /// evento (misma resolucion que los emotes), con el origen al centro del sprite.</summary>
    void DrawWorldVfx(int ts)
    {
        foreach (var (key, (def, t)) in worldVfx)
        {
            GridMover? mover = key == "player" ? player : npcMovers.TryGetValue(key, out var m) ? m : null;
            if (mover == null) continue;
            if (key != "player")
            {
                var ev = project.Events.FirstOrDefault(e => e.Id == key);
                if (ev == null || ev.MapId != map.Id) continue;
                // A proposito NO se corta si el evento dejo de estar presente: un teleport-out
                // (PlayVfx + SetVariable que apaga su pagina) termina el fade sobre su ultimo lugar.
            }
            var ox = (int)mover.PixelX(ts) - camera.X + ts / 2;
            var oy = (int)mover.PixelY(ts) - camera.Y + ts / 2;
            VfxRenderer.DrawImpact(def, t, ox, oy, screen!.Width, screen.Height);
        }
    }

    /// <summary>Globos de emote flotando sobre las cabezas: burbuja blanca con borde, cola,
    /// icono adentro y una respiracion vertical de 1px. Todo procedural.</summary>
    void DrawEmotes(int ts)
    {
        foreach (var (key, (icon, _)) in emotes)
        {
            GridMover? mover = key == "player" ? player : npcMovers.TryGetValue(key, out var m) ? m : null;
            if (mover == null) continue;
            if (key != "player")
            {
                var ev = project.Events.FirstOrDefault(e => e.Id == key);
                if (ev == null || ev.MapId != map.Id || !EventPresent(ev)) continue;
            }
            var bob = (int)(MathF.Sin(time * 5f) * 1.5f);
            var bx = (int)mover.PixelX(ts) - camera.X + ts / 2 - 8;
            var by = (int)mover.PixelY(ts) - camera.Y - 15 + bob;
            var border = new Color(26, 28, 44, 255);
            var paper = new Color(248, 246, 235, 255);
            Raylib.DrawRectangle(bx + 1, by, 14, 12, paper);
            Raylib.DrawRectangle(bx, by + 1, 16, 10, paper);
            Raylib.DrawRectangle(bx + 1, by - 1, 14, 1, border);
            Raylib.DrawRectangle(bx + 1, by + 12, 14, 1, border);
            Raylib.DrawRectangle(bx - 1, by + 1, 1, 10, border);
            Raylib.DrawRectangle(bx + 16, by + 1, 1, 10, border);
            Raylib.DrawRectangle(bx + 3, by + 12, 2, 2, paper); // la cola del globo
            Raylib.DrawRectangle(bx + 2, by + 14, 2, 1, border);
            DrawEmoteIcon(icon, bx, by, border);
        }
    }

    void DrawEmoteIcon(string icon, int bx, int by, Color ink)
    {
        switch (icon)
        {
            case "!": font.Draw("!", bx + 7, by + 2, new Color(180, 50, 60, 255)); break;
            case "?": font.Draw("?", bx + 5, by + 2, new Color(40, 90, 170, 255)); break;
            case "zzz":
                font.Draw("Z", bx + 3, by + 3, ink);
                font.Draw("z", bx + 9, by + 1, ink);
                break;
            case "nota":
                Raylib.DrawRectangle(bx + 9, by + 2, 1, 7, ink);
                Raylib.DrawRectangle(bx + 10, by + 2, 3, 2, ink);
                Raylib.DrawRectangle(bx + 6, by + 7, 3, 3, ink);
                break;
            case "puntos":
                for (var i = 0; i < 3; i++) Raylib.DrawRectangle(bx + 3 + i * 4, by + 8, 2, 2, ink);
                break;
            case "corazon":
                var red = new Color(200, 60, 80, 255);
                Raylib.DrawRectangle(bx + 4, by + 3, 3, 3, red);
                Raylib.DrawRectangle(bx + 9, by + 3, 3, 3, red);
                Raylib.DrawRectangle(bx + 4, by + 5, 8, 3, red);
                Raylib.DrawRectangle(bx + 6, by + 8, 4, 2, red);
                Raylib.DrawRectangle(bx + 7, by + 10, 2, 1, red);
                break;
        }
    }

    /// <summary>La ceremonia del item clave: el mundo se oscurece (multiplicativo, no ensucia
    /// el alfa del render texture), rayos dorados giran lento detras del sprite (aditivo,
    /// la leccion de la placa del motor) y el nombre + descripcion presentan al objeto.
    /// El mundo se detiene alrededor del objeto; no es el item alzado sobre la cabeza.</summary>
    void DrawItemGet(ItemDef item)
    {
        var s = screen!;
        var w = s.Width;
        var h = s.Height;
        var t = Math.Min(1f, itemGetTime / 0.45f);
        t = t * t * (3f - 2f * t); // smoothstep: la ceremonia respira al entrar
        var cx = w / 2;
        var cy = h / 2 - 34;

        // Oscurecer el mundo alrededor del objeto (queda apenas legible, como un recuerdo).
        var dim = (byte)(255 - (int)(175 * t));
        Raylib.BeginBlendMode(BlendMode.Multiplied);
        Raylib.DrawRectangle(0, 0, w, h, new Color(dim, dim, (byte)Math.Min(255, dim + 14), (byte)255));
        Raylib.EndBlendMode();

        // Detras del sprite: halo caliente + dos coronas de rayos girando a velocidades
        // distintas (la de atras larga y tenue, la de adelante corta y viva). Todo aditivo.
        Raylib.BeginBlendMode(BlendMode.Additive);
        Raylib.DrawCircleGradient(new System.Numerics.Vector2(cx, cy), 52 * t, new Color((byte)70, (byte)54, (byte)18, (byte)255), new Color((byte)0, (byte)0, (byte)0, (byte)0));
        for (var i = 0; i < 10; i++)
        {
            var a = time * 0.35f + i * MathF.Tau / 10f;
            var tip = new System.Numerics.Vector2(cx + MathF.Cos(a) * 96 * t, cy + MathF.Sin(a) * 96 * t);
            Raylib.DrawLineEx(new System.Numerics.Vector2(cx, cy), tip, 9f, new Color((byte)34, (byte)26, (byte)8, (byte)255));
        }
        for (var i = 0; i < 8; i++)
        {
            var a = -time * 0.55f + i * MathF.Tau / 8f;
            var tip = new System.Numerics.Vector2(cx + MathF.Cos(a) * 52 * t, cy + MathF.Sin(a) * 52 * t);
            Raylib.DrawLineEx(new System.Numerics.Vector2(cx, cy), tip, 6f, new Color((byte)52, (byte)40, (byte)12, (byte)255));
        }
        // Destellos que titilan alrededor, cada uno con su fase (deterministas, sin RNG).
        for (var i = 0; i < 6; i++)
        {
            var a = i * MathF.Tau / 6f + 0.5f;
            var blink = MathF.Sin(time * 3f + i * 1.7f);
            if (blink <= 0.45f) continue;
            var px = (int)(cx + MathF.Cos(a) * 40 * t);
            var py = (int)(cy + MathF.Sin(a) * 34 * t);
            var spark = new Color((byte)120, (byte)110, (byte)70, (byte)255);
            Raylib.DrawRectangle(px - 2, py, 5, 1, spark);
            Raylib.DrawRectangle(px, py - 2, 1, 5, spark);
        }
        Raylib.EndBlendMode();

        // El sprite del item, 3x y flotando apenas (spriteId explicito, o la convencion
        // item.X -> sprite.X como los enemigos; sin sprite, un destello dorado generico).
        var bob = (int)(MathF.Sin(time * 2f) * 2f);
        var candidate = string.IsNullOrWhiteSpace(item.SpriteId) ? "sprite." + item.Id.Replace("item.", "") : item.SpriteId;
        var spriteId = project.Sprites.FirstOrDefault(x => x.Id == candidate)?.Id;
        var drew = false;
        if (spriteBank != null && spriteId != null)
        {
            var (sw, sh) = spriteBank.SizeOf(spriteId);
            if (sw > 0) drew = spriteBank.TryDrawScaled(spriteId, Facing.Down, 0, cx - sw * 3 / 2, cy - sh * 3 / 2 + bob, 3, Color.White);
        }
        if (!drew)
        {
            var gold = new Color(240, 214, 130, 255);
            Raylib.DrawPoly(new System.Numerics.Vector2(cx, cy + bob), 4, 14, 45f, gold);
            Raylib.DrawPoly(new System.Numerics.Vector2(cx, cy + bob), 4, 6, 45f, Color.White);
        }

        // Nombre y descripcion, con la tipografia ceremonial de la placa de dia.
        var alpha = (byte)(255 * t);
        var amber = new Color((byte)240, (byte)214, (byte)130, alpha);
        var name = itemGetCount > 1 ? $"{item.Name} x{itemGetCount}" : item.Name;
        font.DrawShadowed(name, cx - font.Measure(name), cy + 40 + bob / 2, amber, new Color((byte)0, (byte)0, (byte)0, alpha), 2);
        var dy = cy + 62;
        foreach (var line in WrapText(item.Description, w - 64))
        {
            font.DrawShadowed(line, cx - font.Measure(line) / 2, dy, new Color((byte)230, (byte)226, (byte)210, alpha), new Color((byte)0, (byte)0, (byte)0, alpha));
            dy += 11;
        }
        if (itemGetTime > 0.6f) WindowBox.DrawContinueArrow(theme, cx, dy + 10, time);
    }

    /// <summary>Deja solo las ultimas maxLines lineas de un texto ya envuelto (log que crece).</summary>
    static string ClampLines(string wrapped, int maxLines)
    {
        var lines = wrapped.Split('\n');
        return lines.Length <= maxLines ? wrapped : string.Join('\n', lines.Skip(lines.Length - maxLines));
    }

    /// <summary>Corta el texto en lineas que entren en maxWidth pixeles (por palabra).</summary>
    List<string> WrapText(string text, int maxWidth)
    {
        var lines = new List<string>();
        if (string.IsNullOrWhiteSpace(text)) return lines;
        var line = "";
        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var probe = line == "" ? word : line + " " + word;
            if (font.Measure(probe) > maxWidth && line != "") { lines.Add(line); line = word; }
            else line = probe;
        }
        if (line != "") lines.Add(line);
        return lines;
    }

    /// <summary>Menu de pausa clasico: mundo oscurecido detras, secciones a la
    /// izquierda, panel de detalle a la derecha y la plata siempre visible abajo.</summary>
    void DrawPause()
    {
        var s = screen!;
        Raylib.DrawRectangle(0, 0, s.Width, s.Height, new Color(0, 0, 0, 130));

        WindowBox.Draw(theme, 8, 10, 78, 110);
        for (var i = 0; i < PauseSections.Length; i++)
        {
            var color = i == pauseSection ? theme.AccentColor : theme.TextColor;
            if (i == pauseSection && !pauseInPanel) WindowBox.DrawCursor(theme, 14, 19 + i * 14);
            font.DrawShadowed(UiStrings.PauseLabels[i], 24, 18 + i * 14, color, theme.ShadowColor);
        }

        WindowBox.Draw(theme, 8, 124, 78, 24);
        font.DrawShadowed($"${money}", 16, 131, theme.TextColor, theme.ShadowColor);

        WindowBox.Draw(theme, 92, 10, s.Width - 100, 148);
        switch (PauseSections[pauseSection])
        {
            case "ITEMS":
                var items = GroupedInventory();
                if (items.Count == 0) font.DrawShadowed("No tenes items.", 104, 20, theme.TextColor, theme.ShadowColor);
                for (var i = 0; i < items.Count && i < 10; i++)
                {
                    var color = pauseInPanel && i == pauseRow ? theme.AccentColor : theme.TextColor;
                    if (pauseInPanel && i == pauseRow) WindowBox.DrawCursor(theme, 98, 21 + i * 13);
                    font.DrawShadowed($"{items[i].Name}  x{items[i].Count}", 106, 20 + i * 13, color, theme.ShadowColor);
                }
                break;
            case "ESTADO":
                var y = 20;
                foreach (var member in party.Members.Take(3))
                {
                    font.DrawShadowed(UiStrings.Level(member.Def.Name, member.Level), 104, y, theme.AccentColor, theme.ShadowColor);
                    font.DrawShadowed($"HP {member.Hp}/{member.MaxHp}  MP {member.Stats.Mp}", 104, y + 12, theme.TextColor, theme.ShadowColor);
                    font.DrawShadowed(UiStrings.Stats(member.Stats.Attack, member.Stats.Defense, member.Stats.Speed), 104, y + 24, theme.TextColor, theme.ShadowColor);
                    font.DrawShadowed(UiStrings.ExpToNext(member.Exp, PartyMember.ExpToNext(member.Level)), 104, y + 36, theme.TextColor, theme.ShadowColor);
                    y += 52;
                }
                font.DrawShadowed(UiStrings.MapLabel(map.Name), 104, y, theme.TextColor, theme.ShadowColor);
                break;
            case "EQUIPO":
                var ey = 20;
                var equipRow = 0;
                foreach (var member in party.Members.Take(3))
                {
                    font.DrawShadowed($"{member.Def.Name}  Atk {member.Stats.Attack} Def {member.Stats.Defense}", 104, ey, theme.AccentColor, theme.ShadowColor);
                    ey += 12;
                    foreach (var (label, item) in new[] { (UiStrings.Weapon, member.Weapon), (UiStrings.Armor, member.Armor) })
                    {
                        var selected = pauseInPanel && equipRow == pauseRow;
                        if (selected) WindowBox.DrawCursor(theme, 98, ey + 1);
                        font.DrawShadowed($"{label}: {item?.Name ?? "--"}{BonusText(item)}", 106, ey, selected ? theme.AccentColor : theme.TextColor, theme.ShadowColor);
                        ey += 12;
                        equipRow++;
                    }
                    ey += 4;
                }
                font.DrawShadowed("Enter: cambiar equipo", 104, ey, new Color(150, 150, 165, 255), theme.ShadowColor);
                break;
            case "OPCIONES":
                var options = new[] { (UiStrings.OptMusic, settings.MusicVolume), (UiStrings.OptSounds, settings.SfxVolume) };
                for (var i = 0; i < options.Length; i++)
                {
                    var (label, value) = options[i];
                    var oy = 24 + i * 22;
                    var selected = pauseInPanel && i == pauseRow;
                    if (selected) WindowBox.DrawCursor(theme, 98, oy + 1);
                    font.DrawShadowed(label, 106, oy, selected ? theme.AccentColor : theme.TextColor, theme.ShadowColor);
                    // Barra de 10 segmentos estilo 90s; a cero muestra "off".
                    var barX = 158;
                    Raylib.DrawRectangle(barX - 1, oy + 1, 62, 8, theme.ShadowColor);
                    if (value > 0) Raylib.DrawRectangle(barX, oy + 2, (int)(60 * value), 6, theme.AccentColor);
                    var pct = value > 0 ? $"{(int)Math.Round(value * 100)}%" : "off";
                    font.DrawShadowed(pct, barX + 66, oy, theme.TextColor, theme.ShadowColor);
                }
                font.DrawShadowed("Izq/Der: ajustar", 104, 76, new Color(150, 150, 165, 255), theme.ShadowColor);
                font.DrawShadowed("(se guarda solo)", 104, 88, new Color(150, 150, 165, 255), theme.ShadowColor);
                break;
            case "GUARDAR" or "CARGAR":
                for (var slot = 0; slot < 3; slot++)
                {
                    var save = saveSystem.TryLoad(slot);
                    var label = save == null
                        ? UiStrings.SlotEmpty(slot + 1)
                        : $"Slot {slot + 1}  {project.Maps.FirstOrDefault(m => m.Id == save.MapId)?.Name ?? save.MapId}  {save.SavedAt.ToLocalTime():dd/MM HH:mm}";
                    var color = pauseInPanel && slot == pauseRow ? theme.AccentColor : theme.TextColor;
                    if (pauseInPanel && slot == pauseRow) WindowBox.DrawCursor(theme, 98, 23 + slot * 16);
                    font.DrawShadowed(label, 106, 22 + slot * 16, color, theme.ShadowColor);
                }
                break;
            case "SALIR":
                font.DrawShadowed("Enter: cerrar el juego.", 104, 20, theme.TextColor, theme.ShadowColor);
                break;
        }

        WindowBox.Draw(theme, 8, s.Height - 26, s.Width - 16, 20);
        var footer = pauseNote != "" ? pauseNote : pauseInPanel ? UiStrings.PauseFooterInPanel : UiStrings.PauseFooter;
        font.DrawShadowed(footer, 16, s.Height - 20, pauseNote != "" ? theme.AccentColor : new Color(150, 150, 165, 255), theme.ShadowColor);
    }

    /// <summary>UI de tienda clasica: titulo con dinero, lista con precios y cuantos tenes, log abajo.</summary>
    void DrawShop(ShopSession shop)
    {
        var s = screen!;

        WindowBox.Draw(theme, 4, 4, s.Width - 8, 26);
        font.DrawShadowed(shop.Shop.Name, 12, 9, theme.AccentColor, theme.ShadowColor);
        var moneyText = $"${shop.Money}";
        font.DrawShadowed(moneyText, s.Width - 12 - font.Measure(moneyText), 9, theme.TextColor, theme.ShadowColor);
        var mode = shop.Selling ? UiStrings.ShopSell : UiStrings.ShopBuy;
        font.DrawShadowed(mode, (s.Width - font.Measure(mode)) / 2, 19, theme.AccentColor, theme.ShadowColor);

        WindowBox.Draw(theme, 4, 32, s.Width - 8, 126);
        if (shop.Rows.Count == 0)
        {
            font.DrawShadowed(shop.Selling ? "No tenes nada para vender." : "No hay nada en venta.", 16, 42, theme.TextColor, theme.ShadowColor);
        }
        for (var i = 0; i < shop.Rows.Count && i < 8; i++)
        {
            var row = shop.Rows[i];
            var y = 39 + i * 15;
            var color = i == shop.SelectedIndex ? theme.AccentColor : theme.TextColor;
            if (i == shop.SelectedIndex) WindowBox.DrawCursor(theme, 8, y + 3);
            // Icono del item si lo tiene (las armas se VEN, no solo se leen); si no, la
            // columna queda vacia y el nombre arranca igual (margen consistente).
            var item = project.Items.FirstOrDefault(x => x.Id == row.ItemId);
            if (item != null && !string.IsNullOrWhiteSpace(item.SpriteId) && spriteBank != null)
                spriteBank.TryDraw(item.SpriteId, Facing.Down, 0, 16, y - 4, Color.White);
            font.DrawShadowed(row.Name, 34, y, color, theme.ShadowColor);
            if (row.Owned > 0) font.DrawShadowed($"x{row.Owned}", 150, y, color, theme.ShadowColor);
            var price = $"${(shop.Selling ? ShopSession.SellValue(row.Price) : row.Price)}";
            font.DrawShadowed(price, s.Width - 16 - font.Measure(price), y, color, theme.ShadowColor);
        }

        WindowBox.Draw(theme, 4, 162, s.Width - 8, 44);
        font.DrawShadowed(font.WrapPixels(shop.Log, s.Width - 28), 12, 168, theme.TextColor, theme.ShadowColor);
        font.DrawShadowed("Enter: elegir  Izq/Der: modo  Retro: salir", 12, 192, new Color(150, 150, 165, 255), theme.ShadowColor);
    }

    /// <summary>Dibuja un personaje con su sprite (anclado a los pies de la casilla) o un rectangulo placeholder.</summary>
    void DrawCharacter(string spriteId, GridMover mover, int px, int py, int ts, Color placeholder, Color? tint = null, float scale = 1f)
    {
        var bank = spriteBank!;
        if (!string.IsNullOrWhiteSpace(spriteId))
        {
            var frame = mover.AnimFrame(bank.FrameCount(spriteId, mover.Facing));
            var (w, h) = bank.SizeOf(spriteId);
            if (w > 0)
            {
                if (Math.Abs(scale - 1f) < 0.01f)
                {
                    if (bank.TryDraw(spriteId, mover.Facing, frame, px + (ts - w) / 2, py + ts - h, tint ?? Color.White)) return;
                }
                else
                {
                    var dw = w * scale; var dh = h * scale; // redimensionado, anclado a los pies
                    if (bank.TryDrawScaledF(spriteId, mover.Facing, frame, px + (ts - dw) / 2f, py + ts - dh, scale, tint ?? Color.White)) return;
                }
            }
        }
        Raylib.DrawRectangle(px + 3, py + 1, ts - 6, ts - 2, placeholder);
    }

    void DrawBattle(BattleEngine battle)
    {
        var s = screen!;
        Raylib.ClearBackground(new Color(32, 28, 44, 255));
        // Fondo de batalla animado (BattleDef.BackgroundVfxId): patrones que ondulan, scrollean
        // y ciclan colores. Funcion pura del tiempo, loopea mientras dure el combate.
        if (FindVfx(battle.Battle.BackgroundVfxId) is { Kind: "background" } bgVfx)
            VfxRenderer.DrawBackground(bgVfx, time, s.Width, s.Height);

        // Enemigos vivos repartidos al centro en la FRANJA SUPERIOR LIBRE (vista frontal
        // frontal). Los menus viven abajo, asi que aca nada los tapa. Sprites 3x
        // apoyados en la linea de suelo enemyBaseline.
        const int enemyBaseline = 106;
        var alive = battle.AliveEnemyIndexes;
        var (spacing, startX) = EnemyLayout(alive.Count, s.Width);
        var targetedSlot = battle.Current == BattleEngine.Phase.TargetSelect && battle.Targeting == BattleEngine.TargetMode.Enemies
            ? Math.Clamp(battle.SelectedTarget, 0, Math.Max(0, alive.Count - 1)) : -1;
        for (var slot = 0; slot < alive.Count; slot++)
        {
            var index = alive[slot];
            var enemy = battle.Enemies[index];
            var centerX = startX + slot * spacing + spacing / 2;
            // Aura de jefe: un resplandor rojo que RESPIRA detras del enemigo (aditivo, no
            // ensucia el alfa). Da la sensacion de poder sin un solo shader.
            if (battle.Battle.Boss)
            {
                var pulse = 0.55f + 0.45f * MathF.Sin(time * 2.3f);
                Raylib.BeginBlendMode(BlendMode.Additive);
                Raylib.DrawCircleGradient(new System.Numerics.Vector2(centerX, enemyBaseline - 34), 40f + 8f * pulse,
                    new Color((byte)(78 * pulse), (byte)(14 * pulse), (byte)(30 * pulse), (byte)255), new Color((byte)0, (byte)0, (byte)0, (byte)0));
                Raylib.EndBlendMode();
            }
            DrawEnemySprite(enemy, centerX, enemyBaseline, index == enemyFlashIndex && enemyFlashTimer > 0);
            // Nombre SOLO del enemigo apuntado: nombres largos ya no
            // se enciman; el log anuncia a todos al aparecer. La flecha late sobre su cabeza.
            if (slot == targetedSlot)
            {
                font.DrawShadowed(enemy.Name, centerX - font.Measure(enemy.Name) / 2, enemyBaseline + 3, theme.AccentColor, theme.ShadowColor);
                WindowBox.DrawContinueArrow(theme, centerX, 18, time);
            }
            // El tag de estado (DOR/VEN) SI es siempre visible: es info de combate, y es corto.
            if (enemy.StatusTag != "")
                font.DrawShadowed(enemy.StatusTag, centerX - font.Measure(enemy.StatusTag) / 2, enemyBaseline + (slot == targetedSlot ? 13 : 3), theme.AccentColor, theme.ShadowColor);
        }

        // Los VFX de impacto de campo (sobre el cuerpo de un enemigo) van detras de la UI,
        // como los efectos de los 90; los anclados a paneles de party se dibujan DESPUES de
        // la fila (si no, el panel los tapa y el golpe enemigo no se ve).
        foreach (var fx in battleVfx.Where(f => f.Y < 128)) VfxRenderer.DrawImpact(fx.Def, fx.T, fx.X, fx.Y, s.Width, s.Height);

        // Numero flotante de dano: sube desde el cuerpo del golpeado y parpadea antes de irse.
        if (dmgPopTimer > 0)
        {
            var progress = 1f - dmgPopTimer / DmgPopSeconds;
            var text = dmgPopValue.ToString();
            if (dmgPopTimer > 0.25f || ((int)(time * 20)) % 2 == 0)
                font.DrawShadowed(text, dmgPopX - font.Measure(text) / 2, 70 - (int)(progress * 16), Color.White, theme.ShadowColor, 2);
        }

        // Fila de la party: HP/MP por miembro y tags de estado (VEN/DOR), justo debajo de
        // los enemigos. RollingHp aplica al primero. Eligiendo aliado (heal/revive), la
        // flecha late sobre el candidato.
        var allyPool = battle.Targeting == BattleEngine.TargetMode.Fallen ? battle.FallenPartyIndexes : battle.AlivePartyIndexes;
        var allyTarget = battle.Current == BattleEngine.Phase.TargetSelect && battle.Targeting != BattleEngine.TargetMode.Enemies && allyPool.Count > 0
            ? allyPool[Math.Clamp(battle.SelectedTarget, 0, allyPool.Count - 1)] : -1;
        for (var i = 0; i < battle.Party.Count && i < 3; i++)
        {
            var member = battle.Party[i];
            var x = 4 + i * 84;
            WindowBox.Draw(theme, x, 128, 82, 34);
            var isActing = !battle.Resolved && ReferenceEquals(battle.Acting, member);
            font.DrawShadowed(member.Name, x + 7, 132, isActing ? theme.AccentColor : theme.TextColor, theme.ShadowColor);
            var shownHp = battle.Battle.RollingHp && i == 0 ? rollingHp.Value : member.Hp;
            var hpColor = battle.Battle.RollingHp && i == 0 && rollingHp.Rolling ? theme.AccentColor : theme.TextColor;
            font.DrawShadowed($"HP {shownHp}/{member.Stats.Hp}", x + 7, 142, member.Alive ? hpColor : new Color(255, 110, 110, 255), theme.ShadowColor);
            font.DrawShadowed($"MP {member.Mp}", x + 7, 152, theme.TextColor, theme.ShadowColor);
            if (member.StatusTag != "")
                font.DrawShadowed(member.StatusTag, x + 45, 152, theme.AccentColor, theme.ShadowColor);
            if (i == allyTarget) WindowBox.DrawContinueArrow(theme, x + 41, 122, time);
        }

        // Segunda pasada de impactos: los anclados a paneles (cura, revive, golpe recibido)
        // brillan ENCIMA de la fila de party, con sus numeros "+N"/"-N" subiendo.
        foreach (var fx in battleVfx.Where(f => f.Y >= 128)) VfxRenderer.DrawImpact(fx.Def, fx.T, fx.X, fx.Y, s.Width, s.Height);
        foreach (var (text, color, t, px, py) in battlePops)
        {
            if (t > FloatSeconds - 0.35f && ((int)(time * 16)) % 2 == 1) continue; // parpadeo de salida
            font.DrawShadowed(text, px - font.Measure(text) / 2, py - (int)(t * 11f), color, theme.ShadowColor);
        }

        // Zona inferior: el menu de comandos (izquierda) convive con el log (derecha), ambos
        // siempre visibles. El submenu de skills tapa el log mientras se elige (no hace falta
        // leerlo ahi). El jugador ve que hacer Y que paso al mismo tiempo, sin swaps.
        var commanding = !battle.Resolved && battle.Acting is { IsPlayer: true };
        if (commanding)
        {
            var acting = battle.Acting!;
            WindowBox.Draw(theme, 4, 166, 82, 54);
            font.DrawShadowed(acting.Name, 10, 169, theme.AccentColor, theme.ShadowColor);
            for (var i = 0; i < BattleEngine.Commands.Length; i++)
            {
                var highlighted = i == battle.SelectedCommand;
                var color = highlighted ? theme.AccentColor : theme.TextColor;
                if (highlighted && battle.Current == BattleEngine.Phase.Command) WindowBox.DrawCursor(theme, 9, 179 + i * 8);
                font.DrawShadowed(BattleEngine.Commands[i], 16, 178 + i * 8, color, theme.ShadowColor);
            }
        }
        var logX = commanding ? 90 : 4;
        var logW = commanding ? s.Width - 94 : s.Width - 8;
        if (battle.Current == BattleEngine.Phase.SkillSelect)
        {
            // Skills en el panel derecho, sobre el log.
            var skills = battle.ActingSkills;
            WindowBox.Draw(theme, logX, 166, logW, 54);
            for (var i = 0; i < skills.Count && i < 4; i++)
            {
                var color = i == battle.SelectedSkill ? theme.AccentColor : theme.TextColor;
                if (i == battle.SelectedSkill) WindowBox.DrawCursor(theme, logX + 5, 172 + i * 11);
                font.DrawShadowed($"{skills[i].Name}  MP{skills[i].MpCost}", logX + 12, 171 + i * 11, color, theme.ShadowColor);
            }
        }
        else
        {
            WindowBox.Draw(theme, logX, 166, logW, 54);
            // Log clampeado a 4 lineas (las mas recientes): un mensaje largo nunca desborda la caja.
            font.DrawShadowed(ClampLines(font.WrapPixels(battle.Log, logW - 16), 4), logX + 8, 172, theme.TextColor, theme.ShadowColor);
            if (battle.Resolved) WindowBox.DrawContinueArrow(theme, s.Width / 2, 212, time);
        }

        // Placa de nombre del jefe al entrar: banda oscura arriba y el nombre en rojo apagado
        // (tipografia 2x), que se desvanece sola. Ominosa pero contenida, no un cartel estridente.
        if (bossIntroTimer > 0 && battle.Battle.Boss && battle.Enemies.Count > 0)
        {
            var a = (byte)(255 * Math.Min(1f, bossIntroTimer / 0.5f));
            var name = battle.Enemies[0].Name;
            Raylib.DrawRectangle(0, 4, s.Width, 26, new Color((byte)0, (byte)0, (byte)0, (byte)Math.Min(170, (int)a)));
            Raylib.DrawRectangle(0, 30, s.Width, 1, new Color((byte)150, (byte)40, (byte)50, a));
            var nw = font.Measure(name) * 2;
            font.DrawShadowed(name, (s.Width - nw) / 2, 9, new Color((byte)220, (byte)70, (byte)80, a), new Color((byte)0, (byte)0, (byte)0, a), 2);
        }
    }

    /// <summary>Sprite del enemigo 3x por convencion enemy.X -> sprite.X, con flash aditivo al recibir dano.</summary>
    void DrawEnemySprite(BattleCombatant enemy, int centerX, int baselineY, bool flashing)
    {
        var drew = false;
        if (spriteBank != null && enemy.Enemy != null)
        {
            var candidate = "sprite." + enemy.Enemy.Id.Replace("enemy.", "");
            var spriteId = project.Sprites.FirstOrDefault(x => x.Id == candidate || candidate.StartsWith(x.Id))?.Id;
            if (spriteId != null)
            {
                var (w, h) = spriteBank.SizeOf(spriteId);
                if (w > 0)
                {
                    var ex = centerX - w * 3 / 2;
                    var ey = baselineY - h * 3;
                    drew = spriteBank.TryDrawScaled(spriteId, Facing.Down, 0, ex, ey, 3, Color.White);
                    if (drew && flashing)
                    {
                        Raylib.BeginBlendMode(BlendMode.Additive);
                        spriteBank.TryDrawScaled(spriteId, Facing.Down, 0, ex, ey, 3, Color.White);
                        spriteBank.TryDrawScaled(spriteId, Facing.Down, 0, ex, ey, 3, Color.White);
                        Raylib.EndBlendMode();
                    }
                }
            }
        }
        if (!drew) Raylib.DrawRectangle(centerX - 24, baselineY - 44, 48, 44, flashing ? Color.White : new Color(160, 50, 70, 255));
    }

    void DrawDialogueBox(DialogueSession session)
    {
        var s = screen!;
        var node = session.Current;
        var boxY = s.Height - 64;
        // La caja de nombre monta sobre el borde superior de la ventana.
        WindowBox.Draw(theme, 4, boxY, s.Width - 8, 60);
        WindowBox.DrawNameTag(theme, font, node.Speaker, 12, boxY - 8);

        // 4 lineas por pagina: entran en la caja (60px) con la flecha de continuar debajo.
        session.Prepare(font.WrapPixels(node.Text, s.Width - 28), 4);
        font.DrawShadowed(session.VisibleText, 12, boxY + 10, theme.TextColor, theme.ShadowColor);

        if (!session.TextComplete) return;
        // Con paginas pendientes, solo la flecha de continuar (las elecciones van en la ultima).
        if (session.HasMorePages)
        {
            WindowBox.DrawContinueArrow(theme, s.Width / 2, boxY + 52, time);
            return;
        }
        if (node.Choices.Count > 0)
        {
            // Ventana de elecciones sobre el texto. Caja de ancho fijo y CADA
            // opcion se envuelve por pixeles: una opcion larga (o una traduccion a un idioma
            // mas verboso) nunca se sale de pantalla. La altura crece con las lineas.
            var cx = 4;
            var boxW = s.Width - 8;
            var innerW = boxW - 22; // margen para el cursor (izq) y el aire (der)
            var wrapped = node.Choices.Select(c => font.WrapPixels(c.Text, innerW)).ToList();
            var lineCounts = wrapped.Select(w => w.Split('\n').Length).ToList();
            var height = lineCounts.Sum() * 10 + node.Choices.Count * 3 + 7;
            var cy = Math.Max(4, boxY - height - 2);
            WindowBox.Draw(theme, cx, cy, boxW, height);
            var yy = cy + 5;
            for (var i = 0; i < node.Choices.Count; i++)
            {
                var color = i == session.SelectedChoice ? theme.AccentColor : theme.TextColor;
                if (i == session.SelectedChoice) WindowBox.DrawCursor(theme, cx + 6, yy + 1);
                font.DrawShadowed(wrapped[i], cx + 16, yy, color, theme.ShadowColor);
                yy += lineCounts[i] * 10 + 3;
            }
        }
        else
        {
            WindowBox.DrawContinueArrow(theme, s.Width / 2, boxY + 52, time);
        }
    }

    void DrawMessageBox(string text)
    {
        var s = screen!;
        // Con el editor abierto el mensaje sube: abajo vive la paleta de tiles.
        var boxY = editor.Visible ? 14 : s.Height - 38;
        WindowBox.Draw(theme, 4, boxY, s.Width - 8, 34);
        font.DrawShadowed(font.WrapPixels(text, s.Width - 28), 12, boxY + 7, theme.TextColor, theme.ShadowColor);
    }

    static Color ParseColor(string hex) => SpriteRaster.ParseColor(hex);
}
