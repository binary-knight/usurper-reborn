using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 (E2): the rig the weapon poison over time tests share, and the draw count of a fight with poison weapons.
/// The poison proc draws once against PoisonEnchantProcChance, as before; the tick in the status step draws nothing.
/// </summary>
internal static class PoisonDot136Rig
{
    internal const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>A Random that counts every draw made through it.</summary>
    internal sealed class CountingRandom : Random
    {
        public int Draws;
        public CountingRandom(int seed) : base(seed) { }
        public override int Next() { Draws++; return base.Next(); }
        public override int Next(int maxValue) { Draws++; return base.Next(maxValue); }
        public override int Next(int minValue, int maxValue) { Draws++; return base.Next(minValue, maxValue); }
        public override double NextDouble() { Draws++; return base.NextDouble(); }
        public override long NextInt64() { Draws++; return base.NextInt64(); }
        public override long NextInt64(long maxValue) { Draws++; return base.NextInt64(maxValue); }
        public override long NextInt64(long minValue, long maxValue) { Draws++; return base.NextInt64(minValue, maxValue); }
        public override float NextSingle() { Draws++; return base.NextSingle(); }
        public override void NextBytes(byte[] buffer) { Draws++; base.NextBytes(buffer); }
        public override void NextBytes(Span<byte> buffer) { Draws++; base.NextBytes(buffer); }
    }

    internal static int Venom(string name, int poisonDamage) => EquipmentDatabase.RegisterDynamic(new Equipment
    {
        Name = name, Slot = EquipmentSlot.MainHand, WeaponType = WeaponType.Dagger, WeaponPower = 100,
        MinLevel = 1, Rarity = EquipmentRarity.Common, HasPoisonEnchant = true, PoisonDamage = poisonDamage,
    });

    internal static Character Hero(string name, int poisonDamage)
    {
        var hero = new Character
        {
            Name1 = name, Name2 = name, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 60,
            Strength = 200, HP = 1_000_000, MaxHP = 1_000_000, CombatSpeed = CombatSpeed.Instant,
        };
        hero.EquippedItems[EquipmentSlot.MainHand] = Venom($"PD136 Venom {poisonDamage}", poisonDamage);
        return hero;
    }

    internal static Monster Foe() => new Monster
    {
        Name = "Wolf", Level = 60, HP = 10_000_000, MaxHP = 10_000_000, Strength = 10, Defence = 0, ArmPow = 0,
        IsActive = true,
    };

    /// <summary>A Random whose NextDouble is always 0, so every proc check fires; other draws as seeded.</summary>
    internal sealed class ProcRandom : Random
    {
        public ProcRandom() : base(136) { }
        public override double NextDouble() => 0.0;
    }

    internal static CombatEngine Engine(Character player, List<Character> teammates, Random random, MemoryStream? output = null)
    {
        var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), output ?? new MemoryStream()));
        typeof(CombatEngine).GetField("random", F)!.SetValue(engine, random);
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, player);
        typeof(CombatEngine).GetField("currentTeammates", F)!.SetValue(engine, teammates);
        return engine;
    }

    /// <summary>One weapon proc check after a hit of <paramref name="hit"/> HP.</summary>
    internal static void Proc(CombatEngine engine, Character attacker, Monster foe, long hit, CombatResult result) =>
        typeof(CombatEngine).GetMethod("CheckElementalEnchantProcs", F)!
            .Invoke(engine, new object[] { attacker, foe, hit, result, EquipmentSlot.MainHand });

    /// <summary>
    /// The monster's status step for a new round: the monster is held after its ticks (a stun of one round), so the
    /// step takes the ticks and nothing else.
    /// </summary>
    internal static void StatusStep(CombatEngine engine, Monster foe, Character player, CombatResult result)
    {
        foe.StatusTickedThisRound = false;
        foe.StunRounds = 1;
        ((Task)typeof(CombatEngine).GetMethod("ProcessMonsterAction", F)!
            .Invoke(engine, new object?[] { foe, player, result, new List<Monster> { foe } })!).GetAwaiter().GetResult();
    }
}

[Collection("SharedGameSingletons")]
public class PoisonDot136RngTests
{
    // Measured at the start head f020056b (the instant poison proc): receipts/rng-baseline-f020056b.txt.
    private const int DrawsAtStartHead = 160;

    [Fact]
    public void PoisonDot136_AFightWithPoisonWeapons_DrawsTheSameCountAsTheStartHead()
    {
        var hero = PoisonDot136Rig.Hero("Hero", 0);
        var mate = PoisonDot136Rig.Hero("Mate", 400);
        var random = new PoisonDot136Rig.CountingRandom(136);
        var engine = PoisonDot136Rig.Engine(hero, new List<Character> { mate }, random);
        var result = new CombatResult { Player = hero, Teammates = new List<Character> { mate } };
        var foe = PoisonDot136Rig.Foe();
        for (int round = 0; round < 80; round++)
        {
            PoisonDot136Rig.Proc(engine, hero, foe, 1000, result);
            PoisonDot136Rig.Proc(engine, mate, foe, 1000, result);
            PoisonDot136Rig.StatusStep(engine, foe, hero, result);
        }
        foe.IsAlive.Should().BeTrue("the rig monster outlives the run");
        random.Draws.Should().Be(DrawsAtStartHead, "the poison proc draws once per check and the tick draws nothing");
    }
}
