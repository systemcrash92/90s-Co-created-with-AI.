using Raylib_cs;

namespace Seto90;

/// <summary>
/// Placa de arranque del motor, en el lenguaje de los logos retro-corporativos de los 80/90
/// (lenguaje de marca retro, wordmark sobre persiana): fondo negro, el wordmark "90s
/// ENGINE" en blanco bien ANCHO (los glifos se estiran horizontalmente, el bold de la era)
/// y las lineas azules horizontales pasando POR DELANTE del texto, como persiana. Coreografia
/// de encendido: linea celeste -> texto revelado del centro hacia afuera -> lineas deslizandose
/// encima, con el jingle de boot (sfx.boot) y scanlines suaves de tubo. Todo procedural con la
/// fuente embebida redibujada pixel a pixel — cero PNGs, cero shaders, salteable con Enter.
/// </summary>
public sealed class EngineSplash
{
    // Coreografia encadenada: linea -> persiana -> texto encima; luego sostiene y funde a negro.
    const float LineIn = 0.35f, BandsIn = 0.45f, TextIn = 0.5f;
    const float HoldEnd = 2.9f, FadeOut = 0.5f;
    float elapsed;

    public bool Done { get; private set; }

    public void Update(float dt)
    {
        elapsed += dt;
        // Enter saltea directo al fade de salida: nadie espera dos veces la misma placa.
        if (Raylib.IsKeyPressed(KeyboardKey.Enter) || Raylib.IsKeyPressed(KeyboardKey.Space))
            elapsed = Math.Max(elapsed, HoldEnd);
        if (elapsed >= HoldEnd + FadeOut) Done = true;
    }

    static float Ease(float t) => t <= 0 ? 0 : t >= 1 ? 1 : t * t * (3 - 2 * t); // smoothstep

    // Glifos de logo propios para "90s", disenio original estudiando la tipografia de
    // logos de la decada (Pricedown/SEGA/titulos SNES): trazo grueso de 3px, esquinas
    // redondeadas, ancho uniforme de 10. Pixel art nuevo, nada ripeado: licencia limpia.
    static readonly Dictionary<char, string[]> LogoGlyphs = new()
    {
        ['9'] =
        [
            "..######..",
            ".########.",
            "###....###",
            "###....###",
            "###....###",
            "###....###",
            ".#########",
            "..########",
            ".......###",
            ".......###",
            ".......###",
            ".......###",
            ".......###",
            ".......###",
        ],
        ['0'] =
        [
            "..######..",
            ".########.",
            "###....###",
            "###....###",
            "###....###",
            "###....###",
            "###....###",
            "###....###",
            "###....###",
            "###....###",
            "###....###",
            "###....###",
            ".########.",
            "..######..",
        ],
        // La s va en minuscula y a la base: "90s" es la decada, y en mayuscula se leia 905.
        ['s'] =
        [
            "..........",
            "..........",
            "..........",
            "..........",
            ".########.",
            "##########",
            "###.......",
            "###.......",
            ".########.",
            "......####",
            ".......###",
            "###....###",
            "##########",
            ".########.",
        ],
    };

    // La escalera de azules de las lineas, de cielo a profundo.
    static readonly Color[] Blues =
    [
        new(140, 210, 245, 255),
        new(90, 180, 230, 255),
        new(52, 130, 200, 255),
        new(28, 90, 170, 255),
        new(16, 58, 128, 255),
    ];

