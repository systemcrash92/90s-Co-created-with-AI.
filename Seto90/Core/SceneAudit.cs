using System.Text;

namespace Seto90;

/// <summary>Alcance exacto que reviso el auditor. Una escena puede ser una pagina de evento o
/// una escena del Libro Espejo que agrupa varios eventos/dialogos/batallas.</summary>
public sealed record SceneAuditScope(
    string Kind,
    string Id,
    string Title,
    List<string> MapIds,
    List<string> EventIds,
    List<string> DialogueIds,
    List<string> BattleIds);

/// <summary>Capas expresivas presentes. No son una checklist obligatoria: permiten a la IA
/// distinguir una charla intima deliberadamente quieta de una cutscene que quedo sin montar.</summary>
public sealed record SceneAuditCoverage(
    bool HasDialogue,
    bool HasChoices,
    bool HasStateChange,
    bool HasMovement,
    bool HasCamera,
    bool HasTiming,
    bool HasEmote,
    bool HasVisualFx,
    bool HasAudioCue,
    bool HasBuiltInFeedback);

public sealed record SceneAuditSummary(
    int Pages,
    int Commands,
    int DialogueNodes,
    int DialogueCharacters,
    int Warnings,
    int ReviewNotes,
    string Verdict);

/// <summary>Hallazgo explicable. warning = riesgo concreto; info = oportunidad o pregunta de
/// puesta en escena. Evidence cita el hecho que disparo la regla para que la IA no adivine.</summary>
public sealed record SceneAuditFinding(
    string Severity,
    string Dimension,
    string Code,
    string Location,
    string Evidence,
    string Message,
    string Suggestion);

/// <summary>Guion linealizado para el juicio creativo de la IA. Los comandos conservan target/value;
/// los dialogos conservan speaker/texto. No intenta reducir una escena a una nota numerica.</summary>
public sealed record SceneAuditBeat(
    int Index,
    string Source,
    string Kind,
    string Speaker,
    string Text,
    string TargetId,
    string Value);

public sealed record SceneAuditDimension(string Id, string Status, List<string> Evidence);

public sealed class SceneAuditReport
{
    public SceneAuditScope Scope { get; init; } = new("", "", "", [], [], [], []);
    public SceneAuditSummary Summary { get; init; } = new(0, 0, 0, 0, 0, 0, "needs_fix");
    public SceneAuditCoverage Coverage { get; init; } = new(false, false, false, false, false, false, false, false, false, false);
    public List<SceneAuditDimension> Dimensions { get; init; } = [];
    public List<SceneAuditFinding> Findings { get; init; } = [];
    public List<SceneAuditBeat> Beats { get; init; } = [];
    /// <summary>Preguntas que requieren comprension narrativa/visual real. La IA debe contestarlas
    /// usando Beats y luego confirmar la escena mediante screenshot/playtest.</summary>
    public List<string> AiReviewQuestions { get; init; } = [];
    public List<string> SuggestedChecks { get; init; } = [];

    public string ToHumanText()
    {
        var b = new StringBuilder();
        b.AppendLine($"90s Engine - auditor de escena: {Scope.Kind}:{Scope.Id} ({Scope.Title})");
        b.AppendLine($"Veredicto: {Summary.Verdict}. {Summary.Pages} pagina(s), {Summary.Commands} comando(s), {Summary.DialogueNodes} nodo(s), {Summary.DialogueCharacters} caracteres.");
        b.AppendLine($"Hallazgos: {Summary.Warnings} warning(s), {Summary.ReviewNotes} nota(s) de pulido.");
        b.AppendLine("Cobertura: " + string.Join(", ", CoverageLabels(Coverage)));
        foreach (var finding in Findings)
        {
            b.AppendLine($"[{finding.Severity.ToUpperInvariant()}][{finding.Dimension}] {finding.Code} @ {finding.Location}: {finding.Message}");
            b.AppendLine($"  Evidencia: {finding.Evidence}");
            b.AppendLine($"  Sugerencia: {finding.Suggestion}");
        }
        b.AppendLine("Preguntas para la IA/autoria:");
        foreach (var question in AiReviewQuestions) b.AppendLine($"- {question}");
        b.AppendLine("Comprobaciones sugeridas:");
        foreach (var check in SuggestedChecks) b.AppendLine($"- {check}");
        return b.ToString().TrimEnd();
    }

    static IEnumerable<string> CoverageLabels(SceneAuditCoverage c)
    {
        if (c.HasDialogue) yield return "dialogo";
        if (c.HasChoices) yield return "elecciones";
        if (c.HasStateChange) yield return "estado";
        if (c.HasMovement) yield return "movimiento";
        if (c.HasCamera) yield return "camara";
        if (c.HasTiming) yield return "ritmo";
        if (c.HasEmote) yield return "emote";
        if (c.HasVisualFx) yield return "vfx";
        if (c.HasAudioCue) yield return "audio autorado";
        if (c.HasBuiltInFeedback) yield return "feedback del motor";
        if (!c.HasDialogue && !c.HasStateChange && !c.HasMovement && !c.HasVisualFx) yield return "sin beats expresivos detectables";
    }
}

/// <summary>
/// Auditor local de escenas JRPG. Hace dos trabajos:
/// 1) reglas reproducibles sobre flags, repeticion, orden, ritmo, dialogos y feedback;
/// 2) un dossier de beats + preguntas para que la IA juzgue coherencia, intencion y game feel.
/// Nunca pretende resolver creatividad mediante una puntuacion opaca.
/// </summary>
public static class SceneAudit
{
    static readonly HashSet<CommandKind> StateChanges =
    [
        CommandKind.SetVariable, CommandKind.Battle, CommandKind.GiveItem, CommandKind.ShowItemGet,
        CommandKind.AddPartyMember, CommandKind.RemovePartyMember, CommandKind.AdvanceTime,
        CommandKind.GiveMoney, CommandKind.TakeMoney, CommandKind.TransferPlayer
    ];

