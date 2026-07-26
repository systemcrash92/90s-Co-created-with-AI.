namespace Seto90;

/// <summary>
/// Evaluacion PURA de los VFX declarativos: posiciones de particulas, offsets de distorsion y
/// ciclos de color como funcion del tiempo, sin raylib y sin estado. La comparte el validador,
/// el VfxRenderer del runtime y vfx-smoke (que la verifica headless, como MapOps con map-smoke).
///
/// Nota de diseno: los fondos de batalla de la epoca eran exactamente esto — tablas de
/// oscilacion sinusoidal por scanline, palette cycling y scroll parametrizados en la ROM.
/// VfxDef es esa tabla, legible y validada. El "azar" de las particulas es un hash determinista
/// del indice: mismo efecto en cualquier maquina, capturable en fase exacta como los tiles animados.
/// </summary>
public static class VfxEval
{
    public static readonly HashSet<string> Kinds = ["impact", "background", "weather"];
    public static readonly HashSet<string> WeatherShapes = ["rain", "snow", "fog", "flash", "splash"];
    public static readonly HashSet<string> Shapes = ["flash", "spark", "ring", "slash", "beam"];
    public static readonly HashSet<string> Motions = ["burst", "rise", "fall", "spiral", "expand"];
    public static readonly HashSet<string> Patterns = ["bands", "checker", "rings", "waves"];
    public static readonly HashSet<string> Blends = ["additive", "normal", "multiply"];

    /// <summary>Azar determinista 0..1 por (indice de particula, sal): xorshift entero, sin RNG.</summary>
    public static double Hash01(int index, int salt)
    {
        unchecked
        {
            var h = (uint)(index * 374761393 + salt * 668265263);
            h = (h ^ (h >> 13)) * 1274126177u;
            h ^= h >> 16;
            return h / (double)uint.MaxValue;
        }
    }

    /// <summary>Progreso 0..1 de una capa dentro de su ventana [StartMs, EndMs] (EndMs 0 = hasta
    /// el final del efecto); -1 si la capa no esta activa en tMs.</summary>
    public static float LayerProgress(VfxLayer l, int tMs, int durationMs)
    {
        var end = l.EndMs > 0 ? l.EndMs : durationMs;
        if (end <= l.StartMs || tMs < l.StartMs || tMs >= end) return -1f;
        return (tMs - l.StartMs) / (float)(end - l.StartMs);
    }

    /// <summary>Particula i de una capa spark: posicion RELATIVA al origen del efecto y alfa 0..1.
    /// Cada particula tiene direccion y ritmo propios (hash del indice). burst se achata levemente
    /// en Y (la perspectiva frontal de un combate JRPG); rise/fall suben/caen sobre el blanco.</summary>
    public static (float X, float Y, float Alpha) Particle(VfxLayer l, int i, float progress)
    {
        var angle = (float)(Hash01(i, 1) * Math.Tau);
        var pace = 0.55f + 0.45f * (float)Hash01(i, 2);
        var dist = progress * pace * l.SpreadPx;
        var lateral = ((float)Hash01(i, 3) - 0.5f) * l.SpreadPx;
        var alpha = 1f - progress;
        return l.Motion switch
        {
            "rise" => (lateral, -dist, alpha),
            "fall" => (lateral, -(1f - progress) * pace * l.SpreadPx, alpha),
            "spiral" => (MathF.Cos(angle + progress * 6f) * dist, MathF.Sin(angle + progress * 6f) * dist * 0.6f, alpha),
            "expand" => (MathF.Cos(angle) * progress * l.SpreadPx, MathF.Sin(angle) * progress * l.SpreadPx * 0.8f, alpha),
            _ => (MathF.Cos(angle) * dist, MathF.Sin(angle) * dist * 0.8f, alpha), // burst
        };
    }

    /// <summary>Offset horizontal del scanline y de un fondo en el segundo t: la tecnica del
    /// battle swirl (y del HDMA de SNES). 0 si la capa no distorsiona.</summary>
    public static float ScanlineOffset(VfxLayer l, int line, float tSeconds)
        => l.DistortAmp <= 0 ? 0f : MathF.Sin(line * (float)l.DistortFreq + tSeconds * (float)l.DistortSpeed) * (float)l.DistortAmp;

    /// <summary>Indice dentro de Colors de la banda/celda n en el milisegundo t, con el palette
    /// cycling de CycleMs (0 = estatico). Siempre 0..Colors.Count-1, tambien con n negativo (scroll).</summary>
    public static int ColorIndex(VfxLayer l, int band, int tMs)
    {
        var n = Math.Max(1, l.Colors.Count);
        var cycle = l.CycleMs >= 50 ? tMs / l.CycleMs : 0;
        return (int)((((long)band + cycle) % n + n) % n);
    }

    /// <summary>Alto de banda/lado de celda efectivo (SizePx 0 = auto por kind).</summary>
    public static int CellSize(VfxLayer l, bool background) => l.SizePx > 0 ? l.SizePx : background ? 16 : 2;

