namespace Seto90;

/// <summary>Metadatos y capitulos del Libro Espejo. La lista ordenada ES el orden del manuscrito.</summary>
public sealed class StoryBookDef
{
    public string Title { get; set; } = "";
    public string Subtitle { get; set; } = "";
    public string Author { get; set; } = "";
    public string ShortTitle { get; set; } = "";
    public string Language { get; set; } = "es";
    public string Contact { get; set; } = "";
    /// <summary>letter (mercado editorial anglosajon) o a4.</summary>
    public string PageSize { get; set; } = "letter";
    public string Description { get; set; } = "";
    public List<StoryChapterDef> Chapters { get; set; } = [];
}

public sealed class StoryChapterDef
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Summary { get; set; } = "";
    public List<StorySceneDef> Scenes { get; set; } = [];
}

/// <summary>Una unidad canonica con dos expresiones: Prose para el libro y Links/CanonChoices
/// para el juego. Los hashes guardan el ultimo punto que humano e IA declararon sincronizado.</summary>
public sealed class StorySceneDef
{
    public string Id { get; set; } = "";
    public string Title { get; set; } = "";
    public string Synopsis { get; set; } = "";
    public string Pov { get; set; } = "";
    public string Location { get; set; } = "";
    public string Time { get; set; } = "";
    public string Status { get; set; } = "draft";
    public string Prose { get; set; } = "";
    public List<string> Tags { get; set; } = [];
    public List<StoryLinkDef> Links { get; set; } = [];
    public List<StoryCanonChoiceDef> CanonChoices { get; set; } = [];
    public string SyncedGameHash { get; set; } = "";
    public string SyncedProseHash { get; set; } = "";
}

/// <summary>Enlace semantico a contenido jugable. Role es libre (source, setting, cast, canon...).</summary>
public sealed class StoryLinkDef
{
    public string Kind { get; set; } = "dialogue";
    public string Id { get; set; } = "";
    public string Role { get; set; } = "source";
}

/// <summary>Eleccion que el manuscrito considera canon cuando un dialogo del juego ramifica.</summary>
public sealed class StoryCanonChoiceDef
{
    public string DialogueId { get; set; } = "";
    public string NodeId { get; set; } = "";
    public int ChoiceIndex { get; set; }
}
