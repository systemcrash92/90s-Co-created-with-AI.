using System.Text;

namespace Seto90;

public sealed record BalanceAuditFinding(
    string Severity,
    string Dimension,
    string Code,
    string Location,
    string Evidence,
    string Message,
    string Suggestion);

public sealed record BalancePartySnapshot(
    string ActorId,
    string Name,
    int Level,
    int MaxHp,
    int MaxMp,
    int Attack,
    int Defense,
    int Speed);

public sealed record BalanceBattleSimulation(
    string Strategy,
    bool Victory,
    bool Defeat,
    bool Stalled,
    int PlayerActions,
    int TotalHpStart,
    int TotalHpEnd,
    int EnemyHpEnd,
    int HpRemainingPercent,
    int LowestMemberPercent,
    int MpSpent,
    int ItemsUsed,
    string LastLog);

public sealed record BalanceBattleCheckpoint(
    int Order,
    string BattleId,
    string MapId,
    string EventId,
    bool Boss,
    List<BalancePartySnapshot> Party,
    int MoneyBefore,
    int MoneyAfterPreparation,
    List<string> Purchases,
    List<string> AvailableItemIds,
    int EnemyCount,
    int TotalEnemyHp,
    int RewardExp,
    int RewardMoney,
    int MaxIncomingHit,
    int LeaderIncomingHit,
    BalanceBattleSimulation Basic,
    BalanceBattleSimulation Prepared);

public sealed record BalanceShopCheckpoint(
    string ShopId,
    string MapId,
    string EventId,
    int MoneyAtAccess,
    int ItemCount,
    int AffordableItems,
    int UpgradeItems,
    int ConsumableItems,
    int? CheapestPrice,
    int? MostExpensivePrice);

public sealed record BalanceInnCheckpoint(
    string MapId,
    string EventId,
    int Price,
    int MoneyAtAccess,
    bool Affordable);

public sealed record BalanceEconomyProjection(
    int StartMoney,
    int ScriptedIncome,
    int ScriptedCosts,
    int DistinctBattleIncome,
    int ProjectedFinalMoneyNoPurchases,
    int ReachableShopCatalogCost);

public sealed record BalanceAuditSummary(
    int BattleCheckpoints,
    int ShopCheckpoints,
    int Warnings,
    int ReviewNotes,
    string Verdict);

public sealed class BalanceAuditReport
{
    public BalanceAuditSummary Summary { get; init; } = new(0, 0, 0, 0, "needs_fix");
    public BalanceEconomyProjection Economy { get; init; } = new(0, 0, 0, 0, 0, 0);
    public List<BalanceBattleCheckpoint> Battles { get; init; } = [];
    public List<BalanceShopCheckpoint> Shops { get; init; } = [];
    public List<BalanceInnCheckpoint> Inns { get; init; } = [];
    public List<BalanceAuditFinding> Findings { get; init; } = [];
    public List<string> Assumptions { get; init; } = [];
    public List<string> AiReviewQuestions { get; init; } = [];
    public List<string> SuggestedChecks { get; init; } = [];

    public string ToHumanText()
    {
        var b = new StringBuilder();
        b.AppendLine("90s Engine - auditor global de balance");
        b.AppendLine($"Veredicto: {Summary.Verdict}. {Summary.BattleCheckpoints} combate(s), {Summary.ShopCheckpoints} tienda(s), {Summary.Warnings} warning(s), {Summary.ReviewNotes} nota(s).");
        b.AppendLine($"Economia proyectada sin compras: inicio ${Economy.StartMoney} + guion ${Economy.ScriptedIncome} - costos ${Economy.ScriptedCosts} + batallas ${Economy.DistinctBattleIncome} = ${Economy.ProjectedFinalMoneyNoPurchases}; catalogo alcanzable ${Economy.ReachableShopCatalogCost}.");
        foreach (var shop in Shops)
            b.AppendLine($"- Tienda {shop.ShopId} @ {shop.MapId}: ${shop.MoneyAtAccess} al llegar; {shop.AffordableItems}/{shop.ItemCount} items comprables; precios {MoneyRange(shop.CheapestPrice, shop.MostExpensivePrice)}.");
        foreach (var battle in Battles)
        {
            var party = string.Join(", ", battle.Party.Select(x => $"{x.Name} Nv{x.Level}"));
            b.AppendLine($"- #{battle.Order} {battle.BattleId}{(battle.Boss ? " [JEFE]" : "")} @ {battle.MapId}: {party}; enemigos HP {battle.TotalEnemyHp}; premio {battle.RewardExp} EXP/${battle.RewardMoney}.");
            b.AppendLine($"  Basico: {Outcome(battle.Basic)}, {battle.Basic.PlayerActions} accion(es), HP {battle.Basic.HpRemainingPercent}%. Preparado: {Outcome(battle.Prepared)}, {battle.Prepared.PlayerActions} accion(es), HP {battle.Prepared.HpRemainingPercent}% (piso {battle.Prepared.LowestMemberPercent}%), MP {battle.Prepared.MpSpent}, items {battle.Prepared.ItemsUsed}, golpe entrante max {battle.MaxIncomingHit}, presupuesto ${battle.MoneyBefore}->{battle.MoneyAfterPreparation}.");
            if (battle.Purchases.Count > 0) b.AppendLine($"  Preparacion: {string.Join("; ", battle.Purchases)}.");
        }
        foreach (var finding in Findings)
        {
            b.AppendLine($"[{finding.Severity.ToUpperInvariant()}][{finding.Dimension}] {finding.Code} @ {finding.Location}: {finding.Message}");
            b.AppendLine($"  Evidencia: {finding.Evidence}");
            b.AppendLine($"  Sugerencia: {finding.Suggestion}");
        }
        b.AppendLine("Supuestos de la proyeccion:");
        foreach (var assumption in Assumptions) b.AppendLine($"- {assumption}");
        b.AppendLine("Preguntas para la IA/autoria:");
        foreach (var question in AiReviewQuestions) b.AppendLine($"- {question}");
        b.AppendLine("Comprobaciones sugeridas:");
        foreach (var check in SuggestedChecks) b.AppendLine($"- {check}");
        return b.ToString().TrimEnd();
    }

    static string Outcome(BalanceBattleSimulation x) => x.Victory ? "victoria" : x.Defeat ? "derrota" : "sin resolver";
    static string MoneyRange(int? min, int? max) => min is null ? "n/d" : $"${min}..${max}";
}

/// <summary>
/// Auditor determinista de progresion, combate y economia. Proyecta una ruta alcanzable por
/// mapas/eventos, concede una vez cada recompensa de combate y compara ataque basico contra
/// varias preparaciones asequibles (equipo primero, suministros primero o sin compra,
/// siempre con uso tactico de skills) y conserva la que mejor rinde. No intenta decidir
/// si algo es divertido: expone numeros, supuestos y preguntas para el juicio de la IA/autoria.
/// </summary>
public static class BalanceAudit
{
    sealed record TraceCommand(EventCommand Command, string MapId, string EventId, string Source);
    sealed record Preparation(PartyState Party, List<string> Inventory, int MoneyLeft, List<string> Purchases);
    sealed record GearChoice(int Value, List<(int MemberIndex, ItemDef Item)> Items);

