using System.Text;

namespace Seto90;

/// <summary>
/// Smoke headless de la fuente embebida: cobertura de caracteres, integridad de los datos
/// y metricas/wrap funcionando sin ventana ni GPU (solo el lado CPU de PixelFont).
/// </summary>
public sealed class FontSmokeTest
{
    public string Run()
    {
        var font = PixelFont.Embedded();
        var sb = new StringBuilder();

        var missing = new List<char>();
        for (var c = ' '; c <= '~'; c++) if (!font.Has(c)) missing.Add(c);
        foreach (var c in "áéíóúñüÁÉÍÓÚÑÜ¡¿") if (!font.Has(c)) missing.Add(c);
        if (missing.Count > 0) throw new InvalidOperationException($"Glifos faltantes: {string.Join(",", missing)}");

        var sample = "¡Hola, ñandú! El reloj de la plaza camina solo... ¿Nos ayudás?";
        var width = font.Measure(sample);
        if (width <= 0) throw new InvalidOperationException("Measure devolvio 0 para un texto no vacio.");

        var wrapped = font.WrapPixels(sample, 120);
        var lines = wrapped.Split('\n');
        foreach (var line in lines)
        {
            if (font.Measure(line) > 120) throw new InvalidOperationException($"Linea excede el ancho pedido: '{line}'");
        }

        sb.AppendLine($"Fuente OK: {95 + 16} glifos requeridos presentes.");
        sb.AppendLine($"Measure('{sample[..12]}...') = {width}px; wrap a 120px = {lines.Length} lineas.");
        sb.Append("Metricas y wrap calculados 100% en CPU (sin ventana).");
        return sb.ToString();
    }
}
