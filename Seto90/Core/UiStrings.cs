namespace Seto90;

/// <summary>
/// Textos de la INTERFAZ del motor en los idiomas que trae de fabrica. No es localizacion de
/// contenido: los dialogos, nombres y prosa del juego los escribe el autor en el idioma que
/// quiera. Esto es solo lo que dibuja el motor y el autor no controla — comandos de combate,
/// menu de pausa, log, placa de dia, tienda.
///
/// Nota de diseno: el motor arranca en INGLES porque es el idioma en el que se publica y el
/// que espera un agente; un proyecto pone `render.language: "es"` y la UI entera cambia. Vive
/// en Core (sin raylib) para que los smokes headless verifiquen los dos idiomas.
///
/// OJO: los valores de DATOS no se traducen nunca. "manana"/"tarde"/"noche" en minuscula son
/// valores de AdvanceTime y de las condiciones time.franja; aca solo se traduce como se MUESTRAN.
/// </summary>
public static class UiStrings
{
    public static string Language { get; private set; } = "en";

    /// <summary>Fija el idioma de la UI desde render.language. Cualquier valor que no sea "es"
    /// cae a ingles: un proyecto viejo o con un codigo raro sigue siendo legible.</summary>
    public static void Use(string? language) =>
        Language = string.Equals(language, "es", StringComparison.OrdinalIgnoreCase) ? "es" : "en";

    public static readonly string[] Supported = ["en", "es"];

    static string T(string en, string es) => Language == "es" ? es : en;

    // ---- Combate ----
    public static string[] BattleCommands => Language == "es"
        ? ["Atacar", "Habilidad", "Objeto", "Defender", "Huir"]
        : ["Attack", "Skill", "Item", "Defend", "Flee"];
    public static string TagPoison => T("PSN", "VEN");
    public static string TagSleep => T("SLP", "DOR");

    public static string EnemyAppears(string name) => T($"{name} appears.", $"Aparece {name}.");
    public static string EnemiesAppear(string names) => T($"{names} appear.", $"Aparecen {names}.");
    public static string Attacks(string actor, string target, int dmg) =>
        T($"{actor} attacks. {target} loses {dmg} HP.", $"{actor} ataca. {target} pierde {dmg} HP.");
    public static string EnemyHits(string enemy, string target, int dmg, bool defending) =>
        T($"{enemy} hits {target} for {dmg}{(defending ? " (defended)" : "")}.",
          $"{enemy} golpea a {target} por {dmg}{(defending ? " (defendido)" : "")}.");
    public static string Defends(string actor) => T($"{actor} defends.", $"{actor} se defiende.");
    public static string FleeFails(string actor) =>
        T($"{actor} tries to flee... they are too fast.", $"{actor} intenta huir... son demasiado rapidos.");
    public static string PartyEscapes => T("The party escapes.", "La party escapa.");
    public static string NoSkills(string actor) => T($"{actor} knows no skills.", $"{actor} no sabe skills.");
    public static string NoMp(string actor, string skill, int cost) =>
        T($"Not enough MP for {skill} (costs {cost}).", $"Sin MP para {skill} (cuesta {cost}).");
    public static string UsesSkillDamage(string actor, string skill, string target, int dmg) =>
        T($"{actor} uses {skill}. {target} loses {dmg} HP.", $"{actor} usa {skill}. {target} pierde {dmg} HP.");
    public static string UsesSkillHeal(string actor, string skill, string target, int healed) =>
        T($"{actor} uses {skill}. {target} recovers {healed} HP.", $"{actor} usa {skill}. {target} recupera {healed} HP.");
    public static string UsesSkillRevive(string actor, string skill, string target, int hp) =>
        T($"{actor} uses {skill}. {target} returns to the fight with {hp} HP.",
          $"{actor} usa {skill}. {target} vuelve al combate con {hp} HP.");
    public static string UsesItemHeal(string actor, string item, string target, int healed) =>
        T($"{actor} uses {item}. {target} recovers {healed} HP.", $"{actor} usa {item}. {target} recupera {healed} HP.");
    public static string UsesItemCure(string actor, string item, string target) =>
        T($"{actor} uses {item}. {target} recovers.", $"{actor} usa {item}. {target} se recupera.");
    public static string UsesItemRevive(string actor, string item, string target, int hp) =>
        T($"{actor} uses {item}. {target} returns to the fight with {hp} HP.",
          $"{actor} usa {item}. {target} vuelve al combate con {hp} HP.");
    public static string NoBattleItems =>
        T("No usable items in battle (weapons are equipped from the menu).",
          "No tenes items para usar en combate (las armas se equipan en el menu).");
    public static string NoOneFallen => T("Nobody has fallen.", "No hay nadie caido.");
    public static string Falls(string name) => T($" {name} falls.", $" {name} cae.");
    /// <summary>Pierde el turno porque ya estaba dormido.</summary>
    public static string Sleeping(string name) => T($" {name} is asleep.", $" {name} duerme.");
    /// <summary>Un golpe le aplica el estado.</summary>
    public static string GetsSleep(string name) => T($" {name} falls asleep.", $" {name} se durmio.");
    public static string GetsPoison(string name) => T($" {name} is poisoned.", $" {name} queda envenenado.");
    public static string WakesUp(string name) => T($" {name} wakes up.", $" {name} se despierta.");
    public static string PoisonTick(string name, int dmg) =>
        T($" {name} suffers from poison (-{dmg}).", $" {name} sufre el veneno (-{dmg}).");
    public static string Victory(int exp, int money) =>
        T($" Victory. +{exp} EXP, +{money} gold.", $" Victoria. +{exp} EXP, +{money} dinero.");
    public static string Defeat => T(" Defeat.", " Derrota.");
    public static string GameOver => T("GAME OVER", "GAME OVER");

