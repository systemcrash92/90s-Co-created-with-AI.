namespace Seto90;

/// <summary>
/// Sesion de dialogo: navegacion del grafo de nodos + estado del typewriter.
///
/// Nota de diseno: el texto letra-por-letra de los 90 no era estetica, era memoria — el buffer
/// de tiles se llenaba de a un caracter por frame — pero definio el ritmo de lectura del genero.
/// Aca es una cuenta determinista en float (caracteres visibles acumulados por dt * velocidad),
/// sin timers globales: testeable en headless con dt fijo, y el runtime decide cuando sonar el blip.
/// </summary>
public sealed class DialogueSession
{
    readonly Dictionary<string, DialogueNode> nodes;
    List<string> pages = [""];
    int pageIndex;
    string prepared = "";      // texto de la PAGINA actual (sobre el que corre el typewriter)
    string lastWrapped = "";   // ultimo texto envuelto recibido (para no repaginar cada frame)
    float visibleChars;

    public DialogueNode Current { get; private set; }
    public int SelectedChoice { get; set; }

    public DialogueSession(DialogueDef dialogue)
    {
        nodes = dialogue.Nodes.ToDictionary(x => x.Id);
        Current = nodes[dialogue.StartNodeId];
    }

    public void SetNode(string nodeId)
    {
        Current = nodes[nodeId];
        SelectedChoice = 0;
        // Arranca con el texto crudo como pagina unica; el runtime lo re-pagina al envolverlo
        // por pixeles (Prepare). Asi el typewriter funciona aun sin pasar por el wrap (smoke).
        pages = [Current.Text];
        pageIndex = 0;
        prepared = Current.Text;
        lastWrapped = "";
        visibleChars = 0;
    }

    /// <summary>El runtime fija el texto ya envuelto (wrap por pixeles) y cuantas lineas entran
    /// en la caja; el texto se parte en PAGINAS de a maxLines para que un parrafo largo nunca
    /// desborde (Enter pasa de pagina).</summary>
    public void Prepare(string wrappedText, int maxLines)
    {
        if (wrappedText == lastWrapped) return;
        lastWrapped = wrappedText;
        pages = Paginate(wrappedText, Math.Max(1, maxLines));
        pageIndex = Math.Min(pageIndex, pages.Count - 1);
        prepared = pages[pageIndex];
        visibleChars = Math.Min(visibleChars, prepared.Length);
    }

    static List<string> Paginate(string wrapped, int maxLines)
    {
        var lines = wrapped.Split('\n');
        var result = new List<string>();
        for (var i = 0; i < lines.Length; i += maxLines)
            result.Add(string.Join('\n', lines.Skip(i).Take(maxLines)));
        return result.Count == 0 ? [""] : result;
    }

    /// <summary>Quedan mas paginas del mismo nodo por leer.</summary>
    public bool HasMorePages => pageIndex < pages.Count - 1;

    /// <summary>Pasa a la pagina siguiente (reinicia el typewriter). Solo si HasMorePages.</summary>
    public void NextPage()
    {
        if (!HasMorePages) return;
        pageIndex++;
        prepared = pages[pageIndex];
        visibleChars = 0;
    }

    public bool TextComplete => visibleChars >= prepared.Length;
    public int VisibleCount => (int)Math.Min(visibleChars, prepared.Length);
    public string VisibleText => prepared[..VisibleCount];

    /// <summary>Avanza el typewriter; devuelve cuantos caracteres imprimibles nuevos aparecieron (para el blip).</summary>
    public int UpdateTypewriter(float dt, float charsPerSecond)
    {
        if (TextComplete) return 0;
        var before = VisibleCount;
        visibleChars = Math.Min(prepared.Length, visibleChars + dt * MathF.Max(1f, charsPerSecond));
        var after = VisibleCount;
        var printable = 0;
        for (var i = before; i < after; i++) if (!char.IsWhiteSpace(prepared[i])) printable++;
        return printable;
    }

    /// <summary>Enter durante el tipeo: mostrar todo de golpe (cortesia obligatoria de la era).</summary>
    public void FastForward() => visibleChars = prepared.Length;
}
