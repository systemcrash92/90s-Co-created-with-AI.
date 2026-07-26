using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Seto90;

public sealed class ProjectStore
{
    static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase, ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true, Converters = { new JsonStringEnumConverter() } };
    static readonly UTF8Encoding Utf8NoBom = new(false);
    const int IoAttempts = 12;

    public string Root { get; }
    public bool RecoveredFromBackup { get; private set; }
    public string? RecoveryMessage { get; private set; }

    public ProjectStore(string root) => Root = Path.GetFullPath(root);

    /// <summary>Carga la fuente de verdad. Un archivo existente pero invalido JAMAS se convierte
    /// silenciosamente en un proyecto vacio: se recupera desde .bak y se repara el primario; si
    /// ambos estan corruptos, falla con un diagnostico que conserva las dos causas.</summary>
    public GameProject LoadOrCreate()
    {
        Directory.CreateDirectory(Root);
        var path = Path.Combine(Root, "project.json");
        var backup = path + ".bak";

        if (!File.Exists(path))
        {
            if (File.Exists(backup)) return RecoverFromBackup(path, backup, new FileNotFoundException("Falta project.json.", path));
            var created = new GameProject();
            Save(created);
            return created;
        }

        try
        {
            var project = DeserializeProject(ReadShared(path), path);
            lastSavedRevision = project.Revision;
            return project;
        }
        catch (InvalidDataException primaryError) when (File.Exists(backup))
        {
            return RecoverFromBackup(path, backup, primaryError);
        }
    }

    /// <summary>Co-autoria: el MCP (otro proceso) y el runtime (hot reload) tocan project.json a la
    /// vez. Cada escritor termina un temporal propio y reemplaza el primario de una vez; los lectores
    /// comparten lectura/borrado para poder terminar sobre la version exacta que abrieron.</summary>
    // Ultima revision que ESTA instancia escribio: los snapshots de undo/redo traen revisiones
    // viejas, y la revision en disco debe crecer monotona (max + 1) para que la guardia funcione.
    int lastSavedRevision;

    public void Save(GameProject project)
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(Path.Combine(Root, "build"));
        var path = Path.Combine(Root, "project.json");
        project.Revision = Math.Max(project.Revision, lastSavedRevision) + 1;
        lastSavedRevision = project.Revision;
        WriteTextAtomic(path, Snapshot(project), path + ".bak");
    }

    GameProject RecoverFromBackup(string primaryPath, string backupPath, Exception primaryError)
    {
        try
        {
            var json = ReadShared(backupPath);
            var recovered = DeserializeProject(json, backupPath);
            // Repara el primario SIN reemplazar el backup bueno por el archivo corrupto.
            WriteTextAtomic(primaryPath, json, backupPath: null);
            lastSavedRevision = recovered.Revision;
            RecoveredFromBackup = true;
            RecoveryMessage = "project.json estaba ausente o corrupto; se restauro desde project.json.bak.";
            return recovered;
        }
        catch (Exception backupError) when (backupError is InvalidDataException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidDataException(
                "No se pudo cargar project.json ni su backup. No se creo un proyecto vacio para evitar perdida de datos.",
                new AggregateException(primaryError, backupError));
        }
    }

    static GameProject DeserializeProject(string json, string source)
    {
        try
        {
            return JsonSerializer.Deserialize<GameProject>(json, JsonOptions)
                   ?? throw new InvalidDataException($"{source} no contiene un proyecto.");
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"{source} contiene JSON invalido.", ex);
        }
    }

    static string ReadShared(string path)
    {
        IOException? last = null;
        for (var attempt = 0; attempt < IoAttempts; attempt++)
        {
            try
            {
                // FileShare.Delete permite que un escritor reemplace el archivo atomicamente sin
                // invalidar esta lectura: este handle termina de leer la version que abrio.
                using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(stream, Encoding.UTF8);
                return reader.ReadToEnd();
            }
            catch (IOException ex) { last = ex; Thread.Sleep(25); }
        }
        throw last!;
    }
    /// <summary>Lee SOLO la revision actual del disco (-1 si no hay archivo o no parsea): el
    /// chequeo barato que hace Mutate antes de escribir para detectar un escritor externo.</summary>
    public int PeekRevision()
    {
        var path = Path.Combine(Root, "project.json");
        if (!File.Exists(path)) return -1;
        try
        {
            using var doc = JsonDocument.Parse(ReadShared(path));
            return doc.RootElement.TryGetProperty("revision", out var r) && r.TryGetInt32(out var v) ? v : -1;
        }
        catch { return -1; }
    }

    public string Snapshot(GameProject project) => JsonSerializer.Serialize(project, JsonOptions);
    public GameProject FromSnapshot(string json) => JsonSerializer.Deserialize<GameProject>(json, JsonOptions) ?? throw new InvalidOperationException("Snapshot invalido.");
    /// <summary>El pack sigue siendo UN solo archivo: los PNG referenciados viajan adentro como base64 (EmbeddedFiles), que en project.json queda siempre vacio.</summary>
    public string BuildPack(GameProject project)
    {
        var build = Path.Combine(Root, "build"); Directory.CreateDirectory(build); var pack = Path.Combine(build, "game.pack");
        var embedded = new Dictionary<string, string>();
        foreach (var image in project.Sprites.Select(s => s.Image).Concat(project.Fonts.Select(f => f.Image)).Concat(project.Tilesets.Select(t => t.Image)).Append(project.Render.TitleImage).Where(i => !string.IsNullOrWhiteSpace(i)).Distinct())
        {
            var path = Path.Combine(Root, image);
            if (File.Exists(path)) embedded[image] = Convert.ToBase64String(File.ReadAllBytes(path));
        }
        var previous = project.EmbeddedFiles;
        project.EmbeddedFiles = embedded;
        try { WriteGzipAtomic(pack, Snapshot(project)); }
        finally { project.EmbeddedFiles = previous; }
        return pack;
    }
    public static GameProject LoadPack(string packPath) { using var file = File.OpenRead(packPath); using var gzip = new GZipStream(file, CompressionMode.Decompress); using var reader = new StreamReader(gzip, Encoding.UTF8); return JsonSerializer.Deserialize<GameProject>(reader.ReadToEnd(), JsonOptions) ?? throw new InvalidOperationException("Pack invalido."); }

    /// <summary>Escribe al lado del destino, fuerza los bytes a disco y recien entonces reemplaza
    /// el archivo final. El nombre temporal es unico por proceso/operacion: dos MCP simultaneos no
    /// comparten un .tmp. Si se pide backup, contiene siempre el primario completo anterior.</summary>
    static void WriteTextAtomic(string path, string text, string? backupPath)
    {
        var temp = TempPath(path);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.WriteThrough))
            using (var writer = new StreamWriter(stream, Utf8NoBom))
            {
                writer.Write(text);
                writer.Flush();
                stream.Flush(true);
            }
            CommitTemp(temp, path, backupPath);
        }
        finally { TryDelete(temp); }
    }

    /// <summary>Construye el gzip completo en un temporal. Un pack fallido deja intacto el ultimo
    /// game.pack valido, en vez de truncarlo al abrir el destino.</summary>
    static void WriteGzipAtomic(string path, string text)
    {
        var temp = TempPath(path);
        try
        {
            using (var stream = new FileStream(temp, FileMode.CreateNew, FileAccess.Write, FileShare.None, 16 * 1024, FileOptions.WriteThrough))
            {
                using (var gzip = new GZipStream(stream, CompressionLevel.SmallestSize, leaveOpen: true))
                {
                    var bytes = Utf8NoBom.GetBytes(text);
                    gzip.Write(bytes);
                }
                stream.Flush(true);
            }
            CommitTemp(temp, path, backupPath: null);
        }
        finally { TryDelete(temp); }
    }

    static void CommitTemp(string temp, string path, string? backupPath)
    {
        IOException? last = null;
        for (var attempt = 0; attempt < IoAttempts; attempt++)
        {
            try
            {
                if (backupPath != null && File.Exists(path)) File.Copy(path, backupPath, overwrite: true);
                File.Move(temp, path, overwrite: true); // mismo directorio/volumen: reemplazo atomico
                return;
            }
            catch (IOException ex) { last = ex; Thread.Sleep(25); }
        }
        throw last!;
    }

    static string TempPath(string path) => $"{path}.tmp.{Environment.ProcessId}.{Guid.NewGuid():N}";

    static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { /* el error original es mas util; el temporal unico no bloquea futuros guardados */ }
    }
}
