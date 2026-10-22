using System;
using System.Collections.Generic;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.ClassTexts136Support;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 X-B: the group B class texts (Ranger, Assassin, Jester, Bard) state the code: Death Mark and Noctura's Embrace,
/// with their numbers read from the template and one cast at fixed stats.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassTexts136BTests
{
    internal static readonly string[] Keys = { "ability.death_mark.desc", "ability.noctura_embrace.desc" };

    private static ClassAbilitySystem.ClassAbility Ab(string id) => ClassAbilitySystem.GetAbility(id)!;

    [Fact]
    public void ClassTexts136B_EveryChangedKey_HasFiveLanguagesWithEnsPlaceholdersAndNumbers()
    {
        FiveLanguages(Keys);
        foreach (var k in Keys) DescLength(k);
        AbilityDescIsCode("death_mark");
        AbilityDescIsCode("noctura_embrace");
    }

    [Fact]
    public void ClassTexts136B_DeathMark_MarkAndTheCastersAttack()
    {
        var ab = Ab("death_mark");
        En("ability.death_mark.desc").Should().Contain($"Mark target for {ab.Duration} rounds")
            .And.Contain($"+{GameConfig.MarkedBonusPercent}% damage").And.Contain($"you gain +{ab.AttackBonus} attack (grows with level)")
            .And.NotContain("INT");
        ab.Type.Should().Be(ClassAbilitySystem.AbilityType.Debuff, "a Debuff's attack bonus takes the level scale only");
        var a = Hero(CharacterClass.Assassin, level: 20); a.Intelligence = 10;
        var b = Hero(CharacterClass.Assassin, level: 20); b.Intelligence = 80;
        int bonusA = ClassAbilitySystem.UseAbility(a, "death_mark", new Random(5)).AttackBonus;
        bonusA.Should().Be((int)(ab.AttackBonus * (1.0 + 20 * 0.03)));
        ClassAbilitySystem.UseAbility(b, "death_mark", new Random(5)).AttackBonus.Should().Be(bonusA, "Intelligence feeds nothing");
        var foe = G.Dummy();
        Run("death_mark", Hero(CharacterClass.Assassin), new List<Monster> { foe });
        foe.IsMarked.Should().BeTrue();
        foe.MarkedDuration.Should().Be(ab.Duration);
    }

    [Fact]
    public void ClassTexts136B_NocturaEmbrace_BonusesAndOneHiddenCrit()
    {
        var ab = Ab("noctura_embrace");
        En("ability.noctura_embrace.desc").Should().StartWith($"+{ab.AttackBonus} ATK (grows with level and CHA), +{ab.DefenseBonus} DEF (grows with level) for {ab.Duration} rounds")
            .And.Contain("your next attack crits").And.NotContain("2 attacks");
        ab.Type.Should().Be(ClassAbilitySystem.AbilityType.Buff);
        var h = Hero(CharacterClass.Assassin, level: 20); h.Charisma = 30;
        var r = ClassAbilitySystem.UseAbility(h, "noctura_embrace", new Random(5));
        r.AttackBonus.Should().Be((int)(ab.AttackBonus * (1.0 + 20 * 0.03) * (1.0 + 20 * 0.025)));
        r.DefenseBonus.Should().Be((int)(ab.DefenseBonus * (1.0 + 20 * 0.03)));
        var hero = Hero(CharacterClass.Assassin);
        hero.RemoveStatus(StatusEffect.Hidden);
        Run("noctura_embrace", hero);
        hero.HasStatus(StatusEffect.Hidden).Should().BeTrue("the cast hides the caster; the first stealth crit spends it");
    }
}
