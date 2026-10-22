using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.ClassTexts136Support;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 X-D: the group D class texts (Cleric, Sage) state the code. Each damage range, heal range and protection base a
/// changed text gives is the spell's own roll at level 0, Intelligence 10 and Wisdom 10 with the low and the high draw,
/// and the stat a text names is the one that moves the number.
/// </summary>
[Collection("SharedGameSingletons")]
public class ClassTexts136DTests
{
    private const BindingFlags SF = BindingFlags.NonPublic | BindingFlags.Static;

    private static readonly int[] Cleric = { 2, 6, 7, 9, 10, 13, 14, 15, 17, 19, 20, 21, 23, 24, 25 };
    private static readonly int[] Sage = { 3, 6, 8, 11, 12, 13, 15, 16, 17, 18, 20, 21, 23, 24, 25 };

    internal static IEnumerable<string> Keys =>
        new[] { "ability.prayer_of_mending.desc", "ability.beacon_of_light.desc" }
            .Concat(Cleric.Select(n => $"spell.cleric.{n}.desc"))
            .Concat(Sage.Where(n => n != 6 && n != 18).Select(n => $"spell.sage.{n}.desc"));

    /// <summary>A draw that always gives the lowest (or the highest) roll and the middle of every variance.</summary>
    internal sealed class Edge : Random
    {
        private readonly bool _high;
        public Edge(bool high) { _high = high; }
        public override int Next() => 0;
        public override int Next(int maxValue) => _high ? Math.Max(0, maxValue - 1) : 0;
        public override int Next(int minValue, int maxValue) => _high ? Math.Max(minValue, maxValue - 1) : minValue;
        public override double NextDouble() => 0.5;
    }

    internal static Character Caster(CharacterClass cls, int level = 0, long intel = 10, long wis = 10)
    {
        var c = G.Hero(hp: 100_000, level: level, cls: cls);
        c.Class = cls; c.Level = level;
        c.Intelligence = intel; c.BaseIntelligence = intel; c.Wisdom = wis; c.BaseWisdom = wis;
        return c;
    }

    internal static SpellSystem.SpellResult Cast(CharacterClass cls, int slot, bool high, Character? caster = null)
    {
        var r = new SpellSystem.SpellResult { Success = true };
        typeof(SpellSystem).GetMethod("Execute" + cls + "Spell", SF)!
            .Invoke(null, new object?[] { caster ?? Caster(cls), slot, null, null, r, new Edge(high), 1.0f });
        return r;
    }

    private static (int Lo, int Hi) Range(string text, string label)
    {
        var m = Regex.Match(text, label + @":? (\d+)-(\d+)");
        m.Success.Should().BeTrue($"\"{text}\" gives a {label} range");
        return (int.Parse(m.Groups[1].Value), int.Parse(m.Groups[2].Value));
    }

    [Fact]
    public void ClassTexts136D_EveryChangedKey_HasFiveLanguagesWithEnsPlaceholdersAndNumbers()
    {
        FiveLanguages(Keys);
        foreach (var k in Keys) DescLength(k);
        AbilityDescIsCode("prayer_of_mending");
        AbilityDescIsCode("beacon_of_light");
        foreach (var n in Cleric) SpellDescIsCode(CharacterClass.Cleric, n);
        foreach (var n in Sage) SpellDescIsCode(CharacterClass.Sage, n);
    }

