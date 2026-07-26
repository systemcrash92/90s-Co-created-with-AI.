namespace Seto90;

/// <summary>
/// Sintetizador de efectos de sonido: un SfxDef (onda + barrido de frecuencia + decaimiento)
/// se convierte en samples PCM. Es el modelo sfxr reducido a lo esencial de un JRPG.
///
/// Nota de diseno: los SFX de la era eran registros del chip de sonido escritos a mano
/// (frecuencia, envelope, ruido) — potentes pero inescribibles sin saber hardware. Aca son
/// 7 numeros con nombres honestos que una IA puede razonar: de 900Hz a 200Hz en 140ms con
/// decay 0.8 ES un golpe. La fase se integra muestra a muestra para que el barrido no meta clicks.
/// CPU puro: audio-smoke lo corre sin dispositivo de audio.
/// </summary>
public static class SfxSynth
{
    public static short[] Render(SfxDef sfx)
    {
        var total = Math.Max(1, WavWriter.SampleRate * sfx.DurationMs / 1000);
        var samples = new short[total];
        var phase = 0.0;
        for (var i = 0; i < total; i++)
        {
            var progress = i / (double)total;
            var frequency = sfx.StartFreq + (sfx.EndFreq - sfx.StartFreq) * progress;
            phase += frequency / WavWriter.SampleRate;
            var envelope = Math.Pow(1.0 - progress, 1.0 + sfx.Decay * 4.0);
            var value = Wave(sfx.Wave, phase % 1.0, sfx.Duty, i);
            samples[i] = (short)Math.Clamp(value * envelope * sfx.Volume * 24000, short.MinValue, short.MaxValue);
        }
        return samples;
    }

    public static byte[] RenderWavBytes(SfxDef sfx) => WavWriter.ToBytes(Render(sfx));

    static double Wave(string wave, double phase, double duty, int sampleIndex) => wave.ToLowerInvariant() switch
    {
        "triangle" => 4.0 * Math.Abs(phase - 0.5) - 1.0,
        "saw" or "sawtooth" => phase * 2.0 - 1.0,
        "noise" => Noise(sampleIndex / 40), // ruido retenido ~1kHz, sabor NES
        _ => phase < Math.Clamp(duty, 0.05, 0.95) ? 1.0 : -1.0
    };

    static double Noise(int seed)
    {
        unchecked
        {
            var x = (uint)(seed * 747796405 + 2891336453);
            x = ((x >> ((int)(x >> 28) + 4)) ^ x) * 277803737;
            return ((x >> 22) ^ x) / (double)uint.MaxValue * 2.0 - 1.0;
        }
    }
}
