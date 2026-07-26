namespace Seto90;

/// <summary>
/// Assets default embebidos en el motor: lo que el juego usa cuando el proyecto no define el suyo.
///
/// Nota de diseno: separacion motor/contenido con red de seguridad. En los 90 un puntero de
/// grafico sin inicializar dibujaba basura de VRAM; aca todo tiene un default digno (un heroe
/// generico, un tema sobrio) y el contenido lo reemplaza por datos via MCP cuando quiere.
/// Los ids con prefijo "__" son internos del motor y no pueden existir en el proyecto
/// (el validador exige ids en minusculas que empiecen con letra).
/// </summary>
public static class DefaultAssets
{
    public const string PlayerSpriteId = "__default_player";

    /// <summary>
    /// SFX default del motor con ids reservados: el proyecto los sobreescribe definiendo
    /// un SfxDef con el mismo id via sfx.create.
    /// </summary>
    public static IReadOnlyList<SfxDef> DefaultSfx() =>
    [
        new() { Id = "sfx.cursor", Wave = "square", StartFreq = 1200, EndFreq = 1200, DurationMs = 30, Decay = 0.8, Volume = 0.22 },
        new() { Id = "sfx.confirm", Wave = "square", StartFreq = 800, EndFreq = 1300, DurationMs = 70, Decay = 0.5, Volume = 0.28 },
        new() { Id = "sfx.cancel", Wave = "square", StartFreq = 600, EndFreq = 300, DurationMs = 90, Decay = 0.6, Volume = 0.28 },
        new() { Id = "sfx.text_blip", Wave = "square", StartFreq = 1000, EndFreq = 950, DurationMs = 25, Decay = 0.9, Volume = 0.18 },
        new() { Id = "sfx.encounter", Wave = "saw", StartFreq = 220, EndFreq = 880, DurationMs = 350, Decay = 0.3, Volume = 0.38 },
        new() { Id = "sfx.hit", Wave = "noise", StartFreq = 900, EndFreq = 200, DurationMs = 140, Decay = 0.8, Volume = 0.4 },
        new() { Id = "sfx.player_hit", Wave = "noise", StartFreq = 400, EndFreq = 120, DurationMs = 200, Decay = 0.7, Volume = 0.4 },
        new() { Id = "sfx.victory", Wave = "square", StartFreq = 523, EndFreq = 1046, DurationMs = 400, Decay = 0.2, Volume = 0.32 },
        new() { Id = "sfx.save", Wave = "triangle", StartFreq = 660, EndFreq = 990, DurationMs = 180, Decay = 0.4, Volume = 0.32 },
        new() { Id = "sfx.door", Wave = "square", StartFreq = 520, EndFreq = 240, DurationMs = 110, Decay = 0.5, Volume = 0.3 },
        // La fanfarria del item clave (ShowItemGet): campana que sube una octava, duty
        // fino de NES para que brille. Mas larga que sfx.victory: es una ceremonia.
        new() { Id = "sfx.item_get", Wave = "square", StartFreq = 660, EndFreq = 1320, DurationMs = 480, Decay = 0.25, Volume = 0.34, Duty = 0.25 },
        // El par sonoro de vfx.heal, que hasta ahora curaba en silencio: triangle que sube
        // suave, sin ataque percusivo — lo contrario del ruido de un golpe.
        new() { Id = "sfx.heal", Wave = "triangle", StartFreq = 520, EndFreq = 1040, DurationMs = 300, Decay = 0.35, Volume = 0.3 },
    ];

