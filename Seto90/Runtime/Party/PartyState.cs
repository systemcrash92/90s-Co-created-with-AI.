namespace Seto90;

/// <summary>
/// Miembro de party en runtime: nivel, EXP y HP actuales viviendo SOBRE un ActorDef autorado.
/// Los stats efectivos se derivan de base + growth * niveles ganados; nunca se escriben al def.
///
/// Nota de diseno: en los 90 la curva de EXP era una tabla en ROM y el "level up" parcheaba
/// los stats del heroe en SRAM; un bit corrupto y el heroe quedaba roto para siempre. Aca el
/// dato autorado es inmutable y el estado derivado se recalcula: guardar solo persiste
/// (nivel, exp, hp), que siempre se puede reconstruir.
/// </summary>
public sealed class PartyMember
{
    public ActorDef Def { get; private set; }
    public int Level { get; private set; }
    public int Exp { get; private set; }
    public int Hp { get; set; }
    public int Mp { get; set; }
    public StatBlock Stats { get; private set; } = new();

    public static readonly StatBlock DefaultGrowth = new() { Hp = 4, Mp = 1, Attack = 2, Defense = 1, Speed = 1 };

    public PartyMember(ActorDef def, int? level = null, int? exp = null, int? hp = null, int? mp = null)
    {
        Def = def;
        Level = Math.Max(1, level ?? def.Level);
        Exp = Math.Max(0, exp ?? 0);
        RecomputeStats();
        Hp = Math.Clamp(hp ?? Stats.Hp, 0, Stats.Hp);
        Mp = Math.Clamp(mp ?? Stats.Mp, 0, Stats.Mp);
    }

    public bool Alive => Hp > 0;
    public int MaxHp => Stats.Hp;

    /// <summary>Equipo puesto: referencia al ItemDef autorado (nunca copia). El save persiste solo el id.</summary>
    public ItemDef? Weapon { get; private set; }
    public ItemDef? Armor { get; private set; }

    /// <summary>Equipa el item en su slot y devuelve lo que estaba puesto (para volver al inventario).</summary>
    public ItemDef? Equip(ItemDef item)
    {
        ItemDef? previous;
        if (item.Slot.Equals("weapon", StringComparison.OrdinalIgnoreCase)) { previous = Weapon; Weapon = item; }
        else { previous = Armor; Armor = item; }
        RecomputeStats();
        Hp = Math.Clamp(Hp, 0, Stats.Hp);
        Mp = Math.Clamp(Mp, 0, Stats.Mp);
        return previous;
    }

    /// <summary>Saca lo puesto en el slot ("weapon"/"armor") y lo devuelve.</summary>
    public ItemDef? Unequip(string slot)
    {
        ItemDef? removed;
        if (slot.Equals("weapon", StringComparison.OrdinalIgnoreCase)) { removed = Weapon; Weapon = null; }
        else { removed = Armor; Armor = null; }
        RecomputeStats();
        Hp = Math.Clamp(Hp, 0, Stats.Hp);
        Mp = Math.Clamp(Mp, 0, Stats.Mp);
        return removed;
    }

    /// <summary>EXP para pasar del nivel dado al siguiente: 10 + 5*nivel^2 (curva cuadratica clasica).</summary>
    public static int ExpToNext(int level) => 10 + level * level * 5;

    /// <summary>Suma EXP y aplica todos los niveles que correspondan. El HP ganado viene curado
    /// (la alegria del level up de los 90). Devuelve cuantos niveles subio.</summary>
    public int GainExp(int amount)
    {
        Exp += Math.Max(0, amount);
        var gained = 0;
        while (Exp >= ExpToNext(Level))
        {
            Exp -= ExpToNext(Level);
            Level++;
            gained++;
            var beforeMax = Stats.Hp;
            var beforeMp = Stats.Mp;
            RecomputeStats();
            Hp = Math.Min(Stats.Hp, Hp + Math.Max(0, Stats.Hp - beforeMax));
            Mp = Math.Min(Stats.Mp, Mp + Math.Max(0, Stats.Mp - beforeMp));
        }
        return gained;
    }

    /// <summary>Reengancha el def (hot reload / carga) preservando nivel, EXP y HP.</summary>
    public void Rebind(ActorDef def)
    {
        Def = def;
        RecomputeStats();
        Hp = Math.Clamp(Hp, 0, Stats.Hp);
        Mp = Math.Clamp(Mp, 0, Stats.Mp);
    }

    void RecomputeStats()
    {
        var growth = Def.Growth ?? DefaultGrowth;
        var steps = Math.Max(0, Level - Math.Max(1, Def.Level));
        var weapon = Weapon?.Bonus ?? Zero;
        var armor = Armor?.Bonus ?? Zero;
        Stats = new StatBlock
        {
            Hp = Def.Stats.Hp + growth.Hp * steps + weapon.Hp + armor.Hp,
            Mp = Def.Stats.Mp + growth.Mp * steps + weapon.Mp + armor.Mp,
            Attack = Def.Stats.Attack + growth.Attack * steps + weapon.Attack + armor.Attack,
            Defense = Def.Stats.Defense + growth.Defense * steps + weapon.Defense + armor.Defense,
            Speed = Def.Stats.Speed + growth.Speed * steps + weapon.Speed + armor.Speed,
        };
    }

