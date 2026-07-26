namespace Seto90;

/// <summary>Un combatiente en batalla: proyeccion mutable de un PartyMember o un EnemyDef.</summary>
public sealed class BattleCombatant
{
    public string Name = "";
    public bool IsPlayer;
    public int Level = 1;
    public StatBlock Stats = new();
    public int Hp;
    public int Mp;
    public bool Defending;
    public bool Poisoned;
    /// <summary>Turnos propios que le quedan por dormir (0 = despierto). Un golpe lo despierta.</summary>
    public int SleepTurns;
    public PartyMember? Member;
    public EnemyDef? Enemy;
    public bool Alive => Hp > 0;
    /// <summary>Etiqueta corta de estados para la UI ("VEN", "DOR" o ambas).</summary>
    public string StatusTag => string.Join(" ", new[] { Poisoned ? UiStrings.TagPoison : "", SleepTurns > 0 ? UiStrings.TagSleep : "" }.Where(t => t != ""));
}

/// <summary>
/// Motor de combate por turnos: party completa contra multiples enemigos, en orden de velocidad
/// por ronda. Comandos: Atacar (elegir blanco), Skill (dano con blanco o cura automatica, gasta MP),
/// Item (antidoto si hace falta, si no cura al mas lastimado), Defender (mitad de dano hasta tu
/// proximo turno) y Huir (sale si tu mas rapido iguala o supera al de ellos). Logica pura, sin
/// raylib: battle-smoke la recorre entera.
///
/// Estados alterados (deterministas, sin RNG, como todo el motor): veneno descuenta max(1, maxHp/8)
/// al empezar cada turno del envenenado y dura todo el combate (o hasta un cure:poison); dormir
/// salta 2 turnos propios y cualquier golpe despierta. Un golpe que despierta no puede volver a
/// dormir en el mismo impacto (evita el bloqueo infinito de un enemigo que duerme siempre).
///
/// Nota de diseno: hay dos escuelas — resolver la ronda completa despues de elegir todos los
/// comandos, o resolver accion por accion. Elegimos accion-por-accion porque el log cuenta la
/// batalla como una conversacion y el estado entre acciones siempre es consistente.
/// Los turnos enemigos corren solos hasta que vuelve a tocarle a un jugador.
/// </summary>
public sealed class BattleEngine
{
    public enum Phase { Command, SkillSelect, TargetSelect, Resolved }

    public static string[] Commands => UiStrings.BattleCommands;

    public BattleDef Battle { get; }
    readonly GameProject project;
    readonly List<string> inventory;

    public List<BattleCombatant> Party { get; } = [];
    public List<BattleCombatant> Enemies { get; } = [];
    public Phase Current { get; private set; } = Phase.Command;
    public string Log { get; private set; } = "";
    public bool Resolved { get; private set; }
    public bool Victory { get; private set; }
    public bool Defeat { get; private set; }
    public bool Fled { get; private set; }
    public int SelectedCommand { get; set; }
    public int SelectedSkill { get; set; }
    public int SelectedTarget { get; set; }

    public enum TargetMode { Enemies, Allies, Fallen }
    /// <summary>A quien apunta el TargetSelect actual: enemigos (atacar/damage), aliados vivos (heal) o caidos (revive).</summary>
    public TargetMode Targeting { get; private set; } = TargetMode.Enemies;
    public bool TargetingAllies => Targeting == TargetMode.Allies;
    public bool TargetingFallen => Targeting == TargetMode.Fallen;

    /// <summary>Cuantos turnos propios duerme quien recibe sleep.</summary>
    public const int SleepDuration = 2;

    // Feedback de un paso para el runtime (flash y numero flotante del enemigo golpeado, shake si
    // te pegaron, skill usada y aliado curado/revivido para elegir y anclar el VFX de impacto).
    int lastHitEnemy = -1;
    int lastHitDamage;
    bool playerWasHit;
    string lastSkillId = "";
    int lastHealedAlly = -1;
    int lastHitAlly = -1; // indice de party golpeado (ataque enemigo o veneno): ancla su VFX de impacto
    int lastHealAmount;      // HP recuperados por el curado/revivido: el pop "+N" verde
    int lastHitAllyDamage;   // dano recibido por el aliado golpeado: el pop "-N" rojo

    readonly Queue<BattleCombatant> turnOrder = new();
    string pendingSkillId = "";

