using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Seto90.DemoBuilder;

internal static class Program
{
    static int nextId = 1;

    // ---- Pixel art de la demo: plantillas 16x16 ('.' transparente, digito = indice de paleta) ----
    // Paleta comun: 0 contorno, 1 piel, 2 pelo, 3 remera, 4 pantalon, 5 acento.

    // ---- La cueva ----
    // Guardian de cristal, 24x24: mas grande que un NPC, para que la sala del jefe se sienta.
    // Paleta: 0 contorno, 1 cristal claro, 2 cristal medio, 3 cristal oscuro, 4 sombra, 5 brillo.
    static readonly string[] WardenDown0 =
    [
        ".........000000.........",
        "........05555550........",
        ".......0511111150.......",
        "......051111111150......",
        ".....05111111111150.....",
        ".....03111111111130.....",
        "....0311102211013110....",
        "....0311102211013110....",
        "....0331111111111330....",
        ".....03311111113310.....",
        "......033111111330......",
        ".....0033322233300......",
        "....03222222222222.30...",
        "...0322222222222223.0...",
        "..032222444442222230....",
        "..032224444444222230....",
        "..032244444444422230....",
        "..032244444444422230....",
        "...03224444444422300....",
        "....0322244442230.......",
        ".....03222222230........",
        "......033333330.........",
        ".......0000000..........",
        "........................",
    ];

    static readonly string[] WardenDown1 =
    [
        ".........000000.........",
        "........05555550........",
        ".......0511111150.......",
        "......051111111150......",
        ".....05111111111150.....",
        ".....03111111111130.....",
        "....0311022110213110....",
        "....0311022110213110....",
        "....0331111111111330....",
        ".....03311111113310.....",
        "......033111111330......",
        ".....0033322233300......",
        "...03222222222222.30....",
        "...0322222222222223.0...",
        "..032222444442222230....",
        "..032244444444422230....",
        "..032244444444422230....",
        "..032244444444422230....",
        "...03224444444422300....",
        "....0322244442230.......",
        ".....03222222230........",
        "......033333330.........",
        ".......0000000..........",
        "........................",
    ];

    // El corazon de la cueva: item clave de la ceremonia ShowItemGet.
    static readonly string[] ShardDown =
    [
        "................",
        ".......05.......",
        "......0510......",
        "......0510......",
        ".....051110.....",
        ".....051110.....",
        "....05111310....",
        "....05111310....",
        "....03111310....",
        "....03111310....",
        ".....0313.0.....",
        ".....031310.....",
        "......0330......",
        "......0330......",
        ".......00.......",
        "................",
    ];

    static readonly string[] AdultDown0 =
    [
        "....00000000....", "...0222222220...", "...0222222220...", "...0211111120...",
        "...0210110120...", "...0211111120...", "....01111110....", ".....000000.....",
        "....03333330....", "...0333333330...", "...0133333310...", "....03333330....",
        "....04444440....", "....0440.0440...", "....0440.0440...", "....000..000....",
    ];

    static readonly string[] AdultDown1 =
    [
        "....00000000....", "...0222222220...", "...0222222220...", "...0211111120...",
        "...0210110120...", "...0211111120...", "....01111110....", ".....000000.....",
        "....03333330....", "...0333333330...", "...0133333310...", "....03333330....",
        "....04444440....", "....0440.0440...", "....0440..0440..", "...000.....000..",
    ];

    static readonly string[] AdultRight0 =
    [
        "....00000000....", "...0222222220...", "...0222222220...", "...0211111120...",
        "...0211010120...", "...0211111120...", "....01111110....", ".....000000.....",
        "....03333330....", "...0333333330...", "...0133333310...", "....03333330....",
        "....04444440....", "....0440.0440...", "....0440.0440...", "....000..000....",
    ];

    static readonly string[] AdultRight1 =
    [
        "....00000000....", "...0222222220...", "...0222222220...", "...0211111120...",
        "...0211010120...", "...0211111120...", "....01111110....", ".....000000.....",
        "....03333330....", "...0333333330...", "...0133333310...", "....03333330....",
        "....04444440....", "....0440.0440...", "...0440...0440..", "...000....000...",
    ];

