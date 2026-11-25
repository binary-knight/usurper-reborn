using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.15: the death screen tells the truth for each permadeath setting.
/// With online permadeath on, a death never reaches the Veil menu: one life is spent automatically,
/// and at 0 lives the divine-restore lines say the next death erases the character, which it does.
/// With it off (and in single player) a death reaches the Veil menu and nothing erases anyone; the
/// menu used to say "The next death will erase you" after five deaths. It now shows only the count.
/// </summary>
[Collection("SharedGameSingletons")]
public class DeathCount1315Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };

    private static string Plain(string s) => Regex.Replace(s, "\u001b\\[[0-9;]*[A-Za-z]", "");

    private static string Render(Action<CombatEngine> draw)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        draw(new CombatEngine(term));
        term.StreamWriterInternal?.Flush();
        return Plain(Encoding.UTF8.GetString(output.ToArray()));
    }

    private static string MenuCountLine(int deaths) => Render(engine =>
        typeof(CombatEngine).GetMethod("ShowDeathCountWarning", F)!.Invoke(engine, new object?[]
        {
            new Character { Name2 = "Tester", PlaythroughDeaths = deaths },
        }));

    private static void WithSetting(bool permadeathOn, string lang, Action body)
    {
        bool savedSwitch = GameConfig.OnlinePermadeathEnabled;
        string savedLang = GameConfig.Language;
        try
        {
            GameConfig.OnlinePermadeathEnabled = permadeathOn;
            GameConfig.Language = lang;
            body();
        }
        finally
        {
            GameConfig.OnlinePermadeathEnabled = savedSwitch;
            GameConfig.Language = savedLang;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void TheVeilMenuLine_IsTheCountOnly_AndNeverSpeaksOfErasure(bool permadeathOn)
    {
        int max = GameConfig.MaxPlaythroughDeaths;
        foreach (var lang in Languages)
            WithSetting(permadeathOn, lang, () =>
            {
                foreach (int deaths in new[] { 0, 1, max - 2, max - 1, max, max + 3 })
                {
                    string text = MenuCountLine(deaths);
                    text.Should().Contain(Loc.Get("combat.deaths_this_playthrough", deaths), $"{lang} at {deaths}");
                    text.Should().NotContain(Loc.Get("combat.final_death_warning"))
                        .And.NotContain(Loc.Get("combat.final_death_times", deaths))
                        .And.NotContain(Loc.Get("combat.deaths_remaining_one", deaths, max, 1))
                        .And.NotContain(Loc.Get("combat.deaths_remaining_many", deaths, max, 2));
                }
            });
    }

    [Fact]
    public void WithPermadeathOn_TheZeroLivesScreen_StillSaysTheNextDeathErases()
    {
        foreach (var lang in Languages)
            WithSetting(true, lang, () =>
            {
                string atZero = Render(engine => typeof(CombatEngine).GetMethod("ShowDivineRestore", F)!
                    .Invoke(engine, new object?[] { 250, 500L, 0, 3 }));
                atZero.Should().Contain(Loc.Get("death.final_warning2"), lang);

                string withLives = Render(engine => typeof(CombatEngine).GetMethod("ShowDivineRestore", F)!
                    .Invoke(engine, new object?[] { 250, 500L, 2, 3 }));
                withLives.Should().Contain(Loc.Get("death.rez_remaining", 2, 3)).And.NotContain(Loc.Get("death.final_warning2"));
            });
    }

    private static string Src()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "UsurperReborn.sln"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "Scripts", "Systems", "CombatEngine.cs"));
    }

    [Fact]
    public void TheVeilMenu_IsReachedOnlyWhereNoDeathErases_AndTheEraseOnlyWherePermadeathIsOn()
    {
        string src = Src();
        // the only erase on death: online, permadeath on, no lives left
        Regex.Matches(src, @"await HandleExcessiveDeathsPermadeath\(result\);").Count.Should().Be(1);
        src.Should().Contain("bool isOnlinePermadeathMode = UsurperRemake.BBS.DoorMode.IsOnlineMode && GameConfig.OnlinePermadeathEnabled;");
        src.Should().Contain("if (isOnlinePermadeathMode && result.Player.Resurrections <= 0)");

        // the Veil menu (and its count line) is the else of the online-permadeath branch
        int branch = src.IndexOf("if (UsurperRemake.BBS.DoorMode.IsOnlineMode && GameConfig.OnlinePermadeathEnabled)", StringComparison.Ordinal);
        int menu = src.IndexOf("resurrectionResult = await PresentResurrectionChoices(result);", StringComparison.Ordinal);
        branch.Should().BePositive();
        menu.Should().BeGreaterThan(branch);
        src.Substring(branch, menu - branch).Should().Contain("ShowDivineRestore(").And.Contain("else");
        Regex.Matches(src, @"PresentResurrectionChoices\(result\)").Count.Should().Be(1);
        Regex.Matches(src, @"ShowDeathCountWarning\(player\);").Count.Should().Be(1);
    }

    [Fact]
    public void TheNewLine_IsInEveryLanguage()
    {
        foreach (var lang in Languages)
            Loc.GetIn(lang, "combat.deaths_this_playthrough", 7).Should().Contain("7").And.NotContain("combat.deaths_this_playthrough");
    }
}
