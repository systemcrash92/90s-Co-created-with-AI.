using System.Text;
using System.Text.Json.Nodes;

namespace Seto90;

public sealed record QualityAuditIssue(
    string Severity,
    string Source,
    string Code,
    string Location,
    string Evidence,
    string Message,
    string Suggestion);

public sealed record QualitySceneResult(
    string EventId,
    int PageIndex,
    string PageId,
    int Warnings,
    int ReviewNotes,
    string Verdict);

public sealed record QualityRoutePlaytestResult(
    bool Declared,
    bool Ran,
    bool Ok,
    int Steps,
    int Frames,
    JsonNode? Report);

public sealed record QualityRouteResult(
    string RouteId,
    string Name,
    int Checkpoints,
    int ClassifiedBattles,
    BalanceAuditReport Balance,
    QualityRoutePlaytestResult Playtest);

public sealed record QualityAuditSummary(
    bool ValidationOk,
    bool AssetsOk,
    int Routes,
    int Checkpoints,
    int Scenes,
    int PlaytestsRun,
    int PlaytestsPassed,
    int Warnings,
    int ReviewNotes,
    bool ReadyForPack,
    string Verdict);

/// <summary>Un unico dossier para decidir si el juego/capitulo puede cerrarse. Conserva
/// la evidencia de cada sistema; warning bloquea, info exige juicio humano/IA.</summary>
public sealed class QualityAuditReport
{
    public QualityAuditSummary Summary { get; init; } =
        new(false, false, 0, 0, 0, 0, 0, 0, 0, false, "blocked");
    public DesignAuditSummary? Design { get; init; }
    public List<QualityRouteResult> Routes { get; init; } = [];
    public List<QualitySceneResult> Scenes { get; init; } = [];
    public List<QualityAuditIssue> Issues { get; init; } = [];
    public List<string> AiReviewQuestions { get; init; } = [];
    public List<string> SuggestedChecks { get; init; } = [];

    public string ToHumanText()
    {
        var b = new StringBuilder();
        b.AppendLine("90s Engine - director de calidad");
        b.AppendLine($"Veredicto: {Summary.Verdict}. ReadyForPack={Summary.ReadyForPack}; " +
                     $"{Summary.Warnings} warning(s), {Summary.ReviewNotes} nota(s).");
        b.AppendLine($"Cobertura: validacion={(Summary.ValidationOk ? "OK" : "FALLO")}, assets={(Summary.AssetsOk ? "OK" : "FALLO")}, " +
                     $"{Summary.Routes} ruta(s), {Summary.Checkpoints} checkpoint(s), {Summary.Scenes} escena(s), " +
                     $"playtests {Summary.PlaytestsPassed}/{Summary.PlaytestsRun}.");
        foreach (var route in Routes)
        {
            var playtest = !route.Playtest.Declared ? "sin guion" :
                !route.Playtest.Ran ? "pendiente" :
                route.Playtest.Ok ? $"OK ({route.Playtest.Steps} pasos/{route.Playtest.Frames} frames)" : "FALLO";
            b.AppendLine($"- Ruta {route.RouteId}: {route.Checkpoints} hitos, balance {route.Balance.Summary.Verdict}, playtest {playtest}.");
            foreach (var battle in route.Balance.Battles)
                b.AppendLine($"  {battle.BattleId}: Nv {LevelText(battle)}, preparado {Outcome(battle.Prepared)}, " +
                             $"{battle.Prepared.PlayerActions} acciones, HP {battle.Prepared.HpRemainingPercent}%.");
        }
        foreach (var issue in Issues)
        {
            b.AppendLine($"[{issue.Severity.ToUpperInvariant()}][{issue.Source}] {issue.Code} @ {issue.Location}: {issue.Message}");
            if (!string.IsNullOrWhiteSpace(issue.Evidence)) b.AppendLine($"  Evidencia: {issue.Evidence}");
            b.AppendLine($"  Sugerencia: {issue.Suggestion}");
        }
        b.AppendLine("Preguntas para la IA/autoria:");
        foreach (var question in AiReviewQuestions) b.AppendLine($"- {question}");
        b.AppendLine("Comprobaciones sugeridas:");
        foreach (var check in SuggestedChecks) b.AppendLine($"- {check}");
        return b.ToString().TrimEnd();
    }

