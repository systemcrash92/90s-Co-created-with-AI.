namespace Seto90;

/// <summary>
/// Recarga en caliente de project.json: un FileSystemWatcher marca el archivo como sucio y el
/// runtime consume la recarga solo cuando el juego esta en estado "mundo" (sin dialogo, combate,
/// cola de comandos ni transicion). Si el JSON nuevo no valida, se ignora y se avisa: el juego
/// corriendo nunca adopta contenido roto — la misma garantia que da el MCP al escribir.
/// </summary>
public sealed class HotReload : IDisposable
{
    readonly string root;
    readonly FileSystemWatcher? watcher;
    volatile bool dirty;

    public HotReload(string root)
    {
        this.root = root;
        try
        {
            watcher = new FileSystemWatcher(root, "project.json") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            watcher.Changed += (_, _) => dirty = true;
            watcher.Created += (_, _) => dirty = true;
            watcher.Renamed += (_, _) => dirty = true;
            watcher.EnableRaisingEvents = true;
        }
        catch
        {
            watcher = null; // sin watcher no hay hot reload, pero el juego sigue
        }
    }

    /// <summary>Si hubo cambios, intenta cargar y validar. Devuelve el proyecto fresco o un error humano.</summary>
    public bool TryConsume(out GameProject? fresh, out string? error)
    {
        fresh = null;
        error = null;
        if (!dirty) return false;
        dirty = false;
        try
        {
            var candidate = new ProjectStore(root).LoadOrCreate();
            var validation = ProjectValidator.Validate(candidate);
            if (!validation.Ok)
            {
                error = "Hot reload rechazado: " + validation.Issues[0].Message;
                return true;
            }
            fresh = candidate;
            return true;
        }
        catch (Exception ex)
        {
            error = "Hot reload fallo: " + ex.Message;
            return true;
        }
    }

    public void Dispose() => watcher?.Dispose();
}