    public BattleEngine(BattleDef battle, GameProject project, PartyState party, List<string> inventory)
    {
        Battle = battle;
        this.project = project;
        this.inventory = inventory;
        UiStrings.Use(project.Render.Language); // el log del combate tambien es UI del motor
        foreach (var member in party.Members.Where(m => m.Alive))
            Party.Add(new BattleCombatant { Name = member.Def.Name, IsPlayer = true, Level = member.Level, Stats = member.Stats, Hp = member.Hp, Mp = member.Mp, Member = member });
        if (Party.Count == 0 && party.Members.Count > 0)
        {
            var fallback = party.Members[0];
            Party.Add(new BattleCombatant { Name = fallback.Def.Name, IsPlayer = true, Level = fallback.Level, Stats = fallback.Stats, Hp = 1, Mp = fallback.Mp, Member = fallback });
        }
        foreach (var id in battle.EnemyIds)
        {
            var def = project.Enemies.First(e => e.Id == id);
            Enemies.Add(new BattleCombatant { Name = def.Name, Level = 1, Stats = def.Stats, Hp = Math.Max(1, def.Stats.Hp), Enemy = def });
        }
        Log = Enemies.Count == 1 ? UiStrings.EnemyAppears(Enemies[0].Name) : UiStrings.EnemiesAppear(string.Join(", ", Enemies.Select(e => e.Name)));
        StartRound();
    }

    public BattleCombatant? Acting => turnOrder.Count > 0 ? turnOrder.Peek() : null;
    public int TotalExp => Enemies.Sum(e => e.Enemy?.Exp ?? 0);
    public int TotalMoney => Enemies.Sum(e => e.Enemy?.Money ?? 0);
    public List<int> AliveEnemyIndexes => [.. Enumerable.Range(0, Enemies.Count).Where(i => Enemies[i].Alive)];
    public List<int> AlivePartyIndexes => [.. Enumerable.Range(0, Party.Count).Where(i => Party[i].Alive)];
    public List<int> FallenPartyIndexes => [.. Enumerable.Range(0, Party.Count).Where(i => !Party[i].Alive)];

    /// <summary>Cuantos blancos hay en el modo de seleccion actual (para ciclar el cursor).</summary>
    public int TargetCount => Targeting switch
    {
        TargetMode.Allies => AlivePartyIndexes.Count,
        TargetMode.Fallen => FallenPartyIndexes.Count,
        _ => AliveEnemyIndexes.Count,
    };

    /// <summary>Skills que sabe el que esta actuando (resueltas contra el proyecto).</summary>
    public List<SkillDef> ActingSkills =>
        Acting?.Member == null ? [] : [.. Acting.Member.Def.SkillIds.Select(id => project.Skills.FirstOrDefault(s => s.Id == id)).Where(s => s != null).Cast<SkillDef>()];

    /// <summary>Devuelve y limpia el feedback visual del ultimo paso: enemigo golpeado y con cuanto,
    /// jugador golpeado, skill usada ("" = ataque basico/item), aliado curado o revivido (-1 =
    /// ninguno) y aliado golpeado por el enemigo o el veneno (-1 = ninguno; ancla su impacto).</summary>
    public (int HitEnemy, int Damage, bool PlayerHit, string SkillId, int HealedAlly, int HealAmount, int HitAlly, int HitAllyDamage) ConsumeFeedback()
    {
        var result = (lastHitEnemy, lastHitDamage, playerWasHit, lastSkillId, lastHealedAlly, lastHealAmount, lastHitAlly, lastHitAllyDamage);
        lastHitEnemy = -1;
        lastHitDamage = 0;
        playerWasHit = false;
        lastSkillId = "";
        lastHealedAlly = -1;
        lastHitAlly = -1;
        lastHealAmount = 0;
        lastHitAllyDamage = 0;
        return result;
    }

    public void ConfirmCommand()
    {
        if (Current != Phase.Command || Acting is not { IsPlayer: true } actor) return;
        switch (SelectedCommand)
        {
            case 0: // Atacar
                pendingSkillId = "";
                Targeting = TargetMode.Enemies;
                SelectedTarget = 0;
                Current = Phase.TargetSelect;
                break;
            case 1: // Skill
                if (ActingSkills.Count == 0) { Log = UiStrings.NoSkills(actor.Name); return; }
                SelectedSkill = 0;
                Current = Phase.SkillSelect;
                break;
            case 2: // Item
                UseItem(actor);
                break;
            case 3: // Defender
                actor.Defending = true;
                Log = UiStrings.Defends(actor.Name);
                EndPlayerTurn();
                break;
            case 4: // Huir
                TryFlee(actor);
                break;
        }
    }