    static readonly string[] AdultUp0 =
    [
        "....00000000....", "...0222222220...", "...0222222220...", "...0222222220...",
        "...0222222220...", "...0222222220...", "....02222220....", ".....000000.....",
        "....03333330....", "...0333333330...", "...0133333310...", "....03333330....",
        "....04444440....", "....0440.0440...", "....0440.0440...", "....000..000....",
    ];

    // Bicho Reloj: 0 contorno, 1 cara de reloj, 2 caparazon, 3 agujas, 4 patas, 5 campanitas.
    static readonly string[] BichoDown0 =
    [
        ".....0....0.....", "....050..050....", ".....000000.....", "....02222220....",
        "...0221111220...", "..022111111220..", "..022111311220..", "..022111331220..",
        "..022111111220..", "...0221111220...", "....02222220....", ".....000000.....",
        "....04....04....", "...04......04...", "................", "................",
    ];

    static readonly string[] BichoDown1 =
    [
        ".....0....0.....", "....050..050....", ".....000000.....", "....02222220....",
        "...0221111220...", "..022111111220..", "..022111311220..", "..022111331220..",
        "..022111111220..", "...0221111220...", "....02222220....", ".....000000.....",
        "...04......04...", "....04....04....", "................", "................",
    ];

    static object Pose(string direction, params string[][] frames) => new { direction, frames = frames.Select(rows => new { rows }).ToArray() };

    static object[] HumanPoses() => [Pose("Down", AdultDown0, AdultDown1), Pose("Right", AdultRight0, AdultRight1), Pose("Up", AdultUp0)];

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        var root = Path.GetFullPath(args.Length > 0 ? args[0] : Path.Combine(Environment.CurrentDirectory, "DemoGame"));
        Directory.CreateDirectory(root);
        var engineProject = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Seto90", "Seto90.csproj"));

        using var server = Process.Start(new ProcessStartInfo
        {
            FileName = "dotnet",
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            ArgumentList = { "run", "--project", engineProject, "--", "mcp", "--project", root }
        }) ?? throw new InvalidOperationException("No se pudo iniciar el servidor MCP.");

        await Request(server, "initialize", new { });
        // ==================================================================
        // 90s DEMO — una torre-cueva de dos pisos, construida entera por MCP.
        // Todo lo que el motor sabe hacer, en una escena que se juega en cinco
        // minutos: clima, cinematicas, tienda, dos encuentros, un jefe con
        // presentacion, ceremonia de item, y la misma historia como novela.
        // ==================================================================

        // --- Tiles: bloques de color plano, como la Torre. Sin atlas a proposito: a 16px
        // el bloque limpio se lee mejor que una textura con ruido, y es la estetica del blockout.
        await Tool(server, "tileset.create", new { id = "tileset.tower", tileSize = 16, tiles = new object[] {
            new { id = 0, name = "stone floor", solid = false, color = "#565a6e" },
            new { id = 1, name = "tower wall", solid = true, color = "#23232f" },
            new { id = 2, name = "carpet", solid = false, color = "#7e3540" },
            new { id = 3, name = "pillar", solid = true, color = "#3a3a4c" },
            new { id = 4, name = "stairs", solid = false, color = "#c9b45a" },
            new { id = 5, name = "crystal seam", solid = true, color = "#2f4a97" } } });

        // --- Musica: tema de la torre, tema del jefe, y un cierre ---
        await Tool(server, "song.create", new { id = "song.tower", tempo = 88, channels = new object[] {
            new { wave = "triangle", volume = 0.85, releaseMs = 340, notes = new[] { "A2:4", "E2:4", "F2:4", "C2:4", "D2:4", "A2:4", "E2:4", "E2:4" } },
            new { wave = "square", duty = 0.125, volume = 0.45, releaseMs = 140, notes = new[] { "A4:2", "R:2", "C5:2", "R:2", "B4:2", "R:2", "E4:4", "R:4", "G4:2", "R:2", "A4:4" } },
            new { wave = "noise", volume = 0.16, attackMs = 3, releaseMs = 45, notes = new[] { "R:7", "C6", "R:7", "C6" } } } });
        await Tool(server, "song.create", new { id = "song.boss", tempo = 176, channels = new object[] {
            new { wave = "square", duty = 0.125, volume = 0.95, releaseMs = 28, notes = new[] { "A4", "A4", "C5", "E5", "D5", "C5", "B4", "G4", "A4", "A4", "E5", "G5", "F5:2", "E5:2" } },
            new { wave = "square", duty = 0.5, volume = 0.4, releaseMs = 40, notes = new[] { "E5:2", "R:2", "D5:2", "R:2", "C5:2", "R:2", "B4:2", "R:2" } },
            new { wave = "triangle", volume = 0.9, releaseMs = 45, notes = new[] { "A2:2", "A2:2", "G2:2", "G2:2", "F2:2", "F2:2", "E2:2", "E2:2" } },
            new { wave = "noise", volume = 0.42, attackMs = 2, releaseMs = 18, notes = new[] { "C6", "C5", "C6", "C5", "C6", "C5", "C6", "C5", "C6", "C5", "C6", "C5", "C6", "C5", "C6", "C5" } } } });

