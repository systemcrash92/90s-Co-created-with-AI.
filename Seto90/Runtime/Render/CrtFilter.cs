using Raylib_cs;

namespace Seto90;

/// <summary>
/// Filtro CRT de tubo: curvatura de vidrio, scanlines, triadas de fosforo, vineta y
/// desconvergencia RGB sutil, todo en un unico fragment shader sobre el frame ya escalado.
///
/// Nota de diseno: los JRPGs 90s se dibujaban PARA televisores de tubo; el artista contaba con
/// que el fosforo fundiera los dithering y las scanlines dieran textura. El pixel "puro" en un
/// LCD moderno es mas nitido que lo que esos artistas vieron jamas. Este filtro devuelve el
/// vidrio: activado por defecto (render.crtFilter), F2 lo alterna en vivo, y las capturas de
/// autoria siguen siendo el lienzo limpio de 256x224 salvo que se pida el vidrio con --crt.
/// Si el driver no compila el shader, el motor sigue con pixeles puros sin quejarse.
/// </summary>
public sealed class CrtFilter : IDisposable
{
    Shader shader;
    int locResolution;

    public bool Enabled { get; set; }
    public bool Ready { get; private set; }
    public bool Active => Enabled && Ready;

    const string VertexShader = """
        #version 330
        in vec3 vertexPosition;
        in vec2 vertexTexCoord;
        in vec4 vertexColor;
        uniform mat4 mvp;
        out vec2 fragTexCoord;
        out vec4 fragColor;
        void main()
        {
            fragTexCoord = vertexTexCoord;
            fragColor = vertexColor;
            gl_Position = mvp * vec4(vertexPosition, 1.0);
        }
        """;

    const string FragmentShader = """
        #version 330
        in vec2 fragTexCoord;
        in vec4 fragColor;
        uniform sampler2D texture0;
        uniform vec4 colDiffuse;
        uniform vec2 resolution;
        out vec4 finalColor;

        // Curvatura del vidrio: cada eje se comba segun el cuadrado del otro (pincushion clasico).
        vec2 curve(vec2 uv)
        {
            uv = uv * 2.0 - 1.0;
            uv += uv * (uv.yx * uv.yx) * 0.045;
            return uv * 0.5 + 0.5;
        }

        // Muestreo pixel-perfect con antialias subpixel: nitido como Point, sin escalera al curvar.
        vec2 sharpUv(vec2 uv)
        {
            vec2 pix = uv * resolution + 0.5;
            vec2 fl = floor(pix);
            vec2 aa = max(fwidth(pix) * 0.75, vec2(1e-4));
            vec2 fr = smoothstep(0.5 - aa, 0.5 + aa, fract(pix));
            return (fl + fr - 0.5) / resolution;
        }

        void main()
        {
            vec2 uv = curve(fragTexCoord);
            if (uv.x < 0.0 || uv.x > 1.0 || uv.y < 0.0 || uv.y > 1.0)
            {
                finalColor = vec4(0.0, 0.0, 0.0, 1.0);
                return;
            }

            // Desconvergencia RGB sutil: los tres canones nunca apuntaban perfecto.
            vec2 suv = sharpUv(uv);
            vec3 col;
            col.r = texture(texture0, suv + vec2(0.0007, 0.0)).r;
            col.g = texture(texture0, suv).g;
            col.b = texture(texture0, suv - vec2(0.0007, 0.0)).b;

            // Scanlines: cada fila del lienzo brilla en el centro y se apaga hacia el borde.
            float row = fract(uv.y * resolution.y);
            col *= 0.86 + 0.14 * cos((row - 0.5) * 6.28318);

            // Triadas de fosforo verticales (mascara de apertura grille).
            float m = mod(gl_FragCoord.x, 3.0);
            col *= vec3(m < 1.0 ? 1.06 : 0.96,
                        m >= 1.0 && m < 2.0 ? 1.06 : 0.96,
                        m >= 2.0 ? 1.06 : 0.96);

            // Vineta suave + borde del vidrio fundido a negro.
            vec2 c = fragTexCoord * 2.0 - 1.0;
            col *= 1.0 - 0.09 * dot(c, c);
            float edge = min(min(uv.x, 1.0 - uv.x), min(uv.y, 1.0 - uv.y));
            col *= smoothstep(0.0, 0.012, edge);

            col *= 1.12; // compensa el brillo que roban scanlines y mascara

            finalColor = vec4(col, 1.0) * colDiffuse * fragColor;
        }
        """;

    public static CrtFilter Create(bool enabled)
    {
        var filter = new CrtFilter { Enabled = enabled };
        try
        {
            filter.shader = Raylib.LoadShaderFromMemory(VertexShader, FragmentShader);
            filter.Ready = filter.shader.Id != 0;
            if (filter.Ready) filter.locResolution = Raylib.GetShaderLocation(filter.shader, "resolution");
        }
        catch
        {
            filter.Ready = false;
        }
        return filter;
    }

    /// <summary>Activa el shader para el proximo DrawTexturePro. Cerrar siempre con End().</summary>
    public void Begin(int virtualWidth, int virtualHeight)
    {
        Raylib.SetShaderValue(shader, locResolution, new System.Numerics.Vector2(virtualWidth, virtualHeight), ShaderUniformDataType.Vec2);
        Raylib.BeginShaderMode(shader);
    }

    public void End() => Raylib.EndShaderMode();

    public void Dispose()
    {
        if (Ready) Raylib.UnloadShader(shader);
        Ready = false;
    }
}