    static readonly HashSet<CommandKind> RepeatSensitive =
    [
        CommandKind.Battle, CommandKind.GiveItem, CommandKind.ShowItemGet,
        CommandKind.AddPartyMember, CommandKind.RemovePartyMember,
        CommandKind.GiveMoney, CommandKind.TakeMoney
    ];

    static readonly HashSet<CommandKind> Blocking =
    [
        CommandKind.Dialogue, CommandKind.Battle, CommandKind.OpenShop, CommandKind.TransferPlayer,
        CommandKind.Wait, CommandKind.MoveEvent, CommandKind.MovePlayer, CommandKind.PanCamera,
        CommandKind.OpenInn, CommandKind.AdvanceTime, CommandKind.ShowItemGet
    ];

    static readonly string[] DimensionOrder =
    [
        "structure", "state", "script", "staging", "expression", "gamefeel", "audio", "pacing", "continuity"
    ];

    sealed record PageContext(EventDef Event, EventPage Page, int PageIndex);

    public static SceneAuditReport AnalyzeEvent(
        GameProject p,
        string eventId,
        string pageId = "",
        bool includeInfo = true,
        bool includeTranscript = true,
        int pageIndex = -1)
    {
        var ev = p.Events.FirstOrDefault(x => x.Id == eventId)
            ?? throw new KeyNotFoundException($"No existe el evento '{eventId}'.");
        var pages = ev.Pages.Select((page, index) => new PageContext(ev, page, index))
            .Where(x => pageIndex >= 0 ? x.PageIndex == pageIndex : string.IsNullOrWhiteSpace(pageId) || x.Page.Id == pageId)
            .ToList();
        if (pageIndex >= 0 && pages.Count == 0)
            throw new KeyNotFoundException($"El evento '{eventId}' no tiene una pagina en indice {pageIndex}.");
        if (!string.IsNullOrWhiteSpace(pageId) && pages.Count == 0)
            throw new KeyNotFoundException($"El evento '{eventId}' no tiene una pagina '{pageId}'.");
        return AnalyzeCore(
            p,
            new SceneAuditScope("event", ev.Id, string.IsNullOrWhiteSpace(ev.Name) ? ev.Id : ev.Name,
                [ev.MapId], [ev.Id], [], []),
            pages,
            [],
            [],
            null,
            includeInfo,
            includeTranscript);
    }