    // ---- Menu de pausa ----
    /// <summary>Etiquetas visibles; las CLAVES internas de las secciones no cambian (ver PauseKeys).</summary>
    public static string[] PauseLabels => Language == "es"
        ? ["ITEMS", "ESTADO", "EQUIPO", "OPCIONES", "GUARDAR", "CARGAR", "SALIR"]
        : ["ITEMS", "STATUS", "EQUIP", "OPTIONS", "SAVE", "LOAD", "QUIT"];

    public static string Level(string name, int lv) => T($"{name}  Lv {lv}", $"{name}  Nv {lv}");
    public static string HpMp(int hp, int maxHp, int mp) => $"HP {hp}/{maxHp}  MP {mp}";
    public static string ExpToNext(int exp, int next) =>
        T($"EXP {exp}/{next} to level", $"EXP {exp}/{next} para subir");
    public static string Stats(int atk, int def, int spd) =>
        T($"Atk {atk} Def {def} Spd {spd}", $"Atk {atk} Def {def} Vel {spd}");
    public static string Money(int money) => T($"Money: ${money}.", $"Dinero: ${money}.");
    public static string Weapon => T("Weapon", "Arma");
    public static string Armor => T("Armor", "Defensa");
    public static string Nothing => T("--none--", "--nada--");
    public static string SlotEmpty(int slot) => T($"Slot {slot}  --empty--", $"Slot {slot}  --vacio--");
    public static string SavedIn(int slot) => T($"Saved in slot {slot}.", $"Guardado en slot {slot}.");
    public static string LoadedFrom(int slot) => T($"Loaded from slot {slot}.", $"Partida cargada desde slot {slot}.");
    public static string OptMusic => T("Music", "Musica");
    public static string OptSounds => T("Sounds", "Sonidos");
    public static string StatSpeed => T("Spd", "Vel");

    public static string MapLabel(string name) => T($"Map: {name}", $"Mapa: {name}");
    public static string PauseFooterInPanel => T("Enter: choose   Back: return", "Enter: elegir   Retro: volver");
    public static string PauseFooter => T("Enter: open   Esc: close menu", "Enter: entrar   Esc: cerrar menu");

    public static string ControlsHint =>
        T("Arrows: move   Enter: talk/act   Esc: menu", "Flechas: mover   Enter: hablar/activar   Esc: menu");

    // ---- Titulo ----
    public static string NewGame => T("New game", "Nueva partida");
    public static string Continue => T("Continue", "Continuar");

