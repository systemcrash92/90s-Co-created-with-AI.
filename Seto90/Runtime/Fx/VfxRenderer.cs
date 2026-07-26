using Raylib_cs;

namespace Seto90;

/// <summary>
/// Dibuja los VFX declarativos (VfxDef) con primitivas raylib: cero PNGs, cero shaders.
/// Sin estado propio — la matematica vive en VfxEval (pura, testeada por vfx-smoke) y los
/// timers viven en VisualRuntime como los emotes; aca solo se traduce (def, t) a draw calls.
///
/// Nota de diseno: las mismas tecnicas que ya probaron los efectos hardcodeados del motor —
/// el swirl dibuja scanlines desplazadas, el aura del jefe usa blend aditivo, los rayos de
/// ShowItemGet giran con seno. Los overlays alfa directos degradan el canal alfa del render
/// texture (la leccion de la placa): la luz va en aditivo, la sombra en multiplicativo.
/// </summary>
public static class VfxRenderer
{
    /// <summary>Fondo de batalla vivo (kind background): capas infinitas que loopean con t.</summary>
    public static void DrawBackground(VfxDef def, float t, int width, int height)
    {
        var tMs = (int)(t * 1000);
        foreach (var layer in def.Layers)
        {
            if (layer.Colors.Count == 0) continue;
            BeginBlend(layer.Blend);
            switch (layer.Pattern)
            {
                case "checker": DrawChecker(layer, t, tMs, width, height); break;
                case "rings": DrawRings(layer, t, tMs, width, height); break;
                case "waves": DrawWaves(layer, t, tMs, width, height); break;
                default: DrawBands(layer, t, tMs, width, height); break;
            }
            EndBlend(layer.Blend);
        }
    }

    /// <summary>Clima (kind weather) sobre el mundo: lluvia/nieve/niebla/relampago que loopean
    /// con t, en coordenadas de pantalla (el cielo acompana a la camara, como en SNES).</summary>
    public static void DrawWeather(VfxDef def, float t, int width, int height, int camX = 0, int camY = 0)
    {
        var tMs = (int)(t * 1000);
        foreach (var layer in def.Layers)
        {
            // Ciclo del clima: cada capa vive en su ventana con rampas (llueve -> escampa).
            var phase = VfxEval.WeatherPhase(layer, tMs, def.DurationMs > 1000 ? def.DurationMs : 0);
            if (phase <= 0f) continue;
            var color = SpriteRaster.ParseColor(layer.Color);
            BeginBlend(layer.Blend);
            switch (layer.Shape)
            {
                case "fog":
                    for (var i = 0; i < layer.Count; i++)
                    {
                        var (fx, fy, fr, fa) = VfxEval.FogBank(layer, i, t, width, height, camX, camY);
                        Raylib.DrawCircleGradient(new System.Numerics.Vector2(fx, fy), fr, Fade(color, 0.4f * fa * phase), Fade(color, 0f));
                    }
                    break;
                case "flash":
                    var la = VfxEval.LightningAlpha(layer, tMs);
                    if (la > 0) Raylib.DrawRectangle(0, 0, width, height, Fade(color, la * 0.75f * phase));
                    break;
                case "snow":
                    var flake = layer.SizePx > 0 ? layer.SizePx : 2;
                    for (var i = 0; i < layer.Count; i++)
                    {
                        var (sx, sy, sa) = VfxEval.WeatherDrop(layer, i, t, width, height, camX, camY);
                        Raylib.DrawRectangle((int)sx, (int)sy, flake, flake, Fade(color, sa * 0.65f * phase));
                    }
                    break;
                case "splash":
                    // La lluvia toca el piso: gotitas que se abren y mueren donde caen.
                    for (var i = 0; i < layer.Count; i++)
                    {
                        var (px, py, grow, pa) = VfxEval.Splash(layer, i, t, width, height, camX, camY);
                        var a = Fade(color, pa * 0.6f * phase);
                        var open = 1f + grow * 3f;
                        Raylib.DrawRectangle((int)(px - open), (int)py, 1, 1, a);
                        Raylib.DrawRectangle((int)(px + open), (int)py, 1, 1, a);
                        if (grow < 0.35f) Raylib.DrawRectangle((int)px, (int)py - 1, 1, 2, a); // el impacto
                    }
                    break;
                default: // rain: trazos ladeados por el viento (Angle), largo = sizePx (default 7)
                    // Tenue a proposito (a pleno brillaba y mareaba): alfa a mitad.
                    var len = layer.SizePx > 0 ? layer.SizePx : 4;
                    var slant = MathF.Tan((float)(layer.Angle * Math.PI / 180.0));
                    for (var i = 0; i < layer.Count; i++)
                    {
                        var (rx, ry, ra) = VfxEval.WeatherDrop(layer, i, t, width, height, camX, camY);
                        Raylib.DrawLine((int)rx, (int)ry, (int)(rx - slant * len), (int)(ry - len), Fade(color, ra * 0.55f * phase));
                    }
                    break;
            }
            EndBlend(layer.Blend);
        }
    }