    public void Draw(PixelFont font, int width, int height)
    {
        Raylib.ClearBackground(Color.Black);
        var ox = width / 2;

        var lineT = Ease(elapsed / LineIn);
        var bandsT = Ease((elapsed - LineIn) / BandsIn);
        // El texto arranca cuando la persiana ya casi cerro: entra sobre escenario armado.
        var textT = Ease((elapsed - LineIn - BandsIn * 0.7f) / TextIn);

        // 0) El cielo de la decada: noche sintetizada estilo
        // outrun — estrellas que titilan, resplandor de horizonte, grilla en perspectiva que
        // corre hacia la camara y una estrella fugaz — encendiendose DETRAS de la coreografia.
        // Todo aditivo (la leccion de la placa: la luz nunca degrada el alfa del render texture).
        var skyT = Ease((elapsed - LineIn) / (BandsIn + 0.35f));
        if (skyT > 0f) DrawBackdrop(width, height, skyT);

        // 1) La linea de encendido crece desde el centro y se desvanece al entrar la persiana.
        if (bandsT < 1f)
        {
            var lw = (int)(56 * lineT);
            var glow = (int)(255 * (1f - bandsT));
            if (lw > 0 && glow > 0)
            {
                Raylib.BeginBlendMode(BlendMode.Additive);
                Raylib.DrawRectangle(ox - lw, 112, lw * 2, 1, new Color(90, 180, 230, glow));
                Raylib.EndBlendMode();
            }
        }

        // 2) La persiana de arriba: lineas anchas casi sin aire con degradado celeste,
        // deslizandose alternadas — el techo del logo, como las franjas del Discord retro.
        if (bandsT > 0f)
        {
            const int bandWidth = 76; // cortas: apenas asoman detras del simbolo
            var by = 82;
            for (var i = 0; i < 6; i++)
            {
                var slide = (int)((1f - bandsT) * (i % 2 == 0 ? -45 : 45));
                var blue = Blues[Math.Min(Blues.Length - 1, i * Blues.Length / 6)];
                var c = new Color(blue.R, blue.G, blue.B, (byte)(255 * bandsT));
                Raylib.DrawRectangle(ox - bandWidth / 2 + slide, by, bandWidth, 4, c);
                by += 6; // 4 de linea + 2 de aire: persiana cerrada
            }
        }

        // 3) La marca (simbolo montado sobre la persiana): "90s" blanco como simbolo
        // sobre las franjas, "ENGINE" grande como wordmark debajo, y su reflejo rayado
        // en degradado celeste por debajo. Sin "made with": eso lo dice el titulo.
        if (textT > 0f)
        {
            var logo = "90s";
            const int logoScale = 2;
            const int logoGap = 3;
            var lw = logo.Length * 10 * logoScale + (logo.Length - 1) * logoGap;
            var lx = (width - lw) / 2;
            const int ly = 84; // montado sobre la persiana, como el simbolo del reference
            for (var dy = -2; dy <= 2; dy += 2)
                for (var dx = -2; dx <= 2; dx += 2)
                    if (dx != 0 || dy != 0)
                        DrawLogoText(logo, lx + dx, ly + dy, logoScale, textT, Color.Black, logoGap);
            DrawLogoText(logo, lx, ly, logoScale, textT, Color.White, logoGap);

            var word = "ENGINE";
            const int engSx = 1, engSy = 1; // minimo: la firma bajo el simbolo
            const int spacing = 3;
            var ew = font.Measure(word) * engSx + (word.Length - 1) * spacing;
            var ex = (width - ew) / 2;
            const int ey = 118; // apenas debajo del simbolo
            for (var dy = -1; dy <= 1; dy += 2)
                for (var dx = -1; dx <= 1; dx += 2)
                    DrawReveal(font, word, ex + dx, ey + dy, engSx, engSy, textT, Color.Black, spacing);
            DrawReveal(font, word, ex, ey, engSx, engSy, textT, Color.White, spacing);

            // Firma del autor del MOTOR, y solo aca: la placa es el momento del motor. El pie del
            // titulo sigue diciendo unicamente "Made with 90s Engine", que es del juego de quien
            // lo haya hecho. En celeste apagado y sin contorno para que sea firma, no wordmark.
            var site = "seto.dev";
            var sw = font.Measure(site);
            var sx = (width - sw) / 2;
            DrawReveal(font, site, sx, ey + 12, 1, 1, textT, new Color((byte)120, (byte)185, (byte)225, (byte)255), 0);
        }

        // El tubo de la tele vieja: scanlines multiplicativas suaves de la propia placa
        // (no tocan el canal alfa del render texture: el negro queda negro).
        Raylib.BeginBlendMode(BlendMode.Multiplied);
        for (var sy = 0; sy < height; sy += 2)
            Raylib.DrawRectangle(0, sy, width, 1, new Color(198, 203, 212, 255));
        Raylib.EndBlendMode();

        // Fundido de salida sobre todo lo dibujado (la entrada ES la coreografia).
        if (elapsed > HoldEnd)
        {
            var shade = (int)(255 * Math.Min(1f, (elapsed - HoldEnd) / FadeOut));
            if (shade > 0) Raylib.DrawRectangle(0, 0, width, height, new Color(0, 0, 0, shade));
        }
    }