    [Theory]
    [InlineData(CharacterClass.Cleric, 6)]
    [InlineData(CharacterClass.Cleric, 9)]
    [InlineData(CharacterClass.Cleric, 13)]
    [InlineData(CharacterClass.Cleric, 17)]
    [InlineData(CharacterClass.Cleric, 19)]
    [InlineData(CharacterClass.Cleric, 21)]
    [InlineData(CharacterClass.Cleric, 24)]
    [InlineData(CharacterClass.Cleric, 25)]
    [InlineData(CharacterClass.Sage, 3)]
    [InlineData(CharacterClass.Sage, 8)]
    [InlineData(CharacterClass.Sage, 11)]
    [InlineData(CharacterClass.Sage, 12)]
    [InlineData(CharacterClass.Sage, 15)]
    [InlineData(CharacterClass.Sage, 17)]
    [InlineData(CharacterClass.Sage, 21)]
    [InlineData(CharacterClass.Sage, 23)]
    [InlineData(CharacterClass.Sage, 25)]
    public void ClassTexts136D_DamageRange_IsTheSpellsRoll(CharacterClass cls, int slot)
    {
        string text = En($"spell.{cls.ToString().ToLowerInvariant()}.{slot}.desc");
        var (lo, hi) = Range(text, "Damage");
        Cast(cls, slot, high: false).Damage.Should().Be(lo, $"{cls} {slot} low roll");
        Cast(cls, slot, high: true).Damage.Should().Be(hi, $"{cls} {slot} high roll");
        if (cls == CharacterClass.Cleric)
        {
            text.Should().Contain("level, INT and WIS");
            Cast(cls, slot, true, Caster(cls, intel: 30)).Damage.Should().BeGreaterThan(hi, "Intelligence raises it");
            Cast(cls, slot, true, Caster(cls, wis: 30)).Damage.Should().BeGreaterThan(hi, "Wisdom raises it");
            Cast(cls, slot, true, Caster(cls, level: 20)).Damage.Should().BeGreaterThan(hi, "level raises it");
        }
    }

    [Theory]
    [InlineData(CharacterClass.Cleric, 2)]
    [InlineData(CharacterClass.Cleric, 7)]
    [InlineData(CharacterClass.Cleric, 10)]
    [InlineData(CharacterClass.Cleric, 14)]
    [InlineData(CharacterClass.Cleric, 15)]
    [InlineData(CharacterClass.Cleric, 20)]
    [InlineData(CharacterClass.Sage, 13)]
    [InlineData(CharacterClass.Sage, 16)]
    [InlineData(CharacterClass.Sage, 20)]
    [InlineData(CharacterClass.Sage, 24)]
    public void ClassTexts136D_Protection_IsTheBaseAndGrowsWithLevelAndIntelligenceNotWisdom(CharacterClass cls, int slot)
    {
        string text = En($"spell.{cls.ToString().ToLowerInvariant()}.{slot}.desc");
        var m = Regex.Match(text, @"[Pp]rotection:? \+(\d+), scales with level and INT");
        m.Success.Should().BeTrue(text);
        int b = int.Parse(m.Groups[1].Value);
        text.Should().NotContain("level and WIS");
        Cast(cls, slot, false).ProtectionBonus.Should().Be(b, $"{cls} {slot} base");
        Cast(cls, slot, false, Caster(cls, level: 30)).ProtectionBonus.Should().BeGreaterThan(b, "level raises it");
        Cast(cls, slot, false, Caster(cls, intel: 40)).ProtectionBonus.Should().BeGreaterThan(b, "Intelligence raises it");
        Cast(cls, slot, false, Caster(cls, wis: 60)).ProtectionBonus.Should().Be(b, "Wisdom does not");
    }

