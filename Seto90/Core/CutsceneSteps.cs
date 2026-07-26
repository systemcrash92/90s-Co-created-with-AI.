using System.Globalization;

namespace Seto90;

/// <summary>
/// Pasos declarativos de cutscene ("up,up,face:left"): el guion describe el movimiento como
/// datos y el runtime lo ejecuta tile a tile. Logica pura compartida entre el validador
/// (rechaza pasos desconocidos al escribir) y el runtime (los reproduce); sin raylib, el
/// event-smoke la recorre con un GridMover de verdad.
/// </summary>
public static class CutsceneSteps
{
    public static readonly string[] Valid = ["up", "down", "left", "right", "face:up", "face:down", "face:left", "face:right"];

    public static List<string> Parse(string value) =>
        [.. value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(s => s.ToLowerInvariant())];

    public static bool IsValid(string step) => Valid.Contains(step);

    /// <summary>Descompone un paso: delta de caminata o giro puro (faceOnly).</summary>
    public static (int Dx, int Dy, Facing Facing, bool FaceOnly) Decode(string step)
    {
        var faceOnly = step.StartsWith("face:", StringComparison.Ordinal);
        var dir = faceOnly ? step[5..] : step;
        return dir switch
        {
            "up" => (0, -1, Facing.Up, faceOnly),
            "down" => (0, 1, Facing.Down, faceOnly),
            "left" => (-1, 0, Facing.Left, faceOnly),
            _ => (1, 0, Facing.Right, faceOnly),
        };
    }

    /// <summary>Parsea los segundos de un Wait ("0.8"); invariante de cultura, como todo el JSON.</summary>
    public static bool TryParseWait(string value, out float seconds) =>
        float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds is > 0 and <= 10;

    /// <summary>Iconos de globo de emote (la mimica clasica del genero): idioma-
    /// independientes, se leen en un decimo de segundo y no interrumpen el flujo.</summary>
    public static readonly string[] EmoteIcons = ["!", "?", "zzz", "nota", "puntos", "corazon"];

    /// <summary>Parsea el value de ShowEmote: "icono" o "icono:segundos" (ej "zzz:6", "!").
    /// Default 2 segundos. Rango propio 0 < s <= 60 (NO el de Wait, que es <= 10): un emote
    /// de estado —el Zzz de sueno que acompana una caminata— dura mucho mas que una pausa
    /// de cutscene. Compartido entre validador y runtime.</summary>
    public static bool TryParseEmote(string value, out string icon, out float seconds)
    {
        seconds = 2f;
        var parts = (value ?? "").Split(':', 2, StringSplitOptions.TrimEntries);
        icon = parts[0].ToLowerInvariant();
        if (!EmoteIcons.Contains(icon)) return false;
        return parts.Length == 1
            || (float.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out seconds) && seconds is > 0 and <= 60);
    }
}