    /// <summary>Impacto (kind impact) con origen en (ox, oy), en el segundo t desde el disparo.</summary>
    public static void DrawImpact(VfxDef def, float t, int ox, int oy, int width, int height)
    {
        var tMs = (int)(t * 1000);
        foreach (var layer in def.Layers)
        {
            var progress = VfxEval.LayerProgress(layer, tMs, def.DurationMs);
            if (progress < 0) continue;
            var color = SpriteRaster.ParseColor(layer.Color);
            BeginBlend(layer.Blend);
            switch (layer.Shape)
            {
                case "flash":
                    // Pantalla entera: pega fuerte al aparecer y se apaga (el pantallazo de impacto).
                    Raylib.DrawRectangle(0, 0, width, height, Fade(color, 1f - progress));
                    break;
                case "ring":
                    var radius = Math.Max(1f, progress * layer.SpreadPx);
                    Raylib.DrawRing(new System.Numerics.Vector2(ox, oy), radius, radius + 2f, 0f, 360f, 48, Fade(color, 1f - progress));
                    break;
                case "slash":
                    // Tajo que crece a traves del blanco, con una segunda linea pegada de cuerpo.
                    var rad = (float)(layer.Angle * Math.PI / 180.0);
                    var (dx, dy) = (MathF.Cos(rad), MathF.Sin(rad));
                    var half = (0.2f + 0.8f * progress) * layer.SpreadPx;
                    var a = new System.Numerics.Vector2(ox - dx * half, oy - dy * half);
                    var b = new System.Numerics.Vector2(ox + dx * half, oy + dy * half);
                    Raylib.DrawLineEx(a, b, 2f, Fade(color, 1f - progress));
                    Raylib.DrawLineEx(a with { Y = a.Y + 1 }, b with { Y = b.Y + 1 }, 1f, Fade(color, (1f - progress) * 0.5f));
                    break;
                case "beam":
                    // Columna de luz desde el cielo hasta el blanco, que respira con el progreso.
                    var bw = Math.Max(2, VfxEval.CellSize(layer, background: false) * 3);
                    var pulse = MathF.Sin(progress * MathF.PI); // entra y sale suave
                    Raylib.DrawRectangle(ox - bw / 2, 0, bw, oy, Fade(color, pulse * 0.85f));
                    Raylib.DrawRectangle(ox - bw / 6, 0, Math.Max(1, bw / 3), oy, Fade(color, pulse));
                    break;
                default: // spark: particulas con direccion y ritmo propios (hash del indice, sin RNG)
                    var size = VfxEval.CellSize(layer, background: false);
                    for (var i = 0; i < layer.Count; i++)
                    {
                        var (px, py, alpha) = VfxEval.Particle(layer, i, progress);
                        Raylib.DrawRectangle(ox + (int)px - size / 2, oy + (int)py - size / 2, size, size, Fade(color, alpha));
                    }
                    break;
            }
            EndBlend(layer.Blend);
        }
    }

