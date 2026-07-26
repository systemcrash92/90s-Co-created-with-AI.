namespace Seto90;

/// <summary>
/// Numero rodante tipo odometro: el valor mostrado persigue al real a velocidad
/// constante, dando esos segundos de tension para curarte antes de que el contador llegue a 0.
///
/// Nota de diseno: el HP rodante cambia la mecanica del combate — un dano mortal no es
/// instantaneo, es una cuenta regresiva. El estado visual (rodado) vive separado
/// del estado logico (TurnBattleSession), que no se entera de nada.
/// </summary>
public sealed class RollingNumber(float unitsPerSecond = 24f)
{
    float current;
    bool initialized;

    public int Value => (int)MathF.Round(current);
    public bool Rolling { get; private set; }

    public void Snap(int value)
    {
        current = value;
        initialized = true;
        Rolling = false;
    }

    public void Update(int target, float dt)
    {
        if (!initialized) { Snap(target); return; }
        if (Math.Abs(current - target) < 0.01f) { Rolling = false; return; }
        Rolling = true;
        var step = unitsPerSecond * dt;
        current = current < target ? MathF.Min(target, current + step) : MathF.Max(target, current - step);
    }
}
