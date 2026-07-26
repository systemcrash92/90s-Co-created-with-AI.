using System.Text;

namespace Seto90;

public static class ChiptuneSynth
{
    const int SampleRate = 44100;

    public static string WriteSongWav(SongDef song, string directory)
    {
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, song.Id.Replace('.', '_') + ".wav");
        try
        {
            using var stream = File.Create(path);
            WavWriter.Write(stream, Render(song));
            return path;
        }
        catch (IOException)
        {
            // El WAV esta bloqueado por OTRO proceso del motor: un juego vivo streameando la
            // misma cancion mientras una captura arranca (descubierto capturando un juego con
            // la sesion en vivo abierta). Cada proceso cae a su propia copia con sufijo de
            // PID: una captura jamas puede tirar al juego, ni al reves.
            var fallback = Path.Combine(directory, $"{song.Id.Replace('.', '_')}_{Environment.ProcessId}.wav");
            using var stream = File.Create(fallback);
            WavWriter.Write(stream, Render(song));
            return fallback;
        }
    }

    /// <summary>Renderiza la cancion como tracker: cada nota dura N pulsos ("C4:2"), cada canal
    /// tiene volumen, envolvente (attack/release en ms) y duty de pulso. El pulso es 60/tempo.</summary>
    public static short[] Render(SongDef song)
    {
        var pulseSeconds = 60.0 / Math.Max(1, song.Tempo);
        var totalPulses = Math.Max(1, song.Channels.Count == 0 ? 1 : song.Channels.Max(c => c.Notes.Sum(NoteLength)));
        var totalSamples = Math.Max(1, (int)(totalPulses * pulseSeconds * SampleRate));
        var mix = new double[totalSamples];
        var channelCount = Math.Max(1, song.Channels.Count);

        foreach (var channel in song.Channels)
        {
            var volume = Math.Clamp(channel.Volume, 0.0, 1.0) / channelCount;
            var attackSeconds = Math.Max(0, channel.AttackMs) / 1000.0;
            var releaseSeconds = Math.Max(0, channel.ReleaseMs) / 1000.0;
            var duty = Math.Clamp(channel.Duty, 0.05, 0.95);
            var pulseCursor = 0;
            foreach (var note in channel.Notes)
            {
                var length = NoteLength(note);
                var frequency = Frequency(NoteName(note));
                var start = (int)(pulseCursor * pulseSeconds * SampleRate);
                var end = Math.Min(totalSamples, (int)((pulseCursor + length) * pulseSeconds * SampleRate));
                pulseCursor += length;
                if (frequency <= 0) continue;
                var noteSeconds = (end - start) / (double)SampleRate;
                var attack = Math.Min(attackSeconds, noteSeconds * 0.5);
                var release = Math.Min(releaseSeconds, noteSeconds * 0.5);
                for (var i = start; i < end; i++)
                {
                    var t = (i - start) / (double)SampleRate;
                    var envelope = Math.Min(
                        attack <= 0 ? 1.0 : Math.Min(1.0, t / attack),
                        release <= 0 ? 1.0 : Math.Min(1.0, (noteSeconds - t) / release));
                    mix[i] += Wave(channel.Wave, frequency, t, duty) * envelope * volume;
                }
            }
        }

        var output = new short[totalSamples];
        for (var i = 0; i < output.Length; i++) output[i] = (short)Math.Clamp(mix[i] * 12000, short.MinValue, short.MaxValue);
        return output;
    }

    /// <summary>Pulsos que dura una nota: "C4" = 1, "C4:3" = 3. Minimo 1.</summary>
    public static int NoteLength(string note)
    {
        var colon = note.IndexOf(':');
        if (colon < 0) return 1;
        return int.TryParse(note[(colon + 1)..], out var parsed) ? Math.Max(1, parsed) : 1;
    }

    static string NoteName(string note)
    {
        var colon = note.IndexOf(':');
        return colon < 0 ? note : note[..colon];
    }

    static double Wave(string wave, double frequency, double t, double duty)
    {
        var phase = (t * frequency) % 1.0;
        return wave.ToLowerInvariant() switch
        {
            "triangle" => 4.0 * Math.Abs(phase - 0.5) - 1.0,
            "saw" or "sawtooth" => phase * 2.0 - 1.0,
            "noise" => DeterministicNoise((int)(t * frequency * 32.0)),
            _ => phase < duty ? 1.0 : -1.0
        };
    }

    static double Frequency(string note)
    {
        if (string.IsNullOrWhiteSpace(note) || note.Equals("R", StringComparison.OrdinalIgnoreCase)) return 0;
        var name = note.Trim().ToUpperInvariant();
        var semitone = name[0] switch
        {
            'C' => 0,
            'D' => 2,
            'E' => 4,
            'F' => 5,
            'G' => 7,
            'A' => 9,
            'B' => 11,
            _ => 0
        };
        var pos = 1;
        if (name.Length > pos && name[pos] == '#') { semitone++; pos++; }
        else if (name.Length > pos && name[pos] == 'B') { semitone--; pos++; }
        var octave = pos < name.Length && int.TryParse(name[pos..], out var parsed) ? parsed : 4;
        var midi = (octave + 1) * 12 + semitone;
        return 440.0 * Math.Pow(2.0, (midi - 69) / 12.0);
    }

    static double DeterministicNoise(int seed)
    {
        unchecked
        {
            var x = (uint)(seed * 747796405 + 2891336453);
            x = ((x >> ((int)(x >> 28) + 4)) ^ x) * 277803737;
            return ((x >> 22) ^ x) / (double)uint.MaxValue * 2.0 - 1.0;
        }
    }
}
