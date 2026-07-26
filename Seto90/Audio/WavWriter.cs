using System.Text;

namespace Seto90;

/// <summary>
/// Escritor WAV PCM 16-bit mono compartido por musica (ChiptuneSynth) y efectos (SfxSynth).
///
/// Nota de diseno: el SPC700 de la SNES recibia samples BRR por DMA; nuestro equivalente
/// domestico es PCM crudo con cabecera RIFF. Un solo lugar sabe escribir la cabecera:
/// la musica lo usa hacia archivo (cache en temp) y los SFX hacia memoria (sin archivos).
/// </summary>
public static class WavWriter
{
    public const int SampleRate = 44100;

    public static void Write(Stream stream, short[] samples)
    {
        using var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: true);
        var dataBytes = samples.Length * sizeof(short);
        writer.Write(Encoding.ASCII.GetBytes("RIFF"));
        writer.Write(36 + dataBytes);
        writer.Write(Encoding.ASCII.GetBytes("WAVE"));
        writer.Write(Encoding.ASCII.GetBytes("fmt "));
        writer.Write(16);
        writer.Write((short)1);
        writer.Write((short)1);
        writer.Write(SampleRate);
        writer.Write(SampleRate * sizeof(short));
        writer.Write((short)sizeof(short));
        writer.Write((short)16);
        writer.Write(Encoding.ASCII.GetBytes("data"));
        writer.Write(dataBytes);
        foreach (var sample in samples) writer.Write(sample);
    }

    public static byte[] ToBytes(short[] samples)
    {
        using var stream = new MemoryStream();
        Write(stream, samples);
        return stream.ToArray();
    }
}
