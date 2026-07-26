using System.Globalization;
using System.Text;

namespace Seto90;

public sealed record StoryImportScene(string Id, string Title, string Prose, int Words);

public sealed record StoryImportChapter(string Id, string Title, List<StoryImportScene> Scenes);

public sealed record StoryImportSceneView(string Id, string Title, int Words);

public sealed record StoryImportChapterView(string Id, string Title, List<StoryImportSceneView> Scenes);

/// <summary>Informe sin la prosa (un manuscrito pesa megas): estructura, palabras y avisos.
/// Es el mismo objeto que devuelven el MCP y la CLI, para que ambos digan exactamente lo mismo.</summary>
public sealed record StoryImportSummary(
    bool DryRun,
    string Mode,
    int Chapters,
    int Scenes,
    int Words,
    List<StoryImportChapterView> Structure,
    List<string> Warnings,
    string Next);

public sealed record StoryImportReport(
    List<StoryImportChapter> Chapters,
    int Words,
    List<string> Warnings,
    bool DryRun,
    string Mode)
{
    public int ChapterCount => Chapters.Count;
    public int SceneCount => Chapters.Sum(x => x.Scenes.Count);

    public StoryImportSummary Summarize() => new(
        DryRun, Mode, ChapterCount, SceneCount, Words,
        [.. Chapters.Select(ch => new StoryImportChapterView(ch.Id, ch.Title,
            [.. ch.Scenes.Select(sc => new StoryImportSceneView(sc.Id, sc.Title, sc.Words))]))],
        Warnings,
        DryRun
            ? "Vista previa: nada se escribio. Repetir sin dryRun para incorporar el manuscrito."
            : "Las escenas entraron en estado draft y SIN links al juego. Leerlas con story.query y construir el contenido con las herramientas de siempre; despues enlazar con story.scene.set y cerrar con story.scene.sync.");
}

/// <summary>
/// La rampa de entrada del Libro Espejo: convierte un manuscrito o guion en capitulos y escenas
/// del proyecto. DEPOSITA PROSA Y NADA MAS.
///
/// Nota de diseno (regla explicita del motor): importar NO es una segunda forma de
/// escribir contenido. No crea mapas, eventos, dialogos ni links, no adivina gameplay por
/// heuristica y no toca nada existente. El texto entra como dato consultable (story.query) y el
/// juego lo sigue construyendo la IA por el mismo camino validado de siempre. Una heuristica que
/// inventara contenido seria un segundo pipeline, y el motor tiene una sola verdad.
///
/// El parseo es puro y determinista: mismo texto, mismos ids, mismas escenas.
/// </summary>
public static class StoryImporter
{
    /// <summary>Cortes de escena del oficio editorial, para manuscritos sin encabezados.</summary>
    static readonly HashSet<string> SceneBreaks = ["***", "* * *", "---", "___", "* * * *", "###"];

    public const int MaxCharacters = 5_000_000;

    /// <summary>
    /// Formato: '# titulo' abre capitulo, '## titulo' abre escena, el resto es prosa. Sin
    /// encabezados, el documento entero es un capitulo y los cortes de escena (***, ---) lo
    /// separan. Nunca falla por formato: un .txt plano entra como una sola escena.
    /// </summary>
    public static StoryImportReport Parse(
        string text,
        string defaultTitle,
        IEnumerable<string> takenChapterIds,
        IEnumerable<string> takenSceneIds,
        bool dryRun = false)
    {
        var chapterIds = new HashSet<string>(takenChapterIds, StringComparer.Ordinal);
        var sceneIds = new HashSet<string>(takenSceneIds, StringComparer.Ordinal);
        var warnings = new List<string>();
        var lines = StripFrontMatter((text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));
        var hasHeadings = lines.Any(x => HeadingLevel(x) > 0);

        var chapters = new List<StoryImportChapter>();
        var scenes = new List<(string Title, StringBuilder Body)>();
        var chapterTitle = defaultTitle;
        var chapterOpen = false;
        var pendingChapters = new List<(string Title, List<(string Title, StringBuilder Body)> Scenes)>();

        void CloseChapter()
        {
            if (scenes.Count > 0) pendingChapters.Add((chapterTitle, scenes));
            else if (chapterOpen) warnings.Add($"El capitulo '{chapterTitle}' no tenia texto debajo y se omitio.");
            scenes = [];
        }

        foreach (var line in lines)
        {
            var level = hasHeadings ? HeadingLevel(line) : 0;
            if (level == 1)
            {
                CloseChapter();
                chapterTitle = HeadingText(line, defaultTitle);
                chapterOpen = true;
                continue;
            }
            if (level == 2)
            {
                scenes.Add((HeadingText(line, $"Escena {scenes.Count + 1}"), new StringBuilder()));
                continue;
            }
            if (!hasHeadings && SceneBreaks.Contains(line.Trim()))
            {
                if (scenes.Count > 0) scenes.Add(("", new StringBuilder()));
                continue;
            }
            if (scenes.Count == 0)
            {
                // Prosa antes del primer '##': el capitulo arranca con una escena implicita.
                if (line.Trim().Length == 0) continue;
                scenes.Add(("", new StringBuilder()));
            }
            scenes[^1].Body.AppendLine(line);
        }
        CloseChapter();

        var totalWords = 0;
        var sceneNumber = 0;
        foreach (var (title, bodies) in pendingChapters)
        {
            var chapterSlug = Slug(title, $"capitulo_{chapters.Count + 1}");
            var chapterId = Unique($"chapter.{chapterSlug}", chapterIds, warnings, "un capitulo");
            var imported = new List<StoryImportScene>();
            foreach (var (sceneTitle, body) in bodies)
            {
                sceneNumber++;
                var prose = Tidy(body.ToString());
                var effectiveTitle = string.IsNullOrWhiteSpace(sceneTitle)
                    ? (bodies.Count == 1 ? title : $"{title} ({imported.Count + 1})")
                    : sceneTitle;
                var sceneSlug = Slug(effectiveTitle, $"escena_{sceneNumber}");
                var sceneId = Unique($"scene.{sceneSlug}", sceneIds, warnings, "una escena");
                var words = NarrativeTwin.WordCount(prose);
                totalWords += words;
                if (words == 0) warnings.Add($"{sceneId}: quedo sin prosa (encabezado sin texto debajo).");
                imported.Add(new StoryImportScene(sceneId, effectiveTitle, prose, words));
            }
            chapters.Add(new StoryImportChapter(chapterId, title, imported));
        }

        var mode = hasHeadings ? "encabezados" : "texto plano";
        if (!hasHeadings && chapters.Count > 0)
            warnings.Add("El documento no usa encabezados; entro como un capitulo. Marcar capitulos con '# titulo' y escenas con '## titulo' da un corte fino.");
        return new StoryImportReport(chapters, totalWords, warnings, dryRun, mode);
    }

