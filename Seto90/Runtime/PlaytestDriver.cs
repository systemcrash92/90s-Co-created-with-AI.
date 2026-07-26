using System.Text.Json;
using Raylib_cs;

namespace Seto90;

/// <summary>
/// playtest.run: guiones de partida deterministas — la IA JUEGA su propio
/// contenido con el juego oculto y recibe un reporte (pasos ok/fallo + estado final del
/// mundo) para corregirse. El guion no simula teclado del sistema: inyecta teclas
/// SINTETICAS (synthConfirm/Up/Down/Cancel) que los estados leen junto a las reales, y
/// para caminar usa el mismo GridMover validado de las cutscenes. Sin RNG en el motor,
/// mismo guion = mismo resultado, siempre.
///
/// Pasos (uno por string; '#' comenta):
///   event event.id 2     dispara una pagina exacta en su mapa (atajo de autoria)
///   goto map.id x,y    teletransporte de test (llegar rapido a lo que se prueba)
///   move up,up,left    camina (paso bloqueado se saltea, como en cutscenes)
///   face up            mirar sin caminar
///   interact           Enter sobre la casilla mirada
///   auto               confirma cada frame hasta que el mundo quede libre (dialogos,
///                      paginas, ceremonias y combates enteros a fuerza de Atacar)
///   choose N           en una eleccion de dialogo: avanza hasta la eleccion y elige N (0-based)
///   confirm|cancel|up|down   una tecla sintetica, un frame
///   wait 1.5           espera en segundos simulados
///   assert-flag flag.x true|false      assert-map map.id      assert-item item.id
///   assert-pos x,y     assert-money N|MIN..MAX
///   assert-party actor.a,actor.b       assert-level actor.a N|MIN..MAX
///   checkpoint id      marca el estado entre hitos de una ruta
///   screenshot foo.png captura el lienzo a build/
///   dump               vuelca el estado del mundo al reporte
/// </summary>
public sealed partial class VisualRuntime
{
    Queue<string>? scriptQueue;
    readonly List<object> scriptLog = [];
    string scriptStep = "";
    int scriptStepFrames;
    int scriptTotalFrames;
    readonly Queue<string> scriptMoves = new();
    int scriptChoose = -1;
    float scriptWaitLeft;
    bool scriptOk = true;
    bool synthConfirm, synthUp, synthDown, synthCancel;

    /// <summary>Carga el guion; se ejecuta durante Run() (usar hidden:true y maxFrames generoso).</summary>
    public void DebugRunScript(IEnumerable<string> steps) =>
        scriptQueue = new Queue<string>(steps.Select(s => s.Trim()).Where(s => s.Length > 0 && !s.StartsWith('#')));

    /// <summary>Nada retiene al jugador: sin dialogo/combate/tienda/ceremonia/cola/transicion.</summary>
    bool WorldIdle() => activeDialogue == null && activeBattle == null && activeShop == null
        && itemGetItem == null && !transition.Blocking && commandQueue.Count == 0
        && cutsceneWait <= 0 && cutsceneMover == null && !camPanning
        && title == null && splash == null && !gameOver && !paused;

    void UpdateScriptDriver(float dt)
    {
        synthConfirm = synthUp = synthDown = synthCancel = false;
        scriptTotalFrames++;
        // Un guion que cae al titulo o al game over no puede seguir jugando: se aborta y
        // el reporte lo cuenta (derrota inesperada = exactamente lo que la IA quiere saber).
        if (title != null || gameOver) { AbortScript(gameOver ? "game over" : "pantalla de titulo"); return; }
        if (scriptQueue == null) return;

        if (scriptStep != "")
        {
            scriptStepFrames++;
            if (scriptStepFrames > 1800) { StepDone(false, "timeout del paso (30s simulados)"); return; }
            RunStep(dt);
            return;
        }
        if (scriptQueue.Count == 0) { FinishScript(); return; }
        scriptStep = scriptQueue.Dequeue();
        scriptStepFrames = 0;
        StartStep();
    }

