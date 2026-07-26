using System.Text.RegularExpressions;

namespace Seto90;

/// <summary>
/// Contrato de produccion del juego. Es opt-in para conservar compatibilidad con proyectos
/// anteriores: EnforceOnPack activa el gate que impide empaquetar con riesgos concretos.
/// </summary>
public sealed class QualityPlanDef
{
    public bool EnforceOnPack { get; set; }
    public bool RunPlaytestsOnPack { get; set; }
    public bool AuditAllScenes { get; set; } = true;
    public string CanonicalRouteId { get; set; } = "";
    public List<QualityEncounterDef> Encounters { get; set; } = [];
    public List<QualityRouteDef> Routes { get; set; } = [];
}

/// <summary>Intencion autoral de un combate: evita que una heuristica decida sola si su
/// duracion/margen son correctos. Los limites -1 significan "sin contrato explicito".</summary>
public sealed class QualityEncounterDef
{
    public string BattleId { get; set; } = "";
    public string Requirement { get; set; } = "required";
    public string Role { get; set; } = "common";
    public int MinPreparedActions { get; set; } = -1;
    public int MaxPreparedActions { get; set; } = -1;
    public int MinPreparedHpPercent { get; set; } = -1;
}

/// <summary>Una ruta es una secuencia autorada de checkpoints. CanonChoices cierra las
/// ramificaciones de dialogo para que balance.audit no sume caminos mutuamente excluyentes.</summary>
public sealed class QualityRouteDef
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public List<StoryCanonChoiceDef> CanonChoices { get; set; } = [];
    public List<QualityCheckpointDef> Checkpoints { get; set; } = [];
}

/// <summary>
/// Hito posterior a Steps. EventId/PageIndex alimentan scene.audit y balance.audit; Steps
/// alimentan el runtime real. Las expectativas generan asserts automaticos al correr la ruta.
/// </summary>
public sealed class QualityCheckpointDef
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string EventId { get; set; } = "";
    public int PageIndex { get; set; } = -1;
    public string BattleId { get; set; } = "";
    public string ExpectedMapId { get; set; } = "";
    public int ExpectedMinMoney { get; set; } = -1;
    public int ExpectedMaxMoney { get; set; } = -1;
    public List<string> ExpectedPartyActorIds { get; set; } = [];
    public List<QualityExpectedLevelDef> ExpectedLevels { get; set; } = [];
    public List<QualityExpectedFlagDef> ExpectedFlags { get; set; } = [];
    public List<string> ExpectedItemIds { get; set; } = [];
    public bool RunInPlaytest { get; set; } = true;
    public List<string> Steps { get; set; } = [];
}

public sealed class QualityExpectedLevelDef
{
    public string ActorId { get; set; } = "";
    public int Min { get; set; } = 1;
    public int Max { get; set; } = 1;
}

public sealed class QualityExpectedFlagDef
{
    public string VariableId { get; set; } = "";
    public bool Value { get; set; } = true;
}

/// <summary>Validacion referencial del contrato de calidad. La calidad subjetiva vive en
/// QualityAudit; aca solo se rechazan rutas ambiguas o imposibles de ejecutar.</summary>
public static class QualityPlanValidator
{
    static readonly Regex IdPattern = new("^[a-z][a-z0-9_.-]*$", RegexOptions.Compiled);
    static readonly HashSet<string> Requirements = ["required", "optional", "repeatable"];
    static readonly HashSet<string> Roles = ["tutorial", "common", "elite", "boss"];
    static readonly HashSet<string> StepVerbs =
    [
        "event", "goto", "move", "face", "interact", "auto", "choose", "confirm",
        "cancel", "up", "down", "wait", "assert-flag", "assert-map", "assert-item",
        "assert-pos", "assert-money", "assert-party", "assert-level", "screenshot",
        "checkpoint", "dump"
    ];