    [Fact]
    public void ClassTexts136D_HolyBonus_IsHalfTheRoll()
    {
        foreach (var n in new[] { 9, 19 }) En($"spell.cleric.{n}.desc").Should().Contain("+50% damage vs undead and demons").And.NotContain("Double");
        En("spell.cleric.24.desc").Should().Contain("Undead and demons take half again").And.NotContain("Double");
        foreach (var n in new[] { 21, 25 }) En($"spell.cleric.{n}.desc").Should().Contain("Undead and demons also take half the full roll").And.NotContain("Double");
        var hero = Caster(CharacterClass.Cleric, level: 10);
        var foe = G.Dummy(); foe.MonsterClass = MonsterClass.Undead;
        var rig = G.NewRig(hero, new Random(3), null, foe);
        long hp0 = foe.HP;
        var method = typeof(CombatEngine).GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).First(m => m.Name == "HandleSpecialSpellEffectOnMonster");
        var args = method.GetParameters().Select(p => p.ParameterType == typeof(string) ? (object?)"holy"
            : p.ParameterType == typeof(Monster) ? foe
            : p.ParameterType == typeof(Character) ? hero
            : p.ParameterType == typeof(CombatResult) ? rig.Result
            : p.ParameterType == typeof(long) ? 100L
            : p.ParameterType == typeof(int) ? (object)100
            : p.HasDefaultValue ? p.DefaultValue : null).ToArray();
        var ret = method.Invoke(rig.Engine, args);
        if (ret is System.Threading.Tasks.Task t) t.GetAwaiter().GetResult();
        (hp0 - foe.HP).Should().Be(50, "an undead takes half the spell's roll again");
    }

    [Theory]
    [InlineData(13)]
    [InlineData(21)]
    [InlineData(25)]
    public void ClassTexts136D_ClericAreaSpells_NameTheSplit(int slot)
    {
        En($"spell.cleric.{slot}.desc").Should().MatchRegex(@"to the first( foe)?, then 75%, 50%,? (and )?25%");
        SpellSystem.GetSpellInfo(CharacterClass.Cleric, slot)!.IsMultiTarget.Should().BeTrue();
    }

    [Fact]
    public void ClassTexts136D_SageSpells_StatedEffects()
    {
        En("spell.sage.12.desc").Should().Contain("to the first, then 75%, 50%, 25%");
        SpellSystem.GetSpellInfo(CharacterClass.Sage, 12)!.IsMultiTarget.Should().BeTrue();
        En("spell.sage.11.desc").Should().Contain("heals half");
        En("spell.sage.15.desc").Should().Contain("heals half");
        Cast(CharacterClass.Sage, 11, false).SpecialEffect.Should().Be("drain");
        Cast(CharacterClass.Sage, 15, false).SpecialEffect.Should().Be("drain");
        En("spell.sage.21.desc").Should().Contain("feared for 2 rounds and loses 25% of its armor");
        En("spell.sage.23.desc").Should().Contain("stuns for 2 rounds (a boss resists half the time and is held 1 round)");
        Percent(GameConfig.BossStunResistChance).Should().Be(50);
        GameConfig.MaxStunDurationBoss.Should().Be(1);
        En("spell.sage.25.desc").Should().Contain("under 20% HP that is not a boss dies 30% of the time").And.NotContain("drain");
        var heal = Range(En("spell.sage.24.desc"), "heal");
        Cast(CharacterClass.Sage, 24, false).Healing.Should().Be(heal.Lo);
        Cast(CharacterClass.Sage, 24, true).Healing.Should().Be(heal.Hi);
        En("spell.sage.24.desc").Should().Contain("scales with WIS");
        Cast(CharacterClass.Sage, 24, false, Caster(CharacterClass.Sage, wis: 40)).Healing.Should().BeGreaterThan(heal.Lo);
    }

    [Fact]
    public void ClassTexts136D_DivineAvatar_DefenseAlsoWithIntelligence()
    {
        En("spell.cleric.23.desc").Should().Contain("ATK and DEF +55, scales with level; DEF also with INT");
        var r = Cast(CharacterClass.Cleric, 23, false);
        r.AttackBonus.Should().Be(55);
        r.ProtectionBonus.Should().Be(55);
        var smart = Cast(CharacterClass.Cleric, 23, false, Caster(CharacterClass.Cleric, intel: 40));
        smart.AttackBonus.Should().Be(55, "attack takes the level only");
        smart.ProtectionBonus.Should().BeGreaterThan(55);
    }

    [Fact]
    public void ClassTexts136D_PrayerAndBeacon_HealAtOnce()
    {
        En("ability.prayer_of_mending.desc").Should().Contain("at once").And.NotContain("over time");
        En("ability.beacon_of_light.desc").Should().Contain("healing them at once").And.NotContain("slowly");
        var hero = Hero(CharacterClass.Cleric); hero.HP = 1000;
        var c = Run("prayer_of_mending", hero);
        c.Result.Healing.Should().BeGreaterThan(0);
        hero.HP.Should().BeGreaterThan(1000, "healed on the cast");
        hero.HasStatus(StatusEffect.Regenerating).Should().BeFalse("nothing over time");
    }

    [Fact]
    public void ClassTexts136D_ScholarsMarkAndUnveil_CodeTextHasTheLibraryClause()
    {
        foreach (var n in new[] { 6, 18 })
            SpellSystem.GetSpellInfo(CharacterClass.Sage, n)!.Description.Should().Contain($"+{GameConfig.SageLibraryMarkBonusPercent}% while Library study sharpens the mark")
                .And.Contain($"+{GameConfig.MarkedBonusPercent}% damage");
    }
}
