using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.4: layout leftovers. News rows wrap at 79, the tame flavor wraps at 79, a French mark keeps to
/// the word before it, /who locations keep their spacing, the hu settlement hint has no dash character,
/// and the fight's opening phrase reaches each group member in their language.
/// </summary>
[Collection("SharedGameSingletons")]
public class LayoutLeftovers124Tests
{
    private const string LongName = "Eszterhazy Kalandor Bajnoka Ur";
    private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static List<string> ShownRows(Action<TerminalEmulator> write)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        write(term);
        term.StreamWriterInternal?.Flush();
        var text = Strip(Encoding.UTF8.GetString(output.ToArray())).Replace("\r", "");
        if (text.EndsWith("\n")) text = text.Substring(0, text.Length - 1);
        return text.Split('\n').ToList();
    }

    private static void AllFit(IEnumerable<string> rows, string what)
    {
        foreach (var row in rows)
            UIHelper.VisibleLength(row).Should().BeLessThanOrEqualTo(79, $"{what}: \"{row}\"");
    }

    // ---------- 1. news rows ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void NewsBoard_TheSealLineWraps_UnderTheTextAfterTheStamp(string lang)
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        string seal = Loc.GetIn(lang, "dungeon.news_seal_found", LongName, 7);

        // the shared online board: "  [MM/dd HH:mm] " in gray, then the message
        string stamp = "  [10/02 12:34] ";
        (stamp.Length + seal.Length).Should().BeGreaterThan(79, "the case being fixed overflows");
        var online = ShownRows(t => OnlineChatSystem.WriteNewsEntry(t, stamp, seal, "green"));
        CheckWrapped(online, stamp, seal, $"online news {lang}");

        // the local board: "  [HH:mm] message"
        string local = "[12:34] " + seal + " " + seal;
        var rows = NewsLocation.NewsRows(local);
        CheckWrapped(rows, "  [12:34] ", local.Substring(8), $"local news {lang}");
    }

    private static void CheckWrapped(List<string> rows, string prefix, string text, string what)
    {
        rows.Count.Should().BeGreaterThan(1, what);
        AllFit(rows, what);
        rows[0].Should().StartWith(prefix, what);
        string indent = new string(' ', prefix.Length);
        foreach (var row in rows.Skip(1))
        {
            row.Should().StartWith(indent, $"{what}: later rows sit under the text");
            row[indent.Length].Should().NotBe(' ', $"{what}: \"{row}\"");
        }
        string.Join(" ", rows.Select((r, i) => i == 0 ? r.Substring(prefix.Length) : r.Substring(indent.Length)))
            .Should().Be(text, $"{what}: no word is lost");
    }

    [Fact]
    public void NewsBoard_RowsThatFit_AreUnchanged()
    {
        const string line = "[12:34] A player has reached Level 20!";
        NewsLocation.NewsRows(line).Should().Equal("  " + line);
        NewsLocation.NewsRows("no stamp here").Should().Equal("  no stamp here");
        var exact = "[12:34] " + new string('x', 79 - 10);
        NewsLocation.NewsRows(exact).Should().Equal(new[] { "  " + exact }, "a row of exactly 79 fits");

        var output = new MemoryStream();
        var term = new TerminalEmulator(new MemoryStream(), output);
        OnlineChatSystem.WriteNewsEntry(term, "  [10/02 12:34] ", "A player has found a Seal!", "green");
        term.StreamWriterInternal?.Flush();
        Strip(Encoding.UTF8.GetString(output.ToArray())).Replace("\r", "")
            .Should().Be("  [10/02 12:34] A player has found a Seal!\n");
    }

    // ---------- 2. tame flavor ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("fr")]
    public void TameFlavor_EveryBeastFits79(string lang)
    {
        int wrapped = 0;
        foreach (var beast in BeastData.Beasts)
        {
            string flavor;
            using (Loc.RenderLanguage(lang)) flavor = beast.LocTameSuccessFlavor();
            var rows = WildernessLocation.TameFlavorRows(flavor);
            AllFit(rows, $"{beast.Id} in {lang}");
            rows.Should().OnlyContain(r => r.StartsWith("  "));
            string.Join(" ", rows.Select(r => r.Substring(2))).Should().Be(string.Join(" ", flavor.Split(new[] { '\n', ' ' }, StringSplitOptions.RemoveEmptyEntries)));
            if (flavor.Split('\n').Any(l => l.Length + 2 > 79)) wrapped++;
        }
        wrapped.Should().BeGreaterThan(0, $"some {lang} flavor overflows at 79 before the wrap");
    }

    [Fact]
    public void TameFlavor_ThatFits_IsShownLineForLine()
    {
        WildernessLocation.TameFlavorRows("The goat nods.\nIt follows you now.")
            .Should().Equal("  The goat nods.", "  It follows you now.");
        string fit = new string('a', 77);
        WildernessLocation.TameFlavorRows(fit + "\nb").Should().Equal("  " + fit, "  b");
    }

    // ---------- 3. French marks ----------

    [Theory]
    [InlineData("!")]
    [InlineData("?")]
    [InlineData(":")]
    [InlineData(";")]
    public void WordWrap_KeepsAMarkWithTheWordBeforeIt(string mark)
    {
        // without the rule this is "aaaa bbbb" then a row starting with the mark
        UIHelper.WordWrap($"aaaa bbbb {mark} cc", 9).Should().Equal(new[] { "aaaa", $"bbbb {mark} cc" });
        // the mark behind an ANSI color is still a mark
        var colored = UIHelper.WordWrap($"aaaa bbbb \u001b[31m{mark}\u001b[0m", 9);
        colored.Select(Strip).Should().Equal(new[] { "aaaa", $"bbbb {mark}" });
    }

    [Fact]
    public void WordWrap_AFrenchMarkNeverStartsARow()
    {
        var marks = "!?:;";
        int checkedTexts = 0;
        foreach (var key in new[] { "dungeon.guardian_phrase", "dungeon.news_seal_found" })
        {
            string text = Loc.GetIn("fr", key, LongName, 7);
            text.Should().MatchRegex(" [!?:;]");
            checkedTexts++;
            for (int width = 8; width <= 79; width++)
                foreach (var row in UIHelper.WordWrap(text, width))
                    (row.Length > 0 && marks.IndexOf(row[0]) >= 0).Should().BeFalse($"width {width}: \"{row}\"");
        }
        checkedTexts.Should().Be(2);
        // a mark at the start of a paragraph has no word before it and stays
        UIHelper.WordWrap("! hello", 79).Should().Equal("! hello");
    }

    // ---------- 4. /who locations ----------

    [Theory]
    [InlineData("MainStreet", "Main Street")]
    [InlineData("TheInn", "The Inn")]
    [InlineData("Dungeon", "Dungeon")]
    [InlineData("The Divine Realm", "The Divine Realm")]
    [InlineData("SysOp Console", "SysOp Console")]
    [InlineData("Level Master's Sanctum", "Level Master's Sanctum")]
    [InlineData("Dungeon (Group: Leader)", "Dungeon (Group: Leader)")]
    [InlineData("Dungeon (Group: DarkLord)", "Dungeon (Group: DarkLord)")]
    [InlineData("Spectating McKay", "Spectating McKay")]
    [InlineData("(Odd)Name", "(Odd)Name")]
    [InlineData("", "Unknown")]
    public void FormatLocation_KnownLocations(string location, string shown)
        => OnlineChatSystem.FormatLocation(location).Should().Be(shown);

    [Fact]
    public void FormatLocation_AGroupWithALongName_HasNoSpaceAfterTheParen()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        string shown = OnlineChatSystem.FormatLocation($"Dungeon (Group: {LongName})");
        shown.Should().Be($"Dungeon (Group: {LongName})").And.NotContain("( ");
    }

    // ---------- 5. hu settlement hint ----------

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void SettlementHint_Fits_AndHuHasNoDashCharacter(string lang)
    {
        string hint = Loc.GetIn(lang, "dungeon.event_hint_settlement");
        ("  " + hint).Length.Should().BeLessThanOrEqualTo(79);
        if (lang == "hu")
            hint.Should().NotContain("\u2013").And.NotContain("\u2014").And.Contain("--");
    }

    // ---------- 6. the opening phrase per reader ----------

    private static Character Member(string name, string? username) => new Character
    {
        Name1 = name, Name2 = name, AI = CharacterAI.Human, Level = 20, HP = 300, MaxHP = 400, BaseMaxHP = 400,
        GroupPlayerUsername = username,
    };

    /// <summary>
    /// The leader's session is en; the intro goes through the live broadcast path (the test sink) to two
    /// followers, one hu and one fr. The leader's own screen phrase stays en; each follower reads theirs.
    /// </summary>
    [Fact]
    public void OpeningPhrase_LeaderEn_FollowersHuAndFr_EachReadTheirOwn()
    {
        var prevSink = CombatEngine.GroupBroadcastSink;
        var prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";
            var sent = new List<Func<string, string>>();
            CombatEngine.GroupBroadcastSink = (_, build) => sent.Add(build);

            var secret = SecretBossManager.Instance.CreateBossMonster(SecretBossType.TheFirstWave, 30);
            secret.CanSpeak = true;
            var oldGod = (Monster)typeof(OldGodBossSystem).GetMethod("CreateBossMonster", F)!
                .Invoke(new OldGodBossSystem(), new object[] { OldGodsData.GetGodBossData(OldGodType.Maelketh) })!;

            var mates = new List<Character> { Member(LongName, "ll124hu"), Member("Fr", "ll124fr") };
            var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
            var send = typeof(CombatEngine).GetMethod("BroadcastGroupLocalized", F)!;
            foreach (var (monster, key) in new[] { (secret, "secretboss.first_wave.battlecry"), (oldGod, "oldgod.maelketh.intro.0") })
            {
                sent.Clear();
                var result = new CombatResult { Player = Member("Leader", null), Teammates = mates };
                var monsters = new List<Monster> { monster };
                send.Invoke(engine, new object[] { result, (Func<string, string>)(lang => CombatEngine.GroupCombatIntro(lang, monsters, result.Teammates)) });
                sent.Should().HaveCount(1);

                monster.Phrase.Should().Contain(Loc.GetIn("en", key), "the leader's screen line is the leader's (en)");
                foreach (var lang in new[] { "hu", "fr" })
                {
                    Loc.GetIn(lang, key).Should().NotBe(Loc.GetIn("en", key));
                    string intro = Strip(sent[0](lang));
                    string joined = Regex.Replace(intro, "\n  ", " ");
                    joined.Should().Contain(Loc.GetIn(lang, key), $"the {lang} follower reads the phrase in {lang}");
                    joined.Should().NotContain(Loc.GetIn("en", key));
                    AllFit(PhraseRows(intro, monster), $"{lang} phrase");
                }
                Regex.Replace(Strip(sent[0]("en")), "\n  ", " ").Should().Contain(Loc.GetIn("en", key), "an en follower reads en");
                AllFit(PhraseRows(Strip(sent[0]("en")), monster), "en phrase");
            }
            GameConfig.Language.Should().Be("en", "building for the followers leaves the leader's language");
        }
        finally { CombatEngine.GroupBroadcastSink = prevSink; GameConfig.Language = prevLang; }
    }

    /// <summary>The intro rows between the header and the "facing" row: the phrase.</summary>
    private static List<string> PhraseRows(string intro, Monster m)
    {
        var rows = intro.Replace("\r", "").Split('\n').Skip(1).TakeWhile(r => !r.Contains(m.GetDisplayInfo())).ToList();
        rows.Should().NotBeEmpty();
        return rows;
    }

    [Fact]
    public void OpeningPhrase_ThePlainPhraseStillShows_WhenNoBuilderIsSet()
    {
        var m = new Monster { Name = "Ogre", Level = 5, HP = 50, MaxHP = 50, CanSpeak = true, Phrase = "Grrr! Me smash you!" };
        Strip(CombatEngine.GroupCombatIntro("hu", new List<Monster> { m }, null)).Should().Contain("Grrr! Me smash you!");
    }

    [Fact]
    public void OpeningPhrase_TheDungeonPhrasesCarryABuilder()
    {
        string src = File.ReadAllText(Path.Combine(RepoRoot(), "Scripts", "Locations", "DungeonLocation.cs"));
        src.Should().Contain("guardian.PhraseInLanguage = () => Loc.Get(\"dungeon.guardian_phrase\");");
        src.Should().Contain("boss.PhraseInLanguage = () => GetBossPhrase(bossTheme);");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("repo root not found");
    }
}