    /// <summary>Bandas horizontales que scrollean, ciclan colores y ondulan (el fondo de batalla
    /// canonico). La distorsion corre el MUESTREO de la banda (que banda pinta este scanline), no
    /// la posicion de dibujo: una fila uniforme desplazada a los costados se veria identica; lo
    /// que tiene que ondular es el BORDE entre bandas. Una fila de 1px por linea, como el swirl.</summary>
    static void DrawBands(VfxLayer l, float t, int tMs, int width, int height)
    {
        var cell = VfxEval.CellSize(l, background: true);
        var scroll = (int)MathF.Floor((float)(l.ScrollY * t));
        for (var y = 0; y < height; y++)
        {
            var band = FloorDiv(y + scroll + (int)VfxEval.ScanlineOffset(l, y, t), cell);
            var color = SpriteRaster.ParseColor(l.Colors[VfxEval.ColorIndex(l, band, tMs)]);
            Raylib.DrawRectangle(0, y, width, 1, color);
        }
    }

    /// <summary>Damero que scrollea en diagonal; cada fila de celdas ondula con la distorsion.</summary>
    static void DrawChecker(VfxLayer l, float t, int tMs, int width, int height)
    {
        var cell = VfxEval.CellSize(l, background: true);
        var (sx, sy) = ((int)MathF.Floor((float)(l.ScrollX * t)), (int)MathF.Floor((float)(l.ScrollY * t)));
        for (var cy = -1; cy <= height / cell + 1; cy++)
        {
            var off = (int)VfxEval.ScanlineOffset(l, cy * cell, t);
            for (var cx = -1; cx <= width / cell + 1; cx++)
            {
                var idx = VfxEval.ColorIndex(l, FloorDiv(cx * cell + sx, cell) + FloorDiv(cy * cell + sy, cell), tMs);
                Raylib.DrawRectangle(cx * cell + off - sx % cell, cy * cell - sy % cell, cell, cell, SpriteRaster.ParseColor(l.Colors[idx]));
            }
        }
    }

    /// <summary>Anillos concentricos que laten desde el centro (ScrollY = deriva radial px/seg).</summary>
    static void DrawRings(VfxLayer l, float t, int tMs, int width, int height)
    {
        var cell = VfxEval.CellSize(l, background: true);
        var center = new System.Numerics.Vector2(width / 2f, height / 2f);
        var maxR = MathF.Sqrt(width * width + height * height) / 2f;
        var drift = (float)(l.ScrollY * t) % cell;
        var baseBand = (int)MathF.Floor((float)(l.ScrollY * t) / cell);
        for (var r = -drift; r < maxR; r += cell)
        {
            var band = (int)MathF.Round((r + drift) / cell) - baseBand;
            var color = SpriteRaster.ParseColor(l.Colors[VfxEval.ColorIndex(l, band, tMs)]);
            Raylib.DrawRing(center, MathF.Max(0, r), r + cell, 0f, 360f, 64, color);
        }
    }

    /// <summary>Columnas verticales que ondulan: las bandas giradas 90 grados, con la misma
    /// correccion — el seno corre el muestreo de la columna, para que el borde serpentee.</summary>
    static void DrawWaves(VfxLayer l, float t, int tMs, int width, int height)
    {
        var cell = VfxEval.CellSize(l, background: true);
        var scroll = (int)MathF.Floor((float)(l.ScrollX * t));
        for (var x = 0; x < width; x++)
        {
            var band = FloorDiv(x + scroll + (int)VfxEval.ScanlineOffset(l, x, t), cell);
            var color = SpriteRaster.ParseColor(l.Colors[VfxEval.ColorIndex(l, band, tMs)]);
            Raylib.DrawRectangle(x, 0, 1, height, color);
        }
    }

    static int FloorDiv(int a, int b) => (int)Math.Floor(a / (double)b);

    static Color Fade(Color c, float alpha) => new(c.R, c.G, c.B, (byte)Math.Clamp((int)(alpha * 255), 0, 255));

    static void BeginBlend(string blend)
    {
        if (blend == "additive") Raylib.BeginBlendMode(BlendMode.Additive);
        else if (blend == "multiply") Raylib.BeginBlendMode(BlendMode.Multiplied);
    }

    static void EndBlend(string blend)
    {
        if (blend != "normal") Raylib.EndBlendMode();
    }
}