    static readonly StatBlock Zero = new() { Hp = 0, Mp = 0, Attack = 0, Defense = 0, Speed = 0 };

    /// <summary>Reengancha el equipo tras hot reload/carga: si el item ya no existe o cambio de slot, cae.</summary>
    public void RebindEquipment(GameProject project)
    {
        Weapon = Weapon == null ? null : project.Items.FirstOrDefault(i => i.Id == Weapon.Id && i.Slot.Equals("weapon", StringComparison.OrdinalIgnoreCase));
        Armor = Armor == null ? null : project.Items.FirstOrDefault(i => i.Id == Armor.Id && i.Slot.Equals("armor", StringComparison.OrdinalIgnoreCase));
        RecomputeStats();
        Hp = Math.Clamp(Hp, 0, Stats.Hp);
        Mp = Math.Clamp(Mp, 0, Stats.Mp);
    }
}

/// <summary>La party viva del runtime: quien pelea, quien recibe EXP, a quien curan los items.</summary>
public sealed class PartyState
{
    public List<PartyMember> Members { get; } = [];

    public static PartyState Create(GameProject project)
    {
        var party = new PartyState();
        var ids = project.PartyActorIds.Count > 0 ? project.PartyActorIds : [.. project.Actors.Take(1).Select(a => a.Id)];
        foreach (var id in ids)
        {
            var def = project.Actors.FirstOrDefault(a => a.Id == id);
            if (def != null) party.Members.Add(new PartyMember(def));
        }
        if (party.Members.Count == 0) party.Members.Add(new PartyMember(new ActorDef { Id = "actor.default", Name = "Heroe" }));
        return party;
    }

    public PartyMember? FirstAlive => Members.FirstOrDefault(m => m.Alive);

    /// <summary>Cada miembro vivo recibe la EXP completa (no se reparte). Devuelve los avisos de nivel.</summary>
    public List<string> GrantExp(int amount)
    {
        var messages = new List<string>();
        foreach (var member in Members.Where(m => m.Alive))
        {
            if (member.GainExp(amount) > 0) messages.Add($"{member.Def.Name} sube a nivel {member.Level}!");
        }
        return messages;
    }

    /// <summary>Usa un item consumible desde el menu: heal:N cura al vivo mas lastimado,
    /// revive:N levanta al primer caido. Mensaje, o null si no aplica.</summary>
    public string? UseHealItem(ItemDef item)
    {
        if (item.Effect.StartsWith("revive:", StringComparison.OrdinalIgnoreCase))
        {
            var hp = int.TryParse(item.Effect.Split(':', 2)[1], out var rev) ? rev : 0;
            var fallen = Members.FirstOrDefault(m => !m.Alive);
            if (hp <= 0 || fallen == null) return null;
            fallen.Hp = Math.Clamp(hp, 1, fallen.MaxHp);
            return $"{fallen.Def.Name} vuelve en si con {fallen.Hp} HP gracias a {item.Name}.";
        }
        if (!item.Effect.StartsWith("heal:", StringComparison.OrdinalIgnoreCase)) return null;
        var amount = int.TryParse(item.Effect.Split(':', 2)[1], out var parsed) ? parsed : 0;
        if (amount <= 0) return null;
        var target = Members.Where(m => m.Alive && m.Hp < m.MaxHp).OrderBy(m => (float)m.Hp / m.MaxHp).FirstOrDefault();
        if (target == null) return null;
        var healed = Math.Min(amount, target.MaxHp - target.Hp);
        target.Hp += healed;
        return $"{target.Def.Name} recupera {healed} HP con {item.Name}.";
    }

    /// <summary>Suma un actor a la party en runtime (comando AddPartyMember). No duplica.
    /// Devuelve el aviso, o null si ya estaba o el actor no existe.</summary>
    public string? AddMember(GameProject project, string actorId)
    {
        if (Members.Any(m => m.Def.Id == actorId)) return null;
        var def = project.Actors.FirstOrDefault(a => a.Id == actorId);
        if (def == null) return null;
        Members.Add(new PartyMember(def));
        return $"{def.Name} se une a la party!";
    }

    /// <summary>Saca un actor de la party (comando RemovePartyMember). Nunca la deja vacia.</summary>
    public string? RemoveMember(string actorId)
    {
        var member = Members.FirstOrDefault(m => m.Def.Id == actorId);
        if (member == null || Members.Count <= 1) return null;
        Members.Remove(member);
        return $"{member.Def.Name} deja la party.";
    }

    /// <summary>Descanso de posada: HP/MP al maximo y los caidos vuelven en si.</summary>
    public void RestAll()
    {
        foreach (var member in Members)
        {
            member.Hp = member.MaxHp;
            member.Mp = member.Stats.Mp;
        }
    }

    /// <summary>Reengancha todos los defs tras un hot reload; los actores borrados salen de la party.</summary>
    public void Rebind(GameProject project)
    {
        for (var i = Members.Count - 1; i >= 0; i--)
        {
            var def = project.Actors.FirstOrDefault(a => a.Id == Members[i].Def.Id);
            if (def != null) { Members[i].Rebind(def); Members[i].RebindEquipment(project); }
            else if (Members.Count > 1) Members.RemoveAt(i);
        }
    }
}
