using Raylib_cs;

namespace Seto90;

/// <summary>
/// Musica de fondo por streaming: el WAV sintetizado se cachea en temp y raylib lo streamea
/// con loop nativo sample-exacto (adios al gap del re-disparo de Sound).
///
/// Nota de diseno: en la SNES la musica loopeaba porque el driver saltaba a un punto del
/// pattern; el equivalente correcto en PC es Music (streaming + Looping), no Sound (one-shot).
/// ChiptunePlayer queda como fallback si LoadMusicStream fallara en algun entorno.
/// </summary>
public sealed class MusicPlayer : IDisposable
{
    GameProject project;
    readonly string cacheDirectory;
    Music music;
    bool hasMusic;
    string currentSongId = "";
    double volume = 1.0;
    const float BaseGain = 0.35f; // nivel de mezcla del motor; el volumen del jugador multiplica

    MusicPlayer(GameProject project)
    {
        this.project = project;
        cacheDirectory = Path.Combine(Path.GetTempPath(), "Seto90", "Audio", project.Id);
    }

    public static MusicPlayer? TryStart(GameProject project, string songId)
    {
        try
        {
            if (!Raylib.IsAudioDeviceReady()) Raylib.InitAudioDevice();
            if (!Raylib.IsAudioDeviceReady()) return null;
            var player = new MusicPlayer(project);
            if (!string.IsNullOrWhiteSpace(songId)) player.Play(songId);
            return player;
        }
        catch
        {
            return null;
        }
    }

    public void Play(string songId)
    {
        if (songId == currentSongId && hasMusic) return;
        Stop();
        var song = project.Songs.FirstOrDefault(s => s.Id == songId);
        if (song == null) return;
        var path = ChiptuneSynth.WriteSongWav(song, cacheDirectory);
        music = Raylib.LoadMusicStream(path);
        if (music.FrameCount == 0) return; // carga fallida: quedarse mudo antes que crashear
        music.Looping = true;
        Raylib.SetMusicVolume(music, (float)(BaseGain * volume));
        Raylib.PlayMusicStream(music);
        currentSongId = songId;
        hasMusic = true;
    }

    /// <summary>Adopta el proyecto recargado en caliente. Sin esto, el reproductor buscaba
    /// canciones en el proyecto VIEJO y una cancion creada en vivo jamas sonaba (bug del
    /// primer mapa del juego). Si la cancion actual cambio o desaparecio, corta — el
    /// proximo Play re-renderiza; si sigue igual, no interrumpe el loop.</summary>
    public void Rebind(GameProject fresh)
    {
        if (hasMusic)
        {
            var old = project.Songs.FirstOrDefault(s => s.Id == currentSongId);
            var neu = fresh.Songs.FirstOrDefault(s => s.Id == currentSongId);
            if (neu == null || System.Text.Json.JsonSerializer.Serialize(neu) != System.Text.Json.JsonSerializer.Serialize(old))
                Stop();
        }
        project = fresh;
    }

    /// <summary>Volumen del jugador (0..1); 0 = musica apagada de hecho, quedan solo los SFX.</summary>
    public void SetVolume(double value)
    {
        volume = Math.Clamp(value, 0.0, 1.0);
        if (hasMusic) Raylib.SetMusicVolume(music, (float)(BaseGain * volume));
    }

    /// <summary>Llamar cada frame: alimenta el buffer de streaming.</summary>
    public void Update()
    {
        if (hasMusic) Raylib.UpdateMusicStream(music);
    }

    public void Stop()
    {
        if (!hasMusic) return;
        Raylib.StopMusicStream(music);
        Raylib.UnloadMusicStream(music);
        hasMusic = false;
        currentSongId = "";
    }

    public void Dispose() => Stop();
}