        // --- Clima: llueve dentro de la galeria (el techo se cayo) y hay niebla arriba ---
        await Tool(server, "vfx.create", new { id = "vfx.rain", kind = "weather", durationMs = 0, layers = new object[] {
            new { shape = "rain", count = 140, color = "#d6e8fa", angle = 14, scrollY = 118, sizePx = 7, blend = "additive" },
            new { shape = "rain", count = 90, color = "#8fb4d6", angle = 11, scrollY = 76, sizePx = 5, blend = "additive" },
            new { shape = "splash", count = 36, color = "#d6e8fa", blend = "additive" } } });
        await Tool(server, "vfx.create", new { id = "vfx.vault_mist", kind = "weather", durationMs = 0, layers = new object[] {
            new { shape = "fog", count = 5, color = "#4a6a8c", spreadPx = 72, scrollX = 7, blend = "additive" },
            new { shape = "fog", count = 3, color = "#2c3f5c", spreadPx = 112, scrollX = -4, blend = "additive" } } });

        // --- VFX de combate ---
        await Tool(server, "vfx.create", new { id = "vfx.vault_bg", kind = "background", layers = new object[] {
            new { pattern = "bands", colors = new[] { "#0d1626", "#132140", "#1b2f5c", "#132140" }, sizePx = 14, scrollY = 9, distortAmp = 10, distortFreq = 0.09, cycleMs = 420, blend = "normal" },
            new { pattern = "rings", colors = new[] { "#2f4a97", "#1b2a55" }, sizePx = 22, scrollY = -5, cycleMs = 900, blend = "additive" } } });
        await Tool(server, "vfx.create", new { id = "vfx.cave_bg", kind = "background", layers = new object[] {
            new { pattern = "waves", colors = new[] { "#161822", "#23232f", "#31313f" }, sizePx = 18, scrollX = 11, distortAmp = 7, distortFreq = 0.11, cycleMs = 600, blend = "normal" } } });
        await Tool(server, "vfx.create", new { id = "vfx.shatter", kind = "impact", durationMs = 640, sfxId = "sfx.hit", layers = new object[] {
            new { shape = "flash", color = "#a8c4ff", startMs = 0, endMs = 120, blend = "additive" },
            new { shape = "slash", color = "#dfe9ff", angle = 55, spreadPx = 32, startMs = 40, endMs = 330, blend = "additive" },
            new { shape = "spark", color = "#5a7fd8", count = 16, spreadPx = 36, motion = "burst", startMs = 60, endMs = 640, blend = "additive" },
            new { shape = "ring", color = "#a8c4ff", spreadPx = 44, startMs = 90, endMs = 540, blend = "additive" } } });
        await Tool(server, "sfx.create", new { id = "sfx.alerta", wave = "square", startFreq = 350, endFreq = 950, durationMs = 160, decay = 0.5, volume = 0.5, duty = 0.35 });