    public static void Validate(GameProject p, ValidationResult r)
    {
        var plan = p.QualityPlan ?? new QualityPlanDef();
        var routeIds = plan.Routes.Select(x => x.Id).ToList();
        Duplicate(routeIds, "quality_route_duplicate", "rutas de calidad", r);
        if (plan.EnforceOnPack && plan.Routes.Count == 0)
            r.Issues.Add(new("quality_gate_without_routes",
                "qualityPlan.enforceOnPack esta activo pero no hay rutas declaradas.",
                "Crear al menos una ruta con quality.route.set o desactivar enforceOnPack."));
        if (!string.IsNullOrWhiteSpace(plan.CanonicalRouteId) &&
            !routeIds.Contains(plan.CanonicalRouteId, StringComparer.Ordinal))
            r.Issues.Add(new("missing_canonical_route",
                $"La ruta canonica '{plan.CanonicalRouteId}' no existe.",
                "Crear esa ruta o corregir qualityPlan.canonicalRouteId."));
        if (plan.EnforceOnPack && string.IsNullOrWhiteSpace(plan.CanonicalRouteId))
            r.Issues.Add(new("quality_gate_without_canonical_route",
                "El gate de pack requiere una ruta canonica.",
                "Definir qualityPlan.canonicalRouteId con quality.plan.set."));

        var battles = p.Battles.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var actors = p.Actors.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var items = p.Items.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var variables = p.Variables.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var maps = p.Maps.Select(x => x.Id).ToHashSet(StringComparer.Ordinal);
        var events = p.Events.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var dialogues = p.Dialogues.ToDictionary(x => x.Id, StringComparer.Ordinal);

        Duplicate(plan.Encounters.Select(x => x.BattleId), "quality_encounter_duplicate", "encuentros clasificados", r);
        foreach (var encounter in plan.Encounters)
        {
            if (!battles.Contains(encounter.BattleId))
                r.Issues.Add(new("missing_quality_battle", $"qualityPlan clasifica el combate inexistente '{encounter.BattleId}'.", "Corregir battleId o crear el combate."));
            if (!Requirements.Contains(encounter.Requirement))
                r.Issues.Add(new("bad_encounter_requirement", $"{encounter.BattleId}: requirement '{encounter.Requirement}'.", "Usar required, optional o repeatable."));
            if (!Roles.Contains(encounter.Role))
                r.Issues.Add(new("bad_encounter_role", $"{encounter.BattleId}: role '{encounter.Role}'.", "Usar tutorial, common, elite o boss."));
            if (encounter.MinPreparedActions < -1 || encounter.MaxPreparedActions < -1 ||
                encounter.MaxPreparedActions >= 0 && encounter.MinPreparedActions > encounter.MaxPreparedActions)
                r.Issues.Add(new("bad_encounter_action_range", $"{encounter.BattleId}: rango de acciones {encounter.MinPreparedActions}..{encounter.MaxPreparedActions}.", "Usar -1 (sin limite) o un rango creciente >= 0."));
            if (encounter.MinPreparedHpPercent is < -1 or > 100)
                r.Issues.Add(new("bad_encounter_hp_floor", $"{encounter.BattleId}: minPreparedHpPercent={encounter.MinPreparedHpPercent}.", "Usar -1 o 0..100."));
        }

        foreach (var route in plan.Routes)
        {
            if (!IdPattern.IsMatch(route.Id))
                r.Issues.Add(new("bad_quality_route_id", $"Ruta con id invalido '{route.Id}'.", "Usar minusculas, numeros, puntos, guiones o guion bajo."));
            if (route.Checkpoints.Count == 0)
                r.Issues.Add(new("quality_route_empty", $"Ruta {route.Id} no tiene checkpoints.", "Agregar hitos con quality.route.set."));
            Duplicate(route.Checkpoints.Select(x => x.Id), "quality_checkpoint_duplicate", $"checkpoints de {route.Id}", r);
            Duplicate(route.CanonChoices.Select(x => $"{x.DialogueId}/{x.NodeId}"), "quality_choice_duplicate", $"elecciones canonicas de {route.Id}", r);
            ValidateCanonChoices(route, dialogues, r);
            foreach (var checkpoint in route.Checkpoints)
                ValidateCheckpoint(route, checkpoint, maps, events, battles, actors, items, variables, r);
        }
        if (plan.EnforceOnPack && plan.RunPlaytestsOnPack)
        {
            var canonical = plan.Routes.FirstOrDefault(x => x.Id == plan.CanonicalRouteId);
            if (canonical != null && canonical.Checkpoints.All(x => !x.RunInPlaytest))
                r.Issues.Add(new("quality_gate_without_playtest",
                    $"La ruta canonica '{canonical.Id}' no tiene checkpoints ejecutables, pero runPlaytestsOnPack esta activo.",
                    "Marcar al menos un checkpoint con runInPlaytest=true o desactivar runPlaytestsOnPack."));
        }
    }