    /// <summary>El cielo synthwave detras de la insignia: 46 estrellas deterministas
    /// (VfxEval.Hash01, el azar del motor), horizonte con resplandor, piso de grilla en
    /// perspectiva desplazandose hacia la camara y UNA estrella fugaz durante el sostenido.
    /// Los tonos son la misma escalera de azules de la persiana: el fondo pertenece al logo.</summary>
    void DrawBackdrop(int width, int height, float t)
    {
        const int horizon = 170; // debajo de "ENGINE" (y=118): la insignia flota sobre el piso
        Raylib.BeginBlendMode(BlendMode.Additive);

        // Estrellas: posicion por hash (mismo cielo en cada arranque), titileo lento propio.
        for (var i = 0; i < 46; i++)
        {
            var sx = (int)(VfxEval.Hash01(i, 11) * width);
            var sy = (int)(VfxEval.Hash01(i, 23) * (horizon - 22));
            var twinkle = 0.55f + 0.45f * MathF.Sin(elapsed * (1.2f + (float)VfxEval.Hash01(i, 37) * 2.6f) + i);
            var a = (byte)(160 * t * twinkle);
            Raylib.DrawRectangle(sx, sy, 1, 1, new Color((byte)140, (byte)210, (byte)245, a));
            if (VfxEval.Hash01(i, 51) > 0.8) // las brillantes ganan una cruz de difraccion
            {
                var soft = new Color((byte)90, (byte)180, (byte)230, (byte)(a / 2));
                Raylib.DrawRectangle(sx - 1, sy, 1, 1, soft);
                Raylib.DrawRectangle(sx + 1, sy, 1, 1, soft);
                Raylib.DrawRectangle(sx, sy - 1, 1, 1, soft);
                Raylib.DrawRectangle(sx, sy + 1, 1, 1, soft);
            }
        }

        // Estrella fugaz: una sola, cruzando alto durante el sostenido (deterministica).
        var shootT = (elapsed - 1.7f) / 0.55f;
        if (shootT is > 0f and < 1f)
        {
            var hx = width * 0.78f - shootT * 96f;
            var hy = 22f + shootT * 30f;
            var fade = (byte)(220 * t * MathF.Sin(shootT * MathF.PI));
            for (var k = 0; k < 9; k++) // la cola se apaga hacia atras
                Raylib.DrawRectangle((int)(hx + k * 2), (int)(hy - k * 0.65f), 2, 1,
                    new Color((byte)180, (byte)225, (byte)250, (byte)(fade * (9 - k) / 9)));
        }

        // Resplandor del horizonte: la promesa de que hay algo del otro lado.
        for (var i = 0; i < 12; i++)
            Raylib.DrawRectangle(0, horizon - i, width, 1, new Color((byte)28, (byte)90, (byte)170, (byte)(34 * t * (12 - i) / 12f)));
        Raylib.DrawRectangle(0, horizon, width, 1, new Color((byte)90, (byte)180, (byte)230, (byte)(120 * t)));

        // Piso: grilla en perspectiva. Horizontales con espaciado cuadratico (densas en el
        // horizonte) que corren hacia la camara; verticales en abanico desde el punto de fuga.
        var grid = new Color((byte)28, (byte)90, (byte)170, (byte)(95 * t));
        var scroll = elapsed * 0.9f;
        for (var k = 0; k < 12; k++)
        {
            var f = (k + scroll % 1f) / 12f;
            var y = horizon + 1 + (int)((height - horizon) * f * f);
            if (y < height) Raylib.DrawRectangle(0, y, width, 1, grid);
        }
        var ox = width / 2;
        for (var m = -7; m <= 7; m++)
            Raylib.DrawLine(ox + m * 5, horizon, ox + m * 46, height + 8, grid);

        Raylib.EndBlendMode();
    }

    /// <summary>El simbolo "90s" con los glifos de logo propios, revelado del centro vertical
    /// hacia afuera (misma ventana que DrawReveal pero leyendo de LogoGlyphs).</summary>
    static void DrawLogoText(string text, int x, int y, int scale, float t, Color color, int gap)
    {
        var half = LogoGlyphs['0'].Length * scale / 2f;
        var window = half * Math.Min(1f, t);
        var cx = x;
        foreach (var ch in text)
        {
            var rows = LogoGlyphs[ch];
            for (var gy = 0; gy < rows.Length; gy++)
                for (var i = 0; i < scale; i++)
                {
                    var row = gy * scale + i;
                    if (Math.Abs(row + 0.5f - half) > window) continue;
                    for (var gx = 0; gx < rows[gy].Length; gx++)
                        if (rows[gy][gx] == '#')
                            Raylib.DrawRectangle(cx + gx * scale, y + row, scale, 1, color);
                }
            cx += rows[0].Length * scale + gap;
        }
    }

    /// <summary>Texto redibujado pixel a pixel desde la fuente CPU con escala horizontal y
    /// vertical independientes (letras anchas), mostrando solo las filas dentro de la ventana
    /// de revelado que crece del centro vertical hacia afuera.</summary>
    static void DrawReveal(PixelFont font, string text, int x, int y, int scaleX, int scaleY, float t, Color color, int letterSpacing = 0)
    {
        var half = PixelFont.GlyphHeight * scaleY / 2f;
        var window = half * Math.Min(1f, t);
        var cx = x;
        foreach (var ch in text)
        {
            var w = font.WidthOf(ch);
            for (var gy = 0; gy < PixelFont.GlyphHeight; gy++)
                for (var i = 0; i < scaleY; i++)
                {
                    var row = gy * scaleY + i;
                    if (Math.Abs(row + 0.5f - half) > window) continue;
                    for (var gx = 0; gx < w; gx++)
                        if (font.PixelOn(ch, gx, gy))
                            Raylib.DrawRectangle(cx + gx * scaleX, y + row, scaleX, 1, color);
                }
            cx += (w + 1) * scaleX + letterSpacing;
        }
    }
}