        // --- Piso 1: la galeria inundada, bajo la lluvia ---
        await Tool(server, "map.create", new { id = "map.gallery", name = "The Flooded Gallery", tilesetId = "tileset.tower", width = 20, height = 15, fillTile = 1, songId = "song.tower" });
        await Tool(server, "map.paint_rect", new { mapId = "map.gallery", x = 1, y = 1, width = 18, height = 13, tileId = 0 });
        await Tool(server, "map.paint_rect", new { mapId = "map.gallery", x = 9, y = 1, width = 2, height = 13, tileId = 2 });   // alfombra central
        await Tool(server, "map.paint_rect", new { mapId = "map.gallery", x = 4, y = 4, width = 1, height = 1, tileId = 3 });    // pilares
        await Tool(server, "map.paint_rect", new { mapId = "map.gallery", x = 4, y = 10, width = 1, height = 1, tileId = 3 });
        await Tool(server, "map.paint_rect", new { mapId = "map.gallery", x = 15, y = 4, width = 1, height = 1, tileId = 3 });
        await Tool(server, "map.paint_rect", new { mapId = "map.gallery", x = 15, y = 10, width = 1, height = 1, tileId = 3 });
        await Tool(server, "map.paint_rect", new { mapId = "map.gallery", x = 9, y = 1, width = 2, height = 1, tileId = 4 });    // escalera al piso 2
        await Tool(server, "map.set_info", new { mapId = "map.gallery", weatherVfxId = "vfx.rain" });

        // --- Piso 2: la boveda de cristal, con niebla ---
        await Tool(server, "map.create", new { id = "map.vault", name = "The Crystal Vault", tilesetId = "tileset.tower", width = 20, height = 15, fillTile = 1, songId = "song.tower" });
        await Tool(server, "map.paint_rect", new { mapId = "map.vault", x = 1, y = 1, width = 18, height = 13, tileId = 0 });
        await Tool(server, "map.paint_rect", new { mapId = "map.vault", x = 9, y = 2, width = 2, height = 12, tileId = 2 });
        await Tool(server, "map.paint_rect", new { mapId = "map.vault", x = 1, y = 1, width = 18, height = 1, tileId = 5 });     // vetas al fondo
        await Tool(server, "map.paint_rect", new { mapId = "map.vault", x = 1, y = 5, width = 1, height = 4, tileId = 5 });
        await Tool(server, "map.paint_rect", new { mapId = "map.vault", x = 18, y = 5, width = 1, height = 4, tileId = 5 });
        await Tool(server, "map.paint_rect", new { mapId = "map.vault", x = 6, y = 3, width = 1, height = 1, tileId = 3 });      // sala del jefe
        await Tool(server, "map.paint_rect", new { mapId = "map.vault", x = 13, y = 3, width = 1, height = 1, tileId = 3 });
        await Tool(server, "map.paint_rect", new { mapId = "map.vault", x = 9, y = 13, width = 2, height = 1, tileId = 4 });     // escalera de bajada
        await Tool(server, "map.set_info", new { mapId = "map.vault", weatherVfxId = "vfx.vault_mist" });

        await Tool(server, "map.set_warps", new { mapId = "map.gallery", warps = new object[] {
            new { x = 9, y = 1, toMapId = "map.vault", toX = 9, toY = 12, transition = "spiral" },
            new { x = 10, y = 1, toMapId = "map.vault", toX = 10, toY = 12, transition = "spiral" } } });
        await Tool(server, "map.set_warps", new { mapId = "map.vault", warps = new object[] {
            new { x = 9, y = 13, toMapId = "map.gallery", toX = 9, toY = 2, transition = "spiral" },
            new { x = 10, y = 13, toMapId = "map.gallery", toX = 10, toY = 2, transition = "spiral" } } });