    public void ConfirmSkill()
    {
        if (Current != Phase.SkillSelect || Acting is not { IsPlayer: true } actor) return;
        var skills = ActingSkills;
        var skill = skills[Math.Clamp(SelectedSkill, 0, skills.Count - 1)];
        if (actor.Mp < skill.MpCost) { Log = UiStrings.NoMp(actor.Name, skill.Name, skill.MpCost); return; }
        if (skill.Kind.Equals("revive", StringComparison.OrdinalIgnoreCase) && FallenPartyIndexes.Count == 0) { Log = UiStrings.NoOneFallen; return; }
        pendingSkillId = skill.Id;
        // Heals y revives tambien eligen blanco: cursor sobre los paneles de la party.
        Targeting = skill.Kind.ToLowerInvariant() switch
        {
            "heal" => TargetMode.Allies,
            "revive" => TargetMode.Fallen,
            _ => TargetMode.Enemies,
        };
        SelectedTarget = 0;
        Current = Phase.TargetSelect;
    }

    public void ConfirmTarget()
    {
        if (Current != Phase.TargetSelect || Acting is not { IsPlayer: true } actor) return;
        if (Targeting == TargetMode.Allies) { ConfirmAllyTarget(actor); return; }
        if (Targeting == TargetMode.Fallen) { ConfirmReviveTarget(actor); return; }
        var alive = AliveEnemyIndexes;
        if (alive.Count == 0) return;
        var index = alive[Math.Clamp(SelectedTarget, 0, alive.Count - 1)];
        var target = Enemies[index];

        if (pendingSkillId == "")
        {
            var damage = FormulaValidator.EvalBasicDamage(Battle.DamageFormula, actor.Stats, target.Stats, actor.Level);
            Log = UiStrings.Attacks(actor.Name, target.Name, damage);
            DamageAndStatus(target, damage, "");
            lastHitDamage = damage;
        }
        else
        {
            var skill = project.Skills.First(s => s.Id == pendingSkillId);
            actor.Mp -= skill.MpCost;
            var damage = Math.Max(1, skill.Power + actor.Stats.Attack / 2 - target.Stats.Defense);
            Log = UiStrings.UsesSkillDamage(actor.Name, skill.Name, target.Name, damage);
            DamageAndStatus(target, damage, skill.Status);
            lastHitDamage = damage;
            lastSkillId = skill.Id;
        }
        lastHitEnemy = index;
        EndPlayerTurn();
    }

    void ConfirmAllyTarget(BattleCombatant actor)
    {
        var alive = AlivePartyIndexes;
        if (alive.Count == 0) return;
        var target = Party[alive[Math.Clamp(SelectedTarget, 0, alive.Count - 1)]];
        var skill = project.Skills.First(s => s.Id == pendingSkillId);
        actor.Mp -= skill.MpCost;
        var healed = Math.Min(skill.Power, target.Stats.Hp - target.Hp);
        target.Hp += healed;
        var woke = target.SleepTurns > 0;
        target.SleepTurns = 0;
        Log = UiStrings.UsesSkillHeal(actor.Name, skill.Name, target.Name, healed) + (woke ? UiStrings.WakesUp(target.Name) : "");
        lastSkillId = skill.Id;
        lastHealedAlly = Party.IndexOf(target);
        lastHealAmount = healed;
        Targeting = TargetMode.Enemies;
        EndPlayerTurn();
    }

    void ConfirmReviveTarget(BattleCombatant actor)
    {
        var fallen = FallenPartyIndexes;
        if (fallen.Count == 0) return;
        var target = Party[fallen[Math.Clamp(SelectedTarget, 0, fallen.Count - 1)]];
        var skill = project.Skills.First(s => s.Id == pendingSkillId);
        actor.Mp -= skill.MpCost;
        Revive(target, skill.Power);
        Log = UiStrings.UsesSkillRevive(actor.Name, skill.Name, target.Name, target.Hp);
        lastSkillId = skill.Id;
        lastHealedAlly = Party.IndexOf(target);
        lastHealAmount = target.Hp;
        Targeting = TargetMode.Enemies;
        EndPlayerTurn();
    }