    public static BalanceAuditReport Analyze(GameProject p, bool includeInfo = true, string routeId = "")
    {
        var findings = new List<BalanceAuditFinding>();
        QualityRouteDef? route = null;
        if (!string.IsNullOrWhiteSpace(routeId))
            route = p.QualityPlan.Routes.FirstOrDefault(x => x.Id == routeId)
                ?? throw new KeyNotFoundException($"No existe la ruta de calidad '{routeId}'.");

        var trace = BuildTrace(p, route);
        var roster = PartyState.Create(p);
        var money = Math.Max(0, p.StartMoney);
        var scriptedIncome = 0;
        var scriptedCosts = 0;
        var battleIncome = 0;
        var availableShopIds = new HashSet<string>(StringComparer.Ordinal);
        var availableItemIds = new HashSet<string>(StringComparer.Ordinal);
        var giftedItems = new List<string>();
        var battles = new List<BalanceBattleCheckpoint>();
        var shops = new List<BalanceShopCheckpoint>();
        var inns = new List<BalanceInnCheckpoint>();
        var seenBattles = new HashSet<string>(StringComparer.Ordinal);
        var seenShops = new HashSet<string>(StringComparer.Ordinal);

        foreach (var beat in trace)
        {
            var command = beat.Command;
            switch (command.Kind)
            {
                case CommandKind.AddPartyMember:
                    roster.AddMember(p, command.TargetId);
                    break;
                case CommandKind.RemovePartyMember:
                    roster.RemoveMember(command.TargetId);
                    break;
                case CommandKind.GiveItem:
                case CommandKind.ShowItemGet:
                    AddCopies(giftedItems, command.TargetId, PositiveAmount(command.Value, 1));
                    break;
                case CommandKind.GiveMoney:
                    {
                        var amount = PositiveAmount(command.Value, 0);
                        money += amount;
                        scriptedIncome += amount;
                        break;
                    }
                case CommandKind.TakeMoney:
                    {
                        var amount = PositiveAmount(command.Value, 0);
                        if (amount > money)
                        {
                            findings.Add(new(
                                "warning", "economy", "scripted_cost_unaffordable", beat.Source,
                                $"El guion intenta cobrar ${amount} con ${money} proyectados; TakeMoney corta la cola del evento cuando no alcanza.",
                                "La ruta proyectada puede detenerse antes de entregar su recompensa o abrir su siguiente beat.",
                                "Adelantar ingreso, bajar el costo o autorar una rama explicita de falta de dinero y confirmar la ruta con playtest.run."));
                            break;
                        }
                        money -= amount;
                        scriptedCosts += amount;
                        break;
                    }
                case CommandKind.OpenShop:
                    {
                        var shop = p.Shops.FirstOrDefault(x => x.Id == command.TargetId);
                        if (shop == null) break;
                        availableShopIds.Add(shop.Id);
                        foreach (var id in shop.ItemIds) availableItemIds.Add(id);
                        if (!seenShops.Add(shop.Id)) break;
                        var items = shop.ItemIds.Select(id => p.Items.FirstOrDefault(x => x.Id == id)).Where(x => x != null).Cast<ItemDef>().ToList();
                        var positive = items.Where(x => x.Price > 0).Select(x => x.Price).ToList();
                        shops.Add(new(
                            shop.Id, beat.MapId, beat.EventId, money, items.Count,
                            items.Count(x => x.Price <= money),
                            items.Count(x => !string.IsNullOrWhiteSpace(x.Slot) && GearScore(x) > 0),
                            items.Count(x => !string.IsNullOrWhiteSpace(x.Effect)),
                            positive.Count == 0 ? null : positive.Min(),
                            positive.Count == 0 ? null : positive.Max()));
                        break;
                    }
                case CommandKind.OpenInn:
                    {
                        var price = int.TryParse(command.Value, out var parsed) ? Math.Max(0, parsed) : 0;
                        inns.Add(new(beat.MapId, beat.EventId, price, money, money >= price));
                        break;
                    }
                case CommandKind.Battle:
                    {
                        var battle = p.Battles.FirstOrDefault(x => x.Id == command.TargetId);
                        if (battle == null || !seenBattles.Add(battle.Id)) break;
                        var checkpoint = BuildCheckpoint(
                            p, battle, battles.Count + 1, beat, roster, money,
                            availableShopIds, availableItemIds, giftedItems);
                        battles.Add(checkpoint);
                        var exp = battle.EnemyIds.Select(id => p.Enemies.FirstOrDefault(x => x.Id == id)?.Exp ?? 0).Sum();
                        var reward = battle.EnemyIds.Select(id => p.Enemies.FirstOrDefault(x => x.Id == id)?.Money ?? 0).Sum();
                        roster.GrantExp(exp);
                        money += reward;
                        battleIncome += reward;
                        break;
                    }
            }
        }

        AuditBattles(p, battles, findings);
        AuditEconomy(p, shops, inns, money, availableItemIds, findings);
        AuditItems(p, availableItemIds, findings);
        AuditSkills(p, battles, findings);

        if (!includeInfo) findings.RemoveAll(x => x.Severity == "info");
        findings = findings
            .OrderBy(x => x.Severity == "warning" ? 0 : 1)
            .ThenBy(x => x.Dimension, StringComparer.Ordinal)
            .ThenBy(x => x.Code, StringComparer.Ordinal)
            .ThenBy(x => x.Location, StringComparer.Ordinal)
            .ToList();

        var warnings = findings.Count(x => x.Severity == "warning");
        var notes = findings.Count(x => x.Severity == "info");
        var verdict = warnings > 0 ? "needs_fix" : notes > 0 ? "needs_tuning_review" : "balanced_for_playtest";
        var catalogCost = availableItemIds.Select(id => p.Items.FirstOrDefault(x => x.Id == id))
            .Where(x => x != null && x.Price > 0).Cast<ItemDef>().DistinctBy(x => x.Id).Sum(x => x.Price);

        return new BalanceAuditReport
        {
            Summary = new(battles.Count, shops.Count, warnings, notes, verdict),
            Economy = new(p.StartMoney, scriptedIncome, scriptedCosts, battleIncome, money, catalogCost),
            Battles = battles,
            Shops = shops,
            Inns = inns,
            Findings = findings,
            Assumptions = BuildAssumptions(route),
            AiReviewQuestions =
            [
                "¿Que combates son obligatorios, opcionales o repetibles, y coincide esa intencion con la ruta que el informe tuvo que fusionar?",
                "¿La diferencia entre Basico y Preparado recompensa aprender/equiparse, o convierte una compra concreta en una llave invisible?",
                "¿La duracion, margen de HP y riesgo de one-shot expresan el tono del encuentro (tutorial, desgaste, jefe) sin volverse tedio?",
                "¿La EXP produce niveles en los beats narrativos deseados y evita que un miembro que se suma tarde quede inutil?",
                "¿El dinero permite decisiones reales entre equipo, cura y posada, o existe una opcion dominante/inalcanzable?",
                "¿Las skills justifican su MP frente al ataque basico y cada estado peligroso tiene una respuesta que el jugador pudo conocer y conseguir?"
            ],
            SuggestedChecks =
            [
                "Ejecutar battle-smoke sobre el proyecto y comparar sus derrotas con los perfiles Basico/Preparado.",
                "Usar playtest.run por la ruta canonica con asserts de dinero, nivel, items y flags antes de cada jefe.",
                "Capturar tienda, estado/equipo y el combate mas estrecho; comprobar que la informacion para prepararse sea visible al jugador.",
                "Repetir balance.audit despues de tocar stats, growth, precios, rewards, party, skills o el orden de encuentros."
            ]
        };
    }