        // --- Elenco ---
        await Tool(server, "sprite.create", new { id = "sprite.hero", name = "Vera", width = 16, height = 16, palette = new[] { "#14141c", "#e8c49a", "#3a2a3e", "#4a6ea8", "#2e3f66", "#c9b45a" }, poses = HumanPoses() });
        await Tool(server, "sprite.create", new { id = "sprite.merchant", name = "Pell", width = 16, height = 16, palette = new[] { "#14141c", "#d8b088", "#5c4a2e", "#7e3540", "#4a2028", "#c9b45a" }, poses = HumanPoses() });
        await Tool(server, "sprite.create", new { id = "sprite.crawler", name = "Rust Crawler", width = 16, height = 16, palette = new[] { "#14141c", "#8a6a3a", "#5c4020", "#3a2a18", "#2a1e12", "#c9b45a" }, poses = new[] { Pose("Down", BichoDown0, BichoDown1) } });
        await Tool(server, "sprite.create", new { id = "sprite.wisp", name = "Mire Wisp", width = 16, height = 16, palette = new[] { "#14141c", "#a8c4ff", "#5a7fd8", "#2f4a97", "#1b2a55", "#dfe9ff" }, poses = new[] { Pose("Down", ShardDown, ShardDown) } });
        await Tool(server, "sprite.create", new { id = "sprite.warden", name = "Crystal Warden", width = 24, height = 24, palette = new[] { "#0d0f16", "#a8c4ff", "#5a7fd8", "#2f4a97", "#1b2a55", "#dfe9ff" }, poses = new[] { Pose("Down", WardenDown0, WardenDown1) } });
        await Tool(server, "sprite.create", new { id = "sprite.shard", name = "Heart of the Tower", width = 16, height = 16, palette = new[] { "#0d0f16", "#a8c4ff", "#5a7fd8", "#2f4a97", "#1b2a55", "#dfe9ff" }, poses = new[] { Pose("Down", ShardDown, ShardDown) } });

        await Tool(server, "sfx.create", new { id = "sfx.text_blip", wave = "square", startFreq = 880, endFreq = 660, durationMs = 38, decay = 0.9, volume = 0.32, duty = 0.5 });

        // --- Party, habilidades e items ---
        await Tool(server, "skill.create", new { id = "skill.spark", name = "Spark", mpCost = 3, power = 9, kind = "damage", vfxId = "vfx.shatter" });
        await Tool(server, "skill.create", new { id = "skill.mend", name = "Mend", mpCost = 4, power = 18, kind = "heal" });
        await Tool(server, "actor.create", new { id = "actor.vera", name = "Vera", level = 3, stats = new { hp = 48, mp = 12, attack = 13, defense = 6, speed = 8 }, growth = new { hp = 6, mp = 2, attack = 2, defense = 1, speed = 1 }, skillIds = new[] { "skill.spark", "skill.mend" } });
        await Tool(server, "item.create", new { id = "item.potion", name = "Potion", price = 10, effect = "heal:24" });
        await Tool(server, "item.create", new { id = "item.antidote", name = "Antidote", price = 8, effect = "cure:poison" });
        await Tool(server, "item.create", new { id = "item.blade", name = "Chipped blade", price = 22, slot = "weapon", bonus = new { attack = 4 } });
        await Tool(server, "item.create", new { id = "item.cloak", name = "Damp cloak", price = 14, slot = "armor", bonus = new { defense = 3 } });
        await Tool(server, "item.create", new { id = "item.shard", name = "Heart of the Tower", price = 0, description = "It kept beating after the Warden stopped.", spriteId = "sprite.shard" });
        await Tool(server, "shop.create", new { id = "shop.pell", name = "Pell's landing", itemIds = new[] { "item.potion", "item.antidote", "item.blade", "item.cloak" } });

        // --- Enemigos: dos encuentros y un jefe ---
        await Tool(server, "enemy.create", new { id = "enemy.crawler", name = "Rust Crawler", stats = new { hp = 20, mp = 0, attack = 8, defense = 3, speed = 5 }, exp = 10, money = 12, inflicts = "poison" });
        await Tool(server, "enemy.create", new { id = "enemy.wisp", name = "Mire Wisp", stats = new { hp = 26, mp = 0, attack = 9, defense = 4, speed = 9 }, exp = 14, money = 16, inflicts = "sleep" });
        await Tool(server, "enemy.create", new { id = "enemy.warden", name = "Crystal Warden", stats = new { hp = 58, mp = 0, attack = 11, defense = 6, speed = 7 }, exp = 40, money = 45 });

        await Tool(server, "variable.define", new { id = "flag.intro_done", kind = "Flag", @default = "false" });
        await Tool(server, "variable.define", new { id = "flag.crawler_beaten", kind = "Flag", @default = "false" });
        await Tool(server, "variable.define", new { id = "flag.wisp_beaten", kind = "Flag", @default = "false" });
        await Tool(server, "variable.define", new { id = "flag.warden_beaten", kind = "Flag", @default = "false" });

