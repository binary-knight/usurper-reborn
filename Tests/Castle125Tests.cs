using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using Microsoft.Data.Sqlite;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: CastleLocation in the player's language. The throne room and the castle gate (full and BBS menus), the
/// prison orders, the orphanage, the succession list, the royal armory, the royal quest, the knighting ceremony,
/// the rebellion, the news, the mail and live notices to another player (in that player's language) and the
/// Electron castle menu. What is stored or matched stays English: King.GetTitle and NobleTitle (King, Queen, Sir,
/// Dame), the mercenary role, the court plot type, the orphan backstory, the royal quest description QuestSystem
/// matches words in, the armory item names and every Electron key, category and icon. Every changed row fits 79
/// columns in English and Hungarian with a 30-character name.
/// </summary>
[Collection("SharedGameSingletons")]
public class Castle125Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player, NPC or monarch can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-castle-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);
    private readonly King? _kingBefore = CastleLocation.GetCurrentKing();

    public void Dispose()
    {
        CastleLocation.SetKing(_kingBefore!);
        if (_db == null) return;
        SqliteConnection.ClearAllPools();
        try { File.Delete(_path); } catch { }
    }

    // ---------- helpers ----------

    private sealed class Screen
    {
        public TerminalEmulator Term = null!;
        public MemoryStream Output = new();
        public string Text
        {
            get
            {
                Term.StreamWriterInternal?.Flush();
                return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            }
        }
    }

    private static Screen NewScreen(params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static Screen At(CastleLocation castle, Character hero, bool asKing, params string[] lines)
    {
        var s = NewScreen(lines);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(castle, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(castle, hero);
        typeof(CastleLocation).GetField("playerIsKing", F)!.SetValue(castle, asKing);
        return s;
    }

    private static async Task Run(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, F)!;
        var r = m.Invoke(target, args);
        if (r is Task t) await t;
    }

    private static async Task<T> InLanguage<T>(string lang, Func<Task<T>> body, bool compact = false)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode, cm = GameConfig.CompactMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            GameConfig.CompactMode = compact;
            return await body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; GameConfig.CompactMode = cm; }
    }

    private static T InLang<T>(string lang, Func<T> body) => InLanguage(lang, () => Task.FromResult(body())).GetAwaiter().GetResult();

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    /// <summary>A framed box row (WriteBoxHeader) is at most 80 wide; every other row fits in 79.</summary>
    private static void EveryRowFits(IEnumerable<string> rows, string screen)
    {
        foreach (var row in rows)
        {
            if (row.Length > 0 && "╔║╚".IndexOf(row[0]) >= 0)
                row.Length.Should().BeLessOrEqualTo(MaxWidth + 1, $"the {screen} box keeps its width: \"{row}\"");
            else
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
        }
    }

    private static void EveryRowFits(string text, string screen) => EveryRowFits(Rows(text), screen);

    /// <summary>The Hungarian screen holds none of the English text of these keys (each literal piece of the
    /// English value with 5 letters or more, unless the Hungarian value has it too).</summary>
    private static void NoEnglishLeft(string huText, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            string en = Loc.GetIn("en", key), hu = Loc.GetIn("hu", key);
            hu.Should().NotBe(en, $"{key} has a Hungarian text");
            foreach (var piece in Regex.Split(en, @"\{\d+(?:,-?\d+)?\}").Select(p => p.Trim()).Where(p => p.Count(char.IsLetter) >= 5))
                if (!hu.Contains(piece))
                    huText.Should().NotContain(piece, $"{key} is shown in Hungarian");
        }
    }

    private static Character Hero(CharacterSex sex = CharacterSex.Male, int level = 100) => new()
    {
        Name1 = "tester", Name2 = LongName, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = 50000, MaxHP = 50000, BaseMaxHP = 50000, Mental = 100, Dexterity = 200, Charisma = 10,
        AI = CharacterAI.Human, Gold = 2_000_000_000, Sex = sex, Chivalry = 500, Fame = 500,
    };

    private static King LongKing(CharacterSex sex = CharacterSex.Female)
    {
        var king = King.CreateNewKing(LongName, CharacterAI.Human, sex);
        king.Treasury = 123_456_789;
        return king;
    }

    private static string Src() =>
        File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Locations", "CastleLocation.cs"));

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    private static string ClientSrc(string file) =>
        File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "electron-client", "src", file));

    /// <summary>The longest title a monarch or knight can be shown with in the language.</summary>
    private static string LongestTitle(string lang) =>
        new[] { "castle.king", "castle.queen", "castle.title_sir", "castle.title_dame" }.Select(k => L(lang, k)).OrderByDescending(t => t.Length).First();

    private static readonly string[] CombatNameChecks =
    {
        // CombatEngine.cs isUndead, boss bonus and evil target checks: substrings of a Monster name.
        "Skeleton", "Zombie", "Ghost", "Lich", "Wraith", "Vampire", "Undead", "Revenant", "Boss", "Chief", "Lord", "King",
        "Demon", "Devil",
    };

    [Fact]
    public void LongName_IsTheLongestPlayerName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
    }

    // ================= the throne room and the castle gate =================

    private static readonly string[] RoyalBbsKeys =
    {
        "castle.bbs_prison", "castle.bbs_orders", "castle.bbs_security", "castle.bbs_history", "castle.bbs_abdicate",
        "castle.bbs_fiscal", "castle.bbs_quests", "castle.bbs_orphanage", "castle.bbs_wedding", "castle.bbs_succession",
        "castle.bbs_bodyguards", "castle.bbs_sponsor_heir", "castle.bbs_bloodlines", "castle.bbs_statues",
    };

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task ThroneRoom_Fits_AndIsInThePlayersLanguage(string lang)
    {
        CastleLocation.SetKing(LongKing());
        string full = await InLanguage(lang, async () =>
        {
            var castle = new CastleLocation();
            var s = At(castle, Hero(CharacterSex.Female), true);
            await Run(castle, "DisplayRoyalCastleInterior");
            return s.Text;
        });
        string bbs = await InLanguage(lang, async () =>
        {
            var castle = new CastleLocation();
            var s = At(castle, Hero(CharacterSex.Female), true);
            await Run(castle, "DisplayRoyalCastleInteriorBBS");
            return s.Text;
        }, compact: true);
        Capture($"castle-throne-{lang}.txt", full);
        Capture($"castle-throne-bbs-{lang}.txt", bbs);
        EveryRowFits(full, $"{lang} throne room");
        EveryRowFits(bbs, $"{lang} BBS throne room");
        full.Should().Contain(L(lang, "castle.menu_statues"));
        foreach (var key in RoyalBbsKeys) bbs.Should().Contain("]" + L(lang, key) + " ", key);
        bbs.Should().Contain("[R]" + L(lang, "ui.return"));
        if (lang == "hu")
        {
            NoEnglishLeft(full, new[] { "castle.menu_statues" });
            NoEnglishLeft(bbs, RoyalBbsKeys);
        }
        else
        {
            full.Should().Contain("[V] Courtyard Statues");
            bbs.Should().Contain("[P]Prison [O]Orders [1]Mail [G]Sleep").And.Contain("[Y]Sponsor Heir [Z]Bloodlines [V]Statues");
        }
    }

    private static readonly string[] GateBbsKeys =
    {
        "castle.bbs_royal_guard", "castle.bbs_donate", "castle.bbs_history", "castle.bbs_audience", "castle.bbs_apply_guard",
        "castle.bbs_courtyard_statues", "castle.bbs_bloodlines", "castle.bbs_join_crown",
    };

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task CastleGate_Fits_AndIsInThePlayersLanguage(string lang)
    {
        CastleLocation.SetKing(LongKing(CharacterSex.Male));
        string full = await InLanguage(lang, async () =>
        {
            var castle = new CastleLocation();
            var s = At(castle, Hero(), false);
            await Run(castle, "DisplayCastleExterior");
            return s.Text;
        });
        string bbs = await InLanguage(lang, async () =>
        {
            var castle = new CastleLocation();
            var s = At(castle, Hero(), false);
            await Run(castle, "DisplayCastleExteriorBBS");
            return s.Text;
        }, compact: true);
        Capture($"castle-gate-{lang}.txt", full);
        Capture($"castle-gate-bbs-{lang}.txt", bbs);
        EveryRowFits(full, $"{lang} castle gate");
        EveryRowFits(bbs, $"{lang} BBS castle gate");
        full.Should().Contain(L(lang, "castle.menu_statues")).And.Contain(L(lang, "castle.king") + " " + LongName);
        bbs.Should().Contain(" " + L(lang, "castle.king") + " " + LongName).And.Contain(L(lang, "castle.bbs_rules", 0, "123,456,789").Trim());
        foreach (var key in GateBbsKeys) bbs.Should().Contain("]" + L(lang, key) + " ", key);
        bbs.Should().Contain("[I]" + L(lang, "castle.bbs_infiltrate_level", GameConfig.MinLevelKing) + " ");
        L(lang, "castle.bbs_infiltrate_challenge").Should().NotBeEmpty();
        if (lang == "hu")
        {
            NoEnglishLeft(bbs, GateBbsKeys);
            bbs.Should().NotContain(" King ");
        }
        else bbs.Should().Contain("[T]Royal Guard [P]Prison [D]Donate").And.Contain("[V]Courtyard Statues [Z]Bloodlines");
    }

    [Fact]
    public void TheMonarchsTitle_IsShownKeyed_AndKingGetTitleStaysTheStoredEnglish()
    {
        var queen = LongKing(CharacterSex.Female);
        queen.GetTitle().Should().Be("Queen");
        King.CreateNewKing("X", CharacterAI.Human, CharacterSex.Male).GetTitle().Should().Be("King");
        foreach (var lang in AllLanguages)
        {
            CastleLocation.RoyalTitleIn(lang, CharacterSex.Female).Should().Be(L(lang, "castle.queen"));
            CastleLocation.RoyalTitleIn(lang, CharacterSex.Male).Should().Be(L(lang, "castle.king"));
        }
        string src = Src();
        src.Should().NotContain("currentKing.GetTitle()", "the shown title is KingTitle(); GetTitle feeds the stored monarch history");
        Regex.Matches(src, @"\.GetTitle\(\)").Count.Should().Be(4, "only the monarch history entries store the title");
        src.Should().Contain("{NobleTitleLabel(monarch.Title),-8}");
        InLang("hu", () => CastleLocation.NobleTitleLabel("Queen")).Should().Be(L("hu", "castle.queen"));
        InLang("hu", () => CastleLocation.NobleTitleLabel("Sir")).Should().Be(L("hu", "castle.title_sir"));
        InLang("hu", () => CastleLocation.NobleTitleLabel("Gold Champion")).Should().Be("Gold Champion", "an arena tier title is shown as stored");
    }

    // ================= the prison =================

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task PrisonOrders_Fit_AndAreInThePlayersLanguage(string lang)
    {
        CastleLocation.SetKing(LongKing());
        var keys = new[] { "castle.prison_no_throne", "castle.already_imprisoned", "castle.player_imprisoned_today",
            "castle.guards_process", "castle.dungeons_overwhelmed", "castle.rebellion_whispers", "castle.rebellion_tolerate",
            "castle.rebellion_silence", "castle.bounty_self" };
        foreach (var key in keys)
            L(lang, key, LongName).Length.Should().BeLessOrEqualTo(MaxWidth, $"{lang} {key}");
        string text = await InLanguage(lang, async () =>
        {
            var castle = new CastleLocation();
            var s = At(castle, Hero(CharacterSex.Female), true, "R");
            CastleLocation.GetCurrentKing().Prisoners[LongName] = new PrisonRecord { CharacterName = LongName, Crime = "Treason", Sentence = 7 };
            await Run(castle, "ManagePrisonCells");
            return s.Text;
        });
        Capture($"castle-prison-{lang}.txt", text);
        EveryRowFits(text, $"{lang} prison orders");
        text.Should().Contain(LongName).And.Contain("Treason");
        // The vacant-throne line, rendered
        string vacant = await InLanguage(lang, async () =>
        {
            CastleLocation.SetKing(null!);
            var castle = new CastleLocation();
            var s = At(castle, Hero(), false);
            await Run(castle, "ManagePrisonCells");
            return s.Text;
        });
        vacant.Should().Contain(L(lang, "castle.prison_no_throne"));
        if (lang == "hu") NoEnglishLeft(vacant, new[] { "castle.prison_no_throne" });
        else vacant.Should().Contain("  The throne is vacant. No one has authority over the prison.");
    }

    // ================= the orphanage =================

    private static RoyalOrphan RealOrphan(string name, int age, CharacterSex sex) => new()
    {
        Name = name, Sex = sex, IsRealOrphan = true, Race = CharacterRace.HalfElf, Happiness = 100, Soul = 300,
        BirthDate = DateTime.Now - TimeSpan.FromHours((age + 0.5) * GameConfig.NpcLifecycleHoursPerYear),
        BackgroundStory = "Both parents lost.",
    };

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task Orphanage_Fits_WithTheLongestName_AndIsInThePlayersLanguage(string lang)
    {
        var king = LongKing();
        king.Orphans.Add(RealOrphan(LongName, 18, CharacterSex.Female));
        king.Orphans.Add(RealOrphan("Short", GameConfig.OrphanCommissionAge, CharacterSex.Male));
        king.Orphans.Add(new RoyalOrphan { Name = "Pip", Age = 7, Sex = CharacterSex.Male, BackgroundStory = "Parents lost to dungeon creatures." });
        CastleLocation.SetKing(king);
        string text = await InLanguage(lang, async () =>
        {
            var castle = new CastleLocation();
            var s = At(castle, Hero(CharacterSex.Female), true, "V", "3", "", "R");
            await Run(castle, "RoyalOrphanage");
            return s.Text;
        });
        Capture($"castle-orphanage-{lang}.txt", text);
        EveryRowFits(text, $"{lang} orphanage");
        text.Should().Contain(L(lang, "castle.orphan_kind_orphaned")).And.Contain(L(lang, "castle.orphan_kind_adopted"))
            .And.Contain(L(lang, "castle.orphan_girl")).And.Contain(L(lang, "castle.orphan_adult").Trim())
            .And.Contain(L(lang, "castle.orphan_ready_tag").Trim()).And.Contain(InLang(lang, () => GameConfig.GetLocalizedRaceName(CharacterRace.HalfElf)))
            .And.Contain(L(lang, "castle.orphan_backstory_1"), "the stored English backstory is shown keyed");
        if (lang == "hu")
        {
            NoEnglishLeft(text, new[] { "castle.orphan_kind_orphaned", "castle.orphan_kind_adopted", "castle.orphan_backstory_1" });
            text.Should().NotContain("Ready!").And.NotContain("Girl");
        }
        else text.Should().Contain("Orphaned").And.Contain("  Ready!").And.Contain("\"Parents lost to dungeon creatures.\"");
        // The stored backstory is untouched
        king.Orphans[2].BackgroundStory.Should().Be("Parents lost to dungeon creatures.");
    }

    [Fact]
    public void OrphanBackstories_AreStoredInEnglish_AndShownKeyed()
    {
        var table = (string[])typeof(CastleLocation).GetField("OrphanBackstories", S)!.GetValue(null)!;
        table.Should().HaveCount(10);
        for (int i = 0; i < table.Length; i++)
        {
            L("en", $"castle.orphan_backstory_{i}").Should().Be(table[i]);
            InLang("hu", () => CastleLocation.OrphanBackstoryText(table[i])).Should().Be(L("hu", $"castle.orphan_backstory_{i}"));
        }
        // v1.2.5: the world simulation's own backstory (worldsim.orphan_backstory, stored in English) is shown keyed too;
        // any other stored text is shown as stored
        InLang("hu", () => CastleLocation.OrphanBackstoryText("Both parents lost. Mother: A, Father: B.")).Should().Be(L("hu", "worldsim.orphan_backstory", "A", "B"));
        InLang("hu", () => CastleLocation.OrphanBackstoryText("A backstory of its own.")).Should().Be("A backstory of its own.");
        Src().Should().Contain("BackgroundStory = OrphanBackstories[random.Next(OrphanBackstories.Length)]");
    }

    // ================= succession, mercenaries, court =================

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task SuccessionList_Fits_AndIsInThePlayersLanguage(string lang)
    {
        var king = LongKing();
        king.Heirs.Add(new RoyalHeir { Name = "Ilona", Age = 30, Sex = CharacterSex.Female, ClaimStrength = 90, IsDesignated = true });
        king.Heirs.Add(new RoyalHeir { Name = "Bence", Age = 9, Sex = CharacterSex.Male, ClaimStrength = 40 });
        king.Heirs.Add(new RoyalHeir { Name = "Zsofi", Age = 20, Sex = CharacterSex.Male, ClaimStrength = 60 });
        CastleLocation.SetKing(king);
        string text = await InLanguage(lang, async () =>
        {
            var castle = new CastleLocation();
            var s = At(castle, Hero(CharacterSex.Female), true, "R");
            await Run(castle, "ManageSuccession");
            return s.Text;
        });
        Capture($"castle-succession-{lang}.txt", text);
        EveryRowFits(text, $"{lang} succession");
        text.Should().Contain(L(lang, "castle.heir_status_designated")).And.Contain(L(lang, "castle.heir_adult"))
            .And.Contain(L(lang, "castle.heir_minor")).And.Contain(L(lang, "base.female"));
        if (lang == "hu") NoEnglishLeft(text, new[] { "castle.heir_status_designated", "castle.heir_minor", "base.female" });
        else text.Should().Contain("DESIGNATED").And.Contain("Minor").And.Contain("Female");
    }

    [Fact]
    public void MercenaryRoles_StayEnglishInStorage_AndAreShownKeyed()
    {
        InLang("hu", () =>
        {
            var castle = new CastleLocation();
            typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(castle, Hero());
            var merc = (RoyalMercenary)typeof(CastleLocation).GetMethod("GenerateMercenary", F)!.Invoke(castle, new object[] { "Tank", 50 })!;
            merc.Role.Should().Be("Tank", "the role is stored in English and keys MercenaryNames");
            return 0;
        });
        foreach (var (role, key) in new[] { ("Tank", "castle.role_tank"), ("DPS", "castle.role_dps"), ("Support", "castle.role_support") })
            InLang("hu", () => CastleLocation.MercRoleLabel(role)).Should().Be(L("hu", key));
        string src = Src();
        src.Should().Contain("string role = orphan.Soul > 100 ? \"Support\" :").And.Contain("Loc.Get(\"castle.commissioned_merc\", orphan.Name, MercRoleLabel(role))");
        foreach (var (plot, key) in new[] { ("Assassination", "castle.plot_assassination"), ("Coup", "castle.plot_coup"), ("Scandal", "castle.plot_scandal"), ("Sabotage", "castle.plot_sabotage") })
            InLang("hu", () => CastleLocation.PlotTypeLabel(plot)).Should().Be(L("hu", key));
        src.Should().Contain("court.ActivePlots.FirstOrDefault(p => p.PlotType == plot.PlotType", "the stored plot type is still compared");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void SponsorChildRow_Fits_WithTheLongestNameAndClass(string lang)
    {
        string longestClass = InLang(lang, () => Enum.GetValues<CharacterClass>().Select(c => GameConfig.GetLocalizedClassName(c)).OrderByDescending(n => n.Length).First());
        foreach (var track in new[] { "castle.d3_track_court", "castle.d3_track_faith", "castle.d3_track_shadows" })
        {
            string row = L(lang, "castle.d3_child_row", 1, LongName, 100, longestClass, L(lang, track));
            if (row.Length > MaxWidth)
            {
                L(lang, "castle.d3_child_row_short", 1, LongName, 100, longestClass).Length.Should().BeLessOrEqualTo(MaxWidth);
                L(lang, "castle.d3_child_track", L(lang, track)).Length.Should().BeLessOrEqualTo(MaxWidth);
            }
        }
        string src = Src();
        src.Should().Contain("if (childRow.Length <= 79)").And.Contain("Loc.Get(\"castle.d3_child_track\", track)");
    }

    // ================= stored values shown keyed: history, court, parents, gold =================

    private static readonly (string Stored, string Key, string? Arg)[] EndReasons =
    {
        ("Died", "castle.end_died", null), ("Died of old age", "castle.end_old_age", null), ("Fell in battle", "castle.end_battle", null),
        ("Abdicated", "castle.end_abdicated", null), ("abdicated the throne to ascend to godhood", "castle.end_godhood", null),
        ("abdicated the throne to start anew", "castle.end_anew", null), ("left the throne and the realm", "castle.end_left", null),
        ("Defeated by " + LongName, "castle.end_defeated_by", LongName), ("Overthrown by Wolves of the North siege", "castle.end_siege", "Wolves of the North"),
    };

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task MonarchHistory_ShowsTheStoredEndReasons_InThePlayersLanguage_AndFits(string lang)
    {
        CastleLocation.SetKing(LongKing());
        var field = typeof(CastleLocation).GetField("monarchHistory", S)!;
        var saved = (List<MonarchRecord>)field.GetValue(null)!;
        var list = EndReasons.Select((r, i) => new MonarchRecord { Name = LongName, Title = i % 2 == 0 ? "Queen" : "King", DaysReigned = 1000 + i,
            EndReason = r.Stored, CoronationDate = DateTime.Now.AddDays(-i) }).ToList();
        field.SetValue(null, list);
        try
        {
            string text = await InLanguage(lang, async () =>
            {
                var castle = new CastleLocation();
                var s = At(castle, Hero(), false, "");
                await Run(castle, "ShowMonarchHistory");
                return s.Text;
            });
            Capture($"castle-history-{lang}.txt", text);
            EveryRowFits(text, $"{lang} monarch history");
            foreach (var (stored, key, arg) in EndReasons)
            {
                string shown = arg == null ? L(lang, key) : L(lang, key, arg);
                InLang(lang, () => CastleLocation.EndReasonLabel(stored)).Should().Be(shown);
                text.Should().Contain(shown);
                if (lang == "en") shown.Should().Be(stored, "the English reads as stored");
            }
            if (lang == "hu") text.Should().NotContain("Defeated by").And.NotContain("Fell in battle").And.NotContain("siege").And.NotContain("Queen");
            list.Select(m => m.EndReason).Should().Equal(EndReasons.Select(r => r.Stored), "the stored reasons are untouched");
            InLang("hu", () => CastleLocation.EndReasonLabel("Swallowed by a dragon")).Should().Be("Swallowed by a dragon");
        }
        finally { field.SetValue(null, saved); }
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task CourtRolesAndFactions_AreShownInThePlayersLanguage(string lang)
    {
        var king = LongKing();
        string[] roles = { "Royal Advisor", "Court Steward", "Marshal", "Spymaster", "Treasurer", "Advisor", L("hu", "castle.d3_role_chaplain"), "Keeper of Hounds" };
        var factions = new[] { CourtFaction.Loyalists, CourtFaction.Reformists, CourtFaction.Militarists, CourtFaction.Merchants, CourtFaction.Faithful };
        for (int i = 0; i < roles.Length; i++)
            king.CourtMembers.Add(new CourtMember { Name = i == 0 ? LongName : $"Member {i}", Role = roles[i], Faction = factions[i % factions.Length], LoyaltyToKing = 50, Influence = 50 });
        CastleLocation.SetKing(king);
        string text = await InLanguage(lang, async () =>
        {
            var castle = new CastleLocation();
            var s = At(castle, Hero(), false, "", "");
            await Run(castle, "ViewCourtPolitics");
            return s.Text;
        });
        Capture($"castle-court-{lang}.txt", text);
        EveryRowFits(text, $"{lang} royal court");
        foreach (var key in new[] { "castle.d3_role_advisor", "castle.court_role_steward", "castle.court_role_marshal", "castle.court_role_spymaster",
                     "castle.court_role_treasurer", "castle.court_role_advisor", "castle.d3_role_chaplain" })
            text.Should().Contain(L(lang, key), key);
        foreach (var f in factions) text.Should().Contain(L(lang, $"castle.court_faction_{f.ToString().ToLowerInvariant()}"));
        text.Should().Contain("Keeper of Hounds", "an unknown role is shown as stored");
        if (lang == "hu") text.Should().NotContain("Court Steward").And.NotContain("Treasurer").And.NotContain("Loyalists").And.NotContain("Reformists");
        king.CourtMembers.Select(m => m.Role).Should().Equal(roles, "the stored roles are untouched");
        string src = Src();
        src.Should().Contain("string roleName = Loc.Get(roleKey), roleStored = Loc.GetIn(\"en\", roleKey);")
            .And.Contain("Role = roleStored,", "a sponsored heir's role is stored in English");
    }

    [Fact]
    public async Task UnknownParents_AndTheBbsTreasury_AreInHungarian()
    {
        var king = LongKing();
        king.Orphans.Add(RealOrphan("Pip", 10, CharacterSex.Male));
        CastleLocation.SetKing(king);
        string text = await InLanguage("hu", async () =>
        {
            var castle = new CastleLocation();
            var s = At(castle, Hero(), true, "1", "");
            await Run(castle, "ViewOrphanDetails");
            return s.Text;
        });
        text.Should().Contain(L("hu", "castle.orphan_mother", L("hu", "castle.orphan_unknown"))).And.NotContain("Unknown");
        string bbs = await InLanguage("hu", async () =>
        {
            var castle = new CastleLocation();
            var s = At(castle, Hero(), true);
            await Run(castle, "DisplayRoyalCastleInteriorBBS");
            return s.Text;
        }, compact: true);
        bbs.Should().Contain(L("hu", "magic_shop.gold_short", "123,456,789")).And.NotContain("123,456,789g");
    }

    [Fact]
    public void FloorTargets_AreShownInThePlayersLanguage()
    {
        InLang("hu", () => CastleLocation.QuestTargetLabel("Floor 45")).Should().Be(L("hu", "dungeon.floor", 45));
        InLang("hu", () => CastleLocation.QuestTargetLabel("Goblin")).Should().Be("Goblin");
    }

    // ================= the armory =================

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void RoyalArmory_IsInThePlayersLanguage_AndTheItemKeepsItsEnglishName(string lang)
    {
        foreach (var key in new[] { "castle.armory_sr_blade", "castle.armory_sr_plate", "castle.armory_sr_shield", "castle.armory_sr_ring" })
            ("1. " + L(lang, key, "99,999,999", 999)).Length.Should().BeLessOrEqualTo(MaxWidth, $"{lang} {key}");
        foreach (var item in new[] { "blade", "plate", "shield", "ring" })
            ("  " + L(lang, "castle.armory_cant_afford", L(lang, $"castle.armory_item_{item}"))).Length.Should().BeLessOrEqualTo(MaxWidth);
        if (lang == "hu")
            foreach (var key in new[] { "castle.armory_sr_blade", "castle.armory_purchase", "castle.armory_item_blade", "castle.armory_item_plate" })
                L("hu", key).Should().NotBe(L("en", key));
        else
        {
            L("en", "castle.armory_sr_blade", "50,000", 150).Should().Be("Crown Blade - 50,000g - WeapPow 150");
            L("en", "castle.armory_cant_afford", L("en", "castle.armory_item_blade")).Should().Be("You can't afford the Crown Blade.");
        }
        string src = Src();
        foreach (var name in new[] { "Crown Blade", "Royal Guard Plate", "Crown Shield", "Royal Signet Ring" })
            src.Should().Contain($"Name = \"{name}\",", "the item is stored with its English name");
        foreach (var item in new[] { "blade", "plate", "shield", "ring" })
            src.Should().Contain($"itemName = Loc.Get(\"castle.armory_item_{item}\");");
        src.Should().Contain("var input = await terminal.GetInput(Loc.Get(\"castle.armory_purchase\"));");
    }

    // ================= royal quests =================

    [Fact]
    public void RoyalQuestDescriptions_GoToQuestSystemInEnglish_AndAreShownKeyed()
    {
        // Type 2 ("Clear a dungeon floor of all hostile creatures") is a floor to clear; it was read as a monster
        // quest until 1.2.5 because "creature" was checked before "floor".
        var expected = new[] { QuestTarget.Monster, QuestTarget.ReachFloor, QuestTarget.ClearFloor, QuestTarget.ReachFloor, QuestTarget.DefeatNPC };
        var objective = new[] { QuestObjectiveType.KillMonsters, QuestObjectiveType.ReachDungeonFloor, QuestObjectiveType.ClearDungeonFloor, QuestObjectiveType.ReachDungeonFloor, QuestObjectiveType.KillBoss };
        CastleLocation.RoyalQuestTypes.Should().HaveCount(5);
        InLang("hu", () =>
        {
            for (int i = 0; i < CastleLocation.RoyalQuestTypes.Length; i++)
            {
                L("en", $"castle.quest_type_{i}").Should().Be(CastleLocation.RoyalQuestTypes[i]);
                L("hu", $"castle.quest_type_{i}").Should().NotBe(CastleLocation.RoyalQuestTypes[i]);
                var quest = QuestSystem.CreateRoyalAudienceQuest(Hero(), "Queen Test", 2, 100, 100, CastleLocation.RoyalQuestTypes[i]);
                try
                {
                    quest.QuestTarget.Should().Be(expected[i], $"QuestSystem reads type {i}");
                    quest.Objectives[0].ObjectiveType.Should().Be(objective[i]);
                    quest.Comment.Should().Be(CastleLocation.RoyalQuestTypes[i]);
                }
                finally { ((List<Quest>)typeof(QuestSystem).GetField("questDatabase", S)!.GetValue(null)!).Remove(quest); }
            }
            return 0;
        });
        string src = Src();
        src.Should().Contain("string questDesc = RoyalQuestTypes[questType];")
            .And.Contain("Loc.Get(\"castle.quest_desc\", Loc.Get($\"castle.quest_type_{questType}\"))");
        for (int i = 0; i < 5; i++)
            L("hu", "castle.quest_desc", L("hu", $"castle.quest_type_{i}")).Length.Should().BeLessOrEqualTo(MaxWidth);
    }

    private static List<Quest> QuestDb() => (List<Quest>)typeof(QuestSystem).GetField("questDatabase", S)!.GetValue(null)!;

    private static async Task<string> AskForAQuest(string lang, Character hero) => await InLanguage(lang, async () =>
    {
        var castle = new CastleLocation();
        var s = At(castle, hero, false, "N", "", "");
        await Run(castle, "AudienceRequestQuest");
        return s.Text;
    });

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("fr")]
    public async Task APlayerWithARoyalQuest_CannotTakeASecond_InAnyLanguage(string lang)
    {
        var king = LongKing(CharacterSex.Male);
        CastleLocation.SetKing(king);
        var hero = Hero();
        hero.Name2 = "Questor " + lang;
        Quest? first = null;
        try
        {
            first = InLang(lang, () => QuestSystem.CreateRoyalAudienceQuest(hero, king.Name, 2, 100, 100, CastleLocation.RoyalQuestTypes[1]));
            first.TitleKey.Should().Be(QuestSystem.RoyalCommissionTitleKey);
            first.Comment.Should().Be(CastleLocation.RoyalQuestTypes[1], "the description is stored in English");
            int before = QuestDb().Count;
            string text = await AskForAQuest(lang, hero);
            Capture($"castle-quest-active-{lang}.txt", text);
            EveryRowFits(text, $"{lang} royal quest already active");
            QuestDb().Count.Should().Be(before, "no second royal quest is made");
            text.Should().Contain(L(lang, "castle.quest_already_active"));
            string shown = InLang(lang, () => first.GetDisplayTitle());
            shown.Should().Be(L(lang, "quest.royal_commission", L(lang, "castle.quest_type_1")));
            text.Replace("\n", " ").Should().Contain(L(lang, "castle.quest_type_1").Split(' ')[0]);
            if (lang != "en") InLang(lang, () => first.GetDisplayTitle()).Should().NotContain(CastleLocation.RoyalQuestTypes[1], "the title is not built from the English description");
        }
        finally { if (first != null) QuestDb().Remove(first); }
    }

    [Fact]
    public void AQuestDescriptionFromElsewhere_IsReadByItsWords_FloorFirst()
    {
        QuestSystem.RoyalQuestKind(-1, "Clear a dungeon floor of all hostile creatures").Should().Be(2);
        QuestSystem.RoyalQuestKind(-1, "Slay the creatures").Should().Be(0);
        QuestSystem.RoyalQuestKind(-1, "Bring back an artifact").Should().Be(1);
        QuestSystem.RoyalQuestKind(-1, "Find a criminal").Should().Be(4);
        QuestSystem.RoyalQuestKind(-1, "Look around").Should().Be(3);
        for (int i = 0; i < 5; i++) QuestSystem.RoyalQuestKind(i, "monster").Should().Be(i);
    }

    [Fact]
    public void ARoyalQuestFromBefore125_IsStillFound_ByItsEnglishDescription()
    {
        // A quest saved by 1.2.4 in Hungarian: no TitleKey, the title in Hungarian, the comment the English description.
        var legacy = new Quest { Initiator = "Al", Title = L("hu", "quest.royal_commission", CastleLocation.RoyalQuestTypes[0]), Comment = CastleLocation.RoyalQuestTypes[0] };
        QuestSystem.IsRoyalCommission(legacy).Should().BeTrue();
        QuestSystem.IsRoyalCommission(new Quest { Initiator = "Al", Title = "Royal Commission: something", Comment = "a bounty" }).Should().BeFalse();
        Src().Should().Contain("q.Initiator == currentKing.Name && QuestSystem.IsRoyalCommission(q)").And.NotContain("StartsWith(\"Royal Commission\")");
    }

    // ================= knighting =================

    private static readonly string[] KnightRows =
    {
        "castle.kn_silent", "castle.kn_gaze_1", "castle.kn_gaze_2", "castle.kn_watched_2", "castle.kn_step", "castle.kn_echo",
        "castle.kn_kneel", "castle.kn_blade_1", "castle.kn_blade_2", "castle.kn_right", "castle.kn_authority", "castle.kn_left",
        "castle.kn_valor_1", "castle.kn_valor_2", "castle.kn_dub_2", "castle.kn_rise_2", "castle.kn_applause_1",
        "castle.kn_applause_2", "castle.kn_bonus_chivalry", "castle.kn_bonus_fame", "castle.kn_bonus_who",
    };

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void KnightingRows_Fit_WithTheLongestNameAndTitle(string lang)
    {
        string title = LongestTitle(lang);
        foreach (var key in KnightRows)
            L(lang, key).Length.Should().BeLessOrEqualTo(MaxWidth, $"{lang} {key}");
        foreach (var key in new[] { "castle.kn_all_rise", "castle.kn_rises", "castle.kn_dub_1", "castle.kn_rise_1", "castle.kn_now" })
            L(lang, key, title, LongName).Length.Should().BeLessOrEqualTo(MaxWidth, $"{lang} {key}");
        L(lang, "castle.kn_watched_1", LongName).Length.Should().BeLessOrEqualTo(MaxWidth);
        L(lang, "castle.kn_bonus_damage", 100).Length.Should().BeLessOrEqualTo(MaxWidth);
        L(lang, "castle.kn_bonus_defense", 100).Length.Should().BeLessOrEqualTo(MaxWidth);
        if (lang == "hu") foreach (var key in KnightRows) L("hu", key).Should().NotBe(L("en", key), key);
    }

    [Fact]
    public void TheRiseLine_GoesOnTwoRows_WhenALongNamePassesColumn79()
    {
        // A short name keeps the one English row; a 30-character name was 80 columns and now splits.
        L("en", "castle.kn_rise", "Sir", "Bob").Should().Be("  \"Rise, Sir Bob. You are now a Knight of the Realm.\"");
        L("en", "castle.kn_rise", "Sir", LongName).Length.Should().BeGreaterThan(MaxWidth);
        string src = Src();
        src.Should().Contain("string rise = Loc.Get(\"castle.kn_rise\", shownTitle, currentPlayer.DisplayName);")
            .And.Contain("if (rise.Length <= 79)")
            .And.Contain("terminal.WriteLine(Loc.Get(\"castle.kn_rise_1\", shownTitle, currentPlayer.DisplayName));");
    }

    [Fact]
    public void TheKnightTitle_IsStoredInEnglish_WhateverTheLanguage_AndTheSaveKeepsIt()
    {
        string src = Src();
        src.Should().Contain("string title = currentPlayer.Sex == CharacterSex.Male ? \"Sir\" : \"Dame\";")
            .And.Contain("currentPlayer.NobleTitle = title;")
            .And.Contain("MetaProgressionSystem.Instance.UnlockedTitles.Add(title);")
            .And.Contain("string shownTitle = NobleTitleLabel(title);");
        Regex.Matches(src, Regex.Escape("currentPlayer.NobleTitle = currentPlayer.Sex == CharacterSex.Female ? \"Queen\" : \"King\";")).Count
            .Should().Be(2, "the throne won in combat and by siege stores the English title");
        Src("Locations", "SanctumLocation.cs").Should().Contain("currentPlayer.NobleTitle == \"Sir\" || currentPlayer.NobleTitle == \"Dame\"");
        // Saved through SaveSystem.SerializePlayer and read back from the JSON
        var hero = Hero(CharacterSex.Female);
        hero.NobleTitle = "Dame";
        hero.IsKnighted = true;
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        var player = InLang("hu", () => (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { hero })!);
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(player))!;
        back.NobleTitle.Should().Be("Dame", "the title is stored in English");
        InLang("hu", () => CastleLocation.NobleTitleLabel(back.NobleTitle)).Should().Be(L("hu", "castle.title_dame"));
    }

    // ================= the rebellion =================

    private static readonly string[] RebellionTitles =
        { "castle.rebel_risen", "castle.rebel_overthrown", "castle.rebel_court", "castle.rebel_coin", "castle.rebel_walk", "castle.rebel_story_ends" };

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("es")] [InlineData("fr")] [InlineData("it")]
    public void RebellionBoxes_KeepTheirFrame_InEveryLanguage(string lang)
    {
        foreach (var key in RebellionTitles)
        {
            L(lang, key).Length.Should().BeLessOrEqualTo(51, $"{lang} {key} fits the frame");
            CastleLocation.FramedRow(L(lang, key), 51, true).Length.Should().Be(55, $"{lang} {key}: the row meets the frame");
        }
        foreach (var key in new[] { "castle.rebel_deleted_1", "castle.rebel_deleted_2", "castle.rebel_deleted_3" })
        {
            L(lang, key).Length.Should().BeLessOrEqualTo(48, $"{lang} {key} fits the frame");
            CastleLocation.FramedRow(L(lang, key), 51, false).Length.Should().Be(55);
        }
        foreach (var key in new[] { "castle.rebel_heads", "castle.rebel_tails" })
            CastleLocation.FramedRow(L(lang, key), 31, true).Length.Should().Be(35);
        if (lang == "en")
        {
            CastleLocation.FramedRow("THE COIN OF FATE", 51, true).Should().Be("  ║                 THE COIN OF FATE                  ║");
            CastleLocation.FramedRow("H E A D S", 31, true).Should().Be("  ║           H E A D S           ║");
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void RebellionRows_Fit_WithTheLongestNameAndTitle(string lang)
    {
        var en = JsonSerializer.Deserialize<Dictionary<string, string>>(File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Localization", "en.json")))!;
        var rows = en.Keys.Where(k => k.StartsWith("castle.rebel_") && !RebellionTitles.Contains(k)
            && !k.StartsWith("castle.rebel_deleted_") && k != "castle.rebel_heads" && k != "castle.rebel_tails" && k != "castle.rebel_penalties").ToList();
        rows.Should().HaveCountGreaterThan(80);
        foreach (var key in rows)
        {
            string row = L(lang, key, key == "castle.rebel_accused_1" ? LongestTitle(lang) : LongName, LongName);
            if (key == "castle.rebel_pen_gold") row = L(lang, key, "2,000,000,000");
            if (key == "castle.rebel_woman_1" || key == "castle.rebel_accused_2") row = L(lang, key, 99);
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"{lang} {key}: \"{row}\"");
            if (lang == "hu") L("hu", key).Should().NotBe(L("en", key), key);
        }
        ("  ═══ " + L(lang, "castle.rebel_penalties") + " ═══").Length.Should().BeLessOrEqualTo(MaxWidth);
        string src = Src();
        src.Should().NotContain("terminal.WriteLine(\"  ║").And.Contain("WriteFramedBox(51, Loc.Get(\"castle.rebel_risen\"));");
    }

    // ================= news, mail and live notices =================

    [Fact]
    public void News_IsInTheWritersLanguage_AndTheEnglishReadsAsBefore()
    {
        InLang("en", () =>
        {
            Loc.Get("castle.news_imprisoned", "Queen", "Ann", "Bob", "theft").Should().Be("Queen Ann imprisoned Bob for theft!");
            Loc.Get("castle.news_est_closed", "King", "Al", "The Inn").Should().Be("King Al has closed the The Inn!");
            Loc.Get("castle.news_proclamation", "hi", "King", "Al").Should().Be("Royal Proclamation: \"hi\" - King Al");
            CastleLocation.ReignEndedNews("Al", "abdicated the throne to ascend to godhood").Should().Be("Al has abdicated the throne to ascend to godhood! The kingdom is in chaos!");
            CastleLocation.ReignEndedNews("Al", "left the throne and the realm").Should().Be("Al has left the throne and the realm! The kingdom is in chaos!");
            CastleLocation.ReignEndedNews("Al", "wandered off").Should().Be("Al has wandered off! The kingdom is in chaos!");
            Loc.Get("castle.news_vacant", "Al", CastleLocation.VacancyReasonText("The ruler has died of old age.")).Should().Be("Al is no longer ruler! The throne stands vacant. The ruler has died of old age.");
            return 0;
        });
        InLang("hu", () =>
        {
            CastleLocation.ReignEndedNews("Al", "abdicated the throne to start anew").Should().Be(L("hu", "castle.news_reign_anew", "Al"));
            CastleLocation.VacancyReasonText("The ruler has fallen in battle.").Should().Be(L("hu", "castle.vacant_battle"));
            return 0;
        });
        Src().Should().NotContain("Newsy(true, $\"").And.NotContain("Newsy(false, $\"").And.NotContain("AddNews(\n                $\"");
    }

    [Fact]
    public void TheRoyalGuardFoe_HoldsNoCombatCheckWord_InAnyLanguage()
    {
        foreach (var lang in AllLanguages)
        {
            string name = L(lang, "castle.royal_guard_monster", "Bob");
            name.Should().Contain("Bob");
            foreach (var word in CombatNameChecks) name.Should().NotContain(word, $"{lang}: CombatEngine reads {word} in a monster name");
        }
        Src().Should().Contain("Name = Loc.Get(\"castle.royal_guard_monster\", guard.Name),");
    }

    private void Player(string key, string display, string lang) =>
        Exec("INSERT INTO players (username, display_name, player_data, language, last_login) VALUES (@u, @d, @p, @l, datetime('now'));",
            ("@u", key), ("@d", display), ("@p", $"{{\"player\":{{\"name2\":\"{display}\",\"level\":50,\"hp\":100}}}}"), ("@l", lang));

    private void Exec(string sql, params (string Name, object Value)[] args)
    {
        _ = Db;
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private List<(string From, string To, string Type, string Message)> Mails()
    {
        var list = new List<(string, string, string, string)>();
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = "SELECT from_player, to_player, message_type, message FROM messages ORDER BY id;";
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.GetString(0), r.GetString(1), r.GetString(2), r.GetString(3)));
        return list;
    }

    [Fact]
    public async Task RoyalMail_ReachesTheRecipient_InTheRecipientsLanguage()
    {
        Player("prisoner_hu", "PrisonerHu", "hu");
        Player("prisoner_en", "PrisonerEn", "en");
        await InLanguage("fr", async () =>
        {
            foreach (var to in new[] { "PrisonerHu", "PrisonerEn" })
            {
                await Db.SendMessageLocalized("System", to, "system", lang => CastleLocation.ImprisonedMail(lang, CharacterSex.Female, "Ann", 1, "theft"));
                await Db.SendMessageLocalized("System", to, "system", lang => CastleLocation.PardonedMail(lang, CharacterSex.Female, "Ann"));
                await Db.SendMessageLocalized("System", to, "system", lang => CastleLocation.ExecutedMail(lang, CharacterSex.Male, "Al"));
                await Db.SendMessageLocalized("System", to, "system", lang => CastleLocation.BailSetMail(lang, CharacterSex.Male, "Al", 5000));
                await Db.SendMessageLocalized("System", to, "system", lang => CastleLocation.DethronedInCombatMail(lang, "Bob"));
                await Db.SendMessageLocalized("System", to, "system", lang => CastleLocation.DethronedBySiegeMail(lang, "Bob", "Wolves"));
            }
            return 0;
        });
        var mails = Mails();
        mails.Should().HaveCount(12);
        mails.Take(6).Select(m => m.Message).Should().Equal(
            L("hu", "castle.mail_imprisoned", L("hu", "castle.queen"), "Ann", 1, "theft"),
            L("hu", "castle.mail_pardoned", L("hu", "castle.queen"), "Ann"),
            L("hu", "castle.mail_executed", L("hu", "castle.king"), "Al"),
            L("hu", "castle.mail_bail_set", "5,000", L("hu", "castle.king"), "Al"),
            L("hu", "castle.mail_dethroned_combat", "Bob"),
            L("hu", "castle.mail_dethroned_siege", "Bob", "Wolves"));
        mails.Skip(6).Select(m => m.Message).Should().Equal(new[]
        {
            "You have been imprisoned by Queen Ann for 1 days! Crime: theft",
            "You have been pardoned by Queen Ann! You are free!",
            "You were sentenced to execution by King Al! You narrowly escaped with your life but lost 10% of your gold.",
            "Bail has been set at 5,000 gold by King Al. Use [B] Pay Bail in prison to purchase your freedom.",
            "You have been DETHRONED! Bob defeated you in combat and now sits on the throne. Your reign has ended.",
            "You have been DETHRONED! Bob was overthrown by Wolves's siege and now sits on the throne. Your reign has ended.",
        }, "the English mail reads as before");
        mails.Should().OnlyContain(m => m.From == "System" && m.Type == "system");
        string src = Src();
        src.Should().NotContain("backend.SendMessage(\"System\"", "every castle mail is built in the recipient's language")
            .And.Contain("lang => ImprisonedMail(lang, royalSex, royalName, sentence, crime)")
            .And.Contain("lang => PardonedMail(lang, royalSex, royalName)")
            .And.Contain("lang => ExecutedMail(lang, royalSex, royalName)")
            .And.Contain("lang => BailSetMail(lang, royalSex, royalName, amount)")
            .And.Contain("await backend.SendMessageLocalized(\"System\", oldKingName, \"system\", mail);")
            .And.Contain("NotifyDethronedPlayer(oldKingName, lang => DethronedInCombatMail(lang, newKingName));")
            .And.Contain("NotifyDethronedPlayer(oldKingName, lang => DethronedBySiegeMail(lang, newKingName, siegeTeam));");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("it")]
    public void LiveNotices_AreInTheReadersLanguage(string lang)
    {
        InLang("fr", () =>
        {
            string arrest = CastleLocation.ArrestNotice(lang, CharacterSex.Male, "Al", "theft", 1);
            arrest.Should().Contain(L(lang, "castle.arrest_seize")).And.Contain(L(lang, "castle.arrest_by_order", L(lang, "castle.king"), "Al"))
                .And.Contain(L(lang, "castle.arrest_sentence_one", 1)).And.StartWith("\u001b[1;31m\n").And.EndWith("\u001b[0m");
            CastleLocation.ArrestNotice(lang, CharacterSex.Male, "Al", "theft", 3).Should().Contain(L(lang, "castle.arrest_sentence_many", 3));
            CastleLocation.ExecutionBroadcast(lang, CharacterSex.Female, "Ann", "Bob").Should().Be("\u001b[1;31m" + L(lang, "castle.broadcast_executed", L(lang, "castle.queen"), "Ann", "Bob") + "\u001b[0m");
            CastleLocation.EstablishmentBroadcast(lang, CharacterSex.Male, "Al", "Inn", false).Should().Contain(L(lang, "castle.broadcast_est_closed", L(lang, "castle.king"), "Al", L(lang, "castle.est_inn")));
            CastleLocation.ProclamationBroadcast(lang, CharacterSex.Male, "Al", "hi").Should().Contain(L(lang, "castle.broadcast_proclamation", L(lang, "castle.king"), "Al", "hi"));
            CastleLocation.BountyBroadcast(lang, CharacterSex.Male, "Al", "Bob", 1000).Should().Contain(L(lang, "castle.broadcast_bounty", "1,000", "Bob", L(lang, "castle.king"), "Al"));
            CastleLocation.KnightedBroadcast(lang, CharacterSex.Female, "Ann", CharacterSex.Male, "Al").Should().Contain(L(lang, "castle.broadcast_knighted", L(lang, "castle.title_dame"), "Ann", L(lang, "castle.king"), "Al"));
            CastleLocation.RebellionExecutedBroadcast(lang, "Al").Should().Contain(L(lang, "castle.broadcast_rebel_executed", "Al"));
            CastleLocation.RebellionShamedBroadcast(lang, "Al").Should().Contain(L(lang, "castle.broadcast_rebel_shamed", "Al"));
            return 0;
        });
        if (lang == "en")
            CastleLocation.ArrestNotice("en", CharacterSex.Male, "Al", "theft", 1).Should().Be(
                "\u001b[1;31m\n  *** ROYAL GUARDS SEIZE YOU! ***\n  By order of King Al, you are under arrest!\n  Crime: theft\n  Sentence: 1 day\n  You will be sent to prison on your next action.\u001b[0m");
        string src = Src();
        src.Should().NotContain("BroadcastToAll(", "every castle broadcast is built in each reader's language")
            .And.Contain("targetSession!.IncomingMessages.Enqueue(ArrestNotice(targetSession.Context?.Language ?? \"en\",");
    }

    // ================= Electron =================

    private const string KingIds = "\"T\"|royal|treasury;\"G\"|royal|guards;\"P\"|royal|prison;\"C\"|royal|court;\"L\"|royal|law;\"M\"|royal|mail;\"X\"|royal|tax;\"Q\"|royal|establishment;\"S\"|info|info;\"V\"|info|statue;\"R\"|navigate|back;";
    private const string VisitorIds = "\"D\"|service|donate;\"H\"|info|history;\"S\"|social|audience;\"A\"|duty|guard;\"I\"|combat|throne;\"C\"|royal|throne;\"L\"|shop|armory;\"B\"|combat|siege;\"V\"|info|statue;\"R\"|navigate|back;";

    private static readonly Regex MenuItem = new(@"Key = ([^,]+), Label = (.+?), Category = ""([^""]*)"", Icon = ""([^""]*)""");

    [Fact]
    public void ElectronCastle_LabelsAreKeys_AndTheIdsAreUnchanged()
    {
        string src = Src();
        var items = MenuItem.Matches(src).ToList();
        string ids = string.Concat(items.Select(m => $"{m.Groups[1].Value}|{m.Groups[3].Value}|{m.Groups[4].Value};"));
        ids.Should().Be(KingIds + VisitorIds, "the keys the client sends and the categories and icons it styles by stay as they were");
        items.Select(m => m.Groups[2].Value).Should().OnlyContain(l => l.StartsWith("Loc.Get(\""), "every castle Electron label is keyed");
        foreach (var m in items)
        {
            string key = Regex.Match(m.Groups[2].Value, "Loc.Get\\(\"([^\"]+)\"").Groups[1].Value;
            foreach (var lang in AllLanguages) L(lang, key).Should().NotBe(key, $"{lang} {key} exists");
        }
        src.Should().Contain("description: Loc.Get(playerIsKing ? \"castle.electron_desc_king\" : \"castle.electron_desc_visitor\"),");
        src.Should().Contain("name: Loc.Get(\"castle.header\"),", "the location name is as it was at bfecaf1");
        ClientSrc("game-ui.js").Should().Contain("keywords: ['castle']").And.Contain("if (data.description) this.sceneTitle.textContent = data.description;");
    }
}
