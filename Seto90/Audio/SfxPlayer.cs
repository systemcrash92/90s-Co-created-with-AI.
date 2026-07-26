using Raylib_cs;

namespace Seto90;

/// <summary>
/// Reproductor de SFX: sintetiza cada efecto una vez en memoria (LoadWaveFromMemory, sin
/// archivos temporales) y los dispara por id. Los defaults del motor se cargan primero y
/// el proyecto los sobreescribe definiendo un SfxDef con el mismo id reservado.
/// Degrada a null si no hay dispositivo de audio (headless/CI): el juego suena mudo, no crashea.
/// </summary>
public sealed class SfxPlayer : IDisposable
{
    readonly Dictionary<string, Sound> sounds = [];

    SfxPlayer() { }

    public static SfxPlayer? TryCreate(GameProject project)
    {
        try
        {
            if (!Raylib.IsAudioDeviceReady()) Raylib.InitAudioDevice();
            if (!Raylib.IsAudioDeviceReady()) return null;
            var player = new SfxPlayer();
            var catalog = new Dictionary<string, SfxDef>();
            foreach (var def in DefaultAssets.DefaultSfx()) catalog[def.Id] = def;
            foreach (var def in project.Sfx) catalog[def.Id] = def; // el contenido pisa al motor
            foreach (var def in catalog.Values)
            {
                var wave = Raylib.LoadWaveFromMemory(".wav", SfxSynth.RenderWavBytes(def));
                player.sounds[def.Id] = Raylib.LoadSoundFromWave(wave);
                Raylib.UnloadWave(wave);
            }
            // El arranque del motor: si el proyecto no definio su propio sfx.boot, se
            // sintetiza el jingle embebido (acorde + campana, mas de lo que puede un SfxDef).
            if (!catalog.ContainsKey("sfx.boot"))
            {
                var wave = Raylib.LoadWaveFromMemory(".wav", WavWriter.ToBytes(ChiptuneSynth.Render(DefaultAssets.BootJingle())));
                player.sounds["sfx.boot"] = Raylib.LoadSoundFromWave(wave);
                Raylib.UnloadWave(wave);
            }
            return player;
        }
        catch
        {
            return null;
        }
    }

    double volume = 1.0;

    /// <summary>Volumen del jugador para todos los SFX (0..1).</summary>
    public void SetVolume(double value) => volume = Math.Clamp(value, 0.0, 1.0);

    public void Play(string id)
    {
        if (volume <= 0) return;
        if (!sounds.TryGetValue(id, out var sound)) return;
        Raylib.SetSoundVolume(sound, (float)volume);
        Raylib.PlaySound(sound);
    }

    public void Dispose()
    {
        foreach (var sound in sounds.Values) Raylib.UnloadSound(sound);
        sounds.Clear();
    }
}