        await Tool(server, "battle.create", new { id = "battle.crawler", view = "Frontal", rollingHp = true, enemyIds = new[] { "enemy.crawler" }, victoryFlag = "flag.crawler_beaten", damageFormula = "max(1, attack - defense)", songId = "song.boss", backgroundVfxId = "vfx.cave_bg" });
        await Tool(server, "battle.create", new { id = "battle.wisp", view = "Frontal", rollingHp = true, enemyIds = new[] { "enemy.wisp" }, victoryFlag = "flag.wisp_beaten", damageFormula = "max(1, attack - defense)", songId = "song.boss", backgroundVfxId = "vfx.cave_bg" });
        await Tool(server, "battle.create", new { id = "battle.warden", view = "Frontal", rollingHp = true, boss = true, enemyIds = new[] { "enemy.warden" }, victoryFlag = "flag.warden_beaten", damageFormula = "max(1, attack - defense)", songId = "song.boss", backgroundVfxId = "vfx.vault_bg" });

        // --- Dialogos ---
        await Tool(server, "dialogue.create", new { id = "dialogue.intro", startNodeId = "a", nodes = new object[] {
            new { id = "a", speaker = "Vera", text = "The roof gave in a hundred years ago. The rain has been coming down ever since, and the tower has been letting it.", nextNodeId = "b" },
            new { id = "b", speaker = "Vera", text = "Whatever is keeping the light on up there does not know the sky fell.", effects = new object[] { new { kind = "SetVariable", targetId = "flag.intro_done", value = "true" } } } } });
        await Tool(server, "dialogue.create", new { id = "dialogue.pell", startNodeId = "a", nodes = new object[] {
            new { id = "a", speaker = "Pell", text = "I sell to anyone who comes down again. That is the whole business plan. Want to look?", choices = new object[] {
                new { text = "Show me", nextNodeId = "shop" }, new { text = "Not yet", nextNodeId = "no" } } },
            new { id = "shop", speaker = "Pell", text = "Take your time. The rain is not going anywhere." },
            new { id = "no", speaker = "Pell", text = "Then walk carefully. Things down here rust, and things up there do not." } } });
        await Tool(server, "dialogue.create", new { id = "dialogue.warden", startNodeId = "a", nodes = new object[] {
            new { id = "a", speaker = "Crystal Warden", text = "You climbed here with a lantern.", nextNodeId = "b" },
            new { id = "b", speaker = "Crystal Warden", text = "Down where you come from, light is something you carry. Up here it is something we are.", nextNodeId = "c" },
            new { id = "c", speaker = "Vera", text = "Then let go of it." } } });
        await Tool(server, "dialogue.create", new { id = "dialogue.after", startNodeId = "a", nodes = new object[] {
            new { id = "a", speaker = "Vera", text = "The tower keeps humming. It did not need a guardian. It needed someone to notice." } } });
        await Tool(server, "dialogue.create", new { id = "dialogue.warden_done", startNodeId = "a", nodes = new object[] {
            new { id = "a", speaker = "", text = "The vault is quiet. The rain downstairs keeps falling anyway." } } });

        // --- Eventos: cinematica de apertura, tienda, dos encuentros y el jefe ---
        await Tool(server, "event.create", new { id = "event.pell", mapId = "map.gallery", name = "Pell", kind = "Npc", x = 4, y = 7, sprite = "sprite.merchant", routineId = "look_around" });
        await Tool(server, "event.set_commands", new { eventId = "event.pell", commands = new object[] {
            new { kind = "Dialogue", targetId = "dialogue.pell" }, new { kind = "OpenShop", targetId = "shop.pell" } } });

        await Tool(server, "event.create", new { id = "event.intro", mapId = "map.gallery", name = "Arrival", kind = "Trigger", x = 10, y = 12, sprite = "", routineId = "idle" });
        await Tool(server, "event.set_pages", new { eventId = "event.intro", pages = new object[] {
            new { id = "play", conditions = new object[] { new { variableId = "flag.intro_done", equalsValue = "false" } }, commands = new object[] {
                new { kind = "ShowEmote", targetId = "player", value = "puntos:1.4" },
                new { kind = "Wait", targetId = "", value = "0.9" },
                new { kind = "PanCamera", targetId = "event.pell", value = "1.1" },
                new { kind = "Wait", targetId = "", value = "0.5" },
                new { kind = "PanCamera", targetId = "player", value = "0.9" },
                new { kind = "Dialogue", targetId = "dialogue.intro" } } },
            new { id = "done", conditions = new object[] { new { variableId = "flag.intro_done", equalsValue = "true" } }, commands = new object[] { } } } });