    static BalanceBattleCheckpoint BuildCheckpoint(
        GameProject p,
        BattleDef battle,
        int order,
        TraceCommand beat,
        PartyState progression,
        int money,
        HashSet<string> availableShopIds,
        HashSet<string> availableItemIds,
        List<string> giftedItems)
    {
        var baselineParty = CloneParty(progression, keepEquipment: false);
        var baselineInventory = giftedItems.Where(id => p.Items.FirstOrDefault(x => x.Id == id) is { Effect.Length: > 0 }).ToList();
        var basic = Simulate(p, battle, baselineParty, baselineInventory, tactical: false);
        var emptyShops = new HashSet<string>(StringComparer.Ordinal);
        var preparedOptions = new[]
        {
            Prepare(p, battle, progression, money, availableShopIds, giftedItems, suppliesFirst: false),
            Prepare(p, battle, progression, money, availableShopIds, giftedItems, suppliesFirst: true),
            Prepare(p, battle, progression, money, emptyShops, giftedItems, suppliesFirst: false)
        }.Select(preparation => (Preparation: preparation, Simulation:
            Simulate(p, battle, preparation.Party, preparation.Inventory, tactical: true))).ToList();
        var selected = preparedOptions
            .OrderByDescending(x => x.Simulation.Victory)
            .ThenByDescending(x => x.Simulation.HpRemainingPercent)
            .ThenBy(x => x.Simulation.EnemyHpEnd)
            .ThenByDescending(x => x.Simulation.LowestMemberPercent)
            .ThenBy(x => x.Simulation.PlayerActions)
            .ThenByDescending(x => x.Preparation.MoneyLeft)
            .First();
        var preparation = selected.Preparation;
        var prepared = selected.Simulation;
        var foes = battle.EnemyIds.Select(id => p.Enemies.FirstOrDefault(x => x.Id == id)).Where(x => x != null).Cast<EnemyDef>().ToList();
        var snapshots = progression.Members.Select(Snapshot).ToList();
        var preparedMembers = preparation.Party.Members;
        var hits = foes.SelectMany(enemy => preparedMembers.Select(member =>
            SafeDamage(battle.DamageFormula, enemy.Stats, member.Stats, 1))).ToList();
        var leaderHits = preparedMembers.Count == 0 ? [] : foes.Select(enemy =>
            SafeDamage(battle.DamageFormula, enemy.Stats, preparedMembers[0].Stats, 1)).ToList();
        return new(
            order, battle.Id, beat.MapId, beat.EventId, battle.Boss, snapshots,
            money, preparation.MoneyLeft, preparation.Purchases,
            availableItemIds.Concat(giftedItems).Distinct(StringComparer.Ordinal).OrderBy(x => x, StringComparer.Ordinal).ToList(),
            foes.Count, foes.Sum(x => Math.Max(0, x.Stats.Hp)), foes.Sum(x => Math.Max(0, x.Exp)), foes.Sum(x => Math.Max(0, x.Money)),
            hits.Count == 0 ? 0 : hits.Max(), leaderHits.Count == 0 ? 0 : leaderHits.Max(),
            basic, prepared);
    }