    /// <summary>Vuelca lo parseado al Libro Espejo. Solo AGREGA: no reemplaza capitulos, escenas,
    /// links ni sincronizaciones existentes. Las escenas nacen 'draft' y sin enlaces al juego.</summary>
    public static void Apply(GameProject project, StoryImportReport report)
    {
        foreach (var chapter in report.Chapters)
        {
            project.StoryBook.Chapters.Add(new StoryChapterDef
            {
                Id = chapter.Id,
                Title = chapter.Title,
                Summary = "",
                Scenes = [.. chapter.Scenes.Select(scene => new StorySceneDef
                {
                    Id = scene.Id,
                    Title = scene.Title,
                    Status = "draft",
                    Prose = scene.Prose
                })]
            });
        }
    }

    public static IEnumerable<string> ChapterIds(GameProject project) => project.StoryBook.Chapters.Select(x => x.Id);
    public static IEnumerable<string> SceneIds(GameProject project) => project.StoryBook.Chapters.SelectMany(x => x.Scenes).Select(x => x.Id);

    // ---- Parseo ----

    static string[] StripFrontMatter(string[] lines)
    {
        var first = Array.FindIndex(lines, x => x.Trim().Length > 0);
        if (first < 0 || lines[first].Trim() != "---") return lines;
        var close = Array.FindIndex(lines, first + 1, x => x.Trim() is "---" or "...");
        return close < 0 ? lines : lines[(close + 1)..];
    }

    static int HeadingLevel(string line)
    {
        var trimmed = line.TrimStart();
        if (!trimmed.StartsWith('#')) return 0;
        var hashes = trimmed.TakeWhile(c => c == '#').Count();
        if (hashes > 2) return 0; // '###' y mas profundo es prosa (o corte de escena en texto plano)
        var rest = trimmed[hashes..];
        return rest.Length == 0 || char.IsWhiteSpace(rest[0]) ? hashes : 0;
    }

    static string HeadingText(string line, string fallback)
    {
        var text = line.TrimStart().TrimStart('#').Trim().TrimEnd('#').Trim();
        return text.Length == 0 ? fallback : text;
    }

    /// <summary>Normaliza el cuerpo: sin espacios al final de linea y sin huecos de 3+ lineas.</summary>
    static string Tidy(string body)
    {
        var lines = body.Replace("\r\n", "\n").Split('\n').Select(x => x.TrimEnd());
        var result = new StringBuilder();
        var blanks = 0;
        foreach (var line in lines)
        {
            if (line.Length == 0) { blanks++; continue; }
            if (result.Length > 0) result.Append(blanks > 0 ? "\n\n" : "\n");
            blanks = 0;
            result.Append(line);
        }
        return result.ToString().Trim();
    }

    /// <summary>Id legible y seguro: ASCII, minusculas, sin acentos. Los ids del Libro Espejo
    /// viajan a project.json, al DOCX y a los reportes; un id con tilde es una bomba de tiempo.</summary>
    static string Slug(string value, string fallback)
    {
        var normalized = (value ?? "").Normalize(NormalizationForm.FormD);
        var b = new StringBuilder();
        foreach (var c in normalized)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
            if (c is >= 'a' and <= 'z' or >= '0' and <= '9') b.Append(c);
            else if (c is >= 'A' and <= 'Z') b.Append(char.ToLowerInvariant(c));
            else if (b.Length > 0 && b[^1] != '_') b.Append('_');
        }
        var slug = b.ToString().Trim('_');
        if (slug.Length > 48) slug = slug[..48].TrimEnd('_');
        return slug.Length == 0 || !char.IsLetter(slug[0]) && !char.IsDigit(slug[0]) ? fallback : slug;
    }

    static string Unique(string candidate, HashSet<string> taken, List<string> warnings, string kind)
    {
        if (taken.Add(candidate)) return candidate;
        for (var n = 2; ; n++)
        {
            var id = $"{candidate}_{n}";
            if (!taken.Add(id)) continue;
            warnings.Add($"Ya existia {kind} con id '{candidate}'; el importado entro como '{id}'.");
            return id;
        }
    }
}