        await Tool(server, "event.create", new { id = "event.crawler", mapId = "map.gallery", name = "Rust Crawler", kind = "Trigger", x = 10, y = 8, sprite = "sprite.crawler", routineId = "pace_horizontal" });
        await Tool(server, "event.set_pages", new { eventId = "event.crawler", pages = new object[] {
            new { id = "fight", commands = new object[] {
                new { kind = "ShowEmote", targetId = "player", value = "!:1.0" },
                new { kind = "PlaySfx", targetId = "sfx.alerta" },
                new { kind = "Battle", targetId = "battle.crawler" },
                new { kind = "GiveMoney", targetId = "", value = "10" } } },
            new { id = "gone", conditions = new object[] { new { variableId = "flag.crawler_beaten", equalsValue = "true" } } } } });

        await Tool(server, "event.create", new { id = "event.wisp", mapId = "map.gallery", name = "Mire Wisp", kind = "Trigger", x = 10, y = 4, sprite = "sprite.wisp", routineId = "look_around" });
        await Tool(server, "event.set_pages", new { eventId = "event.wisp", pages = new object[] {
            new { id = "fight", commands = new object[] {
                new { kind = "PlayVfx", targetId = "vfx.shatter", value = "player" },
                new { kind = "Battle", targetId = "battle.wisp" },
                new { kind = "GiveMoney", targetId = "", value = "14" } } },
            new { id = "gone", conditions = new object[] { new { variableId = "flag.wisp_beaten", equalsValue = "true" } } } } });

        await Tool(server, "event.create", new { id = "event.warden", mapId = "map.vault", name = "Crystal Warden", kind = "Trigger", x = 10, y = 4, sprite = "sprite.warden", routineId = "guard" });
        await Tool(server, "event.set_pages", new { eventId = "event.warden", pages = new object[] {
            new { id = "fight", commands = new object[] {
                new { kind = "ShowEmote", targetId = "player", value = "!:1.2" },
                new { kind = "PanCamera", targetId = "event.warden", value = "1.0" },
                new { kind = "Wait", targetId = "", value = "0.5" },
                new { kind = "Dialogue", targetId = "dialogue.warden" },
                new { kind = "PanCamera", targetId = "player", value = "0.7" },
                new { kind = "Battle", targetId = "battle.warden" },
                new { kind = "ShowItemGet", targetId = "item.shard", value = "1" },
                new { kind = "GiveMoney", targetId = "", value = "45" },
                new { kind = "Dialogue", targetId = "dialogue.after" } } },
            new { id = "done", conditions = new object[] { new { variableId = "flag.warden_beaten", equalsValue = "true" } }, commands = new object[] {
                new { kind = "Dialogue", targetId = "dialogue.warden_done" } } } } });

        // --- El Libro Espejo: los dos pisos, contados como prosa ---
        await Tool(server, "story.book.set", new { title = "90s Demo", author = "SETO DEV", language = "en" });
        await Tool(server, "story.chapter.set", new { id = "chapter.tower", title = "Two floors of a hundred", order = 1 });
        await Tool(server, "story.scene.set", new { chapterId = "chapter.tower", id = "scene.gallery", title = "The rain got in first",
            synopsis = "Vera enters the flooded gallery, meets the only merchant who still comes down, and learns the tower is inhabited.",
            pov = "Vera / third limited", location = "The Flooded Gallery", time = "no hour indoors", status = "draft",
            prose = "The rain did not fall through the roof so much as live there. It had had a century to learn the shape of the room, and it had learned it well: the same three puddles, the same patient sound.\n\nPell had a landing, a crate and a business plan that fit in one sentence. Vera bought what she could carry and did not ask what he had seen come back down.",
            links = new object[] {
                new { kind = "map", id = "map.gallery", role = "setting" },
                new { kind = "event", id = "event.pell", role = "source" },
                new { kind = "dialogue", id = "dialogue.intro", role = "source" } } });
        await Tool(server, "story.scene.set", new { chapterId = "chapter.tower", id = "scene.vault", title = "Something that is light",
            synopsis = "At the top of the second floor the Warden explains the difference between carrying light and being it. Vera answers with a blade.",
            pov = "Vera / third limited", location = "The Crystal Vault", time = "after the climb", status = "draft",
            prose = "Up here the mist did not drift; it waited. The seams in the wall held a blue that had never been lit by anything.\n\nThe Warden spoke the way old things do, as if the sentence had been ready for a long time. Vera understood the argument and refused it anyway, and afterwards the tower kept humming, which she took to mean she had not put a light out. She had taken one.",
            links = new object[] {
                new { kind = "map", id = "map.vault", role = "setting" },
                new { kind = "event", id = "event.warden", role = "source" },
                new { kind = "dialogue", id = "dialogue.warden", role = "source" },
                new { kind = "battle", id = "battle.warden", role = "outcome" } } });

