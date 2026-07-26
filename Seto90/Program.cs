using System.Text;
using System.Text.Json.Nodes;

namespace Seto90;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        if (args.Length == 0)
        {
            // Modo jugador: si hay un game.pack junto al exe (carpeta dist/ publicada),
            // doble click = jugar. Sin pack, muestra la ayuda de desarrollo.
            var autoPack = Path.Combine(AppContext.BaseDirectory, "game.pack");
            if (File.Exists(autoPack))
            {
                var packProject = ProjectStore.LoadPack(autoPack);
                var packCheck = ProjectValidator.Validate(packProject);
                if (packCheck.Ok) { new VisualRuntime(packProject).Run(); return 0; }
                Console.Error.WriteLine(packCheck.ToHumanText());
                return 1;
            }
            Help();
            return 0;
        }
        if (args[0] is "--help" or "-h") { Help(); return 0; }
        var projectPath = Option(args, "--project") ?? Path.Combine(Environment.CurrentDirectory, "game");
        switch (args[0])
        {
            case "new": return NewProject(projectPath, args);
            case "import-story": return ImportStory(projectPath, args);
            case "publish": return Publish(projectPath, args);
            case "mcp": await new McpServer(projectPath, Console.OpenStandardInput(), Console.OpenStandardOutput()).RunAsync(); return 0;
            case "validate": return Validate(projectPath);
            case "audit": return Audit(projectPath);
            case "scene-audit": return SceneAuditCli(projectPath, args);
            case "balance-audit": return BalanceAuditCli(projectPath, args);
            case "quality-audit": return QualityAuditCli(projectPath, args);
            case "playtest": return Playtest(projectPath);
            case "dialogue-smoke": return DialogueSmoke(projectPath);
            case "event-smoke": return EventSmoke(projectPath);
            case "battle-smoke": return BattleSmoke(projectPath);
            case "audio-smoke": return AudioSmoke(projectPath);
            case "asset-smoke": return AssetSmoke(projectPath);
            case "font-smoke": Console.WriteLine(new FontSmokeTest().Run()); return 0;
            case "map-smoke": Console.WriteLine(new MapOpsSmokeTest().Run()); return 0;
            case "vfx-smoke": Console.WriteLine(new VfxSmokeTest().Run()); return 0;
            case "session-smoke": Console.WriteLine(new SessionSmokeTest().Run()); return 0;
            case "audit-smoke": Console.WriteLine(new DesignAuditSmokeTest().Run()); return 0;
            case "scene-smoke": Console.WriteLine(new SceneAuditSmokeTest().Run()); return 0;
            case "balance-smoke": Console.WriteLine(new BalanceAuditSmokeTest().Run()); return 0;
            case "quality-smoke": Console.WriteLine(new QualityAuditSmokeTest().Run()); return 0;
            case "story-smoke": Console.WriteLine(new NarrativeTwinSmokeTest().Run()); return 0;
            case "ui-smoke": return UiSmoke(projectPath);
            case "export-assets": return ExportAssets(projectPath);
            case "export-book": return ExportBook(projectPath, args);
            case "run": return RunVisual(projectPath, args);
            case "screenshot": return Screenshot(projectPath, args);
            case "playtest-run": return PlaytestRun(projectPath, args);
            case "evals": return Evals(projectPath, args);
            case "run-pack": return RunPack(args);
            case "validate-pack": return ValidatePack(args);
            case "save-smoke": return SaveSmoke(projectPath);
            case "shop-smoke": return ShopSmoke(projectPath);
            case "party-smoke": return PartySmoke(projectPath);
            case "pack": return Pack(projectPath);
            case "import-sprites": return ImportSprites(projectPath, args);
            default: Console.Error.WriteLine($"Comando desconocido: {args[0]}"); Help(); return 2;
        }
    }

    static string? Option(string[] args, string name) { for (var i = 0; i < args.Length - 1; i++) if (args[i] == name) return args[i + 1]; return null; }

    /// <summary>Herramienta reutilizable: importa TODOS los PNGs de una carpeta como sprites del
    /// proyecto (id sprite.PREFIJO_nombre), copiados a assets/props/. Una sola transaccion validada.
    /// Uso: import-sprites --project X --from <carpeta> --prefix pueblo [--max 128]</summary>
    static int ImportSprites(string root, string[] args)
    {
        var from = Option(args, "--from");
        var prefix = Option(args, "--prefix") ?? "prop";
        var max = int.TryParse(Option(args, "--max"), out var m) ? m : 128; // saltea spritesheets grandes
        if (from == null || !Directory.Exists(from)) { Console.Error.WriteLine("Falta --from <carpeta con PNGs>."); return 1; }
        var session = new CommandSession(root);
        var destDir = Path.Combine(session.ProjectRoot, "assets", "props");
        Directory.CreateDirectory(destDir);
        var defs = new List<SpriteDef>();
        var skipped = 0;
        foreach (var png in Directory.GetFiles(from, "*.png").OrderBy(f => f, StringComparer.Ordinal))
        {
            var (w, h) = PngSize(png);
            if (w <= 0 || h <= 0) { skipped++; continue; }
            if (w > max || h > max) { skipped++; continue; } // sheet/objeto gigante: no es un prop
            var clean = SanitizeName(Path.GetFileNameWithoutExtension(png));
            var destName = $"{prefix}_{clean}.png";
            File.Copy(png, Path.Combine(destDir, destName), true);
            defs.Add(new SpriteDef { Id = $"sprite.{prefix}_{clean}", Name = clean, Width = w, Height = h, Image = $"assets/props/{destName}", SheetFramesPerDirection = 1 });
        }
        var result = session.Mutate(p => { foreach (var d in defs) { var i = p.Sprites.FindIndex(s => s.Id == d.Id); if (i >= 0) p.Sprites[i] = d; else p.Sprites.Add(d); } });
        Console.WriteLine(result.Ok
            ? $"Importados {defs.Count} sprites (prefijo {prefix}), {skipped} salteados. Total sprites del proyecto: {session.Project.Sprites.Count}."
            : $"FALLO: {result.Error?.Code} {result.Error?.Message}");
        return result.Ok ? 0 : 1;
    }

    static string SanitizeName(string n)
    {
        var s = new string(n.ToLowerInvariant().Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray());
        while (s.Contains("__")) s = s.Replace("__", "_");
        return s.Trim('_');
    }

    /// <summary>Lee ancho/alto de un PNG por su cabecera IHDR (sin raylib, headless).</summary>
    static (int W, int H) PngSize(string path)
    {
        try
        {
            using var fs = File.OpenRead(path);
            var b = new byte[24];
            if (fs.Read(b, 0, 24) < 24) return (0, 0);
            var w = (b[16] << 24) | (b[17] << 16) | (b[18] << 8) | b[19];
            var h = (b[20] << 24) | (b[21] << 16) | (b[22] << 8) | b[23];
            return (w, h);
        }
        catch { return (0, 0); }
    }

    static void Help() => Console.WriteLine($"""
90s Engine - motor JRPG 90s con co-autoria humano/IA
  Guia completa: {GuidePath()}   |   Modelo de co-autoria: ORCHESTRATION.md

CREAR (los dos modos crean juego espejo: juego + Libro Espejo en el mismo proyecto)
  new --project MiJuego                              juego nuevo
  new --project MiJuego --from manuscrito.md         partir de un texto ya escrito
  import-story --project MiJuego --from cap2.md [--dry-run]

AUTORIA
  mcp --project MiJuego                              servidor MCP por stdio (57 herramientas)
  run --project MiJuego                              jugar/editar en vivo (F1 = editor)
  screenshot --project MiJuego [--crt] --out build\shot.png
  playtest-run --project MiJuego --steps guion.txt [--out reporte.json] [--crt]
  import-sprites --project MiJuego --from carpeta --prefix P [--max 128]

VERIFICAR
  validate --project MiJuego
  audit --project MiJuego
  scene-audit --project MiJuego --event event.intro [--page default | --page-index 0]
  scene-audit --project MiJuego --story-scene scene.intro
  balance-audit --project MiJuego [--warnings-only]
  quality-audit --project MiJuego [--route route.canon] [--run-playtests] [--warnings-only]
  playtest --project MiJuego
  evals --project MiJuego [--only cap1_intro] [--out reporte.json]   regresion de gameplay

SMOKES (headless y deterministas)
  dialogue-smoke / event-smoke / battle-smoke / audio-smoke / asset-smoke --project MiJuego
  ui-smoke / save-smoke / shop-smoke / party-smoke --project MiJuego
  font-smoke / map-smoke / vfx-smoke / session-smoke / audit-smoke
  scene-smoke / balance-smoke / quality-smoke / story-smoke

PUBLICAR
  export-book --project MiJuego [--name MiNovela] [--strict]
  export-assets --project MiJuego
  pack --project MiJuego
  validate-pack --pack MiJuego\build\game.pack
  run-pack --pack MiJuego\build\game.pack [--frames 5]
  publish --project MiJuego

Todos los comandos se invocan como: dotnet run --project Seto90 -- <comando>
""");

    static int Validate(string root) { var p = new ProjectStore(root).LoadOrCreate(); var r = ProjectValidator.Validate(p); Console.WriteLine(r.ToHumanText()); return r.Ok ? 0 : 1; }
    static int Audit(string root)
    {
        var p = new ProjectStore(root).LoadOrCreate();
        var validation = ProjectValidator.Validate(p);
        if (!validation.Ok) Console.Error.WriteLine(validation.ToHumanText());
        Console.WriteLine(DesignAudit.Analyze(p).ToHumanText());
        return validation.Ok ? 0 : 1;
    }
    static int SceneAuditCli(string root, string[] args)
    {
        var p = new ProjectStore(root).LoadOrCreate();
        var validation = ProjectValidator.Validate(p);
        if (!validation.Ok) { Console.Error.WriteLine(validation.ToHumanText()); return 1; }
        var eventId = Option(args, "--event") ?? "";
        var storySceneId = Option(args, "--story-scene") ?? "";
        if (string.IsNullOrWhiteSpace(eventId) == string.IsNullOrWhiteSpace(storySceneId))
        {
            Console.Error.WriteLine("Indicar exactamente uno: --event <id> o --story-scene <id>.");
            return 2;
        }
        try
        {
            var includeInfo = !args.Contains("--warnings-only", StringComparer.Ordinal);
            var pageId = Option(args, "--page") ?? "";
            var pageIndex = int.TryParse(Option(args, "--page-index"), out var parsedPage) ? parsedPage : -1;
            if (!string.IsNullOrWhiteSpace(pageId) && pageIndex >= 0)
            {
                Console.Error.WriteLine("Usar --page o --page-index, no ambos.");
                return 2;
            }
            var report = !string.IsNullOrWhiteSpace(eventId)
                ? SceneAudit.AnalyzeEvent(p, eventId, pageId, includeInfo, pageIndex: pageIndex)
                : SceneAudit.AnalyzeStoryScene(p, storySceneId, includeInfo);
            Console.WriteLine(report.ToHumanText());
            return report.Summary.Warnings > 0 ? 1 : 0;
        }
        catch (KeyNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }
    static int BalanceAuditCli(string root, string[] args)
    {
        var p = new ProjectStore(root).LoadOrCreate();
        var validation = ProjectValidator.Validate(p);
        if (!validation.Ok) { Console.Error.WriteLine(validation.ToHumanText()); return 1; }
        try
        {
            var report = BalanceAudit.Analyze(
                p,
                includeInfo: !args.Contains("--warnings-only", StringComparer.Ordinal),
                routeId: Option(args, "--route") ?? "");
            Console.WriteLine(report.ToHumanText());
            return report.Summary.Warnings > 0 ? 1 : 0;
        }
        catch (KeyNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }
    static int QualityAuditCli(string root, string[] args)
    {
        var p = new ProjectStore(root).LoadOrCreate();
        try
        {
            var report = QualityAudit.Analyze(
                p,
                root,
                includeInfo: !args.Contains("--warnings-only", StringComparer.Ordinal),
                runPlaytests: args.Contains("--run-playtests", StringComparer.Ordinal),
                routeId: Option(args, "--route") ?? "");
            Console.WriteLine(report.ToHumanText());
            return report.Summary.ReadyForPack ? 0 : 1;
        }
        catch (KeyNotFoundException ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 2;
        }
    }
    static int Playtest(string root) { var p = new ProjectStore(root).LoadOrCreate(); var r = ProjectValidator.Validate(p); if (!r.Ok) { Console.Error.WriteLine(r.ToHumanText()); return 1; } Console.WriteLine(new HeadlessPlaytest(p).Run()); return 0; }
    static int DialogueSmoke(string root) { var p = new ProjectStore(root).LoadOrCreate(); var r = ProjectValidator.Validate(p); if (!r.Ok) { Console.Error.WriteLine(r.ToHumanText()); return 1; } Console.WriteLine(new DialogueSmokeTest(p).Run()); return 0; }
    static int EventSmoke(string root) { var p = new ProjectStore(root).LoadOrCreate(); var r = ProjectValidator.Validate(p); if (!r.Ok) { Console.Error.WriteLine(r.ToHumanText()); return 1; } Console.WriteLine(new EventSmokeTest(p).Run()); return 0; }
    static int BattleSmoke(string root) { var p = new ProjectStore(root).LoadOrCreate(); var r = ProjectValidator.Validate(p); if (!r.Ok) { Console.Error.WriteLine(r.ToHumanText()); return 1; } Console.WriteLine(new BattleSmokeTest(p).Run()); return 0; }
    static int AudioSmoke(string root) { var p = new ProjectStore(root).LoadOrCreate(); var r = ProjectValidator.Validate(p); if (!r.Ok) { Console.Error.WriteLine(r.ToHumanText()); return 1; } Console.WriteLine(new AudioSmokeTest(p).Run()); return 0; }
    static int AssetSmoke(string root)
    {
        var p = new ProjectStore(root).LoadOrCreate();
        var report = AssetPipeline.Validate(p, root);
        Console.WriteLine(report.ToHumanText());
        if (!report.Ok) return 1;
        // Rasterizado CPU de cada sprite procedural: si una fila o indice esta mal, falla aca y no en pantalla.
        foreach (var sprite in p.Sprites.Where(s => string.IsNullOrWhiteSpace(s.Image)))
        {
            var raster = SpriteRaster.Rasterize(sprite);
            var frames = raster.Poses.Sum(x => x.Value.Count);
            var opaque = raster.Poses[Facing.Down][0].Count(c => c.A > 0);
            if (opaque == 0) { Console.Error.WriteLine($"Sprite {sprite.Id}: frame down vacio."); return 1; }
            Console.WriteLine($"sprite {sprite.Id}: 4 direcciones resueltas, {frames} frames, {opaque} px opacos en down[0].");
        }
        // Tiles animados: el ciclo de celdas es determinista respecto del reloj recibido.
        var anim = new TileDef { Id = 3, Frames = [10, 11, 12] };
        if (TileBank.AnimCell(anim, 3, 300, 0) != 10 || TileBank.AnimCell(anim, 3, 300, 300) != 11 ||
            TileBank.AnimCell(anim, 3, 300, 650) != 12 || TileBank.AnimCell(anim, 3, 300, 900) != 10 ||
            TileBank.AnimCell(new TileDef { Id = 5 }, 5, 300, 123) != 5)
        { Console.Error.WriteLine("Tiles animados: el ciclo de AnimCell no es el esperado."); return 1; }
        Console.WriteLine("tiles animados: ciclo determinista OK (3 frames a 300ms -> 10,11,12,10; estatico = su id).");
        return 0;
    }
    static int UiSmoke(string root) { var p = new ProjectStore(root).LoadOrCreate(); Console.WriteLine(new UiSmokeTest(p).Run()); return 0; }
    static int ExportAssets(string root) { var p = new ProjectStore(root).LoadOrCreate(); var report = AssetPipeline.Validate(p); if (!report.Ok) { Console.Error.WriteLine(report.ToHumanText()); return 1; } Console.WriteLine($"Asset manifest: {AssetPipeline.WriteManifest(p, root)}"); return 0; }
    /// <summary>Ingesta de un manuscrito ya escrito al Libro Espejo del proyecto. Pasa por la MISMA
    /// herramienta MCP (una sola verdad: humano e IA importan igual).
    /// Uso: import-story --project X --from manuscrito.md [--dry-run]</summary>
    static int ImportStory(string root, string[] args)
    {
        var from = Option(args, "--from");
        if (string.IsNullOrWhiteSpace(from))
        {
            Console.Error.WriteLine("Falta --from <archivo .md o .txt con el manuscrito>.");
            Console.Error.WriteLine("Formato: '# titulo' abre capitulo, '## titulo' abre escena. Un .txt plano entra como un capitulo.");
            return 2;
        }
        var dryRun = Array.IndexOf(args, "--dry-run") >= 0;
        var session = new CommandSession(root);
        var payload = ToolRegistry.Call("story.import", new JsonObject { ["source"] = from, ["dryRun"] = dryRun }, session);
        if (!payload.Ok || payload.Data is not StoryImportSummary summary)
        {
            Console.Error.WriteLine(payload.Error is null ? "El import fallo." : $"{payload.Error.Code}: {payload.Error.Message}\n  {payload.Error.Fix}");
            return 1;
        }
        Console.WriteLine($"{(summary.DryRun ? "VISTA PREVIA (no se escribio nada)" : "Manuscrito importado")}: {summary.Chapters} capitulos, {summary.Scenes} escenas, {summary.Words} palabras (corte por {summary.Mode}).");
        foreach (var chapter in summary.Structure)
        {
            Console.WriteLine($"  {chapter.Id}  {chapter.Title}");
            foreach (var scene in chapter.Scenes) Console.WriteLine($"    {scene.Id}  {scene.Title} ({scene.Words} palabras)");
        }
        foreach (var warning in summary.Warnings) Console.WriteLine("AVISO: " + warning);
        Console.WriteLine(summary.Next);
        return 0;
    }

    static int ExportBook(string root, string[] args)
    {
        var p = new ProjectStore(root).LoadOrCreate();
        var validation = ProjectValidator.Validate(p);
        if (!validation.Ok) { Console.Error.WriteLine(validation.ToHumanText()); return 1; }
        try
        {
            var report = StoryBookExporter.Export(p, root, Option(args, "--name"), Array.IndexOf(args, "--strict") >= 0);
            Console.WriteLine($"Manuscrito: {report.MarkdownPath}");
            Console.WriteLine($"Editorial:  {report.DocxPath}");
            Console.WriteLine($"{report.Chapters} capitulos, {report.Scenes} escenas, {report.Words} palabras, {report.Warnings.Count} avisos.");
            foreach (var warning in report.Warnings) Console.WriteLine("AVISO: " + warning);
            return 0;
        }
        catch (InvalidOperationException ex) { Console.Error.WriteLine(ex.Message); return 1; }
    }
    static int RunVisual(string root, string[] args) { var session = new CommandSession(root); var p = session.Project; var r = ProjectValidator.Validate(p); if (!r.Ok) { Console.Error.WriteLine(r.ToHumanText()); return 1; } var framesText = Option(args, "--frames"); int? frames = int.TryParse(framesText, out var parsed) ? parsed : null; new VisualRuntime(p, root, session).Run(frames); return 0; }
    static int Screenshot(string root, string[] args)
    {
        var p = new ProjectStore(root).LoadOrCreate();
        var r = ProjectValidator.Validate(p);
        if (!r.Ok) { Console.Error.WriteLine(r.ToHumanText()); return 1; }
        var frames = int.TryParse(Option(args, "--frames"), out var parsed) ? parsed : 30;
        var output = Option(args, "--out") ?? Path.Combine(root, "build", "screenshot.png");
        var runtime = new VisualRuntime(p, root);
        var eventId = Option(args, "--event");
        if (eventId != null) runtime.DebugStartEvent(eventId,
            int.TryParse(Option(args, "--event-page-index"), out var eventPage) ? eventPage : -1);
        var startMap = Option(args, "--map");
        if (startMap != null) runtime.DebugStartAt(startMap,
            int.TryParse(Option(args, "--x"), out var sx) ? sx : 1,
            int.TryParse(Option(args, "--y"), out var sy) ? sy : 1);
        if (Array.IndexOf(args, "--title") >= 0) runtime.ForceTitle();
        if (Array.IndexOf(args, "--splash") >= 0) runtime.ForceSplash();
        if (Array.IndexOf(args, "--editor") >= 0) runtime.ForceEditor(
            int.TryParse(Option(args, "--editor-zoom"), out var ez) ? ez : 1,
            int.TryParse(Option(args, "--editor-tool"), out var et) ? et : 0);
        if (Array.IndexOf(args, "--pause") >= 0) runtime.ForcePause(
            int.TryParse(Option(args, "--pause-section"), out var ps) ? ps : 1);
        if (Array.IndexOf(args, "--attack") >= 0) runtime.DebugAutoAttack();
        var scrubEvent = Option(args, "--scrub");
        if (scrubEvent != null) runtime.DebugScrub(scrubEvent,
            int.TryParse(Option(args, "--scrub-steps"), out var ss) ? ss : 0,
            int.TryParse(Option(args, "--scrub-page-index"), out var scrubPage) ? scrubPage : -1);
        runtime.Run(frames, output, hidden: true, crtCapture: Array.IndexOf(args, "--crt") >= 0);
        Console.WriteLine($"Screenshot: {Path.GetFullPath(output)}");
        return 0;
    }
    /// <summary>playtest.run por CLI: corre un guion de partida determinista con el juego
    /// oculto y escribe/imprime el reporte JSON. --steps archivo (un paso por linea, # comenta).</summary>
    /// <summary>Corre la SUITE DE EVALS del juego (evals/*.txt). Es la regresion de gameplay:
    /// los smokes prueban el motor, los evals prueban que este juego sigue siendo jugable.
    /// Exit 1 si alguno falla, para poder usarlo como gate.</summary>
    static int Evals(string root, string[] args)
    {
        var report = EvalSuite.Run(root, Option(args, "--only"));
        Console.WriteLine(EvalSuite.ToHumanText(report, root));
        var output = Option(args, "--out");
        if (output != null)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
            File.WriteAllText(output, EvalSuite.ToJson(report), new UTF8Encoding(false));
            Console.WriteLine($"Reporte: {Path.GetFullPath(output)}");
        }
        return report["ok"]?.GetValue<bool>() == true ? 0 : 1;
    }

    static int PlaytestRun(string root, string[] args)
    {
        var p = new ProjectStore(root).LoadOrCreate();
        var r = ProjectValidator.Validate(p);
        if (!r.Ok) { Console.Error.WriteLine(r.ToHumanText()); return 1; }
        var stepsFile = Option(args, "--steps");
        if (stepsFile == null || !File.Exists(stepsFile)) { Console.Error.WriteLine("Falta --steps <archivo> (un paso por linea; # comenta). Pasos: move/face/interact/auto/choose/confirm/wait/assert-*/screenshot/dump."); return 2; }
        var runtime = new VisualRuntime(p, root);
        runtime.DebugRunScript(File.ReadAllLines(stepsFile));
        runtime.Run(30000, null, hidden: true, crtCapture: Array.IndexOf(args, "--crt") >= 0);
        var report = runtime.BuildScriptReport();
        var output = Option(args, "--out") ?? Path.Combine(root, "build", "playtest-report.json");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output))!);
        File.WriteAllText(output, report);
        Console.WriteLine(report);
        var ok = System.Text.Json.Nodes.JsonNode.Parse(report)?["ok"]?.GetValue<bool>() == true;
        return ok ? 0 : 1;
    }

    static int Pack(string root)
    {
        var s = new ProjectStore(root);
        var p = s.LoadOrCreate();
        var r = ProjectValidator.Validate(p);
        if (!r.Ok) { Console.Error.WriteLine(r.ToHumanText()); return 1; }
        var gate = QualityAudit.CheckForPack(p, root);
        if (gate != null) { Console.Error.WriteLine(gate.ToHumanText()); return 1; }
        Console.WriteLine($"Pack creado: {s.BuildPack(p)}");
        return 0;
    }
    static int RunPack(string[] args) { var pack = Option(args, "--pack") ?? "game.pack"; var p = ProjectStore.LoadPack(pack); var r = ProjectValidator.Validate(p); if (!r.Ok) { Console.Error.WriteLine(r.ToHumanText()); return 1; } var framesText = Option(args, "--frames"); int? frames = int.TryParse(framesText, out var parsed) ? parsed : null; new VisualRuntime(p).Run(frames); return 0; }
    static int ValidatePack(string[] args) { var pack = Option(args, "--pack") ?? "game.pack"; var p = ProjectStore.LoadPack(pack); var r = ProjectValidator.Validate(p); Console.WriteLine(r.ToHumanText()); return r.Ok ? 0 : 1; }
    static int ShopSmoke(string root)
    {
        var p = new ProjectStore(root).LoadOrCreate();
        var shopDef = p.Shops.FirstOrDefault();
        if (shopDef == null || shopDef.ItemIds.Count == 0) { Console.Error.WriteLine("No hay tienda con items en el proyecto."); return 1; }
        var inventory = new List<string>();
        var shop = new ShopSession(shopDef, p, 50, inventory);
        var item = p.Items.First(i => i.Id == shopDef.ItemIds[0]);
        if (!shop.Confirm() || shop.Money != 50 - item.Price || inventory.Count != 1) { Console.Error.WriteLine($"Compra fallo: money={shop.Money} inv={inventory.Count}"); return 1; }
        Console.WriteLine($"Compra OK: {item.Name} por ${item.Price}, quedan ${shop.Money}, inventario={inventory.Count}.");
        shop.ToggleMode();
        var expected = shop.Money + ShopSession.SellValue(item.Price);
        if (!shop.Confirm() || shop.Money != expected || inventory.Count != 0) { Console.Error.WriteLine($"Venta fallo: money={shop.Money} inv={inventory.Count}"); return 1; }
        Console.WriteLine($"Venta OK: {item.Name} por ${ShopSession.SellValue(item.Price)} (mitad), quedan ${shop.Money}, inventario vacio.");
        shop.ToggleMode();
        for (var i = 0; i < 20 && shop.Confirm(); i++) { }
        Console.WriteLine($"Compra hasta quedarse sin plata OK: ${shop.Money} restantes, {inventory.Count} items, log='{shop.Log}'.");
        // Item CLAVE (precio 0): jamas aparece en el mostrador de venta (antes se vendia por $1).
        var keyInventory = new List<string> { "item.clave_smoke" };
        var keyProject = new GameProject { Items = [new ItemDef { Id = "item.clave_smoke", Name = "Llave", Price = 0 }] };
        var keyShop = new ShopSession(shopDef, keyProject, 10, keyInventory);
        keyShop.ToggleMode();
        if (keyShop.Rows.Count != 0) { Console.Error.WriteLine("Un item de precio 0 aparecio en el mostrador de venta."); return 1; }
        Console.WriteLine("Item clave OK: precio 0 no aparece en el mostrador de venta.");
        return 0;
    }

    /// <summary>
    /// Crea un proyecto nuevo minimo y jugable: un mapa con borde, un heroe y un NPC guia.
    /// Desde ahi el humano edita con F1 y la IA con MCP; nadie arranca frente a un JSON vacio.
    ///
    /// DOS MODOS DE ARRANQUE, un solo proyecto resultante (todo juego del motor es juego espejo:
    /// nace con Libro Espejo y se publica como juego y como manuscrito):
    ///   new --project X                        juego nuevo; la historia se escribe con el juego.
    ///   new --project X --from manuscrito.md   parte de un texto ya escrito: el manuscrito entra
    ///                                          como capitulos/escenas y el juego se construye
    ///                                          desde ahi (la prosa no genera gameplay sola).
    /// </summary>
    static int NewProject(string root, string[] args)
    {
        root = Path.GetFullPath(root);
        if (File.Exists(Path.Combine(root, "project.json"))) { Console.Error.WriteLine($"Ya existe un proyecto en {root}. Elegir otra carpeta con --project."); return 2; }
        // El manuscrito se resuelve contra el directorio actual (donde lo tiene el autor), no
        // contra el proyecto que todavia no existe. Se verifica ANTES de crear nada.
        var manuscript = Option(args, "--from") is { Length: > 0 } from ? Path.GetFullPath(from) : null;
        if (manuscript != null && !File.Exists(manuscript))
        {
            Console.Error.WriteLine($"No existe el manuscrito '{manuscript}'.");
            Console.Error.WriteLine("Sin --from se crea un juego nuevo en blanco (tambien con Libro Espejo).");
            return 2;
        }
        var name = Path.GetFileName(root.TrimEnd(Path.DirectorySeparatorChar));
        var safeId = new string([.. name.ToLowerInvariant().Where(char.IsLetterOrDigit)]);
        if (safeId.Length == 0) safeId = "juego";

        var map = new MapDef { Id = "map.inicio", Name = "Inicio", TilesetId = "tileset.base", Width = 16, Height = 12, Tiles = [.. Enumerable.Repeat(0, 16 * 12)] };
        for (var x = 0; x < 16; x++) { map.Tiles[x] = 2; map.Tiles[11 * 16 + x] = 2; }
        for (var y = 0; y < 12; y++) { map.Tiles[y * 16] = 2; map.Tiles[y * 16 + 15] = 2; }
        map.EventIds.Add("event.guia");

        var project = new GameProject
        {
            Id = $"game.{safeId}",
            Title = name,
            StartMapId = "map.inicio",
            StartEventId = "event.guia",
            Tilesets = [new TilesetDef { Id = "tileset.base", TileSize = 16, Tiles = [
                new TileDef { Id = 0, Name = "pasto", Color = "#5cad5c" },
                new TileDef { Id = 1, Name = "camino", Color = "#d0b060" },
                new TileDef { Id = 2, Name = "pared", Solid = true, Color = "#604040" }] }],
            Maps = [map],
            Actors = [new ActorDef { Id = "actor.heroe", Name = "Heroe", Level = 1, Stats = new StatBlock() }],
            Dialogues = [new DialogueDef { Id = "dialogue.bienvenida", StartNodeId = "inicio", Nodes = [
                new DialogueNode { Id = "inicio", Speaker = "Guia", Text = "Bienvenido a tu juego. F1 abre el editor; la IA puede ayudarte via MCP." }] }],
            Events = [new EventDef { Id = "event.guia", MapId = "map.inicio", Name = "Guia", Kind = EventKind.Npc, X = 8, Y = 5, RoutineId = "idle",
                Pages = [new EventPage { Commands = [new EventCommand { Kind = CommandKind.Dialogue, TargetId = "dialogue.bienvenida" }] }] }],
            QualityPlan = new QualityPlanDef
            {
                CanonicalRouteId = "route.canon",
                Routes =
                [
                    new QualityRouteDef
                    {
                        Id = "route.canon",
                        Name = "Ruta canonica",
                        Description = "Primer contrato de calidad; ampliar con cada capitulo.",
                        Checkpoints =
                        [
                            new QualityCheckpointDef
                            {
                                Id = "cp.bienvenida",
                                Label = "Hablar con el guia",
                                EventId = "event.guia",
                                PageIndex = 0,
                                ExpectedMapId = "map.inicio",
                                ExpectedPartyActorIds = ["actor.heroe"]
                            }
                        ]
                    }
                ]
            },
            StoryBook = new StoryBookDef
            {
                Title = name,
                ShortTitle = name,
                Chapters =
                [
                    new StoryChapterDef
                    {
                        Id = "chapter.inicio", Title = "El comienzo", Summary = "La primera escena jugable y literaria.",
                        Scenes =
                        [
                            new StorySceneDef
                            {
                                Id = "scene.bienvenida", Title = "La bienvenida", Status = "draft",
                                Synopsis = "El heroe llega y el guia presenta el mundo.",
                                Links =
                                [
                                    new StoryLinkDef { Kind = "map", Id = "map.inicio", Role = "setting" },
                                    new StoryLinkDef { Kind = "event", Id = "event.guia", Role = "trigger" },
                                    new StoryLinkDef { Kind = "dialogue", Id = "dialogue.bienvenida", Role = "source" }
                                ]
                            }
                        ]
                    }
                ]
            }
        };

        var check = ProjectValidator.Validate(project);
        if (!check.Ok) { Console.Error.WriteLine(check.ToHumanText()); return 1; }
        new ProjectStore(root).Save(project);
        WriteDesignTemplate(root, name);
        // Handshake de co-autoria: el proyecto nuevo se explica solo. Sin esto, un agente que
        // abre la carpeta ve un project.json y nada mas — no sabe que existen 57 herramientas
        // MCP ni como enchufarse a ellas.
        WriteAgentsTemplate(root, name);
        WriteMcpConfig(root);
        WriteProjectGitignore(root);

        // Modo "importar historia": el manuscrito entra primero; recien si entro bien se retira la
        // escena de arranque (si el import falla, el proyecto queda intacto y jugable).
        if (manuscript != null)
        {
            var session = new CommandSession(root);
            var payload = ToolRegistry.Call("story.import", new JsonObject { ["source"] = manuscript, ["defaultTitle"] = name }, session);
            if (!payload.Ok || payload.Data is not StoryImportSummary summary)
            {
                Console.Error.WriteLine(payload.Error is null ? "El import fallo." : $"{payload.Error.Code}: {payload.Error.Message}\n  {payload.Error.Fix}");
                Console.Error.WriteLine($"El proyecto quedo creado y jugable en {root}; reintentar con: import-story --project \"{root}\" --from \"{manuscript}\"");
                return 1;
            }
            ToolRegistry.Call("story.delete", new JsonObject { ["kind"] = "chapter", ["id"] = "chapter.inicio" }, session);
            Console.WriteLine($"Manuscrito importado: {summary.Chapters} capitulos, {summary.Scenes} escenas, {summary.Words} palabras (corte por {summary.Mode}).");
            foreach (var chapter in summary.Structure) Console.WriteLine($"  {chapter.Id}  {chapter.Title} ({chapter.Scenes.Count} escenas)");
            foreach (var warning in summary.Warnings) Console.WriteLine("AVISO: " + warning);
            Console.WriteLine("Las escenas entraron en draft y SIN links al juego: el gameplay se construye escena por escena.");
        }

        Console.WriteLine($"""
Proyecto creado: {root}
  Diseno:        {Path.Combine(root, "design.md")}  (completar ANTES de construir: guion primero)
  Para la IA:    {Path.Combine(root, "AGENTS.md")} + .mcp.json (ya configurado, listo para enchufar)
  Guia completa: {GuidePath()}
  Jugar/editar:  dotnet run --project Seto90 -- run --project "{root}"   (F1 = editor)
  IA via MCP:    dotnet run --project Seto90 -- mcp --project "{root}"
  Libro:         F1 > LIBRO | dotnet run --project Seto90 -- export-book --project "{root}"
  Validar:       dotnet run --project Seto90 -- validate --project "{root}"
  Calidad:       dotnet run --project Seto90 -- quality-audit --project "{root}" --run-playtests
  Publicar:      dotnet run --project Seto90 -- publish --project "{root}"
""");
        return 0;
    }

    /// <summary>El documento de diseno nace con el proyecto: todo juego arranca con guion, no
    /// con un JSON. Breve a proposito (la columna vertebral se cierra; los detalles quedan
    /// abiertos porque el playtesting manda). Convencion, no codigo: el motor no lo interpreta.</summary>
    static void WriteDesignTemplate(string root, string title)
    {
        var path = Path.Combine(root, "design.md");
        if (File.Exists(path)) return;
        File.WriteAllText(path, $"""
# {title} - Documento de diseno

Breve a proposito: la columna vertebral se cierra aca; los detalles se descubren jugando.
Se versiona en git junto al proyecto y se actualiza al final de cada capitulo.

## Premisa

(2-3 lineas: quien es el heroe, que quiere, que se lo impide, como termina.)

## Tono

(Ej: melancolico con humor cotidiano. Referencias concretas.)

## Core loop

(Que hace el jugador una y otra vez y por que es rico. Ej: explorar pueblo ->
hablar/pistas -> zona de combate -> recompensa -> mejorar party -> nueva zona.)

## Personajes

- Heroe: (nombre, motivacion, arco en una linea)
- Aliado 1:
- Antagonista:

## Mapa de capitulos

Una linea por capitulo. El capitulo 0 es la vertical slice: el core loop completo
en miniatura con placeholders, jugable de punta a punta antes de hacer arte.

- Cap 0 (vertical slice):
- Cap 1:
- Cap 2:

## Biblia de estilo

- Tile: 16x16. Personajes: (16x16 o 16x24). Paleta base: (colores o referencia DB16).
- Proporciones y sombreado: (ej: chibi cabeza 40%, sombreado plano 1-2 tonos, estilo retro medido).
- Musica: (tempo/ondas de referencia, ej: square melodica + triangle bajo).

## Convencion de flags

`flag.cap<N>_<hecho>` (ej: `flag.cap1_hablo_alcalde`). Cada beat del guion que cambia
el mundo nombra su flag aca ANTES de construirse: el guion habla el idioma del motor.

| Flag | Capitulo | Que significa |
|------|----------|---------------|
|      |          |               |

## Reglas de produccion

1. Escena gris primero (tiles planos, sprites placeholder), arte al final.
2. Cada capitulo termina jugable + validado + commiteado.
3. El guion es vivo: lo que el playtesting contradiga, se reescribe aca.
4. Cada escena canonica mantiene sus dos expresiones en el Libro Espejo: gameplay y prosa.
""");
    }

    /// <summary>Raiz del motor (la carpeta que contiene Seto90.sln), subiendo desde el binario.
    /// Devuelve null si el motor corre publicado/aislado, donde no hay repo alrededor.</summary>
    static string? EngineRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (var i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "Seto90.sln"))) return dir.FullName;
        return null;
    }

    /// <summary>Ruta de la guia de autoria. El repo publico la publica en ingles (AUTHORING.md)
    /// y el repo de desarrollo la mantiene en espanol; se nombra la que exista de verdad.</summary>
    static string GuidePath()
    {
        var root = EngineRoot();
        if (root == null) return "AUTHORING.md (en el repositorio del motor)";
        foreach (var name in new[] { "AUTHORING.md", "COMO-CREAR-UN-JUEGO.md" })
            if (File.Exists(Path.Combine(root, name))) return Path.Combine(root, name);
        return "AUTHORING.md (en el repositorio del motor)";
    }

    /// <summary>Como invocar este mismo motor desde afuera. Se prefiere el ejecutable ya
    /// construido antes que `dotnet run`: con una ventana del juego abierta el build queda
    /// bloqueado por el exe en uso, y un servidor MCP que no arranca por eso es un misterio caro.</summary>
    static (string Command, string[] Prefix) EngineInvocation()
    {
        var exe = Environment.ProcessPath;
        var isDotnetHost = exe == null || Path.GetFileNameWithoutExtension(exe).Equals("dotnet", StringComparison.OrdinalIgnoreCase);
        if (!isDotnetHost) return (exe!, []);
        var root = EngineRoot();
        var csproj = root == null ? "Seto90" : Path.Combine(root, "Seto90", "Seto90.csproj");
        return ("dotnet", ["run", "--project", csproj, "--"]);
    }

    /// <summary>Config de cliente MCP lista para usar: el agente se enchufa sin configurar nada.
    /// Es config LOCAL del autor (rutas absolutas de esta maquina), por eso el .gitignore
    /// generado la excluye.</summary>
    static void WriteMcpConfig(string root)
    {
        var path = Path.Combine(root, ".mcp.json");
        if (File.Exists(path)) return;
        var (command, prefix) = EngineInvocation();
        var argv = new JsonArray();
        foreach (var a in prefix) argv.Add(a);
        argv.Add("mcp");
        argv.Add("--project");
        argv.Add(root);
        var config = new JsonObject
        {
            ["mcpServers"] = new JsonObject
            {
                ["90s-engine"] = new JsonObject { ["command"] = command, ["args"] = argv }
            }
        };
        File.WriteAllText(path, config.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
    }

    /// <summary>El .gitignore del PROYECTO (no el del motor): artefactos regenerables y la
    /// config local de MCP, que tiene rutas de esta maquina.</summary>
    static void WriteProjectGitignore(string root)
    {
        var path = Path.Combine(root, ".gitignore");
        if (File.Exists(path)) return;
        File.WriteAllText(path, """
# Artefactos regenerables (pack, capturas, export de assets y del libro)
build/
# Distribucion publicada
dist/
# Config local de cliente MCP: tiene rutas absolutas de esta maquina
.mcp.json
# Backup del guardado atomico de project.json
project.json.bak
""", new UTF8Encoding(false));
    }

    /// <summary>Instrucciones para el agente que abra este proyecto. Sin esto la carpeta es un
    /// project.json mudo: el agente no sabe que existen 57 herramientas MCP, ni que project.json
    /// no se edita a mano, ni donde esta la guia. En ingles porque es el idioma de trabajo de
    /// los agentes; el contenido del juego sigue en el idioma que elija el autor.</summary>
    static void WriteAgentsTemplate(string root, string title)
    {
        var path = Path.Combine(root, "AGENTS.md");
        if (File.Exists(path)) return;
        var (command, prefix) = EngineInvocation();
        var invocation = prefix.Length == 0 ? command : command + " " + string.Join(" ", prefix);
        File.WriteAllText(path, $"""
# {title}

A **90s Engine** project: a 90s-style JRPG that is authored together with its mirror novel.
You are expected to co-author it through the engine's MCP server, alongside a human working
in the in-game editor (F1) on the very same session.

Full authoring guide: `{GuidePath()}`

## Rules that are not negotiable

1. **Never hand-edit `project.json`.** Every write goes through an MCP tool. The file is the
   store, not the interface: each tool validates the whole project and rolls itself back if it
   would leave a dangling reference.
2. **Check `ok` on every single response**, not just the last one. A failed write is reverted
   silently; a final `validate` that says ok proves nothing about the calls before it.
3. **Read the design document before building anything** (`project.design`, or the `design.md`
   file next to this one). The script comes first; the JSON is a consequence. If it is still a
   blank template, do not build: ask the author for premise, tone, core loop, characters and
   chapters, then write what you agree on back with `project.design`.
4. **Grey-box first.** Flat tiles and placeholder sprites until a chapter is playable end to
   end. Art last.

## Connecting

`.mcp.json` in this folder is already configured — point your client at it and the 57 tools
appear. To run the server by hand:

```
{invocation} mcp --project "{root}"
```

The transport is stdio JSON-RPC: `initialize`, `tools/list`, `tools/call`.

## The loop

Build with MCP → **see** it with `playtest.screenshot` → **play** it with `playtest.run`
(headless, deterministic: same script, same result) → correct yourself. You never have to ask
the human what your content looks like.

When a chapter is finished, **leave an eval**: save the playtest script that walks it, with
assertions, as `evals/<name>.txt`. `evals.run` replays the whole suite, so any later change that
breaks an earlier chapter shows up on its own. Run it after touching old content.

```
{invocation} run --project "{root}"            # play/edit live, F1 = editor
{invocation} validate --project "{root}"
{invocation} quality-audit --project "{root}" --run-playtests
{invocation} export-book --project "{root}"    # the mirror novel, Markdown + DOCX
```

## Gotchas that cost hours

- **Piping to the MCP server from PowerShell:** use `$OutputEncoding = New-Object
  System.Text.UTF8Encoding($false)`. `[System.Text.Encoding]::UTF8` emits a BOM and the server
  rejects the first line with "invalid start of a value".
- **A running game locks the build.** With a game window open, `dotnet run` fails; invoke the
  built executable directly instead.
- **Hot reload only adopts in the world state.** A write that lands while a dialogue, battle or
  menu is open is picked up when the player returns to the map.
""", new UTF8Encoding(false));
    }

    /// <summary>Publica el juego listo para distribuir: dist/ con el runtime self-contained
    /// (un solo exe win-x64) y el game.pack al lado. Doble click en el exe = jugar.</summary>
    static int Publish(string root, string[] args)
    {
        var store = new ProjectStore(root);
        var p = store.LoadOrCreate();
        var check = ProjectValidator.Validate(p);
        if (!check.Ok) { Console.Error.WriteLine(check.ToHumanText()); return 1; }
        var assets = AssetPipeline.Validate(p, root);
        if (!assets.Ok) { Console.Error.WriteLine(assets.ToHumanText()); return 1; }
        var gate = QualityAudit.CheckForPack(p, root);
        if (gate != null) { Console.Error.WriteLine(gate.ToHumanText()); return 1; }

        var engineProject = FindEngineProject();
        if (engineProject == null) { Console.Error.WriteLine("No encontre Seto90/Seto90.csproj (correr publish desde el repo del motor)."); return 1; }

        var dist = Path.GetFullPath(Option(args, "--out") ?? Path.Combine(root, "dist"));
        Directory.CreateDirectory(dist);
        File.Copy(store.BuildPack(p), Path.Combine(dist, "game.pack"), true);

        Console.WriteLine("Publicando runtime self-contained (Release win-x64, un solo exe)...");
        var stage = Path.Combine(Path.GetTempPath(), "Seto90", "publish");
        var psi = new System.Diagnostics.ProcessStartInfo("dotnet")
        {
            ArgumentList = { "publish", engineProject, "-c", "Release", "-r", "win-x64", "--self-contained", "true",
                "-p:PublishSingleFile=true", "-p:IncludeNativeLibrariesForSelfExtract=true", "-o", stage },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        process.WaitForExit();
        if (process.ExitCode != 0) { Console.Error.WriteLine(process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd()); return 1; }

        var exeName = new string([.. p.Title.Where(char.IsLetterOrDigit)]);
        if (exeName.Length == 0) exeName = "Juego";
        File.Copy(Path.Combine(stage, "Seto90.exe"), Path.Combine(dist, exeName + ".exe"), true);
        File.WriteAllText(Path.Combine(dist, "LEEME.txt"), $"""
{p.Title}
Made with 90s Engine.

Doble click en {exeName}.exe para jugar (necesita game.pack en la misma carpeta).
Controles: flechas mover, Enter hablar, Esc menu, F2 filtro CRT.
""", new UTF8Encoding(false)); // sin BOM: es lo primero que abre el jugador
        Console.WriteLine($"Publicado en: {dist}");
        Console.WriteLine($"  {exeName}.exe + game.pack + LEEME.txt");
        return 0;
    }

    static string? FindEngineProject()
    {
        var dir = new DirectoryInfo(Environment.CurrentDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "Seto90", "Seto90.csproj");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        return null;
    }

    static int PartySmoke(string root)
    {
        var p = new ProjectStore(root).LoadOrCreate();
        var party = PartyState.Create(p);
        var member = party.Members[0];
        var (level0, attack0, maxHp0) = (member.Level, member.Stats.Attack, member.MaxHp);
        member.Hp = Math.Max(1, member.MaxHp - 10); // llega lastimado al level up
        var messages = party.GrantExp(PartyMember.ExpToNext(level0));
        if (member.Level != level0 + 1 || messages.Count != 1) { Console.Error.WriteLine($"Level up fallo: nivel={member.Level}."); return 1; }
        if (member.Stats.Attack <= attack0 || member.MaxHp <= maxHp0) { Console.Error.WriteLine("Los stats no crecieron."); return 1; }
        if (member.Hp != maxHp0 - 10 + (member.MaxHp - maxHp0)) { Console.Error.WriteLine($"El HP ganado no vino curado: {member.Hp}."); return 1; }
        Console.WriteLine($"Level up OK: {messages[0]} Atk {attack0}->{member.Stats.Attack}, HPmax {maxHp0}->{member.MaxHp}, HP curado en el up.");

        var battleDef = p.Battles.FirstOrDefault();
        if (battleDef != null)
        {
            member.Hp = 7;
            var battle = new BattleEngine(battleDef, p, party, []);
            if (battle.Party[0].Hp != 7) { Console.Error.WriteLine("El HP persistente no entro al combate."); return 1; }
            Console.WriteLine($"Combate con HP persistente OK: entra con 7/{member.MaxHp}.");
        }

        var healItem = p.Items.FirstOrDefault(i => i.Effect.StartsWith("heal:", StringComparison.OrdinalIgnoreCase));
        if (healItem != null)
        {
            member.Hp = 1;
            var note = party.UseHealItem(healItem);
            if (note == null || member.Hp <= 1) { Console.Error.WriteLine("El item curativo no curo."); return 1; }
            Console.WriteLine($"Item en menu OK: {note}");
        }

        // Equipamiento: sintetico (no depende del contenido): equipar suma, cambiar devuelve, sacar resta.
        var espada = new ItemDef { Id = "item.smoke_espada", Name = "Espada", Slot = "weapon", Bonus = new StatBlock { Hp = 0, Mp = 0, Attack = 3, Defense = 0, Speed = 0 } };
        var hacha = new ItemDef { Id = "item.smoke_hacha", Name = "Hacha", Slot = "weapon", Bonus = new StatBlock { Hp = 0, Mp = 0, Attack = 5, Defense = 0, Speed = 0 } };
        var gorra = new ItemDef { Id = "item.smoke_gorra", Name = "Gorra", Slot = "armor", Bonus = new StatBlock { Hp = 4, Mp = 0, Attack = 0, Defense = 2, Speed = 0 } };
        var baseAtk = member.Stats.Attack;
        var baseDef = member.Stats.Defense;
        var baseMaxHp = member.MaxHp;
        if (member.Equip(espada) != null) { Console.Error.WriteLine("Equipar con slot vacio devolvio algo."); return 1; }
        if (member.Stats.Attack != baseAtk + 3) { Console.Error.WriteLine($"La espada no sumo: Atk {member.Stats.Attack}, esperaba {baseAtk + 3}."); return 1; }
        var devuelta = member.Equip(hacha);
        if (devuelta?.Id != espada.Id || member.Stats.Attack != baseAtk + 5) { Console.Error.WriteLine("Cambiar de arma no devolvio la anterior o no sumo bien."); return 1; }
        member.Equip(gorra);
        if (member.Stats.Defense != baseDef + 2 || member.MaxHp != baseMaxHp + 4) { Console.Error.WriteLine("La gorra no sumo Def/HP."); return 1; }
        var sacada = member.Unequip("weapon");
        if (sacada?.Id != hacha.Id || member.Stats.Attack != baseAtk) { Console.Error.WriteLine("Sacar el arma no restauro el ataque."); return 1; }
        Console.WriteLine($"Equipo OK: espada +3 -> hacha +5 (devuelve espada) -> gorra +2Def+4HP -> sacar arma restaura Atk {baseAtk}.");

        // Revivir desde el menu: un caido vuelve con los HP del item.
        member.Hp = 0;
        var pluma = new ItemDef { Id = "item.smoke_pluma", Name = "Pluma", Effect = "revive:8" };
        var revNote = party.UseHealItem(pluma);
        if (revNote == null || member.Hp != 8) { Console.Error.WriteLine($"Revivir desde el menu fallo: hp={member.Hp}."); return 1; }
        Console.WriteLine($"Revive en menu OK: {revNote}");

        // Posada: descansar deja HP/MP al maximo y levanta caidos.
        member.Hp = 0;
        member.Mp = 0;
        party.RestAll();
        if (member.Hp != member.MaxHp || member.Mp != member.Stats.Mp) { Console.Error.WriteLine($"La posada no restauro: hp={member.Hp}/{member.MaxHp} mp={member.Mp}."); return 1; }
        Console.WriteLine($"Posada OK: la party despierta con {member.Hp}/{member.MaxHp} HP y {member.Mp} MP.");
        return 0;
    }

    static int SaveSmoke(string root)
    {
        var p = new ProjectStore(root).LoadOrCreate();
        var save = new SaveSystem(p.Id);
        var state = new SaveGame { ProjectId = p.Id, MapId = p.StartMapId, PlayerX = 1, PlayerY = 1, Money = 33, Flags = p.Variables.Where(v => v.Kind == VariableKind.Flag).ToDictionary(v => v.Id, v => v.Default.Equals("true", StringComparison.OrdinalIgnoreCase)), Party = [new PartyMemberSave { ActorId = "actor.smoke", Level = 3, Exp = 12, Hp = 20, Mp = 4, WeaponId = "item.espada", ArmorId = "item.gorra" }] };
        save.Save(0, state);
        var loaded = save.Load(0);
        if (loaded.Money != 33) { Console.Error.WriteLine("El dinero no sobrevivio al save."); return 1; }
        if (loaded.Party.Count != 1 || loaded.Party[0].WeaponId != "item.espada" || loaded.Party[0].ArmorId != "item.gorra" || loaded.Party[0].Mp != 4) { Console.Error.WriteLine("El equipo/MP de la party no sobrevivio al save."); return 1; }
        Console.WriteLine($"Save OK: slot 0 {loaded.ProjectId} {loaded.MapId} ({loaded.PlayerX},{loaded.PlayerY}) ${loaded.Money}");
        state.PlayerX = 5;
        save.Save(1, state);
        if (!save.HasSlot(1) || save.TryLoad(1)?.PlayerX != 5) { Console.Error.WriteLine("Slot 1 fallo."); return 1; }
        if (save.MostRecentSlot() < 0) { Console.Error.WriteLine("MostRecentSlot no encontro nada."); return 1; }
        Console.WriteLine($"Multi-slot OK: slot 1 guardado y legible; mas reciente = slot {save.MostRecentSlot() + 1}.");

        // Preferencias del jugador (volumenes): roundtrip y restauracion de lo que hubiera.
        var originalSettings = PlayerSettings.Load();
        try
        {
            new PlayerSettings { MusicVolume = 0.5, SfxVolume = 0.7 }.Save();
            var reloaded = PlayerSettings.Load();
            if (Math.Abs(reloaded.MusicVolume - 0.5) > 0.001 || Math.Abs(reloaded.SfxVolume - 0.7) > 0.001)
            { Console.Error.WriteLine($"Settings no sobrevivieron: {reloaded.MusicVolume}/{reloaded.SfxVolume}."); return 1; }
            Console.WriteLine("Settings OK: volumenes 0.5/0.7 persisten y recargan.");
        }
        finally { originalSettings.Save(); }
        return 0;
    }
}
