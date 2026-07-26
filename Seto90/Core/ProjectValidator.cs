using System.Text;
using System.Text.RegularExpressions;

namespace Seto90;

public sealed record ValidationIssue(string Code, string Message, string Fix);

public sealed class ValidationResult
{
    public List<ValidationIssue> Issues { get; } = [];
    public bool Ok => Issues.Count == 0;
    public string ToHumanText()
    {
        if (Ok) return "Content is valid: maps, events, dialogues, battles, audio and references are consistent.";
        var sb = new StringBuilder("Invalid content:\n");
        foreach (var i in Issues) sb.AppendLine($"- {i.Code}: {i.Message} Fix: {i.Fix}");
        return sb.ToString();
    }
}

public static class ProjectValidator
{
    static readonly Regex IdRegex = new("^[a-z][a-z0-9_.-]*$", RegexOptions.Compiled);
    static readonly Regex HexColor = new("^#[0-9a-fA-F]{6}$", RegexOptions.Compiled);
    static readonly HashSet<string> Waves = ["square", "triangle", "saw", "sawtooth", "noise"];
    static readonly HashSet<string> ThemeStyles = ["beveled", "rounded", "plain"];
    static readonly HashSet<string> WipeStyles = ["fade", "iris", "spiral"];
    /// <summary>Estados alterados que el motor conoce (deterministas, sin RNG): veneno y dormir.</summary>
    public static readonly HashSet<string> Statuses = ["poison", "sleep"];
    public static ValidationResult Validate(GameProject p)
    {
        var r = new ValidationResult();
        Unique(p.Variables.Select(x => x.Id), "variables", r); Unique(p.Tilesets.Select(x => x.Id), "tilesets", r); Unique(p.Maps.Select(x => x.Id), "maps", r); Unique(p.Events.Select(x => x.Id), "events", r); Unique(p.Dialogues.Select(x => x.Id), "dialogues", r); Unique(p.Actors.Select(x => x.Id), "actors", r); Unique(p.Items.Select(x => x.Id), "items", r); Unique(p.Enemies.Select(x => x.Id), "enemies", r); Unique(p.Battles.Select(x => x.Id), "battles", r); Unique(p.Shops.Select(x => x.Id), "shops", r); Unique(p.Songs.Select(x => x.Id), "songs", r); Unique(p.Sprites.Select(x => x.Id), "sprites", r); Unique(p.UiThemes.Select(x => x.Id), "uiThemes", r); Unique(p.Sfx.Select(x => x.Id), "sfx", r); Unique(p.Vfx.Select(x => x.Id), "vfx", r); Unique(p.Fonts.Select(x => x.Id), "fonts", r);
        var sfxIds = p.Sfx.Select(x => x.Id).Concat(DefaultAssets.DefaultSfx().Select(x => x.Id)).Append("sfx.boot").ToHashSet(); // sfx.boot: jingle embebido de la placa, tambien reservado
        // Kind por id de vfx (proyecto pisa a los reservados del motor): para validar referencias
        // Y que el uso calce con el kind (un fondo no puede ser el impacto de una skill).
        var vfxKinds = DefaultAssets.DefaultVfx().Concat(p.Vfx).GroupBy(v => v.Id).ToDictionary(g => g.Key, g => g.Last().Kind);
        var vars = p.Variables.Select(x => x.Id).ToHashSet(); var tilesets = p.Tilesets.Select(x => x.Id).ToHashSet(); var maps = p.Maps.Select(x => x.Id).ToHashSet(); var events = p.Events.Select(x => x.Id).ToHashSet(); var dialogs = p.Dialogues.Select(x => x.Id).ToHashSet(); var items = p.Items.Select(x => x.Id).ToHashSet(); var enemies = p.Enemies.Select(x => x.Id).ToHashSet(); var battles = p.Battles.Select(x => x.Id).ToHashSet(); var shops = p.Shops.Select(x => x.Id).ToHashSet(); var songs = p.Songs.Select(x => x.Id).ToHashSet(); var sprites = p.Sprites.Select(x => x.Id).ToHashSet(); var themes = p.UiThemes.Select(x => x.Id).ToHashSet(); var fonts = p.Fonts.Select(x => x.Id).ToHashSet();
        if (!string.IsNullOrWhiteSpace(p.StartMapId) && !maps.Contains(p.StartMapId)) r.Issues.Add(new("missing_start_map", $"Starting map '{p.StartMapId}' does not exist.", "Point startMapId at a map that exists."));
        if ((p.StartX >= 0) != (p.StartY >= 0)) r.Issues.Add(new("bad_start_position", $"startX={p.StartX} startY={p.StartY}: set both or neither.", "Set startX and startY together (or -1 on both to derive from startEventId)."));
        if (p.StartX >= 0 && p.StartY >= 0 && p.Maps.FirstOrDefault(m => m.Id == p.StartMapId) is { } sm && (p.StartX >= sm.Width || p.StartY >= sm.Height)) r.Issues.Add(new("bad_start_position", $"startX/Y ({p.StartX},{p.StartY}) cae fuera de {p.StartMapId} ({sm.Width}x{sm.Height}).", "Use coordinates inside the starting map."));
        if (!UiStrings.Supported.Contains(p.Render.Language))
            r.Issues.Add(new("bad_language", $"render.language '{p.Render.Language}' is unknown.",
                $"Use one of: {string.Join(", ", UiStrings.Supported)}. This is the ENGINE UI language; content stays in whatever language the author writes."));
        if (!WipeStyles.Contains(p.Render.WarpTransition)) r.Issues.Add(new("bad_warp_transition", $"render.warpTransition '{p.Render.WarpTransition}' is unknown.", "Use fade, iris or spiral."));
        VfxRef(p.Render.TitleVfxId, "background", "render.titleVfxId", vfxKinds, r);
        if (p.StartMoney < 0) r.Issues.Add(new("bad_start_money", $"startMoney={p.StartMoney}.", "Use a starting money value >= 0."));
        var actorIds = p.Actors.Select(x => x.Id).ToHashSet();
        foreach (var id in p.PartyActorIds) Ref(id, actorIds, "missing_party_actor", $"partyActorIds incluye '{id}'.", "Create the actor with actor.create, or remove it from the party.", r);
        foreach (var actor in p.Actors) if (actor.Growth is { } g && (g.Hp < 0 || g.Mp < 0 || g.Attack < 0 || g.Defense < 0 || g.Speed < 0)) r.Issues.Add(new("bad_growth", $"Actor {actor.Id} has negative growth.", "Use per-level growth values >= 0."));
        Unique(p.Skills.Select(x => x.Id), "skills", r);
        var skills = p.Skills.Select(x => x.Id).ToHashSet();
        foreach (var skill in p.Skills) { Id(skill.Id, "skill", r); if (skill.MpCost < 0) r.Issues.Add(new("bad_skill_cost", $"Skill {skill.Id} has a negative mpCost.", "Use mpCost >= 0.")); if (skill.Power <= 0) r.Issues.Add(new("bad_skill_power", $"Skill {skill.Id} has power {skill.Power}.", "Use power > 0.")); if (skill.Kind is not ("damage" or "heal" or "revive")) r.Issues.Add(new("bad_skill_kind", $"Skill {skill.Id} has kind '{skill.Kind}'.", "Use damage, heal or revive.")); if (!string.IsNullOrWhiteSpace(skill.Status) && !Statuses.Contains(skill.Status)) r.Issues.Add(new("bad_skill_status", $"Skill {skill.Id} aplica estado '{skill.Status}'.", "Use poison, sleep or empty.")); if (!string.IsNullOrWhiteSpace(skill.Status) && skill.Kind != "damage") r.Issues.Add(new("heal_with_status", $"Skill {skill.Id} es {skill.Kind} pero aplica estado.", "Only damage skills can inflict a status.")); VfxRef(skill.VfxId, "impact", $"Skill {skill.Id}", vfxKinds, r); }
        foreach (var enemy in p.Enemies) if (!string.IsNullOrWhiteSpace(enemy.Inflicts) && !Statuses.Contains(enemy.Inflicts)) r.Issues.Add(new("bad_enemy_inflicts", $"Enemigo {enemy.Id} inflige '{enemy.Inflicts}'.", "Use poison, sleep or empty."));
        foreach (var item in p.Items) if (item.Effect.StartsWith("cure:", StringComparison.OrdinalIgnoreCase) && !Statuses.Contains(item.Effect.Split(':', 2)[1]) && !item.Effect.Split(':', 2)[1].Equals("all", StringComparison.OrdinalIgnoreCase)) r.Issues.Add(new("bad_cure_effect", $"Item {item.Id} has effect '{item.Effect}'.", "Use cure:poison, cure:sleep or cure:all."));
        foreach (var item in p.Items) { if (item.Effect.StartsWith("revive:", StringComparison.OrdinalIgnoreCase) && (!int.TryParse(item.Effect.Split(':', 2)[1], out var rev) || rev <= 0)) r.Issues.Add(new("bad_revive_effect", $"Item {item.Id} has effect '{item.Effect}'.", "Use revive:N with N > 0 (the HP the fallen ally returns with)."));
            if (!string.IsNullOrWhiteSpace(item.SpriteId)) Ref(item.SpriteId, sprites, "missing_item_sprite", $"Item {item.Id} usa sprite '{item.SpriteId}'.", "Create the sprite with sprite.create, or clear spriteId.", r);
            if (!string.IsNullOrWhiteSpace(item.Slot) && item.Slot is not ("weapon" or "armor")) r.Issues.Add(new("bad_item_slot", $"Item {item.Id} has slot '{item.Slot}'.", "Use weapon, armor or empty (a consumable).")); if (item.Bonus != null && string.IsNullOrWhiteSpace(item.Slot)) r.Issues.Add(new("bonus_without_slot", $"Item {item.Id} has a bonus but no slot.", "Asignar slot weapon o armor.")); if (item.Bonus is { } bo && (bo.Hp < 0 || bo.Mp < 0 || bo.Attack < 0 || bo.Defense < 0 || bo.Speed < 0)) r.Issues.Add(new("bad_item_bonus", $"Item {item.Id} has a negative bonus.", "Use a bonus >= 0 per stat.")); }
        foreach (var actor in p.Actors) foreach (var sid in actor.SkillIds) Ref(sid, skills, "missing_actor_skill", $"Actor {actor.Id} sabe '{sid}'.", "Create the skill with skill.create, or remove it from the actor.", r);
        if (!string.IsNullOrWhiteSpace(p.PlayerSpriteId)) Ref(p.PlayerSpriteId, sprites, "missing_player_sprite", $"playerSpriteId points at '{p.PlayerSpriteId}'.", "Create the sprite with sprite.create, or clear playerSpriteId.", r);
        if (!string.IsNullOrWhiteSpace(p.UiThemeId)) Ref(p.UiThemeId, themes, "missing_ui_theme", $"uiThemeId points at '{p.UiThemeId}'.", "Create the theme with uitheme.set, or clear uiThemeId.", r);
        foreach (var sp in p.Sprites) Sprite(sp, r);
        foreach (var t in p.UiThemes) Theme(t, fonts, r);
        foreach (var s in p.Sfx) Sfx(s, r);
        foreach (var v in p.Vfx) Vfx(v, sfxIds, r);
        foreach (var s in p.Songs) Song(s, r);
        foreach (var f in p.Fonts) Font(f, r);
        foreach (var m in p.Maps) { Id(m.Id, "map", r); Ref(m.TilesetId, tilesets, "missing_tileset", $"Map {m.Id} points at tileset {m.TilesetId}.", "Create the tileset, or fix tilesetId.", r); if (m.Width <= 0 || m.Height <= 0) r.Issues.Add(new("bad_map_size", $"Map {m.Id} has an invalid size.", "Use positive dimensions.")); if (m.Tiles.Count != m.Width * m.Height) r.Issues.Add(new("tile_count", $"Map {m.Id} has {m.Tiles.Count} tiles, expected {m.Width * m.Height}.", "Repintar mapa.")); if (!string.IsNullOrWhiteSpace(m.SongId)) Ref(m.SongId, songs, "missing_song", $"Map {m.Id} points at song {m.SongId}.", "Create the song, or clear songId.", r); VfxRef(m.WeatherVfxId, "weather", $"Mapa {m.Id}", vfxKinds, r); foreach (var eid in m.EventIds) Ref(eid, events, "missing_map_event", $"Mapa {m.Id} lista evento {eid}.", "Create the event, or remove it.", r); foreach (var w in m.Warps) { Ref(w.ToMapId, maps, "missing_warp_map", $"Map {m.Id} has a warp to {w.ToMapId}.", "Create the destination map, or remove the warp.", r); if (w.X < 0 || w.Y < 0 || w.X >= m.Width || w.Y >= m.Height) r.Issues.Add(new("warp_out_of_bounds", $"Mapa {m.Id}: warp en ({w.X},{w.Y}) fuera del mapa.", "Place the warp inside the map bounds.")); var dest = p.Maps.FirstOrDefault(x => x.Id == w.ToMapId); if (dest != null && (w.ToX < 0 || w.ToY < 0 || w.ToX >= dest.Width || w.ToY >= dest.Height)) r.Issues.Add(new("warp_dest_out_of_bounds", $"Mapa {m.Id}: warp llega a ({w.ToX},{w.ToY}), fuera de {w.ToMapId}.", "Fix the warp's destination coordinates.")); if (!string.IsNullOrWhiteSpace(w.Transition) && !WipeStyles.Contains(w.Transition)) r.Issues.Add(new("bad_warp_transition", $"Map {m.Id}: warp with unknown transition '{w.Transition}'.", "Use fade, iris, spiral or empty (the project default).")); } }
        foreach (var m in p.Maps) { if (m.TileFlags.Count > 0 && m.TileFlags.Count != m.Tiles.Count) r.Issues.Add(new("bad_tile_flags", $"Map {m.Id}: tileFlags has {m.TileFlags.Count}, expected 0 or {m.Tiles.Count}.", "Repaint the map, or clear tileFlags.")); else if (m.TileFlags.Any(f => f is < 0 or > 7)) r.Issues.Add(new("bad_tile_flags", $"Map {m.Id}: a tile orientation is outside 0-7.", "Use 0-7: bits 0-1 = rotation, bit 2 = mirror.")); }
        foreach (var e in p.Events) { Id(e.Id, "event", r); Ref(e.MapId, maps, "missing_event_map", $"Event {e.Id} points at map {e.MapId}.", "Create the map, or move the event.", r); if (!string.IsNullOrWhiteSpace(e.Sprite)) Ref(e.Sprite, sprites, "missing_event_sprite", $"Evento {e.Id} usa sprite '{e.Sprite}'.", "Create the sprite with sprite.create, or clear the sprite field.", r); foreach (var page in e.Pages) { foreach (var c in page.Conditions) Condition(c, e.Id, vars, r); foreach (var c in page.Commands) Cmd(c, e.Id, vars, dialogs, battles, items, shops, songs, maps, events, sfxIds, actorIds, vfxKinds, r); } }
        foreach (var d in p.Dialogues) { Id(d.Id, "dialogue", r); var nodes = d.Nodes.Select(x => x.Id).ToHashSet(); Ref(d.StartNodeId, nodes, "missing_dialogue_start", $"Dialogo {d.Id} inicia en {d.StartNodeId}.", "Create the node, or fix startNodeId.", r); foreach (var n in d.Nodes) { if (!string.IsNullOrWhiteSpace(n.NextNodeId)) Ref(n.NextNodeId, nodes, "missing_dialogue_next", $"Node {d.Id}/{n.Id} points at {n.NextNodeId}.", "Fix nextNodeId.", r); foreach (var ch in n.Choices) Ref(ch.NextNodeId, nodes, "missing_choice_next", $"Choice '{ch.Text}' points at {ch.NextNodeId}.", "Create the destination node.", r); foreach (var fx in n.Effects) Cmd(fx, $"{d.Id}/{n.Id}", vars, dialogs, battles, items, shops, songs, maps, events, sfxIds, actorIds, vfxKinds, r); } }
        foreach (var b in p.Battles) { Id(b.Id, "battle", r); foreach (var eid in b.EnemyIds) Ref(eid, enemies, "missing_battle_enemy", $"Combate {b.Id} usa enemigo {eid}.", "Create the enemy, or remove it.", r); if (!string.IsNullOrWhiteSpace(b.VictoryFlag)) Ref(b.VictoryFlag, vars, "missing_victory_flag", $"Combate {b.Id} setea {b.VictoryFlag}.", "Definir flag.", r); if (!string.IsNullOrWhiteSpace(b.SongId)) Ref(b.SongId, songs, "missing_battle_song", $"Combate {b.Id} usa cancion {b.SongId}.", "Create the song with song.create, or clear songId.", r); VfxRef(b.BackgroundVfxId, "background", $"Combate {b.Id}", vfxKinds, r); FormulaValidator.Validate(b.DamageFormula, r, b.Id); }
        foreach (var s in p.Shops) foreach (var item in s.ItemIds) Ref(item, items, "missing_shop_item", $"Tienda {s.Id} vende {item}.", "Create the item, or remove it.", r);
        // Tiles animados: frames = celdas del atlas que se ciclan al reloj del tileset (animMs).
        foreach (var t in p.Tilesets)
        {
            if (t.AnimMs < 50) r.Issues.Add(new("bad_tileset_anim", $"Tileset {t.Id} has animMs={t.AnimMs}.", "Use animMs >= 50 (milliseconds per animation step)."));
            foreach (var tile in t.Tiles)
            {
                if (tile.Frames.Any(f => f < 0)) r.Issues.Add(new("bad_tile_frames", $"Tileset {t.Id}: tile {tile.Id} has a negative frame.", "Use atlas cell indices >= 0."));
                if (tile.Frames.Count > 0 && !t.Image.EndsWith(".png", StringComparison.OrdinalIgnoreCase)) r.Issues.Add(new("frames_without_atlas", $"Tileset {t.Id}: tile {tile.Id} has frames but the set has no PNG atlas.", "Add image (an atlas) to the tileset, or clear frames (a flat colour cannot animate)."));
            }
        }
        NarrativeTwin.Validate(p, r);
        QualityPlanValidator.Validate(p, r);
        return r;
    }
    /// <summary>Condicion de pagina: variable definida, o los ids reservados del sistema de
    /// horario (time.dia = entero >= 1; time.franja = manana/tarde/noche).</summary>
    static void Condition(ConditionDef c, string owner, HashSet<string> vars, ValidationResult r)
    {
        if (c.VariableId == "time.dia")
        {
            if (!int.TryParse(c.EqualsValue.Trim(), out var d) || d < 1) r.Issues.Add(new("bad_time_condition", $"Evento {owner}: time.dia = '{c.EqualsValue}'.", "Use a whole day number >= 1."));
            return;
        }
        if (c.VariableId == "time.franja")
        {
            if (c.EqualsValue.Trim().ToLowerInvariant() is not ("manana" or "tarde" or "noche")) r.Issues.Add(new("bad_time_condition", $"Evento {owner}: time.franja = '{c.EqualsValue}'.", "Use manana, tarde or noche."));
            return;
        }
        Ref(c.VariableId, vars, "missing_condition_variable", $"Evento {owner} usa variable {c.VariableId}.", "Definir variable.", r);
    }

