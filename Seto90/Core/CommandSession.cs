namespace Seto90;

public sealed record ToolPayload(bool Ok, object? Data, ToolError? Error)
{
    public static ToolPayload Success(object data) => new(true, data, null);
    public static ToolPayload Fail(string code, string msg, string fix) => new(false, null, new ToolError(code, msg, fix));
}

public sealed record ToolError(string Code, string Message, string Fix);

/// <summary>
/// Capa de comandos compartida: TODA escritura al proyecto pasa por aca, venga del MCP (la IA),
/// de la CLI o del editor visual (el humano). Un solo undo/redo, una sola validacion con
/// reversion automatica, una sola verdad en disco.
///
/// Nota de diseno: en los 90 el editor interno del equipo y el juego final leian estructuras
/// distintas y desincronizarlas era un bug de produccion clasico. Aca no puede pasar: humano e
/// IA son clientes del mismo puñado de operaciones atomicas, asi que pintar un tile con el mouse
/// y pintarlo via MCP son literalmente la misma llamada.
/// </summary>
public sealed class CommandSession
{
    readonly ProjectStore store;
    readonly TransactionLog tx;
    readonly List<string> history = [];
    GameProject project;

    public CommandSession(string projectPath)
    {
        store = new ProjectStore(projectPath);
        project = store.LoadOrCreate();
        tx = new TransactionLog(store);
        if (store.RecoveredFromBackup) Note("sistema", store.RecoveryMessage!);
    }

    public GameProject Project => project;
    public string ProjectRoot => store.Root;
    public int DiskRevision => store.PeekRevision();

    /// <summary>Bitacora de la co-autoria: quien hizo que, en orden. El editor la muestra (HISTORIAL)
    /// para que el humano vea sus operaciones y las de la IA en la misma linea de tiempo.</summary>
    public IReadOnlyList<string> History => history;

    public void Note(string source, string text)
    {
        history.Add($"[{source}] {text}");
        if (history.Count > 60) history.RemoveAt(0);
    }

    /// <summary>Escritura transaccional: snapshot antes, validacion global despues; si algo rompe, se revierte solo.</summary>
    public ToolPayload Mutate(Action<GameProject> edit)
    {
        // Dentro de un batch, las escrituras se aplican solo en memoria: los estados intermedios
        // pueden ser legitimamente incompletos (referencias adelantadas); CommitBatch valida y
        // guarda una sola vez. Un paso que lanza lo reporta y el caller aborta el batch entero.
        if (batching)
        {
            try { edit(project); return ToolPayload.Success("ok"); }
            catch (Exception ex) { return ToolPayload.Fail("mutation_failed", ex.Message, "Revisar argumentos de la herramienta."); }
        }
        RebaseIfStale();
        tx.BeforeChange(project);
        try { edit(project); }
        catch (Exception ex) { tx.Rollback(ref project); return ToolPayload.Fail("mutation_failed", ex.Message, "Revisar argumentos de la herramienta."); }
        var v = ProjectValidator.Validate(project);
        if (!v.Ok) { tx.Rollback(ref project); return ToolPayload.Fail(v.Issues[0].Code, v.Issues[0].Message, v.Issues[0].Fix); }
        try { store.Save(project); }
        catch (Exception ex) { tx.Rollback(ref project); return ToolPayload.Fail("save_failed", $"No se pudo escribir project.json: {ex.Message}", "Reintentar la operacion (el archivo estaba ocupado)."); }
        return ToolPayload.Success("ok");
    }

    // ---- Modo batch (batch.apply del MCP): N escrituras, UNA validacion, UN guardado, UN undo ----

    bool batching;
    bool previewing;
    string? previewSessionSnapshot;
    string? previewBaseSnapshot;

    /// <summary>Abre un batch: snapshot UNA vez; las Mutate siguientes se aplican en memoria sin
    /// validar ni guardar, hasta CommitBatch (todo o nada). No se permite anidar.</summary>
    public ToolPayload BeginBatch()
    {
        if (batching) return ToolPayload.Fail("batch_nested", "Ya hay un batch abierto (no se permite anidar).", "Cerrar el batch actual antes de abrir otro.");
        RebaseIfStale(); // el commit escribira el proyecto ENTERO: arrancar desde el disco actual
        tx.BeforeChange(project);
        batching = true;
        return ToolPayload.Success("batch abierto");
    }

    /// <summary>Abre un batch DE PRUEBA sobre un clon de la revision mas fresca. No crea undo,
    /// no escribe disco y no anota historial; CompletePreviewBatch restaura la sesion exacta.</summary>
    public ToolPayload BeginPreviewBatch()
    {
        if (batching) return ToolPayload.Fail("batch_nested", "Ya hay un batch abierto (no se permite anidar).", "Cerrar el batch actual antes de previsualizar.");
        previewSessionSnapshot = store.Snapshot(project);
        var baseline = store.PeekRevision() > project.Revision ? store.LoadOrCreate() : project;
        previewBaseSnapshot = store.Snapshot(baseline);
        project = store.FromSnapshot(previewBaseSnapshot);
        batching = true;
        previewing = true;
        return ToolPayload.Success("preview abierto");
    }

    /// <summary>La guardia de la co-autoria entre procesos (la vieja regla operativa 7b): si el
    /// disco tiene una revision mayor que la nuestra, OTRO escritor (el editor en vivo, otra
    /// sesion MCP) guardo despues que nosotros. En vez de pisarlo con nuestra copia vieja, se
    /// adopta el disco (queda en el undo) y el cambio propio se aplica ENCIMA. Los edits son
    /// lambdas que buscan por id: re-aplicarlos sobre el proyecto fresco es seguro (si la
    /// entidad fue borrada afuera, First lanza y sale el error estructurado de siempre).</summary>
    void RebaseIfStale()
    {
        if (store.PeekRevision() <= project.Revision) return;
        tx.BeforeChange(project);
        project = store.LoadOrCreate();
        Note("ext", UiStrings.ExternalChangeRebased);
    }

