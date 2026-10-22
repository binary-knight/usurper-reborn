using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.ClassTexts136Support;
using D = UsurperReborn.Tests.ClassTexts136DTests;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 X-E: the group E class texts (Magician) state the code. Each protection and attack base is the spell's own value
/// at level 0 and Intelligence 10; the hold texts name the GameConfig caps and the boss rule.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassTexts136ETests
{
    private static readonly int[] Spells = { 2, 6, 10, 18, 20, 21, 23, 25 };

    internal static readonly string[] Keys = Spells.Select(n => $"spell.magician.{n}.desc").Concat(new[]
    {
        "combat.spell_web_cast", "combat.spell_arcane_immunity_cast", "combat.spell_power_word_stun_cast", "combat.spell_wish_cast",
    }).ToArray();

    private static string Desc(int n) => En($"spell.magician.{n}.desc");
    private static SpellSystem.SpellResult Cast(int n, Character? c = null) => D.Cast(CharacterClass.Magician, n, false, c);
    private static string BossHold => $"a boss resists half the time, held {GameConfig.MaxStunDurationBoss} round";

    [Fact]
    public void ClassTexts136E_EveryChangedKey_HasFiveLanguagesWithEnsPlaceholdersAndNumbers()
    {
        FiveLanguages(Keys);
        foreach (var n in Spells) { DescLength($"spell.magician.{n}.desc"); SpellDescIsCode(CharacterClass.Magician, n); }
        Fits79("combat.spell_web_cast", "A Very Long Monster Name Here");
        Fits79("combat.spell_power_word_stun_cast", "A Very Long Monster Name Here");
        Fits79("combat.spell_arcane_immunity_cast", "A Long Teammate Name", 9999);
        Fits79("combat.spell_wish_cast", "A Long Teammate Name");
        Fits79("spell.magician.18.desc");
    }

    [Fact]
    public void ClassTexts136E_ArcaneShield_BaseAndScaling()
    {
        Desc(2).Should().Contain("Protection: +10, scales with level and INT");
        Cast(2).ProtectionBonus.Should().Be(10);
        Cast(2, D.Caster(CharacterClass.Magician, level: 30)).ProtectionBonus.Should().BeGreaterThan(10);
        Cast(2, D.Caster(CharacterClass.Magician, intel: 40)).ProtectionBonus.Should().BeGreaterThan(10);
    }

    [Fact]
    public void ClassTexts136E_Holds_NameTheCapAndTheBossRule()
    {
        Percent(GameConfig.BossStunResistChance).Should().Be(50, "half the time");
        Desc(6).Should().Contain($"cannot act, at most {GameConfig.MaxStunDurationNormal} rounds ({BossHold})");
        Desc(10).Should().Contain($"it loses its turns ({BossHold})").And.NotContain("reduced effectiveness");
        Desc(21).Should().Contain($"cannot act 3 rounds ({BossHold})");
        Cast(21).Duration.Should().Be(3);
        En("combat.spell_web_cast").Should().NotContain("traps");
        En("combat.spell_power_word_stun_cast").Should().NotContain("paralyzes");
    }

    [Fact]
    public void ClassTexts136E_TimeStop_NoExtraTurn()
    {
        var r = Cast(18);
        Desc(18).Should().Contain($"{r.Duration} rounds: +{r.AttackBonus} ATK and protection").And.Contain("dodge next attack").And.NotContain("Extra turn");
        r.ProtectionBonus.Should().Be(r.AttackBonus);
        r.SpecialEffect.Should().Be("timestop");
        Cast(18, D.Caster(CharacterClass.Magician, level: 30)).AttackBonus.Should().BeGreaterThan(r.AttackBonus, "up with level");
    }

    [Fact]
    public void ClassTexts136E_ArcaneImmunity_StatusEffectsNotMagic()
    {
        var r = Cast(20);
        Desc(20).Should().Contain($"immune to status effects. Protection: +{r.ProtectionBonus}, scales with level and INT").And.NotContain("lesser");
        r.SpecialEffect.Should().Be("immunity");
        Loc.GetIn("en", "combat.immune_status_effects").Should().Contain("status effects");
        En("combat.spell_arcane_immunity_cast").Should().NotContain("lesser");
    }

    [Fact]
    public void ClassTexts136E_SummonDemon_AttackAndTheDodge()
    {
        var r = Cast(23);
        Desc(23).Should().Contain($"Attack: +{r.AttackBonus}, scales with level. Dodge next attack.");
        r.SpecialEffect.Should().Be("demon");
        Cast(23, D.Caster(CharacterClass.Magician, level: 20)).AttackBonus.Should().Be(r.AttackBonus + 20);
    }

    [Fact]
    public void ClassTexts136E_Wish_StrengthAndDefenseNotAllStats()
    {
        Desc(25).Should().Contain("Attack +your Strength and defense +your Defense").And.NotContain("doubled");
        En("combat.spell_wish_cast").Should().NotContain("doubled");
        Cast(25).SpecialEffect.Should().Be("wish");
    }
}
