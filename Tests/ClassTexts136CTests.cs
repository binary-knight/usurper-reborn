using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.ClassTexts136Support;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 X-C: the group C class texts (Alchemist, Mystic Shaman) state the code, with their numbers read from the
/// template, GameConfig and one cast at fixed stats; and hu.json holds no Cyrillic letter.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassTexts136CTests
{
    internal static readonly string[] Keys =
    {
        "ability.transmutation.desc", "ability.windfury_totem.desc", "ability.smoke_bomb.desc", "ability.stimulant_brew.desc",
        "ability.smoke_screen.desc", "ability.grand_remedy.desc", "combat.party_remedy", "combat.tm_fully_restored",
        "ability.flametongue.desc", "ability.healing_totem.desc", "ability.earthbind_totem.desc",
        "combat.shaman_totem_earthbind_summon", "ability.searing_totem.desc",
        "combat.shaman_enchant_frost", "combat.shaman_enchant_storm", "combat.shaman_enchant_storm_hit",
    };

    private static readonly string[] Descs =
    {
        "transmutation", "windfury_totem", "smoke_bomb", "stimulant_brew", "smoke_screen", "grand_remedy", "flametongue",
        "healing_totem", "earthbind_totem", "searing_totem",
    };

    private static ClassAbilitySystem.ClassAbility Ab(string id) => ClassAbilitySystem.GetAbility(id)!;
    private static double Lv(int level) => 1.0 + level * 0.03;

    [Fact]
    public void ClassTexts136C_EveryChangedKey_HasFiveLanguagesWithEnsPlaceholdersAndNumbers()
    {
        FiveLanguages(Keys);
        foreach (var id in Descs) { DescLength($"ability.{id}.desc"); AbilityDescIsCode(id); }
        Fits79("combat.shaman_totem_earthbind_summon", GameConfig.ShamanTotemBaseDuration);
        Fits79("combat.party_remedy");
        Fits79("combat.tm_fully_restored", "A Long Teammate Name", 99999);
    }

    [Fact]
    public void ClassTexts136C_HuJson_HasNoCyrillicLetter()
    {
        string hu = File.ReadAllText(Path.Combine(UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Localization", "hu.json"));
        Regex.Matches(hu, "[Ѐ-ӿ]").Count.Should().Be(0, "every word is in Latin letters");
        Loc.GetIn("hu", "combat.shaman_enchant_frost").Should().StartWith("Fegyvered ").And.Contain("jégsebzés");
        Loc.GetIn("hu", "combat.shaman_enchant_storm").Should().Contain("viharsebzés");
        Loc.GetIn("hu", "combat.shaman_enchant_storm_hit").Should().Contain("viharsebzéssel");
    }

    [Fact]
    public void ClassTexts136C_Transmutation_HealBonusesAndWhatItClears()
    {
        var ab = Ab("transmutation");
        En("ability.transmutation.desc").Should().Contain($"heals {ab.BaseHealing} HP, +{ab.AttackBonus} ATK and +{ab.DefenseBonus} DEF for {ab.Duration} rounds")
            .And.Contain("not lasting poison or disease").And.NotContain("Purges ailments");
        var h = Hero(CharacterClass.Alchemist, level: 20); h.Charisma = 30;
        var r = ClassAbilitySystem.UseAbility(h, "transmutation", new Random(5));
        r.AttackBonus.Should().Be((int)(ab.AttackBonus * Lv(20) * (1.0 + 20 * 0.025)));
        r.DefenseBonus.Should().Be((int)(ab.DefenseBonus * Lv(20)));
        r.Duration.Should().Be(ab.Duration);
        var hero = Hero(CharacterClass.Alchemist);
        hero.HP = 1000;
        hero.ApplyStatus(StatusEffect.Poisoned, 5);
        hero.Poison = 5;
        Run("transmutation", hero);
        hero.ActiveStatuses.Keys.Should().NotContain(s => s.IsNegative(), "harmful statuses are cleared");
        hero.Poison.Should().Be(5, "the lasting poison stays");
    }

    [Fact]
    public void ClassTexts136C_WindfuryTotem_OnlyTheCastersAttacks()
    {
        En("ability.windfury_totem.desc").Should().Contain($"for {GameConfig.ShamanTotemBaseDuration} rounds, each of your attack turns has a {GameConfig.ShamanWindfuryProcChance}% chance of one extra attack")
            .And.NotContain("all allies");
        var mate = G.Mate("Mate0");
        var hero = Hero(CharacterClass.MysticShaman);
        Run("windfury_totem", hero, team: new List<Character> { mate });
        hero.ActiveTotemType.Should().Be(4);
        hero.ActiveTotemRounds.Should().Be(GameConfig.ShamanTotemBaseDuration);
        mate.ActiveTotemType.Should().Be(0, "a teammate gains no extra attack");
    }

    [Fact]
    public void ClassTexts136C_SmokeBombAndSmokeScreen_DefenseOnly()
    {
        var bomb = Ab("smoke_bomb");
        En("ability.smoke_bomb.desc").Should().Contain($"+{bomb.DefenseBonus} defense (grows with level) for {bomb.Duration} rounds").And.NotContain("confuse");
        var h = Hero(CharacterClass.Alchemist, level: 20);
        ClassAbilitySystem.UseAbility(h, "smoke_bomb", new Random(5)).DefenseBonus.Should().Be((int)(bomb.DefenseBonus * Lv(20)));
        var foe = G.Dummy();
        Run("smoke_bomb", Hero(CharacterClass.Alchemist), new List<Monster> { foe });
        foe.IsConfused.Should().BeFalse("no confusion");

        var screen = Ab("smoke_screen");
        En("ability.smoke_screen.desc").Should().Contain($"+{screen.DefenseBonus} defense (grows with level and CON) for the whole party for {screen.Duration} rounds")
            .And.NotContain("evasion");
        screen.Type.Should().Be(ClassAbilitySystem.AbilityType.Defense);
        var c = Hero(CharacterClass.Alchemist, level: 20); c.Constitution = 30;
        ClassAbilitySystem.UseAbility(c, "smoke_screen", new Random(5)).DefenseBonus.Should().Be((int)(screen.DefenseBonus * Lv(20) * (1.0 + 20 * 0.025)));
        var mate = G.Mate("Mate0");
        var cast = Run("smoke_screen", Hero(CharacterClass.Alchemist), team: new List<Character> { mate });
        mate.TempDefenseBonus.Should().Be(cast.Result.DefenseBonus, "the whole party gets the full bonus");
    }

    [Fact]
    public void ClassTexts136C_StimulantBrew_AttackForThePartyNoStamina()
    {
        var ab = Ab("stimulant_brew");
        En("ability.stimulant_brew.desc").Should().Contain($"+{ab.AttackBonus} attack (grows with level and CHA) for {ab.Duration} rounds; teammates get 75%")
            .And.NotContain("stamina");
        var mate = G.Mate("Mate0");
        var hero = Hero(CharacterClass.Alchemist);
        hero.CurrentCombatStamina = 0;
        var cast = Run("stimulant_brew", hero, team: new List<Character> { mate });
        mate.TempAttackBonus.Should().Be((int)(cast.Result.AttackBonus * 0.75));
        hero.CurrentCombatStamina.Should().Be(0, "no stamina is given");
    }

    [Fact]
    public void ClassTexts136C_GrandRemedy_HealShareAndCures()
    {
        var ab = Ab("grand_remedy");
        En("ability.grand_remedy.desc").Should().Contain($"heals you {ab.BaseHealing} HP and each teammate 75% of that")
            .And.Contain("cures poison, bleeding, burning, curses, weakness and disease").And.NotContain("fully");
        En("combat.party_remedy").Should().NotContain("all wounds");
        En("combat.tm_fully_restored").Should().NotContain("fully");
        var mate = G.Mate("Mate0"); mate.HP = 1;
        mate.ApplyStatus(StatusEffect.Bleeding, 5);
        var hero = Hero(CharacterClass.Alchemist); hero.HP = 1000;
        var cast = Run("grand_remedy", hero, team: new List<Character> { mate });
        (mate.HP - 1).Should().Be((long)(cast.Result.Healing * 0.75));
        mate.HasStatus(StatusEffect.Bleeding).Should().BeFalse();
    }

    [Fact]
    public void ClassTexts136C_Flametongue_AttackIsAShareOfStrength()
    {
        En("ability.flametongue.desc").Should().Contain($"for {GameConfig.ShamanEnchantDuration} rounds").And.Contain("attack equal to 15% of your Strength")
            .And.NotContain("+15% attack power");
        var hero = Hero(CharacterClass.MysticShaman); hero.Strength = 200;
        Run("flametongue", hero);
        hero.TempAttackBonus.Should().Be(Math.Max(1, (int)(hero.Strength * 0.15)));
        hero.TempAttackBonusDuration.Should().Be(GameConfig.ShamanEnchantDuration);
    }

    [Fact]
    public void ClassTexts136C_HealingTotem_AloneOrWithTeammates()
    {
        En("ability.healing_totem.desc").Should().Contain($"for {GameConfig.ShamanTotemBaseDuration} rounds")
            .And.Contain($"heals you {Percent(GameConfig.ShamanTotemHealPercent)}% of max HP each round")
            .And.Contain($"you and each teammate {Percent(GameConfig.ShamanTotemHealPercentGroup)}% each while a teammate lives");
    }

    [Fact]
    public void ClassTexts136C_EarthbindTotem_CutsAttackNoSlow()
    {
        En("ability.earthbind_totem.desc").Should().Contain($"cutting their attack by 20% for {GameConfig.ShamanTotemBaseDuration} rounds").And.NotContain("slow");
        En("combat.shaman_totem_earthbind_summon").Should().Contain("enemy attack -20%").And.NotContain("slow");
        var foe = G.Dummy();
        var hero = Hero(CharacterClass.MysticShaman);
        Run("earthbind_totem", hero, new List<Monster> { foe });
        hero.ActiveTotemType.Should().Be(2);
        hero.ActiveTotemPower.Should().Be(20, "the attack cut the text gives");
        foe.IsSlowed.Should().BeFalse("no Slow is set");
    }

    [Fact]
    public void ClassTexts136C_SearingTotem_CastHitThenThePulse()
    {
        var ab = Ab("searing_totem");
        En("ability.searing_totem.desc").Should().Contain($"a {ab.BaseDamage} damage fire hit on your target now")
            .And.Contain($"every enemy each round for {GameConfig.ShamanTotemBaseDuration} rounds");
        ab.Duration.Should().Be(GameConfig.ShamanTotemBaseDuration);
        var foe = G.Dummy();
        var hero = Hero(CharacterClass.MysticShaman);
        var cast = Run("searing_totem", hero, new List<Monster> { foe });
        cast.Lost[0].Should().BeGreaterThan(0, "the cast hits its target at once");
        hero.ActiveTotemPower.Should().Be(cast.Result.Damage, "the pulse is the cast's hit");
    }
}