    static Preparation Prepare(
        GameProject p,
        BattleDef battle,
        PartyState progression,
        int money,
        HashSet<string> shopIds,
        List<string> gifts,
        bool suppliesFirst)
    {
        var party = CloneParty(progression, keepEquipment: false);
        var purchases = new List<string>();
        var inventory = gifts.Where(id => p.Items.FirstOrDefault(x => x.Id == id) is { Effect.Length: > 0 }).ToList();
        var giftEquipment = gifts.Select(id => p.Items.FirstOrDefault(x => x.Id == id))
            .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Slot)).Cast<ItemDef>().ToList();
        foreach (var item in giftEquipment)
        {
            var member = party.Members.FirstOrDefault(x =>
                item.Slot.Equals("weapon", StringComparison.OrdinalIgnoreCase) ? x.Weapon == null : x.Armor == null);
            if (member == null) continue;
            member.Equip(item);
            purchases.Add($"equipa regalo {item.Name} en {member.Def.Name}");
        }

        var shopItems = shopIds.Select(id => p.Shops.FirstOrDefault(x => x.Id == id))
            .Where(x => x != null).Cast<ShopDef>().SelectMany(x => x.ItemIds)
            .Distinct(StringComparer.Ordinal).Select(id => p.Items.FirstOrDefault(x => x.Id == id))
            .Where(x => x != null).Cast<ItemDef>().ToList();
        var moneyLeft = money;
        void BuySupplies()
        {
            var statuses = battle.EnemyIds.Select(id => p.Enemies.FirstOrDefault(x => x.Id == id)?.Inflicts ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            foreach (var status in statuses)
                BuyOne(shopItems.Where(x => Cures(x, status)).OrderBy(x => x.Price), $"respuesta a {status}", ref moneyLeft, inventory, purchases);
            if (party.Members.Count > 1)
                BuyOne(shopItems.Where(x => x.Effect.StartsWith("revive:", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x.Price), "reserva de revive", ref moneyLeft, inventory, purchases);
            var heal = shopItems.Where(x => HealAmount(x) > 0 && x.Price > 0)
                .OrderByDescending(x => (double)HealAmount(x) / x.Price).ThenBy(x => x.Price).FirstOrDefault();
            if (heal != null)
                for (var i = 0; i < Math.Max(1, party.Members.Count); i++)
                    if (!BuyOne([heal], "cura de reserva", ref moneyLeft, inventory, purchases)) break;
        }
        if (suppliesFirst) BuySupplies();

        var groups = new List<(int MemberIndex, string Slot)>();
        for (var i = 0; i < party.Members.Count; i++)
        {
            if (party.Members[i].Weapon == null) groups.Add((i, "weapon"));
            if (party.Members[i].Armor == null) groups.Add((i, "armor"));
        }
        var states = new Dictionary<int, GearChoice> { [0] = new(0, []) };
        foreach (var group in groups)
        {
            var next = new Dictionary<int, GearChoice>();
            foreach (var (spent, plan) in states)
            {
                KeepBest(next, spent, plan);
                foreach (var item in shopItems.Where(x =>
                             x.Price > 0 && x.Price + spent <= moneyLeft &&
                             x.Slot.Equals(group.Slot, StringComparison.OrdinalIgnoreCase) && GearScore(x) > 0))
                {
                    var choice = new GearChoice(plan.Value + GearScore(item), [.. plan.Items, (group.MemberIndex, item)]);
                    KeepBest(next, spent + item.Price, choice);
                }
            }
            states = next;
        }
        var best = states.OrderByDescending(x => x.Value.Value).ThenBy(x => x.Key).First();
        moneyLeft -= best.Key;
        foreach (var (memberIndex, item) in best.Value.Items)
        {
            party.Members[memberIndex].Equip(item);
            purchases.Add($"compra/equipa {item.Name} en {party.Members[memberIndex].Def.Name} (${item.Price})");
        }

        if (!suppliesFirst) BuySupplies();

        return new(party, inventory, moneyLeft, purchases);
    }

    static void KeepBest(Dictionary<int, GearChoice> states, int spent, GearChoice candidate)
    {
        if (!states.TryGetValue(spent, out var current) || candidate.Value > current.Value)
            states[spent] = candidate;
    }

    static bool BuyOne(
        IEnumerable<ItemDef> candidates,
        string purpose,
        ref int money,
        List<string> inventory,
        List<string> purchases)
    {
        var budget = money;
        var item = candidates.FirstOrDefault(x => x.Price > 0 && x.Price <= budget);
        if (item == null) return false;
        money -= item.Price;
        inventory.Add(item.Id);
        purchases.Add($"{item.Name} ({purpose}, ${item.Price})");
        return true;
    }

    static BalanceBattleSimulation Simulate(
        GameProject p,
        BattleDef battle,
        PartyState party,
        List<string> inventory,
        bool tactical)
    {
        var bag = inventory.ToList();
        var engine = new BattleEngine(battle, p, party, bag);
        var hpStart = engine.Party.Sum(x => x.Stats.Hp);
        var mpStart = engine.Party.Sum(x => x.Mp);
        var itemsStart = bag.Count;
        var actions = 0;
        var lowest = 100;
        var plannedSkill = -1;
        var guard = 0;
        UpdateLowest(engine, ref lowest);
        while (!engine.Resolved && guard++ < 2000)
        {
            switch (engine.Current)
            {
                case BattleEngine.Phase.Command:
                    actions++;
                    if (tactical)
                    {
                        plannedSkill = ChooseSkill(engine, battle);
                        if (plannedSkill >= 0)
                        {
                            engine.SelectedCommand = 1;
                            engine.ConfirmCommand();
                        }
                        else if (HasApplicableItem(engine, p, bag))
                        {
                            engine.SelectedCommand = 2;
                            engine.ConfirmCommand();
                        }
                        else
                        {
                            engine.SelectedCommand = 0;
                            engine.ConfirmCommand();
                        }
                    }
                    else
                    {
                        engine.SelectedCommand = 0;
                        engine.ConfirmCommand();
                    }
                    break;
                case BattleEngine.Phase.SkillSelect:
                    if (plannedSkill < 0) { engine.Cancel(); break; }
                    engine.SelectedSkill = plannedSkill;
                    engine.ConfirmSkill();
                    if (engine.Current == BattleEngine.Phase.SkillSelect)
                    {
                        engine.Cancel();
                        plannedSkill = -1;
                    }
                    break;
                case BattleEngine.Phase.TargetSelect:
                    engine.SelectedTarget = TargetPosition(engine);
                    engine.ConfirmTarget();
                    plannedSkill = -1;
                    break;
                default:
                    guard = 2000;
                    break;
            }
            UpdateLowest(engine, ref lowest);
        }
        var hpEnd = engine.Party.Sum(x => Math.Max(0, x.Hp));
        var enemyHpEnd = engine.Enemies.Sum(x => Math.Max(0, x.Hp));
        return new(
            tactical ? "prepared_tactical" : "progression_basic",
            engine.Victory, engine.Defeat, !engine.Resolved,
            actions, hpStart, hpEnd, enemyHpEnd,
            hpStart <= 0 ? 0 : hpEnd * 100 / hpStart,
            lowest,
            Math.Max(0, mpStart - engine.Party.Sum(x => Math.Max(0, x.Mp))),
            Math.Max(0, itemsStart - bag.Count),
            Clip(engine.Log, 220));
    }

    static int ChooseSkill(BattleEngine engine, BattleDef battle)
    {
        var actor = engine.Acting;
        if (actor == null) return -1;
        var skills = engine.ActingSkills;
        var revive = skills.Select((skill, index) => (skill, index))
            .Where(x => x.skill.Kind.Equals("revive", StringComparison.OrdinalIgnoreCase) &&
                        actor.Mp >= x.skill.MpCost && engine.FallenPartyIndexes.Count > 0)
            .OrderByDescending(x => x.skill.Power).FirstOrDefault();
        if (revive.skill != null) return revive.index;

        var needsHeal = engine.Party.Any(x => x.Alive &&
            (x.SleepTurns > 0 || x.Hp * 100 / Math.Max(1, x.Stats.Hp) < 50));
        if (needsHeal)
        {
            var heal = skills.Select((skill, index) => (skill, index))
                .Where(x => x.skill.Kind.Equals("heal", StringComparison.OrdinalIgnoreCase) && actor.Mp >= x.skill.MpCost)
                .OrderByDescending(x => x.skill.Power).FirstOrDefault();
            if (heal.skill != null) return heal.index;
        }

        var target = engine.Enemies.FirstOrDefault(x => x.Alive);
        if (target == null) return -1;
        var basic = SafeDamage(battle.DamageFormula, actor.Stats, target.Stats, actor.Level);
        var damage = skills.Select((skill, index) => (skill, index,
                damage: Math.Max(1, skill.Power + actor.Stats.Attack / 2 - target.Stats.Defense)))
            .Where(x => x.skill.Kind.Equals("damage", StringComparison.OrdinalIgnoreCase) && actor.Mp >= x.skill.MpCost)
            .Where(x => x.damage > basic ||
                        (!string.IsNullOrWhiteSpace(x.skill.Status) && !HasStatus(target, x.skill.Status)))
            .OrderByDescending(x => x.damage + (!string.IsNullOrWhiteSpace(x.skill.Status) ? 4 : 0))
            .ThenBy(x => x.skill.MpCost).FirstOrDefault();
        return damage.skill == null ? -1 : damage.index;
    }

    static bool HasApplicableItem(BattleEngine engine, GameProject p, List<string> inventory)
    {
        foreach (var id in inventory.Distinct(StringComparer.Ordinal))
        {
            var item = p.Items.FirstOrDefault(x => x.Id == id);
            if (item == null) continue;
            if (item.Effect.StartsWith("revive:", StringComparison.OrdinalIgnoreCase) && engine.FallenPartyIndexes.Count > 0) return true;
            if (item.Effect.StartsWith("heal:", StringComparison.OrdinalIgnoreCase) &&
                engine.Party.Any(x => x.Alive && x.Hp * 100 / Math.Max(1, x.Stats.Hp) < 40)) return true;
            if (item.Effect.StartsWith("cure:", StringComparison.OrdinalIgnoreCase))
            {
                var what = item.Effect.Split(':', 2)[1];
                if (engine.Party.Any(x => x.Alive &&
                    ((what.Equals("poison", StringComparison.OrdinalIgnoreCase) || what.Equals("all", StringComparison.OrdinalIgnoreCase)) && x.Poisoned ||
                     (what.Equals("sleep", StringComparison.OrdinalIgnoreCase) || what.Equals("all", StringComparison.OrdinalIgnoreCase)) && x.SleepTurns > 0)))
                    return true;
            }
        }
        return false;
    }

    static int TargetPosition(BattleEngine engine)
    {
        if (engine.TargetingFallen) return 0;
        if (engine.TargetingAllies)
        {
            var indexes = engine.AlivePartyIndexes;
            var sleeping = indexes.FindIndex(i => engine.Party[i].SleepTurns > 0);
            if (sleeping >= 0) return sleeping;
            return indexes.Select((index, position) => (position, ratio: (double)engine.Party[index].Hp / Math.Max(1, engine.Party[index].Stats.Hp)))
                .OrderBy(x => x.ratio).FirstOrDefault().position;
        }
        var enemies = engine.AliveEnemyIndexes;
        return enemies.Select((index, position) => (position, hp: engine.Enemies[index].Hp))
            .OrderBy(x => x.hp).FirstOrDefault().position;
    }

    static void UpdateLowest(BattleEngine engine, ref int lowest)
    {
        foreach (var member in engine.Party)
            lowest = Math.Min(lowest, Math.Max(0, member.Hp) * 100 / Math.Max(1, member.Stats.Hp));
    }

    static void AuditBattles(GameProject p, List<BalanceBattleCheckpoint> battles, List<BalanceAuditFinding> findings)
    {
        void Add(string severity, string dimension, string code, string location, string evidence, string message, string suggestion) =>
            findings.Add(new(severity, dimension, code, location, evidence, message, suggestion));
        BalanceBattleCheckpoint? previous = null;
        var previousAverageLevel = 0;
        foreach (var battle in battles)
        {
            var location = $"battle:{battle.BattleId}";
            if (battle.Prepared.Defeat || battle.Prepared.Stalled)
                Add("warning", "difficulty", "battle_unwinnable_prepared", location,
                    $"Preparado: victory={battle.Prepared.Victory}, defeat={battle.Prepared.Defeat}, actions={battle.Prepared.PlayerActions}, HP={battle.Prepared.HpRemainingPercent}%, compras=${battle.MoneyBefore - battle.MoneyAfterPreparation}.",
                    "La simulacion tactica pierde aun usando progresion y preparacion asequible.",
                    "Revisar HP/ataque/defensa/velocidad enemigos, orden del encuentro, EXP previa o acceso real a equipo/curas; no bajar numeros sin probar la ruta.");
            else if (battle.Basic.Defeat && battle.Prepared.Victory)
                Add("info", "difficulty", "battle_requires_preparation", location,
                    $"Basico pierde; preparado vence con {battle.Prepared.HpRemainingPercent}% HP y {battle.Prepared.PlayerActions} acciones.",
                    "El encuentro exige usar equipo, suministros o skills.",
                    "Es correcto para un jefe si el juego ensena y ofrece esa preparacion; agregar una pista o bajar la exigencia si es un encuentro comun.");

            if (battle.Prepared.Victory && battle.Prepared.HpRemainingPercent <= 20)
                Add("info", "difficulty", "battle_narrow_margin", location,
                    $"La party termina con {battle.Prepared.HpRemainingPercent}% del HP total y el miembro mas comprometido llego a {battle.Prepared.LowestMemberPercent}%.",
                    "El margen preparado es muy estrecho.",
                    "Confirmar con playtest que la tension sea intencional y no dependa de una decision invisible.");
            if (battle.Prepared.Victory && battle.Prepared.PlayerActions > Math.Max(12, battle.EnemyCount * 8))
                Add("info", "pacing", "battle_long_prepared", location,
                    $"{battle.Prepared.PlayerActions} acciones del jugador aun preparado contra {battle.EnemyCount} enemigo(s).",
                    "El encuentro puede sentirse esponjoso aunque sea ganable.",
                    "Mirar el ritmo real; reducir HP/defensa o aumentar opciones ofensivas si los turnos no agregan decisiones.");
            if (battle.Order > 1 && battle.Prepared.Victory && battle.Prepared.PlayerActions <= Math.Max(1, battle.EnemyCount) && battle.Prepared.HpRemainingPercent >= 95)
                Add("info", "difficulty", "battle_trivial_after_progression", location,
                    $"{battle.Prepared.PlayerActions} accion(es), HP final {battle.Prepared.HpRemainingPercent}%.",
                    "La progresion/preparacion borra casi toda respuesta enemiga.",
                    "Mantenerlo si comunica poder ganado; si debe tensar, reforzar el patron enemigo antes que inflar HP.");

            var leader = battle.Party.FirstOrDefault();
            if (leader != null && battle.LeaderIncomingHit >= leader.MaxHp)
                Add("warning", "stats", "leader_one_shot", location,
                    $"Golpe maximo estimado {battle.LeaderIncomingHit} vs {leader.Name} {leader.MaxHp} HP.",
                    "Un ataque normal puede derribar de un golpe al primer blanco de la party.",
                    "Bajar ataque enemigo, subir defensa/HP alcanzable o telegrafiar/proveer una defensa obligatoria.");
            else if (battle.Party.Count > 0 && battle.MaxIncomingHit >= battle.Party.Min(x => x.MaxHp))
                Add("info", "stats", "fragile_member_one_shot", location,
                    $"Golpe maximo {battle.MaxIncomingHit}; miembro mas fragil {battle.Party.Min(x => x.MaxHp)} HP.",
                    "Un miembro secundario puede caer de un golpe si pasa a ser el primer blanco.",
                    "Comprobar que revive/defensa y la lectura del peligro esten disponibles.");

            var definition = p.Battles.FirstOrDefault(x => x.Id == battle.BattleId);
            var statuses = definition?.EnemyIds.Select(id => p.Enemies.FirstOrDefault(x => x.Id == id)?.Inflicts ?? "")
                .Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList() ?? [];
            foreach (var status in statuses)
                if (!HasCounter(p, battle, status))
                    Add("info", "resources", "status_without_reachable_counter", location,
                        $"Enemigos infligen {status}; items/skills disponibles antes del checkpoint no ofrecen una respuesta directa.",
                        "El estado puede convertir el encuentro en una trampa de conocimiento.",
                        $"Vender/entregar cure:{status}, cure:all o una skill de respuesta antes del combate, o ensenar que la ausencia de cura es deliberada.");

            if (battle.Boss && battle.RewardExp == 0 && battle.RewardMoney == 0)
                Add("info", "rewards", "boss_without_numeric_reward", location,
                    "El jefe no entrega EXP ni dinero.",
                    "El climax depende solo de su recompensa narrativa/estado.",
                    "Mantenerlo si la consecuencia visible basta; si no, agregar una recompensa coherente con la economia.");

            var averageLevel = battle.Party.Count == 0 ? 0 : (int)Math.Round(battle.Party.Average(x => x.Level));
            if (previousAverageLevel > 0 && averageLevel - previousAverageLevel > 2)
                Add("info", "progression", "level_spike_between_checkpoints", location,
                    $"Nivel medio {previousAverageLevel}->{averageLevel}.",
                    "La curva concede varios niveles entre encuentros autorados.",
                    "Confirmar que el salto sea un momento de poder deliberado y que las pantallas de level up no saturen el ritmo.");
            previousAverageLevel = averageLevel;

            if (previous != null)
            {
                var priorEffort = Math.Max(1, previous.Prepared.PlayerActions);
                var currentEffort = battle.Prepared.Defeat ? 999 : Math.Max(1, battle.Prepared.PlayerActions);
                if (currentEffort >= priorEffort * 2 && battle.RewardExp <= previous.RewardExp)
                    Add("info", "rewards", "reward_lags_difficulty", location,
                        $"Esfuerzo preparado {priorEffort}->{currentEffort} acciones; EXP {previous.RewardExp}->{battle.RewardExp}.",
                        "La dificultad sube con fuerza pero la EXP no acompana.",
                        "Revisar si la recompensa narrativa/equipo compensa; si no, ajustar EXP sin romper la curva de niveles.");
            }
            previous = battle;
        }
    }

    static void AuditEconomy(
        GameProject p,
        List<BalanceShopCheckpoint> shops,
        List<BalanceInnCheckpoint> inns,
        int finalMoney,
        HashSet<string> reachableItems,
        List<BalanceAuditFinding> findings)
    {
        foreach (var shop in shops)
        {
            if (shop.ItemCount > 0 && shop.AffordableItems == 0)
                findings.Add(new("info", "economy", "shop_nothing_affordable_on_arrival", $"shop:{shop.ShopId}",
                    $"Dinero proyectado ${shop.MoneyAtAccess}; precio minimo {shop.CheapestPrice?.ToString() ?? "n/d"}.",
                    "La primera visita abre una tienda donde no se puede tomar ninguna decision de compra.",
                    "Mantenerlo si la tienda planta una meta visible; si no, bajar un precio o adelantar una fuente de dinero."));
            var definition = p.Shops.FirstOrDefault(x => x.Id == shop.ShopId);
            if (definition == null) continue;
            foreach (var item in definition.ItemIds.Select(id => p.Items.FirstOrDefault(x => x.Id == id)).Where(x => x != null).Cast<ItemDef>())
                if (item.Price <= 0)
                    findings.Add(new("warning", "economy", "free_shop_item", $"shop:{shop.ShopId}/item:{item.Id}",
                        $"Precio {item.Price}; la tienda tiene stock ilimitado.",
                        "El jugador puede comprar copias infinitas sin costo.",
                        "Usar precio > 0 o entregar el item clave mediante una escena de una sola vez."));
        }
        foreach (var inn in inns.Where(x => !x.Affordable))
            findings.Add(new("info", "economy", "inn_unaffordable_on_arrival", $"event:{inn.EventId}",
                $"Posada ${inn.Price}; dinero proyectado ${inn.MoneyAtAccess}.",
                "La opcion de descanso aparece antes de poder pagarla.",
                "Es valido como meta; si debe ser una red de seguridad, adelantar ingreso o bajar el precio."));
        if (shops.Count > 0 && finalMoney <= 0 && reachableItems.Any())
            findings.Add(new("info", "economy", "campaign_no_discretionary_money", "economy",
                $"Dinero final proyectado sin compras: ${finalMoney}.",
                "La ruta no deja margen monetario para experimentar con la tienda.",
                "Confirmar que vender/grindear sea intencional; si no, agregar una recompensa o reducir costos obligatorios."));
    }

    static void AuditItems(GameProject p, HashSet<string> reachableItems, List<BalanceAuditFinding> findings)
    {
        var items = reachableItems.Select(id => p.Items.FirstOrDefault(x => x.Id == id))
            .Where(x => x != null).Cast<ItemDef>().ToList();
        foreach (var item in items.Where(x => !string.IsNullOrWhiteSpace(x.Slot)))
        {
            var better = items.FirstOrDefault(other =>
                other.Id != item.Id &&
                other.Slot.Equals(item.Slot, StringComparison.OrdinalIgnoreCase) &&
                other.Price <= item.Price &&
                BonusDominates(other.Bonus, item.Bonus));
            if (better != null)
                findings.Add(new("warning", "economy", "dominated_equipment", $"item:{item.Id}",
                    $"{item.Name} ${item.Price} bonus {BonusText(item.Bonus)}; {better.Name} ${better.Price} bonus {BonusText(better.Bonus)}.",
                    "Existe otro equipo alcanzable igual o mejor en todos los stats y no mas caro.",
                    "Diferenciar su bonus/precio, su disponibilidad narrativa o quitar la opcion falsa."));
        }
        foreach (var item in items.Where(x => HealAmount(x) > 0))
        {
            var heal = HealAmount(item);
            var better = items.FirstOrDefault(other =>
                other.Id != item.Id && HealAmount(other) >= heal && other.Price <= item.Price &&
                (HealAmount(other) > heal || other.Price < item.Price));
            if (better != null)
                findings.Add(new("warning", "economy", "dominated_healing_item", $"item:{item.Id}",
                    $"{item.Name}: heal {heal} por ${item.Price}; {better.Name}: heal {HealAmount(better)} por ${better.Price}.",
                    "El consumible es estrictamente peor por potencia/precio.",
                    "Darle otra funcion, ajustar cura/precio o retirarlo del mismo catalogo alcanzable."));
        }
    }

    static void AuditSkills(GameProject p, List<BalanceBattleCheckpoint> battles, List<BalanceAuditFinding> findings)
    {
        foreach (var actor in p.Actors)
        {
            var snapshots = battles.SelectMany(x => x.Party).Where(x => x.ActorId == actor.Id).ToList();
            var maxMp = snapshots.Count == 0 ? actor.Stats.Mp : snapshots.Max(x => x.MaxMp);
            var reachableGear = battles.Where(x => x.Party.Any(member => member.ActorId == actor.Id))
                .SelectMany(x => x.AvailableItemIds).Distinct(StringComparer.Ordinal)
                .Select(id => p.Items.FirstOrDefault(x => x.Id == id))
                .Where(x => x != null && !string.IsNullOrWhiteSpace(x.Slot)).Cast<ItemDef>().ToList();
            maxMp += reachableGear.Where(x => x.Slot.Equals("weapon", StringComparison.OrdinalIgnoreCase))
                .Select(x => Math.Max(0, x.Bonus?.Mp ?? 0)).DefaultIfEmpty(0).Max();
            maxMp += reachableGear.Where(x => x.Slot.Equals("armor", StringComparison.OrdinalIgnoreCase))
                .Select(x => Math.Max(0, x.Bonus?.Mp ?? 0)).DefaultIfEmpty(0).Max();
            foreach (var skill in actor.SkillIds.Select(id => p.Skills.FirstOrDefault(x => x.Id == id)).Where(x => x != null).Cast<SkillDef>())
            {
                if (skill.MpCost > maxMp)
                    findings.Add(new("warning", "skills", "skill_unusable_in_projection", $"actor:{actor.Id}/skill:{skill.Id}",
                        $"Costo {skill.MpCost} MP; maximo proyectado {maxMp} MP.",
                        "El actor nunca puede pagar la habilidad en los checkpoints analizados.",
                        "Bajar mpCost, subir MP/growth o mover la skill a un momento posterior."));
                if (!skill.Kind.Equals("damage", StringComparison.OrdinalIgnoreCase) || !string.IsNullOrWhiteSpace(skill.Status)) continue;
                var comparisons = new List<(int Basic, int Skill)>();
                foreach (var checkpoint in battles.Where(x => x.Party.Any(m => m.ActorId == actor.Id)))
                {
                    var snap = checkpoint.Party.First(x => x.ActorId == actor.Id);
                    var stats = new StatBlock { Hp = snap.MaxHp, Mp = snap.MaxMp, Attack = snap.Attack, Defense = snap.Defense, Speed = snap.Speed };
                    var battle = p.Battles.First(x => x.Id == checkpoint.BattleId);
                    var foe = battle.EnemyIds.Select(id => p.Enemies.FirstOrDefault(x => x.Id == id)).FirstOrDefault(x => x != null);
                    if (foe == null) continue;
                    comparisons.Add((
                        SafeDamage(battle.DamageFormula, stats, foe.Stats, snap.Level),
                        Math.Max(1, skill.Power + stats.Attack / 2 - foe.Stats.Defense)));
                }
                if (comparisons.Count > 0 && comparisons.All(x => x.Skill <= x.Basic))
                    findings.Add(new("info", "skills", "damage_skill_never_beats_attack", $"actor:{actor.Id}/skill:{skill.Id}",
                        string.Join(", ", comparisons.Select(x => $"ataque {x.Basic} vs skill {x.Skill}")),
                        "La habilidad gasta MP sin superar el ataque basico en ningun checkpoint proyectado.",
                        "Mantenerla si su VFX/target/status oculto aporta valor; si es dano puro, subir power o bajar mpCost."));
            }
            var learned = actor.SkillIds.Select(id => p.Skills.FirstOrDefault(x => x.Id == id)).Where(x => x != null).Cast<SkillDef>().ToList();
            foreach (var skill in learned)
            {
                var better = learned.FirstOrDefault(other => other.Id != skill.Id &&
                    other.Kind.Equals(skill.Kind, StringComparison.OrdinalIgnoreCase) &&
                    other.Status.Equals(skill.Status, StringComparison.OrdinalIgnoreCase) &&
                    other.MpCost <= skill.MpCost && other.Power >= skill.Power &&
                    (other.MpCost < skill.MpCost || other.Power > skill.Power));
                if (better != null)
                    findings.Add(new("info", "skills", "dominated_skill", $"actor:{actor.Id}/skill:{skill.Id}",
                        $"{skill.Name}: power {skill.Power}/MP {skill.MpCost}; {better.Name}: power {better.Power}/MP {better.MpCost}.",
                        "Otra habilidad del mismo actor cumple la misma funcion con mejores numeros.",
                        "Diferenciar target/status/VFX/rol o ajustar costo/potencia."));
            }
        }
    }

    static bool HasCounter(GameProject p, BalanceBattleCheckpoint battle, string status)
    {
        var itemCounter = battle.AvailableItemIds.Select(id => p.Items.FirstOrDefault(x => x.Id == id))
            .Any(item => item != null && Cures(item, status));
        if (itemCounter) return true;
        if (status.Equals("sleep", StringComparison.OrdinalIgnoreCase))
            return battle.Party.SelectMany(member => p.Actors.FirstOrDefault(x => x.Id == member.ActorId)?.SkillIds ?? [])
                .Select(id => p.Skills.FirstOrDefault(x => x.Id == id))
                .Any(skill => skill?.Kind.Equals("heal", StringComparison.OrdinalIgnoreCase) == true);
        return false;
    }

    static List<string> BuildAssumptions(QualityRouteDef? route)
    {
        var first = route == null
            ? "La ruta se ordena por mapas alcanzables y por el orden autorado de eventIds/paginas; las ramas condicionadas se fusionan para no fingir que el auditor conoce la eleccion canonica."
            : $"La ruta '{route.Id}' sigue exactamente sus checkpoints y pageIndex declarados; los eventos no listados no alteran esta proyeccion.";
        var choices = route == null
            ? "Las elecciones de dialogo se fusionan cuando no existe una ruta canonica."
            : $"La ruta declara {route.CanonChoices.Count} eleccion(es) canonica(s); una bifurcacion sin declarar todavia se fusiona y quality.audit la senala.";
        return
        [
            first,
            choices,
            "Cada BattleDef distinto se vence una vez y entrega una vez su EXP/dinero; los premios repetibles se revisan aparte con scene.audit.",
            "Cada checkpoint empieza con HP/MP completos. La progresion conserva roster, nivel y EXP acumulados, pero no obliga a grindear encuentros opcionales.",
            "Basico usa solo Atacar. Preparado compara equipo primero, suministros primero y tactica sin compras; conserva la opcion que mejor rinde y usa skills/items de forma determinista explicable.",
            "El presupuesto de preparacion se calcula de forma independiente en cada checkpoint sobre el dinero proyectado sin compras previas; es un techo asequible, no una receta canonica de compras."
        ];
    }

    static List<TraceCommand> BuildTrace(GameProject p, QualityRouteDef? route)
    {
        var maps = p.Maps.ToDictionary(x => x.Id, StringComparer.Ordinal);
        var events = p.Events.ToDictionary(x => x.Id, StringComparer.Ordinal);
        if (route != null)
        {
            var canonical = route.CanonChoices.ToDictionary(
                x => $"{x.DialogueId}/{x.NodeId}", x => x.ChoiceIndex, StringComparer.Ordinal);
            var routed = new List<TraceCommand>();
            var routeDialogueEffects = new HashSet<string>(StringComparer.Ordinal);
            foreach (var checkpoint in route.Checkpoints)
            {
                if (string.IsNullOrWhiteSpace(checkpoint.EventId) ||
                    !events.TryGetValue(checkpoint.EventId, out var ev)) continue;
                var pages = checkpoint.PageIndex >= 0
                    ? ev.Pages.Select((page, index) => (page, index)).Where(x => x.index == checkpoint.PageIndex)
                    : ev.Pages.Select((page, index) => (page, index));
                foreach (var (page, pageIndex) in pages)
                    for (var commandIndex = 0; commandIndex < page.Commands.Count; commandIndex++)
                        AddTraceCommand(
                            p, page.Commands[commandIndex], ev.MapId, ev.Id,
                            $"route:{route.Id}/checkpoint:{checkpoint.Id}/page:{pageIndex}/command:{commandIndex}",
                            routed, routeDialogueEffects, [], canonical);
            }
            return routed;
        }

        var mapOrder = new List<string>();
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (maps.ContainsKey(p.StartMapId)) queue.Enqueue(p.StartMapId);
        while (queue.Count > 0)
        {
            var mapId = queue.Dequeue();
            if (!reached.Add(mapId) || !maps.TryGetValue(mapId, out var map)) continue;
            mapOrder.Add(mapId);
            foreach (var warp in map.Warps) if (maps.ContainsKey(warp.ToMapId)) queue.Enqueue(warp.ToMapId);
            foreach (var eventId in map.EventIds)
            {
                if (!events.TryGetValue(eventId, out var ev) || ev.MapId != map.Id) continue;
                foreach (var command in NestedCommands(p, ev))
                    if (command.Kind == CommandKind.TransferPlayer && maps.ContainsKey(command.TargetId))
                        queue.Enqueue(command.TargetId);
            }
        }

        var result = new List<TraceCommand>();
        var seenDialogueEffects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var mapId in mapOrder)
        {
            var map = maps[mapId];
            foreach (var eventId in map.EventIds)
            {
                if (!events.TryGetValue(eventId, out var ev) || ev.MapId != map.Id) continue;
                for (var pi = 0; pi < ev.Pages.Count; pi++)
                {
                    var page = ev.Pages[pi];
                    for (var ci = 0; ci < page.Commands.Count; ci++)
                    {
                        var source = $"event:{ev.Id}/page:{pi}/command:{ci}";
                        AddTraceCommand(p, page.Commands[ci], mapId, ev.Id, source, result, seenDialogueEffects, [], null);
                    }
                }
            }
        }
        return result;
    }

    static void AddTraceCommand(
        GameProject p,
        EventCommand command,
        string mapId,
        string eventId,
        string source,
        List<TraceCommand> result,
        HashSet<string> seenDialogueEffects,
        HashSet<string> dialogueStack,
        Dictionary<string, int>? canonicalChoices)
    {
        result.Add(new(command, mapId, eventId, source));
        if (command.Kind != CommandKind.Dialogue || !dialogueStack.Add(command.TargetId)) return;
        var dialogue = p.Dialogues.FirstOrDefault(x => x.Id == command.TargetId);
        if (dialogue != null)
        {
            var reachable = ReachableNodes(dialogue, canonicalChoices);
            foreach (var node in dialogue.Nodes.Where(x => reachable.Contains(x.Id)))
                for (var i = 0; i < node.Effects.Count; i++)
                {
                    var effectSource = $"dialogue:{dialogue.Id}/node:{node.Id}/effect:{i}";
                    if (!seenDialogueEffects.Add(effectSource)) continue;
                    AddTraceCommand(p, node.Effects[i], mapId, eventId, effectSource, result, seenDialogueEffects, dialogueStack, canonicalChoices);
                }
        }
        dialogueStack.Remove(command.TargetId);
    }

    static IEnumerable<EventCommand> NestedCommands(GameProject p, EventDef ev)
    {
        var result = new List<EventCommand>();
        var seenEffects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var page in ev.Pages)
            foreach (var command in page.Commands)
                CollectNested(p, command, result, seenEffects, []);
        return result;
    }

    static void CollectNested(
        GameProject p,
        EventCommand command,
        List<EventCommand> result,
        HashSet<string> seenEffects,
        HashSet<string> stack)
    {
        result.Add(command);
        if (command.Kind != CommandKind.Dialogue || !stack.Add(command.TargetId)) return;
        var dialogue = p.Dialogues.FirstOrDefault(x => x.Id == command.TargetId);
        if (dialogue != null)
        {
            var reachable = ReachableNodes(dialogue, null);
            foreach (var node in dialogue.Nodes.Where(x => reachable.Contains(x.Id)))
                for (var i = 0; i < node.Effects.Count; i++)
                {
                    var key = $"{dialogue.Id}/{node.Id}/{i}";
                    if (seenEffects.Add(key)) CollectNested(p, node.Effects[i], result, seenEffects, stack);
                }
        }
        stack.Remove(command.TargetId);
    }

    static HashSet<string> ReachableNodes(DialogueDef dialogue, Dictionary<string, int>? canonicalChoices)
    {
        var nodes = dialogue.Nodes.GroupBy(x => x.Id, StringComparer.Ordinal).ToDictionary(x => x.Key, x => x.Last(), StringComparer.Ordinal);
        var reached = new HashSet<string>(StringComparer.Ordinal);
        var queue = new Queue<string>();
        if (!string.IsNullOrWhiteSpace(dialogue.StartNodeId)) queue.Enqueue(dialogue.StartNodeId);
        while (queue.Count > 0)
        {
            var id = queue.Dequeue();
            if (!reached.Add(id) || !nodes.TryGetValue(id, out var node)) continue;
            if (!string.IsNullOrWhiteSpace(node.NextNodeId)) queue.Enqueue(node.NextNodeId);
            if (canonicalChoices != null &&
                canonicalChoices.TryGetValue($"{dialogue.Id}/{node.Id}", out var selected) &&
                selected >= 0 && selected < node.Choices.Count)
            {
                var choice = node.Choices[selected];
                if (!string.IsNullOrWhiteSpace(choice.NextNodeId)) queue.Enqueue(choice.NextNodeId);
            }
            else
                foreach (var choice in node.Choices.Where(x => !string.IsNullOrWhiteSpace(x.NextNodeId)))
                    queue.Enqueue(choice.NextNodeId);
        }
        return reached;
    }

    static PartyState CloneParty(PartyState source, bool keepEquipment)
    {
        var result = new PartyState();
        foreach (var member in source.Members)
        {
            var clone = new PartyMember(member.Def, member.Level, member.Exp);
            if (keepEquipment)
            {
                if (member.Weapon != null) clone.Equip(member.Weapon);
                if (member.Armor != null) clone.Equip(member.Armor);
            }
            clone.Hp = clone.MaxHp;
            clone.Mp = clone.Stats.Mp;
            result.Members.Add(clone);
        }
        return result;
    }

    static BalancePartySnapshot Snapshot(PartyMember member) => new(
        member.Def.Id, member.Def.Name, member.Level, member.MaxHp, member.Stats.Mp,
        member.Stats.Attack, member.Stats.Defense, member.Stats.Speed);

    static bool HasStatus(BattleCombatant target, string status) =>
        status.Equals("sleep", StringComparison.OrdinalIgnoreCase) ? target.SleepTurns > 0 :
        status.Equals("poison", StringComparison.OrdinalIgnoreCase) && target.Poisoned;

    static bool Cures(ItemDef item, string status)
    {
        if (!item.Effect.StartsWith("cure:", StringComparison.OrdinalIgnoreCase)) return false;
        var what = item.Effect.Split(':', 2)[1];
        return what.Equals(status, StringComparison.OrdinalIgnoreCase) || what.Equals("all", StringComparison.OrdinalIgnoreCase);
    }

    static int HealAmount(ItemDef item) =>
        item.Effect.StartsWith("heal:", StringComparison.OrdinalIgnoreCase) &&
        int.TryParse(item.Effect.Split(':', 2)[1], out var amount) ? Math.Max(0, amount) : 0;

    static int GearScore(ItemDef item)
    {
        var b = item.Bonus;
        return b == null ? 0 : Math.Max(0, b.Hp) + Math.Max(0, b.Mp) * 2 +
            Math.Max(0, b.Attack) * 8 + Math.Max(0, b.Defense) * 7 + Math.Max(0, b.Speed) * 3;
    }

    static bool BonusDominates(StatBlock? better, StatBlock? worse)
    {
        if (better == null || worse == null) return false;
        var all = better.Hp >= worse.Hp && better.Mp >= worse.Mp && better.Attack >= worse.Attack &&
                  better.Defense >= worse.Defense && better.Speed >= worse.Speed;
        var any = better.Hp > worse.Hp || better.Mp > worse.Mp || better.Attack > worse.Attack ||
                  better.Defense > worse.Defense || better.Speed > worse.Speed;
        return all && any;
    }

    static string BonusText(StatBlock? b) => b == null ? "sin bonus" :
        $"HP{b.Hp}/MP{b.Mp}/Atk{b.Attack}/Def{b.Defense}/Vel{b.Speed}";

    static int SafeDamage(string formula, StatBlock attacker, StatBlock defender, int level)
    {
        try { return Math.Max(1, FormulaValidator.EvalBasicDamage(formula, attacker, defender, level)); }
        catch { return 0; }
    }

    static int PositiveAmount(string value, int fallback) =>
        int.TryParse(value, out var parsed) && parsed > 0 ? parsed : fallback;

    static void AddCopies(List<string> values, string id, int count)
    {
        if (string.IsNullOrWhiteSpace(id)) return;
        for (var i = 0; i < Math.Max(0, count); i++) values.Add(id);
    }

    static string Clip(string value, int max) =>
        value.Length <= max ? value : value[..Math.Max(0, max - 1)] + "…";
}
