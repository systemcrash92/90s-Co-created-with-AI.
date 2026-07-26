using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Seto90;

public sealed record AssetIssue(string Code, string Message, string Fix);

public sealed class AssetReport
{
    public List<AssetIssue> Issues { get; } = [];
    public int PaletteColors { get; set; }
    public int TileCount { get; set; }
    public int ReferencedTileCount { get; set; }
    public bool Ok => Issues.Count == 0;

    public string ToHumanText()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"Assets: colors={PaletteColors}, tiles={TileCount}, referencedTiles={ReferencedTileCount}");
        if (Ok) sb.AppendLine("Restricciones retro OK.");
        else foreach (var issue in Issues) sb.AppendLine($"- {issue.Code}: {issue.Message} Fix: {issue.Fix}");
        return sb.ToString();
    }
}

public static class AssetPipeline
{
    static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);

    public static AssetReport Validate(GameProject project, string? root = null)
    {
        var report = new AssetReport();
        ValidateRender(project, report);
        ValidateTiles(project, report);
        ValidatePalette(project, report);
        ValidateImages(project, root, report);
        return report;
    }

    /// <summary>Con un root de proyecto, verifica que cada PNG referenciado exista en disco (o venga embebido en el pack).</summary>
    static void ValidateImages(GameProject project, string? root, AssetReport report)
    {
        var referenced = project.Sprites.Select(s => s.Image)
            .Concat(project.Fonts.Select(f => f.Image))
            .Concat(project.Tilesets.Select(t => t.Image).Where(i => i.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
            .Append(project.Render.TitleImage)
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct();
        foreach (var image in referenced)
        {
            if (project.EmbeddedFiles.ContainsKey(image)) continue;
            if (root != null && !File.Exists(Path.Combine(root, image)))
            {
                report.Issues.Add(new("missing_png", $"Imagen referenciada '{image}' no existe en el proyecto.", "Copiar el PNG a esa ruta relativa o corregir la referencia."));
            }
        }

        // Atlas de tiles: dimensiones multiplo del tileSize y celdas suficientes para el tile mas alto.
        foreach (var tileset in project.Tilesets.Where(t => t.Image.EndsWith(".png", StringComparison.OrdinalIgnoreCase)))
        {
            var bytes = ImageAssets.LoadBytes(project, root, tileset.Image);
            if (bytes == null) continue; // ya reportado como missing_png si corresponde
            var image = Raylib_cs.Raylib.LoadImageFromMemory(".png", bytes);
            if (image.Width <= 0) { report.Issues.Add(new("bad_atlas", $"El atlas '{tileset.Image}' no es un PNG legible.", "Reexportar el PNG.")); continue; }
            try
            {
                if (image.Width % tileset.TileSize != 0 || image.Height % tileset.TileSize != 0)
                    report.Issues.Add(new("bad_atlas_size", $"Atlas de {tileset.Id}: {image.Width}x{image.Height} no es multiplo de {tileset.TileSize}.", "Exportar el atlas en celdas exactas de tileSize."));
                var cells = image.Width / tileset.TileSize * (image.Height / tileset.TileSize);
                // Los frames de animacion tambien son celdas del atlas: cuentan para el maximo.
                var maxTile = tileset.Tiles.Count == 0 ? -1 : tileset.Tiles.Max(t => Math.Max(t.Id, t.Frames.Count == 0 ? -1 : t.Frames.Max()));
                if (maxTile >= cells)
                    report.Issues.Add(new("atlas_too_small", $"Atlas de {tileset.Id} tiene {cells} celdas pero el tile/frame {maxTile} necesita {maxTile + 1}.", "Agregar celdas al atlas o bajar los ids/frames de tiles."));
            }
            finally { Raylib_cs.Raylib.UnloadImage(image); }
        }
    }

    public static string WriteManifest(GameProject project, string root)
    {
        var report = Validate(project, root);
        var build = Path.Combine(Path.GetFullPath(root), "build");
        Directory.CreateDirectory(build);
        var path = Path.Combine(build, "assets.manifest.json");
        var manifest = new
        {
            project = project.Id,
            generatedAt = DateTimeOffset.UtcNow,
            render = project.Render,
            report = new { report.Ok, report.PaletteColors, report.TileCount, report.ReferencedTileCount, issues = report.Issues },
            tilesets = project.Tilesets.Select(t => new { t.Id, t.Image, t.TileSize, tiles = t.Tiles.Count, colors = t.Tiles.Select(x => x.Color).Distinct().Count() }),
            maps = project.Maps.Select(m => new { m.Id, m.Width, m.Height, m.TilesetId, uniqueTiles = m.Tiles.Distinct().Count() }),
            songs = project.Songs.Select(s => new { s.Id, s.Tempo, channels = s.Channels.Count, notes = s.Channels.Sum(c => c.Notes.Count) }),
            sprites = project.Sprites.Select(s => new { s.Id, s.Width, s.Height, source = string.IsNullOrWhiteSpace(s.Image) ? "procedural" : s.Image, poses = s.Poses.Count, frames = s.Poses.Sum(x => x.Frames.Count), colors = s.Palette.Count }),
            uiThemes = project.UiThemes.Select(t => new { t.Id, t.Style, t.TextSpeedCps, font = string.IsNullOrWhiteSpace(t.FontId) ? "embebida" : t.FontId }),
            sfx = project.Sfx.Select(s => new { s.Id, s.Wave, s.DurationMs }),
            fonts = project.Fonts.Select(f => new { f.Id, f.Image, f.GlyphWidth, f.GlyphHeight, glyphs = f.Charset.Length })
        };
        // Sin BOM: un JSON con BOM adelante lo rechazan varios parsers (la misma trampa que el
        // pipe del MCP). El manifiesto esta para que otras herramientas lo lean.
        File.WriteAllText(path, JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), new UTF8Encoding(false));
        return path;
    }

    static void ValidateRender(GameProject project, AssetReport report)
    {
        if (project.Render.VirtualWidth != 256 || project.Render.VirtualHeight != 224)
        {
            report.Issues.Add(new("non_retro_resolution", $"Resolucion virtual {project.Render.VirtualWidth}x{project.Render.VirtualHeight}.", "Usar 256x224 para modo SNES-like o justificar otra restriccion."));
        }
        if (project.Render.TileSize is not (8 or 16 or 32))
        {
            report.Issues.Add(new("bad_tile_size", $"Tile size global {project.Render.TileSize}.", "Usar 8, 16 o 32."));
        }
        if (!project.Render.IntegerScale)
        {
            report.Issues.Add(new("non_integer_scale", "integerScale esta desactivado.", "Activar integerScale para pixel art limpio."));
        }
    }

    static void ValidateTiles(GameProject project, AssetReport report)
    {
        var allTiles = project.Tilesets.Sum(t => t.Tiles.Count);
        report.TileCount = allTiles;
        foreach (var tileset in project.Tilesets)
        {
            if (tileset.TileSize != project.Render.TileSize)
            {
                report.Issues.Add(new("tileset_size_mismatch", $"Tileset {tileset.Id} usa {tileset.TileSize}, render usa {project.Render.TileSize}.", "Unificar tileSize."));
            }
            foreach (var duplicate in tileset.Tiles.GroupBy(t => t.Id).Where(g => g.Count() > 1).Select(g => g.Key))
            {
                report.Issues.Add(new("duplicate_tile", $"Tileset {tileset.Id} repite tile {duplicate}.", "Renombrar tile id dentro del tileset."));
            }
        }

        foreach (var map in project.Maps)
        {
            var tileset = project.Tilesets.FirstOrDefault(t => t.Id == map.TilesetId);
            if (tileset == null) continue;
            var valid = tileset.Tiles.Select(t => t.Id).ToHashSet();
            foreach (var bad in map.Tiles.Where(tile => !valid.Contains(tile)).Distinct())
            {
                report.Issues.Add(new("missing_tile", $"Mapa {map.Id} usa tile {bad}, ausente en {tileset.Id}.", "Crear el tile o repintar el mapa."));
            }
            report.ReferencedTileCount += map.Tiles.Distinct().Count();
        }
    }

    static void ValidatePalette(GameProject project, AssetReport report)
    {
        // Los colores de sprites procedurales cuentan contra el limite global de paleta, como en el hardware real.
        var colors = project.Tilesets.SelectMany(t => t.Tiles).Select(t => t.Color)
            .Concat(project.Sprites.SelectMany(s => s.Palette))
            .Where(c => !string.IsNullOrWhiteSpace(c)).ToList();
        report.PaletteColors = colors.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        foreach (var color in colors.Where(c => !HexColor.IsMatch(c)).Distinct())
        {
            report.Issues.Add(new("bad_color", $"Color invalido {color}.", "Usar #RRGGBB."));
        }
        if (project.Render.LimitPalette && report.PaletteColors > project.Render.MaxColors)
        {
            report.Issues.Add(new("palette_overflow", $"Paleta usa {report.PaletteColors} colores, maximo {project.Render.MaxColors}.", "Reducir colores o subir maxColors explicitamente."));
        }
    }
}
