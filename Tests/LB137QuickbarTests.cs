using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.7 LB: every combat quickbar row fits. Screen reader rows are at most 79 columns in all five languages (the
/// requirement and the not-available note go on an indented continuation row); boxed rows stay inside the 41 column
/// box. Every class, every slot (all its abilities and spells, nine to a bar), with resources spent and with resources
/// full, in both the single monster menu and the dungeon menu.
/// </summary>
[Collection("SharedGameSingletons")]
public class LB137Quickbar
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int MaxWidth = 79;
    private const int BoxWidth = 41;

    private static readonly string[] SrLanguages = { "en", "es", "fr", "hu", "it" };

    public static IEnumerable<object[]> SrLangs() => SrLanguages.Select(l => new object[] { l });
    public static IEnumerable<object[]> BoxLangs() => new[] { "en", "hu" }.Select(l => new object[] { l });

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static IEnumerable<List<string?>> Bars(CharacterClass cls)
    {
        var probe = new Character { Name1 = "t", Name2 = "T", Class = cls, Level = 100 };
        var ids = ClassAbilitySystem.GetAvailableAbilities(probe).OrderBy(a => a.LevelRequired).Select(a => a.Id).ToList();
        foreach (var s in SpellSystem.GetAllSpellsForClass(cls))
            ids.Add(SpellSystem.GetQuickbarId(s.Level));
        for (int i = 0; i < ids.Count; i += 9)
        {
            var bar = new List<string?>(new string?[9]);
            for (int j = 0; j < 9 && i + j < ids.Count; j++) bar[j] = ids[i + j];
            yield return bar;
        }
    }

    private enum Menu { SingleSr, DungeonSr, SingleBox, DungeonBox }

    private static string Render(Menu menu, CharacterClass cls, List<string?> bar, bool spent)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        var engine = new CombatEngine(term);
        var hero = new Character
        {
            Name1 = "tester", Name2 = "Tester", Class = cls, Level = 100, HP = 500, MaxHP = 500,
            AI = CharacterAI.Human, Healing = 3, Quickbar = bar,
            CurrentCombatStamina = spent ? 0 : 1000, Mana = spent ? 0 : 5000, MaxMana = 5000,
        };
        var monster = new Monster { Name = "Kobold", Level = 3, HP = 10, MaxHP = 10, IsActive = true };
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.ScreenReaderMode = menu is Menu.SingleSr or Menu.DungeonSr;
            switch (menu)
            {
                case Menu.SingleSr:
                    typeof(CombatEngine).GetMethod("ShowCombatMenuScreenReader", F)!
                        .Invoke(engine, new object?[] { hero, monster, null, false });
                    break;
                case Menu.SingleBox:
                    typeof(CombatEngine).GetMethod("ShowCombatMenuStandard", F)!
                        .Invoke(engine, new object?[] { hero, monster, null, false });
                    break;
                default:
                    var info = (System.Collections.IEnumerable)typeof(CombatEngine).GetMethod("GetClassSpecificActions", F)!
                        .Invoke(engine, new object?[] { hero })!;
                    var typed = info.Cast<ValueTuple<string, string, bool>>().ToList();
                    string name = menu == Menu.DungeonSr ? "ShowDungeonCombatMenuScreenReader" : "ShowDungeonCombatMenuStandard";
                    typeof(CombatEngine).GetMethod(name, F)!
                        .Invoke(engine, new object?[] { hero, false, false, typed, false, GodDomain.None });
                    break;
            }
        }
        finally { GameConfig.ScreenReaderMode = sr; }
        term.StreamWriterInternal?.Flush();
        return Strip(Encoding.UTF8.GetString(output.ToArray()));
    }

    /// <summary>The quickbar rows of a screen reader menu: from the header to the next blank row.</summary>
    private static List<string> SrQuickbarRows(string text)
    {
        var lines = text.Replace("\r", "").Split('\n').ToList();
        var rows = new List<string>();
        var header = Loc.Get("combat.menu_quickbar");
        int at = lines.FindIndex(l => l.Trim() == header.Trim());
        if (at < 0) return rows;
        for (int i = at + 1; i < lines.Count && lines[i].Trim().Length > 0; i++) rows.Add(lines[i]);
        return rows;
    }

    /// <summary>The quickbar rows of a boxed menu: the section after a rule whose first row is a [digit] slot, up to the next rule.</summary>
    private static List<string> BoxQuickbarRows(string text)
    {
        var rows = new List<string>();
        var lines = text.Replace("\r", "").Split('\n');
        bool inBar = false;
        for (int i = 0; i < lines.Length; i++)
        {
            var l = lines[i];
            if (l.StartsWith("╠") || l.StartsWith("╚")) { inBar = false; if (i + 1 < lines.Length && Regex.IsMatch(lines[i + 1], "^║ \\[[1-9]\\] ")) inBar = true; continue; }
            if (inBar && l.StartsWith("║")) rows.Add(l);
        }
        return rows;
    }

    private static async Task<List<string>> AllRows(string lang, Menu menu, bool box)
    {
        var all = new List<string>();
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = lang;
            foreach (CharacterClass cls in Enum.GetValues(typeof(CharacterClass)))
                foreach (var bar in Bars(cls))
                    foreach (bool spent in new[] { true, false })
                    {
                        string text = Render(menu, cls, bar, spent);
                        if (box)
                            all.AddRange(BoxQuickbarRows(text));
                        else
                            all.AddRange(SrQuickbarRows(text));
                    }
        }
        finally { GameConfig.Language = prev; }
        await Task.CompletedTask;
        return all;
    }

    [Theory]
    [MemberData(nameof(SrLangs))]
    public async Task ScreenReaderRows_SingleMonsterMenu_FitIn79(string lang)
    {
        var rows = await AllRows(lang, Menu.SingleSr, false);
        rows.Should().NotBeEmpty();
        foreach (var r in rows) UIHelper.VisibleLength(r).Should().BeLessOrEqualTo(MaxWidth, $"{lang} row \"{r}\"");
    }

    [Theory]
    [MemberData(nameof(SrLangs))]
    public async Task ScreenReaderRows_DungeonMenu_FitIn79(string lang)
    {
        var rows = await AllRows(lang, Menu.DungeonSr, false);
        rows.Should().NotBeEmpty();
        foreach (var r in rows) UIHelper.VisibleLength(r).Should().BeLessOrEqualTo(MaxWidth, $"{lang} row \"{r}\"");
    }

    [Theory]
    [MemberData(nameof(BoxLangs))]
    public async Task BoxedRows_SingleMonsterMenu_StayInTheBox(string lang)
    {
        var rows = await AllRows(lang, Menu.SingleBox, true);
        rows.Should().Contain(r => r.StartsWith("║ [1]"));
        foreach (var r in rows) UIHelper.VisibleLength(r).Should().Be(BoxWidth, $"{lang} row \"{r}\"");
    }

    [Theory]
    [MemberData(nameof(BoxLangs))]
    public async Task BoxedRows_DungeonMenu_StayInTheBox(string lang)
    {
        var rows = await AllRows(lang, Menu.DungeonBox, true);
        rows.Should().Contain(r => r.StartsWith("║ [1]"));
        foreach (var r in rows) UIHelper.VisibleLength(r).Should().Be(BoxWidth, $"{lang} row \"{r}\"");
    }

    [Fact]
    public async Task ShamanEnchantRows_WrapOntoAContinuationRow()
    {
        var rows = await AllRows("hu", Menu.SingleSr, false);
        rows.Should().Contain(r => r.StartsWith("      ") && r.Trim().Length > 0, "a long row continues on an indented row");
    }
}
