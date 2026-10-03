using System;
using System.Collections;
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
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the remaining town files in the player's language. The Pantheon (menu, boon list, picker, news,
/// messages to followers), the Temple (alignment labels, stat names, news, messages to a player god), Anchor Road and
/// Dormitory news and mail, Main Street (citizen list, Hall of Fame, achievements, slash bar, classic help), the
/// Dark Alley (deed list, foes, news), the Marketplace, the Sanctum, the weapon and armor shops, the Arena, the
/// Healer and the Electron menus of these files. What is stored or matched stays as it was: boon alignments and
/// boon ids, the [DIVINE] news marker, the English god rank title in shared news, the English tier title, stored
/// item names, the Main Street location name the Electron client matches, and every Electron key, category and
/// icon. Every changed row fits 79 columns in English and Hungarian with a 30-character name.
/// </summary>
[Collection("SharedGameSingletons")]
public class TownRest125Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FD = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player, NPC or player god can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-townrest-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public void Dispose()
    {
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
        // Input is not echoed here; a terminal shows the typed line and its Enter, so each read ends the prompt's row.
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 12)), _ =>
        {
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static Screen At(BaseLocation location, Character hero, params string[] lines)
    {
        var s = NewScreen(lines);
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(location, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(location, hero);
        return s;
    }

    private static async Task Run(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, FD) ?? target.GetType().GetMethod(method, F)!;
        var r = m.Invoke(target, args);
        if (r is Task t) await t;
    }

    private static async Task<T> InLanguage<T>(string lang, Func<Task<T>> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            return await body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
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
            foreach (var piece in Regex.Split(en, @"\{\d+(?:,-?\d+)?\}").Select(p => p.Trim()).Where(p => p.Count(char.IsLetter) >= 5))
                if (!hu.Contains(piece))
                    huText.Should().NotContain(piece, $"{key} is shown in Hungarian");
        }
    }

    private static Character Hero(int level = 100) => new()
    {
        Name1 = "tester", Name2 = LongName, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = 50000, MaxHP = 50000, BaseMaxHP = 50000, Mental = 100, Dexterity = 200, Charisma = 10,
        AI = CharacterAI.Human, Gold = 2_000_000_000, Sex = CharacterSex.Male, Darkness = 5000, DarkNr = 5,
    };

    private static Character God(string alignment, string config = "")
    {
        var g = Hero();
        g.IsImmortal = true;
        g.DivineName = LongName;
        g.GodLevel = GameConfig.GodMaxLevel;
        g.GodAlignment = alignment;
        g.DeedsLeft = 9;
        g.DivineBoonConfig = config;
        return g;
    }

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    private static string ClientSrc(string file) =>
        File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "electron-client", "src", file));

    /// <summary>The longest class name in the language (Mystic Shaman and its translations).</summary>
    private static string LongestClass() =>
        Enum.GetValues<CharacterClass>().Select(c => GameConfig.GetLocalizedClassName(c)).OrderByDescending(n => n.Length).First();

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

    // ================= the Pantheon =================

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task PantheonMenu_Fits_AndIsInThePlayersLanguage(string lang)
    {
        string text = await InLanguage(lang, async () =>
        {
            var pantheon = new PantheonLocation();
            var s = At(pantheon, God("Balance"));
            await Run(pantheon, "ShowPantheonMenu");
            return s.Text;
        });
        Capture($"pantheon-menu-{lang}.txt", text);
        EveryRowFits(text, $"{lang} Pantheon menu");
        text.Should().Contain(L(lang, "pantheon.menu_hall")).And.Contain(L(lang, "pantheon.menu_hall_desc"))
            .And.Contain(L(lang, "temple.align.balance"));
        if (lang == "hu") NoEnglishLeft(text, new[] { "pantheon.menu_hall", "pantheon.menu_hall_desc" });
        else text.Should().Contain("  [H] Hall of the Ascended Walk among the alpha-era founder statues");
        // A label never runs into its description
        foreach (var (label, desc) in new[] { ("pantheon.menu_hall", "pantheon.menu_hall_desc"), ("pantheon.menu_visit_manwe", "pantheon.menu_visit_manwe_desc"),
                     ("pantheon.menu_status", "pantheon.menu_status_desc"), ("pantheon.menu_renounce", "pantheon.menu_renounce_desc") })
            text.Should().NotContain(L(lang, label) + L(lang, desc).Substring(0, 3));
    }

    private static string AllBoonsConfig() =>
        DivineBoonRegistry.SerializeConfig(DivineBoonRegistry.AllBoons.Take(4).Select(b => (b.Id, 2)).ToList());

    private static Task<string> BoonScreen(string lang, string alignment, string config, params string[] input) => InLanguage(lang, async () =>
    {
        var pantheon = new PantheonLocation();
        var s = At(pantheon, God(alignment, config), input.Length > 0 ? input : new[] { "0" });
        await Run(pantheon, "ConfigureBoons");
        return s.Text;
    });

    [Theory]
    [InlineData("en", "Light")] [InlineData("en", "Dark")] [InlineData("en", "Balance")]
    [InlineData("hu", "Light")] [InlineData("hu", "Dark")] [InlineData("hu", "Balance")]
    public async Task BoonList_Fits_ForEveryAlignment_AndIsInThePlayersLanguage(string lang, string alignment)
    {
        string text = await BoonScreen(lang, alignment, AllBoonsConfig());
        Capture($"pantheon-boons-{alignment}-{lang}.txt", text);
        EveryRowFits(text, $"{lang} {alignment} boon list");
        text.Should().Contain(L(lang, "pantheon.align_any"));
        DivineBoonRegistry.AllBoons.Select(b => L(lang, "pantheon.boon_added_cost", b.CostPerTier)).Should().Contain(t => text.Contains(t));
        if (lang == "hu")
        {
            NoEnglishLeft(text, new[] { "pantheon.align_any", "pantheon.boon_cost", "pantheon.boon_added_cost" });
            text.Should().NotContain("[Any]").And.NotContain(" pts)").And.NotContain("[Light").And.NotContain("[Dark");
        }
        else text.Should().Contain("[Any]").And.Contain(" pts)");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("fr")]
    public async Task BoonList_ComparesTheStoredAlignment_NotItsLabel(string lang)
    {
        // A Light god sees a Light-only boon as available whatever the language; the boon keeps its English
        // alignment words and the god its stored GodAlignment.
        var lightOnly = DivineBoonRegistry.AllBoons.First(b => b.Alignments.SequenceEqual(new[] { "Light" }));
        string text = await BoonScreen(lang, "Light", "");
        string locked = L(lang, "pantheon.boon_locked").Trim();
        var row = Rows(text).Single(r => r.Contains(lightOnly.Name + " I"));
        row.Should().NotContain(locked, $"a Light god can take {lightOnly.Name} in {lang}");
        row.Should().Contain($"[{L(lang, "temple.align.light")}]");
        lightOnly.Alignments.Should().Equal(new[] { "Light" });
        lightOnly.IsAvailableForAlignment("Light").Should().BeTrue();
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task ChoosingABoon_StoresItsId_AndTheSaveKeepsIt(string lang)
    {
        var god = God("Light");
        var anyBoon = DivineBoonRegistry.AllBoons.First(b => b.Alignments.Length == 0);
        await InLanguage(lang, async () =>
        {
            var pantheon = new PantheonLocation();
            At(pantheon, god, "1", "0");
            await Run(pantheon, "ConfigureBoons");
            return 0;
        });
        god.DivineBoonConfig.Should().NotBeEmpty();
        var parsed = DivineBoonRegistry.ParseConfig(god.DivineBoonConfig);
        parsed.Should().OnlyContain(p => DivineBoonRegistry.GetBoon(p.boonId) != null, "the config holds boon ids");
        god.DivineBoonConfig.Should().NotContain(L("hu", "pantheon.align_any")).And.NotContain(L("hu", "temple.align.light"));
        // Saved through SaveSystem.SerializePlayer and read back from the JSON
        var ser = typeof(SaveSystem).GetMethod("SerializePlayer", F)!;
        var player = (PlayerData)ser.Invoke(SaveSystem.Instance, new object[] { god })!;
        var back = JsonSerializer.Deserialize<PlayerData>(JsonSerializer.Serialize(player))!;
        back.DivineBoonConfig.Should().Be(god.DivineBoonConfig);
        back.GodAlignment.Should().Be("Light", "the alignment is stored in English");
    }

    private static object Targets(params (string Name, int Level, string Status, bool IsPlayer)[] rows)
    {
        var type = typeof(PantheonLocation).GetNestedType("DeedTarget", BindingFlags.NonPublic)!;
        var list = (IList)Activator.CreateInstance(typeof(List<>).MakeGenericType(type))!;
        foreach (var (name, level, status, isPlayer) in rows)
        {
            var t = Activator.CreateInstance(type)!;
            type.GetProperty("Name")!.SetValue(t, name);
            type.GetProperty("Level")!.SetValue(t, level);
            type.GetProperty("Status")!.SetValue(t, status);
            type.GetProperty("IsPlayer")!.SetValue(t, isPlayer);
            list.Add(t);
        }
        return list;
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task DeedPicker_Fits_WithTheLongestNames_AndIsInThePlayersLanguage(string lang)
    {
        string text = await InLanguage(lang, async () =>
        {
            var pantheon = new PantheonLocation();
            var s = At(pantheon, God("Dark"), "0");
            string online = Loc.Get("pantheon.believer_online_tag");
            var targets = Targets(
                (LongName, 100, Loc.Get("pantheon.follows", LongName) + online, true),
                (LongName, 100, Loc.Get("pantheon.pagan") + online, true),
                ("Short", 5, Loc.Get("pantheon.follows", "Solarius"), false));
            var m = typeof(PantheonLocation).GetMethod("PickTarget", F)!;
            await (Task)m.Invoke(pantheon, new object[] { targets, Loc.Get("pantheon.recruit_pick_title"), "bright_yellow", Loc.Get("pantheon.recruit_pick_prompt") })!;
            return s.Text;
        });
        Capture($"pantheon-picker-{lang}.txt", text);
        EveryRowFits(text, $"{lang} deed picker");
        text.Should().Contain(L(lang, "pantheon.recruit_pick_title")).And.Contain(L(lang, "pantheon.pick_prompt", L(lang, "pantheon.recruit_pick_prompt")).TrimEnd());
        text.Should().Contain(L(lang, "pantheon.believer_online_tag").Trim());
        if (lang == "hu")
        {
            NoEnglishLeft(text, new[] { "pantheon.recruit_pick_title", "pantheon.pick_prompt", "pantheon.follows" });
            text.Should().NotContain("[ONLINE]").And.NotContain("to cancel");
        }
        else text.Should().Contain("RECRUIT BELIEVER").And.Contain("Target # (0 to cancel):").And.Contain("[ONLINE]");
    }

    [Fact]
    public void PickerTitles_AreKeys()
    {
        string src = Src("Locations", "PantheonLocation.cs");
        var calls = Regex.Matches(src, @"await Pick(?:Target|NPC)\((.*)").Select(m => m.Groups[1].Value).ToList();
        calls.Should().HaveCount(7);
        // The only literals left in a picker call are colour names
        foreach (var call in calls)
            Regex.Matches(call, "\"([^\"]*)\"").Select(m => m.Groups[1].Value).Where(v => !v.StartsWith("pantheon."))
                .Should().OnlyContain(v => Regex.IsMatch(v, "^(bright_[a-z]+|dark_red)$"), call);
        foreach (var key in new[] { "pantheon.recruit_pick_title", "pantheon.poison_pick_title", "pantheon.poison_pick_title_vs",
                     "pantheon.poison_pick_first", "pantheon.poison_pick_second", "pantheon.free_pick_title", "pantheon.free_pick_prompt" })
            src.Should().Contain($"Loc.Get(\"{key}\"");
    }

    [Fact]
    public void DivineNews_StartsWithTheMarker_InEveryLanguage_AndTheNewsScreenReadsIt()
    {
        foreach (var lang in AllLanguages)
            InLang(lang, () =>
            {
                foreach (var line in new[]
                         {
                             PantheonLocation.DivineNews(Loc.Get("pantheon.news_converted", LongName, LongName)),
                             PantheonLocation.DivineNews(Loc.Get("pantheon.news_ascended", LongName, PantheonLocation.GetGodTitleShared(9))),
                             PantheonLocation.DivineNews(Loc.Get("pantheon.news_fallen", LongName)),
                             PantheonLocation.ProclamationBroadcast(lang, LongName, PantheonLocation.GetGodTitleShared(3), "hello"),
                         })
                    line.Should().Contain(PantheonLocation.DivineNewsTag, $"{lang}: the news screen colours a divine line by the marker");
                return 0;
            });
        PantheonLocation.DivineNewsTag.Should().Be("[DIVINE]");
        string src = Src("Locations", "PantheonLocation.cs");
        src.Should().Contain("bool isDivine = item.Contains(DivineNewsTag);");
        Regex.Matches(src, @"Newsy\(true,\s*DivineNews\(").Should().HaveCount(4, "converted twice, ascended, fallen");
        src.Should().Contain("string newsEntry = DivineNews(Loc.Get(\"pantheon.news_proclaims\", godName, godTitle, message));");
    }

    [Fact]
    public async Task TheNewsScreen_ColoursADivineLine_InHungarian()
    {
        var before = NewsSystem.Instance.GetTodaysNews().ToList();
        string divine = InLang("hu", () => PantheonLocation.DivineNews(Loc.Get("pantheon.news_fallen", LongName)));
        var news = typeof(NewsSystem).GetField("todaysNews", F) ?? typeof(NewsSystem).GetFields(F).FirstOrDefault(f => f.FieldType == typeof(List<string>));
        news.Should().NotBeNull("NewsSystem keeps today's lines in a list");
        var list = (List<string>)news!.GetValue(NewsSystem.Instance)!;
        list.Add(divine);
        try
        {
            string raw = await InLanguage("hu", async () =>
            {
                var pantheon = new PantheonLocation();
                var s = At(pantheon, God("Light"));
                await Run(pantheon, "ShowNews");
                s.Term.StreamWriterInternal?.Flush();
                return Encoding.UTF8.GetString(s.Output.ToArray());
            });
            int at = raw.IndexOf(divine, StringComparison.Ordinal);
            at.Should().BeGreaterThan(0);
            string before2 = raw.Substring(0, at);
            before2.Substring(before2.LastIndexOf('\u001b')).Should().StartWith("\u001b[93m", "the divine line is bright yellow (the marker is found)");
        }
        finally { list.Remove(divine); }
    }

    [Fact]
    public void SharedNews_KeepsTheEnglishRankTitle_AndIsInTheWritersLanguage()
    {
        string hu = InLang("hu", () => Loc.Get("pantheon.news_ascended", LongName, PantheonLocation.GetGodTitleShared(9)));
        hu.Should().Contain(GameConfig.GodTitles[8]).And.NotContain("has ascended");
        string src = Src("Locations", "PantheonLocation.cs");
        src.Should().Contain("Loc.Get(\"pantheon.news_ascended\", currentPlayer.DivineName, GetGodTitleShared(currentPlayer.GodLevel))");
        src.Should().Contain("Loc.Get(\"pantheon.news_speaks\", currentPlayer.DivineName, godTitle, message)");
    }

    // ================= messages to another player =================

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("fr")]
    public void MessagesToAnotherSession_AreInThatLanguage(string lang)
    {
        // Built while the writer's session is French; each is in the reader's language.
        InLang("fr", () =>
        {
            PantheonLocation.PatronReconfiguredMessage(lang, LongName).Should().Contain(L(lang, "pantheon.msg_patron_reconfigured", LongName));
            PantheonLocation.NowWorshipMessage(lang, LongName).Should().Contain(L(lang, "pantheon.msg_now_worship", LongName));
            PantheonLocation.ClaimedMail(lang, LongName).Should().Be(L(lang, "pantheon.mail_claimed", LongName));
            PantheonLocation.ProclamationBroadcast(lang, LongName, "Lesser Spirit", "hi").Should().Contain(L(lang, "pantheon.news_proclaims", LongName, "Lesser Spirit", "hi"));
            TempleLocation.PrayedToYouMessage(lang, LongName, 10).Should().Contain(L(lang, "temple.msg_prayed_to_you", LongName, 10));
            TempleLocation.NewWorshipperMessage(lang, LongName).Should().Contain(L(lang, "temple.msg_new_worshipper", LongName));
            TempleLocation.GoldSacrificedMessage(lang, LongName, 1234567, 99).Should().Contain(L(lang, "temple.msg_gold_sacrificed", LongName, "1,234,567", 99));
            AnchorRoadLocation.GauntletBroadcast(lang, "Grand Champion", LongName).Should().Contain(L(lang, "anchor_road.news_gauntlet_conquered", "Grand Champion", LongName));
            return 0;
        });
        if (lang == "en")
        {
            PantheonLocation.PatronReconfiguredMessage("en", "Zed").Should().Be("\u001b[1;33m  \u2726 Your patron Zed has reconfigured their divine favors! \u2726\u001b[0m");
            TempleLocation.GoldSacrificedMessage("en", "Ann", 1500, 3).Should().Be("\u001b[1;33m  \u2726 Ann sacrificed 1,500 gold at your altar! +3 divine experience. \u2726\u001b[0m");
            AnchorRoadLocation.GauntletBroadcast("en", "Arena Master", "Ann").Should().Be("\u001b[1;33m*** Arena Master Ann has conquered the Anchor Road Gauntlet! ***\u001b[0m");
        }
    }

    [Fact]
    public void MessagesToAnotherSession_UseThatSessionsLanguage()
    {
        string pantheon = Src("Locations", "PantheonLocation.cs"), temple = Src("Locations", "TempleLocation.cs"), road = Src("Locations", "AnchorRoadLocation.cs");
        pantheon.Should().Contain("session.EnqueueMessage(PatronReconfiguredMessage(session.Context?.Language ?? \"en\", divineName));")
            .And.Contain("session.EnqueueMessage(NowWorshipMessage(session.Context?.Language ?? \"en\", godName));")
            .And.Contain("MudServer.Instance?.BroadcastLocalized(lang => ProclamationBroadcast(lang, godName, godTitle, message));")
            .And.Contain("backend.SendMessageToKeyLocalized(godName, target.Username, \"divine\",").And.Contain("lang => ClaimedMail(lang, godName));");
        temple.Should().Contain("kvp.Value.EnqueueMessage(PrayedToYouMessage(kvp.Value.Context?.Language ?? \"en\", currentPlayer.Name2, 10));")
            .And.Contain("MudServer.Instance.SendToPlayerLocalized(chosen.Username, lang => NewWorshipperMessage(lang, mortal));")
            .And.Contain("kvp.Value.EnqueueMessage(GoldSacrificedMessage(kvp.Value.Context?.Language ?? \"en\", currentPlayer.Name2, amount, power));");
        road.Should().Contain("MudServer.Instance?.BroadcastLocalized(lang => GauntletBroadcast(lang, tierTitle, champion));");
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
    public async Task TheDormitoryMurderMail_And_TheRecruitMail_AreInTheRecipientsLanguage()
    {
        Player("victim_hu", "VictimHu", "hu");
        Player("victim_en", "VictimEn", "en");
        await InLanguage("fr", async () =>
        {
            foreach (var key in new[] { "victim_hu", "victim_en" })
                await Db.SendMessageToKeyLocalized("Murderer", key, "sleep_attack", lang => DormitoryLocation.SleepMurderMail(lang, "Murderer", 1234, "Steel Sword"));
            await Db.SendMessageToKeyLocalized("Murderer", "victim_hu", "sleep_attack", lang => DormitoryLocation.SleepMurderMail(lang, "Murderer", 50, null));
            await Db.SendMessageToKeyLocalized("Zed", "victim_hu", "divine", lang => PantheonLocation.ClaimedMail(lang, "Zed"));
            return 0;
        });
        var mails = Mails();
        mails.Should().HaveCount(4);
        mails[0].Message.Should().Be(L("hu", "dormitory.mail_sleep_murder_item", "Murderer", "1,234", "Steel Sword"));
        mails[1].Message.Should().Be("Murderer murdered you in your sleep! They stole 1,234 gold and your Steel Sword.", "the English mail reads as before");
        mails[2].Message.Should().Be(L("hu", "dormitory.mail_sleep_murder", "Murderer", "50"));
        mails[3].Message.Should().Be(L("hu", "pantheon.mail_claimed", "Zed"));
        mails.Take(3).Should().OnlyContain(m => m.From == "Murderer" && m.Type == "sleep_attack");
        mails[0].Message.Should().Contain("Steel Sword", "the stolen item keeps its stored name");
        Src("Locations", "DormitoryLocation.cs").Should().Contain("SendMessageToKeyLocalized(murderer, target.Username, \"sleep_attack\",")
            .And.Contain("lang => SleepMurderMail(lang, murderer, stolenGold, stolenItemName)");
    }

    // ================= the Temple =================

    [Fact]
    public void GodAlignment_IsShownInThePlayersLanguage_FromGoodnessAndDarkness()
    {
        var light = new God { Name = "A", Goodness = 10, Darkness = 1 };
        var dark = new God { Name = "B", Goodness = 1, Darkness = 10 };
        var even = new God { Name = "C", Goodness = 5, Darkness = 5 };
        foreach (var lang in AllLanguages)
            InLang(lang, () =>
            {
                TempleLocation.GodAlignmentLabel(light).Should().Be(L(lang, "temple.align.light"));
                TempleLocation.GodAlignmentLabel(dark).Should().Be(L(lang, "temple.align.dark"));
                TempleLocation.GodAlignmentLabel(even).Should().Be(L(lang, "ui.neutral"));
                return 0;
            });
        InLang("en", () => TempleLocation.GodAlignmentLabel(light)).Should().Be("Light");
        string src = Src("Locations", "TempleLocation.cs");
        src.Should().Contain("terminal.WriteLine($\"  - {match.Name} ({GodAlignmentLabel(match)})\", \"white\");");
        src.Should().Contain("string alignColor = godSide > 0 ? \"bright_cyan\" : godSide < 0 ? \"dark_red\" : \"yellow\";");
    }

    [Fact]
    public void TempleStatNames_AreTheStatKeys()
    {
        string src = Src("Locations", "TempleLocation.cs");
        foreach (var key in new[] { "ui.stat_strength", "ui.stat_dexterity", "ui.stat_constitution", "ui.stat_intelligence", "ui.stat_wisdom", "ui.stat_charisma" })
            src.Should().Contain($"lostStat = Loc.Get(\"{key}\")");
        src.Should().NotContain("case \"Strength\":");
        foreach (var lang in AllLanguages)
            Loc.GetIn(lang, "temple.sanctum_stat_gain", Loc.GetIn(lang, "ui.stat_constitution")).Length.Should().BeLessOrEqualTo(MaxWidth);
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void NewsLines_AreInTheWritersLanguage(string lang)
    {
        var keys = new[]
        {
            "temple.news_vision", "temple.news_aurelion_destroyed", "temple.news_aurelion_saved", "temple.news_aurelion_allied",
            "temple.news_sacrificed_weapon", "temple.news_sacrificed_armor", "temple.news_desecrated", "temple.news_seal_creation",
            "temple.news_found_mira", "temple.news_joined_faith", "anchor_road.news_bounty_collected", "anchor_road.news_gang_war_unopposed",
            "anchor_road.news_gang_war_won", "anchor_road.news_gang_war_repelled", "anchor_road.news_town_taken",
            "anchor_road.news_town_abandoned", "anchor_road.news_prison_escape", "dormitory.news_murdered_sleep", "marketplace.news_purchased",
            "pantheon.news_converted", "pantheon.news_fallen",
        };
        foreach (var key in keys)
        {
            string line = InLang(lang, () => Loc.Get(key, LongName, LongName, LongName));
            line.Should().Contain(LongName).And.NotStartWith(key.Split('.')[0] + ".");
            if (lang != "en") line.Should().NotBe(L("en", key, LongName, LongName, LongName), $"{lang} {key} is translated");
        }
        if (lang == "en")
        {
            L("en", "anchor_road.news_gang_war_unopposed", "Reds", "Blues").Should().Be("Gang War! Reds took the town unopposed -- Blues had no living members left.");
            L("en", "temple.news_desecrated", "Ann", "Mortis").Should().Be("Ann desecrated the altar of Mortis! The gods are furious!");
        }
    }

    // ================= Main Street =================

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void CitizenRows_Fit_WithTheLongestNames_AndAreInThePlayersLanguage(string lang)
    {
        var rows = InLang(lang, () =>
        {
            var all = new List<string>();
            string title = "Grand Champion " + LongName;
            foreach (var cls in Enum.GetValues<CharacterClass>())
            {
                string c = GameConfig.GetLocalizedClassName(cls);
                all.AddRange(MainStreetLocation.CitizenRows("*", title, MainStreetLocation.SexTag(CharacterSex.Female), 100, c,
                    $"{Loc.Get("main_street.citizen_hp", 2_000_000_000L, 2_000_000_000L)} {Loc.Get("main_street.citizens_you_tag")}"));
                all.AddRange(MainStreetLocation.CitizenRows("-", LongName, MainStreetLocation.SexTag(CharacterSex.Male), 100, c, "@ Lizard's Training Center"));
                all.AddRange(MainStreetLocation.CitizenRows(Loc.Get("main_street.citizen_dead"), LongName, MainStreetLocation.SexTag(CharacterSex.Male), 100, c,
                    $"- {Loc.Get("main_street.rip")}"));
            }
            return all;
        });
        Capture($"citizen-rows-{lang}.txt", string.Join("\n", rows));
        EveryRowFits(rows, $"{lang} citizen list");
        string text = string.Join("\n", rows);
        if (lang == "hu")
        {
            text.Should().Contain(L("hu", "main_street.citizen_lv", 100)).And.Contain("ÉP:").And.NotContain(" Lv100").And.NotContain("HP:");
            text.Should().Contain(InLang("hu", LongestClass));
        }
        else
        {
            InLang("en", () => MainStreetLocation.CitizenRows("-", "Bob", "M", 7, "Warrior", "@ Main Street")).Should()
                .Equal(new[] { "  - Bob                M Lv  7 Warrior    @ Main Street" }, "a short English row reads as before");
            text.Should().Contain("Mystic Shaman");
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task Achievements_Fit_InEveryCategory_AndTheHeaderIsInThePlayersLanguage(string lang)
    {
        var all = new StringBuilder();
        foreach (var cat in new[] { "1", "2", "3", "4", "5", "6", "7", "" })
        {
            string page = await InLanguage(lang, async () =>
            {
                var street = new MainStreetLocation();
                var hero = Hero();
                var s = At(street, hero, cat);
                await Run(street, "ShowAchievements");
                return s.Text;
            });
            EveryRowFits(page, $"{lang} achievements {cat}");
            all.Append(page);
        }
        string text = all.ToString();
        Capture($"achievements-{lang}.txt", text);
        // The long English descriptions continue under the text, indented 7
        text.Should().Contain("Survived all 10 fights of the Anchor Road Gauntlet");
        string header = L(lang, "main_street.achieve_header", L(lang, "main_street.achieve_cat_combat")).ToUpper();
        text.Should().Contain(header);
        Rows(text).Where(r => r.Contains(header)).Should().OnlyContain(r => r.Length <= MaxWidth + 1);
        string unlocked = L(lang, "main_street.achieve_unlocked_date", "2026-10-03 23:59", 1000);
        unlocked.Length.Should().BeLessOrEqualTo(MaxWidth);
        if (lang == "en") header.Should().Be("COMBAT ACHIEVEMENTS");
        else text.Should().NotContain("ACHIEVEMENTS");
        foreach (var cat in Enum.GetValues<AchievementCategory>().Cast<AchievementCategory?>().Append(null))
            InLang(lang, () => MainStreetLocation.AchievementCategoryLabel(cat)).Should().NotStartWith("main_street.");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void SlashCommands_StayAsTyped_AndTheirHelpIsInThePlayersLanguage(string lang)
    {
        var rows = InLang(lang, () => new[]
        {
            MainStreetLocation.SlashHelpRow("/say", Loc.Get("main_street.classic_arg_message"), Loc.Get("main_street.broadcast_chat")),
            MainStreetLocation.SlashHelpRow("/tell", Loc.Get("main_street.classic_arg_player_message"), Loc.Get("main_street.private_message")),
            MainStreetLocation.SlashHelpRow("/who", "", Loc.Get("main_street.see_online")),
            MainStreetLocation.SlashHelpRow("/news", "", Loc.Get("main_street.recent_news")),
        });
        EveryRowFits(rows, $"{lang} classic help");
        rows[0].Should().StartWith("  /say ");
        rows[1].Should().StartWith("  /tell ");
        rows[2].Should().StartWith("  /who - ");
        rows[3].Should().StartWith("  /news - ");
        if (lang == "en")
            rows.Should().Equal("  /say message - Broadcast chat", "  /tell player message - Private message", "  /who - See online players", "  /news - Recent news");
        else rows[0].Should().Contain(L("hu", "main_street.classic_arg_message")).And.NotContain("message");
        string street = Src("Locations", "MainStreetLocation.cs");
        street.Should().Contain("WriteSlashCommand(\"/say\");").And.Contain("WriteSlashCommand(\"/who\");");
        Src("Locations", "MainStreetClassic.cs").Should().Contain("SlashHelpRow(\"/say\",").And.Contain("SlashHelpRow(\"/tell\",");
    }

    [Fact]
    public void HallOfFame_And_Citizens_UseKeyedPagers_AndLocalizedClasses()
    {
        string src = Src("Locations", "MainStreetLocation.cs");
        src.Should().NotContain("options.Add(\"[").And.Contain("options.Add(Loc.Get(\"main_street.nav_return\"));");
        src.Should().Contain("string onlineTag = op.IsOnline ? Loc.Get(\"main_street.online_tag\") : \"\";");
        src.Should().NotContain(".Class.ToString(), ").And.NotContain("((CharacterClass)op.ClassId).ToString()");
        foreach (var lang in AllLanguages)
            foreach (var k in new[] { "main_street.nav_prev", "main_street.nav_next", "main_street.nav_dead", "main_street.nav_alive", "main_street.nav_return" })
                L(lang, k).Should().MatchRegex(@"^\[[PNDAR]\]", $"{lang} {k} shows the key it reads");
        L("en", "main_street.online_tag").Should().Be("[ON]");
    }

    [Fact]
    public void WrapWords_StartsANewRow_BeforeAWordThatWouldPassTheWidth()
    {
        MainStreetLocation.WrapWords("aaaaa bbbbb ccccc", 11, 11).Should().Equal("aaaaa bbbbb", "ccccc");
        MainStreetLocation.WrapWords("aaaaa bbbbb ccccc dd", 12, 14).Should().Equal("aaaaa bbbbb", "ccccc dd");
        MainStreetLocation.WrapWords("aaaaa bbbbb ccccc", 20, 11).Should().Equal("aaaaa bbbbb ccccc");
        MainStreetLocation.WrapWords("", 20, 11).Should().Equal("");
    }

    [Fact]
    public void TheDevMenuNotice_IsOneKey_ForBothStreets()
    {
        Src("Locations", "MainStreetLocation.cs").Should().Contain("terminal.WriteLine(Loc.Get(\"main_street.dev_menu_removed\"), \"gray\");");
        Src("Locations", "MainStreetClassic.cs").Should().Contain("terminal.WriteLine(Loc.Get(\"main_street.dev_menu_removed\"), \"gray\");");
        L("en", "main_street.dev_menu_removed").Should().Be("  The dev menu has been removed. Use the admin console.");
    }

    // ================= the Dark Alley =================

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task EvilDeeds_Fit_AndAreInThePlayersLanguage(string lang)
    {
        string text = await InLanguage(lang, async () =>
        {
            var alley = new DarkAlleyLocation();
            var hero = Hero();
            var s = At(alley, hero, "0");
            await Run(alley, "ShowEvilDeeds");
            return s.Text;
        });
        Capture($"evil-deeds-{lang}.txt", text);
        EveryRowFits(text, $"{lang} evil deeds");
        Regex.IsMatch(text, @"\p{L}\+\d").Should().BeFalse("a deed name keeps a space before its tags");
        text.Should().Contain(L(lang, "dark_alley.deed_tag_dark", 6)).And.Contain(L(lang, "dark_alley.deed_tag_gold"))
            .And.Contain(L(lang, "dark_alley.deed_tag_risk", 10));
        if (lang == "hu")
        {
            NoEnglishLeft(text, new[] { "dark_alley.deed_tag_dark", "dark_alley.deed_tag_risk" });
            text.Should().NotContain(" Dark ").And.NotContain("%risk").And.NotContain("+gold");
        }
        else text.Should().Contain("+6 Dark ").And.Contain("+gold ").And.Contain("10%risk");
    }

    [Fact]
    public void EveryDeedThatMakesNews_HasItsNewsKey_WithTheEnglishSourceText()
    {
        var deeds = (IEnumerable)typeof(DarkAlleyLocation).GetField("AllEvilDeeds", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        int news = 0;
        foreach (var d in deeds)
        {
            var t = d.GetType();
            if (!(bool)t.GetProperty("GeneratesNews")!.GetValue(d)!) continue;
            string id = (string)t.GetProperty("Id")!.GetValue(d)!;
            string source = (string)t.GetProperty("NewsText")!.GetValue(d)!;
            string key = $"dark_alley.deed_{id}_news";
            L("en", key, "{PLAYER}").Should().Be(source, $"{key} is the English news text");
            foreach (var lang in AllLanguages.Skip(1))
                L(lang, key, LongName).Should().NotBe(L("en", key, LongName), $"{lang} {key} is translated");
            news++;
        }
        news.Should().Be(6);
        Src("Locations", "DarkAlleyLocation.cs").Should().Contain("Newsy(false, Loc.Get($\"dark_alley.deed_{deed.Id}_news\", currentPlayer.DisplayName));");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void DarkAlleyFoes_AreNamedInTheLanguage_AndMatchNoCombatNameCheck(string lang)
    {
        InLang(lang, () =>
        {
            foreach (var key in new[] { "dark_alley.mugger_name", "dark_alley.enforcer_name" })
            {
                string name = Loc.Get(key);
                foreach (var word in CombatNameChecks) name.Should().NotContain(word, $"{lang} {key} must not make the foe a boss, undead or evil one");
                name.Length.Should().BeLessOrEqualTo(GameConfig.MaxNameLength);
            }
            string pit = Loc.Get("dark_alley.pit_monster_name", "Skeleton Warrior");
            pit.Should().Contain("Skeleton Warrior", "the pit keeps the monster's own name, which the combat checks read");
            foreach (var word in CombatNameChecks.Where(w => w != "Skeleton"))
                pit.Should().NotContain(word);
            return 0;
        });
        if (lang == "en")
        {
            L("en", "dark_alley.pit_monster_name", "Orc").Should().Be("Pit Orc");
            L("en", "dark_alley.mugger_name").Should().Be("Dark Alley Mugger");
            L("en", "dark_alley.enforcer_name").Should().Be("Loan Shark Enforcer");
        }
    }

    // ================= Marketplace, Sanctum, shops, Arena, Healer =================

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task MarketBoard_And_MyListings_Fit_AndAreInThePlayersLanguage(string lang)
    {
        var market = MarketplaceSystem.Instance;
        var saved = market.Listings.ToList();
        market.ClearAllListings();
        try
        {
            var hero = Hero();
            hero.Name2 = "Seller";
            market.ListItem("Seller", new Item { Name = "Ring of the Ancient Kings", Value = 1000 }, 2_000_000_000);
            string text = await InLanguage(lang, async () =>
            {
                var loc = new MarketplaceLocation();
                var s = At(loc, hero, "", "", "");
                await Run(loc, "ShowBoard");
                await Run(loc, "ShowStatus");
                return s.Text;
            });
            Capture($"market-{lang}.txt", text);
            EveryRowFits(text, $"{lang} market board");
            text.Should().Contain($"2,000,000,000 {L(lang, "marketplace.gc")}").And.Contain(L(lang, "marketplace.posted_label", L(lang, "marketplace.posted_today")).Trim());
            if (lang == "hu") text.Should().NotContain(" gc ").And.NotContain("(posted").And.NotContain(" gold");
            else text.Should().Contain("2,000,000,000 gc ");
        }
        finally
        {
            market.ClearAllListings();
            market.Listings.AddRange(saved);
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void SanctumCharityRows_Fit_AndAreInThePlayersLanguage(string lang)
    {
        string text = InLanguage(lang, async () =>
        {
            var sanctum = new SanctumLocation();
            var s = At(sanctum, Hero());
            foreach (var label in new[] { "sanctum.menu_alms", "sanctum.menu_orphanage", "sanctum.menu_hospice" })
                await Run(sanctum, "WriteCharityOption", "A", label, 2_000_000_000L, 10, 10);
            return s.Text;
        }).GetAwaiter().GetResult();
        Capture($"sanctum-{lang}.txt", text);
        EveryRowFits(text, $"{lang} sanctum");
        text.Should().Contain(L(lang, "sanctum.option_cost", 2_000_000_000L, 10, 10));
        if (lang == "hu") NoEnglishLeft(text, new[] { "sanctum.option_cost" });
        else text.Should().Contain("  -- 2000000000 gold  (10/10 today)");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task ArmorShopRows_ShowTheArmorTag_InThePlayersLanguage(string lang)
    {
        string text = await InLanguage(lang, async () =>
        {
            var shop = new ArmorShopLocation();
            var hero = Hero();
            var s = At(shop, hero);
            await Run(shop, "ShowMainMenu");
            return s.Text;
        });
        Capture($"armor-shop-{lang}.txt", text);
        L(lang, "armor_shop.ac_tag", 120).Should().NotBeNullOrEmpty();
        if (lang == "hu") L("hu", "armor_shop.ac_tag", 5).Should().NotContain("AC:");
        else L("en", "armor_shop.ac_tag", 5).Should().Be("(AC:5)");
        string src = Src("Locations", "ArmorShopLocation.cs");
        src.Should().NotContain("(AC:").And.Contain("Loc.Get(\"armor_shop.ac_tag\", currentItem.ArmorClass)").And.Contain("Loc.Get(\"armor_shop.ac_tag\", item.Armor)");
        Src("Locations", "WeaponShopLocation.cs").Should().NotContain("(Pow:").And.NotContain("(AC:")
            .And.Contain("Loc.Get(\"weapon_shop.pow_tag\", mainHand.WeaponPower)");
        text.Should().NotBeNull();
    }

    [Fact]
    public void ArenaClassNames_AreInThePlayersLanguage()
    {
        var arena = new ArenaLocation();
        var m = typeof(ArenaLocation).GetMethod("GetClassName", F)!;
        foreach (var lang in AllLanguages)
            InLang(lang, () =>
            {
                foreach (var cls in Enum.GetValues<CharacterClass>())
                    ((string)m.Invoke(arena, new object[] { (int)cls })!).Should().Be(GameConfig.GetLocalizedClassName(cls));
                ((string)m.Invoke(arena, new object[] { 99 })!).Should().Be(Loc.Get("base.bc_unknown"));
                ((string)m.Invoke(arena, new object[] { -1 })!).Should().Be(Loc.Get("base.bc_unknown"));
                return 0;
            });
        InLang("en", () => (string)m.Invoke(arena, new object[] { (int)CharacterClass.MysticShaman })!).Should().Be("Mystic Shaman");
        InLang("en", () => (string)m.Invoke(arena, new object[] { 99 })!).Should().Be("Unknown");
    }

    [Fact]
    public void HealerCurseFallbackNames_AreKeys()
    {
        string src = Src("Locations", "HealerLocation.cs");
        src.Should().Contain("player.WeaponName ?? Loc.Get(\"base.item_type_weapon\")").And.Contain("player.ArmorName ?? Loc.Get(\"base.item_type_armor\")")
            .And.Contain("cursedItems.Add((Loc.Get(\"base.item_type_shield\"), \"shield\",");
        InLang("hu", () => Loc.Get("shop.cursed_item_healer", Loc.Get("base.item_type_shield"))).Should().Contain(L("hu", "base.item_type_shield"));
    }

    // ================= Electron =================

    /// <summary>Key | Category | Icon of each menu item at 7ddb877; the ids the client sends and styles by.</summary>
    private static readonly Dictionary<string, string> ElectronIds = new()
    {
        ["Pantheon"] = "\"S\"|info|info;\"B\"|divine|believers;\"D\"|divine|deed;\"F\"|divine|boon;\"I\"|info|rank;\"N\"|info|news;\"C\"|divine|proclaim;\"V\"|social|manwe;\"H\"|info|statue;\"R\"|danger|renounce;\"Q\"|navigate|back;",
        ["Temple"] = "GameConfig.TempleMenuWorship|faith|worship;GameConfig.TempleMenuAltars|info|altar;GameConfig.TempleMenuContribute|faith|donate;GameConfig.TempleMenuDesecrate|evil|desecrate;\"F\"|info|scripture;\"O\"|faith|confess;\"M\"|social|bishop;\"S\"|info|info;\"R\"|navigate|back;",
        ["DarkAlley"] = "\"D\"|evil|drug;\"S\"|evil|steroid;\"O\"|service|orb;\"G\"|shop|magic;\"B\"|social|beer;\"A\"|shop|alchemist;\"J\"|team|shadow;\"W\"|social|tribute;\"M\"|shop|market;\"I\"|social|informant;\"P\"|evil|pickpocket;\"C\"|social|dice;\"T\"|combat|pit;\"L\"|shop|loan;\"N\"|service|safe;\"E\"|evil|evil;\"R\"|navigate|back;",
        ["Arena"] = "\"A\"|combat|pvp;\"L\"|info|rank;\"H\"|info|history;\"S\"|info|stats;\"R\"|navigate|back;",
        ["ArmorShop"] = "\"1\"|browse|armor-body;\"2\"|browse|armor-head;\"3\"|browse|armor-arms;\"4\"|browse|armor-hands;\"5\"|browse|armor-legs;\"6\"|browse|armor-feet;\"7\"|browse|armor-waist;\"8\"|browse|armor-face;\"9\"|browse|armor-cloak;\"S\"|sell|sell;\"A\"|service|auto-buy;\"R\"|navigate|back;",
        ["Healer"] = "\"H\"|service|heal;\"F\"|service|heal-full;\"B\"|shop|potion;\"M\"|shop|mana-potion;\"N\"|shop|antidote;\"P\"|service|cure-poison;\"C\"|service|cure-disease;\"D\"|service|cure-curse;\"A\"|service|cure-addiction;\"S\"|info|info;\"R\"|navigate|back;",
        ["LoveCorner"] = "\"A\"|social|approach;\"C\"|info|children;\"D\"|social|divorce;\"E\"|social|child;\"V\"|social|gossip;\"M\"|info|marriage;\"P\"|social|relations;\"G\"|shop|gift;\"S\"|info|info;\"L\"|info|history;\"R\"|navigate|back;",
        ["WeaponShop"] = "\"1\"|browse|sword;\"2\"|browse|greatsword;\"3\"|browse|bow;\"4\"|browse|shield;\"S\"|sell|sell;\"I\"|service|identify;\"R\"|navigate|back;",
        ["Church"] = "\"C\"|faith|donate;\"B\"|faith|blessing;\"H\"|service|heal;\"M\"|social|marriage;\"F\"|faith|confess;\"V\"|info|records;\"S\"|social|bishop;\"R\"|navigate|back;",
        ["MusicShop"] = "\"B\"|browse|instrument;\"P\"|service|music;\"T\"|social|talk;\"L\"|info|scroll;\"R\"|navigate|back;",
        ["MainStreet"] = "\"D\"|explore|dungeon;\"I\"|services|inn;\"W\"|services|weapons;\"A\"|services|armor;\"M\"|services|magic;\"B\"|services|bank;\"1\"|services|healer;\"T\"|services|temple;\"E\"|explore|wilderness;\">\"|explore|outskirts;\"U\"|services|music;\"V\"|progress|training;\"2\"|progress|quests;\"H\"|progress|home;\"S\"|info|status;\"Q\"|info|quit;",
    };

    private static readonly Regex MenuItem = new(@"Key = ([^,]+), Label = (.+?), Category = ""([^""]*)"", Icon = ""([^""]*)""");

    [Theory]
    [InlineData("Pantheon")] [InlineData("Temple")] [InlineData("DarkAlley")] [InlineData("Arena")] [InlineData("ArmorShop")]
    [InlineData("Healer")] [InlineData("LoveCorner")] [InlineData("WeaponShop")] [InlineData("Church")] [InlineData("MusicShop")]
    [InlineData("MainStreet")]
    public void ElectronMenus_LabelsAreKeys_AndTheIdsAreUnchanged(string file)
    {
        var items = MenuItem.Matches(Src("Locations", file + "Location.cs")).ToList();
        string ids = string.Concat(items.Select(m => $"{m.Groups[1].Value}|{m.Groups[3].Value}|{m.Groups[4].Value};"));
        ids.Should().Be(ElectronIds[file], $"{file}: the keys the client sends and the categories and icons it styles by stay as they were");
        items.Select(m => m.Groups[2].Value).Should().OnlyContain(l => l.StartsWith("Loc.Get(\""), $"every {file} Electron label is keyed");
        foreach (var m in items)
        {
            string key = Regex.Match(m.Groups[2].Value, "Loc.Get\\(\"([^\"]+)\"").Groups[1].Value;
            foreach (var lang in AllLanguages) L(lang, key).Should().NotBe(key, $"{lang} {key} exists");
        }
    }

    [Fact]
    public void MainStreetLocationName_StaysTheEnglishSceneId_TheClientMatches()
    {
        string src = Src("Locations", "MainStreetLocation.cs");
        src.Should().Contain("ElectronBridge.EmitLocation(\"Main Street\",");
        string client = ClientSrc("game-ui.js");
        client.Should().Contain("keywords: ['main street']").And.Contain("this._setLocation(data.name);")
            .And.Contain("const scene = this.sceneMap.find(s => s.keywords.some(k => loc.includes(k)));");
        "Main Street".ToLowerInvariant().Should().Contain("main street");
    }

    [Fact]
    public void ShopBrowseItems_KeepTheirNumericKey_AndShowTheSlotOnly()
    {
        string src = Src("Locations", "WeaponShopLocation.cs");
        src.Should().Contain("Key = (i + 1).ToString(),")
            .And.Contain("Slot = Loc.Get(category == WeaponCategory.Shields ? \"base.item_type_shield\" : \"base.item_type_weapon\"),");
        string client = ClientSrc("game-ui.js");
        Regex.Matches(client, @"item\.slot\b").Should().HaveCount(1, "the shop card prints the slot and nothing compares it");
        client.Should().Contain("<span class=\"shop-item-slot\">${this._escapeHtml(item.slot)}</span>");
    }

    // ================= the keys =================

    private static readonly string[] Files =
    {
        "PantheonLocation.cs", "TempleLocation.cs", "AnchorRoadLocation.cs", "MainStreetLocation.cs", "MainStreetClassic.cs",
        "DarkAlleyLocation.cs", "DormitoryLocation.cs", "MarketplaceLocation.cs", "WeaponShopLocation.cs", "ArmorShopLocation.cs",
        "SanctumLocation.cs", "ArenaLocation.cs", "HealerLocation.cs", "LoveCornerLocation.cs", "ChurchLocation.cs", "MusicShopLocation.cs",
    };

    /// <summary>The keys this change added (keys-new-en.txt in the receipts).</summary>
    private static readonly string[] NewKeyPrefixes =
    {
        "pantheon.menu_hall", "pantheon.align_any", "pantheon.boon_cost", "pantheon.boon_added_cost", "pantheon.msg_", "pantheon.news_",
        "pantheon.pick_prompt", "pantheon.mail_claimed", "pantheon.recruit_pick_", "pantheon.poison_pick_", "pantheon.free_pick_",
        "pantheon.electron_", "temple.news_", "temple.msg_", "temple.electron_", "anchor_road.news_", "dormitory.news_", "dormitory.mail_",
        "marketplace.gc", "marketplace.money", "marketplace.news_", "sanctum.option_cost", "sanctum.you", "main_street.dev_menu_removed", "main_street.classic_arg_",
        "main_street.online_tag", "main_street.nav_", "main_street.sex_", "main_street.citizen_", "main_street.achieve_cat_",
        "main_street.god_floor", "main_street.npc_activity_default", "dark_alley.fence_shadow_rank", "dark_alley.mana_restored",
        "dark_alley.pit_monster_name", "dark_alley.mugger_name", "dark_alley.enforcer_", "dark_alley.deed_tag_", "dark_alley.deed_spread_rumors_news",
        "dark_alley.deed_poison_well_news", "dark_alley.deed_arson_market_news", "dark_alley.deed_sabotage_wagons_news",
        "dark_alley.deed_thorgrim_law_news", "dark_alley.deed_shatter_seal_news", "dark_alley.electron_", "armor_shop.ac_tag",
        "armor_shop.electron_", "weapon_shop.pow_tag", "weapon_shop.electron_", "arena.electron_", "healer.electron_",
        "love_corner.electron_", "church.electron_", "music_shop.electron_",
    };

    /// <summary>New keys whose Hungarian value may equal the English one.</summary>
    private static readonly HashSet<string> SameInHungarian = new() { "main_street.citizens_you_tag" };

    [Fact]
    public void Keys_AreInEveryLanguage_WithTheSamePlaceholders_AndNoDashes()
    {
        var langs = AllLanguages.ToDictionary(l => l, l => JsonDocument.Parse(File.ReadAllText(
            Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Localization", l + ".json"))).RootElement);
        var used = Files.SelectMany(f => Regex.Matches(Src("Locations", f), "Loc\\.Get(?:In)?\\((?:\\w+, )?\"([a-z_]+\\.[a-z0-9_.]+)\"")
            .Select(m => m.Groups[1].Value)).Where(k => !k.EndsWith("_") && !k.EndsWith(".")).Distinct().ToList();
        used.Count.Should().BeGreaterThan(400);
        var added = langs["en"].EnumerateObject().Select(p => p.Name).Where(k => NewKeyPrefixes.Any(k.StartsWith)).ToList();
        added.Count.Should().BeGreaterThan(130);
        foreach (var key in used.Concat(added).Distinct())
        {
            langs["en"].TryGetProperty(key, out var e).Should().BeTrue($"{key} is in en.json");
            var holes = Regex.Matches(e.GetString()!, @"\{\d+(?:,-?\d+)?\}").Select(m => m.Value).Distinct().OrderBy(x => x).ToList();
            foreach (var lang in AllLanguages.Skip(1))
            {
                langs[lang].TryGetProperty(key, out var v).Should().BeTrue($"{key} is in {lang}.json");
                if (!added.Contains(key)) continue;   // pre-existing keys keep their own choices (some suffixes are empty)
                v.GetString().Should().NotBeNullOrWhiteSpace($"{lang} {key}");
                Regex.Matches(v.GetString()!, @"\{\d+(?:,-?\d+)?\}").Select(m => m.Value).Distinct().OrderBy(x => x).Should().Equal(holes, $"{lang} {key} keeps the placeholders");
            }
        }
        foreach (var key in added)
        {
            foreach (var lang in AllLanguages)
                langs[lang].GetProperty(key).GetString().Should().NotContain("\u2014").And.NotContain("\u2013", $"{lang} {key}");
            if (!SameInHungarian.Contains(key))
                langs["hu"].GetProperty(key).GetString().Should().NotBe(langs["en"].GetProperty(key).GetString(), $"{key} is translated");
        }
    }
}
