using System;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.3.1: every bonus clamp in StatEffectsSystem.CritChance uses one ceiling, 50 + min(25, effectiveDex / 30),
/// from the same Dexterity (with the drug bonus) that the base chance uses. Before, the drug and boon clamps
/// were a flat 50 and the four later clamps used the Dexterity without the drug bonus.
/// All state is built in memory on Character instances; no singleton or static is written, and
/// the real save folder is neither read nor written.
/// </summary>
[Collection("SharedGameSingletons")]
public class CritCeiling131Tests
{
    private static Character Hero(long dex) => new Character
    {
        Name2 = "Hero", Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 40,
        HP = 900, MaxHP = 900, BaseMaxHP = 900, Dexterity = dex, BaseDexterity = dex,
    };

    private static void Drug(Character c, DrugType d) { c.ActiveDrug = d; c.DrugEffectDays = 3; }
    private static void Boon(Character c, float pct) { c.CachedBoonEffects = new ActiveBoonEffects { CritPercent = pct }; }
    private static void Shrine(Character c) { c.AttunedShrineId = "noctura"; c.AttunedShrineExpiresGameDay = int.MaxValue; }
    private static void Pet(Character c) { c.ActivePetId = "cave_spider"; }
    private static void Align(Character c) { c.Chivalry = 0; c.Darkness = 900; }   // Evil, +5

    private static int Ceiling(long effDex) => 50 + (int)Math.Min(25, effDex / 30);

    [Fact]
    public void Dex750_NoBonus_Is75()
    {
        StatEffectsSystem.CritChance(Hero(750)).Should().Be(75);
    }

    [Fact]
    public void Dex750_DrugCritBonus_Stays75()
    {
        var c = Hero(750); Drug(c, DrugType.QuickSilver);
        StatEffectsSystem.CritChance(c).Should().Be(75, "was clamped to 50 by the drug clamp");
    }

    [Fact]
    public void Dex750_BoonCritPercent_Stays75()
    {
        var c = Hero(750); Boon(c, 0.10f);
        StatEffectsSystem.CritChance(c).Should().Be(75, "was clamped to 50 by the boon clamp");
    }

    [Fact]
    public void Dex750_DrugAndBoon_Stays75()
    {
        var c = Hero(750); Drug(c, DrugType.QuickSilver); Boon(c, 0.15f);
        StatEffectsSystem.CritChance(c).Should().Be(75);
    }

    [Fact]
    public void Dex280_QuickSilver_ReachesFifty_ThenDrugPlusBoonStopAtCeilingSixty()
    {
        // QuickSilver adds 20 Dexterity too: effective 300, base 5 + 30 = 35, ceiling 60.
        var c = Hero(280); Drug(c, DrugType.QuickSilver);
        StatEffectsSystem.CritChance(c).Should().Be(50, "35 + 15");

        Boon(c, 0.15f);   // 35 + 15 + 15 = 65
        StatEffectsSystem.CritChance(c).Should().Be(60, "the ceiling of 300, not 50");
    }

    [Fact]
    public void Dex300_BoonAlone_AddsItsValue()
    {
        var c = Hero(300);
        StatEffectsSystem.CritChance(c).Should().Be(35);
        Boon(c, 0.10f);
        StatEffectsSystem.CritChance(c).Should().Be(45);
    }

    [Fact]
    public void Dex600_BoonAndDrug_PassFifty()
    {
        var c = Hero(600); Drug(c, DrugType.QuickSilver); Boon(c, 0.15f);
        // effective 620: base 67, ceiling 70
        StatEffectsSystem.CritChance(c).Should().Be(70, "was 50");
    }

    [Theory]
    [InlineData("shrine")]
    [InlineData("pet")]
    [InlineData("align")]
    public void LaterBonus_AfterDrugDexLiftsCeiling_NeverLowersChance(string later)
    {
        // Dex 590: ceiling 69 alone, 70 with the drug's Dexterity (610). A later bonus must not pull 70 down to 69.
        void Apply(Character c)
        {
            switch (later) { case "shrine": Shrine(c); break; case "pet": Pet(c); break; default: Align(c); break; }
        }

        var without = Hero(590); Drug(without, DrugType.DarkEssence);   // +15 Dexterity only, no crit bonus
        int before = StatEffectsSystem.CritChance(without);
        before.Should().Be(65, "605 effective: 5 + 60, ceiling 70");

        var with = Hero(590); Drug(with, DrugType.DarkEssence); Apply(with);
        int after = StatEffectsSystem.CritChance(with);
        after.Should().BeGreaterThanOrEqualTo(before);
        after.Should().BeLessThanOrEqualTo(Ceiling(605));

        // Push the base to the ceiling: QuickSilver, effective 610, base 66 + 15 = 81 -> 70, then the later bonus.
        var capped = Hero(590); Drug(capped, DrugType.QuickSilver);
        StatEffectsSystem.CritChance(capped).Should().Be(70);
        var cappedLater = Hero(590); Drug(cappedLater, DrugType.QuickSilver); Apply(cappedLater);
        StatEffectsSystem.CritChance(cappedLater).Should().Be(70, "the later clamp must use the ceiling of 610, not of 590");
    }

    [Fact]
    public void Table_EveryBonus_NeverLowersAndStaysInBounds()
    {
        long[] dexes = { 0, 100, 300, 450, 600, 750, 1000 };
        string[] bonuses = { "drug", "boon", "shrine", "pet", "align", "temp", "all" };

        foreach (var dex in dexes)
        {
            int without = StatEffectsSystem.CritChance(Hero(dex));
            foreach (var b in bonuses)
            {
                var c = Hero(dex);
                if (b is "drug" or "all") Drug(c, DrugType.QuickSilver);
                if (b is "boon" or "all") Boon(c, 0.15f);
                if (b is "shrine" or "all") Shrine(c);
                if (b is "pet" or "all") Pet(c);
                if (b is "align" or "all") Align(c);
                if (b is "temp" or "all") c.TempCritChanceBonus = 10;

                long eff = dex + DrugSystem.GetDrugEffects(c).DexterityBonus;
                int with = StatEffectsSystem.CritChance(c);
                string why = $"dex {dex}, bonus {b}";
                with.Should().BeGreaterThanOrEqualTo(without, why);
                with.Should().BeLessThanOrEqualTo(Ceiling(eff), why);
                with.Should().BeGreaterThanOrEqualTo(5, why);
            }
        }
    }

    [Fact]
    public void NegativeTempBonus_StillLowers_AndStopsAtFive()
    {
        var c = Hero(300);
        c.TempCritChanceBonus = -10;
        StatEffectsSystem.CritChance(c).Should().Be(25);
        c.TempCritChanceBonus = -100;
        StatEffectsSystem.CritChance(c).Should().Be(5);
    }
}