    /// <summary>Los pasos instantaneos (asserts, capturas, dump) resuelven aca; los que llevan
    /// tiempo (move/auto/choose/wait) dejan armado su estado y avanzan en RunStep.</summary>
    void StartStep()
    {
        var parts = scriptStep.Split(' ', 2);
        var verb = parts[0].ToLowerInvariant();
        var arg = parts.Length > 1 ? parts[1].Trim() : "";
        switch (verb)
        {
            case "event":
            {
                if (!WorldIdle()) { StepDone(false, "el mundo no estaba libre antes de disparar el evento"); break; }
                var e = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var eventId = e.FirstOrDefault() ?? "";
                var pageIndex = e.Length > 1 && int.TryParse(e[1], out var parsedPage) ? parsedPage : -1;
                var ev = project.Events.FirstOrDefault(x => x.Id == eventId);
                if (ev == null || pageIndex >= ev.Pages.Count || pageIndex < -1)
                {
                    StepDone(false, $"evento/pagina inexistente: {eventId} {pageIndex}");
                    break;
                }
                PrepareDebugScene(eventId);
                if (pageIndex >= 0) ApplyDebugPageConditions(ev.Pages[pageIndex]);
                StartEvent(ev, pageIndex);
                StepDone(true, $"{eventId} pagina {(pageIndex < 0 ? "activa" : pageIndex)} en {map.Id}");
                break;
            }
            case "goto":
            {
                var g = arg.Split(' ', 2);
                var xy = (g.Length > 1 ? g[1] : "").Split(',');
                if (g.Length < 2 || xy.Length != 2 || !int.TryParse(xy[0].Trim(), out var gx) || !int.TryParse(xy[1].Trim(), out var gy))
                { StepDone(false, "uso: goto map.id x,y"); break; }
                ApplyTransfer(g[0].Trim(), gx, gy);
                StepDone(project.Maps.Any(m => m.Id == g[0].Trim()), $"en {map.Id} ({player.TileX},{player.TileY})");
                break;
            }
            case "move": scriptMoves.Clear(); foreach (var s in CutsceneSteps.Parse(arg)) scriptMoves.Enqueue(s); break;
            case "face": scriptMoves.Clear(); scriptMoves.Enqueue("face:" + arg.ToLowerInvariant()); break;
            case "choose": scriptChoose = int.TryParse(arg, out var c) ? Math.Max(0, c) : 0; break;
            case "wait": scriptWaitLeft = CutsceneSteps.TryParseWait(arg, out var w) ? w : 0.5f; break;
            case "auto": case "interact": case "confirm": case "cancel": case "up": case "down": break; // corren en RunStep
            case "assert-flag":
            {
                var a = arg.Split(' ', 2);
                var expected = a.Length < 2 || a[1].Trim().Equals("true", StringComparison.OrdinalIgnoreCase);
                var actual = flags.TryGetValue(a[0].Trim(), out var v) && v;
                StepDone(actual == expected, $"{a[0].Trim()} = {actual.ToString().ToLowerInvariant()}");
                break;
            }
            case "assert-map": StepDone(map.Id == arg, $"mapa actual = {map.Id}"); break;
            case "assert-item": { var n = inventory.Count(i => i == arg); StepDone(n > 0, $"{arg} x{n} en inventario"); break; }
            case "assert-pos":
            {
                var xy = arg.Split(',');
                var okPos = xy.Length == 2 && int.TryParse(xy[0].Trim(), out var ax) && int.TryParse(xy[1].Trim(), out var ay) && player.TileX == ax && player.TileY == ay;
                StepDone(okPos, $"jugador en ({player.TileX},{player.TileY})");
                break;
            }
            case "assert-money":
            {
                var ok = TryRange(arg, out var min, out var max) && money >= min && money <= max;
                StepDone(ok, $"dinero = {money}; esperado {arg}");
                break;
            }
            case "assert-party":
            {
                var expected = arg.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                var actual = party.Members.Select(x => x.Def.Id).ToArray();
                StepDone(expected.SequenceEqual(actual, StringComparer.Ordinal), $"party = {string.Join(",", actual)}");
                break;
            }
            case "assert-level":
            {
                var a = arg.Split(' ', 2, StringSplitOptions.RemoveEmptyEntries);
                var member = a.Length > 0 ? party.Members.FirstOrDefault(x => x.Def.Id == a[0]) : null;
                var ok = a.Length == 2 && member != null && TryRange(a[1], out var min, out var max) &&
                         member.Level >= min && member.Level <= max;
                StepDone(ok, member == null ? "actor ausente" : $"{member.Def.Id} nivel {member.Level}; esperado {(a.Length > 1 ? a[1] : "n/d")}");
                break;
            }
            case "checkpoint":
                StepDone(true, $"{arg}: {map.Id} ({player.TileX},{player.TileY}), party {string.Join(",", party.Members.Select(x => $"{x.Def.Id}@{x.Level}"))}, ${money}");
                break;
            case "screenshot":
            {
                // Sin extension .png raylib no exporta nada y el paso reportaba OK igual: un
                // guion que cree haber capturado y no capturo es peor que uno que falla.
                var name = string.IsNullOrWhiteSpace(arg) ? "playtest.png" : arg;
                if (!name.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) name += ".png";
                var file = Path.Combine(projectRoot ?? ".", "build", name);
                if (screen != null)
                {
                    screen.BeginVirtual(); DrawVirtual(); screen.EndVirtual();
                    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
                    // Mismo criterio que la captura final: con --crt sale el vidrio a 3x.
                    if (captureCrt && crt is { Ready: true }) { crt.Enabled = true; screen.ExportPresentedPng(file, crt); }
                    else screen.ExportPng(file);
                }
                StepDone(screen != null, file);
                break;
            }
            case "dump": scriptLog.Add(new { step = "dump", ok = true, state = StateDump() }); scriptStep = ""; break;
            default: StepDone(false, $"paso desconocido '{verb}'"); break;
        }
    }

