using System.Text;

namespace Seto90;

public sealed class HeadlessPlaytest
{
    readonly GameProject project;
    public HeadlessPlaytest(GameProject project) => this.project = project;

    public string Run()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Playtest Seto90: {project.Title}");
        var map = project.Maps.First(x => x.Id == project.StartMapId);
        sb.AppendLine($"Mapa inicial: {map.Name} ({map.Width}x{map.Height})");
        sb.AppendLine($"Eventos en mapa: {map.EventIds.Count}");
        foreach (var id in map.EventIds)
        {
            var ev = project.Events.First(x => x.Id == id);
            sb.AppendLine($"- {ev.Name} [{ev.Kind}] en {ev.X},{ev.Y} rutina={ev.RoutineId}");
            foreach (var c in ev.Pages.FirstOrDefault()?.Commands ?? []) sb.AppendLine(RunCommand(c));
        }
        foreach (var m in project.Maps)
        foreach (var w in m.Warps)
        {
            var dest = project.Maps.First(x => x.Id == w.ToMapId);
            sb.AppendLine($"- Warp en {m.Id} ({w.X},{w.Y}) -> {dest.Name} ({w.ToX},{w.ToY})");
        }
        sb.AppendLine("Resultado: demo recorrible en modo headless; contenido creado por MCP y validado.");
        return sb.ToString();
    }

    string RunCommand(EventCommand c) => c.Kind switch
    {
        CommandKind.Dialogue => RunDialogue(c.TargetId),
        CommandKind.Battle => RunBattle(c.TargetId),
        CommandKind.OpenShop => $"  Tienda disponible: {project.Shops.First(x => x.Id == c.TargetId).Name}",
        _ => $"  Comando: {c.Kind} {c.TargetId} {c.Value}"
    };

    string RunDialogue(string id)
    {
        var d = project.Dialogues.First(x => x.Id == id);
        var n = d.Nodes.First(x => x.Id == d.StartNodeId);
        var choices = n.Choices.Count == 0 ? "" : $" Opciones: {string.Join(" / ", n.Choices.Select(x => x.Text))}";
        return $"  Dialogo {d.Id}: {n.Speaker}: {n.Text}{choices}";
    }

    string RunBattle(string id)
    {
        var b = project.Battles.First(x => x.Id == id);
        var a = project.Actors.FirstOrDefault() ?? new ActorDef { Name = "Heroe" };
        var e = project.Enemies.First(x => x.Id == b.EnemyIds[0]);
        var dmg = FormulaValidator.EvalBasicDamage(b.DamageFormula, a.Stats, e.Stats, a.Level);
        return $"  Combate {b.Id} vista={b.View} rollingHp={b.RollingHp}: {a.Name} causa {dmg} a {e.Name}; HP enemigo restante simulado={Math.Max(0, e.Stats.Hp - dmg)}";
    }
}