    /// <summary>Vuelve a un caido con hasta N HP, limpio de estados (la muerte los borra, como en los 90).</summary>
    static void Revive(BattleCombatant target, int hp)
    {
        target.Hp = Math.Clamp(hp, 1, target.Stats.Hp);
        target.Poisoned = false;
        target.SleepTurns = 0;
        target.Defending = false;
    }

    /// <summary>Aplica dano y el estado que el golpe inflige. Golpe que despierta no duerme de nuevo.</summary>
    void DamageAndStatus(BattleCombatant target, int damage, string inflicts)
    {
        var wasAsleep = target.SleepTurns > 0;
        target.Hp = Math.Max(0, target.Hp - damage);
        if (!target.Alive) { Log += UiStrings.Falls(target.Name); return; }
        if (wasAsleep) { target.SleepTurns = 0; Log += UiStrings.WakesUp(target.Name); return; }
        if (inflicts.Equals("poison", StringComparison.OrdinalIgnoreCase) && !target.Poisoned) { target.Poisoned = true; Log += UiStrings.GetsPoison(target.Name); }
        else if (inflicts.Equals("sleep", StringComparison.OrdinalIgnoreCase) && target.SleepTurns == 0) { target.SleepTurns = SleepDuration; Log += UiStrings.GetsSleep(target.Name); }
    }

    /// <summary>Vuelve al menu de comandos desde skills o eleccion de blanco.</summary>
    public void Cancel()
    {
        if (Current is Phase.SkillSelect or Phase.TargetSelect) { Current = Phase.Command; Targeting = TargetMode.Enemies; }
    }

    void UseItem(BattleCombatant actor)
    {
        // Prioridad clasica: revivir a un caido > curar estados > curar HP al mas lastimado.
        foreach (var id in inventory.Distinct().ToList())
        {
            var revive = project.Items.FirstOrDefault(i => i.Id == id);
            var effect = revive?.Effect ?? "";
            if (!effect.StartsWith("revive:", StringComparison.OrdinalIgnoreCase)) continue;
            var fallen = Party.FirstOrDefault(p => !p.Alive);
            if (fallen == null) continue;
            var hp = int.TryParse(effect.Split(':', 2)[1], out var reviveHp) ? reviveHp : 1;
            inventory.Remove(id);
            Revive(fallen, hp);
            Log = UiStrings.UsesItemRevive(actor.Name, revive!.Name, fallen.Name, fallen.Hp);
            lastHealedAlly = Party.IndexOf(fallen);
            lastHealAmount = fallen.Hp;
            EndPlayerTurn();
            return;
        }
        foreach (var id in inventory.Distinct().ToList())
        {
            var cure = project.Items.FirstOrDefault(i => i.Id == id);
            var effect = cure?.Effect ?? "";
            if (!effect.StartsWith("cure:", StringComparison.OrdinalIgnoreCase)) continue;
            var what = effect.Split(':', 2)[1].ToLowerInvariant();
            var afflicted = Party.FirstOrDefault(p => p.Alive && (
                (what is "poison" or "all" && p.Poisoned) || (what is "sleep" or "all" && p.SleepTurns > 0)));
            if (afflicted == null) continue;
            inventory.Remove(id);
            if (what is "poison" or "all") afflicted.Poisoned = false;
            if (what is "sleep" or "all") afflicted.SleepTurns = 0;
            Log = UiStrings.UsesItemCure(actor.Name, cure!.Name, afflicted.Name);
            lastHealedAlly = Party.IndexOf(afflicted);
            EndPlayerTurn();
            return;
        }
        var itemId = inventory.FirstOrDefault(id => (project.Items.FirstOrDefault(i => i.Id == id)?.Effect ?? "").StartsWith("heal:", StringComparison.OrdinalIgnoreCase));
        if (itemId == null) { Log = UiStrings.NoBattleItems; return; }
        var item = project.Items.First(i => i.Id == itemId);
        var amount = int.TryParse(item.Effect.Split(':', 2)[1], out var parsed) ? parsed : 0;
        var target = Party.Where(p => p.Alive).OrderBy(p => (float)p.Hp / Math.Max(1, p.Stats.Hp)).First();
        var healed = Math.Min(amount, target.Stats.Hp - target.Hp);
        inventory.Remove(itemId);
        target.Hp += healed;
        Log = UiStrings.UsesItemHeal(actor.Name, item.Name, target.Name, healed);
        lastHealedAlly = Party.IndexOf(target);
        lastHealAmount = healed;
        EndPlayerTurn();
    }