    // ---- Tienda y posada ----
    public static string ShopBuy => T("< BUY >", "< COMPRAR >");
    public static string ShopSell => T("< SELL >", "< VENDER >");
    public static string CantAfford(string what) => T($"Not enough gold for {what}.", $"No te alcanza para {what}.");
    public static string CantAffordAmount(int cost) => T($"Not enough gold (${cost}).", $"No te alcanza (${cost}).");
    public static string CantAffordRest(int price) =>
        T($"Not enough gold to rest (${price}).", $"No te alcanza para descansar (${price}).");
    public static string BoughtAndEquipped(string item, string who) =>
        T($"Bought {item} and {who} equipped it.", $"Compraste {item} y {who} la equipo.");
    public static string BoughtUpgrade(string item, string who, string previous) =>
        T($"Bought {item}: upgrade for {who} (replaces {previous}).",
          $"Compraste {item}: mejora a {who} (sale {previous}).");

    // ---- Tiempo del mundo ----
    public static string DayPlate(int day, string phase) => T($"DAY {day} - {phase}", $"DIA {day} - {phase}");
    public static string PhaseMorning => T("MORNING", "MANANA");
    public static string PhaseAfternoon => T("AFTERNOON", "TARDE");
    public static string PhaseNight => T("NIGHT", "NOCHE");
    /// <summary>Nombre visible de una franja a partir de su VALOR de datos (que nunca se traduce).</summary>
    public static string PhaseLabel(string dataValue) => dataValue switch
    {
        "tarde" => PhaseAfternoon,
        "noche" => PhaseNight,
        _ => PhaseMorning,
    };

    // ---- Editor: bitacora de co-autoria y tablero del Libro Espejo ----
    public static string LogHuman => T("you", "vos");
    public static string LogAi => T("ai", "ia");
    // ---- Notas de la bitacora del editor (lo que anota el humano al editar) ----
    public static string NotePaint(int cells, string map) => T($"paints {cells} tiles in {map}", $"pinta {cells} tiles en {map}");
    public static string NotePlace(string what, string map, int x, int y) => T($"places {what} in {map} ({x},{y})", $"coloca {what} en {map} ({x},{y})");
    public static string NoteMove(string what, int x, int y) => T($"moves {what} to ({x},{y})", $"mueve {what} a ({x},{y})");
    public static string NoteDelete(string what) => T($"deletes {what}", $"borra {what}");
    public static string NoteCreate(string what, string map, int x, int y) => T($"creates {what} in {map} ({x},{y})", $"crea {what} en {map} ({x},{y})");
    public static string NoteCreateId(string what) => T($"creates {what}", $"crea {what}");
    public static string NoteMoveWarp(int x, int y) => T($"moves warp to ({x},{y})", $"mueve warp a ({x},{y})");
    public static string NoteDeleteWarp(string map) => T($"deletes warp in {map}", $"borra warp en {map}");
    public static string NoteEdit(string? field, string dialogue, string node) => T($"edits {field} of {dialogue}/{node}", $"edita {field} de {dialogue}/{node}");
    public static string FieldSaved(string dialogue, string node, string? field) =>
        T($"{dialogue}/{node}: {field} saved.", $"{dialogue}/{node}: {field} guardado.");
    public static string NoteCut(int w, int h, string map) => T($"cuts {w}x{h} in {map}", $"corta {w}x{h} en {map}");
    public static string NotePaste(int w, int h, string map) => T($"pastes a {w}x{h} stamp in {map}", $"pega stamp {w}x{h} en {map}");

    public static string SyncInSync => T("IN SYNC", "AL DIA");
    public static string SyncGameChanged => T("GAME CHANGED", "CAMBIO EL JUEGO");
    public static string SyncBookChanged => T("BOOK CHANGED", "CAMBIO EL LIBRO");
    public static string SyncBothChanged => T("BOTH CHANGED", "CAMBIARON AMBOS");
    public static string ExternalChangeAdopted =>
        T("External change adopted (Ctrl+Z undoes it).", "Cambio externo adoptado (Ctrl+Z lo deshace).");
    public static string ExternalChangeRebased =>
        T("External change adopted before writing (rebase; Ctrl+Z undoes it).",
          "Cambio externo adoptado antes de escribir (rebase; Ctrl+Z lo deshace).");
    public static string SyncNever => T("NOT SYNCED", "SIN SYNC");
}
