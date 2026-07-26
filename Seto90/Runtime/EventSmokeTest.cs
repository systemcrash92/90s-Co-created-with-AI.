using System.Text;

namespace Seto90;

public sealed class EventSmokeTest
{
    readonly GameProject project;

    public EventSmokeTest(GameProject project) => this.project = project;

    public string Run()
    {
        var flags = project.Variables.Where(v => v.Kind == VariableKind.Flag).ToDictionary(v => v.Id, v => v.Default.Equals("true", StringComparison.OrdinalIgnoreCase));
        var sb = new StringBuilder();
        sb.AppendLine("Eventos activos con flags iniciales:");
        Report(flags, sb);
        foreach (var battle in project.Battles.Where(b => !string.IsNullOrWhiteSpace(b.VictoryFlag))) flags[battle.VictoryFlag] = true;
        sb.AppendLine("Eventos activos tras simular victorias:");
        Report(flags, sb);
        sb.Append(DirectedMoveSmoke());
        return sb.ToString();
    }

    /// <summary>Recorre una caminata dirigida de cutscene con un GridMover real (sin raylib):
    /// pasos, giros puros, paso bloqueado que se saltea, y espera parseada. Numeros exactos.</summary>
    static string DirectedMoveSmoke()
    {
        var sb = new StringBuilder();
        var mover = new GridMover { SecondsPerTile = 0.1f };
        mover.Teleport(2, 2);
        var steps = new Queue<string>(CutsceneSteps.Parse("right,right,down,face:up,left"));
        if (steps.Count != 5) return "FALLO cutscene: parse esperaba 5 pasos.\n";
        // (4,2) esta bloqueada: el segundo 'right' se saltea y el resto sigue (nunca congelar la cutscene).
        bool CanOccupy(int x, int y) => (x, y) != (4, 2);
        var guard = 0;
        while ((steps.Count > 0 || mover.Moving) && guard++ < 1000)
        {
            mover.Update(1f / 60f);
            while (!mover.Moving && steps.Count > 0)
            {
                var (dx, dy, facing, faceOnly) = CutsceneSteps.Decode(steps.Dequeue());
                if (faceOnly) mover.Facing = facing;
                else mover.TryStep(dx, dy, CanOccupy);
            }
        }
        // right (3,2) + right bloqueado (se saltea) + down (3,3) + face:up + left (2,3)
        if (mover.TileX != 2 || mover.TileY != 3) return $"FALLO cutscene: termino en ({mover.TileX},{mover.TileY}), esperaba (2,3).\n";
        if (mover.Facing != Facing.Left) return $"FALLO cutscene: facing final {mover.Facing}, esperaba Left (el ultimo paso pisa el face:up).\n";
        if (!CutsceneSteps.TryParseWait("0.8", out var wait) || Math.Abs(wait - 0.8f) > 0.001f) return "FALLO cutscene: Wait '0.8' no parsea.\n";
        if (CutsceneSteps.TryParseWait("11", out _) || CutsceneSteps.TryParseWait("abc", out _)) return "FALLO cutscene: Wait invalido aceptado.\n";
        return "cutscene: pasos=OK bloqueado-se-saltea=OK face=OK wait=OK (termina en (2,3) mirando Left)\n";
    }

    void Report(Dictionary<string, bool> flags, StringBuilder sb)
    {
        foreach (var ev in project.Events.OrderBy(e => e.Id))
        {
            var page = SelectActivePage(ev, flags);
            var commands = page == null ? "sin pagina" : string.Join(",", page.Commands.Select(c => c.Kind + ":" + c.TargetId));
            sb.AppendLine($"- {ev.Id} [{ev.Kind}] page={page?.Id ?? "none"} commands={commands}");
        }
    }

    static EventPage? SelectActivePage(EventDef ev, Dictionary<string, bool> flags)
    {
        for (var i = ev.Pages.Count - 1; i >= 0; i--)
        {
            var page = ev.Pages[i];
            if (page.Conditions.All(c => Matches(c, flags))) return page;
        }
        return ev.Pages.FirstOrDefault();
    }

    static bool Matches(ConditionDef condition, Dictionary<string, bool> flags)
    {
        var actual = flags.TryGetValue(condition.VariableId, out var value) && value;
        var expected = condition.EqualsValue.Equals("true", StringComparison.OrdinalIgnoreCase);
        return actual == expected;
    }
}
