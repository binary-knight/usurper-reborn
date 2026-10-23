using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 (MW): on Manwe himself the Creator's Ward still stops 55 percent of each enchant item rider after a hit,
/// the god's counter held or not (the counter lifts the cut from the strike only): the weapon procs (fire, frost,
/// lightning, poison, holy, shadow) at 0.45 of their chance, Lifedrinker and Siphon at 0.45 of their amount, Boss
/// Slayer at 0.45 of its 10 percent, equipment Thorns at 0.45 of the reflection. Terravok, Manwe's summon and a plain
/// monster take every rider as before. The fight start says so for Manwe only.
/// Every item this class registers is removed when its test ends, and every name is 30 characters or fewer.
/// </summary>
[Collection("SharedGameSingletons")]
public class MW137ManweWard : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;
    private const double Share = 0.45;   // what reaches Manwe of each rider
    private const long D = 1000;
    private const double Eps = 1e-6;
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static readonly List<int> Registered = new();
    private readonly string _prevLanguage = GameConfig.Language;
    private readonly bool _prevPacing = Pacing.Disabled;

    public MW137ManweWard()
    {
        GameConfig.Language = "en";
        Pacing.Disabled = true;
    }

    public void Dispose()
    {
        GameConfig.Language = _prevLanguage;
        Pacing.Disabled = _prevPacing;
        var db = typeof(EquipmentDatabase);
        var all = (Dictionary<int, Equipment>)db.GetField("_allEquipment", S)!.GetValue(null)!;
        var gate = db.GetField("_lock", S)!.GetValue(null)!;
        lock (Registered)
        {
            lock (gate)
                foreach (int id in Registered)
                    all.Remove(id);
            Registered.Clear();
        }
    }

    // ---------- items and fighters ----------

    private static int Register(Equipment e)
    {
        e.Name.Length.Should().BeLessOrEqualTo(30, "a test item name stays short");
        int id = EquipmentDatabase.RegisterDynamic(e);
        lock (Registered) Registered.Add(id);
        return id;
    }

    private static Equipment Blade(string name, EquipmentSlot slot = EquipmentSlot.MainHand) => new()
    {
        Name = name, Slot = slot, WeaponType = WeaponType.Sword, Handedness = WeaponHandedness.OneHanded,
        WeaponPower = 50, MinLevel = 1, Value = 100, Rarity = EquipmentRarity.Common,
    };

    private static Equipment Gear(string name, Action<Equipment> set)
    {
        var e = new Equipment
        {
            Name = name, Slot = EquipmentSlot.Body, WeaponType = WeaponType.None, Handedness = WeaponHandedness.None,
            ArmorClass = 5, MinLevel = 1, Value = 100, Rarity = EquipmentRarity.Common,
        };
        set(e);
        return e;
    }

    /// <summary>The weapon of a rider by its name, as the shop or the loot writes it.</summary>
    private static Equipment RiderWeapon(string rider, EquipmentSlot slot = EquipmentSlot.MainHand)
    {
        var e = Blade("MW137 " + rider, slot);
        switch (rider)
        {
            case "phoenix": e.RecordEnchantBase(); e.IncrementEnchantmentCount(); e.AddEnchantedKind("phoenix"); MagicShopLocation.ApplyNamedKind(e, "phoenix"); break;
            case "frostbite": e.RecordEnchantBase(); e.IncrementEnchantmentCount(); e.AddEnchantedKind("frostbite"); MagicShopLocation.ApplyNamedKind(e, "frostbite"); break;
            case "fire": e.HasFireEnchant = true; break;
            case "frost": e.HasFrostEnchant = true; break;
            case "lightning": e.HasLightningEnchant = true; break;
            case "poison": e.HasPoisonEnchant = true; break;
            case "holy": e.HasHolyEnchant = true; break;
            case "shadow": e.HasShadowEnchant = true; break;
            default: throw new ArgumentException(rider);
        }
        return e;
    }

    /// <summary>The proc chance of a rider, as the game reads it.</summary>
    private static double Chance(string rider) => rider switch
    {
        "phoenix" => GameConfig.PhoenixFireProcChance,
        "fire" => GameConfig.FireEnchantProcChance,
        "frostbite" => GameConfig.FrostbiteProcChance,
        "frost" => GameConfig.FrostEnchantProcChance,
        "lightning" => GameConfig.LightningEnchantProcChance,
        "poison" => GameConfig.PoisonEnchantProcChance,
        "holy" => GameConfig.HolyEnchantProcChance,
        "shadow" => GameConfig.ShadowEnchantProcChance,
        _ => throw new ArgumentException(rider),
    };

    /// <summary>A level 100 fighter at low HP and no mana. With counter, the main hand (or, when the main hand is
    /// empty, a plain blade in it) carries Godbane, Manwe's counter.</summary>
    private static Character Hero(Equipment? main = null, Equipment? body = null, Equipment? off = null, bool counter = false,
        CharacterAI ai = CharacterAI.Human, string name = "Hero")
    {
        var c = new Character
        {
            Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 100,
            Strength = 2000, BaseStrength = 2000, Dexterity = 50, BaseDexterity = 50, Agility = 20, BaseAgility = 20,
            Defence = 0, BaseDefence = 0, Constitution = 30, BaseConstitution = 30, Stamina = 100, BaseStamina = 100,
            HP = 1_000, MaxHP = 1_000_000, BaseMaxHP = 1_000_000, Mana = 0, MaxMana = 1_000_000, Mental = 80,
            CombatSpeed = CombatSpeed.Instant, AI = ai, AutoHeal = false,
        };
        if (counter)
        {
            main ??= Blade("MW137 Godbane Blade");
            main.AddEnchantedKind("godbane");
        }
        if (main != null) c.EquippedItems[EquipmentSlot.MainHand] = Register(main);
        if (off != null) c.EquippedItems[EquipmentSlot.OffHand] = Register(off);
        if (body != null) c.EquippedItems[EquipmentSlot.Body] = Register(body);
        return c;
    }

    public enum Foe { Manwe, Terravok, Summon, Plain }

    private static CombatEngine Engine(Character player, Random random, BossCombatContext? context, List<Character>? team = null)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, random);
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, team ?? new List<Character>());
        engine.BossContext = context;
        return engine;
    }

    /// <summary>The foe as the game builds it: a god from BuildBossFight with its context (no defence and no
    /// abilities, so a hit and a strike are plain), Manwe's summon from his Split Form, or a plain monster.</summary>
    private static (Monster Monster, BossCombatContext? Context) Target(Foe foe, Character hero)
    {
        switch (foe)
        {
            case Foe.Manwe:
            case Foe.Terravok:
            {
                var fight = OldGodBossSystem.BuildBossFight(foe == Foe.Manwe ? OldGodType.Manwe : OldGodType.Terravok, 0, BossDialogue.None, hero);
                var m = fight.Monster;
                m.OldGod.Should().Be(foe == Foe.Manwe ? OldGodType.Manwe : OldGodType.Terravok);
                m.HP = m.MaxHP = 1_000_000_000L;
                m.Defence = 0; m.ArmPow = 0; m.MagicRes = 0;
                m.SpecialAbilities = new List<string>();
                return (m, fight.Context);
            }
            case Foe.Summon:
            {
                var fight = OldGodBossSystem.BuildBossFight(OldGodType.Manwe, 0, BossDialogue.None, hero);
                var list = new List<Monster> { fight.Monster };
                var engine = Engine(hero, new Coverage136Random(0.99), fight.Context);
                var result = new CombatResult { Player = hero, Monsters = new List<Monster>(), CombatLog = new List<string>() };
                ((Task<bool>)typeof(CombatEngine).GetMethod("TryManweBossAbility", F)!
                    .Invoke(engine, new object?[] { fight.Monster, hero, "Split Form", result, list })!).GetAwaiter().GetResult();
                list.Should().HaveCount(2, "Split Form brings the Shadow of Manwe");
                var shadow = list[1];
                shadow.OldGod.Should().BeNull("the summon is not the god");
                shadow.HP = shadow.MaxHP = 1_000_000_000L;
                shadow.Defence = 0; shadow.ArmPow = 0;
                return (shadow, null);
            }
            default:
                return (new Monster
                {
                    Name = "Cave Bear", Level = 60, HP = 1_000_000_000L, MaxHP = 1_000_000_000L, Strength = 10, Defence = 0, ArmPow = 0,
                    IsActive = true,
                }, null);
        }
    }

    private sealed class Hit
    {
        public long Loss;
        public long Healed;
        public long Mana;
        public Monster Monster = null!;
    }

    /// <summary>The real dungeon post-hit step on a hit of D, every draw at <paramref name="roll"/>.</summary>
    private static Hit PostHit(Character attacker, Foe foe, double roll, EquipmentSlot slot = EquipmentSlot.MainHand, Character? player = null)
    {
        var owner = player ?? attacker;
        var (m, ctx) = Target(foe, owner);
        var team = player != null ? new List<Character> { attacker } : new List<Character>();
        var engine = Engine(owner, new Coverage136Random(roll), ctx, team);
        long hp0 = attacker.HP, mana0 = attacker.Mana, m0 = m.HP;
        typeof(CombatEngine).GetMethod("ApplyPostHitEnchantments", F)!
            .Invoke(engine, new object[] { attacker, m, D, new CombatResult { Player = owner, Teammates = team }, false, slot, false });
        return new Hit { Loss = m0 - m.HP, Healed = attacker.HP - hp0, Mana = attacker.Mana - mana0, Monster = m };
    }

    private static bool Procced(string rider, Hit hit) => rider switch
    {
        "frost" or "frostbite" => hit.Monster.IsSlowed,
        "poison" => hit.Monster.EnchantPoisonRounds > 0,
        _ => hit.Loss > 0,
    };

    public static IEnumerable<object[]> Riders() =>
        new[] { "phoenix", "fire", "frostbite", "frost", "lightning", "poison", "holy", "shadow" }.Select(r => new object[] { r });

    public static IEnumerable<object[]> RidersAndCounter() =>
        Riders().SelectMany(r => new[] { new object[] { r[0], false }, new object[] { r[0], true } });

    public static IEnumerable<object[]> RidersAndOtherFoes() =>
        Riders().SelectMany(r => new[] { Foe.Terravok, Foe.Summon, Foe.Plain }.Select(f => new object[] { r[0], f }));

    // ---------- the const ----------

    [Fact]
    public void MW137_TheWardOnRiders_IsFiftyFivePercent()
    {
        var field = typeof(GameConfig).GetField("ManweWardOnRiders", BindingFlags.Public | BindingFlags.Static);
        field.Should().NotBeNull("GameConfig names the share of each rider the Creator's Ward stops");
        ((double)field!.GetValue(null)!).Should().Be(0.55);
        (1.0 - (double)field.GetValue(null)!).Should().BeApproximately(Share, 1e-12);
    }

    // ---------- the weapon procs ----------

    [Theory]
    [MemberData(nameof(RidersAndCounter))]
    public void MW137_Proc_OnManwe_TakesFortyFivePercentOfItsChance(string rider, bool counter)
    {
        double cut = Chance(rider) * Share;
        Procced(rider, PostHit(Hero(RiderWeapon(rider), counter: counter), Foe.Manwe, cut - Eps))
            .Should().BeTrue($"{rider} procs below 0.45 of its chance on Manwe, counter {counter}");
        Procced(rider, PostHit(Hero(RiderWeapon(rider), counter: counter), Foe.Manwe, cut + Eps))
            .Should().BeFalse($"{rider} does not proc past 0.45 of its chance on Manwe, counter {counter}");
        Procced(rider, PostHit(Hero(RiderWeapon(rider), counter: counter), Foe.Manwe, Chance(rider) - Eps))
            .Should().BeFalse($"{rider} at its full chance does not proc on Manwe, counter {counter}");
    }

    [Theory]
    [MemberData(nameof(RidersAndOtherFoes))]
    public void MW137_Proc_OnAnyOtherFoe_KeepsItsFullChance(string rider, Foe foe)
    {
        Procced(rider, PostHit(Hero(RiderWeapon(rider)), foe, Chance(rider) * Share + Eps))
            .Should().BeTrue($"{rider} on {foe} procs past 0.45 of its chance");
        Procced(rider, PostHit(Hero(RiderWeapon(rider)), foe, Chance(rider) - Eps))
            .Should().BeTrue($"{rider} on {foe} procs below its full chance");
        Procced(rider, PostHit(Hero(RiderWeapon(rider)), foe, Chance(rider) + Eps))
            .Should().BeFalse($"{rider} on {foe} does not proc past its full chance");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MW137_Proc_OffHandAndTeammate_OnManwe_AreCutAlike(bool counter)
    {
        double cut = GameConfig.PhoenixFireProcChance * Share;
        // the off hand weapon's proc on its own swing
        var dual = Hero(Blade("MW137 Main"), off: RiderWeapon("phoenix", EquipmentSlot.OffHand), counter: counter);
        PostHit(dual, Foe.Manwe, cut - Eps, EquipmentSlot.OffHand).Loss.Should().BeGreaterThan(0, "the off hand procs below the cut chance");
        dual = Hero(Blade("MW137 Main"), off: RiderWeapon("phoenix", EquipmentSlot.OffHand), counter: counter);
        PostHit(dual, Foe.Manwe, cut + Eps, EquipmentSlot.OffHand).Loss.Should().Be(0, "the off hand is cut as the main hand");
        // a teammate's proc
        var player = Hero(counter: counter);
        PostHit(Hero(RiderWeapon("phoenix"), ai: CharacterAI.Computer, name: "Mate"), Foe.Manwe, cut - Eps, player: player)
            .Loss.Should().BeGreaterThan(0, "a teammate procs below the cut chance");
        PostHit(Hero(RiderWeapon("phoenix"), ai: CharacterAI.Computer, name: "Mate"), Foe.Manwe, cut + Eps, player: player)
            .Loss.Should().Be(0, "a teammate's proc is cut as the player's");
    }

    // ---------- Lifedrinker and Siphon ----------

    public static IEnumerable<object[]> AllFoesAndCounter() =>
        new[] { Foe.Manwe, Foe.Terravok, Foe.Summon, Foe.Plain }.SelectMany(f => new[] { new object[] { f, false }, new object[] { f, true } });

    [Theory]
    [MemberData(nameof(AllFoesAndCounter))]
    public void MW137_Lifedrinker_HealsFortyFivePercentOnManweOnly(Foe foe, bool counter)
    {
        var hero = Hero(Blade("MW137 Plain"), Gear("MW137 Leech Mail", e => e.LifeSteal = 10), counter: counter);
        long full = Math.Max(1, D * 10 / 100);
        PostHit(hero, foe, 0.99).Healed.Should().Be(foe == Foe.Manwe ? (long)(full * Share) : full, $"Lifedrinker on {foe}, counter {counter}");
        var mate = Hero(Blade("MW137 Plain"), Gear("MW137 Leech Mail", e => e.LifeSteal = 10), ai: CharacterAI.Computer, name: "Mate");
        PostHit(mate, foe, 0.99, player: Hero(counter: counter)).Healed
            .Should().Be(foe == Foe.Manwe ? (long)(full * Share) : full, $"a teammate's Lifedrinker on {foe}");
    }

    [Theory]
    [MemberData(nameof(AllFoesAndCounter))]
    public void MW137_Siphon_RestoresFortyFivePercentOnManweOnly(Foe foe, bool counter)
    {
        var hero = Hero(Blade("MW137 Plain"), Gear("MW137 Siphon Mail", e => e.ManaSteal = 10), counter: counter);
        long full = Math.Max(1, D * 10 / 100);
        PostHit(hero, foe, 0.99).Mana.Should().Be(foe == Foe.Manwe ? (long)(full * Share) : full, $"Siphon on {foe}, counter {counter}");
        var mate = Hero(Blade("MW137 Plain"), Gear("MW137 Siphon Mail", e => e.ManaSteal = 10), ai: CharacterAI.Computer, name: "Mate");
        PostHit(mate, foe, 0.99, player: Hero(counter: counter)).Mana
            .Should().Be(foe == Foe.Manwe ? (long)(full * Share) : full, $"a teammate's Siphon on {foe}");
    }

    // ---------- Boss Slayer ----------

    private static Equipment Slayer() => Gear("MW137 Slayer Mail", e => e.HasBossSlayer = true);

    /// <summary>The added share of Boss Slayer on a boss: 0.10 times 0.45 on Manwe, 0.10 on Terravok.</summary>
    private static double SlayerAdd(Foe foe) => foe == Foe.Manwe ? 0.10 * Share : 0.10;

    public static IEnumerable<object[]> GodsAndCounter() =>
        new[] { Foe.Manwe, Foe.Terravok }.SelectMany(f => new[] { new object[] { f, false }, new object[] { f, true } });

    [Theory]
    [MemberData(nameof(GodsAndCounter))]
    public void MW137_BossSlayer_OnAPowerOrAbilityHit_AddsFortyFivePercentOfItsTenOnManwe(Foe foe, bool counter)
    {
        // ApplyBossSlayer, the class ability and Power Attack site
        var hero = Hero(Blade("MW137 Plain"), Slayer(), counter: counter);
        var (m, _) = Target(foe, hero);
        const long hit = 100_000;
        CombatEngine.ApplyBossSlayer(hero, m, hit).Should().Be(hit + (long)(hit * SlayerAdd(foe)), $"{foe}, counter {counter}");

        // through the ability handler helper, with the god's context (its divine armor after the add)
        long Dealt(bool slayer)
        {
            var h = Hero(Blade("MW137 Plain"), slayer ? Slayer() : null, counter: counter);
            var (t, ctx) = Target(foe, h);
            var engine = Engine(h, new Coverage136Random(0.99), ctx);
            var reap = new ClassAbilityResult { SpecialEffect = "execute", Damage = (int)hit };
            return (long)typeof(CombatEngine).GetMethod("ApplyHandlerAbilityDamage", F)!
                .Invoke(engine, new object[] { h, t, hit, new CombatResult { Player = h }, reap })!;
        }
        long without = Dealt(false);
        without.Should().BeGreaterThan(1000);
        ((double)Dealt(true) / without).Should().BeApproximately(1.0 + SlayerAdd(foe), 0.0005, $"{foe}, counter {counter}");
    }

    [Theory]
    [InlineData(Foe.Summon)]
    [InlineData(Foe.Plain)]
    public void MW137_BossSlayer_NothingOnTheSummonOrAPlainMonster(Foe foe)
    {
        var hero = Hero(Blade("MW137 Plain"), Slayer());
        var (m, _) = Target(foe, hero);
        CombatEngine.ApplyBossSlayer(hero, m, 100_000).Should().Be(100_000, "neither is a boss");
    }

    /// <summary>The live swing (the multi-monster path): the damage of one main hand swing on a fresh engine.</summary>
    private static long Swing(Character hero, Monster target, BossCombatContext? ctx) =>
        (long)typeof(CombatEngine).GetMethod("ComputePlayerSwingDamage", F)!
            .Invoke(Engine(hero, new Coverage136Random(0.99), ctx), new object[] { hero, target, false, 0, 1 })!;

    [Theory]
    [MemberData(nameof(GodsAndCounter))]
    public void MW137_BossSlayer_OnTheLiveSwing_AddsFortyFivePercentOfItsTenOnManwe(Foe foe, bool counter)
    {
        var plain = Hero(Blade("MW137 Plain"), counter: counter);
        var slayer = Hero(Blade("MW137 Plain"), Slayer(), counter: counter);
        var (m, ctx) = Target(foe, plain);
        long without = Swing(plain, m, ctx);
        without.Should().BeGreaterThan(1000);
        ((double)Swing(slayer, m, ctx) / without).Should().BeApproximately(1.0 + SlayerAdd(foe), 0.0005, $"{foe}, counter {counter}");
    }

    /// <summary>The single target attack (ExecuteSingleAttack): the HP one swing takes, every draw at 0.99.</summary>
    private static long SingleAttack(Character hero, Foe foe)
    {
        var (m, ctx) = Target(foe, hero);
        var engine = Engine(hero, new Coverage136Random(0.99), ctx);
        long m0 = m.HP;
        ((Task)typeof(CombatEngine).GetMethod("ExecuteSingleAttack", F)!
            .Invoke(engine, new object[] { hero, m, new CombatResult { Player = hero, CombatLog = new List<string>() }, false, false })!).GetAwaiter().GetResult();
        return m0 - m.HP;
    }

    [Theory]
    [MemberData(nameof(GodsAndCounter))]
    public void MW137_BossSlayer_OnTheSingleAttack_AddsFortyFivePercentOfItsTenOnManwe(Foe foe, bool counter)
    {
        long without = SingleAttack(Hero(Blade("MW137 Plain"), counter: counter), foe);
        without.Should().BeGreaterThan(1000);
        ((double)SingleAttack(Hero(Blade("MW137 Plain"), Slayer(), counter: counter), foe) / without)
            .Should().BeApproximately(1.0 + SlayerAdd(foe), 0.0005, $"{foe}, counter {counter}");
    }

    // ---------- equipment Thorns ----------

    private static Monster Striker(Foe foe, Character hero, out BossCombatContext? ctx)
    {
        var (m, c) = Target(foe, hero);
        m.Strength = 3000; m.WeapPow = 3000; m.Level = 100;
        ctx = c;
        return m;
    }

    /// <summary>One monster turn on the player: what the player lost and what the monster lost.</summary>
    private static (long HeroLoss, long MonsterLoss) StrikePlayer(Foe foe, int thorns, bool counter)
    {
        var hero = Hero(Blade("MW137 Plain"), thorns > 0 ? Gear("MW137 Thorn Mail", e => e.Thorns = thorns) : null, counter: counter);
        hero.HP = hero.MaxHP;
        var m = Striker(foe, hero, out var ctx);
        var engine = Engine(hero, new Coverage136Random(0.99), ctx);
        long h0 = hero.HP, m0 = m.HP;
        var result = new CombatResult { Player = hero, CombatLog = new List<string>(), CurrentRound = 5 };
        ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(engine, new object?[] { m, hero, result, new List<Monster> { m } })!).GetAwaiter().GetResult();
        return (h0 - hero.HP, m0 - m.HP);
    }

    /// <summary>One monster turn on a companion: what the companion lost and what the monster lost.</summary>
    private static (long MateLoss, long MonsterLoss) StrikeCompanion(Foe foe, int thorns, bool counter)
    {
        var hero = Hero(counter: counter);
        var mate = Hero(Blade("MW137 Plain"), thorns > 0 ? Gear("MW137 Thorn Mail", e => e.Thorns = thorns) : null,
            ai: CharacterAI.Computer, name: "Mate");
        mate.HP = mate.MaxHP;
        var m = Striker(foe, hero, out var ctx);
        var engine = Engine(hero, new Coverage136Random(0.99), ctx, new List<Character> { mate });
        long c0 = mate.HP, m0 = m.HP;
        var result = new CombatResult { Player = hero, Teammates = new List<Character> { mate }, CombatLog = new List<string>(), CurrentRound = 5 };
        ((Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!
            .Invoke(engine, new object?[] { m, mate, result, new List<Monster> { m } })!).GetAwaiter().GetResult();
        return (c0 - mate.HP, m0 - m.HP);
    }

    private static long Thorns(Foe foe, long taken, int pct) =>
        foe == Foe.Manwe ? Math.Max(1, (long)(taken * pct / 100 * Share)) : Math.Max(1, taken * pct / 100);

    [Theory]
    [MemberData(nameof(AllFoesAndCounter))]
    public void MW137_Thorns_ThePlayersReflection_IsFortyFivePercentOnManweOnly(Foe foe, bool counter)
    {
        var none = StrikePlayer(foe, 0, counter);
        none.HeroLoss.Should().BeGreaterThan(1000, $"{foe}'s strike landed");
        none.MonsterLoss.Should().Be(0, "no thorns gear, nothing reflected");
        var with = StrikePlayer(foe, 20, counter);
        with.HeroLoss.Should().Be(none.HeroLoss);
        with.MonsterLoss.Should().Be(Thorns(foe, with.HeroLoss, 20), $"the player's thorns on {foe}, counter {counter}");
    }

    [Theory]
    [MemberData(nameof(AllFoesAndCounter))]
    public void MW137_Thorns_ACompanionsReflection_IsFortyFivePercentOnManweOnly(Foe foe, bool counter)
    {
        var none = StrikeCompanion(foe, 0, counter);
        none.MateLoss.Should().BeGreaterThan(1000, $"{foe}'s strike landed");
        none.MonsterLoss.Should().Be(0, "no thorns gear, nothing reflected");
        var with = StrikeCompanion(foe, 20, counter);
        with.MateLoss.Should().Be(none.MateLoss);
        with.MonsterLoss.Should().Be(Thorns(foe, with.MateLoss, 20), $"a companion's thorns on {foe}, counter {counter}");
    }

    // ---------- the fight start ----------

    private static string Intro(OldGodType god, Character hero)
    {
        var data = OldGodsData.GetGodBossData(god);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes("\n")), output);
        ((Task)typeof(OldGodBossSystem).GetMethod("PlayBossIntroduction", F)!
            .Invoke(OldGodBossSystem.Instance, new object[] { data, hero, term })!).GetAwaiter().GetResult();
        term.StreamWriterInternal!.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
    }

    private static string Norm(string s) => Regex.Replace(s, @"\s+", " ").Trim();

    private static string WardLine() => Loc.Get("old_god.ward_on_riders", "55");

    [Theory]
    [InlineData("en", "The Creator's Ward still stops 55% of what your gear's enchantments add.")]
    [InlineData("es", "55%")]
    [InlineData("fr", "55%")]
    [InlineData("hu", "55%-ot")]
    [InlineData("it", "55%")]
    public void MW137_FightStart_ManweSaysTheWardStopsFiftyFivePercentOfGearEnchants_CounterOrNot(string lang, string part)
    {
        GameConfig.Language = lang;
        WardLine().Should().Contain(part, lang);
        foreach (bool counter in new[] { false, true })
            Norm(Intro(OldGodType.Manwe, Hero(counter: counter))).Should().Contain(Norm(WardLine()), $"{lang} Manwe, counter {counter}");
    }

    [Theory]
    [InlineData(OldGodType.Aurelion)]
    [InlineData(OldGodType.Terravok)]
    [InlineData(OldGodType.Maelketh)]
    [InlineData(OldGodType.Noctura)]
    public void MW137_FightStart_NoOtherGodShowsTheLine(OldGodType god)
    {
        foreach (bool counter in new[] { false, true })
        {
            string shown = Norm(Intro(god, Hero(counter: counter)));
            shown.Should().NotContain(Norm(WardLine()), $"{god}, counter {counter}");
            shown.Should().NotContain("enchantments add", god.ToString());
        }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull("the test must run inside the repository");
        return dir!.FullName;
    }

    [Fact]
    public void MW137_WardLine_IsInEveryLanguage_Translated_NoDash_AndFits()
    {
        var files = Langs.ToDictionary(l => l, l =>
            JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", l + ".json")))!);
        string en = files["en"]["old_god.ward_on_riders"].GetString()!;
        foreach (var lang in Langs)
        {
            files[lang].Should().ContainKey("old_god.ward_on_riders", lang);
            string text = files[lang]["old_god.ward_on_riders"].GetString()!;
            text.Should().Contain("{0}", lang);
            text.Should().NotMatchRegex("[\u2013\u2014]", $"{lang}: no dash");
            if (lang != "en") text.Should().NotBe(en, $"{lang} is translated");
            string row = string.Format(text, "55");
            if (lang == "en" || lang == "hu")
                row.TrimEnd().Length.Should().BeLessOrEqualTo(79, $"{lang} row fits: [{row}]");
        }
        en.Should().NotContainEquivalentOf("every enchant", "class enchants are not covered");
    }
}
