using System.Text;
using System.Text.Json.Nodes;

namespace Seto90;

/// <summary>
/// Smoke headless de la co-autoria entre sesiones: dos CommandSession sobre el MISMO
/// directorio simulan al editor en vivo y al MCP (dos procesos). Verifica la guardia de revision
/// (rebase: nadie pisa a nadie), el rechazo de undo con cambio externo, el modo batch
/// (todo-o-nada, referencias adelantadas, un solo undo) y la persistencia atomica con recuperacion
/// de project.json/pack. Sin raylib, proyecto temporal desechable.
/// </summary>
public sealed class SessionSmokeTest
{
    public string Run()
    {
        var dir = Path.Combine(Path.GetTempPath(), "seto90-session-smoke-" + Guid.NewGuid().ToString("N")[..8]);
        try { return RunIn(dir); }
        finally { try { Directory.Delete(dir, true); } catch { /* mejor esfuerzo */ } }
    }

    static string RunIn(string dir)
    {
        var sb = new StringBuilder();

        // Proyecto minimo valido: tileset + mapa + spawn.
        var a = new CommandSession(dir);
        var setup = a.Mutate(p =>
        {
            p.Id = "game.smoke";
            p.Title = "Session Smoke";
            p.Tilesets.Add(new TilesetDef { Id = "tileset.t", TileSize = 16, Tiles = [new TileDef { Id = 0, Name = "piso", Color = "#204020" }, new TileDef { Id = 1, Name = "pared", Solid = true, Color = "#404040" }] });
            p.Maps.Add(new MapDef { Id = "map.m", Name = "Mapa", TilesetId = "tileset.t", Width = 4, Height = 3, Tiles = [.. Enumerable.Repeat(0, 12)] });
            p.StartMapId = "map.m";
            p.StartX = 1;
            p.StartY = 1;
        });
        Expect(setup.Ok, $"setup fallo: {setup.Error?.Code} {setup.Error?.Message}");

        // ---- Rebase: B (copia vieja) escribe DESPUES de A; el disco termina con LOS DOS cambios ----
        var b = new CommandSession(dir);                                     // B carga la revision actual
        Expect(a.Mutate(p => p.Maps[0].Name = "Renombrado por A").Ok, "A no pudo escribir");
        var writeB = b.Mutate(p => p.Maps[0].Tiles[0] = 1);                  // B esta desactualizado: debe rebasear
        Expect(writeB.Ok, $"B no pudo escribir tras el cambio de A: {writeB.Error?.Message}");
        var disk = new ProjectStore(dir).LoadOrCreate();
        Expect(disk.Maps[0].Name == "Renombrado por A", "el rebase perdio el cambio de A (la vieja carrera 7b)");
        Expect(disk.Maps[0].Tiles[0] == 1, "el rebase perdio el cambio de B");
        sb.AppendLine("Rebase OK: dos sesiones escribieron en cascada y el disco conserva ambos cambios.");

        // ---- Undo con cambio externo sin adoptar: rechazado (deshacer pisaria al otro) ----
        Expect(a.Mutate(p => p.Maps[0].Name = "A de nuevo").Ok, "A no pudo re-escribir");
        var undoB = b.Undo();                                                // B quedo viejo otra vez
        Expect(!undoB.Ok && undoB.Error!.Code == "external_change", $"undo desactualizado debio fallar con external_change, dio {(undoB.Ok ? "ok" : undoB.Error!.Code)}");
        sb.AppendLine("Undo con cambio externo OK: rechazado con external_change (nadie pisa a nadie).");

        // ---- Batch positivo con referencia ADELANTADA: evento habla con un dialogo creado despues ----
        var before = new ProjectStore(dir).Snapshot(new ProjectStore(dir).LoadOrCreate());
        var batchOk = ToolRegistry.Call("batch.apply", Args("""
        {"calls":[
          {"name":"event.create","arguments":{"id":"event.npc","mapId":"map.m","name":"NPC","kind":"Npc","x":2,"y":1}},
          {"name":"event.set_commands","arguments":{"eventId":"event.npc","commands":[{"kind":"Dialogue","targetId":"dialogue.d"}]}},
          {"name":"dialogue.create","arguments":{"id":"dialogue.d","startNodeId":"n1","nodes":[{"id":"n1","speaker":"NPC","text":"Hola."}]}}
        ]}
        """), a);
        Expect(batchOk.Ok, $"batch valido fallo: {batchOk.Error?.Code} {batchOk.Error?.Message}");
        Expect(a.Project.Events.Any(e => e.Id == "event.npc") && a.Project.Dialogues.Any(d => d.Id == "dialogue.d"), "el batch no aplico sus pasos");
        // UN undo deshace el batch ENTERO.
        Expect(a.Undo().Ok, "undo del batch fallo");
        var afterUndo = new ProjectStore(dir).LoadOrCreate();
        Expect(!afterUndo.Events.Any(e => e.Id == "event.npc") && !afterUndo.Dialogues.Any(d => d.Id == "dialogue.d"), "un solo undo no deshizo el batch entero");
        sb.AppendLine("Batch OK: referencia adelantada valida al final y UN undo lo deshace entero.");

        // ---- Batch negativo: un paso con referencia rota revierte TODO (el disco queda intacto) ----
        var beforeBad = new ProjectStore(dir).Snapshot(new ProjectStore(dir).LoadOrCreate());
        var batchBad = ToolRegistry.Call("batch.apply", Args("""
        {"calls":[
          {"name":"variable.define","arguments":{"id":"flag.x","kind":"Flag","default":"false"}},
          {"name":"event.create","arguments":{"id":"event.roto","mapId":"map.inexistente","name":"Roto","kind":"Npc","x":1,"y":1}}
        ]}
        """), a);
        Expect(!batchBad.Ok, "batch con referencia rota debio fallar");
        var afterBad = new ProjectStore(dir).LoadOrCreate();
        Expect(!afterBad.Variables.Any(v => v.Id == "flag.x") && !afterBad.Events.Any(e => e.Id == "event.roto"), "el batch fallido dejo pasos aplicados (debia revertir entero)");
        sb.AppendLine($"Batch fallido OK: revertido entero ({batchBad.Error!.Code}), disco intacto.");

        // ---- Whitelist: undo/redo/lecturas no van en batch ----
        var notBatchable = ToolRegistry.Call("batch.apply", Args("""{"calls":[{"name":"transaction.undo","arguments":{}}]}"""), a);
        Expect(!notBatchable.Ok && notBatchable.Error!.Code == "not_batchable", "transaction.undo dentro de un batch debio dar not_batchable");
        sb.AppendLine("Whitelist OK: not_batchable para undo dentro de un batch.");

        // ---- Preview: mismo motor del batch, pero cero disco/revision/historial/undo ----
        var previewSession = new CommandSession(dir); // sesion limpia: permite comprobar que no ensucia undo
        var previewStore = new ProjectStore(dir);
        var previewBefore = previewStore.Snapshot(previewStore.LoadOrCreate());
        var previewRevision = previewStore.PeekRevision();
        var previewHistory = previewSession.History.Count;
        var previewArgs = Args("""
        {"calls":[
          {"name":"variable.define","arguments":{"id":"flag.preview","kind":"Flag","default":"false"}},
          {"name":"map.paint_rect","arguments":{"mapId":"map.m","x":1,"y":0,"width":1,"height":1,"tileId":1}}
        ]}
        """);
        var preview = ToolRegistry.Call("batch.preview", previewArgs, previewSession);
        Expect(preview.Ok && preview.Data is BatchPreviewReport, $"batch.preview fallo: {preview.Error?.Code} {preview.Error?.Message}");
        var previewReport = (BatchPreviewReport)preview.Data!;
        Expect(!previewReport.WouldWrite && previewReport.WouldChange && previewReport.BaseRevision == previewRevision,
            "el reporte de preview no describe correctamente escritura/base/cambios");
        Expect(previewReport.Diff.Changes.Any(x => x.Change == "added" && x.Kind == "variable" && x.Id == "flag.preview"),
            "el diff no reporto la variable agregada");
        Expect(previewReport.Diff.Changes.Any(x => x.Change == "modified" && x.Kind == "map" && x.Id == "map.m" && x.ChangedTileCells == 1),
            "el diff no cuantifico la celda de mapa cambiada");
        Expect(previewStore.Snapshot(previewStore.LoadOrCreate()) == previewBefore, "preview modifico project.json");
        Expect(previewStore.PeekRevision() == previewRevision, "preview incremento la revision");
        Expect(!previewSession.Project.Variables.Any(x => x.Id == "flag.preview"), "preview contamino el proyecto de la sesion");
        Expect(previewSession.History.Count == previewHistory, "preview escribio historial");
        var undoAfterPreview = previewSession.Undo();
        Expect(!undoAfterPreview.Ok && undoAfterPreview.Error!.Code == "nothing_to_undo", "preview contamino la pila de undo");

        // Una propuesta invalida tambien debe restaurar sin escribir.
        var invalidPreview = ToolRegistry.Call("batch.preview", Args("""
        {"calls":[{"name":"event.create","arguments":{"id":"event.preview_roto","mapId":"map.no","name":"Roto","kind":"Npc","x":0,"y":0}}]}
        """), previewSession);
        Expect(!invalidPreview.Ok, "preview invalido debio fallar la validacion global");
        Expect(previewStore.Snapshot(previewStore.LoadOrCreate()) == previewBefore && previewStore.PeekRevision() == previewRevision,
            "preview invalido escribio o cambio la revision");

        // Optimistic concurrency: entre aprobar y aplicar, otro co-autor cambia el disco.
        var external = new CommandSession(dir);
        Expect(external.Mutate(p => p.Variables.Add(new GameVariable { Id = "flag.external", Kind = VariableKind.Flag, Default = "false" })).Ok,
            "no se pudo simular el cambio externo");
        var guardedApplyArgs = (JsonObject)previewArgs.DeepClone();
        guardedApplyArgs["expectedRevision"] = previewReport.BaseRevision;
        var staleApply = ToolRegistry.Call("batch.apply", guardedApplyArgs, previewSession);
        Expect(!staleApply.Ok && staleApply.Error!.Code == "stale_preview", "una propuesta vieja se aplico sobre una revision nueva");
        var afterStale = previewStore.LoadOrCreate();
        Expect(afterStale.Variables.Any(x => x.Id == "flag.external") && !afterStale.Variables.Any(x => x.Id == "flag.preview"),
            "stale_preview no preservo el cambio externo o aplico la propuesta vieja");

        // Re-previsualizar sobre la base fresca permite aplicar; un undo revierte SOLO la propuesta.
        var freshPreview = ToolRegistry.Call("batch.preview", previewArgs, previewSession);
        Expect(freshPreview.Ok && freshPreview.Data is BatchPreviewReport, "no se pudo recalcular la propuesta sobre la revision fresca");
        var freshReport = (BatchPreviewReport)freshPreview.Data!;
        var freshApplyArgs = (JsonObject)previewArgs.DeepClone();
        freshApplyArgs["expectedRevision"] = freshReport.BaseRevision;
        var guardedApply = ToolRegistry.Call("batch.apply", freshApplyArgs, previewSession);
        Expect(guardedApply.Ok, $"batch.apply con revision fresca fallo: {guardedApply.Error?.Code}");
        var afterGuardedApply = previewStore.LoadOrCreate();
        Expect(afterGuardedApply.Variables.Any(x => x.Id == "flag.external") && afterGuardedApply.Variables.Any(x => x.Id == "flag.preview") && afterGuardedApply.Maps[0].Tiles[1] == 1,
            "la propuesta aprobada no se aplico completa");
        Expect(previewSession.Undo().Ok, "undo de la propuesta aprobada fallo");
        var afterPreviewUndo = previewStore.LoadOrCreate();
        Expect(afterPreviewUndo.Variables.Any(x => x.Id == "flag.external") && !afterPreviewUndo.Variables.Any(x => x.Id == "flag.preview") && afterPreviewUndo.Maps[0].Tiles[1] == 0,
            "un undo no revirtio solo la propuesta preservando el cambio externo");
        sb.AppendLine("Preview OK: diff semantico, cero efectos laterales, guardia stale_preview y apply/undo sobre la revision aprobada.");

        // ---- La revision del disco crece monotona ----
        var rev = new ProjectStore(dir).PeekRevision();
        Expect(rev > 0, "la revision del disco no crecio");
        sb.AppendLine($"Revision monotona OK: disco en revision {rev}.");

        // ---- Persistencia atomica: backup valido + recuperacion sin proyecto vacio ----
        var persistenceStore = new ProjectStore(dir);
        var beforeCorruption = persistenceStore.LoadOrCreate();
        persistenceStore.Save(beforeCorruption); // fuerza un backup completo del primario anterior
        var primaryPath = Path.Combine(dir, "project.json");
        var backupPath = primaryPath + ".bak";
        Expect(File.Exists(backupPath), "Save no creo project.json.bak");
        File.WriteAllText(primaryPath, "{ json truncado", Encoding.UTF8);
        var recoveringStore = new ProjectStore(dir);
        var recovered = recoveringStore.LoadOrCreate();
        Expect(recoveringStore.RecoveredFromBackup, "project.json corrupto no activo la recuperacion");
        Expect(recovered.Id == "game.smoke" && recovered.Maps.Any(m => m.Id == "map.m"), "el backup recuperado no corresponde al proyecto");
        Expect(JsonNode.Parse(File.ReadAllText(primaryPath, Encoding.UTF8)) is JsonObject, "la recuperacion no reparo project.json");
        Expect(Directory.GetFiles(dir, "project.json.tmp.*").Length == 0, "la escritura atomica dejo temporales de project.json");
        sb.AppendLine("Persistencia OK: project.json atomico, backup valido y recuperacion automatica tras corrupcion.");

        // ---- Pack atomico: assets embebidos sin mutar la fuente de verdad ----
        var assetPath = Path.Combine(dir, "asset-smoke.bin");
        File.WriteAllBytes(assetPath, [1, 3, 3, 7]);
        var packProject = recoveringStore.LoadOrCreate();
        packProject.Render.TitleImage = "asset-smoke.bin"; // solo en memoria; el proyecto del smoke no se reescribe
        var sourceEmbedded = packProject.EmbeddedFiles;
        var packPath = recoveringStore.BuildPack(packProject);
        var packed = ProjectStore.LoadPack(packPath);
        Expect(ReferenceEquals(packProject.EmbeddedFiles, sourceEmbedded) && sourceEmbedded.Count == 0, "BuildPack contamino EmbeddedFiles de la fuente");
        Expect(packed.EmbeddedFiles.TryGetValue("asset-smoke.bin", out var encoded) && Convert.FromBase64String(encoded).SequenceEqual(new byte[] { 1, 3, 3, 7 }), "el pack no embebio el asset esperado");
        Expect(Directory.GetFiles(Path.GetDirectoryName(packPath)!, "game.pack.tmp.*").Length == 0, "la construccion atomica dejo temporales del pack");
        if (OperatingSystem.IsWindows())
        {
            var lastGoodPack = File.ReadAllBytes(packPath);
            var failedWhileLocked = false;
            using (var locked = new FileStream(packPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                try { recoveringStore.BuildPack(packProject); }
                catch (IOException) { failedWhileLocked = true; }
                catch (UnauthorizedAccessException) { failedWhileLocked = true; }
            }
            Expect(failedWhileLocked, "BuildPack debio fallar mientras el destino estaba bloqueado sin permiso de reemplazo");
            Expect(File.ReadAllBytes(packPath).SequenceEqual(lastGoodPack), "un BuildPack fallido dano el ultimo pack valido");
            Expect(Directory.GetFiles(Path.GetDirectoryName(packPath)!, "game.pack.tmp.*").Length == 0, "un BuildPack fallido dejo temporales");
        }
        sb.AppendLine("Pack atomico OK: se publica completo, un fallo conserva el anterior y no muta project.json.");

        // Si primario Y backup estan rotos, debe fallar: crear un proyecto vacio esconderia una
        // perdida de contenido, que es el peor resultado posible para una herramienta de autoria.
        File.WriteAllText(primaryPath, "{ primario roto", Encoding.UTF8);
        File.WriteAllText(backupPath, "{ backup roto", Encoding.UTF8);
        var refusedEmptyProject = false;
        try { _ = new ProjectStore(dir).LoadOrCreate(); }
        catch (InvalidDataException) { refusedEmptyProject = true; }
        Expect(refusedEmptyProject, "dos archivos corruptos crearon o devolvieron un proyecto vacio");
        sb.Append("Doble corrupcion OK: falla de forma explicita y nunca reemplaza el trabajo por un proyecto vacio.");
        return sb.ToString();
    }

    static JsonObject Args(string json) => JsonNode.Parse(json)!.AsObject();
    static void Expect(bool ok, string error) { if (!ok) throw new InvalidOperationException(error); }
}
