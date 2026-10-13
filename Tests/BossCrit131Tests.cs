using System;
using System.IO;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.3.1: the world boss attacks roll their critical hit on StatEffectsSystem.CritChance, the chance every
/// other fight rolls on, not on the Dexterity and equipment base alone. The normal attack rolls on it; the
/// special (precise) attack rolls on twice of it, capped at 95. CalculatePlayerDamage and
/// CalculatePreciseStrikeDamage are private instance methods reached by reflection, with a fixed rng
/// (variance exactly 1.0, the crit roll value chosen) and a terminal on memory streams. State is in
/// memory on Character instances; no singleton is written (nothing to restore in Dispose beyond the
/// terminal streams) and the real save folder is neither read nor written.
/// </summary>
[Collection("SharedGameSingletons")]
public class BossCrit131Tests : IDisposable
{
    private static readonly BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly WorldBossDefinition Boss = WorldBossDatabase.GetBossById("abyssal_leviathan")!;
    private readonly WorldBossSystem _sys = new();
    private readonly MemoryStream _in = new(), _out = new();
    private readonly TerminalEmulator _term;

    public BossCrit131Tests() { _term = new TerminalEmulator(_in, _out); }
    public void Dispose() { _in.Dispose(); _out.Dispose(); }

    /// <summary>Next(n) is a fixed roll, NextDouble is 0.5 (variance exactly 1.0).</summary>
    private sealed class FixedRandom : Random
    {
        private readonly int _next;
        public FixedRandom(int next) { _next = next; }
        public override int Next(int maxValue) => Math.Min(_next, maxValue - 1);
        public override double NextDouble() => 0.5;
    }

    // Dexterity 300: base chance 35, ceiling 60. Strength 100, no weapon, boss defence 0: a plain hit is 100.
    private static Character Hero() => new Character
    {
        Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 40,
        HP = 900, MaxHP = 900, BaseMaxHP = 900, Strength = 100, WeapPow = 0, Dexterity = 300, BaseDexterity = 300,
    };

    private static void Drug(Character c) { c.ActiveDrug = DrugType.QuickSilver; c.DrugEffectDays = 3; }   // Dex +20, crit +15: full 52
    private static void Boon(Character c) { c.CachedBoonEffects = new ActiveBoonEffects { CritPercent = 0.10f }; }   // full 45

    private static WorldBossRuntimeData Data() => new() { ScaledStrength = 100, ScaledDefence = 0 };

    private long Normal(Character c, int roll) =>
        (long)typeof(WorldBossSystem).GetMethod("CalculatePlayerDamage", F)!
            .Invoke(_sys, new object?[] { c, Boss, Data(), new FixedRandom(roll), true, null })!;

    private long Special(Character c, int roll) =>
        (long)typeof(WorldBossSystem).GetMethod("CalculatePreciseStrikeDamage", F)!
            .Invoke(_sys, new object?[] { c, Boss, Data(), new FixedRandom(roll), _term })!;

    // ---------------- 1. the normal attack ----------------

    [Fact]
    public void Normal_NoBonus_RollsOnTheBase_Unchanged()
    {
        var c = Hero();
        StatEffectsSystem.CritChance(c).Should().Be(35);
        Normal(c, 34).Should().Be(150, "34 is under 35");
        Normal(c, 35).Should().Be(100, "35 is not under 35");
    }

    [Fact]
    public void Normal_DrugBonus_CritsBetweenBaseAndFullChance()
    {
        var c = Hero(); Drug(c);
        StatEffectsSystem.CritChance(c).Should().Be(52);
        Normal(c, 40).Should().Be(150, "40 is over the base 35 and under the full 52");
        Normal(c, 52).Should().Be(100, "52 is not under 52");
    }

    [Fact]
    public void Normal_BoonBonus_CritsBetweenBaseAndFullChance()
    {
        var c = Hero(); Boon(c);
        StatEffectsSystem.CritChance(c).Should().Be(45);
        Normal(c, 40).Should().Be(150, "40 is over the base 35 and under the full 45");
        Normal(c, 45).Should().Be(100);
    }

    // ---------------- 2. the special attack ----------------

    [Fact]
    public void Special_NoBonus_RollsOnTwiceTheBase_Unchanged()
    {
        var c = Hero();
        Special(c, 69).Should().Be(144, "69 is under 70, a crit: 80 x 1.8");
        Special(c, 70).Should().Be(80, "70 is not under 70");
    }

    [Fact]
    public void Special_DrugBonus_TwiceTheFullChance_CappedAt95()
    {
        var c = Hero(); Drug(c);
        Special(c, 80).Should().Be(144, "80 is over twice the base (70) and under the cap 95 (twice 52 is 104)");
        Special(c, 94).Should().Be(144);
        Special(c, 95).Should().Be(80, "the cap is 95");
    }

    [Fact]
    public void Special_BoonBonus_TwiceTheFullChance()
    {
        var c = Hero(); Boon(c);
        Special(c, 80).Should().Be(144, "80 is over twice the base (70) and under twice 45 (90)");
        Special(c, 90).Should().Be(80);
    }

    [Fact]
    public void SourcePin_BothWorldBossSitesCallCritChance()
    {
        var src = File.ReadAllText(Path.Combine(FindRoot(), "Scripts", "Systems", "WorldBossSystem.cs"));
        src.Should().Contain("int critChance = StatEffectsSystem.CritChance(player);");
        src.Should().Contain("int critChance = Math.Min(95, StatEffectsSystem.CritChance(player) * 2);");
        src.Should().NotContain("GetCriticalHitChance(player.Dexterity");
    }

    private static string FindRoot()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d != null && !File.Exists(Path.Combine(d.FullName, "usurper-reloaded.csproj"))) d = d.Parent;
        return d!.FullName;
    }
}