    /// <summary>
    /// VFX default del motor con ids reservados (el proyecto los pisa via vfx.create):
    /// vfx.hit = el impacto del ataque basico y de las skills damage sin VfxId propio
    /// (flash + tajo + chispas + onda, el gesto de impacto clasico); vfx.heal = el brillo
    /// de curas y revives (chispas verdes que suben + onda suave). Todo combate se ve
    /// mejor gratis, como los SFX reservados.
    /// </summary>
    public static IReadOnlyList<VfxDef> DefaultVfx() =>
    [
        new()
        {
            Id = "vfx.hit", Kind = "impact", DurationMs = 450, SfxId = "sfx.hit",
            Layers =
            [
                new VfxLayer { Shape = "flash", Color = "#FFFFFF", StartMs = 0, EndMs = 90 },
                new VfxLayer { Shape = "slash", Color = "#F5E15A", StartMs = 30, EndMs = 200, Angle = 45, SpreadPx = 26 },
                new VfxLayer { Shape = "spark", Color = "#FFE070", Motion = "burst", Count = 12, StartMs = 60, SpreadPx = 26, SizePx = 2 },
                new VfxLayer { Shape = "ring", Color = "#FFFFFF", StartMs = 80, EndMs = 320, SpreadPx = 22 },
            ],
        },
        new()
        {
            // Climas default (reservados sobreescribibles): lluvia melancolica de dos planos,
            // niebla de bancos que derivan y nevada suave. Enganchar con map.weatherVfxId.
            // v2 (probado en vivo: brillaba, mareaba, gotas grandes, no tocaba el piso):
            // mas lenta, tenue, gotas cortas y con salpicaduras abriendose donde caen.
            Id = "vfx.lluvia", Kind = "weather", DurationMs = 1000,
            Layers =
            [
                new VfxLayer { Shape = "rain", Color = "#7FA6C8", Count = 54, Angle = 12, ScrollY = 95, SizePx = 4 },
                new VfxLayer { Shape = "rain", Color = "#4A6685", Count = 36, Angle = 12, ScrollY = 60, SizePx = 3 },
                new VfxLayer { Shape = "splash", Color = "#9CBCD8", Count = 14 },
            ],
        },
        new()
        {
            // Tormenta con CICLO declarativo (durationMs = largo del ciclo; cada capa vive en
            // su ventana con rampas): llueve fuerte 50s con relampagos, escampa 30s, repite.
            Id = "vfx.tormenta", Kind = "weather", DurationMs = 80000,
            Layers =
            [
                new VfxLayer { Shape = "rain", Color = "#8CACC8", Count = 90, Angle = 20, ScrollY = 130, SizePx = 5, StartMs = 0, EndMs = 50000 },
                new VfxLayer { Shape = "rain", Color = "#4A6685", Count = 50, Angle = 20, ScrollY = 85, SizePx = 3, StartMs = 0, EndMs = 50000 },
                new VfxLayer { Shape = "splash", Color = "#9CBCD8", Count = 20, StartMs = 0, EndMs = 50000 },
                new VfxLayer { Shape = "flash", Color = "#E8F0FF", CycleMs = 7000, StartMs = 15000, EndMs = 50000 },
            ],
        },
        new()
        {
            Id = "vfx.niebla", Kind = "weather", DurationMs = 1000,
            Layers =
            [
                new VfxLayer { Shape = "fog", Color = "#93A0B4", Count = 4, SpreadPx = 84, ScrollX = 8 },
                new VfxLayer { Shape = "fog", Color = "#6E7C94", Count = 3, SpreadPx = 120, ScrollX = 5 },
            ],
        },
        new()
        {
            // v2 (mismo feedback que la lluvia: menos, mas lenta, mas transparente).
            Id = "vfx.nieve", Kind = "weather", DurationMs = 1000,
            Layers =
            [
                new VfxLayer { Shape = "snow", Color = "#C2D2E2", Count = 38, ScrollY = 22, SizePx = 2 },
                new VfxLayer { Shape = "snow", Color = "#66788E", Count = 26, ScrollY = 14, SizePx = 1 },
            ],
        },
        new()
        {
            Id = "vfx.heal", Kind = "impact", DurationMs = 600, SfxId = "sfx.heal",
            Layers =
            [
                new VfxLayer { Shape = "spark", Color = "#7CE8A0", Motion = "rise", Count = 10, StartMs = 0, SpreadPx = 20, SizePx = 2 },
                new VfxLayer { Shape = "ring", Color = "#A8F0C0", StartMs = 0, EndMs = 380, SpreadPx = 18 },
            ],
        },
        new()
        {
            // El impacto que se ve sobre TU panel cuando te pegan. Existe aparte de vfx.hit
            // para que el sonido siga siendo el de recibir (sfx.player_hit) y no el de pegar:
            // ahora que el sonido viaja en el VFX, cada lado del golpe necesita el suyo.
            Id = "vfx.hit_ally", Kind = "impact", DurationMs = 420, SfxId = "sfx.player_hit",
            Layers =
            [
                new VfxLayer { Shape = "flash", Color = "#FFFFFF", StartMs = 0, EndMs = 80 },
                new VfxLayer { Shape = "spark", Color = "#FF9080", Motion = "burst", Count = 10, StartMs = 40, SpreadPx = 22, SizePx = 2 },
                new VfxLayer { Shape = "ring", Color = "#FF6050", StartMs = 60, EndMs = 300, SpreadPx = 20 },
            ],
        },
    ];

