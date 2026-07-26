using System.Text;

namespace Seto90;

/// <summary>Hallazgo explicable del auditor. Un warning indica contenido probablemente roto o
/// inalcanzable; info es una pregunta de diseno que puede ser completamente intencional.</summary>
public sealed record DesignAuditIssue(
    string Severity,
    string Code,
    string Location,
    string Message,
    string Suggestion);

public sealed record DesignAuditSummary(
    int Maps,
    int ReachableMaps,
    int Events,
    int Dialogues,
    int ReachableDialogues,
    int DialogueNodes,
    int ReachableDialogueNodes,
    int Variables,
    int Warnings,
    int Info);

/// <summary>Estimacion deliberadamente simple y reproducible. No simula estrategia ni RNG:
/// sirve para comparar encuentros y detectar saltos de ritmo, no para declarar si son divertidos.</summary>
public sealed record BattleDesignMetric(
    string BattleId,
    int EnemyCount,
    int TotalEnemyHp,
    int TotalExp,
    int TotalMoney,
    int EstimatedPartyDamagePerRound,
    int EstimatedEnemyDamagePerRound,
    int EstimatedRoundsToWin,
    int EstimatedRoundsToLose);

/// <summary>Montos potenciales definidos por contenido; no pretende sumar una partida completa
/// porque batallas, compras y ramas pueden repetirse o excluirse mutuamente.</summary>
public sealed record EconomyDesignMetric(
    int StartMoney,
    int DefinedBattlePayout,
    int ReachableScriptedGiveMoney,
    int? CheapestReachableShopItem,
    int? MostExpensiveReachableShopItem);

public sealed class DesignAuditReport
{
    public DesignAuditSummary Summary { get; init; } = new(0, 0, 0, 0, 0, 0, 0, 0, 0, 0);
    public List<DesignAuditIssue> Issues { get; init; } = [];
    public List<BattleDesignMetric> Battles { get; init; } = [];
    public EconomyDesignMetric Economy { get; init; } = new(0, 0, 0, null, null);

    public string ToHumanText()
    {
        var b = new StringBuilder();
        b.AppendLine("90s Engine - auditor de diseno");
        b.AppendLine($"Mundo: {Summary.ReachableMaps}/{Summary.Maps} mapas alcanzables; {Summary.Events} eventos.");
        b.AppendLine($"Narrativa: {Summary.ReachableDialogues}/{Summary.Dialogues} dialogos y {Summary.ReachableDialogueNodes}/{Summary.DialogueNodes} nodos alcanzables desde el mundo.");
        b.AppendLine($"Hallazgos: {Summary.Warnings} warning(s), {Summary.Info} nota(s) de revision.");
        b.AppendLine($"Economia potencial: inicio ${Economy.StartMoney}; recompensas definidas ${Economy.DefinedBattlePayout}; GiveMoney alcanzable ${Economy.ReachableScriptedGiveMoney}; tienda {MoneyRange(Economy)}.");
        if (Battles.Count > 0)
        {
            b.AppendLine("Combates (estimacion determinista, ataque basico):");
            foreach (var x in Battles)
                b.AppendLine($"- {x.BattleId}: {x.EnemyCount} enemigo(s), HP {x.TotalEnemyHp}, EXP {x.TotalExp}, $ {x.TotalMoney}, rondas ganar/perder ~{RoundText(x.EstimatedRoundsToWin)}/{RoundText(x.EstimatedRoundsToLose)}.");
        }
        foreach (var x in Issues)
        {
            b.AppendLine($"[{x.Severity.ToUpperInvariant()}] {x.Code} @ {x.Location}: {x.Message}");
            b.AppendLine($"  Sugerencia: {x.Suggestion}");
        }
        return b.ToString().TrimEnd();
    }

    static string MoneyRange(EconomyDesignMetric x) => x.CheapestReachableShopItem is null
        ? "sin precios alcanzables"
        : $"${x.CheapestReachableShopItem}..${x.MostExpensiveReachableShopItem}";
    static string RoundText(int n) => n <= 0 ? "n/d" : n.ToString();
}

