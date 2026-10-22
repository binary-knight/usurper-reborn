using System;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.ClassTexts136Support;
using D = UsurperReborn.Tests.ClassTexts136DTests;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 DK: Death Kiss (Sage 25) and Power Word: Kill (Magician 24) share the "death" case of
/// HandleSpecialSpellEffectOnMonster. The instant kill spares a mini boss as well as a boss, and both texts say so.
/// The case itself is called with the target's HP above the spell's damage, so a spared target shows no kill line and an
/// unchanged HP.
/// </summary>
public class DeathSpell136Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>A Random whose rolls are all 0: every percent roll lands under 30.</summary>
    private sealed class LowRoll : Random
    {
        public override int Next(int maxValue) => 0;
        public override int Next(int minValue, int maxValue) => minValue;
        public override double NextDouble() => 0.0;
    }

    public static readonly object[][] Spells =
    {
        new object[] { CharacterClass.Sage, 25 },
        new object[] { CharacterClass.Magician, 24 },
    };

    public static System.Collections.Generic.IEnumerable<object[]> SpellsByKind()
    {
        foreach (var s in Spells)
            foreach (var kind in new[] { "plain", "mini", "boss", "plain21" })
                yield return new object[] { s[0], s[1], kind, false };
        foreach (var kind in new[] { "plain", "mini", "boss", "plain21" })
            yield return new object[] { CharacterClass.Sage, 25, kind, true };
    }

    private static Monster Foe(string kind, long damage)
    {
        var m = G.Dummy();
        m.MaxHP = 10_000_000L;
        m.HP = kind == "plain21" ? 2_100_000L : 1_000_000L;
        m.HP.Should().BeGreaterThan(damage * 2, "HP is set above the spell's damage");
        if (kind == "mini") m.IsMiniBoss = true;
        if (kind == "boss") m.IsBoss = true;
        return m;
    }

    [Theory]
    [MemberData(nameof(SpellsByKind))]
    public void DeathSpell136_TheCase_KillsOnlyAPlainMonsterUnderTwentyPercent(CharacterClass cls, int slot, string kind, bool teammate)
    {
        var caster = D.Caster(cls, level: 50);
        var cast = D.Cast(cls, slot, true, caster);
        cast.SpecialEffect.Should().Be("death");
        var foe = Foe(kind, cast.Damage);
        var hero = D.Caster(CharacterClass.Warrior, level: 50);
        var team = new System.Collections.Generic.List<Character>();
        if (teammate) team.Add(caster);
        var rig = G.NewRig(teammate ? hero : caster, new LowRoll(), team, foe);
        long hp0 = foe.HP;

        typeof(CombatEngine).GetMethod("HandleSpecialSpellEffectOnMonster", F)!
            .Invoke(rig.Engine, new object?[] { foe, cast.SpecialEffect, cast.Duration, caster, cast.Damage, rig.Result });
        string shown = rig.Shown();

        if (kind == "plain")
        {
            foe.HP.Should().Be(0, "a plain monster under 20% dies on a roll under 30");
            shown.Should().Contain("remaining life force", "the kill line");
            rig.Result.CombatLog.Should().Contain(l => l.Contains("Death spell instant kill"));
        }
        else
        {
            foe.HP.Should().Be(hp0, $"the case spares a {kind} target: HP unchanged by the case");
            shown.Should().NotContain("remaining life force", "no kill line");
            rig.Result.CombatLog.Should().NotContain(l => l.Contains("Death spell instant kill"));
            if (kind != "plain21") shown.Should().BeEmpty("a spared target gets no resist line either");
        }
    }

    [Theory]
    [InlineData(CharacterClass.Sage, 25)]
    [InlineData(CharacterClass.Magician, 24)]
    public void DeathSpell136_Text_NamesTheMiniBossInFiveLanguagesAndEqualsTheCode(CharacterClass cls, int slot)
    {
        string key = $"spell.{cls.ToString().ToLowerInvariant()}.{slot}.desc";
        FiveLanguages(new[] { key });
        DescLength(key);
        SpellDescIsCode(cls, slot);
        En(key).Should().Contain("that is not a boss or mini boss dies 30% of the time");
        foreach (var lang in Langs)
            Loc.GetIn(lang, key).Should().Contain("mini", $"{key} {lang} names the mini boss");
    }
}