    /// <summary>
    /// Jingle de arranque de la placa "MADE WITH 90s ENGINE" (id reservado sfx.boot, el
    /// proyecto lo pisa con sfx.create): el gesto SEGA/PlayStation traducido a chiptune —
    /// pad triangle en quinta (C3+G3) que se hincha y campana square que sube C5-E5-G5-C6.
    /// Es una SongDef y no un SfxDef porque un solo barrido no puede hacer un acorde.
    /// </summary>
    public static SongDef BootJingle() => new()
    {
        Id = "song.engine_boot",
        Tempo = 240,
        Channels =
        [
            new SongChannel { Wave = "triangle", Notes = ["C3:8"], Volume = 0.6, AttackMs = 450, ReleaseMs = 900 },
            new SongChannel { Wave = "triangle", Notes = ["G3:8"], Volume = 0.42, AttackMs = 650, ReleaseMs = 900 },
            new SongChannel { Wave = "square", Duty = 0.25, Notes = ["R:2", "C5", "E5", "G5", "C6:3"], Volume = 0.3, AttackMs = 5, ReleaseMs = 420 },
        ],
    };

    /// <summary>Heroe generico embebido: se usa si el proyecto no define playerSpriteId.</summary>
    public static SpriteDef PlayerSprite() => new()
    {
        Id = PlayerSpriteId,
        Name = "Heroe default",
        Width = 16,
        Height = 16,
        Palette = ["#1a1c2c", "#ffcd75", "#4a5268", "#f5e15a", "#8a5a44", "#ffffff"],
        Poses =
        [
            new SpritePose
            {
                Direction = Facing.Down,
                Frames =
                [
                    new SpriteFrame { Rows = [
                        "....00000000....", "...0222222220...", "...0222222220...", "...0211111120...",
                        "...0210110120...", "...0211111120...", "....01111110....", ".....000000.....",
                        "....03333330....", "...0333333330...", "...0133333310...", "....03333330....",
                        "....02222220....", "....0220.0220...", "....0220.0220...", "....000..000....",
                    ] },
                    new SpriteFrame { Rows = [
                        "....00000000....", "...0222222220...", "...0222222220...", "...0211111120...",
                        "...0210110120...", "...0211111120...", "....01111110....", ".....000000.....",
                        "....03333330....", "...0333333330...", "...0133333310...", "....03333330....",
                        "....02222220....", "....0220.0220...", "....0220..0220..", "...000.....000..",
                    ] },
                ]
            },
            new SpritePose
            {
                Direction = Facing.Right,
                Frames =
                [
                    new SpriteFrame { Rows = [
                        "....00000000....", "...0222222220...", "...0222222220...", "...0211111120...",
                        "...0211010120...", "...0211111120...", "....01111110....", ".....000000.....",
                        "....03333330....", "...0333333330...", "...0133333310...", "....03333330....",
                        "....02222220....", "....0220.0220...", "....0220.0220...", "....000..000....",
                    ] },
                ]
            },
            new SpritePose
            {
                Direction = Facing.Up,
                Frames =
                [
                    new SpriteFrame { Rows = [
                        "....00000000....", "...0222222220...", "...0222222220...", "...0222222220...",
                        "...0222222220...", "...0222222220...", "....02222220....", ".....000000.....",
                        "....03333330....", "...0333333330...", "...0133333310...", "....03333330....",
                        "....02222220....", "....0220.0220...", "....0220.0220...", "....000..000....",
                    ] },
                ]
            },
        ]
    };
}