/// <summary>
/// Auditor semantico puro: observa hechos del grafo de contenido sin modificar el proyecto.
/// Complementa ProjectValidator (integridad estructural) con preguntas de autoria para una IA:
/// alcance, consecuencias narrativas, estados, paginas y curvas comparables de combate/economia.
/// </summary>
public static class DesignAudit
{
    static readonly HashSet<CommandKind> DurableEffects =
    [
        CommandKind.Battle, CommandKind.SetVariable, CommandKind.GiveItem, CommandKind.OpenShop,
        CommandKind.TransferPlayer, CommandKind.AddPartyMember, CommandKind.RemovePartyMember,
        CommandKind.AdvanceTime, CommandKind.ShowItemGet, CommandKind.OpenInn,
        CommandKind.GiveMoney, CommandKind.TakeMoney
    ];

    public static DesignAuditReport Analyze(GameProject p, bool includeInfo = true)
    {
        var issues = new List<DesignAuditIssue>();
        void Add(string severity, string code, string location, string message, string suggestion) =>
            issues.Add(new(severity, code, location, message, suggestion));

        var maps = LastById(p.Maps, x => x.Id);
        var events = LastById(p.Events, x => x.Id);
        var dialogues = LastById(p.Dialogues, x => x.Id);
        var actors = LastById(p.Actors, x => x.Id);
        var enemies = LastById(p.Enemies, x => x.Id);
        var items = LastById(p.Items, x => x.Id);
        var shops = LastById(p.Shops, x => x.Id);

        var localDialogueNodes = dialogues.Values.ToDictionary(
            d => d.Id, ReachableNodes, StringComparer.Ordinal);

        // Solo eventos realmente montados en un mapa son raices del contenido jugable.
        var worldEvents = new List<EventDef>();
        foreach (var map in p.Maps)
            foreach (var eventId in map.EventIds.Distinct(StringComparer.Ordinal))
                if (events.TryGetValue(eventId, out var ev) && ev.MapId == map.Id)
                    worldEvents.Add(ev);
        worldEvents = worldEvents.DistinctBy(x => x.Id, StringComparer.Ordinal).ToList();

        var worldCommands = worldEvents.SelectMany(EventCommands).ToList();
        var reachableDialogueIds = ReachableDialogues(worldCommands, dialogues, localDialogueNodes);
        var reachableCommands = new List<EventCommand>(worldCommands);
        foreach (var dialogueId in reachableDialogueIds)
        {
            if (!dialogues.TryGetValue(dialogueId, out var dialogue)) continue;
            var nodes = LastById(dialogue.Nodes, x => x.Id);
            foreach (var nodeId in localDialogueNodes.GetValueOrDefault(dialogueId, []))
                if (nodes.TryGetValue(nodeId, out var node)) reachableCommands.AddRange(node.Effects);
        }
        var allCommands = p.Events.SelectMany(EventCommands)
            .Concat(p.Dialogues.SelectMany(d => d.Nodes).SelectMany(n => n.Effects)).ToList();

        AuditState(p, allCommands, issues);
        AuditDialogues(p, localDialogueNodes, issues);
        AuditEvents(p, maps, issues);
        AuditStory(p, issues);

        var reachableMaps = ReachableMaps(p, maps, events, dialogues, localDialogueNodes);
        foreach (var map in p.Maps.Where(m => !reachableMaps.Contains(m.Id)))
            Add("warning", "unreachable_map", $"map:{map.Id}",
                "No existe una ruta desde el mapa inicial mediante warps o TransferPlayer alcanzables.",
                "Conectar el mapa, transferir al jugador desde una escena, o borrar el borrador si ya no se usa.");

        foreach (var dialogue in p.Dialogues.Where(d => !reachableDialogueIds.Contains(d.Id)))
            Add("warning", "orphan_dialogue", $"dialogue:{dialogue.Id}",
                "Ningun evento montado ni dialogo alcanzable inicia este dialogo.",
                "Referenciarlo con Dialogue desde una escena alcanzable o retirarlo del contenido publicado.");

        AuditOrphans(p, reachableCommands, shops, issues);

        var battleMetrics = BattleMetrics(p, actors, enemies);
        var reachableShopIds = reachableCommands.Where(c => c.Kind == CommandKind.OpenShop)
            .Select(c => c.TargetId).ToHashSet(StringComparer.Ordinal);
        var reachablePrices = reachableShopIds
            .Where(shops.ContainsKey).SelectMany(id => shops[id].ItemIds)
            .Distinct(StringComparer.Ordinal).Where(items.ContainsKey).Select(id => items[id].Price)
            .Where(price => price > 0).ToList();
        var scriptedMoney = reachableCommands.Where(c => c.Kind == CommandKind.GiveMoney)
            .Select(c => int.TryParse(c.Value, out var amount) && amount > 0 ? amount : 0).Sum();
        var economy = new EconomyDesignMetric(
            p.StartMoney,
            battleMetrics.Sum(x => x.TotalMoney),
            scriptedMoney,
            reachablePrices.Count == 0 ? null : reachablePrices.Min(),
            reachablePrices.Count == 0 ? null : reachablePrices.Max());

        if (!includeInfo) issues.RemoveAll(x => x.Severity == "info");
        issues = issues
            .OrderBy(x => x.Severity == "warning" ? 0 : 1)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.Location, StringComparer.Ordinal)
            .ToList();

