using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Seto90;

/// <summary>Contrato headless del Libro Espejo: deriva en ambos sentidos, validacion,
/// vista previa MCP y manuscritos Markdown/DOCX legibles por herramientas editoriales.</summary>
public sealed class NarrativeTwinSmokeTest
{
    public string Run()
    {
        var project = Fixture();
        var scene = NarrativeTwin.FindScene(project, "scene.faro")!;
        NarrativeTwin.Sync(project, scene);
        Expect(NarrativeTwin.State(project, scene).InSync, "la escena inicial no quedo sincronizada.");

        project.Dialogues[0].Nodes[0].Text = "La luz se apago antes de que llegaras.";
        var gameDrift = NarrativeTwin.State(project, scene);
        Expect(gameDrift.GameChanged && !gameDrift.BookChanged && !gameDrift.InSync,
            "no detecto que cambio solo el juego.");
        Expect(DesignAudit.Analyze(project).Issues.Any(x => x.Code == "story_twin_drift" && x.Severity == "warning"),
            "el auditor de diseno no elevo la deriva narrativa.");
        NarrativeTwin.Sync(project, scene);

        scene.Prose += "\n\nEn la escalera, una campana que nadie habia tocado pronuncio su nombre.";
        var bookDrift = NarrativeTwin.State(project, scene);
        Expect(!bookDrift.GameChanged && bookDrift.BookChanged && !bookDrift.InSync,
            "no detecto que cambio solo el libro.");
        NarrativeTwin.Sync(project, scene);
        Expect(ProjectValidator.Validate(project).Ok, "el proyecto sincronizado no valida.");

        var root = Path.Combine(Path.GetTempPath(), $"seto90-story-smoke-{Guid.NewGuid():N}");
        try
        {
            var store = new ProjectStore(root);
            store.Save(project);
            var report = StoryBookExporter.Export(project, root, "LaLuzQueRecuerda", strict: true);
            Expect(File.Exists(report.MarkdownPath) && File.Exists(report.DocxPath), "faltan archivos del manuscrito.");
            var md = File.ReadAllText(report.MarkdownPath);
            Expect(md.Contains("# La luz que recuerda") && md.Contains("campana que nadie habia tocado"),
                "el Markdown no contiene el titulo y la prosa.");
            Expect(report.Words > 20 && report.Warnings.Count == 0, "conteo o estado editorial inesperado.");

            using (var zip = ZipFile.OpenRead(report.DocxPath))
            {
                foreach (var entry in new[] { "word/document.xml", "word/styles.xml", "word/header1.xml" })
                    Expect(zip.GetEntry(entry) != null, $"el DOCX no contiene {entry}.");
                using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
                var document = reader.ReadToEnd();
                Expect(document.Contains("La luz que recuerda") && document.Contains("campana que nadie habia tocado"),
                    "el DOCX no contiene el manuscrito.");
            }

            var catalog = JsonSerializer.SerializeToNode(ToolRegistry.List())?.AsArray() ?? [];
            Expect(catalog.Count == 57, $"tools/list declara {catalog.Count} herramientas, esperaba 57.");
            foreach (var name in new[] { "story.book.set", "story.chapter.set", "story.scene.set", "story.scene.sync", "story.delete", "story.import", "story.query", "story.export" })
                Expect(catalog.Any(x => x?["name"]?.GetValue<string>() == name), $"falta {name} en tools/list.");

            var session = new CommandSession(root);
            var query = ToolRegistry.Call("story.query", new JsonObject { ["sceneId"] = "scene.faro", ["includeSources"] = true }, session);
            var queryJson = JsonSerializer.SerializeToNode(query.Data)?.ToJsonString() ?? "";
            Expect(query.Ok && queryJson.Contains("dialogue.faro") && queryJson.Contains("CurrentGameHash"),
                "story.query no devolvio fuentes y estado de sincronizacion.");

            var beforeRevision = session.DiskRevision;
            var preview = ToolRegistry.Call("batch.preview", new JsonObject
            {
                ["calls"] = new JsonArray
                {
                    new JsonObject
                    {
                        ["name"] = "story.scene.set",
                        ["arguments"] = new JsonObject
                        {
                            ["chapterId"] = "chapter.uno",
                            ["id"] = "scene.faro",
                            ["synopsis"] = "La protagonista descubre que el faro conserva voces."
                        }
                    }
                }
            }, session);
            Expect(preview.Ok && preview.Data is BatchPreviewReport pr && pr.Diff.Changes.Any(x => x.Kind == "storyscene" && x.Id == "scene.faro"),
                "batch.preview no describio el cambio literario.");
            Expect(session.DiskRevision == beforeRevision, "la vista previa del libro escribio en disco.");

            var mcpExport = ToolRegistry.Call("story.export", new JsonObject { ["baseName"] = "mcp-book", ["strict"] = true }, session);
            Expect(mcpExport.Ok && mcpExport.Data is BookExportReport, "story.export no devolvio un informe estructurado.");
            Expect(ToolRegistry.WriteNote("story.query", new JsonObject()) is null && ToolRegistry.WriteNote("story.export", new JsonObject()) is null,
                "una lectura/exportacion ensucio la bitacora de autoria.");

            ImportContract(session, root);

            var broken = store.FromSnapshot(store.Snapshot(project));
            broken.StoryBook.Chapters[0].Scenes[0].Links[0].Id = "dialogue.no_existe";
            Expect(ProjectValidator.Validate(broken).Issues.Any(x => x.Code == "missing_story_link"),
                "un enlace narrativo roto paso la validacion.");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        return $"story smoke OK: juego<->libro detecta deriva; Markdown + DOCX editorial; {NarrativeTwin.WordCount(project.StoryBook)} palabras; MCP transaccional.";
    }

    /// <summary>El contrato de la rampa de entrada (story.import): un texto ya escrito entra como
    /// capitulos y escenas, SOLO prosa. Lo que se verifica es tanto lo que hace como lo que NO hace:
    /// no inventa links ni gameplay, no pisa nada existente y la vista previa no toca el disco.</summary>
    static void ImportContract(CommandSession session, string root)
    {
        // Acentos escapados a proposito: el codigo del motor es ASCII, pero el manuscrito real no.
        const string manuscript = "# Cap\u00edtulo I: La Llegada\n\n" +
            "Vera bajo del tren con el farol apagado.\n\n" +
            "## El and\u00e9n\n\nNo habia nadie esperandola.\n\n" +
            "# Cap\u00edtulo II\n\n## La casa de la t\u00eda\n\nLa puerta estaba abierta.\n";

        var before = session.Project.StoryBook.Chapters.Count;
        var beforeRevision = session.DiskRevision;
        var dry = ToolRegistry.Call("story.import", new JsonObject { ["text"] = manuscript, ["dryRun"] = true }, session);
        Expect(dry.Ok && dry.Data is StoryImportSummary { DryRun: true, Chapters: 2, Scenes: 3 }, "la vista previa del import no describio 2 capitulos y 3 escenas.");
        Expect(session.DiskRevision == beforeRevision && session.Project.StoryBook.Chapters.Count == before,
            "la vista previa del import escribio en el proyecto.");

        var imported = ToolRegistry.Call("story.import", new JsonObject { ["text"] = manuscript }, session);
        Expect(imported.Ok && imported.Data is StoryImportSummary { DryRun: false, Chapters: 2, Scenes: 3, Words: > 15 }, "el import no incorporo el manuscrito.");
        var book = session.Project.StoryBook;
        Expect(book.Chapters.Count == before + 2, "el import no agrego los capitulos al Libro Espejo.");
        Expect(book.Chapters.Any(x => x.Id == "chapter.capitulo_i_la_llegada"), "el id del capitulo no se normalizo a ASCII.");
        var anden = NarrativeTwin.FindScene(session.Project, "scene.el_anden");
        Expect(anden != null && anden.Prose.Contains("No habia nadie") && anden.Status == "draft",
            "la escena importada no conservo su prosa en estado draft.");
        Expect(NarrativeTwin.FindScene(session.Project, "scene.la_casa_de_la_tia") != null, "el acento de 'tia' rompio el id de la escena.");
        var newScenes = book.Chapters.Where(x => x.Id != "chapter.uno").SelectMany(x => x.Scenes).ToList();
        Expect(newScenes.All(x => x.Links.Count == 0 && x.CanonChoices.Count == 0 && x.SyncedGameHash.Length == 0),
            "el import invento enlaces al juego (solo debe depositar prosa).");
        // La prosa anterior al primer '##' no se pierde: entra como escena implicita del capitulo.
        Expect(newScenes.Any(x => x.Prose.Contains("Vera bajo del tren")), "se perdio la prosa previa al primer encabezado.");
        Expect(ProjectValidator.Validate(session.Project).Ok, "el proyecto no valida despues de importar.");

        // Reimportar el mismo texto NO pisa: los ids repetidos entran con sufijo y avisan.
        var again = ToolRegistry.Call("story.import", new JsonObject { ["text"] = manuscript }, session);
        Expect(again.Ok && again.Data is StoryImportSummary second && second.Warnings.Any(x => x.Contains("scene.el_anden")),
            "reimportar no aviso de los ids repetidos.");
        Expect(NarrativeTwin.FindScene(session.Project, "scene.el_anden_2") != null, "el id repetido no entro con sufijo.");
        Expect(NarrativeTwin.FindScene(session.Project, "scene.el_anden")!.Prose.Contains("No habia nadie"),
            "reimportar piso la escena original.");
        Expect(ToolRegistry.Call("transaction.undo", new JsonObject(), session).Ok &&
            NarrativeTwin.FindScene(session.Project, "scene.el_anden_2") == null,
            "el import no quedo en el undo de la sesion compartida.");

        // Texto plano sin encabezados: el corte editorial clasico (***) separa escenas.
        var plain = ToolRegistry.Call("story.import", new JsonObject
        {
            ["text"] = "Primera parte del texto.\n\n***\n\nSegunda parte del texto.\n",
            ["defaultTitle"] = "Guion suelto"
        }, session);
        Expect(plain.Ok && plain.Data is StoryImportSummary { Chapters: 1, Scenes: 2, Mode: "texto plano" }, "el texto plano no se corto por escenas.");

        // Archivo en disco, con ruta RELATIVA a la carpeta del proyecto.
        File.WriteAllText(Path.Combine(root, "manuscrito.md"), "# Acto unico\n\nUna sola escena llego desde el disco.\n");
        var fromFile = ToolRegistry.Call("story.import", new JsonObject { ["source"] = "manuscrito.md" }, session);
        Expect(fromFile.Ok && fromFile.Data is StoryImportSummary { Chapters: 1, Scenes: 1 }, "no se pudo importar desde un archivo relativo al proyecto.");

        foreach (var (args, code) in new (JsonObject, string)[]
        {
            (new JsonObject { ["source"] = "manuscrito.md", ["text"] = "algo" }, "bad_story_source"),
            (new JsonObject(), "bad_story_source"),
            (new JsonObject { ["source"] = "no_existe.md" }, "missing_story_file"),
            (new JsonObject { ["text"] = "# Solo un titulo sin texto debajo" }, "empty_story_source"),
        })
        {
            var failure = ToolRegistry.Call("story.import", args, session);
            Expect(!failure.Ok && failure.Error!.Code == code, $"esperaba el error {code} y llego '{failure.Error?.Code ?? "ok"}'.");
        }
        Expect(ToolRegistry.WriteNote("story.import", new JsonObject { ["dryRun"] = true }) is null,
            "una vista previa de import ensucio la bitacora de autoria.");
    }

    static GameProject Fixture()
    {
        var dialogue = new DialogueDef
        {
            Id = "dialogue.faro",
            StartNodeId = "inicio",
            Nodes = [new DialogueNode { Id = "inicio", Speaker = "Vera", Text = "La luz sigue encendida." }]
        };
        var scene = new StorySceneDef
        {
            Id = "scene.faro",
            Title = "La luz que recuerda",
            Synopsis = "Vera llega al faro y escucha una voz imposible.",
            Pov = "Vera, tercera limitada",
            Location = "Faro del cabo",
            Time = "Noche",
            Prose = "La lluvia habia borrado el camino, pero no la luz. Vera subio hasta el faro con la certeza de que alguien la esperaba.\n\nArriba no encontro a nadie. Solo una voz guardada en el vidrio.",
            Tags = ["misterio", "vera", "faro"],
            Links =
            [
                new StoryLinkDef { Kind = "dialogue", Id = "dialogue.faro", Role = "source" },
                new StoryLinkDef { Kind = "event", Id = "event.faro", Role = "trigger" },
                new StoryLinkDef { Kind = "map", Id = "map.faro", Role = "setting" }
            ]
        };
        return new GameProject
        {
            Id = "story.smoke",
            Title = "La luz que recuerda",
            StartMapId = "map.faro",
            StartX = 1,
            StartY = 1,
            Tilesets =
            [
                new TilesetDef
                {
                    Id = "tileset.faro",
                    Tiles = [new TileDef { Id = 0, Name = "piso", Color = "#182030" }]
                }
            ],
            Maps =
            [
                new MapDef
                {
                    Id = "map.faro", Name = "Faro", TilesetId = "tileset.faro",
                    Width = 4, Height = 4, Tiles = [.. Enumerable.Repeat(0, 16)], EventIds = ["event.faro"]
                }
            ],
            Events =
            [
                new EventDef
                {
                    Id = "event.faro", MapId = "map.faro", Name = "La lente", Kind = EventKind.Object,
                    X = 2, Y = 1, Pages = [new EventPage { Commands = [new EventCommand { Kind = CommandKind.Dialogue, TargetId = "dialogue.faro" }] }]
                }
            ],
            Dialogues = [dialogue],
            StoryBook = new StoryBookDef
            {
                Title = "La luz que recuerda",
                Subtitle = "Una novela de memoria y mareas",
                ShortTitle = "Luz que recuerda",
                Author = "Autora de prueba",
                Language = "es",
                Description = "Una historia nacida a la vez como juego y novela.",
                Chapters = [new StoryChapterDef { Id = "chapter.uno", Title = "El faro", Summary = "Vera oye la primera voz.", Scenes = [scene] }]
            }
        };
    }

    static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("story smoke: " + message);
    }
}