    static void ValidateCheckpoint(
        QualityRouteDef route,
        QualityCheckpointDef checkpoint,
        HashSet<string> maps,
        Dictionary<string, EventDef> events,
        HashSet<string> battles,
        HashSet<string> actors,
        HashSet<string> items,
        HashSet<string> variables,
        ValidationResult r)
    {
        var owner = $"Ruta {route.Id}, checkpoint {checkpoint.Id}";
        if (!IdPattern.IsMatch(checkpoint.Id))
            r.Issues.Add(new("bad_quality_checkpoint_id", $"{owner}: id invalido.", "Usar un id estable en minusculas."));
        if (string.IsNullOrWhiteSpace(checkpoint.EventId) && checkpoint.Steps.Count == 0)
            r.Issues.Add(new("quality_checkpoint_without_driver", $"{owner} no declara eventId ni steps.", "Enlazar un evento o agregar pasos de playtest."));
        if (!string.IsNullOrWhiteSpace(checkpoint.EventId))
        {
            if (!events.TryGetValue(checkpoint.EventId, out var ev))
                r.Issues.Add(new("missing_quality_event", $"{owner}: evento '{checkpoint.EventId}' inexistente.", "Corregir eventId."));
            else if (checkpoint.PageIndex >= ev.Pages.Count)
                r.Issues.Add(new("bad_quality_page", $"{owner}: pageIndex {checkpoint.PageIndex}, pero {checkpoint.EventId} tiene {ev.Pages.Count} paginas.", "Usar -1 o un indice existente."));
        }
        if (checkpoint.PageIndex < -1)
            r.Issues.Add(new("bad_quality_page", $"{owner}: pageIndex {checkpoint.PageIndex}.", "Usar -1 o un indice >= 0."));
        if (!string.IsNullOrWhiteSpace(checkpoint.BattleId) && !battles.Contains(checkpoint.BattleId))
            r.Issues.Add(new("missing_quality_battle", $"{owner}: combate '{checkpoint.BattleId}' inexistente.", "Corregir battleId."));
        if (!string.IsNullOrWhiteSpace(checkpoint.ExpectedMapId) && !maps.Contains(checkpoint.ExpectedMapId))
            r.Issues.Add(new("missing_quality_map", $"{owner}: mapa esperado '{checkpoint.ExpectedMapId}' inexistente.", "Corregir expectedMapId."));
        if (checkpoint.ExpectedMinMoney < -1 || checkpoint.ExpectedMaxMoney < -1 ||
            checkpoint.ExpectedMaxMoney >= 0 && checkpoint.ExpectedMinMoney > checkpoint.ExpectedMaxMoney)
            r.Issues.Add(new("bad_quality_money_range", $"{owner}: dinero {checkpoint.ExpectedMinMoney}..{checkpoint.ExpectedMaxMoney}.", "Usar -1 o un rango creciente >= 0."));
        foreach (var id in checkpoint.ExpectedPartyActorIds.Where(id => !actors.Contains(id)))
            r.Issues.Add(new("missing_quality_actor", $"{owner}: actor esperado '{id}' inexistente.", "Corregir expectedPartyActorIds."));
        Duplicate(checkpoint.ExpectedLevels.Select(x => x.ActorId), "quality_level_duplicate", $"niveles de {route.Id}/{checkpoint.Id}", r);
        foreach (var level in checkpoint.ExpectedLevels)
        {
            if (!actors.Contains(level.ActorId))
                r.Issues.Add(new("missing_quality_actor", $"{owner}: nivel esperado para actor inexistente '{level.ActorId}'.", "Corregir expectedLevels."));
            if (level.Min < 1 || level.Max < level.Min)
                r.Issues.Add(new("bad_quality_level_range", $"{owner}: {level.ActorId} nivel {level.Min}..{level.Max}.", "Usar un rango creciente desde nivel 1."));
        }
        foreach (var flag in checkpoint.ExpectedFlags)
            if (!variables.Contains(flag.VariableId) && flag.VariableId is not ("time.dia" or "time.franja"))
                r.Issues.Add(new("missing_quality_flag", $"{owner}: flag esperada '{flag.VariableId}' inexistente.", "Definir la variable o corregir expectedFlags."));
        foreach (var id in checkpoint.ExpectedItemIds.Where(id => !items.Contains(id)))
            r.Issues.Add(new("missing_quality_item", $"{owner}: item esperado '{id}' inexistente.", "Corregir expectedItemIds."));
        foreach (var step in checkpoint.Steps)
        {
            var verb = step.Trim().Split(' ', 2)[0].ToLowerInvariant();
            if (verb.Length > 0 && !verb.StartsWith('#') && !StepVerbs.Contains(verb))
                r.Issues.Add(new("bad_quality_step", $"{owner}: paso desconocido '{step}'.", $"Usar: {string.Join(", ", StepVerbs.OrderBy(x => x))}."));
        }
    }

    static void ValidateCanonChoices(QualityRouteDef route, Dictionary<string, DialogueDef> dialogues, ValidationResult r)
    {
        foreach (var choice in route.CanonChoices)
        {
            var owner = $"Ruta {route.Id}: canon {choice.DialogueId}/{choice.NodeId}[{choice.ChoiceIndex}]";
            if (!dialogues.TryGetValue(choice.DialogueId, out var dialogue))
            {
                r.Issues.Add(new("missing_quality_dialogue", $"{owner}: dialogo inexistente.", "Corregir dialogueId."));
                continue;
            }
            var node = dialogue.Nodes.FirstOrDefault(x => x.Id == choice.NodeId);
            if (node == null || choice.ChoiceIndex < 0 || choice.ChoiceIndex >= node.Choices.Count)
                r.Issues.Add(new("bad_quality_choice", $"{owner}: opcion inexistente.", "Corregir nodeId/choiceIndex."));
        }
    }

    static void Duplicate(IEnumerable<string> ids, string code, string owner, ValidationResult r)
    {
        foreach (var id in ids.Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(x => x, StringComparer.Ordinal).Where(x => x.Count() > 1))
            r.Issues.Add(new(code, $"{owner}: id '{id.Key}' repetido.", "Dejar una sola definicion por id."));
    }
}