    static void Cmd(EventCommand c, string owner, HashSet<string> vars, HashSet<string> dialogs, HashSet<string> battles, HashSet<string> items, HashSet<string> shops, HashSet<string> songs, HashSet<string> maps, HashSet<string> events, HashSet<string> sfxIds, HashSet<string> actors, Dictionary<string, string> vfxKinds, ValidationResult r) { switch (c.Kind) {
        case CommandKind.PlaySfx: Ref(c.TargetId, sfxIds, "missing_sfx", $"{owner} plays sfx {c.TargetId}.", "Create the sfx with sfx.create, or use one of the engine's reserved ids.", r); break;
        case CommandKind.PlayVfx:
            if (string.IsNullOrWhiteSpace(c.TargetId)) r.Issues.Add(new("missing_vfx", $"{owner}: PlayVfx without a targetId.", "Give the vfx id (your own, or the reserved vfx.hit / vfx.heal)."));
            else VfxRef(c.TargetId, "impact", owner, vfxKinds, r);
            if (!string.IsNullOrWhiteSpace(c.Value) && !c.Value.Equals("player", StringComparison.OrdinalIgnoreCase)) Ref(c.Value.Trim(), events, "missing_vfx_anchor", $"{owner} anchors the vfx to event {c.Value}.", "Create the event, or use '' / 'player' for the player.", r);
            break;
        case CommandKind.AddPartyMember: case CommandKind.RemovePartyMember: Ref(c.TargetId, actors, "missing_party_actor", $"{owner} adds/removes actor {c.TargetId} from the party.", "Create the actor with actor.create, or fix targetId.", r); break;
        case CommandKind.AdvanceTime:
            if (c.Value.Trim().ToLowerInvariant() is not ("manana" or "tarde" or "noche" or "+dia")) r.Issues.Add(new("bad_time_value", $"{owner}: AdvanceTime with value '{c.Value}'.", "Use manana, tarde, noche (change the phase) or +dia (dawn of the next day)."));
            break;
        case CommandKind.ShowEmote:
            if (!string.IsNullOrWhiteSpace(c.TargetId) && !c.TargetId.Equals("player", StringComparison.OrdinalIgnoreCase)) Ref(c.TargetId, events, "missing_emote_target", $"{owner} shows an emote over event {c.TargetId}.", "Create the event, or use '' / 'player' for the player.", r);
            if (!CutsceneSteps.TryParseEmote(c.Value, out _, out _)) r.Issues.Add(new("bad_emote_value", $"{owner}: ShowEmote with value '{c.Value}'.", $"Use 'icon' or 'icon:seconds' (e.g. 'zzz:6'); icons: {string.Join(", ", CutsceneSteps.EmoteIcons)}."));
            break;
        case CommandKind.GiveMoney: case CommandKind.TakeMoney:
            if (!int.TryParse(c.Value, out var monto) || monto <= 0) r.Issues.Add(new("bad_money_value", $"{owner}: {c.Kind} with value '{c.Value}'.", "Use the amount as a whole number > 0 (e.g. '4')."));
            break;
        case CommandKind.SetWeather:
            if (!string.IsNullOrWhiteSpace(c.TargetId)) VfxRef(c.TargetId, "weather", owner, vfxKinds, r); // "" = despejar
            break;
        case CommandKind.ShowFloat:
            if (!string.IsNullOrWhiteSpace(c.TargetId) && !c.TargetId.Equals("player", StringComparison.OrdinalIgnoreCase)) Ref(c.TargetId, events, "missing_float_target", $"{owner} shows floating text over event {c.TargetId}.", "Create the event, or use '' / 'player' for the player.", r);
            var floatParts = c.Value.Split(':', 2);
            if (string.IsNullOrWhiteSpace(floatParts[0])) r.Issues.Add(new("bad_float_value", $"{owner}: ShowFloat without text.", "Use 'text' or 'text:#RRGGBB' (e.g. '+6 HP:#82F0A0')."));
            else if (floatParts.Length > 1 && !System.Text.RegularExpressions.Regex.IsMatch(floatParts[1].Trim(), "^#[0-9a-fA-F]{6}$")) r.Issues.Add(new("bad_float_value", $"{owner}: ShowFloat with colour '{floatParts[1]}'.", "Use #RRGGBB (e.g. '#82F0A0'), or just the text without ':'."));
            break;
        case CommandKind.ShowItemGet:
            Ref(c.TargetId, items, "missing_itemget_item", $"{owner} presents item {c.TargetId}.", "Create the item with item.create, or fix targetId.", r);
            if (!string.IsNullOrWhiteSpace(c.Value) && (!int.TryParse(c.Value.Trim(), out var igCount) || igCount <= 0)) r.Issues.Add(new("bad_itemget_value", $"{owner}: ShowItemGet with value '{c.Value}'.", "Use the amount as a whole number > 0, or empty (= 1)."));
            break;
        case CommandKind.Wait: if (!CutsceneSteps.TryParseWait(c.Value, out _)) r.Issues.Add(new("bad_wait_value", $"{owner}: Wait with value '{c.Value}'.", "Use seconds between 0 and 10, e.g. '0.8'.")); break;
        case CommandKind.MoveEvent: Ref(c.TargetId, events, "missing_move_event", $"{owner} mueve al evento {c.TargetId}.", "Create the event, or fix targetId.", r); Steps(c, owner, r); break;
        case CommandKind.MovePlayer: Steps(c, owner, r); break;
        case CommandKind.PanCamera: if (!string.IsNullOrWhiteSpace(c.TargetId) && !c.TargetId.Equals("player", StringComparison.OrdinalIgnoreCase)) Ref(c.TargetId, events, "missing_pan_event", $"{owner} pans the camera to event {c.TargetId}.", "Create the event, or use '' / 'player' to return to the player.", r); if (!string.IsNullOrWhiteSpace(c.Value) && !CutsceneSteps.TryParseWait(c.Value, out _)) r.Issues.Add(new("bad_pan_seconds", $"{owner}: PanCamera with value '{c.Value}'.", "Use seconds between 0 and 10 (or empty = 1).")); break;
        case CommandKind.OpenInn: if (!int.TryParse(c.Value, out var innPrice) || innPrice < 0) r.Issues.Add(new("bad_inn_price", $"{owner}: OpenInn with value '{c.Value}'.", "Use the rest price as a whole number >= 0 (e.g. '5').")); break; case CommandKind.Dialogue: Ref(c.TargetId, dialogs, "missing_dialogue", $"{owner} points at dialogue {c.TargetId}.", "Create the dialogue.", r); break; case CommandKind.Battle: Ref(c.TargetId, battles, "missing_battle", $"{owner} points at battle {c.TargetId}.", "Create the battle.", r); break; case CommandKind.SetVariable: Ref(c.TargetId, vars, "missing_set_variable", $"{owner} modifica variable {c.TargetId}.", "Definir variable.", r); break; case CommandKind.GiveItem: Ref(c.TargetId, items, "missing_give_item", $"{owner} entrega item {c.TargetId}.", "Create the item.", r); break; case CommandKind.OpenShop: Ref(c.TargetId, shops, "missing_shop", $"{owner} abre tienda {c.TargetId}.", "Create the shop.", r); break; case CommandKind.PlaySong: Ref(c.TargetId, songs, "missing_command_song", $"{owner} reproduce {c.TargetId}.", "Create the song.", r); break; case CommandKind.TransferPlayer: Ref(c.TargetId, maps, "missing_transfer_map", $"{owner} transfiere al mapa {c.TargetId}.", "Create the destination map, or fix targetId.", r); if (!string.IsNullOrWhiteSpace(c.Value)) { var parts = c.Value.Split(','); if (parts.Length != 2 || !int.TryParse(parts[0].Trim(), out _) || !int.TryParse(parts[1].Trim(), out _)) r.Issues.Add(new("bad_transfer_value", $"{owner}: TransferPlayer with value '{c.Value}'.", "Use 'x,y' (e.g. '5,7').")); } break; } }
    static void Steps(EventCommand c, string owner, ValidationResult r)
    {
        var steps = CutsceneSteps.Parse(c.Value);
        if (steps.Count == 0) { r.Issues.Add(new("bad_move_steps", $"{owner}: {c.Kind} without steps.", "Listar pasos separados por coma, ej 'up,up,face:left'.")); return; }
        foreach (var s in steps.Where(s => !CutsceneSteps.IsValid(s)))
            r.Issues.Add(new("bad_move_steps", $"{owner}: unknown step '{s}'.", $"Use {string.Join(", ", CutsceneSteps.Valid)}."));
    }

