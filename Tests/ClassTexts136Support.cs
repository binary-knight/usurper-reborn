using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using G = UsurperReborn.Tests.HitRoll128Golden;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.6 X (class texts): shared checks for the ClassTexts136 test classes. Each group's class lists the keys it
/// changed; every changed key has all five languages, the same placeholders as en and the same numbers as en, and a
/// changed desc stays the code Description.
/// </summary>
internal static class ClassTexts136Support
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    internal static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    internal static string En(string key) => Loc.GetIn("en", key);

    private static string[] Placeholders(string s) =>
        Regex.Matches(s, @"\{(\d+)[^}]*\}").Select(m => m.Groups[1].Value).Distinct().OrderBy(x => x).ToArray();

    /// <summary>The numbers in a text, in order of value (digits only; a percent sign or a thousands mark is left out).</summary>
    internal static string[] Numbers(string s) =>
        Regex.Matches(Regex.Replace(s, @"\{\d+[^}]*\}", " "), @"\d+").Select(m => m.Value).OrderBy(x => x).ToArray();

    /// <summary>Every key has a text in all five languages, with the placeholders and the numbers of en.</summary>
    internal static void FiveLanguages(IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            string en = En(key);
            foreach (var lang in Langs)
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{key} has a {lang} text");
                string t = Loc.GetIn(lang, key);
                t.Should().NotBeNullOrWhiteSpace($"{key} {lang}");
                Placeholders(t).Should().Equal(Placeholders(en), $"{key} {lang} has the placeholders of en");
                Numbers(t).Should().Equal(Numbers(en), $"{key} {lang} states the numbers of en: \"{t}\"");
                Regex.IsMatch(t, "[\u2013\u2014]").Should().BeFalse($"{key} {lang} has no en or em dash");
            }
        }
    }

    /// <summary>A changed ability desc equals the code Description (the generated wiki tables read the Description).</summary>
    internal static void AbilityDescIsCode(string id) =>
        En($"ability.{id}.desc").Should().Be(ClassAbilitySystem.GetAbility(id)!.Description, $"{id}: en desc equals the code Description");

    /// <summary>A changed spell desc equals the SpellInfo Description.</summary>
    internal static void SpellDescIsCode(CharacterClass cls, int level)
    {
        var info = SpellSystem.GetSpellInfo(cls, level)!;
        En($"spell.{cls.ToString().ToLowerInvariant()}.{level}.desc").Should().Be(info.Description, $"{cls} {level}: en desc equals the code text");
    }

    /// <summary>
    /// A desc is written whole and wrapped by the terminal (the ability and spell menus), or cut to the room left (the
    /// dungeon skill toggles), so it has no row width. A changed desc stays within the longest desc the menus showed at
    /// c4c6d02f: 173 characters in en, 210 in the other languages (es 210, fr 206, hu 205, it 202).
    /// </summary>
    internal static void DescLength(string key)
    {
        Loc.GetIn("en", key).Length.Should().BeLessOrEqualTo(173, $"{key} en");
        foreach (var lang in new[] { "es", "fr", "hu", "it" })
            Loc.GetIn(lang, key).Length.Should().BeLessOrEqualTo(210, $"{key} {lang}");
    }

    /// <summary>A combat line, formatted with the given arguments, fits 79 columns in en and hu.</summary>
    internal static void Fits79(string key, params object[] args)
    {
        foreach (var lang in new[] { "en", "hu" })
            Loc.GetIn(lang, key, args).Length.Should().BeLessOrEqualTo(79, $"{key} {lang}: \"{Loc.GetIn(lang, key, args)}\"");
    }

    internal sealed class Cast
    {
        public G.Rig Rig = null!;
        public ClassAbilityResult Result = null!;
        public Character Actor = null!;
        public long[] Lost = Array.Empty<long>();
    }

    internal static Character Hero(CharacterClass cls, int level = 50)
    {
        var c = G.Hero(hp: 1_000_000, level: level, cls: cls);
        c.Class = cls;
        c.TempAttackBonus = 0; c.TempAttackBonusDuration = 0;
        c.TempDefenseBonus = 0; c.TempDefenseBonusDuration = 0;
        return c;
    }

    /// <summary>One cast of an ability by a player hero through ApplyAbilityEffectsMultiMonster on a seeded engine.</summary>
    internal static Cast Run(string id, Character hero, List<Monster>? monsters = null, int seed = 136, List<Character>? team = null)
    {
        bool saved = GameConfig.PlayerHitRollEnabled;
        try
        {
            GameConfig.PlayerHitRollEnabled = false;
            var foes = monsters ?? new List<Monster> { G.Dummy() };
            var rig = G.NewRig(hero, new Random(seed), team, foes.ToArray());
            var hp0 = foes.Select(m => m.HP).ToArray();
            var r = ClassAbilitySystem.UseAbility(hero, id, new Random(seed));
            r.Success.Should().BeTrue(id);
            ((Task)typeof(CombatEngine).GetMethod("ApplyAbilityEffectsMultiMonster", F)!
                .Invoke(rig.Engine, new object?[] { hero, foes[0], foes, r, rig.Result })!).GetAwaiter().GetResult();
            return new Cast { Rig = rig, Result = r, Actor = hero, Lost = foes.Select((m, i) => hp0[i] - m.HP).ToArray() };
        }
        finally { GameConfig.PlayerHitRollEnabled = saved; }
    }

    /// <summary>The code value of a percent multiplier, as the text states it (1.45 is 45).</summary>
    internal static int PercentOver(double mult) => (int)Math.Round((mult - 1.0) * 100);

    internal static int Percent(double share) => (int)Math.Round(share * 100);
}
