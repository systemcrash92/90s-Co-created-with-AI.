using Raylib_cs;

namespace Seto90;

public sealed class ChiptunePlayer : IDisposable
{
    GameProject project;
    readonly string cacheDirectory;
    Sound currentSound;
    bool hasSound;
    string currentSongId = "";

    ChiptunePlayer(GameProject project)
    {
        this.project = project;
        cacheDirectory = Path.Combine(Path.GetTempPath(), "Seto90", "Audio", project.Id);
    }

    public static ChiptunePlayer? TryStart(GameProject project, string songId)
    {
        try
        {
            if (!Raylib.IsAudioDeviceReady()) Raylib.InitAudioDevice();
            if (!Raylib.IsAudioDeviceReady()) return null;
            var player = new ChiptunePlayer(project);
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
        if (songId == currentSongId && hasSound) return;
        Stop();
        var song = project.Songs.FirstOrDefault(s => s.Id == songId);
        if (song == null) return;
        var path = ChiptuneSynth.WriteSongWav(song, cacheDirectory);
        currentSound = Raylib.LoadSound(path);
        Raylib.SetSoundVolume(currentSound, 0.35f);
        Raylib.PlaySound(currentSound);
        currentSongId = songId;
        hasSound = true;
    }

    /// <summary>Adopta el proyecto recargado en caliente (mismo contrato que MusicPlayer.Rebind).</summary>
    public void Rebind(GameProject fresh)
    {
        if (hasSound)
        {
            var old = project.Songs.FirstOrDefault(s => s.Id == currentSongId);
            var neu = fresh.Songs.FirstOrDefault(s => s.Id == currentSongId);
            if (neu == null || System.Text.Json.JsonSerializer.Serialize(neu) != System.Text.Json.JsonSerializer.Serialize(old))
                Stop();
        }
        project = fresh;
    }

    public void Update()
    {
        if (hasSound && !Raylib.IsSoundPlaying(currentSound)) Raylib.PlaySound(currentSound);
    }

    public void Stop()
    {
        if (!hasSound) return;
        Raylib.StopSound(currentSound);
        Raylib.UnloadSound(currentSound);
        hasSound = false;
        currentSongId = "";
    }

    public void Dispose() => Stop();
}
