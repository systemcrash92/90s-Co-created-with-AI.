using System.Text;

namespace Seto90;

public sealed class DialogueSmokeTest
{
    readonly GameProject project;

    public DialogueSmokeTest(GameProject project) => this.project = project;

    public string Run()
    {
        var flags = project.Variables.Where(v => v.Kind == VariableKind.Flag).ToDictionary(v => v.Id, v => v.Default.Equals("true", StringComparison.OrdinalIgnoreCase));
        var inventory = new List<string>();
        var sb = new StringBuilder();
        foreach (var dialogue in project.Dialogues)
        {
            sb.AppendLine($"Dialogo: {dialogue.Id}");
            Visit(dialogue, dialogue.StartNodeId, flags, inventory, sb, 0);
        }
        sb.AppendLine($"Flags: {string.Join(", ", flags.Select(x => x.Key + "=" + x.Value))}");
        sb.AppendLine($"Inventario simulado: {inventory.Count}");
        return sb.ToString();
    }

    void Visit(DialogueDef dialogue, string nodeId, Dictionary<string, bool> flags, List<string> inventory, StringBuilder sb, int depth)
    {
        if (depth > 8) { sb.AppendLine("  limite de profundidad alcanzado"); return; }
        var node = dialogue.Nodes.First(x => x.Id == nodeId);
        sb.AppendLine($"  {node.Speaker}: {node.Text}");
        foreach (var effect in node.Effects) Apply(effect, flags, inventory, sb);
        if (node.Choices.Count > 0)
        {
            foreach (var choice in node.Choices)
            {
                sb.AppendLine($"  opcion: {choice.Text} -> {choice.NextNodeId}");
                Visit(dialogue, choice.NextNodeId, flags, inventory, sb, depth + 1);
            }
            return;
        }
        if (!string.IsNullOrWhiteSpace(node.NextNodeId)) Visit(dialogue, node.NextNodeId, flags, inventory, sb, depth + 1);
    }

    void Apply(EventCommand command, Dictionary<string, bool> flags, List<string> inventory, StringBuilder sb)
    {
        if (command.Kind == CommandKind.SetVariable)
        {
            flags[command.TargetId] = command.Value.Equals("true", StringComparison.OrdinalIgnoreCase);
            sb.AppendLine($"    efecto flag {command.TargetId}={flags[command.TargetId]}");
        }
        else if (command.Kind == CommandKind.GiveItem)
        {
            var count = int.TryParse(command.Value, out var parsed) ? Math.Max(1, parsed) : 1;
            for (var i = 0; i < count; i++) inventory.Add(command.TargetId);
            sb.AppendLine($"    efecto item {command.TargetId} x{count}");
        }
    }
}