    public static SceneAuditReport AnalyzeStoryScene(
        GameProject p,
        string sceneId,
        bool includeInfo = true,
        bool includeTranscript = true)
    {
        var scene = NarrativeTwin.FindScene(p, sceneId)
            ?? throw new KeyNotFoundException($"No existe la escena literaria '{sceneId}'.");
        var eventIds = scene.Links.Where(x => x.Kind.Equals("event", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Id).Distinct(StringComparer.Ordinal).ToList();
        var directDialogues = scene.Links.Where(x => x.Kind.Equals("dialogue", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Id).Distinct(StringComparer.Ordinal).ToList();
        var directBattles = scene.Links.Where(x => x.Kind.Equals("battle", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Id).Distinct(StringComparer.Ordinal).ToList();
        var mapIds = scene.Links.Where(x => x.Kind.Equals("map", StringComparison.OrdinalIgnoreCase))
            .Select(x => x.Id).Distinct(StringComparer.Ordinal).ToList();
        var events = eventIds.Select(id => p.Events.FirstOrDefault(x => x.Id == id)).Where(x => x != null).Cast<EventDef>().ToList();
        mapIds.AddRange(events.Select(x => x.MapId));
        mapIds = mapIds.Distinct(StringComparer.Ordinal).ToList();
        return AnalyzeCore(
            p,
            new SceneAuditScope("story", scene.Id, string.IsNullOrWhiteSpace(scene.Title) ? scene.Id : scene.Title,
                mapIds, eventIds, directDialogues, directBattles),
            events.SelectMany(ev => ev.Pages.Select((page, index) => new PageContext(ev, page, index))).ToList(),
            directDialogues,
            directBattles,
            scene,
            includeInfo,
            includeTranscript);
    }

    static SceneAuditReport AnalyzeCore(
        GameProject p,
        SceneAuditScope initialScope,
        List<PageContext> pages,
        List<string> directDialogueIds,
        List<string> directBattleIds,
        StorySceneDef? story,
        bool includeInfo,
        bool includeTranscript)
    {
        var findings = new List<SceneAuditFinding>();
        void Add(string severity, string dimension, string code, string location, string evidence, string message, string suggestion) =>
            findings.Add(new(severity, dimension, code, location, evidence, message, suggestion));

        var dialogueRoots = pages.SelectMany(x => x.Page.Commands)
            .Where(x => x.Kind == CommandKind.Dialogue).Select(x => x.TargetId)
            .Concat(directDialogueIds);
        var dialogueIds = ExpandDialogueIds(p, dialogueRoots);
        var dialogues = dialogueIds.Select(id => p.Dialogues.FirstOrDefault(x => x.Id == id))
            .Where(x => x != null).Cast<DialogueDef>().ToList();
        var battleIds = pages.SelectMany(x => x.Page.Commands)
            .Where(x => x.Kind == CommandKind.Battle).Select(x => x.TargetId)
            .Concat(dialogues.SelectMany(ReachableNodes).SelectMany(x => x.Effects)
                .Where(x => x.Kind == CommandKind.Battle).Select(x => x.TargetId))
            .Concat(directBattleIds).Distinct(StringComparer.Ordinal).ToList();
        var eventIds = initialScope.EventIds.Distinct(StringComparer.Ordinal).ToList();
        var mapIds = initialScope.MapIds.Distinct(StringComparer.Ordinal).ToList();
        var scope = initialScope with
        {
            MapIds = mapIds,
            EventIds = eventIds,
            DialogueIds = dialogueIds,
            BattleIds = battleIds
        };

        if (pages.Count == 0 && dialogues.Count == 0)
            Add("warning", "structure", "scene_without_playable_beats", $"{scope.Kind}:{scope.Id}",
                "No hay paginas de evento ni dialogos enlazados.",
                "El alcance no contiene una entrada jugable que el jugador pueda experimentar.",
                "Enlazar al menos un evento o dialogo real; un mapa/actor por si solo describe contexto, no una escena.");

        foreach (var page in pages) AuditPage(p, page.Event, page.Page, findings);
        foreach (var dialogue in dialogues) AuditDialogue(dialogue, findings);
        foreach (var battleId in battleIds) AuditBattle(p, battleId, findings);
        if (story != null) AuditStory(p, story, eventIds, dialogueIds, findings);

        var allCommands = pages.SelectMany(x => x.Page.Commands)
            .Concat(dialogues.SelectMany(ReachableNodes).SelectMany(x => x.Effects)).ToList();
        var nodes = dialogues.SelectMany(ReachableNodes).ToList();
        var coverage = Coverage(allCommands, nodes);
        var beats = includeTranscript ? BuildBeats(pages, dialogues, story) : [];

        if (!includeInfo) findings.RemoveAll(x => x.Severity == "info");
        findings = findings
            .OrderBy(x => x.Severity == "warning" ? 0 : 1)
            .ThenBy(x => Array.IndexOf(DimensionOrder, x.Dimension))
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.Location, StringComparer.Ordinal)
            .ToList();
        var warnings = findings.Count(x => x.Severity == "warning");
        var info = findings.Count(x => x.Severity == "info");
        var verdict = warnings > 0 ? "needs_fix" : info > 0 ? "needs_polish_review" : "ready_for_playtest";
        var dimensions = DimensionOrder.Select(id =>
        {
            var local = findings.Where(x => x.Dimension == id).ToList();
            var status = local.Any(x => x.Severity == "warning") ? "warning" : local.Count > 0 ? "review" : "ok";
            return new SceneAuditDimension(id, status, local.Select(x => x.Evidence).Distinct(StringComparer.Ordinal).Take(4).ToList());
        }).ToList();

        var questions = Questions(coverage, nodes, allCommands, story);
        var checks = SuggestedChecks(scope, pages);
        return new SceneAuditReport
        {
            Scope = scope,
            Summary = new(
                pages.Count,
                pages.Sum(x => x.Page.Commands.Count) + nodes.Sum(x => x.Effects.Count),
                nodes.Count,
                nodes.Sum(x => x.Text?.Length ?? 0),
                warnings,
                info,
                verdict),
            Coverage = coverage,
            Dimensions = dimensions,
            Findings = findings,
            Beats = beats,
            AiReviewQuestions = questions,
            SuggestedChecks = checks
        };
    }

    static void AuditPage(GameProject p, EventDef ev, EventPage page, List<SceneAuditFinding> findings)
    {
        var location = $"event:{ev.Id}/page:{page.Id}";
        var commands = page.Commands;
        void Add(string severity, string dimension, string code, string evidence, string message, string suggestion) =>
            findings.Add(new(severity, dimension, code, location, evidence, message, suggestion));

        if (commands.Count == 0)
        {
            Add("warning", "structure", "scene_page_without_commands", "commands=0",
                "La pagina elegida no ejecuta ningun beat.",
                "Agregar el comando que materializa la escena o auditar la pagina correcta.");
            return;
        }

        var dialogueIds = ExpandDialogueIds(p, commands.Where(x => x.Kind == CommandKind.Dialogue).Select(x => x.TargetId));
        var nodes = dialogueIds.Select(id => p.Dialogues.FirstOrDefault(x => x.Id == id))
            .Where(x => x != null).Cast<DialogueDef>().SelectMany(ReachableNodes).ToList();
        var nested = nodes.SelectMany(x => x.Effects).ToList();
        var all = commands.Concat(nested).ToList();
        var significant = ev.Kind is EventKind.Cutscene or EventKind.Trigger || commands.Count >= 3;
        var hasStaging = all.Any(x => x.Kind is CommandKind.MoveEvent or CommandKind.MovePlayer or CommandKind.PanCamera or CommandKind.ShowEmote);
        var hasVisual = all.Any(x => x.Kind is CommandKind.PlayVfx or CommandKind.ShowFloat or CommandKind.ShowItemGet or CommandKind.SetWeather);
        var hasBuiltInPresentation = all.Any(x => x.Kind is CommandKind.Battle or CommandKind.TransferPlayer or
            CommandKind.ShowItemGet or CommandKind.OpenInn or CommandKind.AdvanceTime or CommandKind.AddPartyMember);
        var hasAudio = all.Any(x => x.Kind is CommandKind.PlaySfx or CommandKind.PlaySong);
        var hasBuiltInAudio = all.Any(x => x.Kind is CommandKind.Battle or CommandKind.TransferPlayer or
            CommandKind.ShowItemGet or CommandKind.OpenInn or CommandKind.AddPartyMember);

        if (significant && nodes.Count > 0 && !hasStaging && !hasVisual && !hasBuiltInPresentation)
            Add("info", "staging", "flat_scene_staging",
                $"{nodes.Count} nodo(s); sin Move/Pan/Emote/VFX",
                "La escena tiene varios beats o consecuencias, pero toda la puesta queda en la caja de dialogo.",
                "Confirmar que la quietud sea intencional; si no, sumar una reaccion, movimiento, foco de camara, emote o efecto en el beat clave.");

        if (all.Any(x => x.Kind is CommandKind.PlayVfx or CommandKind.PanCamera or CommandKind.MoveEvent or CommandKind.MovePlayer) && !hasAudio && !hasBuiltInAudio)
            Add("info", "audio", "visual_beat_without_audio_cue",
                "Hay VFX/camara/movimiento y no hay PlaySfx/PlaySong en la escena.",
                "Un beat visual importante puede sentirse liviano sin puntuacion sonora.",
                "Probar un SFX breve en el impacto/revelacion; mantener silencio si es una decision dramatica consciente.");

        var panAway = commands.FindIndex(x => x.Kind == CommandKind.PanCamera &&
            !string.IsNullOrWhiteSpace(x.TargetId) && !x.TargetId.Equals("player", StringComparison.OrdinalIgnoreCase));
        if (panAway >= 0 && !commands.Skip(panAway + 1).Any(x => x.Kind == CommandKind.PanCamera &&
            (string.IsNullOrWhiteSpace(x.TargetId) || x.TargetId.Equals("player", StringComparison.OrdinalIgnoreCase))))
            Add("info", "staging", "implicit_camera_return",
                $"PanCamera #{panAway} usa el retorno automatico del runtime.",
                "La camara vuelve sola al terminar, pero el autor no controla el momento exacto del regreso.",
                "Agregar PanCamera targetId='player' donde cierre el beat si el ritmo del retorno importa.");

        var maxBurst = 0;
        var burst = 0;
        foreach (var command in commands)
        {
            if (Blocking.Contains(command.Kind)) burst = 0;
            else { burst++; maxBurst = Math.Max(maxBurst, burst); }
        }
        if (maxBurst >= 4 && commands.Any(x => x.Kind is CommandKind.PlayVfx or CommandKind.PlaySfx or CommandKind.ShowEmote or CommandKind.ShowFloat))
            Add("info", "pacing", "same_frame_effect_burst",
                $"{maxBurst} comandos no bloqueantes consecutivos.",
                "Varios efectos se disparan en el mismo frame y pueden taparse o sentirse instantaneos.",
                "Intercalar Wait corto o un beat bloqueante cuando los efectos deban leerse en secuencia.");

        for (var i = 0; i < commands.Count; i++)
        {
            if (commands[i].Kind is not (CommandKind.PlayVfx or CommandKind.ShowEmote or CommandKind.ShowFloat)) continue;
            if (PersistentEmote(commands[i])) continue;
            if (commands.Skip(i + 1).Any(x => Blocking.Contains(x.Kind))) continue;
            Add("info", "pacing", "nonblocking_effect_not_held",
                $"{commands[i].Kind} #{i} no tiene Wait/dialogo/bloqueo posterior.",
                "El efecto acompana al mundo, pero la escena termina sin reservarle tiempo de lectura.",
                "Agregar Wait si el jugador debe contemplarlo; dejarlo asi si debe persistir mientras recupera el control.");
        }

        var dialogueIndex = commands.FindIndex(x => x.Kind == CommandKind.Dialogue);
        var emoteIndex = commands.FindIndex(x => x.Kind == CommandKind.ShowEmote);
        if (dialogueIndex >= 0 && emoteIndex > dialogueIndex && !PersistentEmote(commands[emoteIndex]) &&
            !commands.Skip(emoteIndex + 1).Any(x => x.Kind == CommandKind.Dialogue))
            Add("info", "expression", "emote_after_dialogue",
                $"Dialogue #{dialogueIndex}; ShowEmote #{emoteIndex}; no hay otro Dialogue.",
                "El globo aparece despues de cerrar la conversacion, no durante la linea que probablemente lo motiva.",
                "Mover ShowEmote antes de Dialogue si debe acompanar el texto; conservar el orden si es una reaccion posterior.");

        if (!all.Any(x => x.Kind == CommandKind.ShowEmote) && significant)
        {
            var cue = nodes.Select(EmoteCue).FirstOrDefault(x => x.Icon != "");
            if (!string.IsNullOrEmpty(cue.Icon))
                Add("info", "expression", "emote_opportunity",
                    $"'{Clip(cue.Text, 90)}' sugiere '{cue.Icon}'.",
                    "El dialogo contiene una reaccion corta que podria leerse mejor con un globo emocional.",
                    $"Probar ShowEmote targetId='{ev.Id}' value='{cue.Icon}' antes del dialogo; quitarlo si duplica una actuacion ya clara.");
        }

        // Los efectos de un nodo de dialogo son comandos reales de la escena: una recompensa o
        // flag entregada desde una eleccion puede repetirse igual que si estuviera en la pagina.
        // Auditar solo la cola exterior dejaba pasar NPCs que regalaban items infinitos.
        AuditState(p, ev, page, all, findings);

        foreach (var command in all.Where(x => x.Kind == CommandKind.GiveItem))
        {
            var item = p.Items.FirstOrDefault(x => x.Id == command.TargetId);
            if (item is { Price: 0 })
                Add("info", "gamefeel", "key_item_without_ceremony",
                    $"GiveItem entrega el item clave {item.Id} (price=0).",
                    "El motor dara float y mensaje, pero un objeto narrativo clave puede quedar sin ceremonia.",
                    "Considerar ShowItemGet para detener el mundo con sprite, descripcion y fanfarria.");
        }
    }

    static void AuditState(GameProject p, EventDef ev, EventPage page, List<EventCommand> commands, List<SceneAuditFinding> findings)
    {
        var location = $"event:{ev.Id}/page:{page.Id}";
        var writes = commands.Where(x => x.Kind == CommandKind.SetVariable).ToList();
        var reads = p.Events.SelectMany(x => x.Pages).SelectMany(x => x.Conditions)
            .Select(x => x.VariableId).ToHashSet(StringComparer.Ordinal);
        foreach (var write in writes.Where(x => !reads.Contains(x.TargetId)))
            findings.Add(new("warning", "state", "scene_flag_never_observed", location,
                $"SetVariable {write.TargetId}={write.Value}; ninguna pagina lee ese id.",
                "La escena escribe estado que no cambia ninguna conducta visible del juego.",
                "Agregar una pagina/condicion que materialice la consecuencia o retirar la flag decorativa."));

        foreach (var group in writes.GroupBy(x => x.TargetId, StringComparer.Ordinal)
                     .Where(g => g.Select(x => x.Value.Trim()).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            findings.Add(new("warning", "state", "conflicting_scene_flag_writes", location,
                $"{group.Key} recibe {string.Join(" -> ", group.Select(x => x.Value))} en la misma cola.",
                "La escena reescribe la misma variable sin que una pagina pueda reevaluarse entre ambos comandos.",
                "Conservar solo el valor final o separar las transiciones en escenas/paginas distintas."));

        foreach (var write in writes)
            if (page.Conditions.Any(c => c.VariableId == write.TargetId &&
                                         c.EqualsValue.Trim().Equals(write.Value.Trim(), StringComparison.OrdinalIgnoreCase)))
                findings.Add(new("warning", "state", "flag_write_repeats_page_gate", location,
                    $"La pagina exige {write.TargetId}={write.Value} y vuelve a escribir el mismo valor.",
                    "La escritura no hace avanzar esta pagina y puede dejar repetible una recompensa o cutscene.",
                    "Escribir el estado siguiente o agregar una pagina posterior que represente la consecuencia."));

        // NPC, Object, Trigger y Cutscene pueden reactivarse. El tipo no vuelve segura una
        // recompensa: lo que la cierra es una transicion de estado/pagina detectable.
        if (!commands.Any(x => RepeatSensitive.Contains(x.Kind))) return;
        var transitions = writes.Select(x => (x.TargetId, x.Value.Trim())).ToList();
        foreach (var battle in commands.Where(x => x.Kind == CommandKind.Battle)
                     .Select(x => p.Battles.FirstOrDefault(b => b.Id == x.TargetId)).Where(x => x != null))
            if (!string.IsNullOrWhiteSpace(battle!.VictoryFlag)) transitions.Add((battle.VictoryFlag, "true"));
        var closes = transitions.Any(t =>
            page.Conditions.Any(c => c.VariableId == t.TargetId &&
                                     !c.EqualsValue.Trim().Equals(t.Item2, StringComparison.OrdinalIgnoreCase)) ||
            ev.Pages.Where(x => !ReferenceEquals(x, page)).SelectMany(x => x.Conditions)
                .Any(c => c.VariableId == t.TargetId &&
                          c.EqualsValue.Trim().Equals(t.Item2, StringComparison.OrdinalIgnoreCase)));
        if (!closes)
            findings.Add(new("warning", "state", "repeatable_scene_consequence", location,
                $"{ev.Kind} contiene {string.Join(", ", commands.Where(x => RepeatSensitive.Contains(x.Kind)).Select(x => x.Kind).Distinct())} sin transicion de pagina detectable.",
                "Al volver a pisar/activar la escena, la recompensa, cobro, cambio de party o combate puede repetirse.",
                "Cerrar el beat con SetVariable y una pagina posterior/inversa, o usar la victoryFlag del combate como condicion."));
    }

    static void AuditDialogue(DialogueDef dialogue, List<SceneAuditFinding> findings)
    {
        var nodes = ReachableNodes(dialogue).ToList();
        foreach (var node in nodes)
        {
            var location = $"dialogue:{dialogue.Id}/node:{node.Id}";
            var text = node.Text ?? "";
            if (string.IsNullOrWhiteSpace(text) && node.Choices.Count == 0)
                findings.Add(new("warning", "script", "empty_dialogue_beat", location,
                    "text vacio y sin elecciones.",
                    "El jugador abre una caja sin contenido ni decision.",
                    "Escribir el beat, enlazar el nodo correcto o retirarlo."));
            if (text.Length > 320)
                findings.Add(new("info", "pacing", "long_dialogue_beat", location,
                    $"{text.Length} caracteres en un solo nodo.",
                    "La paginacion evita overflow, pero el beat puede acumular demasiadas ideas sin respiracion.",
                    "Probarlo con typewriter; dividirlo en nodos si cambia de intencion, imagen o reaccion."));
            if (node.Choices.Count > 6)
                findings.Add(new("warning", "script", "too_many_scene_choices", location,
                    $"{node.Choices.Count} opciones en una ventana.",
                    "La ventana de elecciones puede dominar la pantalla y la decision pierde legibilidad.",
                    "Agrupar, reducir o escalonar las opciones."));
            foreach (var choice in node.Choices.Where(x => x.Text.Length > 100))
                findings.Add(new("info", "script", "long_scene_choice", location,
                    $"Opcion de {choice.Text.Length} caracteres: '{Clip(choice.Text, 80)}'.",
                    "La opcion se envuelve, pero deja de poder escanearse como una decision breve.",
                    "Reducirla a la intencion del jugador y dejar la elaboracion para la respuesta."));
            if (node.Choices.Count > 1 && node.Choices.All(choice => !BranchHasDurableEffect(dialogue, choice.NextNodeId)))
                findings.Add(new("info", "continuity", "choice_needs_consequence_review", location,
                    $"{node.Choices.Count} ramas sin efecto durable detectable.",
                    "La eleccion puede ser expresiva, pero la IA debe comprobar que las respuestas respeten lo prometido por cada opcion.",
                    "Mantenerla si define voz/relacion; si promete agencia material, escribir una flag, item, dinero, batalla o ruta."));
        }

        foreach (var duplicate in nodes.Where(x => !string.IsNullOrWhiteSpace(x.Text))
                     .GroupBy(x => x.Text.Trim(), StringComparer.OrdinalIgnoreCase).Where(x => x.Count() > 1))
            findings.Add(new("info", "script", "duplicate_scene_line", $"dialogue:{dialogue.Id}",
                $"La linea '{Clip(duplicate.Key, 90)}' aparece en {duplicate.Count()} nodos.",
                "Puede ser un estribillo intencional o una copia que aplana ramas distintas.",
                "Confirmar que la repeticion tenga funcion dramatica; diferenciarla si cada rama debe reaccionar al jugador."));

        foreach (var variants in nodes.Where(x => !string.IsNullOrWhiteSpace(x.Speaker))
                     .GroupBy(x => x.Speaker.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(g => g.Select(x => x.Speaker.Trim()).Distinct(StringComparer.Ordinal).Count() > 1))
            findings.Add(new("info", "script", "speaker_name_variant", $"dialogue:{dialogue.Id}",
                $"Variantes: {string.Join(", ", variants.Select(x => $"'{x.Speaker}'").Distinct())}.",
                "El mismo hablante aparece con capitalizacion o espacios distintos.",
                "Unificar el nombre salvo que el cambio visible sea intencional."));

        foreach (var cycle in DialogueCycles(dialogue))
            findings.Add(new("warning", "structure", "dialogue_cycle_without_choice_or_exit", $"dialogue:{dialogue.Id}/node:{cycle}",
                $"La cadena NextNodeId vuelve a '{cycle}' sin eleccion ni final.",
                "El jugador queda obligado a repetir dialogo indefinidamente.",
                "Cortar la cadena, agregar una salida o convertir la repeticion en una eleccion explicita."));
    }

    static void AuditBattle(GameProject p, string battleId, List<SceneAuditFinding> findings)
    {
        var battle = p.Battles.FirstOrDefault(x => x.Id == battleId);
        if (battle is not { Boss: true }) return;
        if (string.IsNullOrWhiteSpace(battle.SongId))
            findings.Add(new("info", "audio", "boss_scene_without_song", $"battle:{battle.Id}",
                "boss=true y songId vacio.",
                "La presentacion visual de jefe existe, pero no tiene una identidad musical autorada.",
                "Asignar un tema o confirmar que conservar/silenciar la musica del mapa sea parte de la escena."));
        if (string.IsNullOrWhiteSpace(battle.BackgroundVfxId))
            findings.Add(new("info", "gamefeel", "boss_scene_without_background_vfx", $"battle:{battle.Id}",
                "boss=true y backgroundVfxId vacio.",
                "El jefe tiene aura y placa, pero el fondo conserva la presentacion base.",
                "Probar un background VFX propio si el combate es un climax; no agregarlo si compite con el sprite."));
    }

    static void AuditStory(GameProject p, StorySceneDef story, List<string> eventIds, List<string> dialogueIds, List<SceneAuditFinding> findings)
    {
        var location = $"storybook:scene:{story.Id}";
        if (eventIds.Count == 0)
            findings.Add(new("info", "structure", "story_scene_without_event_entry", location,
                $"links jugables: {story.Links.Count}; eventos enlazados: 0.",
                "La escena puede tener dialogos o contexto, pero no declara cual evento la inicia.",
                "Enlazar el evento de entrada para que la IA pueda capturar, jugar y revisar la puesta completa."));
        if (string.IsNullOrWhiteSpace(story.Synopsis))
            findings.Add(new("info", "script", "scene_without_dramatic_intent", location,
                "synopsis vacia.",
                "Sin una intencion declarada es dificil juzgar si cada beat sirve a la escena.",
                "Escribir una sinopsis causal breve: quien quiere que, que lo impide y que cambia."));
        if (string.IsNullOrWhiteSpace(story.Pov) || string.IsNullOrWhiteSpace(story.Location) || string.IsNullOrWhiteSpace(story.Time))
            findings.Add(new("info", "continuity", "scene_context_incomplete", location,
                $"pov='{story.Pov}', location='{story.Location}', time='{story.Time}'.",
                "Falta contexto para comprobar conocimiento, continuidad espacial o continuidad temporal.",
                "Completar POV/lugar/tiempo con story.scene.set antes del juicio final del guion."));
        var state = NarrativeTwin.State(p, story);
        if (story.Links.Count > 0 && !state.InSync && state.MissingLinks.Count == 0)
            findings.Add(new("warning", "continuity", "scene_twin_not_reconciled", location,
                $"gameChanged={state.GameChanged}, bookChanged={state.BookChanged}.",
                "Gameplay y prosa no estan reconciliados en el canon actual.",
                "Comparar Beats con la prosa, adaptar el lado desactualizado y ejecutar story.scene.sync solo despues del playtest."));
        if (dialogueIds.Count == 0 && string.IsNullOrWhiteSpace(story.Prose))
            findings.Add(new("warning", "script", "scene_without_script_expression", location,
                "Sin dialogos enlazados y prose vacia.",
                "No hay material verbal para revisar coherencia de guion.",
                "Enlazar el dialogo real o escribir la expresion literaria de la escena."));
    }

    static SceneAuditCoverage Coverage(List<EventCommand> commands, List<DialogueNode> nodes)
    {
        var builtIn = commands.Any(x => x.Kind is
            CommandKind.Dialogue or CommandKind.Battle or CommandKind.GiveItem or CommandKind.ShowItemGet or
            CommandKind.GiveMoney or CommandKind.TakeMoney or CommandKind.TransferPlayer or CommandKind.OpenInn or
            CommandKind.AdvanceTime or CommandKind.AddPartyMember);
        return new(
            nodes.Count > 0,
            nodes.Any(x => x.Choices.Count > 0),
            commands.Any(x => StateChanges.Contains(x.Kind)),
            commands.Any(x => x.Kind is CommandKind.MoveEvent or CommandKind.MovePlayer),
            commands.Any(x => x.Kind == CommandKind.PanCamera),
            commands.Any(x => x.Kind == CommandKind.Wait),
            commands.Any(x => x.Kind == CommandKind.ShowEmote),
            commands.Any(x => x.Kind is CommandKind.PlayVfx or CommandKind.ShowFloat or CommandKind.ShowItemGet or CommandKind.SetWeather),
            commands.Any(x => x.Kind is CommandKind.PlaySfx or CommandKind.PlaySong),
            builtIn);
    }

    static List<SceneAuditBeat> BuildBeats(List<PageContext> pages, List<DialogueDef> dialogues, StorySceneDef? story)
    {
        var beats = new List<SceneAuditBeat>();
        var index = 0;
        if (story != null && (!string.IsNullOrWhiteSpace(story.Synopsis) || !string.IsNullOrWhiteSpace(story.Prose)))
            beats.Add(new(index++, $"story:{story.Id}", "prose", story.Pov, story.Prose, "", story.Synopsis));
        var emittedDialogues = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in pages)
        {
            foreach (var command in page.Page.Commands)
            {
                beats.Add(new(index++, $"event:{page.Event.Id}/page:{page.Page.Id}", "command", "", "", command.TargetId, $"{command.Kind}:{command.Value}"));
                if (command.Kind == CommandKind.Dialogue)
                    AppendDialogue(command.TargetId);
            }
        }
        foreach (var dialogue in dialogues) AppendDialogue(dialogue.Id);
        return beats;

        void AppendDialogue(string dialogueId)
        {
            if (!emittedDialogues.Add(dialogueId)) return;
            var dialogue = dialogues.FirstOrDefault(x => x.Id == dialogueId);
            if (dialogue == null) return;
            foreach (var node in ReachableNodes(dialogue))
            {
                beats.Add(new(index++, $"dialogue:{dialogue.Id}/node:{node.Id}", "dialogue", node.Speaker, node.Text, "",
                    node.Choices.Count == 0 ? node.NextNodeId : string.Join(" | ", node.Choices.Select(x => x.Text))));
                foreach (var effect in node.Effects)
                    beats.Add(new(index++, $"dialogue:{dialogue.Id}/node:{node.Id}", "effect", "", "", effect.TargetId, $"{effect.Kind}:{effect.Value}"));
            }
        }
    }

    static List<string> Questions(SceneAuditCoverage coverage, List<DialogueNode> nodes, List<EventCommand> commands, StorySceneDef? story)
    {
        var result = new List<string>
        {
            "¿Cada personaje sabe solamente lo que podria saber en este punto, y cada reaccion nace del beat anterior?",
            "¿La escena cambia objetivo, relacion, informacion o estado; o su funcion atmosferica esta clara y no repite otra escena?",
            "¿El beat emocional central se entiende jugando sin una explicacion externa del autor?",
            "¿Los silencios, esperas y cambios de foco duran lo suficiente en una captura/playtest real, no solo al leer JSON?",
            "¿Un globo de emote, texto flotante, SFX o VFX aclararia la reaccion, o solo duplicaria algo que el sprite/dialogo ya comunica?"
        };
        if (coverage.HasChoices)
            result.Add("¿Cada opcion expresa con claridad la intencion del jugador y la respuesta reconoce esa intencion aunque las ramas vuelvan a juntarse?");
        if (coverage.HasStateChange)
            result.Add("¿La flag/consecuencia se escribe en el instante causal correcto y una segunda interaccion muestra el nuevo estado sin repetir premios?");
        if (commands.Any(x => x.Kind == CommandKind.Battle))
            result.Add("¿La entrada al combate, su identidad audiovisual y la salida comunican por que este encuentro importa en la escena?");
        if (story != null)
            result.Add("¿Gameplay y prosa cuentan el mismo canon sin convertir la novela en una transcripcion literal de cajas de texto?");
        if (nodes.Select(x => x.Speaker.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1)
            result.Add("¿Las voces son distinguibles por deseo, vocabulario y ritmo incluso si se ocultaran los nombres de speaker?");
        return result;
    }

    static List<string> SuggestedChecks(SceneAuditScope scope, List<PageContext> pages)
    {
        var result = new List<string>();
        foreach (var ev in pages.Select(x => x.Event.Id).Distinct(StringComparer.Ordinal))
        {
            foreach (var page in pages.Where(x => x.Event.Id == ev))
            {
                result.Add($"playtest.screenshot con event='{ev}', eventPageIndex={page.PageIndex} para comprobar la pagina '{page.Page.Id}' en su mapa real.");
                result.Add($"playtest.screenshot con scrub='{ev}', scrubPageIndex={page.PageIndex}, scrubSteps=0 y luego scrubSteps={Math.Max(1, page.Page.Commands.Count / 2)} para revisar composicion y ritmo.");
            }
        }
        if (scope.Kind == "story" && pages.Count == 0)
            result.Add("Enlazar un evento de entrada antes de la comprobacion visual; un dialogo aislado no permite revisar staging.");
        result.Add("Ejecutar playtest.run por la ruta real y comprobar flags/posicion antes y despues, incluida una segunda interaccion.");
        result.Add("Cerrar solo despues de mirar al menos una captura limpia y otra CRT en el beat emocional central.");
        return result;
    }

    static List<string> ExpandDialogueIds(GameProject p, IEnumerable<string> roots)
    {
        var result = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(roots.Where(x => !string.IsNullOrWhiteSpace(x)));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id)) continue;
            var dialogue = p.Dialogues.FirstOrDefault(x => x.Id == id);
            if (dialogue == null) continue;
            result.Add(id);
            foreach (var nested in ReachableNodes(dialogue).SelectMany(x => x.Effects)
                         .Where(x => x.Kind == CommandKind.Dialogue).Select(x => x.TargetId))
                queue.Enqueue(nested);
        }
        return result;
    }

    static IEnumerable<DialogueNode> ReachableNodes(DialogueDef dialogue)
    {
        var byId = dialogue.Nodes.GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (!string.IsNullOrWhiteSpace(dialogue.StartNodeId)) queue.Enqueue(dialogue.StartNodeId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id) || !byId.TryGetValue(id, out var node)) continue;
            yield return node;
            if (!string.IsNullOrWhiteSpace(node.NextNodeId)) queue.Enqueue(node.NextNodeId);
            foreach (var choice in node.Choices.Where(x => !string.IsNullOrWhiteSpace(x.NextNodeId))) queue.Enqueue(choice.NextNodeId);
        }
    }