    /// <summary>Cierra el batch: valida el proyecto ENTERO una vez y guarda una vez; si la
    /// validacion o el guardado fallan, revierte el batch completo (el disco queda intacto).</summary>
    public ToolPayload CommitBatch()
    {
        if (!batching) return ToolPayload.Fail("no_batch", "No hay batch abierto.", "batch.apply maneja BeginBatch/CommitBatch solo.");
        if (previewing) return ToolPayload.Fail("preview_open", "Hay una vista previa abierta; no se puede guardar.", "Finalizar con CompletePreviewBatch o AbortBatch.");
        batching = false;
        var v = ProjectValidator.Validate(project);
        if (!v.Ok) { tx.Rollback(ref project); return ToolPayload.Fail(v.Issues[0].Code, v.Issues[0].Message, v.Issues[0].Fix); }
        try { store.Save(project); }
        catch (Exception ex) { tx.Rollback(ref project); return ToolPayload.Fail("save_failed", $"No se pudo escribir project.json: {ex.Message}", "Reintentar la operacion (el archivo estaba ocupado)."); }
        return ToolPayload.Success("ok");
    }

    /// <summary>Valida y describe la propuesta; aun cuando falle o el diff lance, restaura en
    /// memoria el estado previo sin tocar revision, disco, undo/redo ni historial.</summary>
    public ToolPayload CompletePreviewBatch(int calls)
    {
        if (!batching || !previewing)
            return ToolPayload.Fail("no_preview", "No hay una vista previa abierta.", "batch.preview maneja su ciclo automaticamente.");
        ValidationResult validation;
        BatchPreviewReport? report = null;
        try
        {
            validation = ProjectValidator.Validate(project);
            if (validation.Ok)
                report = ProjectDiff.Compare(store.FromSnapshot(previewBaseSnapshot!), project, calls);
        }
        finally
        {
            RestorePreview();
        }
        if (!validation.Ok)
            return ToolPayload.Fail(validation.Issues[0].Code, validation.Issues[0].Message, validation.Issues[0].Fix);
        return ToolPayload.Success(report!);
    }

    /// <summary>Aborta el batch en curso y revierte todo lo aplicado en memoria.</summary>
    public void AbortBatch()
    {
        if (!batching) return;
        if (previewing) { RestorePreview(); return; }
        batching = false;
        tx.Rollback(ref project);
    }

    void RestorePreview()
    {
        project = store.FromSnapshot(previewSessionSnapshot!);
        batching = false;
        previewing = false;
        previewSessionSnapshot = null;
        previewBaseSnapshot = null;
    }

    /// <summary>Adopta un proyecto cargado fuera de la sesion (hot reload de otro proceso, ej. la IA
    /// escribiendo via MCP). Queda registrado en el undo: Ctrl+Z tambien deshace lo que hizo la IA.</summary>
    public void Adopt(GameProject fresh)
    {
        tx.BeforeChange(project);
        project = fresh;
    }

    /// <summary>true si el snapshot de other coincide con el estado actual (eco de nuestra propia escritura).</summary>
    public bool Matches(GameProject other) => store.Snapshot(project) == store.Snapshot(other);

    public ToolPayload Read(Func<GameProject, object> read) => ToolPayload.Success(read(project));
    public ToolPayload Validate() { var v = ProjectValidator.Validate(project); return v.Ok ? ToolPayload.Success(v.ToHumanText()) : ToolPayload.Fail("validation_failed", v.ToHumanText(), "Corregir referencias/datos."); }
    // Deshacer/rehacer escriben snapshots COMPLETOS: con un cambio externo sin adoptar, pisarian
    // el trabajo del otro co-autor. Se rechaza con error estructurado (adoptar primero).
    public ToolPayload Undo() => store.PeekRevision() > project.Revision
        ? ToolPayload.Fail("external_change", "Hay un cambio externo sin adoptar; deshacer ahora lo pisaria.", "Adoptarlo primero (el juego lo hace solo al volver al mundo; via MCP, cualquier escritura rebasea) y reintentar.")
        : tx.Undo(ref project) ? ToolPayload.Success("undo aplicado") : ToolPayload.Fail("nothing_to_undo", "No hay operaciones para deshacer.", "Ejecutar primero una escritura.");
    public ToolPayload Redo() => store.PeekRevision() > project.Revision
        ? ToolPayload.Fail("external_change", "Hay un cambio externo sin adoptar; rehacer ahora lo pisaria.", "Adoptarlo primero (el juego lo hace solo al volver al mundo; via MCP, cualquier escritura rebasea) y reintentar.")
        : tx.Redo(ref project) ? ToolPayload.Success("redo aplicado") : ToolPayload.Fail("nothing_to_redo", "No hay operaciones para rehacer.", "Ejecutar undo primero.");
    public ToolPayload Pack()
    {
        var v = ProjectValidator.Validate(project);
        if (!v.Ok) return ToolPayload.Fail("validation_failed", v.ToHumanText(), "Corregir antes de empaquetar.");
        var gate = QualityAudit.CheckForPack(project, store.Root);
        if (gate != null)
            return ToolPayload.Fail("quality_gate_failed", gate.ToHumanText(),
                "Resolver los warnings del director de calidad o desactivar enforceOnPack conscientemente.");
        return ToolPayload.Success(store.BuildPack(project));
    }
}
