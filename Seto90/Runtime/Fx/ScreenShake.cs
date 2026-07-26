using Raylib_cs;

namespace Seto90;

/// <summary>
/// Screen shake determinista: offset entero pseudo-senoidal con amplitud decreciente.
///
/// Nota de diseno: en la SNES el "temblor" era escribir el registro de scroll con una tabla
/// corta; determinista por construccion. Aca igual: nada de Random — dos senos desfasados
/// muestreados por el tiempo restante, siempre reproducible frame a frame.
/// </summary>
public sealed class ScreenShake
{
    float timer;
    float duration = 1;
    float amplitude;

    public bool Active => timer > 0;

    public void Kick(float pixels, float seconds)
    {
        amplitude = pixels;
        duration = MathF.Max(0.05f, seconds);
        timer = duration;
    }

    public void Update(float dt)
    {
        if (timer > 0) timer -= dt;
    }

    public (int X, int Y) Offset
    {
        get
        {
            if (timer <= 0) return (0, 0);
            var falloff = timer / duration;
            var amp = amplitude * falloff;
            return ((int)(MathF.Sin(timer * 73f) * amp), (int)(MathF.Cos(timer * 57f) * amp * 0.6f));
        }
    }
}
