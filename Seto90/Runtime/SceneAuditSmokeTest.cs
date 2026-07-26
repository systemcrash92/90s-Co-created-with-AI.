using System.Text.Json;
using System.Text.Json.Nodes;

namespace Seto90;

/// <summary>Contrato headless del auditor de escenas: reglas, dossier para IA y superficie MCP.</summary>
public sealed class SceneAuditSmokeTest
{
    public string Run()
    {
        var project = Fixture();
        var first = SceneAudit.AnalyzeEvent(project, "event.ritual");
        var second = SceneAudit.AnalyzeEvent(project, "event.ritual");
        Expect(JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second), "el informe no es determinista");
        Expect(first.Summary.Verdict == "needs_fix", "una escena repetible/incoherente no quedo needs_fix");
        Expect(first.Coverage.HasDialogue && first.Coverage.HasStateChange && first.Coverage.HasEmote, "la cobertura no detecto dialogo/estado/emote");
        Expect(first.Beats.Any(x => x.Kind == "dialogue" && x.Text.Contains("camino", StringComparison.OrdinalIgnoreCase)), "el dossier no incluyo la transcripcion");
        Expect(first.AiReviewQuestions.Any(x => x.Contains("sabe", StringComparison.OrdinalIgnoreCase)), "faltan preguntas semanticas para la IA");
        Expect(first.SuggestedChecks.Any(x => x.Contains("scrub", StringComparison.OrdinalIgnoreCase)), "faltan checkpoints visuales");

