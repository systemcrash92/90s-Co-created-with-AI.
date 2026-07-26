using System.Text.Json;
using System.Text.Json.Nodes;

namespace Seto90;

/// <summary>Contrato headless del auditor de balance: progresion, simulacion, economia y MCP.</summary>
public sealed class BalanceAuditSmokeTest
{
    public string Run()
    {
        var project = Fixture();
        var first = BalanceAudit.Analyze(project);
        var second = BalanceAudit.Analyze(project);
        Expect(JsonSerializer.Serialize(first) == JsonSerializer.Serialize(second), "el informe no es determinista");
        Expect(first.Battles.Count == 2, $"checkpoints={first.Battles.Count}, esperaba 2");
        Expect(first.Shops.Count == 1 && first.Shops[0].AffordableItems == 3, "la asequibilidad de la primera tienda es incorrecta");
        Expect(first.Battles[0].Basic.Defeat, "el encuentro inicial sin preparacion debia perderse");
        Expect(first.Battles[0].Prepared.Victory, "el encuentro inicial preparado debia ganarse");
        Expect(first.Battles[1].Party.Single().Level == 2, "la EXP previa no proyecto el level up");
        Expect(first.Battles[1].Prepared.Defeat, "el jefe imposible no quedo derrotado aun preparado");
        Expect(first.Battles[0].Purchases.Any(x => x.Contains("Espada buena", StringComparison.Ordinal)), "la preparacion no compro el upgrade que vuelve ganable el combate");

        var codes = first.Findings.Select(x => x.Code).ToHashSet(StringComparer.Ordinal);
        foreach (var expected in new[]
        {
            "battle_unwinnable_prepared", "dominated_equipment",
            "dominated_healing_item", "skill_unusable_in_projection",
            "scripted_cost_unaffordable"
        })
            Expect(codes.Contains(expected), $"falto el hallazgo '{expected}'");

        var warningsOnly = BalanceAudit.Analyze(project, includeInfo: false);
        Expect(warningsOnly.Findings.All(x => x.Severity == "warning") && warningsOnly.Summary.ReviewNotes == 0,
            "includeInfo=false no filtro las notas de tuning");

        var catalog = JsonSerializer.SerializeToNode(ToolRegistry.List())?.AsArray() ?? [];
        Expect(catalog.Count == 57, $"tools/list declara {catalog.Count}, esperaba 57");
        Expect(catalog.Any(x => x?["name"]?.GetValue<string>() == "balance.audit"), "balance.audit no figura en tools/list");
        Expect(ToolRegistry.WriteNote("balance.audit", new JsonObject()) is null, "balance.audit intento escribir bitacora");

        var root = Path.Combine(Path.GetTempPath(), $"seto90-balance-smoke-{Guid.NewGuid():N}");
        try
        {
            new ProjectStore(root).Save(project);
            var session = new CommandSession(root);
            var payload = ToolRegistry.Call("balance.audit", new JsonObject
            {
                ["includeInfo"] = true
            }, session);
            Expect(payload.Ok && payload.Data is BalanceAuditReport, "balance.audit no devolvio un informe estructurado");
        }
        finally
        {
            if (Directory.Exists(root)) Directory.Delete(root, true);
        }

        return $"balance smoke OK: {first.Battles.Count} checkpoints, progresion/gear/tactica/economia deterministas, {first.Summary.Warnings} riesgos + {first.Summary.ReviewNotes} notas.";
    }

    static GameProject Fixture()
    {
        var hero = new ActorDef
        {
            Id = "actor.hero",
            Name = "Heroe",
            Level = 1,
            Stats = new StatBlock { Hp = 20, Mp = 2, Attack = 5, Defense = 2, Speed = 4 },
            SkillIds = ["skill.caro"]
        };
        var goodSword = new ItemDef
        {
            Id = "item.espada_buena", Name = "Espada buena", Price = 10, Slot = "weapon",
            Bonus = new StatBlock { Hp = 0, Mp = 0, Attack = 5, Defense = 0, Speed = 0 }
        };
        var badSword = new ItemDef
        {
            Id = "item.espada_mala", Name = "Espada mala", Price = 15, Slot = "weapon",
            Bonus = new StatBlock { Hp = 0, Mp = 0, Attack = 2, Defense = 0, Speed = 0 }
        };
        var goodPotion = new ItemDef { Id = "item.cura_buena", Name = "Cura buena", Price = 5, Effect = "heal:10" };
        var badPotion = new ItemDef { Id = "item.cura_mala", Name = "Cura mala", Price = 8, Effect = "heal:5" };
        var shop = new ShopDef
        {
            Id = "shop.base",
            Name = "Tienda",
            ItemIds = [goodSword.Id, badSword.Id, goodPotion.Id, badPotion.Id]
        };
        var slime = new EnemyDef
        {
            Id = "enemy.slime", Name = "Slime",
            Stats = new StatBlock { Hp = 20, Attack = 8, Defense = 1, Speed = 2 },
            Exp = 20, Money = 10
        };
        var boss = new EnemyDef
        {
            Id = "enemy.jefe", Name = "Jefe",
            Stats = new StatBlock { Hp = 100, Attack = 20, Defense = 4, Speed = 6 },
            Exp = 40, Money = 20
        };
        var firstBattle = new BattleDef
        {
            Id = "battle.primero",
            EnemyIds = [slime.Id],
            DamageFormula = "max(1, attack - defense)"
        };
        var bossBattle = new BattleDef
        {
            Id = "battle.jefe",
            EnemyIds = [boss.Id],
            DamageFormula = "max(1, attack - defense)",
            Boss = true
        };
        var events = new[]
        {
            new EventDef
            {
                Id = "event.tienda", MapId = "map.inicio", Kind = EventKind.Npc,
                Pages = [new EventPage { Id = "default", Commands = [new EventCommand { Kind = CommandKind.OpenShop, TargetId = shop.Id }] }]
            },
            new EventDef
            {
                Id = "event.primero", MapId = "map.inicio", Kind = EventKind.Trigger,
                Pages = [new EventPage { Id = "default", Commands = [new EventCommand { Kind = CommandKind.Battle, TargetId = firstBattle.Id }] }]
            },
            new EventDef
            {
                Id = "event.jefe", MapId = "map.inicio", Kind = EventKind.Cutscene,
                Pages = [new EventPage { Id = "default", Commands = [new EventCommand { Kind = CommandKind.Battle, TargetId = bossBattle.Id }] }]
            },
            new EventDef
            {
                Id = "event.peaje_imposible", MapId = "map.inicio", Kind = EventKind.Cutscene,
                Pages = [new EventPage { Id = "default", Commands = [new EventCommand { Kind = CommandKind.TakeMoney, Value = "99" }] }]
            }
        };
        return new GameProject
        {
            Id = "balance.smoke",
            Title = "Balance",
            StartMapId = "map.inicio",
            StartMoney = 10,
            PartyActorIds = [hero.Id],
            Actors = [hero],
            Skills = [new SkillDef { Id = "skill.caro", Name = "Rayo imposible", Kind = "damage", Power = 30, MpCost = 9 }],
            Items = [goodSword, badSword, goodPotion, badPotion],
            Shops = [shop],
            Enemies = [slime, boss],
            Battles = [firstBattle, bossBattle],
            Events = [.. events],
            Maps =
            [
                new MapDef
                {
                    Id = "map.inicio", Width = 8, Height = 8,
                    Tiles = Enumerable.Repeat(0, 64).ToList(),
                    EventIds = [.. events.Select(x => x.Id)]
                }
            ]
        };
    }

    static void Expect(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException("balance smoke: " + message);
    }
}