    static string Outcome(BalanceBattleSimulation x) => x.Victory ? "victoria" : x.Defeat ? "derrota" : "sin resolver";
    static string LevelText(BalanceBattleCheckpoint x) => x.Party.Count == 0
        ? "n/d"
        : string.Join("/", x.Party.Select(p => p.Level));
}

/// <summary>
/// Orquestador de calidad. No modifica contenido: combina integridad, assets, diseno,
/// escenas, balance por ruta y playtests deterministas. Una ruta declarada reemplaza
/// la inferencia global de orden/ramas para las decisiones numericas.
/// </summary>
public static class QualityAudit
{
    static readonly HashSet<CommandKind> AuditableCommands =
    [
        CommandKind.Dialogue, CommandKind.Battle, CommandKind.SetVariable,
        CommandKind.GiveItem, CommandKind.ShowItemGet, CommandKind.OpenShop,
        CommandKind.OpenInn, CommandKind.TransferPlayer, CommandKind.Wait,
        CommandKind.MoveEvent, CommandKind.MovePlayer, CommandKind.PanCamera,
        CommandKind.PlaySfx, CommandKind.PlayVfx, CommandKind.ShowFloat,
        CommandKind.ShowEmote, CommandKind.AddPartyMember, CommandKind.RemovePartyMember,
        CommandKind.AdvanceTime, CommandKind.GiveMoney, CommandKind.TakeMoney
    ];

