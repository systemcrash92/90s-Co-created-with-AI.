using System.Text.Json;
using System.Text.Json.Nodes;

namespace Seto90;

/// <summary>
/// Suite de EVALS del juego: corre todos los guiones de `<proyecto>/evals/*.txt` con
/// playtest.run y devuelve un veredicto unico. Es la regresion de GAMEPLAY, distinta de los
/// smokes: los smokes verifican que el MOTOR funciona (combate, dialogo, saves, audio); los
/// evals verifican que ESTE JUEGO sigue siendo jugable despues de cambiarle contenido.
///
/// Nota de diseno: la maquinaria ya existia (PlaytestDriver corre un guion determinista con
/// asserts); lo que faltaba era el arnes que acumula muchos guiones y los corre juntos. Un
/// capitulo terminado deja su eval; a partir de ahi, cualquier cambio que lo rompa se ve solo.
/// Cada guion corre sobre un proyecto RECIEN cargado del disco: un eval no contamina al
/// siguiente.
/// </summary>
public static class EvalSuite
{
    public static string DirectoryFor(string projectRoot) => Path.Combine(projectRoot, "evals");

    /// <summary>Guiones de la suite, en orden alfabetico (determinista: mismo orden siempre).</summary>
    public static List<string> Scripts(string projectRoot)
    {
        var dir = DirectoryFor(projectRoot);
        if (!Directory.Exists(dir)) return [];
        return [.. Directory.GetFiles(dir, "*.txt").OrderBy(f => f, StringComparer.Ordinal)];
    }

    /// <summary>Corre la suite. `only` (opcional) filtra por nombre de eval, sin extension.
    /// El reporte es compacto cuando todo pasa y detallado donde falla: un eval verde no
    /// necesita 200 lineas de pasos, uno rojo necesita el primer paso fallido y el estado.</summary>
    public static JsonObject Run(string projectRoot, string? only = null)
    {
        var scripts = Scripts(projectRoot);
        if (only is { Length: > 0 })
            scripts = [.. scripts.Where(f => Path.GetFileNameWithoutExtension(f).Equals(only, StringComparison.OrdinalIgnoreCase))];

        var results = new JsonArray();
        int passed = 0, failed = 0;
        foreach (var file in scripts)
        {
            var name = Path.GetFileNameWithoutExtension(file);
            // Proyecto fresco por eval: el runtime no arrastra estado del anterior.
            var project = new ProjectStore(projectRoot).LoadOrCreate();
            var validation = ProjectValidator.Validate(project);
            if (!validation.Ok)
            {
                failed++;
                results.Add(new JsonObject { ["name"] = name, ["ok"] = false, ["error"] = "El proyecto no valida: " + validation.ToHumanText() });
                continue;
            }

            JsonNode? report;
            try
            {
                var runtime = new VisualRuntime(project, projectRoot);
                runtime.DebugRunScript(File.ReadAllLines(file));
                runtime.Run(30000, null, hidden: true);
                report = JsonNode.Parse(runtime.BuildScriptReport());
            }
            catch (Exception ex)
            {
                failed++;
                results.Add(new JsonObject { ["name"] = name, ["ok"] = false, ["error"] = ex.Message });
                continue;
            }

            var ok = report?["ok"]?.GetValue<bool>() == true;
            var entry = new JsonObject
            {
                ["name"] = name,
                ["ok"] = ok,
                ["frames"] = report?["frames"]?.GetValue<int>() ?? 0,
                ["steps"] = (report?["steps"] as JsonArray)?.Count ?? 0,
            };
            if (ok) passed++;
            else
            {
                failed++;
                // Solo en el fallo: el primer paso que fallo y el estado final, que es lo que
                // se necesita para corregir sin volver a correr nada.
                var steps = report?["steps"] as JsonArray;
                var firstBad = steps?.FirstOrDefault(x => x?["ok"]?.GetValue<bool>() == false);
                entry["failedStep"] = firstBad?.DeepClone();
                entry["state"] = report?["state"]?.DeepClone();
            }
            results.Add(entry);
        }

        return new JsonObject
        {
            ["ok"] = failed == 0,
            ["total"] = scripts.Count,
            ["passed"] = passed,
            ["failed"] = failed,
            ["evals"] = results,
        };
    }

    public static string ToJson(JsonObject report) =>
        report.ToJsonString(new JsonSerializerOptions { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

    /// <summary>Resumen legible para la CLI (una linea por eval, el detalle solo si fallo).</summary>
    public static string ToHumanText(JsonObject report, string projectRoot)
    {
        var total = report["total"]?.GetValue<int>() ?? 0;
        if (total == 0)
            return $"Sin evals: no hay guiones en {DirectoryFor(projectRoot)}.\n" +
                   "Un eval es un guion de playtest.run con asserts (un paso por linea, # comenta).\n" +
                   "Escribir uno por capitulo terminado: a partir de ahi, cualquier cambio que lo rompa se ve solo.";

        var b = new System.Text.StringBuilder();
        foreach (var e in report["evals"] as JsonArray ?? [])
        {
            var ok = e?["ok"]?.GetValue<bool>() == true;
            b.AppendLine($"  {(ok ? "OK   " : "FALLO")} {e?["name"]}{(ok ? $"  ({e?["frames"]} frames, {e?["steps"]} pasos)" : "")}");
            if (ok) continue;
            if (e?["error"] is { } err) b.AppendLine($"         {err}");
            if (e?["failedStep"] is { } bad)
                b.AppendLine($"         paso fallido: {bad["step"]}  ->  {bad["detail"]}");
        }
        var passed = report["passed"]?.GetValue<int>() ?? 0;
        var failed = report["failed"]?.GetValue<int>() ?? 0;
        b.Append(failed == 0
            ? $"Evals: {passed}/{total} OK."
            : $"Evals: {passed}/{total} OK, {failed} FALLARON.");
        return b.ToString();
    }
}
