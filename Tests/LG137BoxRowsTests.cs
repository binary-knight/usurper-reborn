using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 LG: every fixed row of the two boxed combat menus (single monster, dungeon) stays inside the 41 column box in
/// all five languages; a text wider than the box continues on a row inside it.
/// </summary>
[Collection("SharedGameSingletons")]
public class LG137BoxRows
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int BoxWidth = 41;

    public static IEnumerable<object[]> Langs() => new[] { "en", "es", "fr", "hu", "it" }.Select(l => new object[] { l });

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static Character Hero(CharacterClass cls) => new Character
    {
        Name1 = "tester", Name2 = "Tester", Class = cls, Level = 100, HP = 500, MaxHP = 500,
        AI = CharacterAI.Human, Healing = 3, HerbHealing = 2, PoisonVials = 2,
        CurrentCombatStamina = 1000, Mana = 5000, MaxMana = 5000,
        Quickbar = new List<string?>(new string?[9]),
    };

    private static string Render(bool dungeon, CharacterClass cls, bool aid, bool canHeal, GodDomain miracle)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        var engine = new CombatEngine(term);
        var hero = Hero(cls);
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.ScreenReaderMode = false;
            if (!dungeon)
            {
                var monster = new Monster { Name = "Kobold", Level = 3, HP = 10, MaxHP = 10, IsActive = true };
                typeof(CombatEngine).GetMethod("ShowCombatMenuStandard", F)!
                    .Invoke(engine, new object?[] { hero, monster, null, false });
            }
            else
            {
                var info = new List<(string, string, bool)>();
                typeof(CombatEngine).GetMethod("ShowDungeonCombatMenuStandard", F)!
                    .Invoke(engine, new object?[] { hero, aid, canHeal, info, false, miracle });
            }
        }
        finally { GameConfig.ScreenReaderMode = sr; }
        term.StreamWriterInternal?.Flush();
        return Strip(Encoding.UTF8.GetString(output.ToArray()));
    }

    private static List<string> Rows(string lang, bool dungeon)
    {
        var rows = new List<string>();
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            foreach (CharacterClass cls in new[] { CharacterClass.Warrior, CharacterClass.Ranger, CharacterClass.Barbarian, CharacterClass.Assassin, CharacterClass.Paladin })
            {
                if (!dungeon)
                    rows.AddRange(Render(false, cls, false, false, GodDomain.None).Replace("\r", "").Split('\n').Where(l => l.Length > 0));
                else
                    foreach (var aid in new[] { false, true })
                        foreach (var miracle in Enum.GetValues(typeof(GodDomain)).Cast<GodDomain>())
                            rows.AddRange(Render(true, cls, aid, aid && miracle == GodDomain.None, miracle).Replace("\r", "").Split('\n').Where(l => l.Length > 0));
            }
        }
        finally { GameConfig.Language = prev; }
        return rows;
    }

    [Theory]
    [MemberData(nameof(Langs))]
    public void SingleMonsterMenu_EveryRowIsInsideTheBox(string lang)
    {
        var rows = Rows(lang, false);
        rows.Should().Contain(r => r.StartsWith("║ [F] "));
        foreach (var r in rows) UIHelper.VisibleLength(r).Should().Be(BoxWidth, $"{lang} row \"{r}\"");
    }

    [Theory]
    [MemberData(nameof(Langs))]
    public void DungeonMenu_EveryRowIsInsideTheBox(string lang)
    {
        var rows = Rows(lang, true);
        rows.Should().Contain(r => r.StartsWith("║ [R] "));
        foreach (var r in rows) UIHelper.VisibleLength(r).Should().Be(BoxWidth, $"{lang} row \"{r}\"");
    }

    [Fact]
    public void HungarianFightToTheDeath_WrapsOntoAContinuationRow()
    {
        var rows = Rows("hu", false);
        int at = rows.FindIndex(r => r.StartsWith("║ [F] "));
        at.Should().BeGreaterOrEqualTo(0);
        rows[at + 1].Should().StartWith("║     ", "the [F] text continues under itself inside the box");
        rows[at + 1].Should().NotContain("[");
    }
}