    public static QualityAuditReport Analyze(
        GameProject p,
        string projectRoot = "",
        bool includeInfo = true,
        bool runPlaytests = false,
        string routeId = "")
    {
        var issues = new List<QualityAuditIssue>();
        void Add(string severity, string source, string code, string location, string evidence, string message, string suggestion) =>
            issues.Add(new(severity, source, code, location, evidence, message, suggestion));

        var validation = ProjectValidator.Validate(p);
        foreach (var issue in validation.Issues)
            Add("warning", "validation", issue.Code, "project", issue.Message,
                "El proyecto no supera la integridad estructural.", issue.Fix);

        AssetReport assetReport;
        // quality.audit tambien vive en MCP stdio: raylib no puede contaminar stdout con el
        // log de LoadImage porque rompería el framing JSON-RPC.
        Raylib_cs.Raylib.SetTraceLogLevel(Raylib_cs.TraceLogLevel.None);
        try
        {
            assetReport = AssetPipeline.Validate(p, string.IsNullOrWhiteSpace(projectRoot) ? null : projectRoot);
        }
        finally
        {
            Raylib_cs.Raylib.SetTraceLogLevel(Raylib_cs.TraceLogLevel.Info);
        }
        foreach (var issue in assetReport.Issues)
            Add("warning", "assets", issue.Code, "assets", issue.Message,
                "Un asset o restriccion retro no esta listo para distribuir.", issue.Fix);

        if (!validation.Ok)
            return Finish(validation.Ok, assetReport.Ok, null, [], [], issues, includeInfo);

        var design = DesignAudit.Analyze(p, includeInfo);
        foreach (var issue in design.Issues)
            Add(issue.Severity, "design", issue.Code, issue.Location, issue.Message,
                "El auditor de diseno encontro un riesgo o decision pendiente.", issue.Suggestion);

        var selectedRoutes = SelectRoutes(p, routeId);
        var routeResults = new List<QualityRouteResult>();
        if (selectedRoutes.Count == 0)
        {
            var global = BalanceAudit.Analyze(p, includeInfo);
            AddBalanceIssues(global, "global", Add);
            routeResults.Add(new(
                "global", "Proyeccion inferida (sin ruta declarada)", global.Battles.Count, 0, global,
                new(false, false, false, 0, 0, null)));
            if (includeInfo)
                Add("info", "route", "quality_routes_not_declared", "qualityPlan",
                    "El balance tuvo que inferir orden y fusionar ramas.",
                    "Todavia no existe una ruta canonica verificable.",
                    "Declarar qualityPlan + quality.route.set para obtener checkpoints y playtests exactos.");
        }
        else
        {
            foreach (var route in selectedRoutes)
            {
                var balance = BalanceAudit.Analyze(p, includeInfo, route.Id);
                AddBalanceIssues(balance, route.Id, Add);
                AuditRouteChoices(p, route, includeInfo, Add);
                AuditEncounters(p, route, balance, includeInfo, Add);
                var playtest = runPlaytests ? RunRoutePlaytest(p, projectRoot, route) : PendingPlaytest(route);
                if (playtest.Ran && !playtest.Ok)
                    Add("warning", "playtest", "quality_route_playtest_failed", $"route:{route.Id}",
                        PlaytestFailureEvidence(playtest.Report),
                        "La ruta declarada no alcanzo todos sus hitos dentro del runtime real.",
                        "Leer el primer paso fallido; corregir ruta/contenido y repetir quality.audit con runPlaytests=true.");
                else if (!playtest.Ran && playtest.Declared && includeInfo)
                    Add("info", "playtest", "quality_route_playtest_pending", $"route:{route.Id}",
                        $"{playtest.Steps} pasos declarados.",
                        "La ruta tiene guion pero no se ejecuto en esta auditoria.",
                        "Repetir con runPlaytests=true antes de cerrar el capitulo.");
                routeResults.Add(new(
                    route.Id,
                    string.IsNullOrWhiteSpace(route.Name) ? route.Id : route.Name,
                    route.Checkpoints.Count,
                    route.Checkpoints.Count(x => !string.IsNullOrWhiteSpace(x.BattleId)),
                    balance,
                    playtest));
            }
        }

        AuditEncounterCoverage(p, selectedRoutes, includeInfo, Add);
        var scenes = AuditScenes(p, selectedRoutes, includeInfo, Add);
        return Finish(validation.Ok, assetReport.Ok, design.Summary, routeResults, scenes, issues, includeInfo);
    }

    /// <summary>Gate usado por pack/publish. Solo corre si el proyecto lo habilito; las notas
    /// editoriales no bloquean, los warnings y playtests fallidos si.</summary>
    public static QualityAuditReport? CheckForPack(GameProject p, string projectRoot)
    {
        if (!p.QualityPlan.EnforceOnPack) return null;
        var report = Analyze(
            p, projectRoot, includeInfo: false,
            runPlaytests: p.QualityPlan.RunPlaytestsOnPack,
            routeId: p.QualityPlan.CanonicalRouteId);
        return report.Summary.ReadyForPack ? null : report;
    }

    static List<QualityRouteDef> SelectRoutes(GameProject p, string routeId)
    {
        if (!string.IsNullOrWhiteSpace(routeId))
        {
            var route = p.QualityPlan.Routes.FirstOrDefault(x => x.Id == routeId)
                ?? throw new KeyNotFoundException($"No existe la ruta de calidad '{routeId}'.");
            return [route];
        }
        if (!string.IsNullOrWhiteSpace(p.QualityPlan.CanonicalRouteId))
        {
            var canonical = p.QualityPlan.Routes.FirstOrDefault(x => x.Id == p.QualityPlan.CanonicalRouteId);
            if (canonical != null) return [canonical];
        }
        return p.QualityPlan.Routes.ToList();
    }

    static void AddBalanceIssues(
        BalanceAuditReport balance,
        string routeId,
        Action<string, string, string, string, string, string, string> add)
    {
        foreach (var finding in balance.Findings)
            add(finding.Severity, "balance", finding.Code,
                $"route:{routeId}/{finding.Location}", finding.Evidence, finding.Message, finding.Suggestion);
    }