    void TryFlee(BattleCombatant actor)
    {
        // Determinista (sin RNG, como toda la logica del motor): escapas si tu mas rapido no es mas lento.
        var partySpeed = Party.Where(p => p.Alive).Max(p => p.Stats.Speed);
        var enemySpeed = Enemies.Where(e => e.Alive).Max(e => e.Stats.Speed);
        if (partySpeed >= enemySpeed)
        {
            Fled = true;
            Resolved = true;
            Current = Phase.Resolved;
            Log = UiStrings.PartyEscapes;
            return;
        }
        Log = UiStrings.FleeFails(actor.Name);
        EndPlayerTurn();
    }

    void EndPlayerTurn()
    {
        if (CheckEnd()) return;
        if (turnOrder.Count > 0) turnOrder.Dequeue();
        AdvanceUntilPlayer();
    }

    void StartRound()
    {
        turnOrder.Clear();
        foreach (var combatant in Party.Concat(Enemies).Where(c => c.Alive).OrderByDescending(c => c.Stats.Speed))
            turnOrder.Enqueue(combatant);
        AdvanceUntilPlayer();
    }

    /// <summary>Corre los turnos enemigos automaticamente hasta que le toque a un jugador (o termine).</summary>
    void AdvanceUntilPlayer()
    {
        while (!Resolved)
        {
            if (turnOrder.Count == 0) { StartRound(); return; }
            var acting = turnOrder.Peek();
            if (!acting.Alive) { turnOrder.Dequeue(); continue; }
            if (TurnStartEffects(acting)) { turnOrder.Dequeue(); if (CheckEnd()) return; continue; }
            if (acting.IsPlayer)
            {
                acting.Defending = false; // defender dura hasta tu proximo turno
                SelectedCommand = 0;
                Current = Phase.Command;
                return;
            }
            EnemyAct(acting);
            turnOrder.Dequeue();
            if (CheckEnd()) return;
        }
    }

    /// <summary>Veneno y sueno al empezar el turno. True si el turno se pierde (durmio o cayo).</summary>
    bool TurnStartEffects(BattleCombatant acting)
    {
        if (acting.Poisoned)
        {
            var tick = Math.Max(1, acting.Stats.Hp / 8);
            acting.Hp = Math.Max(0, acting.Hp - tick);
            if (acting.IsPlayer) { playerWasHit = true; lastHitAlly = Party.IndexOf(acting); lastHitAllyDamage = tick; }
            Log += UiStrings.PoisonTick(acting.Name, tick);
            if (!acting.Alive) { Log += UiStrings.Falls(acting.Name); return true; }
        }
        if (acting.SleepTurns > 0)
        {
            acting.SleepTurns--;
            Log += UiStrings.Sleeping(acting.Name);
            return true;
        }
        return false;
    }

    void EnemyAct(BattleCombatant enemy)
    {
        enemy.Defending = false;
        var target = Party.FirstOrDefault(p => p.Alive);
        if (target == null) return;
        var damage = FormulaValidator.EvalBasicDamage(Battle.DamageFormula, enemy.Stats, target.Stats, enemy.Level);
        if (target.Defending) damage = Math.Max(1, damage / 2);
        playerWasHit = true;
        lastHitAlly = Party.IndexOf(target);
        lastHitAllyDamage = damage;
        Log += UiStrings.EnemyHits(enemy.Name, target.Name, damage, target.Defending);
        DamageAndStatus(target, damage, enemy.Enemy?.Inflicts ?? "");
    }

    bool CheckEnd()
    {
        if (Resolved) return true;
        if (Enemies.All(e => !e.Alive))
        {
            Resolved = true;
            Victory = true;
            Current = Phase.Resolved;
            Log += UiStrings.Victory(TotalExp, TotalMoney);
            return true;
        }
        if (Party.All(p => !p.Alive))
        {
            Resolved = true;
            Defeat = true;
            Current = Phase.Resolved;
            Log += UiStrings.Defeat;
            return true;
        }
        return false;
    }

    /// <summary>Auto-juega atacando al primer blanco (smokes/CI). Devuelve los turnos consumidos.</summary>
    public int ResolveImmediatelyForSmoke()
    {
        var guard = 0;
        while (!Resolved && guard++ < 200)
        {
            if (Current == Phase.Command) { SelectedCommand = 0; ConfirmCommand(); }
            else if (Current == Phase.TargetSelect) { SelectedTarget = 0; ConfirmTarget(); }
            else break;
        }
        return guard;
    }
}
