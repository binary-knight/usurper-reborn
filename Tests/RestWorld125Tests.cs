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
        foreach (var lang in new[] { "es", "fr", "it" })
            EveryRowFits(await MaintenanceScreen(lang), $"maintenance screen ({lang})");
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
        foreach (var lang in AllLanguages)
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
        foreach (var lang in AllLanguages)
            EveryRowFits(new[] { L(lang, "maint.mail_potions_line1"), L(lang, "maint.mail_potions_line2", 999_999),
                L(lang, "maint.mail_interest_line1"), L(lang, "maint.mail_interest_line2", 999_999_999L) }, $"maintenance mail ({lang})");
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

    // v1.2.5 review: the rows this piece writes fit 79 columns in every language
    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

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
            foreach (var lang in new[] { "es", "fr", "it" })
                EveryRowFits(EventScreen(lang), $"world events screen ({lang})");
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
        foreach (var lang in AllLanguages)
            foreach (WorldEventSystem.EventType t in Enum.GetValues(typeof(WorldEventSystem.EventType)))
            {
                var rows = UsurperRemake.UI.UIHelper.WordWrap(L(lang, WorldEventSystem.EventKey(t, "desc")), MaxWidth - 4).Select(r => "    " + r)
                    .Append("  * " + L(lang, WorldEventSystem.EventKey(t, "title")));
                if (Loc.HasIn("en", WorldEventSystem.EventKey(t, "decree")))
                    rows = rows.Concat(UsurperRemake.UI.UIHelper.WordWrap($"\"{L(lang, WorldEventSystem.EventKey(t, "decree"))}\"", MaxWidth - 2).Select(r => "  " + r));
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
                string text = item.StartsWith("\u2606 ") ? item.Substring(2) : item;
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
            EveryRowFits(fr, "world events screen (fr, the victory description is 87 columns)");
        }
        finally { events.ClearAllEvents(); }
    }
    // ================= CulturalMemeSystem =================

    private static readonly string[] MemeIds =
    {
        "dungeon_danger", "bandit_fear", "plague_dread", "gold_rush", "merchant_bounty", "crafting_craze", "divine_blessing",
        "spiritual_awakening", "holy_pilgrimage", "tax_outrage", "throne_doubt", "freedom_call", "festival_spirit",
        "love_season", "dance_craze", "storytelling_nights", "battle_call", "arms_race", "hero_worship",
        "ancient_prophecy", "dungeon_treasure", "strange_omens",
    };

    private static CulturalMeme MemeFromTemplate(string id)
    {
        var templates = (CulturalMemeTemplate[])typeof(CulturalMemeSystem).GetField("MemeTemplates", SF)!.GetValue(null)!;
        var t = templates.Single(x => x.Id == id);
        return new CulturalMeme { Id = t.Id, Name = t.Name, Description = t.Description, Category = t.Category, GlobalStrength = 0.6f };
    }

    [Fact]
    public void Memes_StoreEnglish_EvenWhenWrittenInHungarian_AndKeepItThroughSaveAndReload()
    {
        var templates = (CulturalMemeTemplate[])typeof(CulturalMemeSystem).GetField("MemeTemplates", SF)!.GetValue(null)!;
        templates.Select(t => t.Id).Should().BeEquivalentTo(MemeIds);
        var meme = InLang("hu", () => MemeFromTemplate("tax_outrage"));
        meme.Name.Should().Be("Tax Outrage");
        meme.Description.Should().Be("Anger over the king's taxes grows");
        InLang("hu", () => MemeFromTemplate("dungeon_danger")).Name.Should().Be("Dungeon Peril");
        // A template made while a Hungarian writer runs stores English too (the templates are made once, in
        // whatever language runs first)
        var made = InLang("hu", () => new CulturalMemeTemplate("throne_doubt", MemeCategory.Unrest, new Dictionary<string, float>()));
        made.Name.Should().Be("Throne Doubt");
        made.Description.Should().Be("Doubts about the ruler spread");

        var sys = new CulturalMemeSystem();
        var active = (List<CulturalMeme>)typeof(CulturalMemeSystem).GetField("_activeMemes", F)!.GetValue(sys)!;
        active.Add(meme);
        var json = System.Text.Json.JsonSerializer.Serialize(sys.ExportSaveData());
        var restored = new CulturalMemeSystem();
        restored.RestoreFromSaveData(System.Text.Json.JsonSerializer.Deserialize<CulturalMemeSaveData>(json));
        var back = ((List<CulturalMeme>)typeof(CulturalMemeSystem).GetField("_activeMemes", F)!.GetValue(restored)!).Single();
        back.Name.Should().Be("Tax Outrage");
        InLang("hu", () => CulturalMemeSystem.NameLabel(back)).Should().Be(L("hu", "meme.tax_outrage.name"));
        InLang("hu", () => CulturalMemeSystem.DescriptionLabel(back)).Should().Be(L("hu", "meme.tax_outrage.desc"));
        CulturalMemeSystem.NameLabel(new CulturalMeme { Id = "from_an_old_save", Name = "Old Idea" }).Should().Be("Old Idea");
    }

    [Fact]
    public void MemeNews_IsWrittenInTheWritersLanguage_AndSortsAsTheEnglish()
    {
        var sys = new CulturalMemeSystem();
        var active = (List<CulturalMeme>)typeof(CulturalMemeSystem).GetField("_activeMemes", F)!.GetValue(sys)!;
        List<string> news = new();
        for (int i = 0; i < 60 && news.Count == 0; i++)
        {
            var dead = MemeFromTemplate("throne_doubt");
            dead.GlobalStrength = 0.01f;
            active.Add(dead);
            news = NewsWritten("hu", () => sys.DecayMemes());
        }
        news.Should().Equal(L("hu", "meme.news_faded", L("hu", "meme.throne_doubt.name")));
        NoEnglishLeft(news[0], new[] { "meme.news_faded", "meme.throne_doubt.name" });

        foreach (var id in MemeIds)
        {
            foreach (var lang in new[] { "en" }.Concat(OtherLanguages))
            {
                string place = InLang(lang, () => GameEngine.NpcPlaceLabel("Main Street"));
                var pairs = new[]
                {
                    (L(lang, "meme.news_new", place, L(lang, $"meme.{id}.name"), L(lang, $"meme.{id}.desc")),
                     L("en", "meme.news_new", "Main Street", L("en", $"meme.{id}.name"), L("en", $"meme.{id}.desc"))),
                    (L(lang, "meme.news_spreading", L(lang, $"meme.{id}.name")), L("en", "meme.news_spreading", L("en", $"meme.{id}.name"))),
                    (L(lang, "meme.news_faded", L(lang, $"meme.{id}.name")), L("en", "meme.news_faded", L("en", $"meme.{id}.name"))),
                };
                foreach (var (written, english) in pairs)
                    GameEngine.CatchUpBucket(written).Should().Be(GameEngine.CatchUpBucket(english), $"\"{written}\" ({lang}) sorts as \"{english}\"");
            }
        }
        L("en", "meme.news_new", "Main Street", "Gold Rush", "Everyone's chasing fortune")
            .Should().Be("A new idea is stirring in Main Street: \"Gold Rush\" -- Everyone's chasing fortune");
    }

    // ================= SocialInfluenceSystem =================

    private static NPC BrainNpc(string name, string location)
    {
        var npc = new NPC { Name1 = name, Name2 = name, Level = 10, HP = 100, MaxHP = 100, BaseMaxHP = 100, CurrentLocation = location };
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        npc.EmotionalState = npc.Brain.Emotions;
        return npc;
    }

    private static readonly string[] SocialNewsKeys =
    {
        "social.news_warning", "social.news_praise", "social.news_witnessed_attack", "social.news_witnessed_steal",
        "social.news_witnessed_help", "social.news_witnessed_challenge", "social.news_witnessed_murder",
        "social.news_witnessed_defend", "social.news_witnessed_heal", "social.news_witnessed_brawl",
        "social.news_witnessed_other", "social.news_recruited", "social.news_role_known", "social.news_new_calling",
        "social.news_heroism", "social.news_dark_whispers",
    };

    [Fact]
    public void WitnessNews_IsWrittenInTheWritersLanguage_AndTheMemoryStaysEnglish()
    {
        var npcs = new List<NPC> { BrainNpc("Witness One", "Main Street"), BrainNpc("Witness Two", "Main Street") };
        var news = NewsWritten("hu", () => SocialInfluenceSystem.RecordWitnesses(npcs, "Main Street", LongName, "Bo", WitnessEventType.SawTheft));
        news.Should().Equal(L("hu", "social.news_witnessed_steal", LongName, "Bo", L("hu", "location.name.MainStreet")));
        NoEnglishLeft(news[0], new[] { "social.news_witnessed_steal" });
        npcs[0].Brain.Memory.AllMemories.Select(e => e.Description).Should().Contain($"Saw {LongName} steal from Bo");

        var en = NewsWritten("en", () => SocialInfluenceSystem.RecordWitnesses(npcs, "Main Street", LongName, "Bo", WitnessEventType.SawBrawl));
        en.Should().Equal($"Several townsfolk witnessed {LongName} brawl with Bo at the Main Street");

        foreach (var key in SocialNewsKeys)
            SameCatchUpBucket(key, LongName, "Bo", key.Contains("witnessed") ? "Main Street" : "Merchant");
        InLang("hu", () => SocialInfluenceSystem.RoleLabel("Merchant")).Should().Be(L("hu", "social.role_merchant"));
        InLang("hu", () => SocialInfluenceSystem.FactionLabel(Faction.TheFaith)).Should().Be(L("hu", "faction.name_faith"));
        SocialInfluenceSystem.RoleLabel("Unheard Of").Should().Be("Unheard Of");
        L("en", "social.news_role_known", LongName, L("en", "social.role_defender")).Should().Be($"{LongName} has become known as the town's Defender");
    }

    // ================= EnhancedNPCBehaviors =================

    private static readonly string[] AffairKeys =
    {
        "npc_behavior.affair_lovers", "npc_behavior.affair_rendezvous", "npc_behavior.affair_connection",
        "npc_behavior.affair_flirting", "npc_behavior.affair_nervous", "npc_behavior.affair_composed",
    };

    [Fact]
    public void AffairMessages_AreInThePlayersLanguage_AndWrapIn79Columns()
    {
        var npc = new NPC { Name1 = LongName, Name2 = LongName, SpouseName = LongName };
        InLang("hu", () => EnhancedNPCBehaviors.ProcessAffairAttempt(npc, new Player { Name2 = "Bo" }, 1f).Message)
            .Should().Be(L("hu", "npc_behavior.affair_unresponsive"));
        InLang("en", () => EnhancedNPCBehaviors.ProcessAffairAttempt(npc, new Player { Name2 = "Bo" }, 1f).Message)
            .Should().Be("They seem unresponsive.");

        foreach (var lang in AllLanguages)
            foreach (var key in AffairKeys.Append("npc_behavior.divorce_found_out").Append("npc_behavior.divorce_leaving"))
            {
                var rows = UsurperRemake.UI.UIHelper.WordWrap(L(lang, key, LongName, LongName), MaxWidth - 2).Select(r => "  " + r);
                EveryRowFits(rows, $"{key} ({lang})");
            }
        NoEnglishLeft(string.Join("\n", AffairKeys.Select(k => L("hu", k, LongName))), AffairKeys);
        L("en", "npc_behavior.affair_composed", "Bo").Should().Be("Bo maintains their composure. \"I'm married, you know.\"");

        // The dialogue writes them wrapped
        string vn = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "VisualNovelDialogueSystem.cs"));
        Regex.Matches(vn, Regex.Escape("terminal.WriteLine($\"  {affairResult.Message}\")")).Count.Should().Be(0);
        Regex.Matches(vn, Regex.Escape("UIHelper.WriteWrapped(terminal!, affairResult.Message, \"  \")")).Count.Should().Be(5);
        vn.Should().Contain("UIHelper.WriteWrapped(terminal!, divorceCheck.Reason, \"  \")");
    }

    [Fact]
    public void RomanceNews_IsGossip_AndSortsAsTheEnglish_InEveryLanguage()
    {
        foreach (var key in new[] { "npc_behavior.news_wedding", "npc_behavior.news_poly_union", "npc_behavior.news_scandal_married",
                     "npc_behavior.news_scandal_left", "npc_behavior.news_scandal_tryst" })
        {
            SameCatchUpBucket(key, LongName, "Bo", "Al");
            SameGossip(key, LongName, "Bo", "Al");
        }
        foreach (var key in new[] { "npc_behavior.news_gang_ceased", "npc_behavior.news_recruited", "npc_behavior.news_converted",
                     "npc_behavior.news_gang_challenge", "npc_behavior.news_left_team", "npc_behavior.news_turf_challenge",
                     "npc_behavior.news_round_results" })
            SameCatchUpBucket(key, LongName, "Bo", "Al");
        L("en", "npc_behavior.news_wedding", "Bo", "Al").Should().Be("Wedding Bells! Bo and Al have gotten married!");
        L("en", "npc_behavior.news_converted", "Bo", "Solarius", "Al").Should().Be("Bo was converted to the faith of Solarius by Al");
    }

    // ================= GoalSystem and the gossip pool =================

    private static readonly string[] GossipKeys =
    {
        "goal.gossip_rich", "goal.gossip_seized", "goal.gossip_city", "goal.gossip_followers", "goal.gossip_blood",
        "goal.gossip_revenge", "worldsim.gossip_wave_rage", "worldsim.gossip_wave_panic", "worldsim.gossip_wave_celebration",
        "worldsim.gossip_wave_grief", "worldsim.gossip_brawl", "worldsim.gossip_stole", "worldsim.gossip_bested",
    };

    [Fact]
    public void Gossip_IsPooledInEnglish_AndToldInTheWritersLanguage()
    {
        WorldSimulator.ClearGossipPool();
        try
        {
            InLang("hu", () => { WorldSimulator.AddGossip("goal.gossip_blood", LongName, "Bo"); WorldSimulator.AddGossip("goal.gossip_blood", LongName, "Bo"); return 0; });
            WorldSimulator.GossipPoolTexts().Should().Equal($"{LongName} took blood for blood from Bo");
        }
        finally { WorldSimulator.ClearGossipPool(); }

        string hu = InLang("hu", () => Loc.Get("worldsim.news_gossip", "Bo", WorldSimulator.GossipText("worldsim.gossip_brawl", new object[] { LongName, "Al", "Main Street" })));
        hu.Should().Be(L("hu", "worldsim.news_gossip", "Bo", L("hu", "worldsim.gossip_brawl", LongName, "Al", L("hu", "location.name.MainStreet"))));
        NoEnglishLeft(hu, new[] { "worldsim.news_gossip", "worldsim.gossip_brawl" });
        InLang("en", () => Loc.Get("worldsim.news_gossip", "Bo", WorldSimulator.GossipText("worldsim.gossip_wave_rage", new object[] { "Inn", "Al" })))
            .Should().Be("Bo is telling anyone who'll listen: \"A wave of rage swept through the Inn -- started by Al\"");

        foreach (var key in GossipKeys)
            foreach (var lang in new[] { "en" }.Concat(OtherLanguages))
            {
                string written = L(lang, "worldsim.news_gossip", "Bo", L(lang, key, key.Contains("wave") ? "Main Street" : LongName, "Al", "Main Street"));
                string english = L("en", "worldsim.news_gossip", "Bo", L("en", key, key.Contains("wave") ? "Main Street" : LongName, "Al", "Main Street"));
                GameEngine.CatchUpBucket(written).Should().Be(GameEngine.CatchUpBucket(english), $"\"{written}\" ({lang}) sorts as \"{english}\"");
            }
        foreach (var key in new[] { "goal.news_fortune", "goal.news_controls_city", "goal.news_alliance", "goal.news_avenged", "goal.news_settled" })
            SameCatchUpBucket(key, LongName, "Bo");
        L("en", "goal.news_avenged", LongName, L("en", "goal.target_enemy")).Should().Be($"{LongName} has avenged the blood of their kin. their enemy is dead.");
    }
    // ================= WorldSimulator, DailySystemManager, WorldInitializerSystem, WorldSimService =================

    private static readonly (string Key, object[] Args)[] WorldNews =
    {
        ("worldsim.news_permadeath", new object[] { LongName, "Bo" }),
        ("worldsim.news_respawned", new object[] { LongName }),
        ("worldsim.news_natural_death", new object[] { LongName, 77 }),
        ("worldsim.news_immigrant", new object[] { "An", "Elf", LongName }),
        ("worldsim.news_orphan_taken", new object[] { LongName, "Bo", "Al" }),
        ("worldsim.news_orphan_guard", new object[] { LongName }),
        ("worldsim.news_orphan_realm", new object[] { LongName }),
        ("worldsim.news_expecting", new object[] { LongName, "Bo" }),
        ("team.news_joined", new object[] { LongName, "Ocean Wardens" }),
        ("worldsim.news_team_formed", new object[] { LongName, "Ocean Wardens", "Bo" }),
        ("worldsim.news_team_recruited", new object[] { LongName, "Bo", "Ocean Wardens" }),
        ("worldsim.news_team_conquered", new object[] { "Ocean Wardens", 12, 30 }),
        ("worldsim.news_boss_defeated", new object[] { LongName, "Goblin Chieftain" }),
        ("worldsim.news_monster_slain", new object[] { LongName, "Goblin", 7, 120 }),
        ("worldsim.news_purchased", new object[] { LongName, "Long Sword" }),
        ("worldsim.news_training", new object[] { LongName }),
        ("worldsim.news_hunt_victory", new object[] { LongName, "Bo" }),
        ("worldsim.home_1", new object[] { LongName }), ("worldsim.home_2", new object[] { LongName }),
        ("worldsim.home_3", new object[] { LongName }), ("worldsim.home_4", new object[] { LongName }),
        ("worldsim.home_5", new object[] { LongName }), ("worldsim.home_6", new object[] { LongName }),
        ("worldsim.news_love_disease", new object[] { LongName }),
        ("worldsim.news_love_seen", new object[] { LongName }),
        ("worldsim.news_blessing", new object[] { LongName }),
        ("worldsim.news_offering", new object[] { LongName }),
        ("worldsim.news_divine_wrath", new object[] { LongName }),
        ("worldsim.news_desecrated", new object[] { LongName }),
        ("worldsim.news_bank_deposit", new object[] { LongName }),
        ("worldsim.news_bank_guard", new object[] { LongName }),
        ("marketplace.news_npc_listed", new object[] { LongName, "Long Sword" }),
        ("marketplace.news_npc_bought", new object[] { LongName, "Long Sword", "Bo" }),
        ("worldsim.court.guard_joined", new object[] { LongName }),
        ("worldsim.news_pickpocket", new object[] { LongName, 50, "Bo" }),
        ("worldsim.news_inn_brawl", new object[] { LongName, "Bo" }),
        ("worldsim.news_inn_drinks", new object[] { LongName, "Bo" }),
        ("worldsim.news_team_disbanded_solo", new object[] { "Ocean Wardens", LongName }),
        ("worldsim.news_member_abandoned", new object[] { LongName, "Ocean Wardens" }),
        ("worldsim.news_team_disbanded", new object[] { "Ocean Wardens" }),
        ("worldsim.news_team_war", new object[] { "Ocean Wardens", "Tide Breakers", "Main Street" }),
        ("worldsim.news_team_victorious", new object[] { "Ocean Wardens", "Tide Breakers" }),
        ("worldsim.news_town_control", new object[] { "Ocean Wardens" }),
        ("worldsim.news_tension", new object[] { LongName, "Bo", "Main Street" }),
        ("worldsim.news_caught_pickpocket", new object[] { LongName, "Bo", "Main Street", 40 }),
        ("worldsim.news_public_challenge", new object[] { LongName, "Bo", "Main Street", "Bo" }),
        ("worldsim.news_treasury_bleeds", new object[] { 500 }),
        ("worldsim.news_new_day", new object[0]),
        ("daily.news_loan_default", new object[] { LongName }),
        ("world_init.news_controls", new object[] { "Ocean Wardens" }),
        ("world_init.history_founded", new object[] { 3, LongName, "Ocean Wardens", 4 }),
        ("world_init.history_left_for_throne", new object[] { 25, LongName, "Ocean Wardens" }),
        ("world_init.history_city", new object[] { 40, "Ocean Wardens" }),
        ("world_init.history_guard", new object[] { 51, LongName, "Sir " + LongName }),
        ("world_init.history_bank_guard", new object[] { 55, LongName }),
        ("world_init.history_conquered", new object[] { 30, LongName, 8 }),
        ("world_init.history_slain", new object[] { 30, LongName }),
        ("world_init.history_level", new object[] { 30, LongName, 20 }),
        ("world_init.history_arrived", new object[] { LongName }),
    };

    [Fact]
    public void WorldNews_InEveryLanguage_SortsAsTheEnglish_AndHasNoEnglishInHungarian()
    {
        foreach (var (key, args) in WorldNews)
        {
            // the place argument is shown by its place name in the writer's language
            foreach (var lang in new[] { "en" }.Concat(OtherLanguages))
            {
                var shown = args.Select(a => a is string s && s == "Main Street" ? (object)WorldSimulator.PlaceIn(lang, s) : a).ToArray();
                string written = L(lang, key, shown);
                GameEngine.CatchUpBucket(written).Should().Be(GameEngine.CatchUpBucket(L("en", key, args)),
                    $"{key} written in {lang} (\"{written}\") sorts as the English \"{L("en", key, args)}\"");
            }
            NoEnglishLeft(L("hu", key, args), new[] { key });
        }
        foreach (var key in new[] { "worldsim.news_natural_death", "worldsim.news_orphan_guard", "worldsim.news_orphan_realm", "worldsim.news_expecting" })
            SameGossip(key, LongName, 77);

        // The throne's news sorts under Royal for a king in every language
        foreach (var lang in new[] { "en" }.Concat(OtherLanguages))
        {
            GameEngine.CatchUpBucket(L(lang, "world_init.news_reign", L(lang, "castle.king"), LongName, 30)).Should().Be(2, $"the reign news ({lang})");
            GameEngine.CatchUpBucket(L(lang, "world_init.news_reign", L(lang, "castle.queen"), LongName, 30)).Should().Be(2, $"a queen's reign news ({lang})");
            GameEngine.CatchUpBucket(L(lang, "world_init.history_claimed", 25, L(lang, "castle.queen"), LongName)).Should().Be(2, $"a queen's throne news ({lang})");
            GameEngine.CatchUpBucket(L(lang, "world_init.history_claimed", 25, L(lang, "castle.king"), LongName)).Should().Be(2, $"the throne news ({lang})");
            GameEngine.CatchUpBucket(L(lang, "combat.news_god_ascended", LongName, L(lang, "god.title.5"))).Should()
                .Be(GameEngine.CatchUpBucket(L("en", "combat.news_god_ascended", LongName, L("en", "god.title.5"))), $"the god news ({lang})");
        }

        // English as before
        L("en", "worldsim.news_natural_death", "Bo", 77).Should().Be("Bo has passed away peacefully at the age of 77. The soul moves on...");
        L("en", "worldsim.news_immigrant", "An", "Elf", "Bo").Should().Be("An Elf traveler named Bo has arrived in town.");
        L("en", "worldsim.news_monster_slain", "Bo", "Goblin", 7, 120).Should().Be("Bo slew a Goblin (Lv7) and earned 120 gold.");
        L("en", "world_init.history_founded", 3, "Bo", "Ocean Wardens", 4).Should().Be("Day 3: Bo founded 'Ocean Wardens' with 4 followers");
        L("en", "worldsim.news_tension", "Bo", "Al", "Main Street").Should().Be("Tensions are rising between Bo and Al at the Main Street.");
        GodText.Title(5).Should().Be(GameConfig.GodTitles[4], "the god title in the news is the same English as before");
    }

    [Fact]
    public void WorldSimulatorMail_IsInTheRecipientsLanguage_NotTheWriters()
    {
        const string when = "2026-10-03 12:00 UTC";
        string hu = InLang("fr", () => WorldSimulator.SpouseDeathNotice("hu", LongName, null, null, "Main Street", null, when));
        hu.Should().Be(L("hu", "worldsim.spouse_death_notice", LongName, L("hu", "combat.killer_unknown_forces"), L("hu", "location.name.MainStreet"), when));
        string oldAge = InLang("fr", () => WorldSimulator.SpouseDeathNotice("hu", null, null, "worldsim.cause_old_age", null, "worldsim.place_home", when));
        oldAge.Should().Be(L("hu", "worldsim.spouse_death_notice", L("hu", "worldsim.your_spouse"), L("hu", "worldsim.cause_old_age"), L("hu", "worldsim.place_home"), when));
        NoEnglishLeft(oldAge, new[] { "worldsim.spouse_death_notice", "worldsim.your_spouse", "worldsim.cause_old_age", "worldsim.place_home" });
        WorldSimulator.SpouseDeathNotice("en", "Bo", "a Goblin", null, "Main Street", null, when)
            .Should().Be($"Grave news: your spouse Bo has died. Cause: a Goblin. Location: Main Street. Time: {when}. The Town Crier extends the realm's condolences.");
        WorldSimulator.SpouseDeathNotice("en", "Bo", null, "worldsim.cause_old_age", null, "worldsim.place_home", when)
            .Should().Contain("Cause: old age. Location: their home.");
        WorldSimulator.SpouseDeathNotice("en", "Bo", "", null, " ", null, when).Should().Contain("Cause: unknown forces. Location: parts unknown.");
        InLang("fr", () => WorldSimulator.SpouseDeathNotice("hu", "Bo", "Goblin", null, "the dungeon", null, when)).Should().Contain(L("hu", "worldsim.place_dungeon"));
        WorldSimulator.SpouseDeathNotice("en", "Bo", "Goblin", null, "the dungeon", null, when).Should().Contain("Location: the dungeon.");

        InLang("fr", () => WorldSimulator.SleepMurderMail("hu", LongName, 1234, "Long Sword"))
            .Should().Be(L("hu", "worldsim.mail_sleep_murder_item", LongName, $"{1234:N0}", L("hu", "item.long_sword")));   // v1.2.5 (data-items2): the item in the sleeper's language too
        WorldSimulator.SleepMurderMail("en", "Bo", 1234, null).Should().Be($"Bo murdered you in your sleep! Lost {1234:N0} gold.");
        WorldSimulator.SleepMurderMail("en", "Bo", 1234, "Long Sword").Should().Be($"Bo murdered you in your sleep! Lost {1234:N0} gold and Long Sword.");
        L("en", "worldsim.mail_widowed", "Bo").Should().Be("Your beloved Bo has passed away. You are now widowed.");
        NoEnglishLeft(L("hu", "worldsim.mail_widowed", LongName), new[] { "worldsim.mail_widowed" });

        // The world simulation sends them through the backend's per-recipient language helpers
        string src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "WorldSimulator.cs"));
        src.Should().Contain("SendMessageToKeyLocalized(\"The Town Crier\", username, \"death\"")
            .And.Contain("backend.SendMessageLocalized(\"System\", deceased.SpouseName, \"system\"")
            .And.Contain("SendMessageToKeyLocalized(attackerNPC.Name2, sleeper.Username, \"sleep_attack\"");
    }

    [Fact]
    public void OrphanBackstory_AndCourtRoles_AreStoredInEnglish_AndShownInTheReadersLanguage()
    {
        string stored = L("en", "worldsim.orphan_backstory", "Mira", "Tor");
        stored.Should().Be("Both parents lost. Mother: Mira, Father: Tor.");
        InLang("hu", () => CastleLocation.OrphanBackstoryText(stored)).Should().Be(L("hu", "worldsim.orphan_backstory", "Mira", "Tor"));
        InLang("hu", () => CastleLocation.OrphanBackstoryText("A story of its own.")).Should().Be("A story of its own.");
        string src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "WorldSimulator.cs"));
        Regex.Matches(src, Regex.Escape("BackgroundStory = Loc.GetIn(\"en\", \"worldsim.orphan_backstory\", child.Mother, child.Father)")).Count.Should().Be(2);

        var sim = (WorldSimulator)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(WorldSimulator));
        var king = King.CreateNewKing("Bo", CharacterAI.Computer, CharacterSex.Male);
        king.CourtMembers.Clear();
        InLang("hu", () => { typeof(WorldSimulator).GetMethod("InitializeCourtMembers", F)!.Invoke(sim, new object[] { king }); return 0; });
        king.CourtMembers.Select(m => m.Role).Should().Equal("Royal Advisor", "Court Steward", "Marshal", "Spymaster", "Treasurer");
        InLang("hu", () => king.CourtMembers.Select(m => CastleLocation.CourtRoleLabel(m.Role)).ToList())
            .Should().Equal(L("hu", "castle.d3_role_advisor"), L("hu", "castle.court_role_steward"), L("hu", "castle.court_role_marshal"),
                L("hu", "castle.court_role_spymaster"), L("hu", "castle.court_role_treasurer"));
    }

    // ================= BugReportSystem and HintSystem =================

    [Fact]
    public void BugReportTitle_AndTipBox_AreInTheReadersLanguage_AsWideAsBefore()
    {
        BugReportSystem.TitleRow(L("en", "bug_report.title")).Should().Be("                         BUG REPORT");
        BugReportSystem.TitleRow(L("hu", "bug_report.title")).Length.Should().BeLessOrEqualTo(MaxWidth);
        L("hu", "bug_report.title").Should().NotBe("BUG REPORT");

        HintSystem.BoxTop("TIP").Should().Be("┌─── TIP ────────────────────────────────────────────────────────────────────┐");
        foreach (var lang in new[] { "es", "fr", "hu", "it" })
            HintSystem.BoxTop(L(lang, "hint.tip_label")).Length.Should().Be(78, $"the {lang} tip box top is as wide as the box");

        var s = NewScreen();
        InLang("hu", () => HintSystem.Instance.TryShowHint(HintSystem.HINT_INVENTORY, s.Term, new HashSet<string>()));
        Capture("tip-box-hu.txt", s.Text);
        Rows(s.Text).Should().Contain(HintSystem.BoxTop(L("hu", "hint.tip_label")));
        s.Text.Should().NotContain(" TIP ");
        EveryRowFits(s.Text, "tip box (hu)");
    }
    // ================= DailySystemManager and PermadeathHelper rows =================

    [Fact]
    public void DailyRows_ThatCouldPass79_AreWrapped_AndGriefStagesAreInTheReadersLanguage()
    {
        var rows = new (string Key, object[] Args)[]
        {
            ("daily.loan_interest", new object[] { $"{9_999_999L:N0}", $"{99_999_999L:N0}", 99 }),
            ("daily.royal_debt_early", new object[] { $"{99_999_999L:N0}", 99, GameConfig.RoyalLoanChivalryLossEarly }),
            ("daily.royal_debt_bounty", new object[] { 99, GameConfig.RoyalLoanChivalryLossMid }),
            ("daily.royal_debt_late", new object[] { 999, GameConfig.RoyalLoanChivalryLossLate }),
        };
        foreach (var lang in AllLanguages)
        {
            foreach (var (key, args) in rows)
                EveryRowFits(UsurperRemake.UI.UIHelper.WordWrap(L(lang, key, args)), $"{key} ({lang})");
            string grief = InLang(lang, () => Loc.Get("daily.grief_evolved",
                DailySystemManager.GriefStageLabel(GriefStage.Bargaining), DailySystemManager.GriefStageLabel(GriefStage.Depression)));
            EveryRowFits(UsurperRemake.UI.UIHelper.WordWrap(grief), $"grief row ({lang})");
            if (lang == "hu")
            {
                grief.Should().Be(L("hu", "daily.grief_evolved", L("hu", "daily.grief_stage_bargaining"), L("hu", "daily.grief_stage_depression")));
                grief.Should().NotContain("Bargaining").And.NotContain("Depression");
            }
            else if (lang == "en") grief.Should().Be("Your grief has evolved... (Bargaining -> Depression)");
        }

        string src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "DailySystemManager.cs"));
        Regex.Matches(src, Regex.Escape("WriteRows(terminal, Loc.Get(\"daily.loan_interest\"")).Count.Should().Be(2);
        foreach (var key in new[] { "royal_debt_early", "royal_debt_bounty", "royal_debt_late" })
            src.Should().Contain($"WriteRows(terminal, Loc.Get(\"daily.{key}\"");
        Regex.Matches(src, Regex.Escape("GriefStageLabel(previousStage), GriefStageLabel(grief.CurrentStage)")).Count.Should().Be(2);
    }

    [Fact]
    public void BloodMoonBroadcast_AndEulogy_AreInEachReadersLanguage_AndFit79()
    {
        DailySystemManager.BloodMoonBroadcast("hu").Should().Contain(L("hu", "daily.blood_moon_broadcast"));
        DailySystemManager.BloodMoonBroadcast("en").Should().Be($"\r\n\u001b[1;31m  \u2605 {L("en", "daily.blood_moon_broadcast")} \u2605\u001b[0m\r\n");
        File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "DailySystemManager.cs"))
            .Should().Contain("MudServer.Instance?.BroadcastLocalized(BloodMoonBroadcast)");

        foreach (var lang in AllLanguages)
        {
            string eulogy = InLang("fr", () => PermadeathHelper.EulogyBroadcast(lang, LongName, 100, CharacterClass.Warrior, LongName));
            var shown = Rows(Regex.Replace(eulogy, "\u001b\\[[0-9;]*m", "")).Where(r => r.Length > 0).ToList();
            EveryRowFits(shown, $"eulogy broadcast ({lang})");
            string expectedClass = InLang(lang, () => GameConfig.GetLocalizedClassName(CharacterClass.Warrior));
            string flat = Regex.Replace(string.Join(" ", shown), @"\s+", " ");
            flat.Should().Contain(Regex.Replace(L(lang, "permadeath.eulogy", LongName, 100, expectedClass, LongName), @"\s+", " "));
        }
        PermadeathHelper.EulogyBroadcast("en", "Bo", 5, CharacterClass.Warrior, "Al")
            .Should().Be($"\u001b[1;31m\r\n  *** {L("en", "permadeath.eulogy", "Bo", 5, "Warrior", "Al")} ***\r\n\u001b[0m");
        File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "PermadeathHelper.cs"))
            .Should().Contain("UIHelper.WriteWrapped(terminal, Loc.Get(\"permadeath.legacy_recorded\"), \"  \")");
        foreach (var lang in AllLanguages)
            EveryRowFits(UsurperRemake.UI.UIHelper.WordWrap(L(lang, "permadeath.legacy_recorded"), MaxWidth - 2).Select(r => "  " + r), $"legacy row ({lang})");
    }
    [Fact]
    public void TimeOfDayRows_AndBloodMoonBroadcast_Fit79_InEveryLanguage()
    {
        foreach (var lang in AllLanguages)
        {
            foreach (var place in new[] { "dungeon", "surface" })
                foreach (var time in new[] { "dawn", "morning", "afternoon", "evening", "night" })
                    EveryRowFits(new[] { L(lang, $"daily.{place}_{time}") }, $"daily.{place}_{time} ({lang})");
            EveryRowFits(Rows(Regex.Replace(DailySystemManager.BloodMoonBroadcast(lang), "\u001b\\[[0-9;]*m", "")), $"blood moon broadcast ({lang})");
            // CombatEngine writes the restore hint two columns in (CombatEngine.cs:22367-22368)
            EveryRowFits(new[] { "  " + L(lang, "permadeath.restore_hint"), "  " + L(lang, "permadeath.restore_hint2") }, $"restore hint ({lang})");
        }
    }
}
