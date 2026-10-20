using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>A Random that counts its draws; every NextDouble gives the same value, every Next the offset n from its lowest, inside its range.</summary>
internal sealed class Coverage136Random : Random
{
    private readonly double _v;
    private readonly int _n;
    public int Draws;
    public Coverage136Random(double v, int n = 99) { _v = v; _n = n; }
    public override int Next() { Draws++; return _n; }
    public override int Next(int maxValue) { Draws++; return maxValue <= 0 ? 0 : Math.Min(_n, maxValue - 1); }
    public override int Next(int minValue, int maxValue) { Draws++; return maxValue <= minValue ? minValue : Math.Min(minValue + _n, maxValue - 1); }
    public override double NextDouble() { Draws++; return _v; }
}

/// <summary>
/// 1.3.6 (E4, enchant coverage extended): (a) a companion's own equipment thorns answer a monster's basic attack;
/// (b) Boss Slayer adds its 10 percent to the Power Attack and class ability hits, never to spells; (c) PvP: the
/// weapon procs on the player's hit, and procs and lifesteal on the NPC's basic attack; (d) world boss: the main
/// hand's procs and lifesteal on the player's weapon hits, added to the pooled hit. Weapon poison in PvP and on the
/// world boss is one hit of the 1.3.5 amount (supervisor, ledger 2026-10-20T17:50:03Z, C1 and C2).
/// </summary>
[Collection("SharedGameSingletons")]
public class Coverage136Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    // Every item this class registers goes through Item and is named "C136 ..."; each test removes its own items when it
    // ends, so no test after it sees them in EquipmentDatabase.GetAll() (DashWidth124 reads the widest name there). The
    // class runs one test at a time (the SharedGameSingletons collection), so one list serves.
    private static readonly List<int> Registered = new();

    public void Dispose() => RemoveRegistered();

    private static void RemoveRegistered()
    {
        var db = typeof(EquipmentDatabase);
        var all = (Dictionary<int, Equipment>)db.GetField("_allEquipment", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        var gate = db.GetField("_lock", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        lock (Registered)
        {
            lock (gate)
                foreach (int id in Registered)
                    all.Remove(id);
            Registered.Clear();
        }
    }

    private static T InLanguage<T>(string lang, Func<T> body)
    {
        string saved = GameConfig.Language;
        try { GameConfig.Language = lang; return body(); }
        finally { GameConfig.Language = saved; }
    }

    private static T InEnglish<T>(Func<T> body) => InLanguage("en", body);

    private static string Plain(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");

    private static int Item(string name, EquipmentSlot slot, Action<Equipment> set)
    {
        var e = new Equipment
        {
            Name = name, Slot = slot, MinLevel = 1, Rarity = EquipmentRarity.Common,
            WeaponType = slot == EquipmentSlot.MainHand ? WeaponType.Sword : WeaponType.None,
            Handedness = slot == EquipmentSlot.MainHand ? WeaponHandedness.OneHanded : WeaponHandedness.None,
            WeaponPower = slot == EquipmentSlot.MainHand ? 50 : 0,
        };
        set(e);
        int id = EquipmentDatabase.RegisterDynamic(e);
        lock (Registered) Registered.Add(id);
        return id;
    }

    /// <summary>A hero with weapons of this class's own (removed when the test ends), in place of G.Hero's main and off.</summary>
    private static Character Armed(Character hero, string tag, int main, WeaponType type, int? off = null)
    {
        hero.EquippedItems[EquipmentSlot.MainHand] = Item($"C136 {tag} main", EquipmentSlot.MainHand, e => { e.WeaponType = type; e.WeaponPower = main; });
        if (off.HasValue)
            hero.EquippedItems[EquipmentSlot.OffHand] = Item($"C136 {tag} off", EquipmentSlot.OffHand, e =>
            { e.WeaponType = WeaponType.Sword; e.Handedness = WeaponHandedness.OneHanded; e.WeaponPower = off.Value; });
        hero.RecalculateStats();
        hero.HP = hero.MaxHP;
        return hero;
    }

    /// <summary>The enchant kinds a weapon proc can carry; "poison37" is weapon poison with a fixed PoisonDamage of 37.</summary>
    private static void Enchant(Equipment e, string kind)
    {
        switch (kind)
        {
            case "fire": e.HasFireEnchant = true; break;
            case "frost": e.HasFrostEnchant = true; break;
            case "lightning": e.HasLightningEnchant = true; break;
            case "poison": e.HasPoisonEnchant = true; break;
            case "poison37": e.HasPoisonEnchant = true; e.PoisonDamage = 37; break;
            case "holy": e.HasHolyEnchant = true; break;
            case "shadow": e.HasShadowEnchant = true; break;
            case "plain": break;
            default: throw new ArgumentException(kind);
        }
    }

    private static int Blade(string tag, string kind, int lifeSteal = 0) =>
        Item($"C136 {tag} {kind} {lifeSteal}", EquipmentSlot.MainHand, e => { Enchant(e, kind); e.LifeSteal = lifeSteal; });

    /// <summary>The rider a hit of <paramref name="hit"/> gets from one proc, by the PvE step's arithmetic (poison as 1.3.5's one hit).</summary>
    private static long Rider(string kind, long hit, bool aurelion = false) => kind switch
    {
        "fire" => Math.Max(1, (long)(hit * GameConfig.FireEnchantDamageMultiplier)),
        "lightning" => Math.Max(1, (long)(hit * GameConfig.LightningEnchantDamageMultiplier)),
        "poison" => Math.Max(1, (int)(hit * 0.10)),
        "poison37" => 37,
        "holy" => Math.Max(1, (long)(hit * (aurelion ? GameConfig.HolyEnchantDamageMultiplier * (1f + GameConfig.ShrineAurelionHolyProcBonus) : GameConfig.HolyEnchantDamageMultiplier))),
        "shadow" => Math.Max(1, (long)(hit * GameConfig.ShadowEnchantDamageMultiplier)),
        _ => 0,
    };

    // The English words of each proc's line, the player's form and the named form.
    private static string PlayerLine(string kind) => kind switch
    {
        "fire" => "(Phoenix Fire)",
        "frost" => "is slowed! (Frostbite)",
        "lightning" => "(Thunderstrike)",
        "poison" or "poison37" => "Venom seeps into the wound!",
        "holy" => "(Radiance)",
        "shadow" => "(Darkstrike)",
        _ => "",
    };

    private static string NamedLine(string kind) => kind switch
    {
        "poison" or "poison37" => "'s blade weeps venom!",
        _ => PlayerLine(kind),
    };

    private static void Attune(Character c)
    {
        c.AttunedShrineId = "aurelion";
        c.AttunedShrineExpiresGameDay = int.MaxValue;
        c.AttunedShrineExpiresUtc = DateTime.MaxValue;
    }

    // ================================================================ (c) PvP

    private static Character Duelist(string name, CharacterAI ai, int? mainHand = null)
    {
        var c = new Character
        {
            Name1 = name, Name2 = name, AI = ai, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 30,
            HP = 100_000, MaxHP = 100_000, BaseMaxHP = 100_000, Strength = 300, BaseStrength = 300, Defence = 10, BaseDefence = 10,
            Dexterity = 20, BaseDexterity = 20, Agility = 20, BaseAgility = 20, Constitution = 20, BaseConstitution = 20,
            Stamina = 100, BaseStamina = 100, Healing = 0, AutoHeal = false, CombatSpeed = CombatSpeed.Instant,
        };
        if (mainHand.HasValue) c.EquippedItems[EquipmentSlot.MainHand] = mainHand.Value;
        return c;
    }

    private sealed class Pvp
    {
        public CombatEngine Engine = null!;
        public TerminalEmulator Terminal = null!;
        public MemoryStream Output = new();
        public Coverage136Random Random = null!;
        public CombatResult Result = null!;
        public string Shown() { Terminal.StreamWriterInternal?.Flush(); return Plain(Encoding.UTF8.GetString(Output.ToArray())); }
    }

    private static Pvp Duel(Character human, Character npc, Coverage136Random random)
    {
        var p = new Pvp { Random = random };
        p.Terminal = new TerminalEmulator(new MemoryStream(), p.Output);
        p.Engine = new CombatEngine(p.Terminal);
        typeof(CombatEngine).GetField("random", F)!.SetValue(p.Engine, random);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(p.Engine, human);
        typeof(CombatEngine).GetField("_combatOwner", F)!.SetValue(p.Engine, human);
        p.Result = new CombatResult { Player = human, Opponent = npc, CombatLog = new List<string>() };
        return p;
    }

    private static void PlayerHit(Pvp p, Character a, Character d) =>
        ((Task)typeof(CombatEngine).GetMethod("ExecutePvPSingleHit", F)!.Invoke(p.Engine, new object[] { a, d, p.Result, false })!).GetAwaiter().GetResult();

    private static void NpcTurn(Pvp p, Character npc, Character human) =>
        ((Task)typeof(CombatEngine).GetMethod("ProcessComputerPlayerAction", F)!.Invoke(p.Engine, new object[] { npc, human, p.Result })!).GetAwaiter().GetResult();

    private static long Struck(string shown, string pattern)
    {
        var m = Regex.Match(shown, pattern);
        m.Success.Should().BeTrue("the hit line is shown: " + shown);
        return long.Parse(m.Groups[1].Value);
    }

    /// <summary>One player hit in a duel; returns (the hit, the defender's loss, the draws, the screen).</summary>
    private static (long Hit, long Loss, int Draws, string Shown, Character Defender) PlayerPvp(string kind, double roll, bool aurelion = false, int lifeSteal = 0, int? weapon = -1)
    {
        var a = Duelist("Attacker", CharacterAI.Human, weapon == -1 ? Blade("pvp", kind, lifeSteal) : weapon);
        if (aurelion) Attune(a);
        a.HP = a.MaxHP / 2;
        var d = Duelist("Defender", CharacterAI.Computer);
        var p = Duel(a, d, new Coverage136Random(roll));
        PlayerHit(p, a, d);
        string shown = p.Shown();
        long hit = Struck(shown, @"You strike Defender for (\d+) damage!");
        return (hit, d.MaxHP - d.HP, p.Random.Draws, shown, d);
    }

    /// <summary>One NPC basic attack on the human (no heal, no spell, no ability at these rolls).</summary>
    private static (long Hit, long Loss, long NpcGain, int Draws, string Shown, Character Human) NpcPvp(string kind, double roll, int lifeSteal = 0, bool aurelion = false, int? weapon = -1)
    {
        var npc = Duelist("Npc", CharacterAI.Computer, weapon == -1 ? Blade("npc", kind, lifeSteal) : weapon);
        if (aurelion) Attune(npc);
        npc.HP = npc.MaxHP / 2;
        var human = Duelist("Human", CharacterAI.Human);
        var p = Duel(human, npc, new Coverage136Random(roll));
        long n0 = npc.HP;
        NpcTurn(p, npc, human);
        string shown = p.Shown();
        long hit = Struck(shown, @"Npc strikes for (\d+) damage!");
        return (hit, human.MaxHP - human.HP, npc.HP - n0, p.Random.Draws, shown, human);
    }

    [Theory]
    [InlineData("fire")]
    [InlineData("lightning")]
    [InlineData("poison")]
    [InlineData("poison37")]
    [InlineData("holy")]
    [InlineData("shadow")]
    public void Coverage136_PvP_PlayerHit_EachDamageProcAddsItsShare(string kind) => InEnglish(() =>
    {
        var run = PlayerPvp(kind, 0.0);
        run.Hit.Should().BeGreaterThan(0);
        run.Loss.Should().Be(run.Hit + Rider(kind, run.Hit), "{0} adds its PvE share of the hit (poison: one hit, C1)", kind);
        run.Shown.Should().Contain(PlayerLine(kind));
        if (kind.StartsWith("poison")) run.Shown.Should().NotContain("rounds", "in PvP weapon poison is one hit, not the 4 round poison");
        return 0;
    });

    [Fact]
    public void Coverage136_PvP_PlayerHit_HolyTakesTheAurelionBonus() => InEnglish(() =>
    {
        var run = PlayerPvp("holy", 0.0, aurelion: true);
        run.Loss.Should().Be(run.Hit + Rider("holy", run.Hit, aurelion: true), "the Aurelion attunement adds 25 percent to the holy proc");
        Rider("holy", run.Hit, aurelion: true).Should().BeGreaterThan(Rider("holy", run.Hit));
        return 0;
    });

    [Fact]
    public void Coverage136_PvP_PlayerHit_FrostSlowsTheDefender() => InEnglish(() =>
    {
        var run = PlayerPvp("frost", 0.0);
        run.Loss.Should().Be(run.Hit, "frost deals nothing");
        run.Defender.HasStatus(StatusEffect.Slow).Should().BeTrue();
        run.Defender.ActiveStatuses[StatusEffect.Slow].Should().Be(GameConfig.FrostEnchantDuration);
        run.Shown.Should().Contain("Defender is slowed! (Frostbite)");
        return 0;
    });

    [Fact]
    public void Coverage136_PvP_SlowHalvesTheSwingsOfTheFighterWhoAttacks()
    {
        // The finding behind frost in PvP: the duel's basic attack counts its swings through GetAttackCount,
        // which halves them for a slowed fighter. The NPC's own turn is one swing and does not read it.
        var hero = Armed(G.Hero(9000, 120, 12, CharacterClass.Assassin), "slow", 40, WeaponType.Sword, off: 35);
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, new Coverage136Random(0.99));
        var count = typeof(CombatEngine).GetMethod("GetAttackCount", F)!;
        int Swings() => (((int, bool))count.Invoke(engine, new object[] { hero })!).Item1;
        int before = Swings();
        before.Should().BeGreaterThan(1, "a dual wielder swings more than once");
        hero.ApplyStatus(StatusEffect.Slow, GameConfig.FrostEnchantDuration);
        Swings().Should().Be(Math.Max(1, before / 2));
    }

    [Theory]
    [InlineData("fire")]
    [InlineData("lightning")]
    [InlineData("poison")]
    [InlineData("poison37")]
    [InlineData("holy")]
    [InlineData("shadow")]
    public void Coverage136_PvP_NpcTurn_EachDamageProcAddsItsShare(string kind) => InEnglish(() =>
    {
        var run = NpcPvp(kind, 0.0);
        run.Loss.Should().Be(run.Hit + Rider(kind, run.Hit), "the NPC's {0} proc adds its PvE share of its hit", kind);
        run.Shown.Should().Contain("Npc");
        run.Shown.Should().Contain(NamedLine(kind));
        if (kind.StartsWith("poison")) run.Shown.Should().NotContain("rounds");
        return 0;
    });

    [Fact]
    public void Coverage136_PvP_NpcTurn_FrostSlowsTheHuman() => InEnglish(() =>
    {
        var run = NpcPvp("frost", 0.0);
        run.Loss.Should().Be(run.Hit);
        run.Human.HasStatus(StatusEffect.Slow).Should().BeTrue();
        run.Shown.Should().Contain("Frost spreads from Npc's strike! Human is slowed!");
        return 0;
    });

    [Fact]
    public void Coverage136_PvP_NpcTurn_HolyTakesTheAurelionBonus() => InEnglish(() =>
    {
        var run = NpcPvp("holy", 0.0, aurelion: true);
        run.Loss.Should().Be(run.Hit + Rider("holy", run.Hit, aurelion: true));
        return 0;
    });

    [Fact]
    public void Coverage136_PvP_NpcTurn_LifestealHealsItsPercent() => InEnglish(() =>
    {
        var run = NpcPvp("plain", 0.99, lifeSteal: 10);
        run.Loss.Should().Be(run.Hit);
        run.NpcGain.Should().Be(Math.Max(1, run.Hit * 10 / 100), "the NPC's gear lifesteal heals 10 percent of its hit");
        run.Shown.Should().Contain("Npc steals");
        var none = NpcPvp("plain", 0.99);
        none.NpcGain.Should().Be(0, "no lifesteal gear, no heal");
        return 0;
    });

    [Fact]
    public void Coverage136_PvP_PlayerHit_KeepsItsOwnLifestealOnce() => InEnglish(() =>
    {
        var a = Duelist("Attacker", CharacterAI.Human, Blade("pvp once", "plain", 10));
        a.HP = a.MaxHP / 2;
        var d = Duelist("Defender", CharacterAI.Computer);
        var p = Duel(a, d, new Coverage136Random(0.99));
        long a0 = a.HP;
        PlayerHit(p, a, d);
        long hit = Struck(p.Shown(), @"You strike Defender for (\d+) damage!");
        (a.HP - a0).Should().Be(Math.Max(1, hit * 10 / 100), "the player's hit heals once, by its own step");
        return 0;
    });

    [Fact]
    public void Coverage136_PvP_NoDrawForAWeaponWithoutTheEnchant() => InEnglish(() =>
    {
        int bare = PlayerPvp("plain", 0.0, weapon: null).Draws;
        var plain = PlayerPvp("plain", 0.0);
        plain.Draws.Should().Be(bare, "a weapon with no proc enchant draws nothing for procs");
        plain.Loss.Should().Be(plain.Hit, "and adds nothing");
        foreach (var kind in new[] { "fire", "frost", "lightning", "poison", "holy", "shadow" })
            PlayerPvp(kind, 0.99).Draws.Should().Be(bare + 1, "one draw, behind the {0} enchant", kind);
        int npcBare = NpcPvp("plain", 0.0, weapon: null).Draws;
        var npcPlain = NpcPvp("plain", 0.0);
        npcPlain.Draws.Should().Be(npcBare);
        npcPlain.Loss.Should().Be(npcPlain.Hit);
        foreach (var kind in new[] { "fire", "frost", "lightning", "poison", "holy", "shadow" })
            NpcPvp(kind, 0.99).Draws.Should().Be(npcBare + 1, "one draw, behind the NPC's {0} enchant", kind);
        return 0;
    });

    [Fact]
    public void Coverage136_PvP_LightningStun_ObeysThePvPControlRule() => InEnglish(() =>
    {
        // a fresh defender: the stun lands
        var fresh = PlayerPvp("lightning", 0.0);
        fresh.Defender.HasStatus(StatusEffect.Stunned).Should().BeTrue();
        fresh.Shown.Should().Contain("Defender is stunned!");

        // a defender already held: no new hold lands (the PvP control rule), the bolt's damage still does
        var a = Duelist("Attacker", CharacterAI.Human, Blade("pvp held", "lightning"));
        var d = Duelist("Defender", CharacterAI.Computer);
        var p = Duel(a, d, new Coverage136Random(0.0));
        p.Engine.TryApplyPvPControl(d, StatusEffect.Sleeping, 2).Should().BeTrue();
        PlayerHit(p, a, d);
        string shown = p.Shown();
        long hit = Struck(shown, @"You strike Defender for (\d+) damage!");
        (d.MaxHP - d.HP).Should().Be(hit + Rider("lightning", hit));
        d.HasStatus(StatusEffect.Stunned).Should().BeFalse("a held fighter takes no second hold");
        shown.Should().Contain("Defender shrugs it off.");
        shown.Should().Contain("Defender resists the stun!");
        return 0;
    });

    // ================================================================ (d) world boss

    private static Character Raider(int? mainHand, bool aurelion = false)
    {
        var c = Duelist("Raider", CharacterAI.Human, mainHand);
        if (aurelion) Attune(c);
        return c;
    }

    [Theory]
    [InlineData("fire")]
    [InlineData("lightning")]
    [InlineData("poison")]
    [InlineData("poison37")]
    [InlineData("holy")]
    [InlineData("shadow")]
    public void Coverage136_WorldBoss_EachDamageProcAddsItsShare(string kind) => InEnglish(() =>
    {
        var p = Raider(Blade("wb", kind));
        var rng = new Coverage136Random(0.0);
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        WorldBossSystem.WeaponHitRiders(p, 1000, "The Boss", rng, term).Should().Be(Rider(kind, 1000), "{0} adds its share to the pooled hit", kind);
        rng.Draws.Should().Be(1);
        term.StreamWriterInternal?.Flush();
        string shown = Plain(Encoding.UTF8.GetString(output.ToArray()));
        shown.Should().Contain(PlayerLine(kind));
        if (kind.StartsWith("poison")) shown.Should().NotContain("rounds", "on the world boss weapon poison is one hit (C2)");
        return 0;
    });

    [Fact]
    public void Coverage136_WorldBoss_HolyTakesTheAurelionBonus_FrostDoesNothing()
    {
        var holy = Raider(Blade("wb aurelion", "holy"), aurelion: true);
        WorldBossSystem.WeaponHitRiders(holy, 1000, "The Boss", new Coverage136Random(0.0), null).Should().Be(Rider("holy", 1000, aurelion: true));
        var frost = new Coverage136Random(0.0);
        WorldBossSystem.WeaponHitRiders(Raider(Blade("wb", "frost")), 1000, "The Boss", frost, null).Should().Be(0, "no frost slow on the world boss");
        frost.Draws.Should().Be(0);
    }

    [Fact]
    public void Coverage136_WorldBoss_NoDrawWithoutTheEnchant_NothingOnAMiss()
    {
        var plain = new Coverage136Random(0.0);
        WorldBossSystem.WeaponHitRiders(Raider(Blade("wb", "plain")), 1000, "The Boss", plain, null).Should().Be(0);
        plain.Draws.Should().Be(0, "a weapon with no proc enchant draws nothing");
        var bare = new Coverage136Random(0.0);
        WorldBossSystem.WeaponHitRiders(Raider(null), 1000, "The Boss", bare, null).Should().Be(0);
        bare.Draws.Should().Be(0);
        var miss = new Coverage136Random(0.0);
        WorldBossSystem.WeaponHitRiders(Raider(Blade("wb", "fire")), 0, "The Boss", miss, null).Should().Be(0);
        miss.Draws.Should().Be(0, "a miss draws nothing");
    }

    [Fact]
    public void Coverage136_WorldBoss_LifestealHealsItsPercent() => InEnglish(() =>
    {
        var p = Raider(Blade("wb leech", "plain", 10));
        p.HP = p.MaxHP / 2;
        long hp0 = p.HP;
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        WorldBossSystem.WeaponHitRiders(p, 1000, "The Boss", new Coverage136Random(0.0), term).Should().Be(0, "lifesteal adds no damage");
        (p.HP - hp0).Should().Be(100, "10 percent of the hit");
        term.StreamWriterInternal?.Flush();
        Plain(Encoding.UTF8.GetString(output.ToArray())).Should().Contain("Your weapon drains 100 life!");
        var none = Raider(Blade("wb", "plain"));
        none.HP = none.MaxHP / 2;
        long n0 = none.HP;
        WorldBossSystem.WeaponHitRiders(none, 1000, "The Boss", new Coverage136Random(0.0), null);
        none.HP.Should().Be(n0);
        return 0;
    });

    private static long BossAction(string input, int? weapon) => InEnglish(() =>
    {
        var p = Raider(weapon);
        var def = WorldBossDatabase.GetBossById("lich_king_vareth")!;
        var data = new WorldBossRuntimeData { DefinitionId = def.Id, CurrentPhase = 1, ScaledLevel = 30, ScaledStrength = 100, ScaledDefence = 0 };
        var term = new TerminalEmulator(new MemoryStream(), new MemoryStream());
        var m = typeof(WorldBossSystem).GetMethod("ProcessPlayerAction", F)!;
        return ((Task<long>)m.Invoke(new WorldBossSystem(), new object[] { input, p, term, def, data, new Coverage136Random(0.0), new WorldBossCombatState() })!).GetAwaiter().GetResult();
    });

    [Theory]
    [InlineData("A")]
    [InlineData("P")]
    [InlineData("E")]
    [InlineData("X")]   // any other key is the standard attack
    public void Coverage136_WorldBoss_TheWeaponAttacksAddTheProcsToThePooledHit(string input)
    {
        long plain = BossAction(input, Blade("wb act", "plain"));
        plain.Should().BeGreaterThan(0);
        BossAction(input, Blade("wb act", "fire")).Should().Be(plain + Rider("fire", plain), "the {0} attack's fire proc adds to the pooled hit", input);
    }

    // ================================================================ (a) companions

    private static Monster Brute(long hp) => new Monster
    {
        Name = "Wolf", TierName = "Wolf", Level = 10, HP = hp, MaxHP = Math.Max(hp, 1_000_000), Strength = 400, WeapPow = 50,
        Defence = 0, Experience = 1, Gold = 0, IsActive = true, CanSpeak = false,
    };

    private static (long MonsterLoss, long MateLoss, CombatResult Result, string Shown) CompanionHit(int thorns, long monsterHp = 1_000_000)
    {
        var mate = G.Mate("Ally One", 100_000, 30, 60);
        mate.Defence = mate.BaseDefence = 0;
        if (thorns > 0) mate.EquippedItems[EquipmentSlot.Body] = Item($"C136 thorns {thorns}", EquipmentSlot.Body, e => e.Thorns = thorns);
        var foe = Brute(monsterHp);
        var rig = G.NewRig(G.Hero(), new Coverage136Random(0.99), new List<Character> { mate }, foe);
        long m0 = foe.HP, c0 = mate.HP;
        ((Task)typeof(CombatEngine).GetMethod("MonsterAttacksCompanion", F)!
            .Invoke(rig.Engine, new object?[] { foe, mate, rig.Result, rig.Monsters })!).GetAwaiter().GetResult();
        return (m0 - foe.HP, c0 - mate.HP, rig.Result, Plain(rig.Shown()));
    }

    [Fact]
    public void Coverage136_Companion_ThornsGearReflectsItsShare() => InEnglish(() =>
    {
        var none = CompanionHit(0);
        none.MateLoss.Should().BeGreaterThan(10, "the basic attack landed");
        none.MonsterLoss.Should().Be(0, "no thorns gear, nothing reflected");
        var with = CompanionHit(10);
        with.MateLoss.Should().Be(none.MateLoss);
        with.MonsterLoss.Should().Be(Math.Max(1, with.MateLoss * 10 / 100), "10 percent of the hit the companion took, as the player's thorns");
        with.Shown.Should().Contain($"Thorns reflect {with.MonsterLoss} damage back at");
        return 0;
    });

    [Fact]
    public void Coverage136_Companion_AThornsReflectionThatKills_CountsTheKill() => InEnglish(() =>
    {
        var run = CompanionHit(10, monsterHp: 1);
        run.MonsterLoss.Should().Be(1);
        run.Result.DefeatedMonsters.Should().ContainSingle(m => m.Name == "Wolf" && m.HP == 0, "the kill is counted");
        run.Shown.Should().Contain("is destroyed by the reflected damage!");
        return 0;
    });

    [Fact]
    public void Coverage136_Companion_GearRegenerationIsTheShareRuleOf135()
    {
        // (a) confirms, changes nothing: the round end calls the 1.3.5 share rule for each member
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        src.Should().Contain("ApplyMemberGearRegenShare(tm);");
    }

    // ================================================================ (b) Boss Slayer

    private static int Slayer(string tag) => Item($"C136 slayer {tag}", EquipmentSlot.Body, e => e.HasBossSlayer = true);

    private static Monster Target(string kind)
    {
        var m = G.Dummy();
        if (kind == "boss") m.IsBoss = true;
        if (kind == "mini") m.IsMiniBoss = true;
        return m;
    }

    private static G.Run Strike(CombatActionType type, string ability, bool slayer, string target)
    {
        var hero = G.Hero();
        if (slayer) hero.EquippedItems[EquipmentSlot.Body] = Slayer($"{type} {ability} {target}");
        return G.Repeat(hero, G.A(type, ability), times: 20, seed: 136, foe: Target(target));
    }

    [Theory]
    [InlineData(CombatActionType.PowerAttack, "", "boss")]
    [InlineData(CombatActionType.PowerAttack, "", "mini")]
    [InlineData(CombatActionType.UseAbility, "power_strike", "boss")]
    [InlineData(CombatActionType.UseAbility, "power_strike", "mini")]
    public void Coverage136_BossSlayer_AddsTenPercentAgainstABossOrMiniBoss(CombatActionType type, string ability, string target) => InEnglish(() =>
    {
        var without = Strike(type, ability, false, target);
        var with = Strike(type, ability, true, target);
        without.Error.Should().BeNull();
        with.Error.Should().BeNull();
        without.Damage.Should().BeGreaterThan(0);
        with.Draws.Should().Equal(without.Draws, "Boss Slayer adds no draw, so the rolls match");
        ((double)with.Damage).Should().BeInRange(without.Damage * 1.09, without.Damage * 1.11, "10 percent more on each hit, before the defence of 0");
        return 0;
    });

    [Theory]
    [InlineData(CombatActionType.PowerAttack, "")]
    [InlineData(CombatActionType.UseAbility, "power_strike")]
    public void Coverage136_BossSlayer_NothingAgainstAPlainMonster(CombatActionType type, string ability) => InEnglish(() =>
    {
        var without = Strike(type, ability, false, "plain");
        Strike(type, ability, true, "plain").Damage.Should().Be(without.Damage);
        return 0;
    });

    [Theory]
    [InlineData("boss", true)]
    [InlineData("mini", true)]
    [InlineData("plain", false)]
    public void Coverage136_BossSlayer_OnTheAbilityHandlerHelper(string target, bool adds)
    {
        long Dealt(bool slayer)
        {
            var hero = G.Hero();
            if (slayer) hero.EquippedItems[EquipmentSlot.Body] = Slayer($"handler {target}");
            var rig = G.NewRig(hero, new Coverage136Random(0.99), null, Target(target));
            var handler = typeof(CombatEngine).GetMethod("ApplyHandlerAbilityDamage", F)!;
            var reap = new ClassAbilityResult { SpecialEffect = "execute", Damage = 1000 };
            return (long)handler.Invoke(rig.Engine, new object[] { hero, rig.Monsters[0], 1000L, rig.Result, reap })!;
        }
        long without = Dealt(false);
        without.Should().BeGreaterThan(0);
        Dealt(true).Should().Be(adds ? without + (long)(without * 0.10) : without);
    }

    [Fact]
    public void Coverage136_BossSlayer_NotOnASpell() => InEnglish(() =>
    {
        // Every Magic Missile cast against a boss takes exactly the damage the spell rolled (after the difficulty
        // multiplier), Boss Slayer or not; the spell's own roll comes from the shared random, so each cast is
        // checked against its own line.
        var hero = Armed(G.Hero(cls: CharacterClass.Magician), "spell", 30, WeaponType.Staff);
        hero.Intelligence = hero.BaseIntelligence = 300;
        hero.Mana = hero.MaxMana = 1_000_000;
        hero.EquippedItems[EquipmentSlot.Body] = Slayer("spell");
        var rig = G.NewRig(hero, new G.RecordingRandom(136), null, Target("boss"));
        int checkedCasts = 0;
        for (int i = 0; i < 40; i++)
        {
            hero.Mana = hero.MaxMana;
            string before = Plain(rig.Shown());
            long hp0 = rig.Monsters[0].HP;
            G.Act(rig, new CombatAction { Type = CombatActionType.CastSpell, SpellIndex = 1, TargetIndex = 0 });
            string shown = Plain(rig.Shown()).Substring(before.Length);
            var m = Regex.Match(shown, @"Magic missiles strike .+? for (\d+) damage!");
            if (!m.Success) continue;
            long rolled = long.Parse(m.Groups[1].Value);
            (hp0 - rig.Monsters[0].HP).Should().Be(DifficultySystem.ApplyPlayerDamageMultiplier(rolled), "a spell takes no Boss Slayer bonus");
            checkedCasts++;
        }
        checkedCasts.Should().BeGreaterThan(10, "enough casts landed to check");
        return 0;
    });

    [Fact]
    public void Coverage136_BossSlayer_TheHelper()
    {
        var hero = G.Hero();
        hero.EquippedItems[EquipmentSlot.Body] = Slayer("helper");
        CombatEngine.ApplyBossSlayer(hero, Target("boss"), 1000).Should().Be(1100);
        CombatEngine.ApplyBossSlayer(hero, Target("mini"), 1000).Should().Be(1100);
        CombatEngine.ApplyBossSlayer(hero, Target("plain"), 1000).Should().Be(1000);
        CombatEngine.ApplyBossSlayer(G.Hero(), Target("boss"), 1000).Should().Be(1000, "no Boss Slayer item");
        CombatEngine.ApplyBossSlayer(hero, Target("boss"), 0).Should().Be(0);
    }

    // ================================================================ text

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Coverage136_TheNewPoisonRows_FitSeventyNineColumns(string lang) => InLanguage(lang, () =>
    {
        var rows = new List<string>();
        var a = Duelist("Bartholomew Longname", CharacterAI.Human, Blade("w79 a", "poison37"));
        var d = Duelist("Defender", CharacterAI.Computer);
        var p = Duel(a, d, new Coverage136Random(0.0));
        PlayerHit(p, a, d);
        var npc = Duelist("Bartholomew Longname", CharacterAI.Computer, Blade("w79 n", "poison37"));
        npc.HP = npc.MaxHP / 2;
        NpcTurn(p, npc, a);
        rows.AddRange(p.Shown().Split('\n'));
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        WorldBossSystem.WeaponHitRiders(Raider(Blade("w79 wb", "poison")), 99_999_999, "Bartholomew the Endless Longname", new Coverage136Random(0.0), term);
        term.StreamWriterInternal?.Flush();
        rows.AddRange(Plain(Encoding.UTF8.GetString(output.ToArray())).Split('\n'));
        rows.Should().Contain(r => r.Contains(Loc.Get("combat.enchant_venom_hit", 37)), "the player's poison row is shown");
        rows.Should().Contain(r => r.Contains(Loc.Get("combat.enchant_venom_hit_tm", "Bartholomew Longname", 37)), "the NPC's poison row is shown");
        rows.Where(r => UIHelper.VisibleLength(r) > 79).Should().BeEmpty();
        foreach (var key in new[] { "combat.enchant_venom_hit", "combat.enchant_venom_hit_tm" })
        {
            string row = key.EndsWith("_tm") ? Loc.Get(key, "Bartholomew the Endless Longname", 99_999_999) : Loc.Get(key, 99_999_999);
            foreach (var r in UIHelper.MessageRows("  " + row)) UIHelper.VisibleLength(r).Should().BeLessThanOrEqualTo(79);
        }
        return 0;
    });

    [Fact]
    public void Coverage136_TheNewKeys_AreTranslatedInEveryLanguage()
    {
        foreach (var key in new[] { "combat.enchant_venom_hit", "combat.enchant_venom_hit_tm" })
        {
            string en = Loc.GetIn("en", key);
            en.Should().NotBe(key);
            foreach (var lang in new[] { "es", "fr", "hu", "it" })
            {
                string t = Loc.GetIn(lang, key);
                t.Should().NotBe(key, "{0} has {1}", lang, key);
                t.Should().NotBe(en, "{0} has its own words for {1}", lang, key);
            }
        }
    }

    [Fact]
    public void Coverage136_TheNewPoisonKeys_NameNoWeapon()
    {
        // 1.3.6: E2 took the weapon tags off the venom lines; the one hit lines carry none either
        var tags = new[] { "Venomstrike", "Venom)", "Golpe Venenoso", "Frappe Venimeuse", "Méregcsapás", "Colpo Velenoso", "(Veneno", "(Venin", "(Méreg", "(Veleno" };
        foreach (var key in new[] { "combat.enchant_venom_hit", "combat.enchant_venom_hit_tm" })
            foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            {
                string value = Loc.GetIn(lang, key);
                value.Should().NotBe(key, "{0} has {1}", lang, key);
                value.Should().NotContain("(", "{0} {1} names no weapon: {2}", lang, key, value);
                foreach (var tag in tags) value.Should().NotContain(tag, "{0} {1} names no weapon: {2}", lang, key, value);
            }
    }

    [Fact]
    public void Coverage136_ARunLeavesTheItemDatabaseAsItFoundIt()
    {
        // Runs every other test of this class, each on its own instance disposed after it, as xUnit runs them. Other test
        // classes may run at the same time and register their own items, so the check is on this class's "C136 " names.
        var before = EquipmentDatabase.GetAll().Select(e => e.Id).ToHashSet();
        int registered = 0;
        foreach (var m in typeof(Coverage136Tests).GetMethods().Where(m => m.Name.StartsWith("Coverage136_") && m.Name != nameof(Coverage136_ARunLeavesTheItemDatabaseAsItFoundIt)))
        {
            var cases = m.GetCustomAttributes<InlineDataAttribute>().Select(a => a.GetData(m).First()).ToList();
            if (cases.Count == 0) cases.Add(Array.Empty<object>());
            foreach (var args in cases)
            {
                var test = new Coverage136Tests();
                try { m.Invoke(test, args); }
                finally { lock (Registered) registered += Registered.Count; test.Dispose(); }
            }
        }
        registered.Should().BeGreaterThan(50, "the run registers its gear, or this proves nothing");
        EquipmentDatabase.GetAll().Where(e => !before.Contains(e.Id) && e.Name.StartsWith("C136 ")).Select(e => $"{e.Name} ({e.Name.Length})")
            .Should().BeEmpty("each test removes the items it registered");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        dir.Should().NotBeNull();
        return dir!.FullName;
    }
}