    static bool BranchHasDurableEffect(DialogueDef dialogue, string start)
    {
        var byId = dialogue.Nodes.GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (!string.IsNullOrWhiteSpace(start)) queue.Enqueue(start);
        for (var guard = 0; queue.Count > 0 && guard < 64; guard++)
        {
            var id = queue.Dequeue();
            if (!seen.Add(id) || !byId.TryGetValue(id, out var node)) continue;
            if (node.Effects.Any(x => StateChanges.Contains(x.Kind))) return true;
            if (!string.IsNullOrWhiteSpace(node.NextNodeId)) queue.Enqueue(node.NextNodeId);
            foreach (var choice in node.Choices) queue.Enqueue(choice.NextNodeId);
        }
        return false;
    }

    static List<string> DialogueCycles(DialogueDef dialogue)
    {
        var byId = dialogue.Nodes.GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);
        var cycles = new HashSet<string>(StringComparer.Ordinal);
        foreach (var start in ReachableNodes(dialogue))
        {
            var path = new HashSet<string>(StringComparer.Ordinal);
            var current = start.Id;
            for (var guard = 0; guard < 64 && byId.TryGetValue(current, out var node); guard++)
            {
                if (node.Choices.Count > 0 || string.IsNullOrWhiteSpace(node.NextNodeId)) break;
                if (!path.Add(current)) { cycles.Add(current); break; }
                current = node.NextNodeId;
            }
        }
        return cycles.OrderBy(x => x, StringComparer.Ordinal).ToList();
    }

    static (string Icon, string Text) EmoteCue(DialogueNode node)
    {
        var text = node.Text?.Trim() ?? "";
        var lower = text.ToLowerInvariant();
        if (text.Length is 0 or > 90) return ("", text);
        if (lower.Contains("dorm") || lower.Contains("sueño") || lower.Contains("sueno") || lower.Contains("zzz")) return ("zzz", text);
        if (lower.Contains("amor") || lower.Contains("te quiero") || lower.Contains("gracias")) return ("corazon", text);
        if (lower.Contains("musica") || lower.Contains("música") || lower.Contains("cancion") || lower.Contains("canción")) return ("nota", text);
        if (text.Contains('?') && (text.StartsWith("¿") || lower.Contains("que ") || lower.Contains("qué ") || lower.Contains("como ") || lower.Contains("cómo "))) return ("?", text);
        if (text.Contains('!') && (lower.Contains("cuidado") || lower.Contains("ayuda") || lower.Contains("no puede") || lower.Contains("mira") || lower.Contains("alto"))) return ("!", text);
        if (text.Contains("...") || text.Contains('…')) return ("puntos", text);
        return ("", text);
    }

    static bool PersistentEmote(EventCommand command) =>
        command.Kind == CommandKind.ShowEmote &&
        CutsceneSteps.TryParseEmote(command.Value, out _, out var seconds) &&
        seconds >= 5f;

    static string Clip(string? text, int max)
    {
        text ??= "";
        return text.Length <= max ? text : text[..Math.Max(0, max - 1)] + "…";
    }
}
