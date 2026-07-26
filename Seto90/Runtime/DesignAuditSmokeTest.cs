using System.Text.Json;
using System.Text.Json.Nodes;

namespace Seto90;

/// <summary>Contrato headless del auditor: hechos conocidos, orden estable y superficie MCP.</summary>
public sealed class DesignAuditSmokeTest
{
    public string Run()
    {
        var project = Fixture();
        var first = DesignAudit.Analyze(project);
        var second = DesignAudit.Analyze(project);
        var firstJson = JsonSerializer.Serialize(first);
        var secondJson = JsonSerializer.Serialize(second);
        Expect(firstJson == secondJson, "El mismo proyecto no produjo el mismo informe.");

        var codes = first.Issues.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[]
        {
            "unreachable_map", "orphan_dialogue", "unreachable_dialogue_node",
            "ambiguous_dialogue_flow", "choice_without_state_consequence",
            "shadowed_event_page", "detached_event", "state_never_written",
            "state_never_read", "state_unused", "orphan_battle", "orphan_shop",
            "orphan_item", "orphan_skill"
        })
            Expect(codes.Contains(expected), $"Falto el hallazgo esperado '{expected}'.");

        Expect(first.Summary.ReachableMaps == 1 && first.Summary.Maps == 2, "La cobertura de mapas no coincide.");
        Expect(first.Summary.ReachableDialogues == 1 && first.Summary.Dialogues == 2, "La cobertura narrativa no coincide.");
        Expect(first.Battles.Count == 1 && first.Battles[0].EstimatedRoundsToWin > 0, "No se calcularon metricas de combate.");

        var catalog = JsonSerializer.SerializeToNode(ToolRegistry.List())?.AsArray() ?? [];
        Expect(catalog.Count == 57, $"tools/list declara {catalog.Count} herramientas, pero la superficie documentada es 55.");
        Expect(catalog.Any(x => x?["name"]?.GetValue<string>() == "project.audit"), "project.audit no figura en tools/list.");
        Expect(ToolRegistry.WriteNote("project.audit", new JsonObject()) is null, "Una lectura project.audit intento escribir en la bitacora.");

        var root = Path.Combine(Path.GetTempPath(), $"seto90-audit-smoke-{Guid.NewGuid():N}");
        try
        {
            new ProjectStore(root).Save(project);
            var payload = ToolRegistry.Call("project.audit", new JsonObject { ["includeInfo"] = false }, new CommandSession(root));
            Expect(payload.Ok && payload.Data is DesignAuditReport, "project.audit no devolvio un informe estructurado.");
            var warningsOnly = (DesignAuditReport)payload.Data!;
            Expect(warningsOnly.Issues.All(x => x.Severity == "warning") && warningsOnly.Summary.Info == 0,
                "includeInfo=false no filtro las notas no bloqueantes.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        return $"audit smoke OK: {first.Summary.Warnings} warnings + {first.Summary.Info} notas; grafo, estado, paginas, contenido, balance y MCP deterministas.";
    }

    static GameProject Fixture()
    {
        var story = new DialogueDef
        {
            Id = "dialogue.story",
            StartNodeId = "root",
            Nodes =
            [
                new DialogueNode
                {
                    Id = "root", Text = "¿Que camino elegis?", NextNodeId = "left",
                    Choices =
                    [
                        new DialogueChoice { Text = "Izquierda", NextNodeId = "left" },
                        new DialogueChoice { Text = "Derecha", NextNodeId = "right" }
                    ]
                },
                new DialogueNode { Id = "left", Text = "Fuiste por la izquierda.", NextNodeId = "end" },
                new DialogueNode { Id = "right", Text = "Fuiste por la derecha.", NextNodeId = "end" },
                new DialogueNode { Id = "end", Text = "Ambos caminos se juntan." },
                new DialogueNode { Id = "lost", Text = "Nadie llega aca." }
            ]
        };
        return new GameProject
        {
            Id = "audit.smoke",
            Title = "Auditoria",
            StartMapId = "map.start",
            StartMoney = 7,
            Maps =
            [
                new MapDef { Id = "map.start", EventIds = ["event.story"] },
                new MapDef { Id = "map.secret" }
            ],
            Events =
            [
                new EventDef
                {
                    Id = "event.story", MapId = "map.start",
                    Pages =
                    [
                        new EventPage
                        {
                            Id = "conditional",
                            Conditions = [new ConditionDef { VariableId = "flag.read_only", EqualsValue = "true" }],
                            Commands = [new EventCommand { Kind = CommandKind.Dialogue, TargetId = "dialogue.story" }]
                        },
                        new EventPage
                        {
                            Id = "always",
                            Commands =
                            [
                                new EventCommand { Kind = CommandKind.Dialogue, TargetId = "dialogue.story" },
                                new EventCommand { Kind = CommandKind.SetVariable, TargetId = "flag.write_only", Value = "true" }
                            ]
                        }
                    ]
                },
                new EventDef { Id = "event.detached", MapId = "map.start", Pages = [new EventPage()] }
            ],
            Dialogues =
            [
                story,
                new DialogueDef { Id = "dialogue.orphan", StartNodeId = "only", Nodes = [new DialogueNode { Id = "only", Text = "Borrador" }] }
            ],
            Variables =
            [
                new GameVariable { Id = "flag.read_only", Kind = VariableKind.Flag, Default = "false" },
                new GameVariable { Id = "flag.write_only", Kind = VariableKind.Flag, Default = "false" },
                new GameVariable { Id = "flag.unused", Kind = VariableKind.Flag, Default = "false" }
            ],
            Actors = [new ActorDef { Id = "actor.hero", Level = 1, Stats = new StatBlock { Hp = 30, Attack = 8, Defense = 4, Speed = 4 } }],
            PartyActorIds = ["actor.hero"],
            Enemies = [new EnemyDef { Id = "enemy.slime", Stats = new StatBlock { Hp = 12, Attack = 5, Defense = 2, Speed = 2 }, Exp = 3, Money = 2 }],
            Battles = [new BattleDef { Id = "battle.orphan", EnemyIds = ["enemy.slime"], DamageFormula = "max(1, attack - defense)" }],
            Items = [new ItemDef { Id = "item.orphan", Price = 5 }],
            Shops = [new ShopDef { Id = "shop.orphan", ItemIds = ["item.orphan"] }],
            Skills = [new SkillDef { Id = "skill.orphan" }]
        };
    }

    static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("audit smoke: " + message);
    }
}