        await Tool(server, "project.set_info", new { id = "game.demo90", title = "90s Demo", startMapId = "map.gallery", startX = 10, startY = 13, playerSpriteId = "sprite.hero", startMoney = 40, partyActorIds = new[] { "actor.vera" }, language = "en" });

        var validation = await Tool(server, "project.validate", new { });
        var graph = await Tool(server, "query.content_graph", new { });
        var pack = await Tool(server, "project.build_pack", new { });
        server.StandardInput.Close();
        await server.WaitForExitAsync();
        // Resumen legible en vez de tres volcados de JSON: lo que importa es que valido, que
        // entro lo que se esperaba y donde quedo el pack. El detalle esta en el project.json.
        var g = Text(graph);
        Console.WriteLine($"Demo creada via MCP en: {root}");
        Console.WriteLine($"  Validacion: {Field(Text(validation), "data") ?? "ok"}");
        Console.WriteLine($"  Mapas: {Count(g, "maps")}  eventos: {EventCount(g)}  dialogos: {Count(g, "dialogues")}  combates: {Count(g, "battles")}");
        Console.WriteLine($"  Sprites: {Count(g, "sprites")}  vfx: {Count(g, "vfx")}  sfx: {Count(g, "sfx")}");
        Console.WriteLine($"  Pack: {Field(Text(pack), "data")}");
        return 0;
    }

    /// <summary>El MCP devuelve el payload como texto dentro de content[0]; aca solo se lee.</summary>
    static string Text(JsonNode? result) => result?["content"]?[0]?["text"]?.GetValue<string>() ?? "";
    static string? Field(string payload, string name)
    {
        try { return JsonNode.Parse(payload)?[name]?.ToString(); } catch { return null; }
    }
    static int Count(string payload, string collection)
    {
        try { return (JsonNode.Parse(payload)?["data"]?[collection] as JsonArray)?.Count ?? 0; } catch { return 0; }
    }

    /// <summary>Los eventos no son una coleccion al tope del grafo: cuelgan de cada mapa.</summary>
    static int EventCount(string payload)
    {
        try
        {
            var maps = JsonNode.Parse(payload)?["data"]?["maps"] as JsonArray;
            return maps?.Sum(m => (m?["events"] as JsonArray)?.Count ?? 0) ?? 0;
        }
        catch { return 0; }
    }

    static Task<JsonNode?> Tool(Process p, string name, object arguments) => Request(p, "tools/call", new { name, arguments });
    static async Task<JsonNode?> Request(Process p, string method, object parameters)
    {
        var request = JsonSerializer.Serialize(new { jsonrpc = "2.0", id = nextId++, method, @params = parameters });
        await p.StandardInput.WriteLineAsync(request);
        var line = await p.StandardOutput.ReadLineAsync() ?? throw new InvalidOperationException("El servidor MCP cerro la salida.");
        var response = JsonNode.Parse(line)!.AsObject();
        if (response["error"] is not null) throw new InvalidOperationException(response["error"]!.ToJsonString());
        var result = response["result"];
        if (result?["isError"]?.GetValue<bool>() == true) throw new InvalidOperationException(result.ToJsonString());
        return result;
    }
}