    static void AuditRouteChoices(
        GameProject p,
        QualityRouteDef route,
        bool includeInfo,
        Action<string, string, string, string, string, string, string> add)
    {
        if (!includeInfo) return;
        var declared = route.CanonChoices.Select(x => $"{x.DialogueId}/{x.NodeId}").ToHashSet(StringComparer.Ordinal);
        foreach (var checkpoint in route.Checkpoints)
        {
            var ev = p.Events.FirstOrDefault(x => x.Id == checkpoint.EventId);
            if (ev == null) continue;
            var pages = checkpoint.PageIndex >= 0 && checkpoint.PageIndex < ev.Pages.Count
                ? [ev.Pages[checkpoint.PageIndex]]
                : ev.Pages;
            var dialogueIds = pages.SelectMany(x => x.Commands)
                .Where(x => x.Kind == CommandKind.Dialogue).Select(x => x.TargetId).Distinct(StringComparer.Ordinal);
            foreach (var dialogueId in dialogueIds)
            {
                var dialogue = p.Dialogues.FirstOrDefault(x => x.Id == dialogueId);
                if (dialogue == null) continue;
                foreach (var node in dialogue.Nodes.Where(x => x.Choices.Count > 1))
                    if (!declared.Contains($"{dialogue.Id}/{node.Id}"))
                        add("info", "route", "route_choice_not_declared",
                            $"route:{route.Id}/dialogue:{dialogue.Id}/node:{node.Id}",
                            $"{node.Choices.Count} opciones; ninguna canonica en la ruta.",
                            "El balance fusiona esta bifurcacion porque no sabe que opcion pertenece al canon.",
                            "Agregar dialogueId/nodeId/choiceIndex a canonChoices o confirmar que las ramas son equivalentes.");
            }
        }
    }

