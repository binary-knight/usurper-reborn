using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.ClassTexts136Support;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 X-A: the group A class texts (Warrior, Barbarian, Paladin and the shared rows) state the code. Each number a
/// changed text gives is read from the code here (the template, a GameConfig value, or one cast at fixed stats), so a
/// later code change that moves a number fails here until the text follows.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassTexts136ATests
{
    internal static readonly string[] Keys =
    {
        "ability.power_strike.desc", "ability.thundering_roar.desc", "ability.wild_swing.desc", "ability.primal_scream.desc",
        "ability.divine_smite.desc", "ability.rally.desc", "ability.focus.desc", "ability.cleansing_light.desc",
    };

    private static ClassAbilitySystem.ClassAbility Ab(string id) => ClassAbilitySystem.GetAbility(id)!;

    [Fact]
    public void ClassTexts136A_EveryChangedKey_HasFiveLanguagesWithEnsPlaceholdersAndNumbers()
    {
        FiveLanguages(Keys);
        foreach (var k in Keys) DescLength(k);
    }

    [Theory]
    [InlineData("power_strike")]
    [InlineData("thundering_roar")]
    [InlineData("wild_swing")]
    [InlineData("primal_scream")]
    [InlineData("divine_smite")]
    [InlineData("rally")]
    [InlineData("focus")]
    [InlineData("cleansing_light")]
    public void ClassTexts136A_Desc_IsTheCodeDescription(string id) => AbilityDescIsCode(id);

    [Fact]
    public void ClassTexts136A_PowerStrike_AnyWeapon_TwoHandedGripAdds()
    {
        string t = En("ability.power_strike.desc");
        t.Should().Contain($"{PercentOver(GameConfig.TwoHandedDamageBonus)}%").And.Contain("any weapon").And.NotContain("two-handed blow");
        Ab("power_strike").RequiredWeaponTypes.Should().BeNullOrEmpty("any weapon serves");
    }

    [Fact]
    public void ClassTexts136A_ThunderingRoar_StatesTheRoundsAndTheStick()
    {
        string t = En("ability.thundering_roar.desc");
        var ab = Ab("thundering_roar");
        t.Should().Contain($"{ab.Duration} rounds").And.Contain($"{GameConfig.SoftTauntStickChance}% roll").And.NotContain("forces");
        var foes = new List<Monster> { G.Dummy(), G.Dummy(), G.Dummy() };
        foes[2].IsBoss = true;
        var c = Run("thundering_roar", Hero(CharacterClass.Warrior), foes);
        foreach (var m in foes)
        {
            m.TauntRoundsLeft.Should().Be(ab.Duration, "every enemy is drawn for the rounds the text gives, a boss too");
            m.TauntStickChance.Should().Be(GameConfig.SoftTauntStickChance);
        }
    }

    [Fact]
    public void ClassTexts136A_WildSwing_BasePlusAQuarterOfWeaponPower()
    {
        var ab = Ab("wild_swing");
        En("ability.wild_swing.desc").Should().Contain($"{ab.BaseDamage} damage plus a quarter of your weapon power")
            .And.NotContain("harder than a normal swing");
        var bare = Hero(CharacterClass.Barbarian, level: 1); bare.Strength = 10; bare.Dexterity = 10; bare.WeapPow = 0;
        var armed = Hero(CharacterClass.Barbarian, level: 1); armed.Strength = 10; armed.Dexterity = 10; armed.WeapPow = ab.BaseDamage * 4;
        long d0 = ClassAbilitySystem.UseAbility(bare, "wild_swing", new Random(7)).Damage;
        long d1 = ClassAbilitySystem.UseAbility(armed, "wild_swing", new Random(7)).Damage;
        d1.Should().BeCloseTo(2 * d0, 2, "a weapon power of four times the base adds the base again (a quarter of it)");
    }

    [Fact]
    public void ClassTexts136A_PrimalScream_FalloffAndConfusion()
    {
        var ab = Ab("primal_scream");
        En("ability.primal_scream.desc").Should().Contain($"{ab.BaseDamage} base damage to the first, 75%, 50% and 25% to the next")
            .And.Contain("Confuses survivors for 2 rounds").And.NotContain("terrif");
        var foes = Enumerable.Range(0, 5).Select(_ => G.Dummy()).ToList();
        var c = Run("primal_scream", Hero(CharacterClass.Barbarian, level: 60), foes);
        c.Lost[0].Should().BeGreaterThan(0);
        c.Lost[1].Should().BeCloseTo((long)(c.Lost[0] * 0.75), 1);
        c.Lost[2].Should().BeCloseTo((long)(c.Lost[0] * 0.50), 1);
        c.Lost[3].Should().BeCloseTo((long)(c.Lost[0] * 0.25), 1);
        c.Lost[4].Should().BeCloseTo((long)(c.Lost[0] * 0.25), 1);
        foes.Should().OnlyContain(m => m.IsConfused && m.ConfusedDuration == 2, "survivors are confused for 2 rounds");
        GameConfig.BossSoftControlResistPercent.Should().BeGreaterThan(0, "a boss may shrug it off");
    }

    [Fact]
    public void ClassTexts136A_DivineSmite_MagicalAndTwiceOnUndeadAndDemons()
    {
        En("ability.divine_smite.desc").Should().Contain("magical strike").And.Contain("Undead and demons take its damage twice")
            .And.NotContain("through your weapon");
        var magical = (bool)typeof(CombatEngine).GetMethod("IsMagicalAbilityEffect", BindingFlags.NonPublic | BindingFlags.Static)!
            .Invoke(null, new object?[] { Ab("divine_smite").SpecialEffect })!;
        magical.Should().BeTrue("the hit is magical: no weapon procs and no two-handed bonus");
        var plain = Run("divine_smite", Hero(CharacterClass.Paladin), new List<Monster> { G.Dummy() }, seed: 11);
        foreach (var kind in new[] { MonsterClass.Undead, MonsterClass.Demon })
        {
            var foe = G.Dummy(); foe.MonsterClass = kind;
            var holy = Run("divine_smite", Hero(CharacterClass.Paladin), new List<Monster> { foe }, seed: 11);
            holy.Result.Damage.Should().Be(plain.Result.Damage);
            (holy.Lost[0] - plain.Lost[0]).Should().Be(holy.Result.Damage, $"{kind} takes the ability's damage a second time");
        }
    }

    [Fact]
    public void ClassTexts136A_Rally_ScaledHealAndFlatStamina()
    {
        var ab = Ab("rally");
        En("ability.rally.desc").Should().Contain($"heals {ab.BaseHealing} HP, scaled by level, Constitution and Wisdom")
            .And.Contain("restores 50 stamina");
        ab.Type.Should().Be(ClassAbilitySystem.AbilityType.Heal, "a Heal ability scales by level, Constitution and Wisdom");
        var hero = Hero(CharacterClass.Warrior);
        hero.CurrentCombatStamina = 0;
        hero.MaxCombatStamina.Should().BeGreaterThan(50);
        Run("rally", hero);
        hero.CurrentCombatStamina.Should().Be(50, "the stamina the text gives");
    }

    [Fact]
    public void ClassTexts136A_Focus_RaisesAttackPowerScaledByLevelAndCharisma()
    {
        var ab = Ab("focus");
        En("ability.focus.desc").Should().Contain($"raise your attack power: +{ab.AttackBonus}, scaled by level and Charisma")
            .And.NotContain("accuracy");
        ab.Type.Should().Be(ClassAbilitySystem.AbilityType.Buff);
        var hero = Hero(CharacterClass.Warrior, level: 10); hero.Charisma = 20;
        var r = ClassAbilitySystem.UseAbility(hero, "focus", new Random(3));
        r.AttackBonus.Should().Be((int)(ab.AttackBonus * (1.0 + 10 * 0.03) * (1.0 + 10 * 0.025)), "the base, by level and Charisma");
        r.Damage.Should().Be(0);
    }

    [Fact]
    public void ClassTexts136A_CleansingLight_StatusesOnly_Q17()
    {
        var ab = Ab("cleansing_light");
        En("ability.cleansing_light.desc").Should().Contain("removes all your harmful statuses")
            .And.Contain($"heals {ab.BaseHealing} HP, scaled by level, Constitution and Wisdom").And.NotContain("corruption");
        ab.Type.Should().Be(ClassAbilitySystem.AbilityType.Heal);
        var hero = Hero(CharacterClass.Paladin);
        hero.HP = 1000;
        hero.ApplyStatus(StatusEffect.Poisoned, 5);
        hero.ApplyStatus(StatusEffect.Bleeding, 5);
        var c = Run("cleansing_light", hero);
        hero.ActiveStatuses.Keys.Should().NotContain(s => s.IsNegative(), "every harmful status is removed");
        c.Result.Healing.Should().BeGreaterThan(0);
    }
}