    /// <summary>Gota/copo i de una capa de clima en el segundo t, en coordenadas de PANTALLA
    /// (envuelve por ambos ejes: el cielo es infinito). rain cae rapido y ladeado por Angle
    /// (grados desde la vertical); snow cae lento y ondula. ScrollY pisa la velocidad de
    /// caida (0 = default por shape), ScrollX es viento lateral extra.</summary>
    public static (float X, float Y, float Alpha) WeatherDrop(VfxLayer l, int i, float t, int width, int height, int camX = 0, int camY = 0)
    {
        var pace = 0.7f + 0.6f * (float)Hash01(i, 11);
        var fall = (l.ScrollY > 0 ? (float)l.ScrollY : l.Shape == "snow" ? 26f : 150f) * pace;
        var slant = MathF.Tan((float)(l.Angle * Math.PI / 180.0));
        // Anclaje calibrado probando en vivo: a lo ANCHO pleno al mundo (mover la
        // camara de costado se siente bien); a lo ALTO la nieve NO se acopla a la camara
        // (factor 0): cae tan lento que cualquier acople vertical se percibe — se aceleraba
        // al subir o retrocedia al bajar. Un campo que cae uniforme no delata anclaje
        // vertical: con 0 no puede pasar, por construccion. La lluvia cae rapido y aguanta
        // el anclaje pleno en ambos ejes.
        var ky = l.Shape == "snow" ? 0f : 1f;
        var span = height + 24f;
        var y = (((float)(Hash01(i, 12) * span) + t * fall - camY * ky) % span + span) % span - 12f;
        var sway = l.Shape == "snow" ? MathF.Sin(t * (1f + (float)Hash01(i, 14)) + i) * 6f : 0f;
        var x = ((((float)(Hash01(i, 13) * width)) + y * slant + (float)l.ScrollX * t + sway - camX) % width + width) % width;
        return (x, y, 0.5f + 0.5f * (float)Hash01(i, 15));
    }

    /// <summary>Banco de niebla i: centro, radio y respiracion de alfa, derivando con el viento
    /// (ScrollX px/seg, default suave). SpreadPx = radio del banco (default 70).</summary>
    public static (float X, float Y, float R, float Alpha) FogBank(VfxLayer l, int i, float t, int width, int height, int camX = 0, int camY = 0)
    {
        var r = l.SpreadPx <= 1 ? 70f : l.SpreadPx;
        var speed = l.ScrollX != 0 ? (float)l.ScrollX : 9f;
        var span = width + r * 2f;
        var x = ((((float)(Hash01(i, 21) * span)) + t * speed * (0.6f + 0.8f * (float)Hash01(i, 22)) - camX) % span + span) % span - r;
        var y = ((((float)(Hash01(i, 23) * height)) - camY) % height + height) % height + MathF.Sin(t * 0.4f + i * 2.1f) * 8f;
        var alpha = 0.55f + 0.35f * MathF.Sin(t * 0.5f + i * 1.7f);
        return (x, y, r, MathF.Max(0.15f, alpha));
    }

    /// <summary>Fase de una capa de clima dentro del CICLO del efecto (durationMs > 0 =
    /// largo del ciclo; 0 = clima permanente, siempre 1). La capa vive en su ventana
    /// [startMs, endMs] del ciclo, con rampas suaves de entrada/salida (~2s): la lluvia
    /// LLEGA y ESCAMPA, no aparece de golpe. 0 = capa dormida en este momento del ciclo.</summary>
    public static float WeatherPhase(VfxLayer l, int tMs, int durationMs)
    {
        if (durationMs <= 0) return 1f;
        var t = tMs % durationMs;
        var start = l.StartMs;
        var end = l.EndMs > 0 ? l.EndMs : durationMs;
        if (t < start || t >= end) return 0f;
        var ramp = MathF.Min(2000f, (end - start) / 4f);
        return MathF.Min(Math.Clamp((t - start) / ramp, 0f, 1f), Math.Clamp((end - t) / ramp, 0f, 1f));
    }

    /// <summary>Salpicadura i contra el piso: nace donde cae una gota (reubicada por hash en
    /// cada ciclo corto propio), se abre y muere. Devuelve centro, apertura 0..1 y alfa.</summary>
    public static (float X, float Y, float Grow, float Alpha) Splash(VfxLayer l, int i, float t, int width, int height, int camX = 0, int camY = 0)
    {
        var cycle = 0.45f + 0.3f * (float)Hash01(i, 31);
        var k = (int)(t / cycle);
        var phase = t / cycle - k;
        var x = ((((float)(Hash01(i * 131 + k, 33) * width)) - camX) % width + width) % width;
        var y = ((((float)(Hash01(i * 131 + k, 35) * height)) - camY) % height + height) % height;
        return (x, y, phase, 1f - phase);
    }

    /// <summary>Relampago de una capa flash de clima: alfa del pantallazo en el ms t del ciclo
    /// (CycleMs >= 1000). Doble destello clasico: golpe, apagon, eco. 0 = sin relampago.</summary>
    public static float LightningAlpha(VfxLayer l, int tMs)
    {
        if (l.CycleMs < 1000) return 0f;
        var phase = tMs % l.CycleMs;
        return phase < 90 ? 1f : phase < 170 ? 0f : phase < 260 ? 0.55f : 0f;
    }

    /// <summary>Resuelve un VfxDef por id: primero el proyecto (puede pisar los reservados),
    /// despues los defaults embebidos del motor. null si no existe.</summary>
    public static VfxDef? Find(GameProject project, string id) =>
        project.Vfx.FirstOrDefault(v => v.Id == id) ?? DefaultAssets.DefaultVfx().FirstOrDefault(v => v.Id == id);
}