        var globallyReachableNodes = reachableDialogueIds.Sum(id =>
            localDialogueNodes.TryGetValue(id, out var nodes) ? nodes.Count : 0);
        var summary = new DesignAuditSummary(
            p.Maps.Count, reachableMaps.Count, p.Events.Count,
            p.Dialogues.Count, reachableDialogueIds.Count,
            p.Dialogues.Sum(d => d.Nodes.Count), globallyReachableNodes,
            p.Variables.Count,
            issues.Count(x => x.Severity == "warning"), issues.Count(x => x.Severity == "info"));

        return new DesignAuditReport { Summary = summary, Issues = issues, Battles = battleMetrics, Economy = economy };
    }

    static void AuditState(GameProject p, List<EventCommand> allCommands, List<DesignAuditIssue> issues)
    {
        var reads = p.Events.SelectMany(e => e.Pages).SelectMany(page => page.Conditions)
            .Select(c => c.VariableId).Where(id => !ReservedState(id)).ToHashSet(StringComparer.Ordinal);
        var writes = allCommands.Where(c => c.Kind == CommandKind.SetVariable).Select(c => c.TargetId)
            .Concat(p.Battles.Select(b => b.VictoryFlag)).Where(id => !string.IsNullOrWhiteSpace(id) && !ReservedState(id))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var variable in p.Variables)
        {
            var read = reads.Contains(variable.Id);
            var written = writes.Contains(variable.Id);
            if (!read && !written)
                issues.Add(new("info", "state_unused", $"variable:{variable.Id}",
                    "La variable esta declarada pero ninguna pagina la consulta ni ningun comando la cambia.",
                    "Usarla como estado narrativo o eliminarla para reducir contexto innecesario para la IA."));
            else if (read && !written)
                issues.Add(new(variable.Kind == VariableKind.Flag && variable.Default.Equals("false", StringComparison.OrdinalIgnoreCase) ? "warning" : "info",
                    "state_never_written", $"variable:{variable.Id}",
                    "Hay paginas que dependen de esta variable, pero el contenido nunca la modifica.",
                    "Agregar SetVariable/una bandera de victoria, o confirmar que el valor inicial fijo es intencional."));
            else if (written && !read)
                issues.Add(new("warning", "state_never_read", $"variable:{variable.Id}",
                    "El juego escribe esta variable pero ninguna pagina cambia su comportamiento al leerla.",
                    "Agregar una condicion que materialice la consecuencia o quitar la escritura decorativa."));
        }
    }

    static void AuditDialogues(
        GameProject p,
        Dictionary<string, HashSet<string>> localReach,
        List<DesignAuditIssue> issues)
    {
        foreach (var dialogue in p.Dialogues)
        {
            var reachable = localReach.GetValueOrDefault(dialogue.Id, []);
            var nodes = LastById(dialogue.Nodes, x => x.Id);
            foreach (var node in dialogue.Nodes)
            {
                var location = $"dialogue:{dialogue.Id}/node:{node.Id}";
                if (!reachable.Contains(node.Id))
                    issues.Add(new("warning", "unreachable_dialogue_node", location,
                        "El nodo no se alcanza desde startNodeId dentro de este dialogo.",
                        "Enlazarlo desde NextNodeId/una eleccion o eliminar el fragmento obsoleto."));
                if (node.Choices.Count > 0 && !string.IsNullOrWhiteSpace(node.NextNodeId))
                    issues.Add(new("warning", "ambiguous_dialogue_flow", location,
                        "El nodo tiene elecciones y tambien NextNodeId; dos salidas compiten por definir el avance.",
                        "Dejar NextNodeId vacio en nodos con elecciones para que la intencion sea inequivoca."));

                foreach (var duplicate in node.Choices.GroupBy(c => c.Text.Trim(), StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                    issues.Add(new("warning", "duplicate_choice_text", location,
                        $"La opcion '{duplicate.Key}' aparece {duplicate.Count()} veces.",
                        "Diferenciar el texto visible o consolidar las rutas duplicadas."));

                if (node.Choices.Count < 2) continue;
                var destinations = node.Choices.Select(c => c.NextNodeId).Distinct(StringComparer.Ordinal).ToList();
                if (destinations.Count == 1)
                {
                    issues.Add(new("info", "choice_same_destination", location,
                        "Todas las opciones llevan directamente al mismo nodo.",
                        "Mantenerlo si expresa personalidad; si promete agencia, separar consecuencias o reacciones."));
                    continue;
                }
                var traces = node.Choices.Select(c => TraceBranch(nodes, c.NextNodeId)).ToList();
                if (traces.All(t => t.DurableEffects == 0) && traces.Select(t => t.Terminal).Distinct(StringComparer.Ordinal).Count() == 1)
                    issues.Add(new("info", "choice_without_state_consequence", location,
                        "Las ramas vuelven al mismo destino sin una consecuencia durable detectable.",
                        "Puede ser una eleccion expresiva; si debe importar al gameplay, escribir una variable, item, dinero, batalla o ruta."));
            }
        }
    }

    static void AuditEvents(GameProject p, Dictionary<string, MapDef> maps, List<DesignAuditIssue> issues)
    {
        foreach (var ev in p.Events)
        {
            if (maps.TryGetValue(ev.MapId, out var owner) && !owner.EventIds.Contains(ev.Id, StringComparer.Ordinal))
                issues.Add(new("warning", "detached_event", $"event:{ev.Id}",
                    $"Declara pertenecer a '{ev.MapId}', pero ese mapa no lo monta en eventIds.",
                    "Agregar el id al mapa o borrar el evento que quedo separado."));

            for (var i = 0; i < ev.Pages.Count; i++)
            {
                var page = ev.Pages[i];
                var laterDefault = ev.Pages.FindIndex(i + 1, x => x.Conditions.Count == 0);
                if (laterDefault >= 0)
                {
                    issues.Add(new("warning", "shadowed_event_page", $"event:{ev.Id}/page:{page.Id}",
                        $"La pagina posterior {laterDefault} no tiene condiciones y siempre gana al evaluar de atras hacia adelante.",
                        "Mover la pagina por defecto al principio o agregarle condiciones."));
                    continue;
                }
                if (page.Conditions.Count == 0) continue;
                var signature = ConditionSignature(page);
                var duplicate = ev.Pages.Skip(i + 1).FirstOrDefault(x => ConditionSignature(x) == signature);
                if (duplicate is not null)
                    issues.Add(new("warning", "duplicate_event_page_conditions", $"event:{ev.Id}/page:{page.Id}",
                        $"Una pagina posterior ('{duplicate.Id}') tiene exactamente las mismas condiciones y la reemplaza siempre.",
                        "Unificar ambas paginas o hacer sus condiciones mutuamente distinguibles."));
            }
        }

        foreach (var map in p.Maps)
            foreach (var eventId in map.EventIds)
                if (p.Events.FirstOrDefault(e => e.Id == eventId) is { } ev && ev.MapId != map.Id)
                    issues.Add(new("warning", "event_map_mismatch", $"map:{map.Id}/event:{eventId}",
                        $"El mapa monta el evento, pero el evento declara mapId '{ev.MapId}'.",
                        "Hacer coincidir ambos propietarios para evitar apariciones ambiguas."));
    }

    static void AuditStory(GameProject p, List<DesignAuditIssue> issues)
    {
        if (p.StoryBook.Chapters.Count == 0) return; // proyectos previos no reciben ruido retroactivo
        if (string.IsNullOrWhiteSpace(p.StoryBook.Author))
            issues.Add(new("info", "story_missing_author", "storybook",
                "El Libro Espejo todavia no declara autor o seudonimo.",
                "Completar author con story.book.set antes de una entrega editorial."));
        foreach (var (chapter, scene) in NarrativeTwin.Scenes(p))
        {
            var location = $"storybook:{chapter.Id}/scene:{scene.Id}";
            if (string.IsNullOrWhiteSpace(scene.Prose))
                issues.Add(new("info", "story_scene_without_prose", location,
                    "La escena jugable aun no tiene una expresion literaria.",
                    "Adaptarla con story.scene.set; no copiar dialogos literalmente: traducir gameplay a voz, ritmo e interioridad."));
            if (scene.Links.Count == 0)
            {
                issues.Add(new("info", "story_scene_without_game_link", location,
                    "La escena literaria no esta enlazada a contenido jugable.",
                    "Agregar links a mapas, eventos, dialogos o combates para habilitar el camino libro -> juego."));
                continue;
            }
            var state = NarrativeTwin.State(p, scene);
            if (string.IsNullOrEmpty(scene.SyncedGameHash) || string.IsNullOrEmpty(scene.SyncedProseHash))
                issues.Add(new("info", "story_scene_unsynced", location,
                    "La escena nunca fue declarada reconciliada entre juego y libro.",
                    "Revisar ambas expresiones y ejecutar story.scene.sync; no sincronizar solo para apagar el aviso."));
            else if (!state.InSync && state.MissingLinks.Count == 0)
            {
                var side = state.GameChanged && state.BookChanged ? "el juego y el libro" : state.GameChanged ? "el juego" : "el libro";
                issues.Add(new("warning", "story_twin_drift", location,
                    $"Cambio {side} desde la ultima reconciliacion canonica.",
                    "Consultar story.query con includeSources, adaptar el otro medio, probar y sincronizar recien al cerrar la revision."));
            }
        }
    }

    static void AuditOrphans(
        GameProject p,
        List<EventCommand> reachableCommands,
        Dictionary<string, ShopDef> shops,
        List<DesignAuditIssue> issues)
    {
        var battleIds = reachableCommands.Where(c => c.Kind == CommandKind.Battle).Select(c => c.TargetId).ToHashSet(StringComparer.Ordinal);
        foreach (var battle in p.Battles.Where(x => !battleIds.Contains(x.Id)))
            issues.Add(new("warning", "orphan_battle", $"battle:{battle.Id}",
                "Ninguna escena alcanzable inicia este combate.",
                "Referenciarlo desde Battle o eliminar el encuentro obsoleto."));

        var shopIds = reachableCommands.Where(c => c.Kind == CommandKind.OpenShop).Select(c => c.TargetId).ToHashSet(StringComparer.Ordinal);
        foreach (var shop in p.Shops.Where(x => !shopIds.Contains(x.Id)))
            issues.Add(new("info", "orphan_shop", $"shop:{shop.Id}",
                "Ninguna escena alcanzable abre esta tienda.",
                "Agregar OpenShop desde un comerciante o retirar la tabla que no participa del juego."));

        var sourcedItems = reachableCommands
            .Where(c => c.Kind is CommandKind.GiveItem or CommandKind.ShowItemGet).Select(c => c.TargetId)
            .Concat(shopIds.Where(shops.ContainsKey).SelectMany(id => shops[id].ItemIds))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var item in p.Items.Where(x => !sourcedItems.Contains(x.Id)))
            issues.Add(new("info", "orphan_item", $"item:{item.Id}",
                "No hay una fuente alcanzable que entregue o venda este item.",
                "Ubicarlo en una escena/tienda o quitarlo si es un resto de produccion."));

        var learnedSkills = p.Actors.SelectMany(a => a.SkillIds).ToHashSet(StringComparer.Ordinal);
        foreach (var skill in p.Skills.Where(x => !learnedSkills.Contains(x.Id)))
            issues.Add(new("info", "orphan_skill", $"skill:{skill.Id}",
                "Ningun actor aprende esta habilidad.",
                "Asignarla a skillIds de un actor o retirarla del catalogo."));
    }

    static List<BattleDesignMetric> BattleMetrics(
        GameProject p,
        Dictionary<string, ActorDef> actors,
        Dictionary<string, EnemyDef> enemies)
    {
        var partyIds = p.PartyActorIds.Count > 0 ? p.PartyActorIds : p.Actors.Take(1).Select(a => a.Id).ToList();
        var party = partyIds.Where(actors.ContainsKey).Select(id => actors[id]).ToList();
        var leader = party.FirstOrDefault();
        var partyHp = party.Sum(a => Math.Max(0, a.Stats.Hp));
        var result = new List<BattleDesignMetric>();
        foreach (var battle in p.Battles)
        {
            var foes = battle.EnemyIds.Where(enemies.ContainsKey).Select(id => enemies[id]).ToList();
            var enemyHp = foes.Sum(e => Math.Max(0, e.Stats.Hp));
            var partyDamage = foes.Count == 0 ? 0 : party.Sum(a => SafeDamage(battle.DamageFormula, a.Stats, foes[0].Stats, a.Level));
            var enemyDamage = leader is null ? 0 : foes.Sum(e => SafeDamage(battle.DamageFormula, e.Stats, leader.Stats, 1));
            result.Add(new(
                battle.Id, foes.Count, enemyHp, foes.Sum(e => e.Exp), foes.Sum(e => e.Money),
                partyDamage, enemyDamage, Rounds(enemyHp, partyDamage), Rounds(partyHp, enemyDamage)));
        }
        return result;
    }

    static HashSet<string> ReachableMaps(
        GameProject p,
        Dictionary<string, MapDef> maps,
        Dictionary<string, EventDef> events,
        Dictionary<string, DialogueDef> dialogues,
        Dictionary<string, HashSet<string>> localDialogueNodes)
    {
        var edges = maps.Keys.ToDictionary(id => id, _ => new HashSet<string>(StringComparer.Ordinal), StringComparer.Ordinal);
        foreach (var map in p.Maps)
        {
            foreach (var warp in map.Warps) edges[map.Id].Add(warp.ToMapId);
            foreach (var eventId in map.EventIds)
            {
                if (!events.TryGetValue(eventId, out var ev) || ev.MapId != map.Id) continue;
                foreach (var command in EventCommands(ev))
                {
                    if (command.Kind == CommandKind.TransferPlayer) edges[map.Id].Add(command.TargetId);
                    if (command.Kind == CommandKind.Dialogue)
                        foreach (var destination in DialogueTransfers(command.TargetId, dialogues, localDialogueNodes, []))
                            edges[map.Id].Add(destination);
                }
            }
        }
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (maps.ContainsKey(p.StartMapId)) queue.Enqueue(p.StartMapId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!reached.Add(id) || !edges.TryGetValue(id, out var destinations)) continue;
            foreach (var next in destinations.Where(maps.ContainsKey)) queue.Enqueue(next);
        }
        return reached;
    }

    static HashSet<string> DialogueTransfers(
        string dialogueId,
        Dictionary<string, DialogueDef> dialogues,
        Dictionary<string, HashSet<string>> localReach,
        HashSet<string> visiting)
    {
        var result = new HashSet<string>(StringComparer.Ordinal);
        if (!visiting.Add(dialogueId) || !dialogues.TryGetValue(dialogueId, out var dialogue)) return result;
        var nodes = LastById(dialogue.Nodes, x => x.Id);
        foreach (var nodeId in localReach.GetValueOrDefault(dialogueId, []))
        {
            if (!nodes.TryGetValue(nodeId, out var node)) continue;
            foreach (var command in node.Effects)
            {
                if (command.Kind == CommandKind.TransferPlayer) result.Add(command.TargetId);
                else if (command.Kind == CommandKind.Dialogue)
                    result.UnionWith(DialogueTransfers(command.TargetId, dialogues, localReach, visiting));
            }
        }
        visiting.Remove(dialogueId);
        return result;
    }

    static HashSet<string> ReachableDialogues(
        List<EventCommand> roots,
        Dictionary<string, DialogueDef> dialogues,
        Dictionary<string, HashSet<string>> localReach)
    {
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>(roots.Where(c => c.Kind == CommandKind.Dialogue).Select(c => c.TargetId));
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!dialogues.TryGetValue(id, out var dialogue) || !reached.Add(id)) continue;
            var nodes = LastById(dialogue.Nodes, x => x.Id);
            foreach (var nodeId in localReach.GetValueOrDefault(id, []))
                if (nodes.TryGetValue(nodeId, out var node))
                    foreach (var nested in node.Effects.Where(c => c.Kind == CommandKind.Dialogue)) queue.Enqueue(nested.TargetId);
        }
        return reached;
    }

    static HashSet<string> ReachableNodes(DialogueDef dialogue)
    {
        var nodes = LastById(dialogue.Nodes, x => x.Id);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (!string.IsNullOrWhiteSpace(dialogue.StartNodeId)) queue.Enqueue(dialogue.StartNodeId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!reached.Add(id) || !nodes.TryGetValue(id, out var node)) continue;
            if (!string.IsNullOrWhiteSpace(node.NextNodeId)) queue.Enqueue(node.NextNodeId);
            foreach (var choice in node.Choices.Where(c => !string.IsNullOrWhiteSpace(c.NextNodeId))) queue.Enqueue(choice.NextNodeId);
        }
        return reached;
    }

    static BranchTrace TraceBranch(Dictionary<string, DialogueNode> nodes, string start)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = start;
        var effects = 0;
        for (var guard = 0; guard < 64; guard++)
        {
            if (string.IsNullOrWhiteSpace(current)) return new(effects, "end");
            if (!seen.Add(current)) return new(effects, $"cycle:{current}");
            if (!nodes.TryGetValue(current, out var node)) return new(effects, $"missing:{current}");
            effects += node.Effects.Count(e => DurableEffects.Contains(e.Kind));
            if (node.Choices.Count > 0) return new(effects, $"choice:{node.Id}");
            current = node.NextNodeId;
        }
        return new(effects, "guard-limit");
    }

    static IEnumerable<EventCommand> EventCommands(EventDef ev) => ev.Pages.SelectMany(p => p.Commands);
    static bool ReservedState(string id) => id.StartsWith("time.", StringComparison.Ordinal);
    static string ConditionSignature(EventPage page) => string.Join("|", page.Conditions
        .OrderBy(c => c.VariableId, StringComparer.Ordinal).ThenBy(c => c.EqualsValue, StringComparer.Ordinal)
        .Select(c => $"{c.VariableId}={c.EqualsValue}"));
    static int SafeDamage(string formula, StatBlock attacker, StatBlock defender, int level)
    {
        try { return Math.Max(1, FormulaValidator.EvalBasicDamage(formula, attacker, defender, level)); }
        catch { return 0; }
    }
    static int Rounds(int hp, int damage) => hp <= 0 || damage <= 0 ? 0 : (hp + damage - 1) / damage;
    static Dictionary<string, T> LastById<T>(IEnumerable<T> values, Func<T, string> id) => values
        .GroupBy(id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);
    sealed record BranchTrace(int DurableEffects, string Terminal);
}