    static void AuditEncounters(
        GameProject p,
        QualityRouteDef route,
        BalanceAuditReport balance,
        bool includeInfo,
        Action<string, string, string, string, string, string, string> add)
    {
        var contracts = p.QualityPlan.Encounters.ToDictionary(x => x.BattleId, StringComparer.Ordinal);
        var simulated = balance.Battles.Select(x => x.BattleId).ToHashSet(StringComparer.Ordinal);
        var declared = route.Checkpoints.Select(x => x.BattleId)
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal);
        foreach (var checkpoint in route.Checkpoints.Where(x =>
                     !string.IsNullOrWhiteSpace(x.BattleId) && !simulated.Contains(x.BattleId)))
            add("warning", "route", "route_battle_not_observed",
                $"route:{route.Id}/checkpoint:{checkpoint.Id}",
                $"El checkpoint declara {checkpoint.BattleId}, pero su evento/pagina no lo incorporo a la proyeccion.",
                "El contrato podria dar por cubierto un combate que la ruta numerica nunca ejecuto.",
                "Enlazar el evento/pagina que dispara ese BattleDef o corregir battleId.");
        if (includeInfo)
            foreach (var battleId in simulated.Where(x => !declared.Contains(x)))
                add("info", "route", "route_battle_without_checkpoint",
                    $"route:{route.Id}/battle:{battleId}",
                    "El balance encontro el combate dentro de un checkpoint, pero ningun hito lo nombra en battleId.",
                    "El encuentro se simula, aunque no queda explicitamente clasificado en la secuencia.",
                    "Asignar battleId al checkpoint que lo contiene.");
        foreach (var battle in balance.Battles)
        {
            if (!contracts.TryGetValue(battle.BattleId, out var contract)) continue;
            var location = $"route:{route.Id}/battle:{battle.BattleId}";
            if (contract.MinPreparedActions >= 0 && battle.Prepared.PlayerActions < contract.MinPreparedActions)
                add("warning", "intent", "encounter_shorter_than_contract", location,
                    $"{battle.Prepared.PlayerActions} acciones; minimo autorado {contract.MinPreparedActions}.",
                    "El encuentro no alcanza la presencia tactica declarada.",
                    "Revisar stats/patron o bajar el minimo si el ritmo real es el deseado.");
            if (contract.MaxPreparedActions >= 0 && battle.Prepared.PlayerActions > contract.MaxPreparedActions)
                add("warning", "intent", "encounter_longer_than_contract", location,
                    $"{battle.Prepared.PlayerActions} acciones; maximo autorado {contract.MaxPreparedActions}.",
                    "El encuentro excede el presupuesto de ritmo declarado.",
                    "Reducir HP/defensa, mejorar opciones ofensivas o ampliar el contrato conscientemente.");
            if (contract.MinPreparedHpPercent >= 0 && battle.Prepared.HpRemainingPercent < contract.MinPreparedHpPercent)
                add("warning", "intent", "encounter_margin_below_contract", location,
                    $"HP final {battle.Prepared.HpRemainingPercent}%; piso autorado {contract.MinPreparedHpPercent}%.",
                    "El encuentro castiga mas de lo que declara su rol.",
                    "Ajustar dano/recursos o bajar el piso despues de un playtest deliberado.");
            var def = p.Battles.FirstOrDefault(x => x.Id == battle.BattleId);
            if (includeInfo && def != null && contract.Role == "boss" != def.Boss)
                add("info", "intent", "encounter_role_mismatch", location,
                    $"Contrato role={contract.Role}; BattleDef.Boss={def.Boss}.",
                    "La presentacion visual y la intencion de dificultad no coinciden.",
                    "Alinear role y boss, o documentar por que un elite usa/no usa presentacion de jefe.");
        }
    }

    static void AuditEncounterCoverage(
        GameProject p,
        List<QualityRouteDef> routes,
        bool includeInfo,
        Action<string, string, string, string, string, string, string> add)
    {
        var canonical = routes.FirstOrDefault(x => x.Id == p.QualityPlan.CanonicalRouteId) ?? routes.FirstOrDefault();
        var routedBattles = canonical?.Checkpoints.Select(x => x.BattleId)
            .Where(x => !string.IsNullOrWhiteSpace(x)).ToHashSet(StringComparer.Ordinal) ?? [];
        var contracts = p.QualityPlan.Encounters.ToDictionary(x => x.BattleId, StringComparer.Ordinal);
        foreach (var contract in p.QualityPlan.Encounters.Where(x => x.Requirement == "required"))
            if (!routedBattles.Contains(contract.BattleId))
                add("warning", "route", "required_battle_missing_from_route", $"battle:{contract.BattleId}",
                    $"Encuentro required, ruta canonica {(canonical?.Id ?? "ausente")}.",
                    "Un combate obligatorio no aparece en los checkpoints canonicos.",
                    "Agregar el checkpoint o reclasificarlo como optional/repeatable.");
        if (!includeInfo) return;
        foreach (var battle in p.Battles.Where(x => !contracts.ContainsKey(x.Id)))
            add("info", "intent", "battle_without_quality_contract", $"battle:{battle.Id}",
                "No tiene requirement/role ni limites de ritmo/margen.",
                "El director no conoce la intencion del encuentro.",
                "Clasificarlo en qualityPlan.encounters.");
    }

    static List<QualitySceneResult> AuditScenes(
        GameProject p,
        List<QualityRouteDef> routes,
        bool includeInfo,
        Action<string, string, string, string, string, string, string> add)
    {
        var scopes = new List<(EventDef Event, int PageIndex)>();
        if (p.QualityPlan.AuditAllScenes || routes.Count == 0)
        {
            foreach (var ev in p.Events)
                for (var i = 0; i < ev.Pages.Count; i++)
                    if (ev.Pages[i].Commands.Any(x => AuditableCommands.Contains(x.Kind)))
                        scopes.Add((ev, i));
        }
        else
        {
            foreach (var checkpoint in routes.SelectMany(x => x.Checkpoints))
            {
                var ev = p.Events.FirstOrDefault(x => x.Id == checkpoint.EventId);
                if (ev == null) continue;
                if (checkpoint.PageIndex >= 0) scopes.Add((ev, checkpoint.PageIndex));
                else for (var i = 0; i < ev.Pages.Count; i++) scopes.Add((ev, i));
            }
        }

        var results = new List<QualitySceneResult>();
        foreach (var (ev, pageIndex) in scopes
                     .DistinctBy(x => $"{x.Event.Id}/{x.PageIndex}", StringComparer.Ordinal)
                     .OrderBy(x => x.Event.MapId, StringComparer.Ordinal)
                     .ThenBy(x => x.Event.Id, StringComparer.Ordinal)
                     .ThenBy(x => x.PageIndex))
        {
            var report = SceneAudit.AnalyzeEvent(
                p, ev.Id, includeInfo: includeInfo, includeTranscript: false, pageIndex: pageIndex);
            results.Add(new(ev.Id, pageIndex, ev.Pages[pageIndex].Id,
                report.Summary.Warnings, report.Summary.ReviewNotes, report.Summary.Verdict));
            foreach (var finding in report.Findings)
                add(finding.Severity, "scene", finding.Code,
                    $"event:{ev.Id}/page:{pageIndex}/{finding.Location}",
                    finding.Evidence, finding.Message, finding.Suggestion);
        }
        return results;
    }

    static QualityRoutePlaytestResult PendingPlaytest(QualityRouteDef route)
    {
        var steps = BuildRouteSteps(route);
        return new(steps.Count > 0, false, false, steps.Count, 0, null);
    }

    static QualityRoutePlaytestResult RunRoutePlaytest(GameProject p, string projectRoot, QualityRouteDef route)
    {
        var steps = BuildRouteSteps(route);
        if (steps.Count == 0) return new(false, false, false, 0, 0, null);
        try
        {
            var runtime = new VisualRuntime(p, string.IsNullOrWhiteSpace(projectRoot) ? null : projectRoot);
            runtime.DebugRunScript(steps);
            runtime.Run(30000, null, hidden: true);
            var node = JsonNode.Parse(runtime.BuildScriptReport());
            return new(true, true, node?["ok"]?.GetValue<bool>() == true, steps.Count,
                node?["frames"]?.GetValue<int>() ?? 0, node);
        }
        catch (Exception ex)
        {
            return new(true, true, false, steps.Count, 0,
                new JsonObject { ["ok"] = false, ["error"] = ex.Message });
        }
    }

    static List<string> BuildRouteSteps(QualityRouteDef route)
    {
        var steps = new List<string>();
        foreach (var checkpoint in route.Checkpoints.Where(x => x.RunInPlaytest))
        {
            steps.Add($"checkpoint {checkpoint.Id}:inicio");
            if (checkpoint.Steps.Count > 0) steps.AddRange(checkpoint.Steps);
            else if (!string.IsNullOrWhiteSpace(checkpoint.EventId))
            {
                steps.Add($"event {checkpoint.EventId}{(checkpoint.PageIndex >= 0 ? $" {checkpoint.PageIndex}" : "")}");
                steps.Add("auto");
            }
            if (!string.IsNullOrWhiteSpace(checkpoint.ExpectedMapId))
                steps.Add($"assert-map {checkpoint.ExpectedMapId}");
            if (checkpoint.ExpectedMinMoney >= 0 || checkpoint.ExpectedMaxMoney >= 0)
            {
                var min = Math.Max(0, checkpoint.ExpectedMinMoney);
                var max = checkpoint.ExpectedMaxMoney >= 0 ? checkpoint.ExpectedMaxMoney : int.MaxValue;
                steps.Add(min == max ? $"assert-money {min}" : $"assert-money {min}..{max}");
            }
            if (checkpoint.ExpectedPartyActorIds.Count > 0)
                steps.Add($"assert-party {string.Join(",", checkpoint.ExpectedPartyActorIds)}");
            foreach (var level in checkpoint.ExpectedLevels)
                steps.Add(level.Min == level.Max
                    ? $"assert-level {level.ActorId} {level.Min}"
                    : $"assert-level {level.ActorId} {level.Min}..{level.Max}");
            foreach (var flag in checkpoint.ExpectedFlags)
                steps.Add($"assert-flag {flag.VariableId} {flag.Value.ToString().ToLowerInvariant()}");
            foreach (var itemId in checkpoint.ExpectedItemIds)
                steps.Add($"assert-item {itemId}");
            steps.Add($"checkpoint {checkpoint.Id}:ok");
        }
        if (steps.Count > 0) steps.Add("dump");
        return steps;
    }

    static string PlaytestFailureEvidence(JsonNode? report)
    {
        if (report?["error"] is JsonNode error) return error.GetValue<string>();
        var failed = report?["steps"]?.AsArray().FirstOrDefault(x => x?["ok"]?.GetValue<bool>() == false);
        return failed == null ? "Reporte sin primer fallo identificable." :
            $"{failed["step"]}: {failed["detail"]}";
    }

    static QualityAuditReport Finish(
        bool validationOk,
        bool assetsOk,
        DesignAuditSummary? design,
        List<QualityRouteResult> routes,
        List<QualitySceneResult> scenes,
        List<QualityAuditIssue> issues,
        bool includeInfo)
    {
        if (!includeInfo) issues.RemoveAll(x => x.Severity == "info");
        issues = issues
            .DistinctBy(x => $"{x.Severity}/{x.Source}/{x.Code}/{x.Location}", StringComparer.Ordinal)
            .OrderBy(x => x.Severity == "warning" ? 0 : 1)
            .ThenBy(x => x.Source, StringComparer.Ordinal)
            .ThenBy(x => x.Location, StringComparer.Ordinal)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ToList();
        var warnings = issues.Count(x => x.Severity == "warning");
        var notes = issues.Count(x => x.Severity == "info");
        var playtests = routes.Count(x => x.Playtest.Ran);
        var passed = routes.Count(x => x.Playtest.Ran && x.Playtest.Ok);
        var ready = validationOk && assetsOk && warnings == 0 && passed == playtests;
        var verdict = !ready ? "blocked" : notes > 0 ? "needs_review" : "ready_for_pack";
        return new QualityAuditReport
        {
            Summary = new(
                validationOk, assetsOk, routes.Count, routes.Sum(x => x.Checkpoints), scenes.Count,
                playtests, passed, warnings, notes, ready, verdict),
            Design = design,
            Routes = routes,
            Scenes = scenes,
            Issues = issues,
            AiReviewQuestions =
            [
                "¿Los checkpoints representan la experiencia canonica completa o usan atajos que ocultan exploracion, desgaste o decisiones?",
                "¿Cada encounter contract describe la intencion real (tutorial, comun, elite, jefe) y no un numero elegido para silenciar el auditor?",
                "¿Las notas de escena/balance son decisiones conscientes del tono o deuda que debe resolverse antes de producir el siguiente capitulo?",
                "¿Las rutas opcionales y repetibles alteran economia/EXP sin convertir el grinding en requisito invisible?"
            ],
            SuggestedChecks =
            [
                "Correr quality.audit con runPlaytests=true sobre la ruta canonica despues de cambiar escenas, flags, stats, economia o warps.",
                "Revisar el primer assert fallido antes de tocar numeros; puede indicar que la ruta o su expectativa quedaron viejas.",
                "Usar scene.audit y capturas en las escenas con nota; quality.audit agrega evidencia pero no reemplaza el juicio visual.",
                "Mantener enforceOnPack activo cuando la ruta canonica sea estable; los warnings bloquearan pack y publish."
            ]
        };
    }
}
