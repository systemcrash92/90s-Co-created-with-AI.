using System.Text.Json;

namespace Seto90;

/// <summary>
/// Preferencias del JUGADOR, no del juego: volumenes de musica y SFX. Por eso NO viven en
/// project.json (dato autorado) sino en un settings.json global junto a los saves. Musica a 0
/// = solo SFX. Tolerante a corrupcion: cualquier error vuelve a defaults sin crashear.
/// </summary>
public sealed class PlayerSettings
{
    public double MusicVolume { get; set; } = 1.0;
    public double SfxVolume { get; set; } = 1.0;

    static string FilePath => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Seto90", "settings.json");

    public static PlayerSettings Load()
    {
        try
        {
            if (File.Exists(FilePath))
            {
                var loaded = JsonSerializer.Deserialize<PlayerSettings>(File.ReadAllText(FilePath)) ?? new PlayerSettings();
                loaded.MusicVolume = Math.Clamp(loaded.MusicVolume, 0.0, 1.0);
                loaded.SfxVolume = Math.Clamp(loaded.SfxVolume, 0.0, 1.0);
                return loaded;
            }
        }
        catch { /* settings corruptos = defaults */ }
        return new PlayerSettings();
    }

    public void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch { /* disco lleno/permiso: las preferencias no valen un crash */ }
    }
}