    static void Sprite(SpriteDef sp, ValidationResult r)
    {
        Id(sp.Id, "sprite", r);
        var procedural = sp.Poses.Count > 0;
        var imported = !string.IsNullOrWhiteSpace(sp.Image);
        if (procedural && imported) { r.Issues.Add(new("sprite_double_source", $"Sprite {sp.Id} defines both Poses and Image.", "Use either procedural poses or a PNG, not both.")); return; }
        if (!procedural && !imported) { r.Issues.Add(new("sprite_empty", $"Sprite {sp.Id} has neither poses nor an image.", "Define poses as rows of pixels, or give a PNG path.")); return; }
        // Procedural (personajes) capea en 64; importado de PNG (objetos/edificios colocables) hasta 256.
        var maxSprite = imported ? 256 : 64;
        if (sp.Width < 1 || sp.Width > maxSprite || sp.Height < 1 || sp.Height > maxSprite) r.Issues.Add(new("sprite_bad_size", $"Sprite {sp.Id} mide {sp.Width}x{sp.Height}.", $"Use between 1 and {maxSprite} pixels per side."));
        if (!procedural) return;
        if (sp.Palette.Count is 0 or > 15) r.Issues.Add(new("sprite_palette_size", $"Sprite {sp.Id} has {sp.Palette.Count} colours.", "Use between 1 and 15 colours (SNES-style palette)."));
        foreach (var color in sp.Palette.Where(c => !HexColor.IsMatch(c))) r.Issues.Add(new("sprite_bad_color", $"Sprite {sp.Id} has an invalid colour '{color}'.", "Use #RRGGBB."));
        if (sp.Poses.All(x => x.Direction != Facing.Down)) r.Issues.Add(new("sprite_missing_down", $"Sprite {sp.Id} has no 'down' pose.", "The down pose is required; up/left/right fall back to it."));
        foreach (var dup in sp.Poses.GroupBy(x => x.Direction).Where(g => g.Count() > 1)) r.Issues.Add(new("sprite_duplicate_pose", $"Sprite {sp.Id} repeats the {dup.Key} pose.", "Keep one pose per direction."));
        foreach (var pose in sp.Poses)
        {
            if (pose.Frames.Count == 0) { r.Issues.Add(new("sprite_pose_empty", $"Sprite {sp.Id} pose {pose.Direction} has no frames.", "Add at least one frame.")); continue; }
            foreach (var frame in pose.Frames)
            {
                if (frame.Rows.Count != sp.Height) { r.Issues.Add(new("sprite_frame_rows", $"Sprite {sp.Id} pose {pose.Direction}: frame has {frame.Rows.Count} rows, expected {sp.Height}.", "Complete the frame's rows.")); continue; }
                foreach (var row in frame.Rows)
                {
                    if (row.Length != sp.Width) { r.Issues.Add(new("sprite_frame_width", $"Sprite {sp.Id} pose {pose.Direction}: fila de {row.Length} chars, esperaba {sp.Width}.", "Every row must be exactly Width characters long.")); break; }
                    foreach (var ch in row.Where(c => c != '.' && !(Uri.IsHexDigit(c) && Convert.ToInt32(c.ToString(), 16) < sp.Palette.Count)))
                    { r.Issues.Add(new("sprite_bad_pixel", $"Sprite {sp.Id} pose {pose.Direction}: invalid character '{ch}'.", $"Use '.' or a hex digit below {sp.Palette.Count} (the palette size).")); break; }
                }
            }
        }
    }
    static void Theme(UiThemeDef t, HashSet<string> fonts, ValidationResult r)
    {
        Id(t.Id, "uiTheme", r);
        foreach (var (name, color) in new[] { ("windowBg", t.WindowBg), ("windowBorder", t.WindowBorder), ("textColor", t.TextColor), ("accentColor", t.AccentColor), ("shadowColor", t.ShadowColor) })
            if (!HexColor.IsMatch(color)) r.Issues.Add(new("theme_bad_color", $"Theme {t.Id}: {name} '{color}' is invalid.", "Use #RRGGBB."));
        if (!ThemeStyles.Contains(t.Style)) r.Issues.Add(new("theme_bad_style", $"Theme {t.Id}: unknown style '{t.Style}'.", "Use beveled, rounded or plain."));
        if (t.TextSpeedCps is < 1 or > 240) r.Issues.Add(new("theme_bad_speed", $"Tema {t.Id}: textSpeedCps={t.TextSpeedCps}.", "Use between 1 and 240 characters per second."));
        if (!string.IsNullOrWhiteSpace(t.FontId)) Ref(t.FontId, fonts, "missing_theme_font", $"Tema {t.Id} usa fuente '{t.FontId}'.", "Import the font with font.import, or clear fontId (the embedded one is used).", r);
    }
    static void Sfx(SfxDef s, ValidationResult r)
    {
        Id(s.Id, "sfx", r);
        if (!Waves.Contains(s.Wave.ToLowerInvariant())) r.Issues.Add(new("sfx_bad_wave", $"SFX {s.Id}: onda '{s.Wave}' desconocida.", "Use square, triangle, saw or noise."));
        if (s.StartFreq is < 20 or > 20000 || s.EndFreq is < 20 or > 20000) r.Issues.Add(new("sfx_bad_freq", $"SFX {s.Id}: frecuencias {s.StartFreq}-{s.EndFreq}.", "Use between 20 and 20000 Hz."));
        if (s.DurationMs is < 1 or > 4000) r.Issues.Add(new("sfx_bad_duration", $"SFX {s.Id}: duracion {s.DurationMs}ms.", "Use between 1 and 4000 ms."));
        if (s.Decay is < 0 or > 1 || s.Volume is < 0 or > 1) r.Issues.Add(new("sfx_bad_level", $"SFX {s.Id}: decay/volume fuera de rango.", "Use values between 0 and 1."));
        if (s.Duty is < 0.05 or > 0.95) r.Issues.Add(new("sfx_bad_duty", $"SFX {s.Id}: duty {s.Duty}.", "Use between 0.05 and 0.95."));
    }
    /// <summary>Referencia a un vfx con kind exigido ("" = sin referencia, valido): existe
    /// (missing_vfx) y su kind calza con el uso (vfx_kind_mismatch) — un fondo de batalla no
    /// puede ser el impacto de una skill ni viceversa.</summary>
    static void VfxRef(string id, string wantKind, string owner, Dictionary<string, string> vfxKinds, ValidationResult r)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        if (!vfxKinds.TryGetValue(id, out var kind)) { r.Issues.Add(new("missing_vfx", $"{owner} uses vfx '{id}'.", "Create the vfx with vfx.create, or use a reserved id (vfx.hit, vfx.heal).")); return; }
        if (kind != wantKind) r.Issues.Add(new("vfx_kind_mismatch", $"{owner} uses vfx '{id}' (kind {kind}) where a {wantKind} belongs.", wantKind switch { "impact" => "Use a vfx of kind impact (an attack flash).", "background" => "Use a vfx of kind background (a battle backdrop).", _ => "Use a vfx of kind weather (map weather: rain/snow/fog/flash; reserved: vfx.lluvia, vfx.niebla, vfx.nieve)." }));
    }

    /// <summary>Un VfxDef: kind conocido, duracion acotada (la leccion del zzz:30 — un impacto no
    /// puede colgar la escena) y cada capa con shape/pattern, colores y ventana validos.</summary>
    static void Vfx(VfxDef v, HashSet<string> sfxIds, ValidationResult r)
    {
        Id(v.Id, "vfx", r);
        if (!VfxEval.Kinds.Contains(v.Kind)) { r.Issues.Add(new("bad_vfx_kind", $"Vfx {v.Id} has kind '{v.Kind}'.", "Use impact (attack flash), background (battle backdrop) or weather (map weather).")); return; }
        var background = v.Kind == "background";
        var weather = v.Kind == "weather";
        // El sonido viaja con el efecto, pero solo en impact: un background o un clima
        // loopean para siempre y dispararian un sample por cuadro.
        if (v.SfxId != "")
        {
            if (background || weather) r.Issues.Add(new("bad_vfx_sfx", $"Vfx {v.Id} is kind '{v.Kind}' and has an sfxId.", "Sound is only valid on kind impact: a backdrop or weather loops and would never stop."));
            else Ref(v.SfxId, sfxIds, "missing_sfx", $"Vfx {v.Id} suena {v.SfxId}.", "Create the sfx with sfx.create, or use a reserved id.", r);
        }
        if (weather)
        {
            // durationMs > 1000 = largo del CICLO del clima (las capas viven en ventanas
            // startMs/endMs con rampas: llueve -> escampa); <= 1000 = clima permanente.
            if (v.DurationMs is < 0 or > 600000) r.Issues.Add(new("bad_vfx_duration", $"Vfx {v.Id}: durationMs={v.DurationMs}.", "For weather: 0 (permanent) or the cycle length, up to 600000 ms (10 min)."));
            if (v.Layers.Count is 0 or > 16) { r.Issues.Add(new("bad_vfx_layer", $"Vfx {v.Id} has {v.Layers.Count} layers.", "Use between 1 and 16 layers.")); return; }
            for (var i = 0; i < v.Layers.Count; i++)
            {
                var l = v.Layers[i];
                var who = $"Vfx {v.Id} capa {i}";
                if (!VfxEval.WeatherShapes.Contains(l.Shape)) r.Issues.Add(new("bad_vfx_layer", $"{who}: shape '{l.Shape}'.", "A weather vfx uses shape: rain, snow, fog or flash (lightning)."));
                if (!HexColor.IsMatch(l.Color)) r.Issues.Add(new("bad_vfx_color", $"{who}: color '{l.Color}'.", "Use #RRGGBB."));
                if (!VfxEval.Blends.Contains(l.Blend)) r.Issues.Add(new("bad_vfx_layer", $"{who}: blend '{l.Blend}'.", "Use additive (light), normal or multiply (shadow)."));
                if (l.Count is < 1 or > 256) r.Issues.Add(new("bad_vfx_layer", $"{who}: count={l.Count}.", "Use between 1 and 256 particles/banks."));
                if (l.SpreadPx is < 1 or > 256) r.Issues.Add(new("bad_vfx_layer", $"{who}: spreadPx={l.SpreadPx}.", "Use between 1 and 256 px (the fog bank radius)."));
                if (l.Shape == "flash" && l.CycleMs < 1000) r.Issues.Add(new("bad_vfx_layer", $"{who}: cycleMs={l.CycleMs}.", "A weather flash is periodic lightning: use cycleMs >= 1000."));
                if (v.DurationMs > 1000 && (l.StartMs < 0 || (l.EndMs != 0 && l.EndMs <= l.StartMs) || l.EndMs > v.DurationMs || l.StartMs >= v.DurationMs)) r.Issues.Add(new("bad_vfx_layer", $"{who}: ventana {l.StartMs}..{l.EndMs} ms.", $"Use 0 <= startMs < endMs <= {v.DurationMs} (endMs 0 = the whole cycle)."));
            }
            return;
        }
        if (!background && v.DurationMs is < 100 or > 5000) r.Issues.Add(new("bad_vfx_duration", $"Vfx {v.Id}: durationMs={v.DurationMs}.", "Use between 100 and 5000 ms (backgrounds loop and ignore it)."));
        if (v.Layers.Count is 0 or > 16) { r.Issues.Add(new("bad_vfx_layer", $"Vfx {v.Id} has {v.Layers.Count} layers.", "Use between 1 and 16 layers.")); return; }
        for (var i = 0; i < v.Layers.Count; i++)
        {
            var l = v.Layers[i];
            var who = $"Vfx {v.Id} capa {i}";
            if (!VfxEval.Blends.Contains(l.Blend)) r.Issues.Add(new("bad_vfx_layer", $"{who}: blend '{l.Blend}'.", "Use additive (light), normal or multiply (shadow)."));
            if (l.SizePx is < 0 or > 64) r.Issues.Add(new("bad_vfx_layer", $"{who}: sizePx={l.SizePx}.", "Use between 0 (auto) and 64."));
            if (background)
            {
                if (!VfxEval.Patterns.Contains(l.Pattern)) r.Issues.Add(new("bad_vfx_layer", $"{who}: pattern '{l.Pattern}'.", "A background uses pattern: bands, checker, rings or waves."));
                if (l.Colors.Count is 0 or > 8) r.Issues.Add(new("bad_vfx_layer", $"{who}: {l.Colors.Count} colores.", "Use between 1 and 8 entries in colors."));
                foreach (var color in l.Colors.Where(c => !HexColor.IsMatch(c))) r.Issues.Add(new("bad_vfx_color", $"{who}: color '{color}'.", "Use #RRGGBB."));
                if (l.CycleMs != 0 && l.CycleMs < 50) r.Issues.Add(new("bad_vfx_layer", $"{who}: cycleMs={l.CycleMs}.", "Use 0 (no cycling) or >= 50 ms."));
                if (l.DistortAmp is < 0 or > 64) r.Issues.Add(new("bad_vfx_layer", $"{who}: distortAmp={l.DistortAmp}.", "Use between 0 and 64 px."));
            }
            else
            {
                if (!VfxEval.Shapes.Contains(l.Shape)) r.Issues.Add(new("bad_vfx_layer", $"{who}: shape '{l.Shape}'.", "An impact uses shape: flash, spark, ring, slash or beam."));
                if (!HexColor.IsMatch(l.Color)) r.Issues.Add(new("bad_vfx_color", $"{who}: color '{l.Color}'.", "Use #RRGGBB."));
                if (!VfxEval.Motions.Contains(l.Motion)) r.Issues.Add(new("bad_vfx_layer", $"{who}: motion '{l.Motion}'.", "Use burst, rise, fall, spiral or expand."));
                if (l.Count is < 1 or > 64) r.Issues.Add(new("bad_vfx_layer", $"{who}: count={l.Count}.", "Use between 1 and 64 particles."));
                if (l.SpreadPx is < 1 or > 256) r.Issues.Add(new("bad_vfx_layer", $"{who}: spreadPx={l.SpreadPx}.", "Use between 1 and 256 px."));
                if (l.StartMs < 0 || (l.EndMs != 0 && l.EndMs <= l.StartMs) || l.EndMs > v.DurationMs || l.StartMs >= v.DurationMs) r.Issues.Add(new("bad_vfx_layer", $"{who}: ventana {l.StartMs}..{l.EndMs} ms.", $"Use 0 <= startMs < endMs <= {v.DurationMs} (endMs 0 = until the end)."));
            }
        }
    }

    static readonly Regex NoteRegex = new("^(R|[A-G][#b]?[0-8])(:[0-9]{1,3})?$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    static void Song(SongDef s, ValidationResult r)
    {
        Id(s.Id, "song", r);
        if (s.Tempo is < 20 or > 600) r.Issues.Add(new("bad_song_tempo", $"Cancion {s.Id}: tempo {s.Tempo}.", "Use between 20 and 600 beats per minute."));
        for (var i = 0; i < s.Channels.Count; i++)
        {
            var c = s.Channels[i];
            if (!Waves.Contains(c.Wave.ToLowerInvariant())) r.Issues.Add(new("bad_song_wave", $"Cancion {s.Id} canal {i}: onda '{c.Wave}'.", "Use square, triangle, saw or noise."));
            if (c.Volume is < 0 or > 1) r.Issues.Add(new("bad_song_channel", $"Cancion {s.Id} canal {i}: volume {c.Volume}.", "Use between 0 and 1."));
            if (c.Duty is < 0.05 or > 0.95) r.Issues.Add(new("bad_song_channel", $"Cancion {s.Id} canal {i}: duty {c.Duty}.", "Use between 0.05 and 0.95."));
            if (c.AttackMs is < 0 or > 2000 || c.ReleaseMs is < 0 or > 2000) r.Issues.Add(new("bad_song_channel", $"Cancion {s.Id} canal {i}: envolvente {c.AttackMs}/{c.ReleaseMs} ms.", "Use attack/release between 0 and 2000 ms."));
            foreach (var n in c.Notes.Where(n => !NoteRegex.IsMatch(n)))
                r.Issues.Add(new("bad_song_note", $"Cancion {s.Id} canal {i}: nota '{n}'.", "Use 'C4', 'G#3', 'Bb2' or 'R' (rest), with an optional ':N' pulse count (e.g. 'C4:2')."));
        }
    }

    static void Font(FontDef f, ValidationResult r)
    {
        Id(f.Id, "font", r);
        if (string.IsNullOrWhiteSpace(f.Image)) r.Issues.Add(new("font_no_image", $"Font {f.Id} has no image.", "Give the path of the PNG holding the glyph grid."));
        if (string.IsNullOrWhiteSpace(f.Charset)) r.Issues.Add(new("font_no_charset", $"Font {f.Id} has no charset.", "List the characters in the grid's reading order."));
        else foreach (var dup in f.Charset.GroupBy(c => c).Where(g => g.Count() > 1)) r.Issues.Add(new("font_dup_char", $"Font {f.Id}: character '{dup.Key}' is repeated in charset.", "Each character exactly once."));
        if (f.GlyphWidth is < 1 or > 32 || f.GlyphHeight is < 1 or > 32) r.Issues.Add(new("font_bad_glyph", $"Fuente {f.Id}: glifos {f.GlyphWidth}x{f.GlyphHeight}.", "Use between 1 and 32 pixels."));
    }
    static void Id(string id, string owner, ValidationResult r) { if (string.IsNullOrWhiteSpace(id) || !IdRegex.IsMatch(id)) r.Issues.Add(new("bad_id", $"{owner} has an invalid id '{id}'.", "Use lowercase letters, digits, dots and dashes.")); }
    static void Unique(IEnumerable<string> ids, string scope, ValidationResult r) { foreach (var d in ids.GroupBy(x => x).Where(x => x.Count() > 1).Select(x => x.Key)) r.Issues.Add(new("duplicate_id", $"Duplicate id '{d}' in {scope}.", "Renombrar uno.")); }
    static void Ref(string id, HashSet<string> scope, string code, string msg, string fix, ValidationResult r) { if (string.IsNullOrWhiteSpace(id) || !scope.Contains(id)) r.Issues.Add(new(code, msg, fix)); }
}
