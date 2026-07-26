using System.Text.Json;
using System.Text.Json.Nodes;

namespace Seto90;

/// <summary>Contrato headless del director: rutas exactas, asserts runtime, gate de pack y MCP.</summary>
public sealed class QualityAuditSmokeTest
{
    public string Run()
    {
        var project = Fixture();
        var validation = ProjectValidator.Validate(project);
        Expect(validation.Ok, validation.ToHumanText());

        var inferred = BalanceAudit.Analyze(project, includeInfo: false);
        var routed = BalanceAudit.Analyze(project, includeInfo: false, routeId: "route.canon");
        Expect(inferred.Battles.Single().MoneyBefore == 0, "la inferencia global no recorrio el peaje opcional");
        Expect(routed.Battles.Single().MoneyBefore == 10, "la ruta canonica incluyo un evento que no estaba en sus checkpoints");

        var pureA = QualityAudit.Analyze(project, includeInfo: false);
        var pureB = QualityAudit.Analyze(project, includeInfo: false);
        Expect(JsonSerializer.Serialize(pureA) == JsonSerializer.Serialize(pureB), "el dossier puro no es determinista");
        Expect(pureA.Summary.ReadyForPack && pureA.Summary.Warnings == 0, "la ruta sana no quedo lista");

        var played = QualityAudit.Analyze(project, includeInfo: false, runPlaytests: true);
        Expect(played.Summary.ReadyForPack, "el playtest canonico sano bloqueo el gate");
        Expect(played.Routes.Single().Playtest is { Ran: true, Ok: true }, "la ruta no se ejecuto dentro del runtime");
        var state = played.Routes.Single().Playtest.Report?["state"];
        Expect(state?["money"]?.GetValue<int>() == 13, "el assert/runtime no preservo la economia canonica");
        Expect(state?["party"]?[0]?["level"]?.GetValue<int>() == 2, "el assert/runtime no comprobo el level up");

        var badStep = Fixture();
        badStep.QualityPlan.Routes[0].Checkpoints[0].Steps = ["teleport inventado"];
        Expect(ProjectValidator.Validate(badStep).Issues.Any(x => x.Code == "bad_quality_step"),
            "el validador acepto un paso de ruta desconocido");
        var noRuntimeGate = Fixture();
        noRuntimeGate.QualityPlan.Routes[0].Checkpoints[0].RunInPlaytest = false;
        Expect(ProjectValidator.Validate(noRuntimeGate).Issues.Any(x => x.Code == "quality_gate_without_playtest"),
            "runPlaytestsOnPack acepto una ruta canonica sin checkpoint ejecutable");
        var fakeCoverage = Fixture();
        fakeCoverage.QualityPlan.Routes[0].Checkpoints[0].EventId = "event.optional_toll";
        var fakeReport = QualityAudit.Analyze(fakeCoverage, includeInfo: false);
        Expect(fakeReport.Issues.Any(x => x.Code == "route_battle_not_observed"),
            "un battleId declarado pero ausente del evento simulado conto como cobertura real");

        var broken = Fixture();
        broken.QualityPlan.RunPlaytestsOnPack = false;
        broken.QualityPlan.Encounters[0].MinPreparedActions = -1;
        broken.QualityPlan.Encounters[0].MaxPreparedActions = 0;
        var blocked = QualityAudit.Analyze(broken, includeInfo: false);
        Expect(!blocked.Summary.ReadyForPack &&
               blocked.Issues.Any(x => x.Code == "encounter_longer_than_contract"),
            "un contrato de ritmo roto no bloqueo el director");

        var catalog = JsonSerializer.SerializeToNode(ToolRegistry.List())?.AsArray() ?? [];
        Expect(catalog.Count == 57, $"tools/list declara {catalog.Count}, esperaba 57");
        foreach (var name in new[] { "quality.audit", "quality.plan.set", "quality.route.set", "quality.route.delete" })
            Expect(catalog.Any(x => x?["name"]?.GetValue<string>() == name), $"{name} no figura en tools/list");
        Expect(ToolRegistry.WriteNote("quality.audit", new JsonObject()) is null, "quality.audit intento escribir bitacora");

        var root = Path.Combine(Path.GetTempPath(), $"seto90-quality-smoke-{Guid.NewGuid():N}");
        try
        {
            new ProjectStore(root).Save(project);
            var session = new CommandSession(root);
            var query = ToolRegistry.Call("query.entity", new JsonObject
            {
                ["kind"] = "quality"
            }, session);
            Expect(query.Ok && query.Data is QualityPlanDef, "query.entity quality no devolvio el plan");
            var audit = ToolRegistry.Call("quality.audit", new JsonObject
            {
                ["includeInfo"] = false,
                ["runPlaytests"] = false,
                ["routeId"] = "route.canon"
            }, session);
            Expect(audit.Ok && audit.Data is QualityAuditReport report && report.Summary.ReadyForPack,
                "quality.audit MCP no devolvio el dossier listo");
            var rename = ToolRegistry.Call("quality.route.set", JsonSerializer.SerializeToNode(new
            {
                id = "route.canon",
                name = "Canon renombrado",
                description = "smoke",
                canonChoices = Array.Empty<object>(),
                checkpoints = project.QualityPlan.Routes[0].Checkpoints
            })!.AsObject(), session);
            Expect(rename.Ok && session.Project.QualityPlan.Routes[0].Name == "Canon renombrado",
                "quality.route.set no escribio mediante CommandSession");
            Expect(session.Undo().Ok && session.Project.QualityPlan.Routes[0].Name == "Canon",
                "la ruta no participo del undo transaccional");
            Expect(session.Pack().Ok, "el gate sano rechazo project.build_pack");

            new ProjectStore(root).Save(broken);
            var blockedSession = new CommandSession(root);
            var pack = blockedSession.Pack();
            Expect(!pack.Ok && pack.Error?.Code == "quality_gate_failed",
                "el pack no respeto un warning contractual");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        return $"quality smoke OK: ruta canonica exacta, {played.Routes.Single().Playtest.Steps} pasos runtime, gate/MCP/undo deterministas.";
    }

    static GameProject Fixture()
    {
        var hero = new ActorDef
        {
            Id = "actor.hero",
            Name = "Heroe",
            Stats = new StatBlock { Hp = 30, Mp = 0, Attack = 10, Defense = 4, Speed = 6 }
        };
        var enemy = new EnemyDef
        {
            Id = "enemy.test",
            Name = "Prueba",
            Stats = new StatBlock { Hp = 5, Attack = 1, Defense = 0, Speed = 1 },
            Exp = 15,
            Money = 3
        };
        var battle = new BattleDef
        {
            Id = "battle.test",
            EnemyIds = [enemy.Id],
            VictoryFlag = "flag.win",
            DamageFormula = "max(1, attack - defense)"
        };
        var toll = new EventDef
        {
            Id = "event.optional_toll",
            MapId = "map.test",
            Name = "Peaje opcional",
            Kind = EventKind.Npc,
            Pages =
            [
                new EventPage
                {
                    Commands = [new EventCommand { Kind = CommandKind.TakeMoney, Value = "10" }]
                }
            ]
        };
        var fight = new EventDef
        {
            Id = "event.fight",
            MapId = "map.test",
            Name = "Combate",
            Kind = EventKind.Trigger,
            Pages =
            [
                new EventPage
                {
                    Id = "vivo",
                    Conditions = [new ConditionDef { VariableId = "flag.win", EqualsValue = "false" }],
                    Commands = [new EventCommand { Kind = CommandKind.Battle, TargetId = battle.Id }]
                }
            ]
        };
        var checkpoint = new QualityCheckpointDef
        {
            Id = "cp.victoria",
            Label = "Victoria",
            EventId = fight.Id,
            PageIndex = 0,
            BattleId = battle.Id,
            ExpectedMapId = "map.test",
            ExpectedMinMoney = 13,
            ExpectedMaxMoney = 13,
            ExpectedPartyActorIds = [hero.Id],
            ExpectedLevels = [new QualityExpectedLevelDef { ActorId = hero.Id, Min = 2, Max = 2 }],
            ExpectedFlags = [new QualityExpectedFlagDef { VariableId = "flag.win", Value = true }]
        };
        return new GameProject
        {
            Id = "quality.smoke",
            Title = "Quality",
            StartMapId = "map.test",
            StartX = 1,
            StartY = 1,
            StartMoney = 10,
            PartyActorIds = [hero.Id],
            Variables = [new GameVariable { Id = "flag.win", Kind = VariableKind.Flag, Default = "false" }],
            Tilesets =
            [
                new TilesetDef
                {
                    Id = "tiles.test",
                    Tiles = [new TileDef { Id = 0, Name = "Piso", Color = "#203040" }]
                }
            ],
            Maps =
            [
                new MapDef
                {
                    Id = "map.test",
                    Name = "Mapa",
                    TilesetId = "tiles.test",
                    Width = 8,
                    Height = 8,
                    Tiles = Enumerable.Repeat(0, 64).ToList(),
                    EventIds = [toll.Id, fight.Id]
                }
            ],
            Events = [toll, fight],
            Actors = [hero],
            Enemies = [enemy],
            Battles = [battle],
            QualityPlan = new QualityPlanDef
            {
                EnforceOnPack = true,
                RunPlaytestsOnPack = true,
                AuditAllScenes = false,
                CanonicalRouteId = "route.canon",
                Encounters =
                [
                    new QualityEncounterDef
                    {
                        BattleId = battle.Id,
                        Requirement = "required",
                        Role = "tutorial",
                        MinPreparedActions = 1,
                        MaxPreparedActions = 2,
                        MinPreparedHpPercent = 100
                    }
                ],
                Routes =
                [
                    new QualityRouteDef
                    {
                        Id = "route.canon",
                        Name = "Canon",
                        Description = "Evita el peaje opcional.",
                        Checkpoints = [checkpoint]
                    }
                ]
            }
        };
    }

    static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("quality smoke: " + message);
    }
}
