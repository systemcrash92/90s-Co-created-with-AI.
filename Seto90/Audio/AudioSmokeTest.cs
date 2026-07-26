using System.Text;

namespace Seto90;

public sealed class AudioSmokeTest
{
    readonly GameProject project;

    public AudioSmokeTest(GameProject project) => this.project = project;

    public string Run()
    {
        var sb = new StringBuilder();
        var dir = Path.Combine(Path.GetTempPath(), "Seto90", "AudioSmoke", project.Id);
        foreach (var song in project.Songs)
        {
            var samples = ChiptuneSynth.Render(song);
            var path = ChiptuneSynth.WriteSongWav(song, dir);
            var duration = samples.Length / 44100.0;
            sb.AppendLine($"{song.Id}: samples={samples.Length} duration={duration:0.00}s wav={path} bytes={new FileInfo(path).Length}");
        }
        if (project.Songs.Count == 0) sb.AppendLine("Sin canciones.");

        // Tracker sintetico (no depende del contenido): duraciones por nota, volumen y silencio exactos.
        // 4 pulsos a 120 BPM = 2.00s = 88200 samples; el canal mudo no debe aportar señal.
        var tracker = new SongDef
        {
            Id = "song.smoke_tracker",
            Tempo = 120,
            Channels =
            [
                new SongChannel { Wave = "square", Notes = ["C4:2", "R", "E4"], Duty = 0.25, ReleaseMs = 40 },
                new SongChannel { Wave = "triangle", Notes = ["C2:4"], Volume = 0.0 },
            ],
        };
        var trackerSamples = ChiptuneSynth.Render(tracker);
        if (trackerSamples.Length != 88200)
            throw new InvalidOperationException($"Tracker: esperaba 88200 samples (4 pulsos a 120), rendereo {trackerSamples.Length}.");
        // El tercer pulso es silencio ('R'): el medio de ese pulso debe ser 0 (el canal 2 esta a volumen 0).
        var silentIndex = (int)(2.5 * 0.5 * 44100);
        if (trackerSamples[silentIndex] != 0)
            throw new InvalidOperationException($"Tracker: el silencio del pulso 3 suena ({trackerSamples[silentIndex]}).");
        var trackerPeak = trackerSamples.Max(s => Math.Abs((int)s));
        if (trackerPeak == 0) throw new InvalidOperationException("Tracker: renderizo silencio total.");
        sb.AppendLine($"tracker sintetico: 88200 samples OK, silencio en R OK, canal mudo OK, pico {trackerPeak}.");

        // Jingle de boot de la placa del motor (sfx.boot): 8 pulsos a 240 BPM = 2.00s de acorde
        // que se hincha + campana; debe renderizar señal real, es lo primero que oye el jugador.
        var boot = ChiptuneSynth.Render(DefaultAssets.BootJingle());
        if (boot.Length != 88200)
            throw new InvalidOperationException($"Boot jingle: esperaba 88200 samples (8 pulsos a 240), rendereo {boot.Length}.");
        var bootPeak = boot.Max(s => Math.Abs((int)s));
        if (bootPeak == 0) throw new InvalidOperationException("Boot jingle: renderizo silencio total.");
        sb.AppendLine($"boot jingle (sfx.boot): 88200 samples OK, pico {bootPeak}.");

        // SFX: defaults del motor + los del proyecto (que pisan por id), renderizados 100% en CPU.
        var catalog = new Dictionary<string, SfxDef>();
        foreach (var def in DefaultAssets.DefaultSfx()) catalog[def.Id] = def;
        foreach (var def in project.Sfx) catalog[def.Id] = def;
        foreach (var def in catalog.Values)
        {
            var samples = SfxSynth.Render(def);
            var peak = samples.Max(s => Math.Abs((int)s));
            if (samples.Length == 0 || peak == 0) throw new InvalidOperationException($"SFX {def.Id} renderizo silencio.");
            var origen = project.Sfx.Any(x => x.Id == def.Id) ? "proyecto" : "motor";
            sb.AppendLine($"sfx {def.Id} [{origen}]: {samples.Length} samples, pico {peak}.");
        }
        return sb.ToString();
    }
}
