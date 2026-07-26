using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;

namespace Seto90;

/// <summary>Cambio semantico compacto: nunca devuelve matrices de tiles o entidades completas.
/// Fields dice que propiedades cambiarian; ChangedTileCells cuantifica el impacto espacial.</summary>
public sealed record ContentChange(
    string Change,
    string Kind,
    string Id,
    List<string> Fields,
    int ChangedTileCells = 0);

public sealed record ProjectDiffSummary(
    int Added,
    int Modified,
    int Removed,
    int ChangedTileCells,
    int TotalChanges);

public sealed class BatchPreviewReport
{
    public int BaseRevision { get; init; }
    public int Calls { get; init; }
    public bool WouldWrite => false;
    public bool WouldChange => Diff.Summary.TotalChanges > 0;
    public ProjectDiffReport Diff { get; init; } = new();
}

public sealed class ProjectDiffReport
{
    public ProjectDiffSummary Summary { get; init; } = new(0, 0, 0, 0, 0);
    public List<ContentChange> Changes { get; init; } = [];
}

/// <summary>Diff puro y estable para la co-autoria humano/IA. Compara por IDs y propiedades
/// superiores: suficiente para aprobar una propuesta sin inundar el contexto con project.json.</summary>
public static class ProjectDiff
{
    static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        Converters = { new JsonStringEnumConverter() }
    };

    static readonly HashSet<string> ContentFields =
    [
        "revision", "variables", "tilesets", "maps", "events", "dialogues", "actors",
        "items", "enemies", "battles", "shops", "skills", "songs", "sprites",
        "uiThemes", "sfx", "vfx", "fonts", "qualityPlan", "storyBook", "embeddedFiles"
    ];

    public static BatchPreviewReport Compare(GameProject before, GameProject after, int calls)
    {
        var changes = new List<ContentChange>();
        var projectFields = ChangedFields(ProjectHeader(before), ProjectHeader(after));
        if (projectFields.Count > 0)
            changes.Add(new("modified", "project", before.Id, projectFields));

        var bookFields = ChangedFields(StoryBookHeader(before.StoryBook), StoryBookHeader(after.StoryBook));
        if (bookFields.Count > 0) changes.Add(new("modified", "storybook", "book", bookFields));
        Compare("storychapter", before.StoryBook.Chapters, after.StoryBook.Chapters, x => x.Id, changes, view: ChapterHeader);
        Compare("storyscene", before.StoryBook.Chapters.SelectMany(x => x.Scenes), after.StoryBook.Chapters.SelectMany(x => x.Scenes), x => x.Id, changes);
        var qualityFields = ChangedFields(QualityPlanHeader(before.QualityPlan), QualityPlanHeader(after.QualityPlan));
        if (qualityFields.Count > 0) changes.Add(new("modified", "qualityplan", "quality", qualityFields));
        Compare("qualityroute", before.QualityPlan.Routes, after.QualityPlan.Routes, x => x.Id, changes);
        Compare("qualityencounter", before.QualityPlan.Encounters, after.QualityPlan.Encounters, x => x.BattleId, changes);

        Compare("variable", before.Variables, after.Variables, x => x.Id, changes);
        Compare("tileset", before.Tilesets, after.Tilesets, x => x.Id, changes);
        Compare("map", before.Maps, after.Maps, x => x.Id, changes, ChangedMapCells);
        Compare("event", before.Events, after.Events, x => x.Id, changes);
        Compare("dialogue", before.Dialogues, after.Dialogues, x => x.Id, changes);
        Compare("actor", before.Actors, after.Actors, x => x.Id, changes);
        Compare("item", before.Items, after.Items, x => x.Id, changes);
        Compare("enemy", before.Enemies, after.Enemies, x => x.Id, changes);
        Compare("battle", before.Battles, after.Battles, x => x.Id, changes);
        Compare("shop", before.Shops, after.Shops, x => x.Id, changes);
        Compare("skill", before.Skills, after.Skills, x => x.Id, changes);
        Compare("song", before.Songs, after.Songs, x => x.Id, changes);
        Compare("sprite", before.Sprites, after.Sprites, x => x.Id, changes);
        Compare("uitheme", before.UiThemes, after.UiThemes, x => x.Id, changes);
        Compare("sfx", before.Sfx, after.Sfx, x => x.Id, changes);
        Compare("vfx", before.Vfx, after.Vfx, x => x.Id, changes);
        Compare("font", before.Fonts, after.Fonts, x => x.Id, changes);

        changes = changes
            .OrderBy(x => x.Kind == "project" ? 0 : 1)
            .ThenBy(x => x.Kind, StringComparer.Ordinal)
            .ThenBy(x => x.Id, StringComparer.Ordinal)
            .ThenBy(x => x.Change, StringComparer.Ordinal)
            .ToList();
        var summary = new ProjectDiffSummary(
            changes.Count(x => x.Change == "added"),
            changes.Count(x => x.Change == "modified"),
            changes.Count(x => x.Change == "removed"),
            changes.Sum(x => x.ChangedTileCells),
            changes.Count);
        return new BatchPreviewReport
        {
            BaseRevision = before.Revision,
            Calls = calls,
            Diff = new ProjectDiffReport { Summary = summary, Changes = changes }
        };
    }

    static void Compare<T>(
        string kind,
        IEnumerable<T> before,
        IEnumerable<T> after,
        Func<T, string> id,
        List<ContentChange> changes,
        Func<T, T, int>? changedCells = null,
        Func<T, JsonObject>? view = null)
    {
        var a = ById(before, id);
        var b = ById(after, id);
        foreach (var removed in a.Keys.Except(b.Keys, StringComparer.Ordinal))
            changes.Add(new("removed", kind, removed, []));
        foreach (var added in b.Keys.Except(a.Keys, StringComparer.Ordinal))
            changes.Add(new("added", kind, added, []));
        foreach (var same in a.Keys.Intersect(b.Keys, StringComparer.Ordinal))
        {
            var fields = ChangedFields(view?.Invoke(a[same]) ?? ToObject(a[same]), view?.Invoke(b[same]) ?? ToObject(b[same]));
            if (fields.Count > 0)
                changes.Add(new("modified", kind, same, fields, changedCells?.Invoke(a[same], b[same]) ?? 0));
        }
    }

    static JsonObject ProjectHeader(GameProject p)
    {
        var json = ToObject(p);
        foreach (var field in ContentFields) json.Remove(field);
        return json;
    }

    static JsonObject StoryBookHeader(StoryBookDef book)
    {
        var json = ToObject(book);
        json.Remove("chapters");
        return json;
    }

    static JsonObject QualityPlanHeader(QualityPlanDef plan)
    {
        var json = ToObject(plan);
        json.Remove("routes");
        json.Remove("encounters");
        return json;
    }

    static JsonObject ChapterHeader(StoryChapterDef chapter)
    {
        var json = ToObject(chapter);
        json.Remove("scenes");
        return json;
    }

    static List<string> ChangedFields(JsonObject before, JsonObject after) => before.Select(x => x.Key)
        .Union(after.Select(x => x.Key), StringComparer.Ordinal)
        .Where(key => !JsonNode.DeepEquals(before[key], after[key]))
        .OrderBy(key => key, StringComparer.Ordinal)
        .ToList();

    static JsonObject ToObject<T>(T value) => JsonSerializer.SerializeToNode(value, JsonOptions)?.AsObject()
        ?? throw new InvalidOperationException("No se pudo serializar una entidad para la vista previa.");

    static Dictionary<string, T> ById<T>(IEnumerable<T> values, Func<T, string> id) => values
        .GroupBy(id, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.Last(), StringComparer.Ordinal);

    static int ChangedMapCells(MapDef before, MapDef after)
    {
        // tileFlags es una extension retrocompatible en desarrollo: leerla del JSON mantiene
        // este diff desacoplado de la version del modelo y aun asi cuenta cambios de orientacion.
        var beforeFlags = ToObject(before)["tileFlags"] as JsonArray;
        var afterFlags = ToObject(after)["tileFlags"] as JsonArray;
        var count = Math.Max(Math.Max(before.Tiles.Count, after.Tiles.Count), Math.Max(beforeFlags?.Count ?? 0, afterFlags?.Count ?? 0));
        var changed = 0;
        for (var i = 0; i < count; i++)
        {
            var oldTile = i < before.Tiles.Count ? before.Tiles[i] : int.MinValue;
            var newTile = i < after.Tiles.Count ? after.Tiles[i] : int.MinValue;
            var oldFlags = FlagAt(beforeFlags, i);
            var newFlags = FlagAt(afterFlags, i);
            if (oldTile != newTile || oldFlags != newFlags) changed++;
        }
        return changed;
    }

    static int FlagAt(JsonArray? flags, int index) => flags is not null && index < flags.Count
        ? flags[index]?.GetValue<int>() ?? 0
        : 0;
}