    void RunStep(float dt)
    {
        switch (scriptStep.Split(' ', 2)[0].ToLowerInvariant())
        {
            case "move": case "face":
                // Un trigger pisado a mitad de camino corta la caminata: el guion sigue con
                // lo que abrio (tipicamente un "auto" a continuacion).
                if (!WorldIdle()) { scriptMoves.Clear(); if (!player.Moving) StepDone(true, "interrumpido por un evento"); return; }
                if (!player.Moving && scriptMoves.Count > 0)
                {
                    var (dx, dy, facing, faceOnly) = CutsceneSteps.Decode(scriptMoves.Dequeue());
                    if (faceOnly) player.Facing = facing;
                    else player.TryStep(dx, dy, (x, y) => CanOccupy(x, y));
                }
                if (scriptMoves.Count == 0 && !player.Moving) StepDone(true, $"jugador en ({player.TileX},{player.TileY}) mirando {player.Facing}");
                return;
            case "wait":
                scriptWaitLeft -= dt;
                if (scriptWaitLeft <= 0) StepDone(true, "espera cumplida");
                return;
            case "interact":
                if (!WorldIdle() || player.Moving) return;
                ActivateFacingEvent();
                StepDone(true, "activado");
                return;
            case "auto":
                if (WorldIdle()) { StepDone(true, $"mundo libre ({scriptStepFrames} frames)"); return; }
                synthConfirm = true;
                return;
            case "choose":
                if (activeDialogue is { } d && d.TextComplete && !d.HasMorePages && d.Current.Choices.Count > 0)
                {
                    d.SelectedChoice = Math.Min(scriptChoose, d.Current.Choices.Count - 1);
                    var picked = d.Current.Choices[d.SelectedChoice].Text;
                    synthConfirm = true;
                    StepDone(true, $"eligio '{picked}'");
                    return;
                }
                if (activeDialogue != null) synthConfirm = true; // avanzar texto/paginas hasta la eleccion
                return;
            case "confirm": synthConfirm = true; StepDone(true, "tecla"); return;
            case "cancel": synthCancel = true; StepDone(true, "tecla"); return;
            case "up": synthUp = true; StepDone(true, "tecla"); return;
            case "down": synthDown = true; StepDone(true, "tecla"); return;
        }
    }

    void StepDone(bool ok, string detail)
    {
        if (!ok) scriptOk = false;
        scriptLog.Add(new { step = scriptStep, ok, frames = scriptStepFrames, detail });
        scriptStep = "";
        scriptMoves.Clear();
        scriptChoose = -1;
        scriptWaitLeft = 0;
    }

    void AbortScript(string reason)
    {
        if (scriptQueue == null) return;
        if (scriptStep != "") StepDone(false, $"abortado: {reason}");
        foreach (var pending in scriptQueue) scriptLog.Add(new { step = pending, ok = false, frames = 0, detail = "no ejecutado (guion abortado)" });
        scriptOk = false;
        FinishScript();
    }

    void FinishScript()
    {
        scriptQueue = null;
        quitRequested = true; // el guion termino: Run() sale solo
    }

    object StateDump() => new
    {
        map = map.Id,
        x = player.TileX,
        y = player.TileY,
        money,
        day,
        phase = dayPhase,
        flags = flags.Where(f => f.Value).Select(f => f.Key).OrderBy(k => k).ToList(),
        inventory = inventory.OrderBy(k => k).ToList(),
        party = party.Members.Select(m => new { actor = m.Def.Id, level = m.Level, hp = m.Hp, maxHp = m.MaxHp, mp = m.Mp }).ToList(),
    };

    /// <summary>Reporte final del guion: pasos con resultado + estado del mundo, JSON legible.</summary>
    public string BuildScriptReport() => JsonSerializer.Serialize(new
    {
        ok = scriptOk && scriptQueue == null && scriptStep == "",
        frames = scriptTotalFrames,
        steps = scriptLog,
        state = StateDump(),
    }, new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    static bool TryRange(string value, out int min, out int max)
    {
        var parts = value.Trim().Split("..", 2, StringSplitOptions.TrimEntries);
        if (parts.Length == 1 && int.TryParse(parts[0], out var exact))
        {
            min = max = exact;
            return true;
        }
        if (parts.Length == 2 && int.TryParse(parts[0], out min) && int.TryParse(parts[1], out max) && min <= max)
            return true;
        min = max = 0;
        return false;
    }
}
