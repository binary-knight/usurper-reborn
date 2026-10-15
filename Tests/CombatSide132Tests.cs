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
/// 1.3.2 combat side. (1) Dispel Evil on an enraged boss takes the enrage's Strength, Defence and extra
/// attacks back, and the enrage does not come again that fight. (2) The Outskirts Workshop (+20 percent,
/// 10 fights) and the Outskirts Arena (the settlement DamageBonus buff) apply on the live player swing
/// (ComputePlayerSwingDamageRolled), not only on the dead single-monster path. In memory only.
/// </summary>
[Collection("SharedGameSingletons")]
public class CombatSide132Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;

    // the swing rows read damage with fixed draws, so the to-hit roll is off for them
    private readonly bool _savedHitRoll = GameConfig.PlayerHitRollEnabled;
    public CombatSide132Tests() { GameConfig.PlayerHitRollEnabled = false; }
    public void Dispose() { GameConfig.PlayerHitRollEnabled = _savedHitRoll; }

    // ---------- harness ----------

    /// <summary>Every draw is fixed: Next(min, max) is min, Next(max) is max - 1.</summary>
    private sealed class FixedRandom : Random
    {
        public override int Next() => 0;
        public override int Next(int maxValue) => maxValue <= 0 ? 0 : maxValue - 1;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0.5;
        protected override double Sample() => 0.5;
    }

    private sealed class Recorder : ICombatObserver
    {
        public readonly List<CombatEvent> Events = new();
        public void OnFightStart(CombatFightStart start) { }
        public void OnEvent(CombatEvent e) { if (!e.IsUse && e.Amount > 0) Events.Add(e); }
        public void OnFightEnd(long playerHpEnd) { }
    }

    private static Character Hero(string name = "Tester", CharacterClass cls = CharacterClass.Warrior,
        CharacterAI ai = CharacterAI.Human, int level = 30)
    {
        var c = new Character
        {
            Name1 = name, Name2 = name, AI = ai, Class = cls, Race = CharacterRace.Human, Sex = CharacterSex.Male,
            Age = 25, Level = level, HP = 5000, MaxHP = 5000, BaseMaxHP = 5000,
            Strength = 120, BaseStrength = 120, Charisma = 20, BaseCharisma = 20,
            Dexterity = 20, BaseDexterity = 20, Agility = 10, BaseAgility = 10,
            Constitution = 30, BaseConstitution = 30, Intelligence = 20, BaseIntelligence = 20,
            Wisdom = 20, BaseWisdom = 20, Stamina = 100, BaseStamina = 100, Mental = 80,
            BonusWeapPow = 40, CombatSpeed = CombatSpeed.Instant,
        };
        c.RecalculateStats();
        c.HP = c.MaxHP;
        return c;
    }

    private static Monster Dummy() => new Monster
    {
        Name = "Training Dummy", Level = 30, HP = 1_000_000_000L, MaxHP = 1_000_000_000L, Strength = 1, Defence = 0,
        ArmPow = 0, Experience = 1, Gold = 0, IsActive = true,
    };

    private static CombatEngine NewEngine(Character player, Random rng, List<Character>? teammates = null)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, rng);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, teammates ?? new List<Character>());
        return engine;
    }

    /// <summary>One live main-hand swing of the player against a dummy, on a fresh engine with fixed draws.</summary>
    private static long Swing(Character player) =>
        (long)typeof(CombatEngine).GetMethod("ComputePlayerSwingDamage", F)!
            .Invoke(NewEngine(player, new FixedRandom()), new object[] { player, Dummy(), false, 0, 1 })!;

    private static Monster Boss() => new Monster
    {
        Name = "Maelketh", Level = 60, HP = 500_000, MaxHP = 500_000, Strength = 1000, Defence = 500,
        IsBoss = true, IsActive = true,
    };

    private static void Enrage(CombatEngine engine, BossCombatContext ctx, Monster boss, int round) =>
        typeof(CombatEngine).GetMethod("CheckBossEnrage", F)!.Invoke(engine, new object?[] { ctx, boss, round });

    private static void Dispel(CombatEngine engine, Monster target) =>
        typeof(CombatEngine).GetMethod("HandleSpecialSpellEffectOnMonster", F)!
            .Invoke(engine, new object[] { target, "dispel", 0, Hero("Caster", CharacterClass.Cleric), 0L, new CombatResult() });

    private static (CombatEngine engine, BossCombatContext ctx) BossFight()
    {
        var engine = NewEngine(Hero(), new FixedRandom());
        var ctx = new BossCombatContext { EnrageRound = 8, AttacksPerRound = 2 };
        engine.BossContext = ctx;
        return (engine, ctx);
    }

    // ---------- 1. Dispel and enrage ----------

    [Fact]
    public void EnragedBoss_Dispelled_StrengthDefenceAndAttacksReturn()
    {
        GameConfig.ModBossEnrageDamageMultiplier.Should().BeGreaterThan(1.0, "the enrage raises Strength");
        GameConfig.ModBossEnrageExtraAttacks.Should().BeGreaterThan(0, "the enrage adds attacks");

        var (engine, ctx) = BossFight();
        var boss = Boss();
        Enrage(engine, ctx, boss, 8);
        boss.IsEnraged.Should().BeTrue();
        boss.Strength.Should().Be((long)(1000 * GameConfig.ModBossEnrageDamageMultiplier));
        ctx.AttacksPerRound.Should().Be(2 + GameConfig.ModBossEnrageExtraAttacks);

        Dispel(engine, boss);

        boss.IsEnraged.Should().BeFalse();
        boss.Strength.Should().Be(1000, "Dispel takes the enrage's Strength back");
        ctx.AttacksPerRound.Should().Be(2, "Dispel takes the enrage's extra attacks back");
        // Dispel then cuts Defence by 20 percent as it always has, from the pre-enrage 500
        boss.Defence.Should().Be(400, "the enrage's Defence is taken back before Dispel's own cut");
    }

    [Fact]
    public void EnragedBoss_Dispelled_DoesNotEnrageAgainThatFight()
    {
        var (engine, ctx) = BossFight();
        var boss = Boss();
        Enrage(engine, ctx, boss, 8);
        Dispel(engine, boss);

        Enrage(engine, ctx, boss, 9);
        Enrage(engine, ctx, boss, 20);

        boss.IsEnraged.Should().BeFalse();
        ctx.IsEnraged.Should().BeTrue("the fight's enrage has happened and stays spent");
        boss.Strength.Should().Be(1000);
        boss.Defence.Should().Be(400);
        ctx.AttacksPerRound.Should().Be(2);
    }

    [Fact]
    public void EnragedBoss_DispelledTwice_SecondDispelOnlyCutsDefence()
    {
        var (engine, ctx) = BossFight();
        var boss = Boss();
        Enrage(engine, ctx, boss, 8);
        Dispel(engine, boss);
        Dispel(engine, boss);

        boss.Strength.Should().Be(1000);
        ctx.AttacksPerRound.Should().Be(2);
        boss.Defence.Should().Be(320, "400 less its own 20 percent, nothing more is taken back");
    }

    [Fact]
    public void EnragedBoss_StrengthCutAfterEnrage_KeepsItsShareOfTheCut()
    {
        var (engine, ctx) = BossFight();
        var boss = Boss();
        Enrage(engine, ctx, boss, 8);
        long enraged = boss.Strength;
        boss.Strength = enraged * 9 / 10;   // a 10 percent cut landed while enraged

        Dispel(engine, boss);

        boss.Strength.Should().Be(900, "the enrage is taken back in proportion, so the cut stays");
    }

    [Fact]
    public void NotEnragedMonster_Dispelled_IsUnchanged()
    {
        var (engine, ctx) = BossFight();
        var boss = Boss();
        Dispel(engine, boss);

        boss.Strength.Should().Be(1000);
        boss.Defence.Should().Be(400, "only Dispel's own 20 percent Defence cut, as before");
        ctx.AttacksPerRound.Should().Be(2);
        ctx.IsEnraged.Should().BeFalse();

        var plain = new Monster { Name = "Brute", Level = 30, HP = 5000, MaxHP = 5000, Strength = 200, Defence = 100, IsActive = true };
        var noBoss = NewEngine(Hero(), new FixedRandom());
        Dispel(noBoss, plain);
        plain.Strength.Should().Be(200);
        plain.Defence.Should().Be(80);
    }

    // ---------- 2. Workshop and Arena on the live swing ----------

    [Fact]
    public void WorkshopBuff_LiveSwing_DealsTwentyPercentMore()
    {
        long plain = Swing(Hero());
        plain.Should().BeGreaterThan(100);

        var sharpened = Hero();
        sharpened.WorkshopBuffCombats = 10;
        Swing(sharpened).Should().Be(plain + (long)(plain * 0.20), "the Workshop sharpening reads on the live swing");
        sharpened.WorkshopBuffCombats.Should().Be(10, "a swing does not spend a fight");
    }

    [Fact]
    public void WorkshopBuff_CounterTicksAtTheEndOfAFight()
    {
        var c = Hero();
        c.WorkshopBuffCombats = 10;
        typeof(CombatEngine).GetMethod("ConsumeCombatBuffs", S)!.Invoke(null, new object[] { c });
        c.WorkshopBuffCombats.Should().Be(9);
        c.WorkshopBuffCombats = 0;
        typeof(CombatEngine).GetMethod("ConsumeCombatBuffs", S)!.Invoke(null, new object[] { c });
        c.WorkshopBuffCombats.Should().Be(0);
    }

    [Fact]
    public void ArenaBuff_LiveSwing_DealsTenPercentMore()
    {
        long plain = Swing(Hero());

        var arena = Hero();
        arena.SettlementBuffType = (int)SettlementBuffType.DamageBonus;
        arena.SettlementBuffCombats = GameConfig.SettlementBuffDuration;
        arena.SettlementBuffValue = GameConfig.SettlementDamageBonus;
        arena.HasSettlementBuff.Should().BeTrue();
        Swing(arena).Should().Be(plain + (long)(plain * GameConfig.SettlementDamageBonus), "the Arena buff reads on the live swing");
    }

    [Fact]
    public void WithoutTheBuffs_LiveSwing_IsUnchanged()
    {
        long plain = Swing(Hero());
        Swing(Hero()).Should().Be(plain);

        var other = Hero();
        other.SettlementBuffType = (int)SettlementBuffType.XPBonus;
        other.SettlementBuffCombats = GameConfig.SettlementBuffDuration;
        other.SettlementBuffValue = GameConfig.SettlementXPBonus;
        Swing(other).Should().Be(plain, "another settlement buff adds no damage");

        var spentArena = Hero();
        spentArena.SettlementBuffType = (int)SettlementBuffType.DamageBonus;
        spentArena.SettlementBuffCombats = 0;
        spentArena.SettlementBuffValue = GameConfig.SettlementDamageBonus;
        Swing(spentArena).Should().Be(plain, "a spent Arena buff adds nothing");
    }

    private static async Task<List<long>> TeammateSwings(bool buffed)
    {
        var leader = Hero("Leader");
        var mate = Hero("Mate", CharacterClass.Warrior, CharacterAI.Computer, level: 9);
        if (buffed)
        {
            mate.WorkshopBuffCombats = 10;
            mate.SettlementBuffType = (int)SettlementBuffType.DamageBonus;
            mate.SettlementBuffCombats = GameConfig.SettlementBuffDuration;
            mate.SettlementBuffValue = GameConfig.SettlementDamageBonus;
        }
        leader.TeammateDisabledAbilities[mate.GetSkillToggleKey()] =
            ClassAbilitySystem.GetAvailableAbilities(mate).Select(a => a.Id).ToList();
        var mates = new List<Character> { mate };
        var engine = NewEngine(leader, new Random(1321), mates);
        var result = new CombatResult { Player = leader, Teammates = mates };
        typeof(CombatEngine).GetMethod("BeginFightEvents", F)!.Invoke(engine, new object[] { result });
        var rec = new Recorder();
        engine.Observer = rec;
        await (Task)typeof(CombatEngine).GetMethod("ProcessTeammateActionMultiMonster", F)!
            .Invoke(engine, new object[] { mate, new List<Monster> { Dummy() }, result })!;
        return rec.Events.Where(e => e.Actor == CombatSide.Team && e.Target == CombatSide.Monster).Select(e => e.Amount).ToList();
    }

    [Fact]
    public async Task TeammateSwing_IsUnchangedByTheBuffs()
    {
        var plain = await TeammateSwings(buffed: false);
        plain.Should().NotBeEmpty("the teammate swings");
        plain.Sum().Should().BeGreaterThan(0);
        (await TeammateSwings(buffed: true)).Should().Equal(plain, "the buffs are the player's own swing only");
    }
}
