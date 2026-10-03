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
using UsurperRemake.Utils;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the world simulation, maintenance and NPC AI in the player's language. Screen rows (maintenance,
/// world events, bug report, tip box) are in the session's language; mail is written in the recipient's
/// language; news is written once, in the writer's language (the server's "en" online, the player's in single
/// player and catch-up), and is still sorted by the catch-up summary and picked up as gossip. What is stored or
/// matched stays English and is shown through a display mapping: world event titles, descriptions and decrees,
/// meme names, court roles, gossip, emergent roles, NPC memories and goals. Every changed row fits 79 columns in
/// English and Hungarian with a 30-character name.
/// </summary>
[Collection("SharedGameSingletons")]
public class RestWorld125Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SF = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player or NPC can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private readonly TerminalEmulator? _prevTerminal;

    public RestWorld125Tests()
    {
        MailSystem.ClearAllMail();
        _prevTerminal = (TerminalEmulator?)typeof(TerminalEmulator).GetField("_fallbackInstance", SF)!.GetValue(null);
    }

    public void Dispose()
    {
        MailSystem.ClearAllMail();
        NewsSystem.Instance.ClearCatchUpBuffer();
        typeof(TerminalEmulator).GetField("_fallbackInstance", SF)!.SetValue(null, _prevTerminal);
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

    /// <summary>A screen that also receives what goes through TerminalEmulator.Instance (TerminalUI).</summary>
    private static Screen NewInstanceScreen()
    {
        var s = NewScreen();
        typeof(TerminalEmulator).GetField("_fallbackInstance", SF)!.SetValue(null, s.Term);
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
            foreach (var piece in Regex.Split(en, @"\{\d+(?:,-?\d+)?(?::[^}]*)?\}").Select(p => p.Trim()).Where(p => p.Count(char.IsLetter) >= 5))
                if (!hu.Contains(piece))
                    huText.Should().NotContain(piece, $"{key} is shown in Hungarian");
        }
    }

    private static object? Call(object target, string method, params object[] args) =>
        target.GetType().GetMethod(method, F)!.Invoke(target, args);

    private static async Task CallAsync(object target, string method, params object[] args) =>
        await (Task)Call(target, method, args)!;

    [Fact]
    public void TheLongName_IsTheLongestNameAllowed()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
    }

    // ================= MaintenanceSystem =================

    private static readonly string[] MaintScreenKeys =
    {
        "maint.header_title", "maint.type_scheduled", "maint.bard_songs", "maint.potions_spoiled", "maint.bank_interest_row",
        "maint.royal_processing", "maint.royal_complete", "maint.economy_processing", "maint.economy_complete",
        "maint.bank_safes_reset", "maint.town_pot", "maint.cleanup_running", "maint.cleanup_complete",
        "maint.inactive_checked", "maint.bounty_updated", "maint.guard_validated", "maint.records_updating",
        "maint.stats_updated", "maint.records_updated", "maint.news_updated",
    };

    private static MaintenanceConfig MaintConfig() => new MaintenanceConfig
    {
        BankInterest = GameConfig.DefaultBankInterest, TownPotValue = GameConfig.DefaultTownPot,
        DungeonFights = GameConfig.DefaultDungeonFights,
    };

    private static Player MaintPlayer(string lang) => new Player
    {
        Name1 = LongName, Name2 = LongName, Class = CharacterClass.Bard, Race = CharacterRace.Human,
        Healing = GameConfig.MaxHealingPotions + 10, BankGold = 1_000_000, Language = lang, HP = 10, Level = 100,
    };

    private static async Task<string> MaintenanceScreen(string lang)
    {
        return await InLanguage(lang, async () =>
        {
            var s = NewInstanceScreen();
            var maint = new MaintenanceSystem(new TerminalUI());
            typeof(MaintenanceSystem).GetField("silentMode", F)!.SetValue(maint, false);
            var player = MaintPlayer(lang);
            var config = MaintConfig();
            await CallAsync(maint, "DisplayMaintenanceHeader", false);
            await CallAsync(maint, "ProcessClassSpecificMaintenance", player, config);
            maint.ProcessHealingSpoilage(player);
            await CallAsync(maint, "ProcessRoyalSystem", config);
            await CallAsync(maint, "ProcessEconomicSystems", config);
            maint.ApplyBankInterest(player, config);
            await CallAsync(maint, "CleanupSystems", config);
            await CallAsync(maint, "UpdateSystemRecords");
            return s.Text;
        });
    }

    [Fact]
    public async Task Maintenance_InHungarian_HasNoEnglish_AndEveryRowFits()
    {
        string en = await MaintenanceScreen("en");
        string hu = await MaintenanceScreen("hu");
        Capture("maintenance-en.txt", en);
        Capture("maintenance-hu.txt", hu);
        EveryRowFits(en, "maintenance screen (en)");
        EveryRowFits(hu, "maintenance screen (hu)");
        NoEnglishLeft(hu, MaintScreenKeys);
        hu.Should().Contain(L("hu", "maint.bard_songs", LongName)).And.Contain(L("hu", "maint.type_scheduled"))
            .And.Contain(L("hu", "maint.economy_complete"));

        // English as before
        Rows(en).Should().Contain("               U S U R P E R   M A I N T E N A N C E              ");
        en.Should().Contain("Maintenance Type: SCHEDULED").And.Contain($"  {LongName}: Bard songs restored")
            .And.Contain($"  {LongName}: 5 healing potions spoiled").And.Contain("Processing royal system...")
            .And.Contain("Economic processing complete.").And.Contain($"  Town pot: {GameConfig.DefaultTownPot} gold")
            .And.Contain("  Royal guard validated").And.Contain("  News files updated");
    }

    [Fact]
    public void Maintenance_RowsNotReachedHere_FitInEnglishAndHungarian()
    {
        foreach (var lang in new[] { "en", "hu" })
        {
            var rows = new List<string>
            {
                "  " + L(lang, "maint.alive_bonus", LongName, 35_000),
                "  " + L(lang, "maint.team_wages_paid", $"{9_999_999L:N0}", 5),
                "  " + L(lang, "maint.team_wages_short", $"{9_999_999L:N0}", $"{9_999_998L:N0}"),
                "  " + L(lang, "maint.npc_left_unpaid", LongName),
                "  " + L(lang, "maint.assassin_bonus", LongName),
                "  " + L(lang, "maint.birthday_row", LongName, 120),
                "  " + L(lang, "maint.royal_limits_reset"),
                "  " + L(lang, "maint.days_in_power", 9999),
                L(lang, "maint.type", L(lang, "maint.type_forced")),
            };
            EveryRowFits(rows, $"maintenance rows ({lang})");
        }
        NoEnglishLeft(string.Join("\n", new[] { "maint.alive_bonus", "maint.team_wages_paid", "maint.team_wages_short",
            "maint.npc_left_unpaid", "maint.assassin_bonus", "maint.birthday_row", "maint.royal_limits_reset",
            "maint.days_in_power", "maint.type_forced" }.Select(k => L("hu", k, LongName, 5))),
            new[] { "maint.alive_bonus", "maint.team_wages_paid", "maint.team_wages_short", "maint.npc_left_unpaid",
                "maint.assassin_bonus", "maint.birthday_row", "maint.royal_limits_reset", "maint.days_in_power", "maint.type_forced" });
        L("en", "maint.alive_bonus", LongName, 5).Should().Be($"{LongName}: Alive bonus +5");
        L("en", "maint.team_wages_short", "10", "3").Should().Be("Team wages: Can't afford 10g! (had 3g)");
    }

    [Fact]
    public void MaintenanceMail_IsWrittenInTheRecipientsLanguage_NotTheWriters()
    {
        var maint = new MaintenanceSystem(new TerminalUI());
        InLang("fr", () =>
        {
            var hu = MaintPlayer("hu");
            maint.ProcessHealingSpoilage(hu);
            maint.ApplyBankInterest(hu, MaintConfig());
            return 0;
        });
        var mails = MailSystem.MailFor(LongName);
        mails.Should().HaveCount(2);
        var potions = mails.Single(m => m.Subject == L("hu", "maint.mail_potions_subject"));
        potions.Sender.Should().Be(L("hu", "mail.sender_system"));
        potions.Lines.Should().Equal(L("hu", "maint.mail_potions_line1"), L("hu", "maint.mail_potions_line2", 5));
        var interest = mails.Single(m => m.Subject == L("hu", "maint.mail_interest_subject"));
        interest.Lines.Should().Equal(L("hu", "maint.mail_interest_line1"),
            L("hu", "maint.mail_interest_line2", 1_000_000L * GameConfig.DefaultBankInterest / 100));
        foreach (var m in mails) EveryRowFits(m.Lines, "maintenance mail (hu)");
        NoEnglishLeft(string.Join("\n", mails.SelectMany(m => m.Lines.Append(m.Subject))),
            new[] { "maint.mail_potions_subject", "maint.mail_potions_line1", "maint.mail_potions_line2",
                "maint.mail_interest_subject", "maint.mail_interest_line1", "maint.mail_interest_line2" });

        // An English recipient gets the mail as before, from a Hungarian writer
        MailSystem.ClearAllMail();
        InLang("hu", () => { maint.ProcessHealingSpoilage(MaintPlayer("en")); return 0; });
        var en = MailSystem.MailFor(LongName).Single();
        en.Subject.Should().Be("Healing Potions");
        en.Sender.Should().Be(L("en", "mail.sender_system"));
        en.Lines.Should().Equal("Some of your extra potions seem to have spoiled during the night!", "Lost 5 healing potions due to spoilage.");
        EveryRowFits(en.Lines, "maintenance mail (en)");
    }

    [Fact]
    public void TeamDepartureMail_IsInTheRecipientsLanguage()
    {
        string en = MaintenanceSystem.TeamDepartureMail("en", LongName, 3);
        en.Should().Be($"{LongName} has left your team. \"You haven't paid me in 3 days. I'm no charity worker -- find yourself another sword arm. Maybe when your coffers aren't empty, we can talk again.\"");
        string hu = InLang("fr", () => MaintenanceSystem.TeamDepartureMail("hu", LongName, 3));
        hu.Should().Be(L("hu", "maint.mail_team_departure", LongName, 3));
        NoEnglishLeft(hu, new[] { "maint.mail_team_departure" });
        MaintenanceSystem.RecipientLanguage(MaintPlayer("hu")).Should().Be("hu");
    }
    // ================= news: catch-up buckets and gossip =================

    private static readonly string[] OtherLanguages = { "es", "fr", "hu", "it" };

    /// <summary>The catch-up bucket of a news text written in each language is the bucket of the English one.</summary>
    private static void SameCatchUpBucket(string key, params object[] args)
    {
        string en = L("en", key, args);
        int expected = GameEngine.CatchUpBucket(en);
        foreach (var lang in OtherLanguages)
            GameEngine.CatchUpBucket(L(lang, key, args)).Should().Be(expected,
                $"{key} written in {lang} (\"{L(lang, key, args)}\") sorts into the catch-up heading of the English \"{en}\"");
    }

    /// <summary>A news text written in each language is gossip for a reader of that language when the English one is.</summary>
    private static void SameGossip(string key, params object[] args)
    {
        bool expected = InLang("en", () => NewsSystem.GossipKeywordsForReader()).Any(k => L("en", key, args).Contains(k, StringComparison.OrdinalIgnoreCase));
        expected.Should().BeTrue($"{key} is gossip in English");
        foreach (var lang in OtherLanguages)
        {
            string text = L(lang, key, args);
            InLang(lang, () => NewsSystem.GossipKeywordsForReader()).Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase))
                .Should().BeTrue($"{key} written in {lang} (\"{text}\") is gossip for a {lang} reader");
        }
    }

    /// <summary>What Newsy writes while the body runs, in the writer's language.</summary>
    private static List<string> NewsWritten(string lang, Action body)
    {
        var buffer = new List<string>();
        NewsSystem.Instance.SetCatchUpBuffer(buffer);
        try { InLang(lang, () => { body(); return 0; }); }
        finally { NewsSystem.Instance.ClearCatchUpBuffer(); }
        return buffer;
    }

    // ================= WorldEventSystem =================

    private static readonly WorldEventSystem.EventType[] ShownEvents =
    {
        WorldEventSystem.EventType.KingMartialLaw, WorldEventSystem.EventType.PlagueOutbreak,
        WorldEventSystem.EventType.WinterSolstice, WorldEventSystem.EventType.AncientRelicFound,
    };

    private static List<string> EventKeys(WorldEventSystem.EventType t) =>
        new[] { "title", "desc", "decree" }.Select(p => WorldEventSystem.EventKey(t, p)).Where(k => Loc.HasIn("en", k)).ToList();

    private static string EventScreen(string lang)
    {
        var s = NewScreen();
        InLang(lang, () => { WorldEventSystem.Instance.DisplayWorldStatus(s.Term); return 0; });
        return s.Text;
    }

    [Fact]
    public void WorldEventsScreen_InHungarian_HasNoEnglish_AndEveryRowFits()
    {
        var events = WorldEventSystem.Instance;
        try
        {
            events.ClearAllEvents();
            string emptyHu = EventScreen("hu");
            emptyHu.Should().Contain(L("hu", "world_event.none_active"));
            NoEnglishLeft(emptyHu, new[] { "world_event.screen_title", "world_event.none_active", "world_event.modifiers" });

            InLang("hu", () => { foreach (var t in ShownEvents) events.ForceEvent(t, 10); return 0; });
            string hu = EventScreen("hu"), en = EventScreen("en");
            Capture("world-events-hu.txt", hu);
            Capture("world-events-en.txt", en);
            EveryRowFits(hu, "world events screen (hu)");
            EveryRowFits(en, "world events screen (en)");
            NoEnglishLeft(hu, ShownEvents.SelectMany(EventKeys).Concat(new[] { "world_event.screen_title",
                "world_event.days_remaining", "world_event.modifiers", "world_event.mod_prices", "world_event.mod_xp",
                "world_event.mod_stats", "world_event.royal_decree" }));
            hu.Should().Contain(L("hu", "world_event.king_martial_law.title")).And.Contain(L("hu", "world_event.king_martial_law.decree"));

            // English as before
            Rows(en).Should().Contain("           WORLD EVENTS").And.Contain("  * Martial Law")
                .And.Contain("    The King declares martial law. Dark Alley is closed!").And.Contain("Current Modifiers:")
                .And.Contain("Royal Decree:").And.Contain("  \"By Royal Decree: Martial law is in effect. Lawbreakers will be punished!\"")
                .And.Contain("  Gold: +30%").And.Contain("  Stats: -2");
            en.Should().MatchRegex(@"\n    \(\d+ days remaining\)");
        }
        finally { events.ClearAllEvents(); }
    }

    [Fact]
    public void WorldEventRows_FitInEnglishAndHungarian_ForEveryEvent()
    {
        foreach (var lang in new[] { "en", "hu" })
            foreach (WorldEventSystem.EventType t in Enum.GetValues(typeof(WorldEventSystem.EventType)))
            {
                var rows = UsurperRemake.UI.UIHelper.WordWrap(L(lang, WorldEventSystem.EventKey(t, "desc")), MaxWidth - 4).Select(r => "    " + r)
                    .Append("  * " + L(lang, WorldEventSystem.EventKey(t, "title")));
                if (Loc.HasIn("en", WorldEventSystem.EventKey(t, "decree")))
                    rows = rows.Append($"  \"{L(lang, WorldEventSystem.EventKey(t, "decree"))}\"");
                EveryRowFits(rows, $"{t} ({lang})");
                L(lang, WorldEventSystem.EventKey(t, "title")).Length.Should().BeLessOrEqualTo(MaxWidth - 4, $"the {t} title fits one row ({lang})");
            }
        WorldEventSystem.ScreenTitleRow(L("hu", "world_event.screen_title")).Length.Should().BeLessOrEqualTo(MaxWidth);
    }

    [Fact]
    public void WorldEvents_StoreEnglish_AndShowTheReadersLanguage_AfterSaveAndReload()
    {
        var events = WorldEventSystem.Instance;
        try
        {
            events.ClearAllEvents();
            InLang("hu", () => { events.ForceEvent(WorldEventSystem.EventType.KingTaxIncrease, 10); events.ForceEvent(WorldEventSystem.EventType.GoldRush, 10); return 0; });
            var stored = events.GetActiveEvents();
            stored.Select(e => e.Title).Should().Equal("Royal Tax Increase", "Gold Rush");
            stored.Select(e => e.Description).Should().Equal("The King has raised taxes! Shop prices increase by 20%.",
                "Gold discovered in the mines! +50% gold from all sources.");
            events.CurrentKingDecree.Should().Be("By Royal Decree: Taxes are raised to fund the kingdom's defense!");

            // Save and reload the way the save file does it (JSON), then read the screen in Hungarian
            var data = (List<WorldEventData>)typeof(SaveSystem).GetMethod("SerializeActiveEvents", F)!.Invoke(SaveSystem.Instance, null)!;
            var json = System.Text.Json.JsonSerializer.Serialize(data);
            events.ClearAllEvents();
            events.RestoreFromSaveData(System.Text.Json.JsonSerializer.Deserialize<List<WorldEventData>>(json)!, 10);
            events.GetActiveEvents().Select(e => e.Title).Should().Equal("Royal Tax Increase", "Gold Rush");
            events.CurrentKingDecree.Should().Be("By Royal Decree: Taxes are raised to fund the kingdom's defense!");
            string hu = EventScreen("hu");
            hu.Should().Contain(L("hu", "world_event.king_tax_increase.title")).And.Contain(L("hu", "world_event.gold_rush.desc"))
                .And.Contain(L("hu", "world_event.king_tax_increase.decree"));
            NoEnglishLeft(hu, EventKeys(WorldEventSystem.EventType.KingTaxIncrease).Concat(EventKeys(WorldEventSystem.EventType.GoldRush)));

            // A decree that is not one of the stored ones is shown as stored
            WorldEventSystem.DecreeLabel("A decree from an older save").Should().Be("A decree from an older save");
        }
        finally { events.ClearAllEvents(); }
    }

    [Fact]
    public void WorldEventNews_IsWrittenOnceInTheWritersLanguage_AndSortsAsTheEnglish()
    {
        var events = WorldEventSystem.Instance;
        try
        {
            events.ClearAllEvents();
            var hu = NewsWritten("hu", () => events.ForceEvent(WorldEventSystem.EventType.KingWarDeclaration, 10));
            hu.Should().Equal(L("hu", "world_event.news_event", L("hu", "world_event.king_war_declaration.title"), L("hu", "world_event.king_war_declaration.desc")),
                L("hu", "world_event.king_war_declaration.decree"));
            events.ClearAllEvents();
            var en = NewsWritten("en", () => events.ForceEvent(WorldEventSystem.EventType.KingWarDeclaration, 10));
            en.Should().Equal("Declaration of War: The King declares war! Combat XP +25%, but danger increases.",
                "By Royal Decree: War is declared against the Northern Hordes!");
        }
        finally { events.ClearAllEvents(); }

        foreach (WorldEventSystem.EventType t in Enum.GetValues(typeof(WorldEventSystem.EventType)))
        {
            if (t == WorldEventSystem.EventType.WorldBossVictory) continue;
            string id = WorldEventSystem.EventKeyId(t);
            foreach (var lang in new[] { "en" }.Concat(OtherLanguages))
            {
                string news = L(lang, "world_event.news_event", L(lang, $"world_event.{id}.title"), L(lang, $"world_event.{id}.desc"));
                GameEngine.CatchUpBucket(news).Should().Be(GameEngine.CatchUpBucket(L("en", "world_event.news_event", L("en", $"world_event.{id}.title"), L("en", $"world_event.{id}.desc"))),
                    $"the {t} news in {lang} (\"{news}\") sorts as the English one");
                string ended = L(lang, "world_event.news_ended", L(lang, $"world_event.{id}.title"));
                GameEngine.CatchUpBucket(ended).Should().Be(GameEngine.CatchUpBucket(L("en", "world_event.news_ended", L("en", $"world_event.{id}.title"))),
                    $"the end of {t} in {lang} (\"{ended}\") sorts as the English one");
            }
            if (Loc.HasIn("en", $"world_event.{id}.decree")) SameCatchUpBucket($"world_event.{id}.decree");
        }
        SameCatchUpBucket("world_event.news_plague_subsided");
        SameCatchUpBucket("world_event.news_peace_returned");
    }

    [Fact]
    public void DistantNews_IsWrittenInTheWritersLanguage_AndSortsAsTheEnglish()
    {
        var keys = new List<string>();
        foreach (var (cat, n) in new[] { ("war", 7), ("trade", 7), ("plague", 5), ("discovery", 6), ("political", 6), ("disaster", 6), ("monster", 7), ("player", 7) })
            for (int i = 1; i <= n; i++) keys.Add($"world_event.distant_{cat}_{i}");
        foreach (var key in keys)
        {
            Loc.HasIn("en", key).Should().BeTrue(key);
            SameCatchUpBucket(key, "Ashenmoor", key.Contains("player") ? LongName : "Duskhollow");
        }
        L("en", "world_event.distant_war_3", "Ashenmoor", "Duskhollow").Should().Be("A ceasefire was declared between Ashenmoor and Duskhollow.");
        L("en", "world_event.distant_player_3", "Ashenmoor", LongName).Should().Be($"The fame of {LongName} has reached even Ashenmoor.");

        var events = WorldEventSystem.Instance;
        string? prevPlayer = events.NotablePlayerName;
        try
        {
            events.NotablePlayerName = LongName;
            var written = new List<string>();
            for (int day = 5000; day < 5040; day++)
                written.AddRange(NewsWritten("hu", () => events.GenerateDistantWorldNews(day)));
            written.Should().NotBeEmpty();
            var huTexts = keys.Select(k => Loc.GetIn("hu", k)).ToList();
            foreach (var item in written)
            {
                string text = item.StartsWith("☆ ") ? item.Substring(2) : item;
                huTexts.Any(t => Regex.IsMatch(text, "^" + Regex.Escape(t).Replace(@"\{0}", ".+").Replace(@"\{1}", ".+") + "$"))
                    .Should().BeTrue($"\"{text}\" is a Hungarian distant news text");
            }
        }
        finally { events.NotablePlayerName = prevPlayer; }
    }

    [Fact]
    public void MartialLaw_ClosesTheDarkAlley_InTheReadersLanguage()
    {
        var events = WorldEventSystem.Instance;
        try
        {
            events.ClearAllEvents();
            events.ForceEvent(WorldEventSystem.EventType.KingMartialLaw, 10);
            InLang("hu", () => events.IsLocationAccessible("Dark Alley")).Should().Be((false, L("hu", "world_event.martial_law_closed")));
            InLang("en", () => events.IsLocationAccessible("Dark Alley")).Should().Be((false, "The Dark Alley is closed under martial law!"));
        }
        finally { events.ClearAllEvents(); }
    }
    [Fact]
    public void WorldBossVictoryEvent_IsStoredInEnglish_AndShownInTheReadersLanguage()
    {
        var events = WorldEventSystem.Instance;
        try
        {
            events.ClearAllEvents();
            InLang("hu", () => { events.ForceEvent(WorldEventSystem.EventType.WorldBossVictory, 10); return 0; });
            var evt = events.GetActiveEvents().Single();
            evt.Title.Should().Be(L("en", "world_boss.victory_event_title"));
            evt.Description.Should().Be(L("en", "world_boss.victory_event_desc"));
            string fr = EventScreen("fr");
            fr.Should().Contain(L("fr", "world_boss.victory_event_title"));
            Regex.Replace(fr, @"\s+", " ").Should().Contain(L("fr", "world_boss.victory_event_desc"), "the description is shown, wrapped at 79 columns");
        }
        finally { events.ClearAllEvents(); }
    }
}
