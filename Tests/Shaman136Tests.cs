using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 G10 (CP12, Q10, Q18). Rule 1: the Mystic Shaman's weapon enchant riders and the Ancestral Guidance heal
/// run on every live swing of a party member, the player's and the NPC teammate's, from one helper, at the
/// swinger's own enchant, and the heal goes to the swinger and the rest of the party once each; a teammate's
/// enchant counts down its own rounds. Rule 3: Guidance heals 25 percent of the whole hit, the swing's HP removed
/// plus the enchant's extra. Rule 2: the Shaman's Lightning Bolt, Chain Lightning and Searing Totem scale with
/// Intelligence (4 percent a point above 10) and Dexterity (1.5 percent), not Strength; Desperate Strike keeps
/// Strength. In memory only.
/// </summary>
[Collection("SharedGameSingletons")]
public class Shaman136Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int WeaponPower = 100;
    private const int MatePower = 37;     // the teammate's rider: 100 x 37 / 100 = 37
    private const int LeaderPower = 90;   // the leader's own enchant, never the teammate's

    // the swing reads damage with fixed draws, so the to-hit roll is off
    private readonly bool _savedHitRoll = GameConfig.PlayerHitRollEnabled;
    public Shaman136Tests() { GameConfig.PlayerHitRollEnabled = false; }
    public void Dispose() { GameConfig.PlayerHitRollEnabled = _savedHitRoll; }

    // ---------- harness ----------

    private sealed class FixedRandom : Random
    {
        public override int Next() => 0;
        public override int Next(int maxValue) => maxValue == 100 ? 99 : (maxValue <= 0 ? 0 : maxValue - 1);
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0.5;
        protected override double Sample() => 0.5;
    }

    private sealed class Recorder : ICombatObserver
    {
        public readonly List<CombatEvent> Events = new();
        public void OnFightStart(CombatFightStart start) { }
        public void OnEvent(CombatEvent e) { if (!e.IsUse && !e.IsRoll && e.Amount > 0) Events.Add(e); }
        public void OnFightEnd(long playerHpEnd) { }
        public List<long> Hits(CombatSide actor) =>
            Events.Where(e => e.Actor == actor && e.Target == CombatSide.Monster).Select(e => e.Amount).ToList();
    }

    // one dynamic item, named in 13 characters, registered once
    private static readonly Lazy<int> AxeId = new(() => EquipmentDatabase.RegisterDynamic(new Equipment
    {
        Name = "Shaman136 Axe", Slot = EquipmentSlot.MainHand, WeaponType = WeaponType.Axe,
        Handedness = WeaponHandedness.OneHanded, WeaponPower = WeaponPower, MinLevel = 1, Rarity = EquipmentRarity.Common,
    }));

    private static Character Member(string name, CharacterClass cls, CharacterAI ai)
    {
        var c = new Character
        {
            Name1 = name, Name2 = name, AI = ai, Class = cls, Race = CharacterRace.Troll, Sex = CharacterSex.Male,
            Age = 25, Level = 60, Strength = 120, BaseStrength = 120, Dexterity = 20, BaseDexterity = 20,
            Agility = 10, BaseAgility = 10, Constitution = 30, BaseConstitution = 30, Intelligence = 40, BaseIntelligence = 40,
            Wisdom = 20, BaseWisdom = 20, Charisma = 20, BaseCharisma = 20, Stamina = 100, BaseStamina = 100, Mental = 80,
            CombatSpeed = CombatSpeed.Instant,
        };
        c.EquippedItems[EquipmentSlot.MainHand] = AxeId.Value;
        c.RecalculateStats();
        c.MaxHP = 100_000; c.HP = c.MaxHP;
        c.MaxMana = 200; c.Mana = 0; c.ManaPotions = 0; c.Healing = 0;
        return c;
    }

    private static Monster Dummy() => new Monster
    {
        Name = "Training Dummy", Level = 60, HP = 1_000_000_000L, MaxHP = 1_000_000_000L, Strength = 1, Defence = 0,
        ArmPow = 0, Experience = 1, Gold = 0, IsActive = true,
    };

    private sealed class Rig
    {
        public CombatEngine Engine = null!;
        public Character Leader = null!;
        public Character Mate = null!;
        public CombatResult Result = null!;
        public Recorder Rec = new();
        public Monster Target = null!;
    }

    private static Rig NewRig()
    {
        var rig = new Rig
        {
            Leader = Member("Leader", CharacterClass.MysticShaman, CharacterAI.Human),
            Mate = Member("Mate", CharacterClass.MysticShaman, CharacterAI.Computer),
            Target = Dummy(),
        };
        // the mate swings: no ability, no spell, no potion
        rig.Leader.TeammateDisabledAbilities[rig.Mate.GetSkillToggleKey()] =
            ClassAbilitySystem.GetAvailableAbilities(rig.Mate).Select(a => a.Id).ToList();
        var mates = new List<Character> { rig.Mate };
        rig.Engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(rig.Engine, new FixedRandom());
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(rig.Engine, rig.Leader);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(rig.Engine, mates);
        rig.Result = new CombatResult { Player = rig.Leader, Teammates = mates };
        typeof(CombatEngine).GetMethod("BeginFightEvents", F)!.Invoke(rig.Engine, new object[] { rig.Result });
        rig.Engine.Observer = rig.Rec;
        return rig;
    }

    private static void Enchant(Character c, int type, int power, int rounds = 5)
    {
        c.ShamanEnchantType = type; c.ShamanEnchantPower = power; c.ShamanEnchantRounds = rounds;
    }

    private static void Guidance(Character c) { c.ActiveTotemType = 6; c.ActiveTotemRounds = 4; c.ActiveTotemPower = 25; }

    private static async Task MateTurn(Rig rig) =>
        await (Task)typeof(CombatEngine).GetMethod("ProcessTeammateActionMultiMonster", F)!
            .Invoke(rig.Engine, new object[] { rig.Mate, new List<Monster> { rig.Target }, rig.Result })!;

    private static void PlayerOnHit(Rig rig, long damage) =>
        typeof(CombatEngine).GetMethod("ApplyPlayerSwingOnHitEffects", F)!
            .Invoke(rig.Engine, new object[] { rig.Leader, rig.Target, damage, false, rig.Result });

    private static void RoundEnd(Rig rig) =>
        typeof(CombatEngine).GetMethod("RegeneratePartyAtRoundEnd", F)!.Invoke(rig.Engine, new object[] { rig.Leader });

    // ---------- rule 1: the riders on a teammate's swing ----------

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public async Task Shaman136_R1_ATeammateShaman_SetsOffItsOwnEnchantRider_OnItsOwnSwing(int type)
    {
        var rig = NewRig();
        Enchant(rig.Mate, type, MatePower);
        Enchant(rig.Leader, type, LeaderPower);
        long leaderMana = rig.Leader.Mana;

        await MateTurn(rig);

        var hits = rig.Rec.Hits(CombatSide.Team);
        hits.Should().NotBeEmpty("the teammate swings");
        long rider = WeaponPower * MatePower / 100;
        hits.Count(h => h == rider).Should().BeGreaterThan(0, "the teammate's rider fires at its own power");
        hits.Should().NotContain(WeaponPower * LeaderPower / 100, "never at the leader's power");
        rig.Rec.Hits(CombatSide.Player).Should().BeEmpty("the leader did not swing");
        rig.Leader.Mana.Should().Be(leaderMana);
        if (type == 2) rig.Target.IsSlowed.Should().BeTrue("Frostbrand slows the target");
        if (type == 4) rig.Mate.Mana.Should().BeGreaterThan(0, "Stormstrike restores the swinger's own mana");
    }

    [Fact]
    public async Task Shaman136_R1_ATeammateWithNoEnchant_HasNoRider()
    {
        var rig = NewRig();
        await MateTurn(rig);
        var hits = rig.Rec.Hits(CombatSide.Team);
        hits.Should().NotBeEmpty();
        hits.Should().NotContain(WeaponPower * MatePower / 100);
        rig.Target.IsSlowed.Should().BeFalse();
    }

    [Fact]
    public async Task Shaman136_R1_ATeammateShamanWithGuidance_HealsItselfOnce_AndThePlayer()
    {
        var rig = NewRig();
        Guidance(rig.Mate);
        rig.Mate.HP = rig.Mate.MaxHP - 50_000;
        rig.Leader.HP = rig.Leader.MaxHP - 50_000;
        long mateHp = rig.Mate.HP, leaderHp = rig.Leader.HP;

        await MateTurn(rig);

        var hits = rig.Rec.Hits(CombatSide.Team);
        hits.Should().NotBeEmpty();
        long expected = hits.Sum(h => (long)(h * 25 / 100.0));
        expected.Should().BeGreaterThan(0);
        (rig.Mate.HP - mateHp).Should().Be(expected, "the Shaman who dealt the hit is healed once");
        (rig.Leader.HP - leaderHp).Should().Be(expected, "an NPC Shaman's Guidance reaches the player");
    }

    [Fact]
    public void Shaman136_R1_ThePlayersSwing_IsUnchanged()
    {
        var rig = NewRig();
        Enchant(rig.Leader, 1, LeaderPower);
        PlayerOnHit(rig, 1000);
        rig.Rec.Hits(CombatSide.Player).Should().Equal(new long[] { WeaponPower * LeaderPower / 100 },
            "the player's rider is the player's own enchant");
        rig.Rec.Hits(CombatSide.Team).Should().BeEmpty();
        rig.Leader.ShamanEnchantRounds.Should().Be(5, "the player's rounds count down in the player's own round end");
    }

    [Fact]
    public async Task Shaman136_R1_ATeammatesEnchant_LastsItsOwnRounds()
    {
        var rig = NewRig();
        int rounds = GameConfig.ShamanEnchantDuration;
        Enchant(rig.Mate, 1, MatePower, rounds);
        Enchant(rig.Leader, 1, LeaderPower, rounds);
        for (int r = 1; r < rounds; r++) RoundEnd(rig);
        rig.Mate.ShamanEnchantRounds.Should().Be(1);
        rig.Mate.ShamanEnchantType.Should().Be(1);
        await MateTurn(rig);
        rig.Rec.Hits(CombatSide.Team).Should().Contain(WeaponPower * MatePower / 100, "the last round still fires");

        RoundEnd(rig);
        rig.Mate.ShamanEnchantRounds.Should().Be(0);
        rig.Mate.ShamanEnchantType.Should().Be(0, "the enchant ends at its own rounds");
        rig.Mate.ShamanEnchantPower.Should().Be(0);
        rig.Leader.ShamanEnchantRounds.Should().Be(rounds, "the party step does not touch the leader's own enchant");
        rig.Rec.Events.Clear();
        await MateTurn(rig);
        rig.Rec.Hits(CombatSide.Team).Should().NotBeEmpty();
        rig.Rec.Hits(CombatSide.Team).Should().NotContain(WeaponPower * MatePower / 100, "no rider after the enchant ends");
    }

    [Fact]
    public void Shaman136_R1_OneHelper_BothSwingPathsCallIt_TheDeadPathKeepsItsCopy()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Systems", "CombatEngine.cs"));
        int Count(string what) { int n = 0, i = 0; while ((i = src.IndexOf(what, i, StringComparison.Ordinal)) >= 0) { n++; i += what.Length; } return n; }
        Count("ApplyShamanSwingRiders(player, target, damage, isOffHandAttack, result);").Should().Be(1, "the player's swing");
        Count("ApplyShamanSwingRiders(teammate, target, tmDealt, isOffHandAttack, result);").Should().Be(1, "the teammate's swing");
        Count("combat.shaman_enchant_fire_hit").Should().Be(2, "the helper and the dead single monster path's own copy");
    }

    // ---------- rule 3: Guidance heals 25 percent of the whole hit ----------

    [Fact]
    public void Shaman136_R3_ThePlayersGuidance_HealsAQuarterOfTheSwingPlusTheEnchant()
    {
        var rig = NewRig();
        Guidance(rig.Leader);
        Enchant(rig.Leader, 1, LeaderPower);
        rig.Leader.HP = rig.Leader.MaxHP - 50_000;
        rig.Mate.HP = rig.Mate.MaxHP - 50_000;
        long leaderHp = rig.Leader.HP, mateHp = rig.Mate.HP;

        PlayerOnHit(rig, 1000);

        long extra = WeaponPower * LeaderPower / 100;   // 90
        long expected = (long)((1000 + extra) * 25 / 100.0);   // 272, not the 22 of the enchant's extra alone
        (rig.Leader.HP - leaderHp).Should().Be(expected, "a quarter of the whole hit, the enchant's extra included");
        (rig.Mate.HP - mateHp).Should().Be(expected, "the same heal reaches every other member");
    }

    [Fact]
    public void Shaman136_R3_WithNoEnchant_GuidanceHealsAQuarterOfTheSwing_AsBefore()
    {
        var rig = NewRig();
        Guidance(rig.Leader);
        rig.Leader.HP = rig.Leader.MaxHP - 50_000;
        long leaderHp = rig.Leader.HP;
        PlayerOnHit(rig, 1000);
        (rig.Leader.HP - leaderHp).Should().Be(250);
    }

    [Fact]
    public async Task Shaman136_R3_ATeammatesGuidance_HealsAQuarterOfEachWholeHit()
    {
        var rig = NewRig();
        Guidance(rig.Mate);
        Enchant(rig.Mate, 3, MatePower);
        rig.Mate.HP = rig.Mate.MaxHP - 50_000;
        long mateHp = rig.Mate.HP;

        await MateTurn(rig);

        long rider = WeaponPower * MatePower / 100;
        var swings = rig.Rec.Hits(CombatSide.Team).Where(h => h != rider).ToList();
        swings.Should().NotBeEmpty();
        rig.Rec.Hits(CombatSide.Team).Count(h => h == rider).Should().Be(swings.Count, "one rider a landed swing");
        long expected = swings.Sum(b => (long)((b + rider) * 25 / 100.0));
        (rig.Mate.HP - mateHp).Should().Be(expected, "each swing heals a quarter of its HP removed plus the enchant's extra");
    }

    // ---------- rule 2: the Shaman's own attacks scale with Intelligence ----------

    /// <summary>A level 50 caster with no weapon: Strength, Intelligence and Dexterity as given.</summary>
    private static Character Caster(CharacterClass cls, int str, int intel, int dex = 30) => new Character
    {
        Name1 = "Caster", Name2 = "Caster", Class = cls, Race = CharacterRace.Troll, Level = 50,
        Strength = str, Intelligence = intel, Dexterity = dex, Constitution = 30, Wisdom = 20, Charisma = 20,
        HP = 100_000, MaxHP = 100_000, CombatSpeed = CombatSpeed.Instant,
    };

    private static long Use(Character c, string id) => ClassAbilitySystem.UseAbility(c, id, new FixedRandom()).Damage;

    /// <summary>The template's centre value: base x level scale x stat scale, the 10 percent spread at its middle.</summary>
    private static long Centre(string id, int primary, int dex)
    {
        var a = ClassAbilitySystem.GetAbility(id)!;
        double levelScale = 1.0 + (50 * 0.03);
        double statScale = 1.0;
        statScale += Math.Max(0, (primary - 10) * 0.04);
        statScale += Math.Max(0, (dex - 10) * 0.015);
        statScale = Math.Min(statScale, 4.5);
        double scaled = (a.BaseDamage + 0 * 0.25) * (levelScale * statScale);
        return (long)(scaled * (0.9 + 0.5 * 0.2));
    }

    [Theory]
    [InlineData("lightning_bolt")]
    [InlineData("chain_lightning")]
    [InlineData("searing_totem")]
    public void Shaman136_R2_TheShamansOwnAttacks_GrowWithIntelligence_NotStrength(string id)
    {
        var plain = Caster(CharacterClass.MysticShaman, 10, 10);
        var wise = Caster(CharacterClass.MysticShaman, 10, 80);
        var strong = Caster(CharacterClass.MysticShaman, 80, 10);
        Use(wise, id).Should().BeGreaterThan(Use(plain, id), "Intelligence raises it");
        Use(strong, id).Should().Be(Use(plain, id), "Strength does not");
        Use(wise, id).Should().Be(Centre(id, 80, 30), "4 percent a point of Intelligence above 10 and the Dexterity term");
        Use(Caster(CharacterClass.MysticShaman, 10, 10, dex: 70), id).Should().BeGreaterThan(Use(plain, id), "the Dexterity term is kept");
    }

    [Fact]
    public void Shaman136_R2_DesperateStrike_KeepsStrength_ForAShamanAndAWarrior()
    {
        foreach (var cls in new[] { CharacterClass.MysticShaman, CharacterClass.Warrior })
        {
            var plain = Caster(cls, 10, 10);
            Use(Caster(cls, 80, 10), "desperate_strike").Should().Be(Centre("desperate_strike", 80, 30), cls.ToString());
            Use(Caster(cls, 10, 80), "desperate_strike").Should().Be(Use(plain, "desperate_strike"), $"{cls}: Intelligence does not raise a melee strike");
        }
        ClassAbilitySystem.ScalesWithShamanIntelligence(ClassAbilitySystem.GetAbility("desperate_strike")!).Should().BeFalse();
    }

    [Fact]
    public void Shaman136_R2_AStrengthClass_IsUnchanged()
    {
        var plain = Caster(CharacterClass.Warrior, 10, 10);
        Use(Caster(CharacterClass.Warrior, 80, 10), "desperate_strike").Should().BeGreaterThan(Use(plain, "desperate_strike"));
        Use(Caster(CharacterClass.Warrior, 10, 80), "desperate_strike").Should().Be(Use(plain, "desperate_strike"));
    }

    [Fact]
    public void Shaman136_R2_OnlyTheThreeShamanAttacks_TakeTheIntelligenceBranch()
    {
        var shamanAttacks = ClassAbilitySystem.GetAvailableAbilities(new Character { Class = CharacterClass.MysticShaman, Level = 100 })
            .Where(ClassAbilitySystem.ScalesWithShamanIntelligence).Select(a => a.Id).OrderBy(x => x).ToList();
        shamanAttacks.Should().Equal("chain_lightning", "lightning_bolt", "searing_totem");
    }

    private static Rig SearingRig(Character caster)
    {
        var rig = NewRig();
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(rig.Engine, caster);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(rig.Engine, new List<Character>());
        rig.Result = new CombatResult { Player = caster, Teammates = new List<Character>() };
        typeof(CombatEngine).GetMethod("BeginFightEvents", F)!.Invoke(rig.Engine, new object[] { rig.Result });
        rig.Engine.Observer = rig.Rec;
        return rig;
    }

    /// <summary>Searing Totem cast through the live ability effects, then one totem pulse: the hit, the power, the tick.</summary>
    private static (long hit, int power, long tick) Searing(Character caster)
    {
        var rig = SearingRig(caster);
        var monsters = new List<Monster> { rig.Target };
        var use = ClassAbilitySystem.UseAbility(caster, "searing_totem", new FixedRandom());
        long before = rig.Target.HP;
        ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
            .Invoke(rig.Engine, new object?[] { caster, rig.Target, monsters, use, rig.Result })!).GetAwaiter().GetResult();
        long hit = before - rig.Target.HP;
        caster.ActiveTotemType.Should().Be(3);
        before = rig.Target.HP;
        typeof(CombatEngine).GetMethod("ProcessShamanTotemEffects", F)!.Invoke(rig.Engine, new object?[] { caster, monsters, rig.Result });
        return (hit, caster.ActiveTotemPower, before - rig.Target.HP);
    }

    [Fact]
    public void Shaman136_R2_SearingTotem_HitAndTick_GrowWithIntelligence_NotStrength()
    {
        var plain = Searing(Caster(CharacterClass.MysticShaman, 10, 10));
        var wise = Searing(Caster(CharacterClass.MysticShaman, 10, 80));
        var strong = Searing(Caster(CharacterClass.MysticShaman, 80, 10));
        plain.power.Should().Be((int)Centre("searing_totem", 10, 30));
        wise.power.Should().Be((int)Centre("searing_totem", 80, 30), "the totem's power is the Intelligence scaled damage");
        wise.hit.Should().BeGreaterThan(plain.hit);
        wise.tick.Should().BeGreaterThan(plain.tick);
        strong.power.Should().Be(plain.power, "Strength does not raise it");
        strong.hit.Should().Be(plain.hit);
        strong.tick.Should().Be(plain.tick);
    }

    private static string RepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !File.Exists(Path.Combine(dir, "UsurperReborn.sln"))) dir = Path.GetDirectoryName(dir);
        return dir!;
    }
}
