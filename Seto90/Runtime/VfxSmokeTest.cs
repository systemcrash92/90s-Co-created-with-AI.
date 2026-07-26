using System.Text;

namespace Seto90;

/// <summary>
/// Smoke headless de los VFX declarativos: verifica la evaluacion PURA (VfxEval) sin ventana —
/// determinismo del hash, ventanas de capa, particulas en el origen al nacer y dispersadas al
/// morir, distorsion acotada y palette cycling exacto — y que los defaults embebidos del motor
/// (vfx.hit, vfx.heal) y la validacion (bad_vfx_*, missing_vfx, vfx_kind_mismatch) funcionen.
/// Como map-smoke con MapOps: la misma matematica que dibuja el runtime, probada con numeros.
/// </summary>
public sealed class VfxSmokeTest
{
    public string Run()
    {
        var sb = new StringBuilder();

        // 1. Determinismo: el azar de particulas es hash del indice, no RNG. Dos evaluaciones
        // identicas dan lo mismo, e indices distintos dan posiciones distintas.
        var spark = new VfxLayer { Shape = "spark", Motion = "burst", Count = 16, SpreadPx = 24 };
        var a1 = VfxEval.Particle(spark, 3, 0.5f);
        var a2 = VfxEval.Particle(spark, 3, 0.5f);
        Expect(a1 == a2, "la misma particula en el mismo t dio posiciones distintas (no determinista)");
        Expect(VfxEval.Particle(spark, 4, 0.5f) != a1, "dos particulas distintas cayeron en el mismo punto exacto");
        for (var i = 0; i < 64; i++)
        {
            var h = VfxEval.Hash01(i, 7);
            Expect(h is >= 0 and <= 1, $"Hash01({i}) fuera de 0..1: {h}");
        }
        sb.AppendLine("Determinismo OK: hash estable por indice, sin RNG.");

        // 2. Ventana de capa: inactiva fuera de [StartMs, EndMs], progreso 0..1 adentro,
        // EndMs 0 = hasta el final del efecto.
        var windowed = new VfxLayer { StartMs = 100, EndMs = 300 };
        Expect(VfxEval.LayerProgress(windowed, 50, 600) < 0, "capa activa antes de startMs");
        Expect(VfxEval.LayerProgress(windowed, 100, 600) == 0f, "progreso != 0 al abrir la ventana");
        Expect(Math.Abs(VfxEval.LayerProgress(windowed, 200, 600) - 0.5f) < 0.001f, "progreso != 0.5 al medio de la ventana");
        Expect(VfxEval.LayerProgress(windowed, 300, 600) < 0, "capa activa despues de endMs");
        var openEnd = new VfxLayer { StartMs = 0, EndMs = 0 };
        Expect(VfxEval.LayerProgress(openEnd, 599, 600) > 0.99f, "endMs 0 no llego hasta el final del efecto");
        sb.AppendLine("Ventanas OK: [startMs, endMs) con endMs 0 = duracion completa.");

        // 3. Particulas: nacen EN el origen (progreso 0) y al morir quedan dentro del alcance.
        foreach (var motion in VfxEval.Motions.Where(m => m != "fall"))
        {
            var l = new VfxLayer { Motion = motion, SpreadPx = 24 };
            var (x0, y0, alpha0) = VfxEval.Particle(l, 5, 0f);
            Expect(motion == "fall" || (Math.Abs(x0) <= 12 && Math.Abs(y0) <= 12), $"particula {motion} nacio lejos del origen ({x0},{y0})");
            Expect(alpha0 == 1f, $"particula {motion} no nacio opaca");
            var (x1, y1, alpha1) = VfxEval.Particle(l, 5, 1f);
            Expect(Math.Abs(x1) <= 24 + 1 && Math.Abs(y1) <= 24 + 1, $"particula {motion} se paso del spreadPx ({x1},{y1})");
            Expect(alpha1 == 0f, $"particula {motion} no murio transparente");
        }
        // fall es el caso invertido: nace ARRIBA (cayendo hacia el blanco) y termina en el origen.
        var fall = new VfxLayer { Motion = "fall", SpreadPx = 24 };
        Expect(VfxEval.Particle(fall, 5, 0f).Y < 0, "particula fall no nacio arriba del blanco");
        Expect(Math.Abs(VfxEval.Particle(fall, 5, 1f).Y) < 0.001f, "particula fall no termino sobre el blanco");
        sb.AppendLine("Particulas OK: nacen en el origen, mueren dispersas dentro del alcance (fall cae hacia el blanco).");

        // 4. Fondo: la distorsion por scanline queda acotada por la amplitud y el palette
        // cycling rota exacto (tambien con bandas negativas, que trae el scroll).
        var bg = new VfxLayer { Pattern = "bands", Colors = ["#111111", "#222222", "#333333"], DistortAmp = 8, CycleMs = 100 };
        for (var y = 0; y < 224; y += 7)
            Expect(Math.Abs(VfxEval.ScanlineOffset(bg, y, 1.234f)) <= 8.0001f, $"offset del scanline {y} supero la amplitud");
        Expect(VfxEval.ColorIndex(bg, 0, 0) == 0, "ciclo: banda 0 en t=0 no dio el color 0");
        Expect(VfxEval.ColorIndex(bg, 0, 100) == 1, "ciclo: 100ms con cycleMs=100 no roto al color 1");
        Expect(VfxEval.ColorIndex(bg, 0, 300) == 0, "ciclo: una vuelta completa no volvio al color 0");
        Expect(VfxEval.ColorIndex(bg, -5, 0) is >= 0 and <= 2, "banda negativa (scroll) dio indice fuera de la paleta");
        Expect(VfxEval.ScanlineOffset(new VfxLayer { DistortAmp = 0 }, 50, 3f) == 0f, "capa sin distorsion devolvio offset");
        sb.AppendLine("Fondos OK: distorsion acotada por amplitud, palette cycling exacto y scroll negativo seguro.");

        // 5. Defaults embebidos: vfx.hit y vfx.heal existen, son impact y VALIDAN (un proyecto
        // que los pisa con vfx.create pasa por el mismo validador).
        var defaults = DefaultAssets.DefaultVfx();
        Expect(defaults.Any(v => v.Id == "vfx.hit") && defaults.Any(v => v.Id == "vfx.heal"), "faltan los vfx reservados del motor");
        var probe = new GameProject { Vfx = [.. defaults] };
        var ok = ProjectValidator.Validate(probe);
        Expect(ok.Ok, "los defaults del motor no pasan su propio validador: " + ok.ToHumanText());
        Expect(VfxEval.Find(probe, "vfx.hit") != null && VfxEval.Find(new GameProject(), "vfx.heal") != null, "Find no resuelve los reservados");
        sb.AppendLine("Defaults OK: vfx.hit y vfx.heal embebidos, kind impact, validos.");

        // 5b. El sonido viaja EN el efecto: los tres impactos reservados traen su par sonoro,
        // y el de recibir un golpe suena distinto del de darlo (por eso vfx.hit_ally existe).
        var sfxIds = DefaultAssets.DefaultSfx().Select(x => x.Id).ToHashSet();
        foreach (var id in new[] { "vfx.hit", "vfx.heal", "vfx.hit_ally" })
        {
            var v = defaults.FirstOrDefault(x => x.Id == id);
            Expect(v != null, $"falta el vfx reservado {id}");
            Expect(v!.SfxId != "", $"{id} quedo mudo: un impacto reservado tiene que traer su sonido");
            Expect(sfxIds.Contains(v.SfxId), $"{id} apunta a un sfx inexistente ({v.SfxId})");
        }
        Expect(defaults.First(v => v.Id == "vfx.hit").SfxId != defaults.First(v => v.Id == "vfx.hit_ally").SfxId,
            "dar y recibir un golpe no pueden compartir el sonido");
        Expect(defaults.Where(v => v.Kind != "impact").All(v => v.SfxId == ""),
            "un background o un clima con sonido lo dispararia un sample por cuadro");
        sb.AppendLine("Sonido del VFX OK: hit/heal/hit_ally con su par sonoro y sin sonido en los que loopean.");

        // 6. Validacion negativa: kind desconocido, capa rota, color invalido, referencia rota
        // y kind cruzado (un fondo donde va un impacto) se detectan con su codigo exacto.
        Expect(HasIssue(new GameProject { Vfx = [new VfxDef { Id = "vfx.x", Kind = "explosion" }] }, "bad_vfx_kind"), "kind desconocido no detectado");
        Expect(HasIssue(new GameProject { Vfx = [new VfxDef { Id = "vfx.x", Layers = [new VfxLayer { Shape = "rayo" }] }] }, "bad_vfx_layer"), "shape desconocido no detectado");
        Expect(HasIssue(new GameProject { Vfx = [new VfxDef { Id = "vfx.x", Layers = [new VfxLayer { Color = "rojo" }] }] }, "bad_vfx_color"), "color invalido no detectado");
        Expect(HasIssue(new GameProject { Vfx = [new VfxDef { Id = "vfx.x", DurationMs = 60000, Layers = [new VfxLayer()] }] }, "bad_vfx_duration"), "duracion desmedida no detectada (la leccion del zzz:30)");
        Expect(HasIssue(new GameProject { Skills = [new SkillDef { Id = "skill.x", Name = "X", VfxId = "vfx.nada" }] }, "missing_vfx"), "referencia rota de skill no detectada");
        var fondo = new VfxDef { Id = "vfx.fondo", Kind = "background", Layers = [new VfxLayer { Pattern = "bands", Colors = ["#101020"] }] };
        Expect(HasIssue(new GameProject { Vfx = [fondo], Skills = [new SkillDef { Id = "skill.x", Name = "X", VfxId = "vfx.fondo" }] }, "vfx_kind_mismatch"), "fondo usado como impacto no detectado");
        Expect(!HasIssue(new GameProject { Vfx = [fondo] }, "bad_vfx_duration"), "los background no deben validar durationMs (loopean)");
        Expect(HasIssue(new GameProject { Vfx = [new VfxDef { Id = "vfx.x", SfxId = "sfx.nada", Layers = [new VfxLayer()] }] }, "missing_sfx"), "sonido inexistente de un vfx no detectado");
        Expect(HasIssue(new GameProject { Vfx = [new VfxDef { Id = "vfx.f", Kind = "background", SfxId = "sfx.hit", Layers = [new VfxLayer { Pattern = "bands", Colors = ["#101020"] }] }] }, "bad_vfx_sfx"), "sonido en un background (que loopea) no detectado");
        sb.AppendLine("Validacion OK: bad_vfx_kind/layer/color/duration/sfx, missing_vfx/sfx y vfx_kind_mismatch detectados.");

        // 7. Clima: gotas deterministas y envueltas en pantalla, niebla acotada,
        // relampago con su doble destello exacto, defaults vfx.lluvia/niebla/nieve validos,
        // y validacion negativa del vocabulario weather.
        var rain = new VfxLayer { Shape = "rain", Count = 32, Angle = 14, ScrollY = 150 };
        Expect(VfxEval.WeatherDrop(rain, 3, 1.5f, 256, 224) == VfxEval.WeatherDrop(rain, 3, 1.5f, 256, 224), "gota no determinista");
        for (var i = 0; i < 32; i++)
        {
            var (wx, wy, wa) = VfxEval.WeatherDrop(rain, i, 7.77f, 256, 224);
            Expect(wx is >= 0 and < 256 && wy is >= -12 and < 224 + 12, $"gota {i} fuera de pantalla ({wx},{wy})");
            Expect(wa is > 0 and <= 1, $"gota {i} con alfa invalido {wa}");
        }
        var d0 = VfxEval.WeatherDrop(rain, 5, 1.0f, 256, 224);
        var d1 = VfxEval.WeatherDrop(rain, 5, 1.1f, 256, 224);
        Expect(d0 != d1, "la lluvia no avanza con el tiempo");
        var fog = new VfxLayer { Shape = "fog", Count = 4, SpreadPx = 84 };
        var (bx, by, br, ba) = VfxEval.FogBank(fog, 2, 3.3f, 256, 224);
        Expect(br == 84 && ba is >= 0.15f and <= 1f, $"banco de niebla fuera de rango (r={br}, a={ba})");
        var bolt = new VfxLayer { Shape = "flash", CycleMs = 4000 };
        Expect(VfxEval.LightningAlpha(bolt, 50) == 1f, "relampago sin golpe inicial");
        Expect(VfxEval.LightningAlpha(bolt, 120) == 0f, "relampago sin apagon entre destellos");
        Expect(VfxEval.LightningAlpha(bolt, 200) == 0.55f, "relampago sin eco");
        Expect(VfxEval.LightningAlpha(bolt, 2000) == 0f, "relampago encendido fuera del destello");
        Expect(VfxEval.LightningAlpha(bolt, 4050) == 1f, "relampago no volvio en el ciclo siguiente");
        Expect(VfxEval.LightningAlpha(new VfxLayer { Shape = "flash", CycleMs = 0 }, 50) == 0f, "flash sin ciclo deberia ser 0");
        Expect(defaults.Any(v => v.Id == "vfx.lluvia" && v.Kind == "weather") && defaults.Any(v => v.Id == "vfx.niebla") && defaults.Any(v => v.Id == "vfx.nieve"), "faltan los climas reservados");
        Expect(HasIssue(new GameProject { Vfx = [new VfxDef { Id = "vfx.w", Kind = "weather", Layers = [new VfxLayer { Shape = "spark" }] }] }, "bad_vfx_layer"), "shape de impacto en weather no detectado");
        Expect(HasIssue(new GameProject { Vfx = [new VfxDef { Id = "vfx.w", Kind = "weather", Layers = [new VfxLayer { Shape = "flash", CycleMs = 200 }] }] }, "bad_vfx_layer"), "relampago con ciclo corto no detectado");
        var clima = new VfxDef { Id = "vfx.w", Kind = "weather", Layers = [new VfxLayer { Shape = "rain" }] };
        Expect(HasIssue(new GameProject { Vfx = [clima], Skills = [new SkillDef { Id = "skill.x", Name = "X", VfxId = "vfx.w" }] }, "vfx_kind_mismatch"), "clima usado como impacto no detectado");
        var mapaClima = new MapDef { Id = "map.m", TilesetId = "tileset.t", Width = 2, Height = 2, Tiles = [0, 0, 0, 0], WeatherVfxId = "vfx.hit" };
        Expect(HasIssue(new GameProject { Tilesets = [new TilesetDef { Id = "tileset.t", Tiles = [new TileDef { Id = 0 }] }], Maps = [mapaClima] }, "vfx_kind_mismatch"), "impacto usado como clima de mapa no detectado");
        // Ciclos de clima: fase 0 fuera de la ventana, 1 en el medio, rampa suave en los
        // bordes; durationMs 0 = permanente. Splash: determinista y reubicada por ciclo.
        var ciclo = new VfxLayer { Shape = "rain", StartMs = 10000, EndMs = 50000 };
        Expect(VfxEval.WeatherPhase(ciclo, 5000, 80000) == 0f, "capa activa fuera de su ventana de ciclo");
        Expect(VfxEval.WeatherPhase(ciclo, 30000, 80000) == 1f, "capa no llego a plena fase en el medio");
        var rampIn = VfxEval.WeatherPhase(ciclo, 11000, 80000);
        Expect(rampIn is > 0f and < 1f, $"la rampa de entrada no es gradual ({rampIn})");
        Expect(VfxEval.WeatherPhase(ciclo, 85000 + 30000 - 80000, 80000) == 1f, "el ciclo no envuelve (segundo loop)");
        Expect(VfxEval.WeatherPhase(ciclo, 30000, 0) == 1f, "clima permanente (durationMs 0) deberia estar siempre en fase 1");
        var splash = new VfxLayer { Shape = "splash", Count = 8 };
        Expect(VfxEval.Splash(splash, 2, 3.14f, 256, 224) == VfxEval.Splash(splash, 2, 3.14f, 256, 224), "salpicadura no determinista");
        var (s1x, s1y, _, _) = VfxEval.Splash(splash, 2, 0.1f, 256, 224);
        var (s2x, s2y, _, _) = VfxEval.Splash(splash, 2, 5.0f, 256, 224);
        Expect(s1x != s2x || s1y != s2y, "la salpicadura no se reubica entre ciclos");
        Expect(defaults.Any(v => v.Id == "vfx.tormenta" && v.DurationMs == 80000), "falta vfx.tormenta (el ciclo de ejemplo)");
        // Anclaje al MUNDO: la lluvia no persigue a la camara (perseguirla mareaba).
        var still = new VfxLayer { Shape = "rain", Count = 8, Angle = 0, ScrollY = 100 };
        var (wx0, wy0, _) = VfxEval.WeatherDrop(still, 3, 2f, 256, 224, 0, 0);
        var (wx1, wy1, _) = VfxEval.WeatherDrop(still, 3, 2f, 256, 224, 10, 0);
        Expect(Math.Abs(((wx0 - 10) % 256 + 256) % 256 - wx1) < 0.01f && Math.Abs(wy0 - wy1) < 0.01f, "mover la camara no desplazo la gota en mundo");
        // Nieve: X pleno al mundo; Y SIN acople a la camara (factor 0, calibrado en vivo:
        // cualquier acople vertical de una caida lenta se percibe como acelerar/retroceder).
        var copo = new VfxLayer { Shape = "snow", Count = 8, ScrollY = 20 };
        var (nx0, ny0, _) = VfxEval.WeatherDrop(copo, 3, 2f, 256, 224, 0, 0);
        var (nx1, ny1, _) = VfxEval.WeatherDrop(copo, 3, 2f, 256, 224, 40, 0);
        Expect(Math.Abs(((nx0 - 40) % 256 + 256) % 256 - nx1) < 0.01f && Math.Abs(ny0 - ny1) < 0.01f, "la nieve no quedo anclada plena en X");
        var (nxc, nyc, _) = VfxEval.WeatherDrop(copo, 3, 2f, 256, 224, 0, 500);
        Expect(Math.Abs(ny0 - nyc) < 0.01f && Math.Abs(nx0 - nxc) < 0.01f, "la camara vertical NO debe mover la nieve (factor 0)");
        sb.AppendLine("Clima OK: gotas envueltas y deterministas, niebla acotada, relampago exacto, ciclos con rampas, splash reubicada, defaults lluvia/niebla/nieve/tormenta y validacion weather.");

        sb.AppendLine("VFX smoke OK.");
        return sb.ToString();
    }

    static bool HasIssue(GameProject p, string code) => ProjectValidator.Validate(p).Issues.Any(i => i.Code == code);

    static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("vfx-smoke: " + message);
    }
}
