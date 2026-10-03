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
}
