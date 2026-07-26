using System.Text;

namespace Seto90;

/// <summary>
/// Smoke headless de la capa de UI: resolucion de tema (proyecto vs default del motor)
/// y typewriter determinista con dt fijo. Sin ventana, sin GPU.
/// </summary>
public sealed class UiSmokeTest(GameProject project)
{
    public string Run()
    {
        var sb = new StringBuilder();

        var resolved = UiTheme.Resolve(project);
        var fallback = UiTheme.Resolve(new GameProject());
        sb.AppendLine($"Tema activo: '{project.UiThemeId}' style={resolved.Style} cps={resolved.TextSpeedCps}; default del motor: style={fallback.Style} cps={fallback.TextSpeedCps}.");

        var dialogue = new DialogueDef
        {
            Id = "d.smoke",
            StartNodeId = "n",
            Nodes = [new DialogueNode { Id = "n", Speaker = "Test", Text = "Hola mundo del motor" }]
        };
        var session = new DialogueSession(dialogue);
        session.SetNode("n");

        var printable = session.UpdateTypewriter(0.25f, 40); // 0.25s * 40cps = 10 chars visibles
        if (session.VisibleCount != 10) throw new InvalidOperationException($"Typewriter no determinista: {session.VisibleCount} visibles, esperaba 10.");
        if (printable != 9) throw new InvalidOperationException($"Blips imprimibles: {printable}, esperaba 9 ('Hola mundo' sin el espacio).");
        if (session.TextComplete) throw new InvalidOperationException("El texto no deberia estar completo a mitad de tipeo.");

        session.FastForward();
        if (!session.TextComplete || session.VisibleText != "Hola mundo del motor") throw new InvalidOperationException("FastForward no completo el texto.");

        sb.Append("Typewriter OK: 10 chars visibles a 40cps en 0.25s, 9 blips imprimibles, fast-forward completo.");
        return sb.ToString();
    }
}
