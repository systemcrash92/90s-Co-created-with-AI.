using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace Seto90;

public sealed record StorySyncState(
    string SceneId,
    bool InSync,
    bool GameChanged,
    bool BookChanged,
    bool HasGameLinks,
    List<string> MissingLinks,
    string CurrentGameHash,
    string CurrentProseHash);

public sealed record StorySourceView(string Kind, string Id, string Role, object? Content);

/// <summary>El puente entre juego y libro. No intenta convertir prosa en gameplay por heuristica:
/// expone ambas fuentes a la IA, fija la ruta canon y detecta deriva en los dos sentidos.</summary>
public static class NarrativeTwin
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };
    static readonly Regex WordRegex = new(@"[\p{L}\p{N}]+(?:['’\-][\p{L}\p{N}]+)*", RegexOptions.Compiled);
    static readonly HashSet<string> SceneStatuses = ["draft", "revised", "final"];

    public static IEnumerable<(StoryChapterDef Chapter, StorySceneDef Scene)> Scenes(GameProject p) =>
        p.StoryBook.Chapters.SelectMany(ch => ch.Scenes.Select(scene => (ch, scene)));

    public static StorySceneDef? FindScene(GameProject p, string sceneId) =>
        Scenes(p).Select(x => x.Scene).FirstOrDefault(x => x.Id == sceneId);

    public static int WordCount(string prose) => WordRegex.Matches(prose ?? "").Count;
    public static int WordCount(StoryBookDef book) => book.Chapters.SelectMany(x => x.Scenes).Sum(x => WordCount(x.Prose));

    public static StorySyncState State(GameProject p, StorySceneDef scene)
    {
        var missing = scene.Links.Where(link => ResolveLink(p, link) is null)
            .Select(link => $"{link.Kind}:{link.Id}").Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList();
        var game = GameHash(p, scene);
        var prose = ProseHash(scene);
        var gameChanged = !scene.SyncedGameHash.Equals(game, StringComparison.Ordinal);
        var bookChanged = !scene.SyncedProseHash.Equals(prose, StringComparison.Ordinal);
        var hasLinks = scene.Links.Count > 0;
        return new(scene.Id, hasLinks && missing.Count == 0 && !gameChanged && !bookChanged,
            gameChanged, bookChanged, hasLinks, missing, game, prose);
    }

    public static void Sync(GameProject p, StorySceneDef scene)
    {
        var state = State(p, scene);
        if (!state.HasGameLinks) throw new InvalidOperationException("La escena no tiene links al juego; enlazarla antes de sincronizar.");
        if (state.MissingLinks.Count > 0) throw new InvalidOperationException("Links inexistentes: " + string.Join(", ", state.MissingLinks));
        scene.SyncedGameHash = state.CurrentGameHash;
        scene.SyncedProseHash = state.CurrentProseHash;
    }

    public static string GameHash(GameProject p, StorySceneDef scene)
    {
        var b = new StringBuilder();
        foreach (var link in scene.Links.OrderBy(x => x.Kind, StringComparer.Ordinal).ThenBy(x => x.Id, StringComparer.Ordinal).ThenBy(x => x.Role, StringComparer.Ordinal))
        {
            b.Append(link.Kind).Append(':').Append(link.Id).Append(':').Append(link.Role).Append('\n');
            var source = ResolveLink(p, link);
            b.Append(source is null ? "<missing>" : JsonSerializer.Serialize(source, source.GetType(), JsonOptions)).Append('\n');
        }
        b.Append("canon:").Append(JsonSerializer.Serialize(scene.CanonChoices, JsonOptions));
        return Hash(b.ToString());
    }

    public static string ProseHash(StorySceneDef scene) => Hash((scene.Prose ?? "").Replace("\r\n", "\n").Trim());

    public static List<StorySourceView> Sources(GameProject p, StorySceneDef scene) => scene.Links
        .Select(link => new StorySourceView(link.Kind, link.Id, link.Role, ResolveLink(p, link))).ToList();

    public static object? ResolveLink(GameProject p, StoryLinkDef link) => link.Kind.Trim().ToLowerInvariant() switch
    {
        "map" => p.Maps.FirstOrDefault(x => x.Id == link.Id),
        "event" => p.Events.FirstOrDefault(x => x.Id == link.Id),
        "dialogue" => p.Dialogues.FirstOrDefault(x => x.Id == link.Id),
        "battle" => p.Battles.FirstOrDefault(x => x.Id == link.Id),
        "actor" => p.Actors.FirstOrDefault(x => x.Id == link.Id),
        "item" => p.Items.FirstOrDefault(x => x.Id == link.Id),
        "enemy" => p.Enemies.FirstOrDefault(x => x.Id == link.Id),
        "shop" => p.Shops.FirstOrDefault(x => x.Id == link.Id),
        "song" => p.Songs.FirstOrDefault(x => x.Id == link.Id),
        _ => null
    };

    public static List<string> ExportWarnings(GameProject p)
    {
        var warnings = new List<string>();
        if (string.IsNullOrWhiteSpace(p.StoryBook.Title)) warnings.Add("Falta el titulo del libro.");
        if (string.IsNullOrWhiteSpace(p.StoryBook.Author)) warnings.Add("Falta el autor del libro.");
        if (p.StoryBook.Chapters.Count == 0) warnings.Add("El libro no tiene capitulos.");
        foreach (var (chapter, scene) in Scenes(p))
        {
            if (string.IsNullOrWhiteSpace(scene.Prose)) warnings.Add($"{chapter.Id}/{scene.Id}: no tiene prosa.");
            var state = State(p, scene);
            if (!state.InSync) warnings.Add($"{chapter.Id}/{scene.Id}: juego y libro no estan sincronizados.");
        }
        return warnings;
    }

    /// <summary>Validacion estructural bloqueante. Prosa vacia/deriva son estados de produccion,
    /// no corrupcion: aparecen en story.query/export strict y en el editor, no rompen el juego.</summary>
    public static void Validate(GameProject p, ValidationResult r)
    {
        var chapters = p.StoryBook.Chapters;
        Unique(chapters.Select(x => x.Id), "capitulo", r);
        Unique(chapters.SelectMany(x => x.Scenes).Select(x => x.Id), "escena", r);
        if (p.StoryBook.PageSize is not ("letter" or "a4"))
            r.Issues.Add(new("bad_book_page_size", $"Libro Espejo usa pageSize '{p.StoryBook.PageSize}'.", "Usar letter o a4."));
        foreach (var chapter in chapters)
        {
            ValidId(chapter.Id, "capitulo", r);
            foreach (var scene in chapter.Scenes)
            {
                ValidId(scene.Id, "escena", r);
                if (!SceneStatuses.Contains(scene.Status)) r.Issues.Add(new("bad_story_status", $"Escena {scene.Id} usa status '{scene.Status}'.", "Usar draft, revised o final."));
                foreach (var link in scene.Links)
                {
                    if (ResolveLink(p, link) is null) r.Issues.Add(new("missing_story_link", $"Escena {scene.Id} enlaza {link.Kind}:{link.Id}, que no existe.", "Crear el contenido, corregir el link o quitarlo."));
                }
                foreach (var canon in scene.CanonChoices)
                {
                    var dialogue = p.Dialogues.FirstOrDefault(x => x.Id == canon.DialogueId);
                    var node = dialogue?.Nodes.FirstOrDefault(x => x.Id == canon.NodeId);
                    if (node is null || canon.ChoiceIndex < 0 || canon.ChoiceIndex >= node.Choices.Count)
                        r.Issues.Add(new("bad_canon_choice", $"Escena {scene.Id}: eleccion canon {canon.DialogueId}/{canon.NodeId}[{canon.ChoiceIndex}] no existe.", "Corregir dialogueId/nodeId/choiceIndex."));
                }
                if (!HashOrEmpty(scene.SyncedGameHash) || !HashOrEmpty(scene.SyncedProseHash))
                    r.Issues.Add(new("bad_story_hash", $"Escena {scene.Id} tiene una huella de sincronizacion invalida.", "Vaciarla y ejecutar story.scene.sync."));
            }
        }
    }

    static void Unique(IEnumerable<string> ids, string kind, ValidationResult r)
    {
        foreach (var duplicate in ids.Where(x => !string.IsNullOrWhiteSpace(x)).GroupBy(x => x, StringComparer.Ordinal).Where(g => g.Count() > 1))
            r.Issues.Add(new("duplicate_story_id", $"Id de {kind} duplicado: {duplicate.Key}.", "Usar ids unicos en todo el Libro Espejo."));
    }

    static void ValidId(string id, string kind, ValidationResult r)
    {
        if (string.IsNullOrWhiteSpace(id) || !char.IsLetter(id[0]) || id.Any(c => !char.IsLetterOrDigit(c) && c is not ('.' or '_' or '-')))
            r.Issues.Add(new("bad_story_id", $"Id de {kind} invalido: '{id}'.", "Empezar con letra y usar letras, numeros, punto, guion o guion bajo."));
    }

    static bool HashOrEmpty(string value) => string.IsNullOrEmpty(value) || value.Length == 64 && value.All(Uri.IsHexDigit);
    static string Hash(string value) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value))).ToLowerInvariant();
}
