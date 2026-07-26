namespace Seto90;

public static class FormulaValidator
{
    static readonly HashSet<string> Allowed = new(StringComparer.OrdinalIgnoreCase) { "attack", "defense", "speed", "level", "hp", "mp", "max", "min" };
    public static void Validate(string formula, ValidationResult r, string owner)
    {
        if (string.IsNullOrWhiteSpace(formula)) { r.Issues.Add(new("empty_formula", $"Formula de {owner} vacia.", "Usar max(1, attack - defense).")); return; }
        foreach (var ch in formula) if (!char.IsLetterOrDigit(ch) && " +-*/(),_".IndexOf(ch) < 0) { r.Issues.Add(new("unsafe_formula", $"Formula de {owner} contiene '{ch}'.", "Usar operadores aritmeticos seguros.")); return; }
        foreach (var w in formula.Split([' ', '+', '-', '*', '/', '(', ')', ','], StringSplitOptions.RemoveEmptyEntries).Where(x => x.Any(char.IsLetter))) if (!Allowed.Contains(w)) r.Issues.Add(new("unknown_formula_symbol", $"Formula de {owner} usa '{w}'.", "Usar attack, defense, speed, level, hp, mp, min o max."));
    }
    public static int EvalBasicDamage(string formula, StatBlock a, StatBlock d, int level)
    {
        var text = formula.Replace("attack", a.Attack.ToString(), StringComparison.OrdinalIgnoreCase).Replace("defense", d.Defense.ToString(), StringComparison.OrdinalIgnoreCase).Replace("speed", a.Speed.ToString(), StringComparison.OrdinalIgnoreCase).Replace("level", level.ToString(), StringComparison.OrdinalIgnoreCase);
        if (text.Contains("max", StringComparison.OrdinalIgnoreCase)) { var inner = text[(text.IndexOf('(') + 1)..text.LastIndexOf(')')]; var parts = inner.Split(',', StringSplitOptions.TrimEntries); return Math.Max(Parse(parts[0]), Parse(parts[1])); }
        return Math.Max(1, Parse(text));
    }
    static int Parse(string text) { var t = text.Split(' ', StringSplitOptions.RemoveEmptyEntries); if (t.Length == 1) return int.Parse(t[0]); var acc = int.Parse(t[0]); for (var i = 1; i < t.Length - 1; i += 2) { var rhs = int.Parse(t[i + 1]); acc = t[i] switch { "+" => acc + rhs, "-" => acc - rhs, "*" => acc * rhs, "/" => rhs == 0 ? acc : acc / rhs, _ => acc }; } return acc; }
}
