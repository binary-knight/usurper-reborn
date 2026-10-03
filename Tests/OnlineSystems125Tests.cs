using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
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
/// v1.2.5: the online systems in the player's language: the who list and the chat frames of
/// OnlineChatSystem (a player's own words are never translated), the news NewsSystem and TeamSystem write
/// (in the writer's language, stored once and shown as stored), the offline mailbox of MailSystem, the
/// guild errors, the party fee rows, and the account messages, save type, PvP fallback name and trade
/// expiry mail of SqlSaveBackend (the mail in the recipient's language). What is stored or matched stays
/// English: message_type, the "System" sender, guild ranks and team names. Every changed row fits 79
/// columns in English and Hungarian with a 30-character name.
/// </summary>
[Collection("SharedGameSingletons")]
public class OnlineSystems125Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player or NPC can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-online-systems-{Guid.NewGuid():N}.db");
    private SqlSaveBackend? _db;
    private SqlSaveBackend Db => _db ??= new SqlSaveBackend(_path);

    public OnlineSystems125Tests() => MailSystem.ClearAllMail();

    public void Dispose()
    {
        MailSystem.ClearAllMail();
        NewsSystem.Instance.ClearCatchUpBuffer();
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

    private static void EveryRowFits(IEnumerable<string> rows, string screen)
    {
        foreach (var row in rows)
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
    }

    private static void EveryRowFits(string text, string screen) => EveryRowFits(Rows(text), screen);

    /// <summary>The Hungarian text holds none of the English text of these keys (each literal piece of the
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

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    private static string Flat(List<(string Text, string Color)> row) => string.Concat(row.Select(p => p.Text));

    private void Exec(string sql, params (string Name, object Value)[] args)
    {
        _ = Db;
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        foreach (var (n, v) in args) cmd.Parameters.AddWithValue(n, v);
        cmd.ExecuteNonQuery();
    }

    private object? Scalar(string sql)
    {
        _ = Db;
        using var c = new SqliteConnection($"Data Source={_path}"); c.Open();
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        return cmd.ExecuteScalar();
    }

    private void Player(string key, string display, string lang) =>
        Exec("INSERT INTO players (username, display_name, player_data, language, last_login) VALUES (@u, @d, @p, @l, datetime('now'));",
            ("@u", key), ("@d", display), ("@p", $"{{\"player\":{{\"name2\":\"{display}\",\"level\":50,\"hp\":100,\"gold\":0}}}}"), ("@l", lang));

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
    public void TheLongName_IsTheLongestNameAllowed()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
    }

    // ================= OnlineChatSystem =================

    private OnlineChatSystem Chat()
    {
        var osm = (OnlineStateManager)Activator.CreateInstance(typeof(OnlineStateManager), F, null, new object[] { Db, "golden" }, null)!;
        return (OnlineChatSystem)Activator.CreateInstance(typeof(OnlineChatSystem), F, null, new object[] { osm }, null)!;
    }

    private static readonly string[] WhoKeys =
    {
        "chat.who_col_player", "chat.who_col_location", "chat.who_col_connected", "chat.who_count_many", "main_street.whos_online",
    };

    [Fact]
    public async Task WhosOnline_InHungarian_HasNoEnglishFrame_AndFits79()
    {
        await Db.RegisterOnline("longone", LongName, $"Dungeon (Group: {LongName})", "Web");
        await Db.RegisterOnline("short", "Bo", "MainStreet", "Local");
        var chat = Chat();
        foreach (var lang in new[] { "en", "hu" })
        {
            var s = NewScreen();
            await InLanguage(lang, async () => { await chat.ShowWhosOnline(s.Term); return 0; });
            Capture($"who-{lang}.txt", s.Text);
            EveryRowFits(s.Text, $"who list ({lang})");
            s.Text.Should().Contain(LongName).And.Contain("Bo");
            if (lang == "hu")
            {
                NoEnglishLeft(s.Text, WhoKeys);
                s.Text.Should().Contain(L("hu", "main_street.whos_online").ToUpperInvariant())
                    .And.Contain(L("hu", "chat.who_count_many", 2)).And.Contain(L("hu", "chat.via_local"));
            }
            else
            {
                s.Text.Should().Contain("                     WHO'S ONLINE", "the English title reads as before")
                    .And.Contain($"  {"Player",-18} {"Location",-16} {"Via",-5} Connected")
                    .And.Contain("  2 players online").And.Contain("Local");
            }
        }
    }

    [Fact]
    public async Task WhosOnline_NobodyElse_InHungarian()
    {
        var chat = Chat();
        var s = NewScreen();
        await InLanguage("hu", async () => { await chat.ShowWhosOnline(s.Term); return 0; });
        s.Text.Should().Contain(L("hu", "chat.who_no_others")).And.Contain(L("hu", "chat.who_count_many", 0));
        NoEnglishLeft(s.Text, new[] { "chat.who_no_others" });
    }

    [Fact]
    public void WhoRow_ShortEntry_KeepsTheOldColumns_LongEntryWrapsInside79()
    {
        InLang("en", () =>
        {
            var rows = OnlineChatSystem.WhoRows("Bo", "Main Street", "Web", "5m", "");
            rows.Should().ContainSingle();
            Flat(rows[0]).Should().Be($"  {"Bo",-18} {"Main Street",-16} {"Web",-5} 5m", "a short entry is one row as before");
            return 0;
        });
        foreach (var lang in new[] { "en", "hu" })
        {
            InLang(lang, () =>
            {
                string tag = Loc.Get("chat.who_watching_tag", LongName);
                var rows = OnlineChatSystem.WhoRows("Baroness " + LongName, OnlineChatSystem.FormatLocation($"Dungeon (Group: {LongName})"), "Steam", "10h 59m", tag);
                rows.Count.Should().BeGreaterThan(1);
                EveryRowFits(rows.Select(Flat), $"long who entry ({lang})");
                string.Join(" ", rows.Select(Flat)).Should().Contain("Baroness " + LongName).And.Contain(tag).And.Contain("10h 59m");
                return 0;
            });
        }
    }

    [Fact]
    public void ChatFrames_InHungarian_KeepThePlayersWords_AndFit79()
    {
        const string words = "See you at the tavern";
        const string longWords = "Hello there, the tavern is open tonight and the bard plays until dawn";
        var msgs = new[]
        {
            new ChatMessage { From = LongName, Type = "chat_private", Text = words },
            new ChatMessage { From = "Server", Type = "system", Text = words },
            new ChatMessage { From = LongName, Type = "duel", Text = "" },
            new ChatMessage { From = LongName, Type = "trade", Text = "" },
            new ChatMessage { From = LongName, Type = "chat_private", Text = longWords },
        };
        foreach (var lang in new[] { "en", "hu" })
        {
            var s = NewScreen();
            InLang(lang, () => { foreach (var m in msgs) OnlineChatSystem.DisplayMessage(s.Term, m); return 0; });
            Capture($"chat-frames-{lang}.txt", s.Text);
            EveryRowFits(s.Text, $"chat frames ({lang})");
            s.Text.Should().Contain(words, "a player's own words are never translated");
            string joined = string.Join(" ", Rows(s.Text).Select(r => r.Trim()));
            joined.Should().Contain(longWords, "a long message wraps, its words unchanged");
            if (lang == "hu")
            {
                NoEnglishLeft(s.Text, new[] { "chat.duel_challenge", "chat.trade_offer" });
                s.Text.Should().Contain(L("hu", "chat.pm_from_tag", LongName)).And.Contain(L("hu", "chat.system_tag"))
                    .And.Contain(L("hu", "chat.duel_tag")).And.Contain(L("hu", "chat.trade_tag"))
                    .And.NotContain("[PM from").And.NotContain("[SYSTEM]").And.NotContain("[DUEL]").And.NotContain("[TRADE]");
            }
            else
            {
                s.Text.Should().Contain($"[PM from {LongName}] {words}").And.Contain($"[SYSTEM] {words}")
                    .And.Contain($"[DUEL] {LongName} challenges you to a duel!").And.Contain($"[TRADE] {LongName} sent you a trade offer.");
            }
        }
    }

    [Fact]
    public async Task SayAndTell_InHungarian_AreKeyed_AndFit79()
    {
        var chat = Chat();
        foreach (var lang in new[] { "en", "hu" })
        {
            var s = NewScreen();
            await InLanguage(lang, async () =>
            {
                (await chat.TryProcessCommand("/say good evening", s.Term)).Should().BeTrue();
                (await chat.TryProcessCommand($"/tell {LongName.Split(' ')[0]} see you soon", s.Term)).Should().BeTrue();
                return 0;
            });
            Capture($"say-tell-{lang}.txt", s.Text);
            EveryRowFits(s.Text, $"say and tell ({lang})");
            s.Text.Should().Contain("good evening").And.Contain("see you soon");
            if (lang == "hu")
                NoEnglishLeft(s.Text, new[] { "chat.message_sent", "chat.message_sent_offline" });
            else
                s.Text.Should().Contain("[You] good evening").And.Contain("  Message sent!")
                    .And.Contain($"[To {LongName.Split(' ')[0]}] see you soon")
                    .And.Contain("they'll see it next login).");
        }
        var stored = Mails();
        stored.Should().Contain(m => m.Type == "chat" && m.Message == "good evening", "the words are stored as typed");
    }

    // ================= NewsSystem =================

    private static List<string> CaptureNews(string lang, Action<NewsSystem> write)
    {
        var buffer = new List<string>();
        InLang(lang, () =>
        {
            NewsSystem.Instance.SetCatchUpBuffer(buffer);
            try { write(NewsSystem.Instance); }
            finally { NewsSystem.Instance.ClearCatchUpBuffer(); }
            return 0;
        });
        return buffer;
    }

    private static void WriteAllNews(NewsSystem n)
    {
        n.WriteDeathNews(LongName, "a cave troll", "Dungeon Level 12");
        n.WriteBirthNews("Mira", "Tobin", "Little Ash");
        n.WriteNaturalDeathNews(LongName, 87, "Dwarf");
        n.WriteComingOfAgeNews("Little Ash", "Mira", "Tobin");
        n.WriteBirthdayNews(LongName, 21, "Elf");
        n.WriteNPCLevelUpNews(LongName, 33, "Warrior", "Human");
        n.WriteMarriageNews("Mira", "Tobin", "Church");
        n.WriteMarriageNews("Mira", "Tobin", "Castle");
        n.WriteDivorceNews("Mira", "Tobin");
        n.WriteAffairNews("Mira", "Tobin");
        n.WriteRoyalNews(LongName, "taxes are lowered");
        n.WriteQuestNews(LongName, "Slay the wyrm", true);
        n.WriteQuestNews(LongName, "Slay the wyrm", false);
        n.WriteTeamNews("Gang Recruitment!", "Bo joined Iron Fist!");
    }

    [Fact]
    public void News_InEnglish_ReadsAsBefore()
    {
        bool sr = GameConfig.ScreenReaderMode;
        var news = CaptureNews("en", WriteAllNews);
        news.Should().Equal(new[]
        {
            $"\u2020 {LongName} was slain by a cave troll at Dungeon Level 12!",
            "\u2665 Mira and Tobin are proud parents of Little Ash!",
            $"\u26B1 {LongName}, a Dwarf of 87 years, has passed away peacefully. The soul moves on...",
            "Little Ash, child of Mira and Tobin, has come of age and joined the realm!",
            $"\U0001F382 {LongName} the Elf celebrates their 21st birthday!",
            $"\u2B06 {LongName} the Human Warrior has achieved Level 33!",
            "\u2665 Mira and Tobin were married at the Church!",
            "\u2665 Mira and Tobin were married at the Castle!",
            "\u2717 Mira and Tobin have divorced!",
            "\U0001F48B Scandal! Mira and Tobin are having a secret affair!",
            $"\u2654 King {LongName} proclaims: taxes are lowered",
            $"\u2694 {LongName} completed quest: Slay the wyrm",
            $"\u2694 {LongName} failed quest: Slay the wyrm",
            "\u2691 Gang Recruitment! Bo joined Iron Fist!",
        });
        GameConfig.ScreenReaderMode.Should().Be(sr);
    }

    [Fact]
    public void News_InHungarian_IsWrittenOnceInTheWritersLanguage_AndShownAsStored()
    {
        var news = CaptureNews("hu", WriteAllNews);
        news.Should().HaveCount(14, "each writer writes one row");
        news[0].Should().Be("\u2020 " + L("hu", "news.death", LongName, "a cave troll", "Dungeon Level 12"));
        news[4].Should().Be("\U0001F382 " + L("hu", "news.birthday", LongName, "Elf", 21, "st"));
        news[6].Should().Be("\u2665 " + L("hu", "news.marriage", "Mira", "Tobin", L("hu", "news.place_church")));
        news[7].Should().Contain(L("hu", "news.place_castle"));
        string all = string.Join("\n", news);
        NoEnglishLeft(all, new[]
        {
            "news.death", "news.birth", "news.natural_death", "news.coming_of_age", "news.birthday", "news.npc_level_up",
            "news.marriage", "news.divorce", "news.affair", "news.royal_proclaims", "news.quest_completed", "news.quest_failed",
            "news.place_church", "news.place_castle",
        });

        // shown to an English reader exactly as it was written
        var s = NewScreen();
        InLang("en", () =>
        {
            foreach (var row in news) OnlineChatSystem.WriteNewsEntry(s.Term, "  [10/03 12:00] ", row, "white");
            return 0;
        });
        string shown = string.Join(" ", Rows(s.Text).Select(r => r.Trim()));
        foreach (var row in news)
            foreach (var word in row.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                shown.Should().Contain(word);
        EveryRowFits(s.Text, "news board (hu rows, en reader)");
    }

    [Fact]
    public void NewDayMarker_KeepsItsFrame_InEveryLanguage()
    {
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            var news = CaptureNews(lang, n => n.ProcessDailyNewsMaintenance());
            news.Should().ContainSingle().Which.Should().Be($"═══ {L(lang, "news.new_day")} ═══",
                "GameEngine's catch-up summary skips rows with the frame");
        }
        CaptureNews("en", n => n.ProcessDailyNewsMaintenance()).Single().Should().Be("═══ New Day ═══");
    }

    [Fact]
    public void Gossip_InHungarian_FindsHungarianNews()
    {
        var today = (List<string>)typeof(NewsSystem).GetField("_todaysNews", F)!.GetValue(NewsSystem.Instance)!;
        var saved = new List<string>(today);
        try
        {
            var hu = CaptureNews("hu", n =>
            {
                n.WriteBirthdayNews("Ilka", 21, "Elf");
                n.WriteMarriageNews("Ilka", "Bence", "Temple");
                n.WriteNPCLevelUpNews("Ilka", 9, "Warrior", "Human");
            });
            today.Clear();
            today.AddRange(hu.Select(r => "[12:00] " + r));
            var gossip = InLang("hu", () => NewsSystem.Instance.GetRecentGossip(3));
            gossip.Should().Contain(today, "Hungarian news is gossip to a Hungarian reader");

            // every keyword of each language occurs in that language's news texts
            foreach (var lang in new[] { "es", "fr", "hu", "it" })
            {
                string texts = string.Join("\n", new[]
                {
                    "news.birth", "news.natural_death", "news.coming_of_age", "news.birthday", "news.npc_level_up",
                    "news.marriage", "news.divorce", "news.affair",
                    "npc_behavior.news_poly_union", "worldsim.news_expecting",   // v1.2.5: gossip keywords of the world simulation's news
                }.Select(k => L(lang, k)));
                foreach (var kw in L(lang, "news.gossip_keywords").Split('|'))
                    texts.Should().ContainEquivalentOf(kw, $"[{lang}] the keyword {kw} matches a news text");
            }
        }
        finally { today.Clear(); today.AddRange(saved); }
    }

    // ================= TeamSystem =================

    [Fact]
    public void TeamNews_InHungarian_AndTheTeamNameIsStoredUnchanged()
    {
        var hero = new Character { Name2 = LongName, Team = "Iron Fist", TeamPW = "pw", CTurf = false };
        var news = CaptureNews("hu", _ => new TeamSystem().QuitTeam(hero).Should().BeTrue());
        hero.Team.Should().Be("");
        string team = $"{GameConfig.NewsColorHighlight}Iron Fist{GameConfig.NewsColorDefault}";
        news.Should().ContainSingle().Which.Should().Be("\u2691 " + L("hu", "team.news_dissolved_header") + " " + L("hu", "team.news_dissolved", team));
        NoEnglishLeft(news[0], new[] { "team.news_dissolved_header", "team.news_dissolved" });

        // a team name is a stored, player-chosen name: never translated
        var founder = new Character { Name2 = LongName };
        var formed = CaptureNews("hu", _ => new TeamSystem().CreateTeam(founder, "Iron Fist", "pw").Should().BeTrue());
        founder.Team.Should().Be("Iron Fist");
        formed.Single().Should().Contain("Iron Fist").And.Contain(L("hu", "team.news_formed_header"));
    }

    [Fact]
    public void TeamNews_ShowsTheHeadline_NotTeamHeadlineColon()
    {
        var news = CaptureNews("en", n =>
        {
            new TeamSystem().QuitTeam(new Character { Name2 = "Bo", Team = "Iron Fist", TeamPW = "pw" });
            n.WriteTeamNews(Loc.Get("street_encounter.gang.news_header"), Loc.Get("street_encounter.gang.news_joined", "Bo", "Iron Fist"));
        });
        news.Should().Equal(
            $"\u2691 Gang Dissolved! Gang {GameConfig.NewsColorHighlight}Iron Fist{GameConfig.NewsColorDefault} has been disbanded!",
            "\u2691 Gang Recruitment! Bo joined Iron Fist!");
        news.Should().NotContain(r => r.Contains("Team Gang") || r.Contains("!:"));
    }

    [Fact]
    public void GangWarHeaders_AreKeyed_InFiveLanguages()
    {
        var keys = (string[])typeof(TeamSystem).GetField("GangWarHeaderKeys", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
        keys.Select(k => L("en", k)).Should().Equal("Gang War!", "Team Bash!", "Team War!", "Turf War!", "Gang Fight!", "Rival Gangs Clash!");
        foreach (var k in keys)
            foreach (var lang in new[] { "es", "fr", "hu", "it" })
                Loc.HasIn(lang, k).Should().BeTrue($"{k} in {lang}");
        NoEnglishLeft(string.Join("\n", keys.Select(k => L("hu", k))), keys);
    }

    // ================= MailSystem (the offline mailbox) =================

    private static List<string> MailRows(MailRecord mail)
    {
        var rows = new List<string>
        {
            $"[1] {mail.Subject} - {mail.Sender} ({Loc.Get("mail.status_new")})",
            Loc.Get("mail.from", mail.Sender),
            Loc.Get("mail.to", mail.Receiver),
            Loc.Get("mail.subject", mail.Subject),
        };
        rows.AddRange(mail.Lines);
        return rows;
    }

    private static void SendEveryMail()
    {
        MailSystem.SendBirthdayMail(LongName, 21);
        MailSystem.SendRoyalGuardMail(LongName, LongName, 1250);
        MailSystem.SendMarriageMail(LongName, LongName, true);
        MailSystem.SendMarriageMail(LongName, LongName, false);
        MailSystem.SendChildBirthMail(LongName, "Little Ash", true);
        MailSystem.SendChildBirthMail(LongName, "Little Ash", false);
        MailSystem.SendSystemMail(LongName, "Notice", "A line.");
    }

    private static readonly string[] MailKeys =
    {
        "mail.sender_town_council", "mail.birthday_subject", "mail.birthday_line_celebrated", "mail.birthday_line_present",
        "mail.birthday_line_choose", "mail.birthday_line_experience", "mail.birthday_line_love", "mail.birthday_line_adopt",
        "mail.birthday_line_skip", "mail.birthday_line_visit", "mail.guard_subject", "mail.guard_line_greetings",
        "mail.guard_line_watching", "mail.guard_line_deeds", "mail.guard_line_offer", "mail.guard_line_pay",
        "mail.guard_line_visit", "mail.guard_line_long_live", "mail.marriage_subject_proposal", "mail.marriage_subject_update",
        "mail.marriage_line_dearest", "mail.marriage_line_considered", "mail.marriage_line_hand", "mail.marriage_line_will_you",
        "mail.marriage_line_visit_temple", "mail.marriage_line_love", "mail.marriage_line_status_update",
        "mail.marriage_line_changed", "mail.marriage_line_check_status", "mail.marriage_line_temple_sign", "mail.sender_stork",
        "mail.child_subject", "mail.child_line_congratulations", "mail.child_line_born_son", "mail.child_line_born_daughter",
        "mail.child_line_name", "mail.child_line_healthy_he", "mail.child_line_healthy_she", "mail.child_line_visit",
        "mail.child_line_family", "mail.child_line_sign", "mail.sender_system",
    };

    [Fact]
    public void OfflineMail_ReachesAHungarianReader_InHungarian_AndFits79()
    {
        InLang("hu", () => { SendEveryMail(); return 0; });
        var mails = MailSystem.MailFor(LongName);
        mails.Should().HaveCount(7);
        var rows = InLang("hu", () => mails.SelectMany(MailRows).ToList());
        Capture("mail-hu.txt", string.Join("\n", rows));
        EveryRowFits(rows, "offline mail (hu)");
        NoEnglishLeft(string.Join("\n", rows), MailKeys);
        mails.Should().Contain(m => m.Subject == L("hu", "mail.birthday_subject") && m.Sender == L("hu", "mail.sender_town_council"));
        mails.Single(m => m.Subject == L("hu", "mail.marriage_subject_proposal")).IsProposal.Should().BeTrue();
        mails.Single(m => m.Subject == L("hu", "mail.marriage_subject_update")).IsProposal.Should().BeFalse();
    }

    [Fact]
    public void OfflineMail_ReachesAnEnglishReader_AsBefore_AndFits79()
    {
        InLang("en", () => { SendEveryMail(); return 0; });
        var mails = MailSystem.MailFor(LongName);
        EveryRowFits(InLang("en", () => mails.SelectMany(MailRows).ToList()), "offline mail (en)");
        var birthday = mails.Single(m => m.Subject == "Birthday Party!");
        birthday.Sender.Should().Be("TOWN COUNCIL");
        birthday.Lines.Should().Equal(
            "You celebrated your 21 birthday!",
            "The Town council has gracefully decided you worthy a present.",
            "",
            "Choose a gift:",
            "(E)xperience - Gain knowledge and wisdom",
            "(L)ove - Increase your charm and charisma",
            "(A)dopt a child - Expand your family",
            "(S)kip - Decline all gifts",
            "",
            "Visit the Town Council to claim your gift!");
        var guard = mails.Single(m => m.Subject == "Royal Guard Recruitment");
        guard.Lines[2].Should().Be($"I, {LongName}, ruler of this realm, have been watching");
        guard.Lines[6].Should().Be("The position pays 1250 gold per day.");
        guard.Lines.Last().Should().Be($"-- {LongName}");
        var son = mails.First(m => m.Subject == "A New Arrival!" && m.Lines.Contains("A new son has been born to you!"));
        son.Sender.Should().Be("THE STORK");
        son.Lines.Should().Contain("He appears to be healthy and strong.").And.Contain("-- The Stork");
        mails.Single(m => m.Subject == "Marriage Proposal").Lines.Should().Contain("Will you marry me and share in life's adventures?");
        mails.Single(m => m.Subject == "Marriage Update").Lines.Last().Should().Be("-- The Temple");
        mails.Single(m => m.Subject == "Notice").Sender.Should().Be("SYSTEM");
    }

    [Fact]
    public void MailAnswers_TakeTheLettersTheOptionsShow()
    {
        InLang("en", () =>
        {
            new[] { "e", "L", "a", "S", "x", "" }.Select(MailSystem.BirthdayChoice).Should().Equal("E", "L", "A", "S", "", "");
            MailSystem.YesNoChoice("y", "mail.guard_yes", "mail.guard_no").Should().Be("Y");
            MailSystem.YesNoChoice("N", "mail.marriage_yes", "mail.marriage_no").Should().Be("N");
            return 0;
        });
        InLang("hu", () =>
        {
            // Hungarian shows (T)apasztalat, (S)zerelem, (Ö)rökbefogadás, (K)ihagyás, (I)gen, (N)em
            new[] { "t", "S", "ö", "K", "E" }.Select(MailSystem.BirthdayChoice).Should().Equal("E", "L", "A", "S", "");
            MailSystem.YesNoChoice("i", "mail.guard_yes", "mail.guard_no").Should().Be("Y");
            MailSystem.YesNoChoice("Y", "mail.marriage_yes", "mail.marriage_no").Should().Be("");
            MailSystem.YesNoChoice("n", "mail.marriage_yes", "mail.marriage_no").Should().Be("N");
            return 0;
        });
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            InLang(lang, () =>
            {
                var letters = new[] { "mail.birthday_experience", "mail.birthday_love", "mail.birthday_adopt", "mail.birthday_skip" }
                    .Select(k => MailSystem.OptionLetter(Loc.Get(k))).ToList();
                letters.Should().OnlyHaveUniqueItems().And.NotContain("", $"[{lang}] every gift shows its own letter");
                MailSystem.OptionLetter(Loc.Get("mail.guard_yes")).Should().NotBe(MailSystem.OptionLetter(Loc.Get("mail.guard_no")));
                return 0;
            });
    }

    [Fact]
    public void MarriageMail_TheAnswerFollowsTheFlag_NotTheSubjectText()
    {
        Src("Systems", "MailSystem.cs").Should().Contain("if (mail.IsProposal)").And.NotContain("Subject.Contains(");
    }

    // ================= GuildSystem =================

    [Fact]
    public void GuildErrors_InHungarian_AndTheStoredRankStaysEnglish()
    {
        _ = Db;
        var guilds = new GuildSystem(_path, register: false);
        InLang("hu", () =>
        {
            guilds.CreateGuild("leader", "X", "X").Should().Be(L("hu", "guild.err_name_length"));
            guilds.WithdrawItem("nobody", 1, out var err).Should().BeNull();
            err.Should().Be(L("hu", "guild.err_not_in_guild"));
            guilds.WithdrawGold("nobody", 10).Should().Be(L("hu", "guild.err_not_in_guild"));
            guilds.DepositGold("nobody", 10).Should().Be(L("hu", "guild.err_not_in_guild"));
            guilds.CreateGuild("leader", "Iron Hand", "Iron Hand").Should().BeNull();
            guilds.AddMember("second", "Iron Hand").Should().BeNull();
            guilds.SetMemberRank("leader", "second", "Captain").Should().Be(L("hu", "guild.err_valid_ranks"));
            guilds.SetMemberRank("leader", "leader", "Officer").Should().Be(L("hu", "guild.err_own_rank"));
            guilds.SetMemberRank("leader", "second", "Officer").Should().BeNull();
            guilds.WithdrawGold("second", GuildSystem.OfficerGoldWithdrawLimit + 1).Should().Be(L("hu", "guild.err_officer_limit", $"{GuildSystem.OfficerGoldWithdrawLimit:N0}"));
            guilds.WithdrawItem("second", 999, out var missing).Should().BeNull();
            missing.Should().Be(L("hu", "guild.err_item_not_found"));
            return 0;
        });
        Scalar("SELECT rank FROM guild_members WHERE username = 'second'").Should().Be("Officer", "a rank is stored in English");
        guilds.GetMemberRank("second").Should().Be("Officer");
        InLang("en", () => guilds.WithdrawItem("nobody", 1, out var e) ?? e).Should().Be("Not in a guild.");
        NoEnglishLeft(string.Join("\n", new[] { "guild.err_name_length", "guild.err_not_in_guild", "guild.err_own_rank", "guild.err_officer_limit", "guild.err_item_not_found" }
            .Select(k => L("hu", k, "1"))), new[] { "guild.err_name_length", "guild.err_not_in_guild", "guild.err_own_rank", "guild.err_officer_limit", "guild.err_item_not_found" });
    }

    [Fact]
    public void GuildRanks_AreShownInTheReadersLanguage_StoredAndTypedInEnglish()
    {
        _ = Db;
        var before = GuildSystem.Instance;
        var instance = typeof(GuildSystem).GetProperty("Instance")!;
        var guilds = new GuildSystem(_path);
        try
        {
            guilds.CreateGuild("leader", "Iron Hand", "Iron Hand").Should().BeNull();
            guilds.AddMember("second", "Iron Hand").Should().BeNull();
            guilds.SetMemberRank("leader", "second", "Officer").Should().BeNull();
            var lookup = typeof(UsurperRemake.Server.MudChatSystem).GetMethod("HandleGuildLookup", BindingFlags.NonPublic | BindingFlags.Static)!;
            foreach (var lang in new[] { "en", "hu" })
            {
                var s = NewScreen();
                InLang(lang, () => lookup.Invoke(null, new object[] { "leader", "Iron Hand", s.Term }));
                Capture($"guild-info-{lang}.txt", s.Text);
                EveryRowFits(s.Text, $"guild info ({lang})");
                if (lang == "en")
                    s.Text.Should().Contain("[Leader]").And.Contain("[Officer]");
                else
                    s.Text.Should().Contain($"[{L("hu", "guild.rank_leader")}]").And.Contain($"[{L("hu", "guild.rank_officer")}]")
                        .And.NotContain("[Leader]").And.NotContain("[Officer]");
            }
            NoEnglishLeft(L("hu", "guild.rank_leader") + L("hu", "guild.rank_officer") + L("hu", "guild.rank_member"),
                new[] { "guild.rank_leader", "guild.rank_officer", "guild.rank_member" });
            GuildSystem.RankLabel("Member", "hu").Should().Be(L("hu", "guild.rank_member"));
            GuildSystem.RankLabel("Founder", "hu").Should().Be("Founder", "an unknown stored rank is shown as stored");
            Scalar("SELECT rank FROM guild_members WHERE username = 'second'").Should().Be("Officer", "storage stays English");
            Scalar("SELECT rank FROM guild_members WHERE username = 'leader'").Should().Be("Leader");
            Src("Server", "MudChatSystem.cs").Should().Contain("newRank = \"Officer\"").And.Contain("newRank = \"Member\"", "the typed rank words stay English");
        }
        finally { instance.SetValue(null, before); }
    }

    // ================= TeamBalanceSystem =================

    [Fact]
    public void FeeRows_EnglishReadsAsBefore_LongRowsWrapInside79()
    {
        InLang("en", () =>
        {
            var rows = TeamBalanceSystem.FeeRows("Bo", 20, 1500, "+15 levels above you");
            rows.Should().ContainSingle();
            Flat(rows[0]).Should().Be("  Bo (Lv 20): 1,500 gold (+15 levels above you)");
            return 0;
        });
        foreach (var lang in new[] { "en", "hu" })
        {
            InLang(lang, () =>
            {
                string reason = Loc.Get("team.levels_low_relationship", 20);
                var rows = TeamBalanceSystem.FeeRows(LongName, 100, 2_500_000, reason);
                EveryRowFits(rows.Select(Flat), $"fee row ({lang})");
                string.Join(" ", rows.Select(Flat)).Should().Contain(LongName).And.Contain(reason);
                if (lang == "hu") Flat(rows[0]).Should().Contain(L("hu", "chat.group_member_level", 100)).And.Contain(L("hu", "anchor_road.gold_amount", "2,500,000"));
                return 0;
            });
        }
    }

    [Fact]
    public async Task FeeInfo_Screen_InHungarian_Fits79()
    {
        var player = new Character { Name2 = "Bo", Level = 5, Gold = 100 };
        var mate = new NPC { Name1 = LongName, Name2 = LongName, Level = 60, HP = 100, MaxHP = 100 };
        foreach (var lang in new[] { "en", "hu" })
        {
            var s = NewScreen();
            await InLanguage(lang, async () => { await TeamBalanceSystem.Instance.DisplayFeeInfo(s.Term, player, new List<Character> { mate }); return 0; });
            Capture($"fee-{lang}.txt", s.Text);
            EveryRowFits(s.Text, $"party fee screen ({lang})");
            s.Text.Should().Contain(LongName);
            if (lang == "en") s.Text.Should().Contain($"  {LongName} (Lv 60): ");
            else s.Text.Should().NotContain("(Lv 60)").And.NotContain(" gold ");
        }
    }

    // ================= SqlSaveBackend: accounts =================

    [Fact]
    public async Task AccountMessages_FollowTheGateLanguage_OrTheSession()
    {
        Player("known", "Known", "en");
        Exec("UPDATE players SET password_hash = @h WHERE username = 'known';", ("@h", "x"));
        Player("banned", "Banned", "en");
        Exec("UPDATE players SET is_banned = 1, ban_reason = 'griefing the newbies', password_hash = 'x' WHERE username = 'banned';");

        (await Db.RegisterPlayer("a", "pass", null, "hu")).message.Should().Be(L("hu", "auth.err_username_len"));
        (await Db.RegisterPlayer("Valid Name", "pw", null, "hu")).message.Should().Be(L("hu", "auth.err_password_len"));
        (await Db.RegisterPlayer("Valid Name", "pa:ss", null, "hu")).message.Should().Be(L("hu", "auth.err_password_colon"));
        (await Db.RegisterPlayer("Known", "secret", null, "hu")).message.Should().Be(L("hu", "auth.err_username_taken"));
        var created = await Db.RegisterPlayer("Fresh Name", "secret", null, "hu");
        created.success.Should().BeTrue();
        created.message.Should().Be(L("hu", "auth.account_created"));
        (await Db.AuthenticatePlayer("nobody", "x", null, "hu")).message.Should().Be(L("hu", "auth.err_unknown_username"));
        (await Db.AuthenticatePlayer("Fresh Name", "wrong", null, "hu")).message.Should().Be(L("hu", "auth.err_wrong_password"));
        (await Db.AuthenticatePlayer("Fresh Name", "secret", null, "hu")).message.Should().Be(L("hu", "auth.login_ok"));
        (await Db.AuthenticatePlayer("banned", "x", null, "hu")).message.Should()
            .Be(L("hu", "auth.err_account_banned") + " " + L("hu", "auth.err_ban_reason", "griefing the newbies"), "the reason is the admin's own text");

        // no gate language: the session's
        await InLanguage("hu", async () =>
        {
            (await Db.ChangePassword("Fresh Name", "wrong", "newer")).message.Should().Be(L("hu", "auth.err_current_password"));
            (await Db.ChangePassword("Fresh Name", "secret", "abc")).message.Should().Be(L("hu", "auth.err_new_password_len"));
            (await Db.AuthenticatePlayer("nobody", "x")).message.Should().Be(L("hu", "auth.err_unknown_username"));
            return 0;
        });

        // English reads as before
        await InLanguage("en", async () =>
        {
            (await Db.AuthenticatePlayer("nobody", "x")).message.Should().Be("Unknown username. Type 'R' to register a new account.");
            (await Db.AuthenticatePlayer("banned", "x")).message.Should().Be("Your account has been banned. Reason: griefing the newbies");
            (await Db.RegisterPlayer("a", "pass")).message.Should().Be("Username must be 2-20 characters.");
            (await Db.AutoProvisionPlayer("Fresh Name")).message.Should().Be("Account already exists.");
            return 0;
        });
        NoEnglishLeft(string.Join("\n", new[] { "auth.err_unknown_username", "auth.err_wrong_password", "auth.err_account_banned", "auth.account_created" }.Select(k => L("hu", k))),
            new[] { "auth.err_unknown_username", "auth.err_wrong_password", "auth.err_account_banned", "auth.account_created" });

        string gate = Src("Server", "MudServer.cs");
        gate.Should().Contain("RegisterPlayer(username!, password!, effectiveIp, authLang)")
            .And.Contain("AuthenticatePlayer(username!, password!, effectiveIp, authLang)");
    }

    // ================= SqlSaveBackend: saves, PvP log, trade expiry =================

    [Fact]
    public async Task OnlineSaveType_AndThePvPFallbackName_InHungarian()
    {
        await Db.WriteGameData("savekey", new SaveGameData
        {
            Version = GameConfig.SaveVersion,
            Player = new PlayerData { Name1 = "savekey", Name2 = LongName, Level = 7 }
        });
        InLang("hu", () => Db.GetPlayerSaves("savekey")).Should().ContainSingle().Which.SaveType.Should().Be(L("hu", "save.type_online"));
        InLang("en", () => Db.GetPlayerSaves("savekey")).Single().SaveType.Should().Be("Online Save");
        // GameEngine's save list pads the save type to 12 columns (save.SaveType.PadRight(12))
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            L(lang, "save.type_online").Length.Should().BeLessOrEqualTo(12, $"[{lang}] the save type fits its column");

        Exec("INSERT INTO pvp_log (attacker, defender, attacker_level, defender_level, winner, gold_stolen) VALUES ('gone1', 'gone2', 5, 6, 'gone1', 10);");
        var fights = await InLanguage("hu", () => Db.GetRecentPvPFights(5));
        fights.Should().ContainSingle();
        fights[0].AttackerName.Should().Be(L("hu", "combat.unknown_name"));
        fights[0].DefenderName.Should().Be(L("hu", "combat.unknown_name"));
        (await InLanguage("en", () => Db.GetRecentPvPFights(5)))[0].AttackerName.Should().Be("Unknown");
    }

    [Fact]
    public async Task ExpiredTradePackage_IsMailedInTheSendersLanguage_AsATradeFromSystem()
    {
        Player("seller_hu", "SellerHu", "hu");
        Player("seller_en", "SellerEn", "en");
        Exec("INSERT INTO trade_offers (from_player, to_player, items_json, gold, status, created_at) VALUES ('seller_hu', 'x', '[]', 1500, 'pending', datetime('now', '-8 days'));");
        Exec("INSERT INTO trade_offers (from_player, to_player, items_json, gold, status, created_at) VALUES ('seller_en', 'x', '[]', 2500, 'pending', datetime('now', '-8 days'));");
        await InLanguage("fr", async () => { await Db.ExpireOldTradeOffers(); return 0; });

        var mails = Mails();
        mails.Should().HaveCount(2);
        mails.Should().OnlyContain(m => m.From == "System" && m.Type == "trade", "the sender and message_type are stored values");
        mails.Single(m => m.To == "SellerHu").Message.Should().Be(L("hu", "mail.trade_expired_gold", "1,500"));
        mails.Single(m => m.To == "SellerEn").Message.Should().Be("Your trade package expired and 2,500 gold was returned.");
        SqlSaveBackend.TradeExpiredMail("en", 300, true).Should().Be("Your trade package expired. 300 gold and items returned.");
        NoEnglishLeft(SqlSaveBackend.TradeExpiredMail("hu", 300, true) + "\n" + SqlSaveBackend.TradeExpiredMail("hu", 300, false),
            new[] { "mail.trade_expired_items", "mail.trade_expired_gold" });
        Scalar("SELECT COUNT(*) FROM trade_offers WHERE status = 'expired'").Should().Be(2L);
    }
}