        var codes = first.Findings.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[]
        {
            "flag_write_repeats_page_gate", "repeatable_scene_consequence",
            "key_item_without_ceremony", "emote_after_dialogue",
            "choice_needs_consequence_review", "dialogue_cycle_without_choice_or_exit"
        })
            Expect(codes.Contains(expected), $"falto el hallazgo '{expected}'");

        var nestedReward = SceneAudit.AnalyzeEvent(project, "event.reward");
        Expect(nestedReward.Findings.Any(x => x.Code == "repeatable_scene_consequence"),
            "una recompensa anidada en dialogo de NPC quedo repetible sin alerta");

        var warningsOnly = SceneAudit.AnalyzeEvent(project, "event.ritual", includeInfo: false);
        Expect(warningsOnly.Findings.All(x => x.Severity == "warning") && warningsOnly.Summary.ReviewNotes == 0,
            "includeInfo=false no filtro las oportunidades de pulido");

        var story = SceneAudit.AnalyzeStoryScene(project, "scene.ritual");
        Expect(story.Scope.EventIds.Contains("event.ritual") && story.Beats.Any(x => x.Kind == "prose"),
            "la escena del Libro Espejo no reunio evento + prosa");
        Expect(story.Findings.Any(x => x.Code == "scene_twin_not_reconciled"),
            "la deriva juego/prosa no aparecio en la auditoria de escena");

        var catalog = JsonSerializer.SerializeToNode(ToolRegistry.List())?.AsArray() ?? [];
        Expect(catalog.Count == 57, $"tools/list declara {catalog.Count}, esperaba 57");
        Expect(catalog.Any(x => x?["name"]?.GetValue<string>() == "scene.audit"), "scene.audit no figura en tools/list");
        Expect(ToolRegistry.WriteNote("scene.audit", new JsonObject()) is null, "scene.audit intento escribir bitacora");

        var root = Path.Combine(Path.GetTempPath(), $"seto90-scene-smoke-{Guid.NewGuid():N}");
        try
        {
            new ProjectStore(root).Save(project);
            var session = new CommandSession(root);
            var payload = ToolRegistry.Call("scene.audit", new JsonObject
            {
                ["eventId"] = "event.ritual",
                ["includeTranscript"] = true
            }, session);
            Expect(payload.Ok && payload.Data is SceneAuditReport, "scene.audit no devolvio informe estructurado");
            var bad = ToolRegistry.Call("scene.audit", new JsonObject(), session);
            Expect(!bad.Ok && bad.Error?.Code == "bad_scene_scope", "scope vacio no devolvio error estructurado");
            var missingPage = ToolRegistry.Call("scene.audit", new JsonObject
            {
                ["eventId"] = "event.ritual",
                ["pageId"] = "no_existe"
            }, session);
            Expect(!missingPage.Ok && missingPage.Error?.Code == "missing_scene_page", "pagina inexistente no devolvio fix estructurado");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        return $"scene smoke OK: {first.Summary.Warnings} riesgos + {first.Summary.ReviewNotes} notas; flags, guion, staging, game feel, Libro Espejo y MCP deterministas.";
    }

    static GameProject Fixture()
    {
        var choice = new DialogueDef
        {
            Id = "dialogue.choice",
            StartNodeId = "root",
            Nodes =
            [
                new DialogueNode
                {
                    Id = "root",
                    Speaker = "Mara",
                    Text = "¿Que camino elegis?",
                    Choices =
                    [
                        new DialogueChoice { Text = "La torre", NextNodeId = "tower" },
                        new DialogueChoice { Text = "Volver a casa", NextNodeId = "home" }
                    ]
                },
                new DialogueNode { Id = "tower", Speaker = "Mara", Text = "Entonces subimos.", NextNodeId = "end" },
                new DialogueNode { Id = "home", Speaker = "Mara", Text = "Entonces volvemos.", NextNodeId = "end" },
                new DialogueNode { Id = "end", Speaker = "Mara", Text = "El camino sigue." }
            ]
        };
        var cycle = new DialogueDef
        {
            Id = "dialogue.cycle",
            StartNodeId = "a",
            Nodes =
            [
                new DialogueNode { Id = "a", Speaker = "Eco", Text = "Otra vez.", NextNodeId = "b" },
                new DialogueNode { Id = "b", Speaker = "Eco", Text = "Y otra.", NextNodeId = "a" }
            ]
        };
        var rewardDialogue = new DialogueDef
        {
            Id = "dialogue.reward",
            StartNodeId = "gift",
            Nodes =
            [
                new DialogueNode
                {
                    Id = "gift",
                    Speaker = "Mercader",
                    Text = "Toma otro.",
                    Effects = [new EventCommand { Kind = CommandKind.GiveItem, TargetId = "item.llave", Value = "1" }]
                }
            ]
        };
        var reward = new EventDef
        {
            Id = "event.reward",
            Name = "Regalo repetible",
            MapId = "map.faro",
            Kind = EventKind.Npc,
            Pages =
            [
                new EventPage
                {
                    Id = "default",
                    Commands = [new EventCommand { Kind = CommandKind.Dialogue, TargetId = rewardDialogue.Id }]
                }
            ]
        };
        var ritual = new EventDef
        {
            Id = "event.ritual",
            Name = "Ritual de la llave",
            MapId = "map.faro",
            Kind = EventKind.Cutscene,
            Pages =
            [
                new EventPage
                {
                    Id = "activo",
                    Conditions = [new ConditionDef { VariableId = "flag.ritual", EqualsValue = "true" }],
                    Commands =
                    [
                        new EventCommand { Kind = CommandKind.Dialogue, TargetId = "dialogue.choice" },
                        new EventCommand { Kind = CommandKind.Dialogue, TargetId = "dialogue.cycle" },
                        new EventCommand { Kind = CommandKind.GiveItem, TargetId = "item.llave", Value = "1" },
                        new EventCommand { Kind = CommandKind.SetVariable, TargetId = "flag.ritual", Value = "true" },
                        new EventCommand { Kind = CommandKind.ShowEmote, TargetId = "event.ritual", Value = "!" }
                    ]
                }
            ]
        };
        var scene = new StorySceneDef
        {
            Id = "scene.ritual",
            Title = "La llave",
            Synopsis = "Mara elige un camino y recibe la llave.",
            Pov = "Mara",
            Location = "Faro",
            Time = "Noche",
            Prose = "Mara sostuvo la llave frente a la luz del faro.",
            Links =
            [
                new StoryLinkDef { Kind = "event", Id = ritual.Id, Role = "entry" },
                new StoryLinkDef { Kind = "dialogue", Id = choice.Id, Role = "script" }
            ]
        };
        return new GameProject
        {
            Id = "scene.smoke",
            Title = "Escenas",
            StartMapId = "map.faro",
            Maps = [new MapDef { Id = "map.faro", Width = 4, Height = 4, Tiles = Enumerable.Repeat(0, 16).ToList(), EventIds = [ritual.Id, reward.Id] }],
            Events = [ritual, reward],
            Dialogues = [choice, cycle, rewardDialogue],
            Variables = [new GameVariable { Id = "flag.ritual", Kind = VariableKind.Flag, Default = "true" }],
            Items = [new ItemDef { Id = "item.llave", Name = "Llave", Price = 0 }],
            StoryBook = new StoryBookDef
            {
                Title = "Escenas",
                Author = "Smoke",
                Chapters = [new StoryChapterDef { Id = "chapter.uno", Title = "Uno", Scenes = [scene] }]
            }
        };
    }

    static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("scene smoke: " + message);
    }
}
