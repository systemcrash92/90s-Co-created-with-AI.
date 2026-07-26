using System.Text.Json.Serialization;

namespace Seto90;

public static class DesignNotes
{
    public const string Core = "En los JRPGs 90s el cartucho era motor y base de datos: veloz, pero opaco. Seto90 conserva tablas, tiles y restricciones; cambia punteros por IDs, ROM por JSON y scripts manuales por MCP validado.";
}

public sealed class GameProject
{
    public string Id { get; set; } = "game.demo";
    public string Title { get; set; } = "Seto90 Demo";
    /// <summary>Contador de guardados, monotono en disco (ProjectStore.Save lo incrementa).
    /// Es la guardia de concurrencia entre procesos co-autores (editor en vivo + MCP): antes de
    /// escribir, una sesion con revision vieja ADOPTA el disco y aplica su cambio encima (rebase),
    /// y deshacer con un cambio externo sin adoptar se rechaza. Proyectos viejos cargan con 0.</summary>
    public int Revision { get; set; }
    public string StartMapId { get; set; } = "";
    public string StartEventId { get; set; } = "";
    /// <summary>Posicion inicial exacta del jugador en StartMapId (-1 = derivar de StartEventId, el legado).</summary>
    public int StartX { get; set; } = -1;
    public int StartY { get; set; } = -1;
    public string PlayerSpriteId { get; set; } = "";
    public string UiThemeId { get; set; } = "";
    public int StartMoney { get; set; }
    /// <summary>Party inicial (ids de actores). Vacio = el primer actor del proyecto.</summary>
    public List<string> PartyActorIds { get; set; } = [];
    public RenderSettings Render { get; set; } = new();
    public List<GameVariable> Variables { get; set; } = [];
    public List<TilesetDef> Tilesets { get; set; } = [];
    public List<MapDef> Maps { get; set; } = [];
    public List<EventDef> Events { get; set; } = [];
    public List<DialogueDef> Dialogues { get; set; } = [];
    public List<ActorDef> Actors { get; set; } = [];
    public List<ItemDef> Items { get; set; } = [];
    public List<EnemyDef> Enemies { get; set; } = [];
    public List<BattleDef> Battles { get; set; } = [];
    public List<ShopDef> Shops { get; set; } = [];
    public List<SkillDef> Skills { get; set; } = [];
    public List<SongDef> Songs { get; set; } = [];
    public List<SpriteDef> Sprites { get; set; } = [];
    public List<UiThemeDef> UiThemes { get; set; } = [];
    public List<SfxDef> Sfx { get; set; } = [];
    public List<VfxDef> Vfx { get; set; } = [];
    public List<FontDef> Fonts { get; set; } = [];
    /// <summary>Plan de calidad: rutas canonicas, hitos verificables y bloqueo de pack opcional.</summary>
    public QualityPlanDef QualityPlan { get; set; } = new();
    /// <summary>Libro Espejo: la expresion literaria del mismo canon que construye el juego.</summary>
    public StoryBookDef StoryBook { get; set; } = new();
    /// <summary>Solo en game.pack: archivos binarios referenciados (PNGs) como base64, ruta relativa -> datos. Vacio en project.json.</summary>
    public Dictionary<string, string> EmbeddedFiles { get; set; } = [];
}

