using System.Text;

namespace Seto90;

public sealed class BattleSmokeTest
{
    readonly GameProject project;

    public BattleSmokeTest(GameProject project) => this.project = project;

    public string Run()
    {
        var sb = new StringBuilder();

        // 1) Cada combate del proyecto se auto-juega atacando (como siempre).
        foreach (var battle in project.Battles)
        {
            var party = PartyState.Create(project);
            var engine = new BattleEngine(battle, project, party, []);
            var turns = engine.ResolveImmediatelyForSmoke();
            sb.AppendLine($"{battle.Id}: victory={engine.Victory} defeat={engine.Defeat} turns={turns} enemigos={engine.Enemies.Count(e => !e.Alive)}/{engine.Enemies.Count} caidos log={engine.Log}");
        }

        // 2) Combate sintetico: 2 enemigos, skill de dano con MP y defensa. No depende del contenido de la demo.
        var synth = new GameProject
        {
            Actors = [new ActorDef { Id = "actor.a", Name = "Alfa", Level = 3, Stats = new StatBlock { Hp = 40, Mp = 10, Attack = 10, Defense = 4, Speed = 9 } }],
            Enemies =
            [
                new EnemyDef { Id = "enemy.uno", Name = "Uno", Stats = new StatBlock { Hp = 12, Attack = 6, Defense = 2, Speed = 3 }, Exp = 4, Money = 2 },
                new EnemyDef { Id = "enemy.dos", Name = "Dos", Stats = new StatBlock { Hp = 12, Attack = 6, Defense = 2, Speed = 2 }, Exp = 4, Money = 2 },
            ],
            Skills = [new SkillDef { Id = "skill.rayo", Name = "Rayo", MpCost = 3, Power = 9, Kind = "damage" }],
            Battles = [new BattleDef { Id = "battle.synth", EnemyIds = ["enemy.uno", "enemy.dos"], DamageFormula = "max(1, attack - defense)" }],
        };
        synth.Actors[0].SkillIds.Add("skill.rayo");
        var synthParty = PartyState.Create(synth);
        var e2 = new BattleEngine(synth.Battles[0], synth, synthParty, []);

        // Turno 1: skill Rayo al segundo enemigo. 9 + 10/2 - 2 = 12 => Dos cae de un golpe.
        e2.SelectedCommand = 1; e2.ConfirmCommand();
        e2.SelectedSkill = 0; e2.ConfirmSkill();
        e2.SelectedTarget = 1; e2.ConfirmTarget();
        if (e2.Enemies[1].Alive) return sb.AppendLine("FALLO: el skill no elimino al segundo enemigo.").ToString();
        if (e2.Party[0].Mp != 7) return sb.AppendLine($"FALLO: MP esperado 7, quedo {e2.Party[0].Mp}.").ToString();

        // Turno 2: defender (el golpe enemigo llega a la mitad).
        var hpBefore = e2.Party[0].Hp;
        e2.SelectedCommand = 3; e2.ConfirmCommand();
        var expected = Math.Max(1, Math.Max(1, 6 - 4) / 2);
        if (hpBefore - e2.Party[0].Hp != expected) return sb.AppendLine($"FALLO: defensa no redujo el dano ({hpBefore - e2.Party[0].Hp} vs {expected}).").ToString();

        // Resto: atacar hasta ganar.
        e2.ResolveImmediatelyForSmoke();
        if (!e2.Victory) return sb.AppendLine("FALLO: el combate sintetico no termino en victoria.").ToString();
        sb.AppendLine($"battle.synth: victory=True skillMp=OK defensa=OK botin={e2.TotalExp}exp/{e2.TotalMoney}oro log={e2.Log}");

        // 3) Estados alterados: dormir (y despertar a golpes), veneno del enemigo, antidoto y
        //    heal con blanco elegido. Todo determinista: los numeros de abajo son exactos.
        var status = new GameProject
        {
            Actors = [new ActorDef { Id = "actor.b", Name = "Beta", Level = 3, Stats = new StatBlock { Hp = 60, Mp = 12, Attack = 8, Defense = 10, Speed = 10 } }],
            Enemies = [new EnemyDef { Id = "enemy.vibora", Name = "Vibora", Stats = new StatBlock { Hp = 30, Attack = 6, Defense = 2, Speed = 5 }, Exp = 6, Money = 3, Inflicts = "poison" }],
            Skills =
            [
                new SkillDef { Id = "skill.somnifero", Name = "Somnifero", MpCost = 2, Power = 1, Kind = "damage", Status = "sleep" },
                new SkillDef { Id = "skill.cura", Name = "Cura", MpCost = 3, Power = 10, Kind = "heal" },
            ],
            Items =
            [
                new ItemDef { Id = "item.antidoto", Name = "Antidoto", Effect = "cure:poison" },
                new ItemDef { Id = "item.pancito", Name = "Pancito", Effect = "heal:10" },
            ],
            Battles = [new BattleDef { Id = "battle.status", EnemyIds = ["enemy.vibora"], DamageFormula = "max(1, attack - defense)" }],
        };
        status.Actors[0].SkillIds.AddRange(["skill.somnifero", "skill.cura"]);
        var bag = new List<string> { "item.antidoto", "item.pancito" };
        var e3 = new BattleEngine(status.Battles[0], status, PartyState.Create(status), bag);
        var beta = e3.Party[0];
        var vibora = e3.Enemies[0];

        // Turno 1: somnifero. Dano 1+8/2-2=3; la vibora se duerme (2 turnos) y pierde el primero ya.
        e3.SelectedCommand = 1; e3.ConfirmCommand();
        e3.SelectedSkill = 0; e3.ConfirmSkill();
        e3.SelectedTarget = 0; e3.ConfirmTarget();
        if (vibora.Hp != 27 || vibora.SleepTurns != 1 || !e3.Log.Contains(UiStrings.GetsSleep(vibora.Name).Trim()) || !e3.Log.Contains(UiStrings.Sleeping(vibora.Name).Trim()))
            return sb.AppendLine($"FALLO sleep: hp={vibora.Hp} turnos={vibora.SleepTurns} log={e3.Log}").ToString();
        if (beta.Hp != 60) return sb.AppendLine("FALLO sleep: el enemigo dormido pego igual.").ToString();

        // Turno 2: atacar despierta (golpe que despierta no vuelve a dormir). La vibora responde:
        // 1 de dano + veneno, y el tick del veneno del proximo turno de Beta ya corre (60/8=7).
        // 60 - 1 - 7 = 52.
        e3.SelectedCommand = 0; e3.ConfirmCommand();
        e3.SelectedTarget = 0; e3.ConfirmTarget();
        if (vibora.Hp != 21 || vibora.SleepTurns != 0 || !e3.Log.Contains(UiStrings.WakesUp(vibora.Name).Trim()))
            return sb.AppendLine($"FALLO despertar: hp={vibora.Hp} turnos={vibora.SleepTurns} log={e3.Log}").ToString();
        if (beta.Hp != 52 || !beta.Poisoned) return sb.AppendLine($"FALLO veneno: hp={beta.Hp} (esperaba 52) poisoned={beta.Poisoned}").ToString();

        // Turno 3: antidoto; la vibora re-envenena al pegar (52-1=51) y el tick vuelve (51-7=44).
        e3.SelectedCommand = 2; e3.ConfirmCommand();
        if (bag.Contains("item.antidoto") || !bag.Contains("item.pancito"))
            return sb.AppendLine($"FALLO antidoto: inventario={string.Join(",", bag)}").ToString();
        if (beta.Hp != 44 || !beta.Poisoned) return sb.AppendLine($"FALLO antidoto: hp={beta.Hp} (esperaba 44) poisoned={beta.Poisoned}").ToString();
        if (!e3.Log.Contains("Antidoto")) return sb.AppendLine($"FALLO antidoto: no se uso. log={e3.Log}").ToString();

        // Turno 4: heal con blanco elegido (44+10=54; golpe 1 => 53; tick 7 => 46). MP 12-2-3=7.
        e3.SelectedCommand = 1; e3.ConfirmCommand();
        e3.SelectedSkill = 1; e3.ConfirmSkill();
        if (e3.Current != BattleEngine.Phase.TargetSelect || !e3.TargetingAllies)
            return sb.AppendLine("FALLO heal: no pide blanco aliado.").ToString();
        e3.SelectedTarget = 0; e3.ConfirmTarget();
        if (beta.Hp != 46 || beta.Mp != 7) return sb.AppendLine($"FALLO heal: hp={beta.Hp} mp={beta.Mp} (esperaba 46/7)").ToString();

        // Resto: atacar hasta ganar (el veneno sigue picando pero Beta aguanta).
        e3.ResolveImmediatelyForSmoke();
        if (!e3.Victory) return sb.AppendLine($"FALLO: el combate de estados no termino en victoria. log={e3.Log}").ToString();
        sb.AppendLine($"battle.status: victory=True sleep=OK despertar=OK veneno=OK antidoto=OK healBlanco=OK log={e3.Log}");

        // 4) Derrota: enemigo aplastante mas rapido. El motor reporta Defeat (el runtime muestra GAME OVER).
        var doom = new GameProject
        {
            Actors = [new ActorDef { Id = "actor.c", Name = "Gamma", Stats = new StatBlock { Hp = 10, Attack = 1, Defense = 0, Speed = 1 } }],
            Enemies = [new EnemyDef { Id = "enemy.tanque", Name = "Tanque", Stats = new StatBlock { Hp = 50, Attack = 30, Defense = 20, Speed = 9 } }],
            Battles = [new BattleDef { Id = "battle.doom", EnemyIds = ["enemy.tanque"], DamageFormula = "max(1, attack - defense)" }],
        };
        var e4 = new BattleEngine(doom.Battles[0], doom, PartyState.Create(doom), []);
        e4.ResolveImmediatelyForSmoke();
        if (!e4.Defeat) return sb.AppendLine($"FALLO: la derrota no se reporto. log={e4.Log}").ToString();
        sb.AppendLine($"battle.doom: defeat=True (game over real en runtime) log={e4.Log}");

        // 5) Revivir: party de 2, el fragil cae, la skill revive lo levanta (elige blanco caido),
        //    el ogro lo vuelve a tumbar y la pluma (revive:5) lo levanta de nuevo. Numeros exactos.
        var revival = new GameProject
        {
            Actors =
            [
                new ActorDef { Id = "actor.eco", Name = "Eco", Stats = new StatBlock { Hp = 10, Mp = 0, Attack = 4, Defense = 0, Speed = 8 } },
                new ActorDef { Id = "actor.delta", Name = "Delta", Stats = new StatBlock { Hp = 40, Mp = 10, Attack = 30, Defense = 2, Speed = 10 }, SkillIds = ["skill.levantar"] },
            ],
            PartyActorIds = ["actor.eco", "actor.delta"],
            Enemies = [new EnemyDef { Id = "enemy.ogro", Name = "Ogro", Stats = new StatBlock { Hp = 15, Attack = 25, Defense = 20, Speed = 5 }, Exp = 9, Money = 7 }],
            Skills = [new SkillDef { Id = "skill.levantar", Name = "Levantar", MpCost = 3, Power = 12, Kind = "revive" }],
            Items = [new ItemDef { Id = "item.pluma", Name = "Pluma", Effect = "revive:5" }],
            Battles = [new BattleDef { Id = "battle.revival", EnemyIds = ["enemy.ogro"], DamageFormula = "max(1, attack - defense)" }],
        };
        var pluma = new List<string> { "item.pluma" };
        var e5 = new BattleEngine(revival.Battles[0], revival, PartyState.Create(revival), pluma);
        var eco = e5.Party[0];

        // Ronda 1: Delta y Eco defienden; el ogro pega 25 (12 defendido) y Eco (10 HP) cae.
        e5.SelectedCommand = 3; e5.ConfirmCommand(); // Delta
        e5.SelectedCommand = 3; e5.ConfirmCommand(); // Eco
        if (eco.Alive || e5.FallenPartyIndexes.Count != 1) return sb.AppendLine($"FALLO revive: Eco no cayo. log={e5.Log}").ToString();

        // Ronda 2: la skill revive pide blanco caido y levanta a Eco con min(12, max 10) = 10 HP.
        e5.SelectedCommand = 1; e5.ConfirmCommand();
        e5.SelectedSkill = 0; e5.ConfirmSkill();
        if (e5.Current != BattleEngine.Phase.TargetSelect || !e5.TargetingFallen) return sb.AppendLine("FALLO revive: la skill no pide blanco caido.").ToString();
        e5.SelectedTarget = 0; e5.ConfirmTarget();
        if (!e5.Log.Contains(UiStrings.UsesSkillRevive("a", "b", eco.Name, 10).Split(". ")[^1]) || e5.Party[1].Mp != 7)
            return sb.AppendLine($"FALLO revive skill: mp={e5.Party[1].Mp} log={e5.Log}").ToString();

        // El ogro lo tumba de nuevo; la pluma lo levanta con 5 HP (prioridad del comando Item).
        if (eco.Alive) return sb.AppendLine("FALLO revive: el ogro no volvio a tumbar a Eco.").ToString();
        e5.SelectedCommand = 2; e5.ConfirmCommand();
        if (pluma.Contains("item.pluma") || !e5.Log.Contains(UiStrings.UsesItemRevive("a", "b", eco.Name, 5).Split(". ")[^1]))
            return sb.AppendLine($"FALLO pluma: inventario={string.Join(",", pluma)} log={e5.Log}").ToString();

        // Resto: Delta pega 10 por turno (ogro 15 HP) hasta ganar.
        e5.ResolveImmediatelyForSmoke();
        if (!e5.Victory) return sb.AppendLine($"FALLO: el combate de revivir no termino en victoria. log={e5.Log}").ToString();
        sb.AppendLine($"battle.revival: victory=True reviveSkill=OK (blanco caido, clamp a maxHp) pluma=OK log={e5.Log}");

        return sb.ToString();
    }
}