public sealed class RenderSettings { public int VirtualWidth { get; set; } = 256; public int VirtualHeight { get; set; } = 224; public int TileSize { get; set; } = 16; public bool LimitPalette { get; set; } = true; public int MaxColors { get; set; } = 64; public bool IntegerScale { get; set; } = true; public bool CrtFilter { get; set; } = true; public string WarpTransition { get; set; } = "iris"; public bool ShowDayClock { get; set; } = false; public string TitleImage { get; set; } = ""; public string TitleVfxId { get; set; } = ""; public string Language { get; set; } = "en"; }
public sealed class GameVariable { public string Id { get; set; } = ""; public VariableKind Kind { get; set; } = VariableKind.Flag; public string Default { get; set; } = "false"; }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum VariableKind { Flag, Number, Text }
/// <summary>AnimMs: velocidad del reloj de animacion de tiles del set (ms por paso, default 300).
/// Es GLOBAL al tileset a proposito: toda el agua del mapa late junta, como en SNES.</summary>
public sealed class TilesetDef { public string Id { get; set; } = ""; public string Image { get; set; } = ""; public int TileSize { get; set; } = 16; public int AnimMs { get; set; } = 300; public List<TileDef> Tiles { get; set; } = []; }
/// <summary>Tint: multiplicador de color (#RRGGBB) para re-teñir el tile sin editar el PNG
/// (bajar packs vivos a la paleta oscura); "" = sin tinte (blanco).
/// Frames: tile ANIMADO — celdas del atlas que se ciclan al reloj del tileset (agua, antorchas);
/// vacio = estatico (la celda es el propio id). Requiere atlas PNG (el color plano no anima).</summary>
public sealed class TileDef { public int Id { get; set; } public string Name { get; set; } = ""; public bool Solid { get; set; } public string Color { get; set; } = "#000000"; public string Tint { get; set; } = ""; public List<int> Frames { get; set; } = []; }
/// <summary>TileFlags: orientacion por celda paralela a Tiles (bits 0-1 = rotacion 0/90/180/270
/// horaria, bit 2 = espejo horizontal; 0 = normal). Vacio = mapa sin rotaciones (retrocompatible:
/// se materializa entero recien cuando alguna celda rota). La colision y la animacion son por
/// tipo de tile, ajenas a la orientacion — solo el render la usa.</summary>
public sealed class MapDef { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public string TilesetId { get; set; } = ""; public int Width { get; set; } public int Height { get; set; } public List<int> Tiles { get; set; } = []; public List<int> TileFlags { get; set; } = []; public List<WarpDef> Warps { get; set; } = []; public List<string> EventIds { get; set; } = []; public string SongId { get; set; } = ""; public string WeatherVfxId { get; set; } = ""; }
/// <summary>Transition: "" usa el default del proyecto (render.warpTransition); o "fade"/"iris"/"spiral".</summary>
public sealed class WarpDef { public int X { get; set; } public int Y { get; set; } public string ToMapId { get; set; } = ""; public int ToX { get; set; } public int ToY { get; set; } public string Transition { get; set; } = ""; }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum Facing { Down, Up, Left, Right }
/// <summary>OffsetX/Y: corrimiento en pixeles dentro de la casilla, para posicion LIBRE de props
/// decorativos (no cambia la colision, que sigue siendo por tile). 0 = pegado a la grilla.</summary>
public sealed class EventDef { public string Id { get; set; } = ""; public string MapId { get; set; } = ""; public string Name { get; set; } = ""; public EventKind Kind { get; set; } = EventKind.Npc; public int X { get; set; } public int Y { get; set; } public string Sprite { get; set; } = ""; public string RoutineId { get; set; } = ""; public List<EventPage> Pages { get; set; } = []; public string Tint { get; set; } = ""; public bool Solid { get; set; } public int OffsetX { get; set; } public int OffsetY { get; set; } public float Scale { get; set; } }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum EventKind { Npc, Trigger, Object, Cutscene }
public sealed class EventPage { public string Id { get; set; } = "default"; public List<ConditionDef> Conditions { get; set; } = []; public List<EventCommand> Commands { get; set; } = []; }
public sealed class ConditionDef { public string VariableId { get; set; } = ""; public string EqualsValue { get; set; } = ""; }
public sealed class EventCommand { public CommandKind Kind { get; set; } = CommandKind.Dialogue; public string TargetId { get; set; } = ""; public string Value { get; set; } = ""; }
/// <summary>Wait: value = segundos ("0.8"). MoveEvent/MovePlayer: value = pasos separados por coma
/// (up/down/left/right caminan un tile; face:up etc. solo giran). Un paso bloqueado se saltea
/// (nunca congela la cutscene). PanCamera: la camara desliza al evento de targetId ("" o "player"
/// = volver al jugador) en value segundos (default 1); al vaciarse la cola vuelve sola.
/// La cola espera a que el movimiento/espera/pan termine. OpenInn: posada — value = precio;
/// si alcanza, cobra, funde a negro y la party despierta curada y revivida.
/// PlaySfx: dispara un efecto de sonido puntual (targetId = id de sfx, propio o reservado).
/// AddPartyMember/RemovePartyMember: suma/saca un actor de la party en runtime (targetId =
/// id de actor); no duplica, nunca deja la party vacia, y el save lo persiste solo.
/// ShowEmote: globo de emote sobre un personaje (targetId = evento, o ""/"player";
/// value = "icono" o "icono:segundos", ej "zzz:6"; iconos: !, ?, zzz, nota, puntos,
/// corazon). NO bloquea la cola: acompana la escena, no la frena.
/// AdvanceTime: avanza el tiempo del mundo (value = "manana"/"tarde"/"noche" cambia la
/// franja del dia actual; "+dia" amanece el dia siguiente). Fade a negro + placa de dia
/// centrada; las paginas condicionan con los ids reservados time.dia y time.franja.
/// ShowItemGet: la ceremonia del item clave (targetId = item; value = cantidad, default 1).
/// Entrega el item Y detiene el mundo: fondo oscurecido, rayos girando detras del sprite,
/// nombre + descripcion y fanfarria (sfx.item_get). Enter continua la cola.
/// PlayVfx: dispara un efecto visual (kind impact) en el mundo — targetId = id de vfx (propio
/// o reservado vfx.hit/vfx.heal), value = evento ancla ("" o "player" = el jugador). NO bloquea
/// la cola (como ShowEmote): acompana la escena; para esperar su duracion, componer con Wait.</summary>
[JsonConverter(typeof(JsonStringEnumConverter))] public enum CommandKind { Dialogue, Battle, SetVariable, GiveItem, OpenShop, PlaySong, TransferPlayer, Wait, MoveEvent, MovePlayer, PanCamera, OpenInn, PlaySfx, AddPartyMember, RemovePartyMember, ShowEmote, AdvanceTime, ShowItemGet, PlayVfx, ShowFloat, SetWeather, GiveMoney, TakeMoney }
public sealed class DialogueDef { public string Id { get; set; } = ""; public string StartNodeId { get; set; } = ""; public List<DialogueNode> Nodes { get; set; } = []; }
public sealed class DialogueNode { public string Id { get; set; } = ""; public string Speaker { get; set; } = ""; public string Text { get; set; } = ""; public List<DialogueChoice> Choices { get; set; } = []; public string NextNodeId { get; set; } = ""; public List<EventCommand> Effects { get; set; } = []; }
public sealed class DialogueChoice { public string Text { get; set; } = ""; public string NextNodeId { get; set; } = ""; }
/// <summary>Growth: cuanto crece cada stat por nivel (null = default del motor: +4HP +1MP +2Atk +1Def +1Vel).</summary>
public sealed class ActorDef { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public int Level { get; set; } = 1; public StatBlock Stats { get; set; } = new(); public StatBlock? Growth { get; set; } public List<string> SkillIds { get; set; } = []; }
public sealed class StatBlock { public int Hp { get; set; } = 30; public int Mp { get; set; } = 0; public int Attack { get; set; } = 8; public int Defense { get; set; } = 4; public int Speed { get; set; } = 4; }
/// <summary>Slot: "" = consumible; "weapon"/"armor" = equipable (Bonus suma stats mientras este puesto).
/// Description: una linea para la ceremonia de ShowItemGet. SpriteId: sprite del item ahi
/// (opcional; sin el rige la convencion item.X -> sprite.X, y sin sprite un destello generico).</summary>
public sealed class ItemDef { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public int Price { get; set; } public string Effect { get; set; } = ""; public string Slot { get; set; } = ""; public StatBlock? Bonus { get; set; } public string Description { get; set; } = ""; public string SpriteId { get; set; } = ""; }
/// <summary>Inflicts: estado que aplica su ataque basico ("" ninguno, "poison" o "sleep"). Determinista: siempre pega el estado.</summary>
public sealed class EnemyDef { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public StatBlock Stats { get; set; } = new(); public int Exp { get; set; } public int Money { get; set; } public string Inflicts { get; set; } = ""; }
/// <summary>SongId: cancion de batalla opcional; al terminar el combate vuelve el tema del mapa.
/// Boss: activa la presentacion de jefe (aura pulsante detras del enemigo, placa de nombre al
/// entrar, temblor mas fuerte al recibir golpes). Para el enemigo climatico de una mazmorra.
/// BackgroundVfxId: fondo de batalla animado (VfxDef kind background); "" = fondo plano.</summary>
public sealed class BattleDef { public string Id { get; set; } = ""; public BattleView View { get; set; } = BattleView.Frontal; public bool RollingHp { get; set; } public List<string> EnemyIds { get; set; } = []; public string VictoryFlag { get; set; } = ""; public string DamageFormula { get; set; } = "attack - defense"; public string SongId { get; set; } = ""; public bool Boss { get; set; } public string BackgroundVfxId { get; set; } = ""; }
[JsonConverter(typeof(JsonStringEnumConverter))] public enum BattleView { Frontal, Side }
public sealed class ShopDef { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public List<string> ItemIds { get; set; } = []; }
/// <summary>Skill de combate. Kind "damage": dano = max(1, power + attack/2 - defense del blanco).
/// Kind "heal": cura power al aliado que elijas (y lo despierta). Consume MpCost del que la usa.
/// Status: estado que aplica ademas del dano ("" ninguno, "poison" o "sleep"); solo kind damage.
/// VfxId: efecto visual (kind impact) al impactar; "" = default del motor (vfx.hit / vfx.heal).</summary>
public sealed class SkillDef { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public int MpCost { get; set; } = 1; public int Power { get; set; } = 5; public string Kind { get; set; } = "damage"; public string Status { get; set; } = ""; public string VfxId { get; set; } = ""; }
public sealed class SongDef { public string Id { get; set; } = ""; public int Tempo { get; set; } = 120; public List<SongChannel> Channels { get; set; } = []; }
/// <summary>Canal estilo tracker. Notas: "C4" (1 pulso), "C4:2" (2 pulsos ligados), "R"/"R:4" silencio.
/// Volume mezcla el canal (0..1). AttackMs/ReleaseMs: envolvente por nota. Duty: ancho del pulso square
/// (0.5 cuadrada; 0.25 y 0.125 son los colores clasicos de NES).</summary>
public sealed class SongChannel { public string Wave { get; set; } = "square"; public List<string> Notes { get; set; } = []; public double Volume { get; set; } = 1.0; public int AttackMs { get; set; } = 10; public int ReleaseMs { get; set; } = 80; public double Duty { get; set; } = 0.5; }
/// <summary>Sprite procedural (Poses con filas de indices hex de paleta) o importado de PNG (Image). Excluyentes.</summary>
public sealed class SpriteDef { public string Id { get; set; } = ""; public string Name { get; set; } = ""; public int Width { get; set; } = 16; public int Height { get; set; } = 16; public List<string> Palette { get; set; } = []; public List<SpritePose> Poses { get; set; } = []; public string Image { get; set; } = ""; public int SheetFramesPerDirection { get; set; } = 2; }
public sealed class SpritePose { public Facing Direction { get; set; } = Facing.Down; public List<SpriteFrame> Frames { get; set; } = []; }
/// <summary>Un frame: Height filas de Width caracteres; '.' transparente, digito hex = indice en Palette.</summary>
public sealed class SpriteFrame { public List<string> Rows { get; set; } = []; }
public sealed class UiThemeDef { public string Id { get; set; } = ""; public string WindowBg { get; set; } = "#182030"; public string WindowBorder { get; set; } = "#E6E6D2"; public string TextColor { get; set; } = "#FFFFFF"; public string AccentColor { get; set; } = "#F5E15A"; public string ShadowColor { get; set; } = "#000000"; public string FontId { get; set; } = ""; public string Style { get; set; } = "beveled"; public int TextSpeedCps { get; set; } = 40; }
/// <summary>Efecto visual declarativo generado por codigo (cero PNGs, cero shaders): el tracker
/// visual simetrico a SongDef. Kind "impact": destello de ataque — timeline corto
/// de capas (flash/spark/ring/slash/beam) ancladas a un blanco. Kind "background": fondo de batalla
/// animado — capas infinitas de patrones (bands/checker/rings/waves) con distorsion
/// sinusoidal por scanline, scroll y ciclado de colores; ignora DurationMs (loopea mientras dure el
/// combate). Determinista: funcion pura del tiempo, sin RNG (el azar de particulas es hash del indice).</summary>
/// <summary>SfxId: el sonido del efecto. Un VFX no es visual, es AUDIOVISUAL — el sonido
/// viaja con el efecto y no con quien lo dispara, asi la skill, el ataque basico, el item y
/// el comando PlayVfx suenan bien sin repetir el id en cada uno. Solo vale en kind impact:
/// los fondos y el clima loopean para siempre y dispararian un sample por cuadro.</summary>
public sealed class VfxDef { public string Id { get; set; } = ""; public string Kind { get; set; } = "impact"; public int DurationMs { get; set; } = 600; public string SfxId { get; set; } = ""; public List<VfxLayer> Layers { get; set; } = []; }
/// <summary>Una capa del efecto (el analogo del canal de SongDef). Impact: Shape + Color + Motion en
/// la ventana [StartMs, EndMs] (EndMs 0 = hasta el final); Count/SpreadPx/SizePx dimensionan las
/// particulas y Angle orienta slash/beam. Background: Pattern + Colors (1..8, ciclados cada CycleMs),
/// ScrollX/Y en px/seg, y la distorsion HDMA (DistortAmp px, DistortFreq rad/linea, DistortSpeed
/// rad/seg); SizePx es el alto de banda/celda (0 = auto: 2 en impact, 16 en background).
/// Blend: additive (luz, default), normal o multiply (sombra) — la leccion de la placa del motor.</summary>
public sealed class VfxLayer
{
    public string Shape { get; set; } = "spark";
    public string Pattern { get; set; } = "";
    public string Color { get; set; } = "#FFFFFF";
    public List<string> Colors { get; set; } = [];
    public string Motion { get; set; } = "burst";
    public string Blend { get; set; } = "additive";
    public int StartMs { get; set; }
    public int EndMs { get; set; }
    public int Count { get; set; } = 8;
    public int SpreadPx { get; set; } = 24;
    public int SizePx { get; set; }
    public double Angle { get; set; } = 45;
    public double ScrollX { get; set; }
    public double ScrollY { get; set; }
    public double DistortAmp { get; set; }
    public double DistortFreq { get; set; } = 0.06;
    public double DistortSpeed { get; set; } = 1.5;
    public int CycleMs { get; set; }
}
/// <summary>Efecto de sonido chiptune por sintesis (estilo sfxr simplificado): barrido de frecuencia + decaimiento.</summary>
public sealed class SfxDef { public string Id { get; set; } = ""; public string Wave { get; set; } = "square"; public double StartFreq { get; set; } = 440; public double EndFreq { get; set; } = 440; public int DurationMs { get; set; } = 120; public double Decay { get; set; } = 0.6; public double Volume { get; set; } = 0.5; public double Duty { get; set; } = 0.5; }
/// <summary>Fuente bitmap importada de PNG: grilla fija de glifos en orden de lectura segun Charset.</summary>
public sealed class FontDef { public string Id { get; set; } = ""; public string Image { get; set; } = ""; public int GlyphWidth { get; set; } = 8; public int GlyphHeight { get; set; } = 8; public string Charset { get; set; } = ""; public bool VariableWidth { get; set; } = true; }

