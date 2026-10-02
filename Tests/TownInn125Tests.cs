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
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the Inn, the Bank, Love Street and the street encounters in the player's language. Companion rows and the
/// combat skills list, room guards, the sleeper list and the mail a murdered sleeper gets (in the victim's language);
/// the bank summary, news and guard names; the courtesans and gigolos (race, description, intro), gifts, mingle and
/// gossip rows, the BBS and Electron menus; the street foes' names and lines and the news they leave. Personal names
/// (courtesans, gigolos, bounty hunters, fallback gangs) stay as they are. What is stored stays English: the court
/// role "Advisor", the paid partner id "courtesan_" + name. Every changed row fits 79 columns in English and Hungarian
/// with a 30-character name and the longest names.
/// </summary>
[Collection("SharedGameSingletons")]
public class TownInn125Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags FS = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player or NPC can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-towninn-{Guid.NewGuid():N}.db");
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
        var r = target.GetType().GetMethod(method, F)!.Invoke(target, args);
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

    /// <summary>A framed box row (WriteBoxHeader) is at most 80 wide (78 inside by default, 77 on Love Street);
    /// every other row fits in 79.</summary>
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

    /// <summary>The rows that hold the given text.</summary>
    private static List<string> RowsWith(string text, string piece) => Rows(text).Where(r => r.Contains(piece)).ToList();

    /// <summary>The Hungarian screen holds none of the English text of these keys (each literal piece of the
    /// English value of 5 letters or more, unless the Hungarian value has it too).</summary>
    private static void NoEnglishLeft(string huText, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            string en = Loc.GetIn("en", key), hu = Loc.GetIn("hu", key);
            foreach (var piece in Regex.Split(en, @"\{\d+\}").Select(p => p.Trim()).Where(p => p.Count(char.IsLetter) >= 5))
                if (!hu.Contains(piece))
                    huText.Should().NotContain(piece, $"{key} is shown in Hungarian");
        }
    }

    private static Character Hero(int level = 100) => new()
    {
        Name1 = "tester", Name2 = LongName, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = level,
        HP = 50000, MaxHP = 50000, BaseMaxHP = 50000, Mental = 100, Dexterity = 200, Charisma = 10,
        AI = CharacterAI.Human, Gold = 2_000_000_000, Sex = CharacterSex.Male, Orientation = SexualOrientation.Bisexual,
    };

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    private static readonly string[] CombatNameChecks =
    {
        // CombatEngine.cs isUndead (9233, 9382), boss bonus (9633), evil target (29307): substrings of a Monster name.
        "Skeleton", "Zombie", "Ghost", "Lich", "Wraith", "Vampire", "Undead", "Revenant", "Boss", "Chief", "Lord", "King",
        "Demon", "Devil",
    };

    [Fact]
    public void LongName_IsTheLongestPlayerName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
    }

    // ================= the Inn =================

    private static Companion Mate(CombatRole role, CompanionId id, string questName) => new()
    {
        Id = id, Name = LongName, Title = "the Long Named", CombatRole = role, Level = 100, RecruitLevel = 100,
        LoyaltyLevel = 99, TrustLevel = 100, RomanceAvailable = true, RomanceLevel = 10, PersonalQuestName = questName,
        IsRecruited = true, IsActive = true,
    };

    private static string LongestQuestName() =>
        CompanionSystem.Instance.GetAllCompanions().Select(c => c.PersonalQuestName).OrderByDescending(n => n.Length).First();

    private static readonly string[] SummaryKeys =
    {
        "inn.lvl_role_bar", "inn.quest_row_complete", "inn.quest_row_in_progress", "inn.quest_row_unlocked",
        "inn.quest_row_build_loyalty", "inn.romance_row", "inn.role_healer", "inn.role_damage", "inn.role_hybrid",
    };

    /// <summary>A companion's summary in each quest state, for every role.</summary>
    private static Task<string> Summaries(string lang) => InLanguage(lang, async () =>
    {
        var sb = new StringBuilder();
        foreach (var role in Enum.GetValues<CombatRole>())
            for (int state = 0; state < 4; state++)
            {
                var mate = Mate(role, CompanionId.Aldric, LongestQuestName());
                mate.PersonalQuestCompleted = state == 0;
                mate.PersonalQuestStarted = state == 1;
                mate.PersonalQuestAvailable = state == 2;
                if (state == 3) mate.LoyaltyLevel = 10;
                var inn = new InnLocation();
                var s = At(inn, Hero());
                await Run(inn, "DisplayCompanionSummary", mate, true);
                sb.Append(s.Text);
            }
        return sb.ToString();
    });

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task CompanionSummary_Fits_AndIsInThePlayersLanguage(string lang)
    {
        string shown = await Summaries(lang);
        Capture($"town-inn-summary-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} companion summary");
        shown.Should().Contain(L(lang, "inn.quest_row_build_loyalty", 10)).And.Contain(L(lang, "inn.romance_row", "**********", 10));
        if (lang == "hu") NoEnglishLeft(shown, SummaryKeys);
        else shown.Should().Contain("    Lvl 100 Tank | ").And.Contain($"    Quest: {LongestQuestName()} (COMPLETE)")
            .And.Contain("    Romance: ********** (10/10)");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task RecruitList_Fits_AndIsInThePlayersLanguage(string lang)
    {
        string shown = await InLanguage(lang, async () =>
        {
            var inn = new InnLocation();
            var s = At(inn, Hero(), "0");
            await Run(inn, "ApproachCompanions");
            return s.Text;
        });
        Capture($"town-inn-recruit-{lang}.txt", shown);
        var c = CompanionSystem.Instance.GetRecruitableCompanions(100).First();
        var row = $"    {L(lang, "inn.recruit_req_trust", c.RecruitLevel, c.TrustLevel)}";
        RowsWith(shown, row).Should().NotBeEmpty();
        EveryRowFits(RowsWith(shown, row.Trim()), $"{lang} recruit list");
        shown.Should().Contain($" ({InLang(lang, () => InnLocation.RoleName(c.CombatRole))})");
        if (lang == "hu") NoEnglishLeft(shown, new[] { "inn.recruit_req_trust" });
        else shown.Should().Contain($"    Level Req: {c.RecruitLevel} | Trust: {c.TrustLevel}%");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task CompanionTalk_StatRows_AreInThePlayersLanguage(string lang)
    {
        string shown = await InLanguage(lang, async () =>
        {
            var inn = new InnLocation();
            var s = At(inn, Hero(), "0");
            var mate = Mate(CombatRole.Hybrid, CompanionId.Vex, LongestQuestName());
            await Run(inn, "TalkToRecruitedCompanion", mate);
            return s.Text;
        });
        Capture($"town-inn-talk-{lang}.txt", shown);
        foreach (var key in new[] { "inn.stat_row_physical", "inn.stat_row_mental" })
        {
            string head = L(lang, key, 0, 0, 0, 0).Split(':')[0];
            var rows = RowsWith(shown, "  " + head + ":");
            rows.Should().NotBeEmpty($"{key} is shown");
            EveryRowFits(rows, $"{lang} companion stats");
        }
        var abilities = Rows(shown).SkipWhile(r => !r.StartsWith("  " + L(lang, "inn.abilities_count_label", 1, "x").Split('(')[0].TrimEnd()))
            .TakeWhile(r => r.Length > 0).ToList();
        abilities.Should().NotBeEmpty();
        EveryRowFits(abilities, $"{lang} companion abilities");
        if (lang == "hu") NoEnglishLeft(shown, new[] { "inn.abilities_count_label", "inn.role_hybrid" });
        else shown.Should().Contain("  STR: ").And.Contain("  INT: ").And.Contain("  Abilities (");
    }

    private static readonly string[] SkillKeys =
    {
        "inn.skills_role_line", "inn.skill_off", "inn.abilities_spells_enabled", "inn.abilities_enabled", "inn.spells_enabled",
        "inn.skills_options", "inn.enabled", "inn.disabled",
    };

    /// <summary>The combat skills screen for a companion of the role, toggling skill 1 off and on.</summary>
    private static Task<string> Skills(string lang, CombatRole role, CompanionId id) => InLanguage(lang, async () =>
    {
        var backend = typeof(SaveSystem).GetField("backend", F)!;
        var real = backend.GetValue(SaveSystem.Instance);
        try
        {
            backend.SetValue(SaveSystem.Instance, DispatchProxy.Create<ISaveBackend, MagicHaggle124Tests.NullBackend>());
            var inn = new InnLocation();
            var s = At(inn, Hero(), "1", "1", "0");
            await Run(inn, "ManageCompanionAbilities", Mate(role, id, "Q"));
            return s.Text;
        }
        finally { backend.SetValue(SaveSystem.Instance, real); }
    });

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task CombatSkills_Fit_ForEveryRole_AndAreInThePlayersLanguage(string lang)
    {
        var all = new StringBuilder();
        foreach (var (role, id) in new[] { (CombatRole.Tank, CompanionId.Aldric), (CombatRole.Healer, CompanionId.Mira),
                     (CombatRole.Damage, CompanionId.Vex), (CombatRole.Hybrid, CompanionId.Lyris), (CombatRole.Bard, CompanionId.Melodia) })
        {
            string shown = await Skills(lang, role, id);
            Capture($"town-inn-skills-{lang}-{role}.txt", shown);
            EveryRowFits(shown, $"{lang} combat skills ({role})");
            all.Append(shown);
        }
        string text = all.ToString();
        text.Should().Contain(L(lang, "dungeon.skills_header", LongName.ToUpper()).Substring(0, 10));
        text.Should().Contain($"[{L(lang, "inn.skill_off")}]").And.Contain($"[{L(lang, "inn.skill_on")}]");
        if (lang == "hu") NoEnglishLeft(text, SkillKeys);
        else
        {
            text.Should().Contain("  Role: Tank (as Warrior) | Level: 100").And.Contain("  [1-N] Toggle  [A] Enable all  [0] Return");
            Rows(text).Should().Contain(r => Regex.IsMatch(r, @"^  \[ 1\] \[(ON\] |OFF\])\s+\S.{20,} +\d+ ST  Lv\d+ +\S"));
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task RoomGuards_Fit_AndAreInThePlayersLanguage(string lang)
    {
        string shown = await InLanguage(lang, async () =>
        {
            var inn = new InnLocation();
            var s = At(inn, Hero(), "1", "6", "D", "N");
            await Run(inn, "RentRoom");
            return s.Text;
        });
        Capture($"town-inn-rent-{lang}.txt", shown);
        // The guard options and the hired guards (the gold summary row, inn.rent_gold_summary, is not changed here).
        var mine = Rows(shown).Where(r => Regex.IsMatch(r, @"^  \[\d\] |^    - ")).ToList();
        mine.Should().HaveCountGreaterThan(6);
        EveryRowFits(mine, $"{lang} room guards");
        shown.Should().Contain(L(lang, "inn.guard_name_hp", L(lang, "inn.guard_drake"), "").Split('(')[0]);
        if (lang == "hu") shown.Should().NotContain("(HP: ").And.Contain($"{L("hu", "inn.guard_hp_tag", 1).Split(' ')[0]} ");
        else Rows(shown).Should().Contain(r => Regex.IsMatch(r, @"^  \[1\] Rookie Guard {5}\s*[\d,]+g  \(HP: \d+\)$"));
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void SleeperRows_And_TemplateRows_Fit_WithTheLongestNames(string lang)
    {
        InLang(lang, () =>
        {
            // The sleeper rows, as AttackSleeper builds them, with a 30-character name, level 100 and the most guards.
            foreach (var text in new[]
            {
                $"{LongName} ({Loc.Get("inn.sleeper_level", 100)}) [{Loc.Get("inn.sleeping_npc")}]",
                $"{LongName} ({Loc.Get("inn.sleeper_level", 100)}) [{Loc.Get("inn.sleeper_guards", GameConfig.MaxSleepGuards)}] [{Loc.Get("inn.sleeping_player")}]",
            })
            {
                var rows = InnLocation.SleeperRows(99, text);
                rows.Should().OnlyContain(r => r.Length <= MaxWidth);
                string.Join(" ", rows.Select(r => r.Trim())).Should().Be($"99. {text}");
            }
            $"  {Loc.Get("inn.stat_row_physical", 99999, 99999, 99999, 99999)}".Length.Should().BeLessOrEqualTo(MaxWidth);
            $"  {Loc.Get("inn.stat_row_mental", 99999, 99999, 99999, 99999)}".Length.Should().BeLessOrEqualTo(MaxWidth);
            $"    {Loc.Get("inn.recruit_req_trust", 100, 100)}".Length.Should().BeLessOrEqualTo(MaxWidth);
            $"  {Loc.Get("inn.skills_role_line", InnLocation.RoleName(CombatRole.Hybrid), GameConfig.GetLocalizedClassName(CharacterClass.Paladin), 100)}"
                .Length.Should().BeLessOrEqualTo(MaxWidth);
            $"  {Loc.Get("inn.skills_options_sr")}".Length.Should().BeLessOrEqualTo(MaxWidth);
            string material = GameConfig.CraftingMaterials.Select(m => m.Name).OrderByDescending(n => n.Length).First();
            UIHelperRowsFit("  ", Loc.Get("inn.material_dissolves", material));
            return 0;
        });
    }

    private static void UIHelperRowsFit(string prefix, string text)
    {
        foreach (var row in UsurperRemake.UI.UIHelper.WrapAfterPrefix(prefix, text, MaxWidth))
            (prefix.Length + row.Length).Should().BeLessOrEqualTo(MaxWidth);
    }

    [Fact]
    public void SleeperList_UsesTheKeys()
    {
        string src = Src("Locations", "InnLocation.cs");
        src.Should().Contain("Loc.Get(\"inn.sleeper_level\", npc.Level)").And.Contain("[{Loc.Get(\"inn.sleeping_npc\")}]")
            .And.Contain("[{Loc.Get(\"inn.sleeping_player\")}]").And.Contain("\"inn.sleeper_guards\" : \"inn.sleeper_guard\"");
        src.Should().NotContain("SLEEPING NPC").And.NotContain("SLEEPING PLAYER");
    }

    // ---------- the sleeper's mail, in the victim's language ----------

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
    public async Task TheSleepMurderMail_IsInTheVictimsLanguage()
    {
        Player("victim_hu", "VictimHu", "hu");
        Player("victim_en", "VictimEn", "en");
        await InLanguage("fr", async () =>
        {
            foreach (var key in new[] { "victim_hu", "victim_en" })
                await Db.SendMessageToKeyLocalized("Murderer", key, "sleep_attack", lang => InnLocation.SleepMurderMail(lang, "Murderer", 1234, "Steel Sword"));
            await Db.SendMessageToKeyLocalized("Murderer", "victim_hu", "sleep_attack", lang => InnLocation.SleepMurderMail(lang, "Murderer", 50, null));
            return 0;
        });
        var mails = Mails();
        mails.Should().HaveCount(3);
        mails[0].Message.Should().Be(L("hu", "inn.mail_sleep_murder_item", "Murderer", "1,234", "Steel Sword"));
        mails[0].Message.Should().NotContain("broke into");
        mails[1].Message.Should().Be("Murderer broke into your Inn room and murdered you! They stole 1,234 gold and your Steel Sword.",
            "the English mail reads as before");
        mails[2].Message.Should().Be(L("hu", "inn.mail_sleep_murder", "Murderer", "50"));
        mails.Should().OnlyContain(m => m.From == "Murderer" && m.Type == "sleep_attack");
        Src("Locations", "InnLocation.cs").Should().Contain("SendMessageToKeyLocalized(murderer, target.Username, \"sleep_attack\",")
            .And.Contain("lang => SleepMurderMail(lang, murderer, stolenGold, stolenItemName)");
    }

    // ================= the Bank =================

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task BankSummary_And_History_AreInThePlayersLanguage(string lang)
    {
        long cap = (long)typeof(BankLocation).GetField("MaxGold", FS)!.GetValue(null)!;
        string shown = await InLanguage(lang, async () =>
        {
            var hero = Hero();
            hero.Gold = cap; hero.BankGold = cap; hero.Loan = 10;
            var bank = new BankLocation();
            var s = At(bank, hero, "", "");
            await Run(bank, "DisplayAccountSummary");
            await Run(bank, "ShowAccountHistory");
            return s.Text;
        });
        Capture($"town-inn-bank-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} bank summary");
        shown.Should().Contain(L(lang, "anchor_road.gold_amount", $"{cap * 2:N0}"));
        shown.Should().Contain(L(lang, "bank.account_status_line", L(lang, "bank.account_status_debt")));
        if (lang == "hu") shown.Should().NotContain(" gold").And.NotContain("Account Status");
        else shown.Should().Contain($"{cap:N0} gold").And.Contain("Account Status: IN DEBT");
    }

    [Fact]
    public void BankGuardNames_AreKeys_AndNoTranslationMatchesACombatNameCheck()
    {
        string src = Src("Locations", "BankLocation.cs");
        foreach (var key in new[] { "bank.guard_captain_name", "bank.guard_name", "bank.war_hound_name" })
        {
            src.Should().Contain($"Name = Loc.Get(\"{key}\")");
            foreach (var lang in AllLanguages)
                foreach (var word in CombatNameChecks)
                    L(lang, key).Should().NotContain(word, $"{lang} {key} must not turn the guard into a boss, undead or evil foe");
        }
        L("hu", "bank.guard_captain_name").Should().NotBe("Captain of the Guard");
    }

    // ================= Love Street =================

    private static readonly string[] WorkerIds =
        { "elly", "lusha", "irma", "elynthia", "melissa", "seraphina", "sonya", "arabella", "loretta" };
    private static readonly string[] GigoloIds =
        { "signori", "tod", "mbuto", "merson", "brian", "rasputin", "manhio", "jake", "banco" };

    private static IEnumerable<string> WorkerKeys() =>
        WorkerIds.SelectMany(i => new[] { $"love_street.courtesan.{i}.desc", $"love_street.courtesan.{i}.intro" })
            .Concat(GigoloIds.SelectMany(i => new[] { $"love_street.gigolo.{i}.desc", $"love_street.gigolo.{i}.intro" }));

    [Theory]
    [InlineData("en", "ShowCourtesanMenu")] [InlineData("hu", "ShowCourtesanMenu")]
    [InlineData("en", "ShowGigoloMenu")] [InlineData("hu", "ShowGigoloMenu")]
    public async Task WorkerMenus_Fit_AndAreInThePlayersLanguage(string lang, string method)
    {
        string shown = await InLanguage(lang, async () =>
        {
            var street = new LoveStreetLocation();
            var s = At(street, Hero(), "0");
            await Run(street, method);
            return s.Text;
        });
        Capture($"town-inn-{method}-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} {method}");
        string kind = method == "ShowCourtesanMenu" ? "courtesan" : "gigolo";
        var ids = kind == "courtesan" ? WorkerIds : GigoloIds;
        foreach (var id in ids)
            string.Join(" ", Rows(shown).Select(r => r.Trim())).Should().Contain(L(lang, $"love_street.{kind}.{id}.desc"));
        shown.Should().Contain($" ({L(lang, "race.human")}) - ");
        if (lang == "hu") NoEnglishLeft(shown, WorkerKeys());
        else if (kind == "courtesan") shown.Should().Contain(" 1) Elly (Mutant) - 1,000 gold").And.Contain("    A strange mix between races - exotic and dangerous.");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task WorkerScenes_Fit_AndAreInThePlayersLanguage(string lang)
    {
        var all = new StringBuilder();
        for (int i = 0; i < 9; i++)
            foreach (var method in new[] { "ShowCourtesanMenu", "ShowGigoloMenu" })
            {
                int pick = i;
                string shown = await InLanguage(lang, async () =>
                {
                    var street = new LoveStreetLocation();
                    var s = At(street, Hero(), $"{pick + 1}", "N");
                    await Run(street, method);
                    return s.Text;
                });
                EveryRowFits(shown, $"{lang} {method} scene {pick + 1}");
                all.Append(shown);
            }
        string text = all.ToString();
        Capture($"town-inn-scenes-{lang}.txt", text);
        text.Should().Contain(L(lang, "love_street.worker_title", "Loretta", L(lang, "race.elf")));
        string joined = string.Join(" ", Rows(text).Select(r => r.Trim()));
        joined.Should().Contain(L(lang, "love_street.gigolo.banco.intro"));
        if (lang == "hu") NoEnglishLeft(text, WorkerKeys().Append("love_street.worker_title"));
        else text.Should().Contain("Elly the Mutant").And.Contain("Banco the Human");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("fr")]
    public async Task APaidVisit_StoresTheEnglishPartnerId_AndTheSaveKeepsIt(string lang)
    {
        var tracker = RomanceTracker.Instance;
        var before = tracker.EncounterHistory.ToList();
        try
        {
            await InLanguage(lang, async () =>
            {
                var hero = Hero();
                var street = new LoveStreetLocation();
                At(street, hero, "9", "Y", "", "", "");
                await Run(street, "ShowCourtesanMenu");
                return 0;
            });
            var last = tracker.EncounterHistory.Last();
            last.PartnerIds.Should().Equal(new[] { "courtesan_Loretta" }, $"the partner id is the personal name in {lang}");
            var data = tracker.ToSaveData();
            string json = JsonSerializer.Serialize(data);
            json.Should().Contain("courtesan_Loretta");
            var back = JsonSerializer.Deserialize<RomanceTrackerData>(json)!;
            tracker.LoadFromSaveData(back);
            tracker.EncounterHistory.Last().PartnerIds.Should().Equal(new[] { "courtesan_Loretta" }, "the save keeps the stored id");
        }
        finally { tracker.EncounterHistory = before; }
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task GiftShop_Fits_AndIsInThePlayersLanguage(string lang)
    {
        string shown = await InLanguage(lang, async () =>
        {
            var street = new LoveStreetLocation();
            var s = At(street, Hero(), "0");
            await Run(street, "VisitGiftShop");
            return s.Text;
        });
        Capture($"town-inn-gifts-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} gift shop");
        foreach (var g in LoveStreetLocation.Gifts)
            shown.Should().Contain(L(lang, g.key) + ", ");
        if (lang == "hu") NoEnglishLeft(shown, LoveStreetLocation.Gifts.Select(g => g.key));
        else shown.Should().Contain("Red Roses, 100g").And.Contain("Star of Eternity, 100,000g");
    }

    private static NPC Single(string name, CharacterRace race, CharacterClass cls, int i)
    {
        var npc = new NPC
        {
            Name1 = name, Name2 = name, ID = $"inn-125-{i}", Level = 100, Class = cls, Race = race, HP = 500, MaxHP = 500,
            AI = CharacterAI.Computer, Sex = i % 2 == 0 ? CharacterSex.Female : CharacterSex.Male,
        };
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        profile.Orientation = SexualOrientation.Bisexual;
        profile.Flirtatiousness = 0.99f;
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        return npc;
    }

    private static T WithNpcs<T>(IEnumerable<NPC> npcs, Func<T> body)
    {
        var list = NPCSpawnSystem.Instance.ActiveNPCs;
        var added = npcs.ToList();
        list.AddRange(added);
        try { return body(); }
        finally { foreach (var n in added) list.Remove(n); }
    }

    private static (CharacterRace race, CharacterClass cls) Longest(string lang) => InLang(lang, () => (
        Enum.GetValues<CharacterRace>().OrderByDescending(r => GameConfig.GetLocalizedRaceName(r).Length).First(),
        Enum.GetValues<CharacterClass>().OrderByDescending(c => GameConfig.GetLocalizedClassName(c).Length).First()));

    [Theory]
    [InlineData("en", "Mingle")] [InlineData("hu", "Mingle")]
    [InlineData("en", "GossipWhosAvailable")] [InlineData("hu", "GossipWhosAvailable")]
    public async Task NpcRows_Fit_WithTheLongestNames_AndShowTheRaceInThePlayersLanguage(string lang, string method)
    {
        var (race, cls) = Longest(lang);
        var npcs = Enumerable.Range(0, 6).Select(i => Single(LongName.Substring(0, 28) + $"{i,2}", race, cls, i)).ToList();
        string shown = await WithNpcs(npcs, () => InLanguage(lang, async () =>
        {
            var street = new LoveStreetLocation();
            var s = At(street, Hero(), "0");
            await Run(street, method);
            return s.Text;
        }));
        Capture($"town-inn-{method}-{lang}.txt", shown);
        EveryRowFits(shown, $"{lang} {method}");
        string tag = InLang(lang, () => Loc.Get("love_street.npc_level_race_class", 100, GameConfig.GetLocalizedRaceName(race), GameConfig.GetLocalizedClassName(cls)));
        shown.Should().Contain($"({tag})");
        if (lang == "hu") shown.Should().NotContain($" {race} ", "the race is shown in Hungarian");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task BbsMenu_Fits_AndIsInThePlayersLanguage(string lang)
    {
        string shown = await InLanguage(lang, async () =>
        {
            var street = new LoveStreetLocation();
            var s = At(street, Hero());
            await Run(street, "DisplayLocationBBS");
            return s.Text;
        });
        Capture($"town-inn-bbs-{lang}.txt", shown);
        var rows = Rows(shown).Where(r => r.StartsWith(" [1]") || r.StartsWith(" [G]")).ToList();
        rows.Should().HaveCount(2);
        EveryRowFits(rows, $"{lang} love street BBS menu");
        if (lang == "hu") NoEnglishLeft(string.Join("\n", rows), new[] { "love_street.bbs_beauty_nest", "love_street.bbs_hall_dreams", "love_street.bbs_gifts", "love_street.bbs_potions" });
        else rows.Should().Equal(" [1]BeautyNest [2]HallDreams [M]Mingle [D]Date ", " [G]Gifts [V]Gossip [L]Potions [R]Return ");
    }

    [Fact]
    public void DateList_Tags_AreKeys()
    {
        string src = Src("Locations", "LoveStreetLocation.cs");
        src.Should().Contain("potentialDates.Add((spouse.NPCId, name, Loc.Get(\"love_street.tag_spouse\")));")
            .And.Contain("potentialDates.Add((lover.NPCId, name, Loc.Get(\"love_street.tag_lover\")));")
            .And.Contain("potentialDates.Add((npc.ID, npc.Name, Loc.Get(\"love_street.tag_friend\")));");
    }

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void LoveStreetTemplates_Fit_WithTheLongestNames(string lang)
    {
        InLang(lang, () =>
        {
            string longestRace = Enum.GetValues<CharacterRace>().Select(GameConfig.GetLocalizedRaceName).OrderByDescending(r => r.Length).First();
            // The worker box is 77 inside; its title must leave a border.
            Loc.Get("love_street.worker_title", "Elynthia", longestRace).Length.Should().BeLessOrEqualTo(75);
            $"9. {Loc.Get("love_street.sr_worker_row", "Elynthia", longestRace, "200,000", "Very High")}".Length.Should().BeLessOrEqualTo(MaxWidth);
            foreach (var g in LoveStreetLocation.Gifts)
                $"9. {Loc.Get(g.key)}, {g.cost:N0}g ({Loc.Get("love_street.luxury")}) ({Loc.Get("love_street.cant_afford")})".Length
                    .Should().BeLessOrEqualTo(MaxWidth);
            return 0;
        });
    }

    [Fact]
    public void WrapParts_StartsANewRow_BeforeAPartThatWouldPassTheWidth()
    {
        var rows = LoveStreetLocation.WrapParts(new List<(string, string)>
            { (" [1] ", "y"), (LongName, "w"), (" (Lv100 Mutant Mystic Shaman)", "g"), (" [Friend]", "c"), (" - seems flirtatious", "d") }, 5);
        rows.Select(r => string.Concat(r.Select(p => p.text))).Should().Equal(
            " [1] Aranyszivu Hosszunevu Kalandor (Lv100 Mutant Mystic Shaman) [Friend]",
            "     - seems flirtatious");
        LoveStreetLocation.WrapParts(new List<(string, string)> { (" * ", "y"), ("Short", "w") }, 3)
            .Should().ContainSingle();
    }

    // ================= the street encounters =================

    private sealed class ZeroRandom : Random { public override int Next(int maxValue) => 0; public override int Next(int min, int max) => min; }

    private static StreetEncounterSystem Street()
    {
        var street = (StreetEncounterSystem)Activator.CreateInstance(typeof(StreetEncounterSystem), nonPublic: true)!;
        typeof(StreetEncounterSystem).GetField("_random", F)!.SetValue(street, new ZeroRandom());
        return street;
    }

    /// <summary>A hero at Mental 0: every fight ends before it starts, so the encounter's own text is all there is.</summary>
    private static Character Spent()
    {
        var hero = Hero(20);
        hero.Mental = 0;
        return hero;
    }

    /// <summary>The encounter's screen, and its result message with the name of the foe it fought (when it fought).</summary>
    private static Task<(string text, EncounterResult result)> Encounter(string lang, string method, string input, params object[] lead) =>
        InLanguage(lang, async () =>
        {
            var s = NewScreen(input, "", "", "");
            var result = new EncounterResult();
            var street = Street();
            var args = lead.Append(result).Append(s.Term).ToArray();
            try { await Run(street, method, args); }
            catch (LocationExitException) { }   // a lost fight with the guards ends in prison
            result.Message += "\n" + street.LastFightMonster?.Name;
            return (s.Text, result);
        });

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task StreetEncounters_NameTheirFoes_InThePlayersLanguage(string lang)
    {
        var cases = new (string method, string input, object[] lead, string nameKey, object[] nameArgs)[]
        {
            ("ProcessPickpocketEncounter", "G", new object[] { Spent() }, "street_encounter.foe.pickpocket", Array.Empty<object>()),
            ("ProcessBrawlEncounter", "F", new object[] { Spent() }, "street_encounter.foe.drunk_sailor", Array.Empty<object>()),
            ("ProcessGangEncounter", "F", new object[] { Spent() }, "street_encounter.foe.gang_leader", new object[] { "Shadow Blades" }),
            ("ProcessAmbushEncounter", "", new object[] { Spent(), GameLocation.MainStreet }, "street_encounter.foe.hired_assassin", Array.Empty<object>()),
        };
        var all = new StringBuilder();
        foreach (var c in cases)
        {
            var (text, result) = await Encounter(lang, c.method, c.input, c.lead);
            string name = L(lang, c.nameKey, c.nameArgs);
            (text + "\n" + result.Message).Should().Contain(name, $"{c.method} names its foe in {lang}");
            EveryRowFits(text, $"{lang} {c.method}");
            all.Append(text).Append('\n').Append(result.Message).Append('\n');
        }

        var guardHero = Spent();
        guardHero.Darkness = 500;
        var (guardText, guardResult) = await Encounter(lang, "ProcessGuardPatrolEncounter", "F", guardHero);
        (guardText + guardResult.Message).Should().Contain(L(lang, "street_encounter.foe.guard_captain"));
        all.Append(guardText).Append(guardResult.Message);

        var (hostile, _) = await Encounter(lang, "ProcessHostileNPCEncounter", "F", Spent(), GameLocation.MainStreet);
        hostile.Should().Contain($"\"{L(lang, "street.hostile_phrase_1")}\"");
        hostile.Should().Contain(L(lang, "street_encounter.foe.street_thug"));
        EveryRowFits(hostile, $"{lang} hostile encounter");
        var (challenge, _) = await Encounter(lang, "ProcessChallengeEncounter", "D", Spent(), GameLocation.MainStreet);
        string.Join(" ", Rows(challenge).Select(r => r.Trim())).Should().Contain($"\"{L(lang, "street.challenge_phrase_1", LongName)}\"");
        EveryRowFits(challenge, $"{lang} challenge encounter (the challenger names a 30-character player)");
        all.Append(hostile).Append(challenge);

        string shown = all.ToString();
        Capture($"town-inn-street-{lang}.txt", shown);
        if (lang == "hu")
            NoEnglishLeft(shown, new[]
            {
                "street_encounter.foe.pickpocket", "street_encounter.foe.drunk_sailor", "street_encounter.foe.gang_leader",
                "street_encounter.foe.hired_assassin", "street_encounter.foe.guard_captain", "street_encounter.foe.street_thug",
                "street.hostile_phrase_1", "street.challenge_phrase_1",
            });
        else shown.Should().Contain("Pickpocket").And.Contain("Drunk Sailor").And.Contain("Shadow Blades Leader")
            .And.Contain("Hired Assassin").And.Contain("Town Guard Captain").And.Contain("\"Your gold or your life!\"");
    }

    private static T Call<T>(StreetEncounterSystem street, string method, params object?[] args) =>
        (T)typeof(StreetEncounterSystem).GetMethod(method, F, null, args.Select(a => a!.GetType()).ToArray(), null)!.Invoke(street, args)!;

    [Theory]
    [InlineData("en")] [InlineData("es")] [InlineData("fr")] [InlineData("hu")] [InlineData("it")]
    public void FoeNames_And_Lines_AreInTheLanguage_AndMatchNoCombatNameCheck(string lang)
    {
        InLang(lang, () =>
        {
            var street = Street();
            var foe = Call<NPC>(street, "CreateRandomHostileNPC", 10);
            foe.Name2.Should().Be(Loc.Get(StreetEncounterSystem.HostileFoeNameKeys[0]));
            for (int i = 0; i < 4; i++)
                Call<string>(street, "GetMuggerName", i).Should().Be(Loc.Get(StreetEncounterSystem.MuggerNameKeys[i]));
            Call<string>(street, "GetRandomBrawlerName").Should().Be(Loc.Get(StreetEncounterSystem.BrawlerNameKeys[0]));
            var keys = StreetEncounterSystem.HostileFoeNameKeys.Concat(StreetEncounterSystem.BrawlerNameKeys).Concat(StreetEncounterSystem.MuggerNameKeys)
                .Concat(new[] { "street_encounter.foe.pickpocket", "street_encounter.foe.angry_drunk", "street_encounter.foe.guard_captain",
                    "street_encounter.foe.hired_assassin", "street_encounter.foe.gang_leader", "street_encounter.foe.gang_enforcer" });
            foreach (var key in keys)
            {
                string name = Loc.Get(key, "Shadow Blades");
                foreach (var word in CombatNameChecks)
                    name.Should().NotContain(word, $"{lang} {key} must not turn the foe into a boss, undead or evil one");
                (name.Length).Should().BeLessOrEqualTo(GameConfig.MaxNameLength + 12, $"{lang} {key} stays a name");
            }
            for (int n = 1; n <= StreetEncounterSystem.HostilePhraseCount; n++)
                Loc.Get($"street.hostile_phrase_{n}").Should().NotStartWith("street.");
            for (int n = 1; n <= StreetEncounterSystem.ChallengePhraseCount; n++)
                Loc.Get($"street.challenge_phrase_{n}", LongName).Should().NotStartWith("street.");
            return 0;
        });
    }

    [Fact]
    public void BountyHunterNames_AreThePersonalNames_InEveryLanguage()
    {
        foreach (var lang in new[] { "en", "hu" })
            InLang(lang, () =>
            {
                var hunter = Call<NPC>(Street(), "CreateBountyHunter", 20, 1);
                hunter.Name2.Should().Be("Hex the Hound", "a personal name is not translated");
                return 0;
            });
    }

    // ================= Electron menus and the keys =================

    [Theory]
    [InlineData("InnLocation.cs")] [InlineData("LoveStreetLocation.cs")]
    public void ElectronMenus_LabelsAreKeys(string file)
    {
        string src = Src("Locations", file);
        string emit = src.Substring(src.IndexOf("var menu = new List<ElectronBridge.MenuItemData>", StringComparison.Ordinal));
        emit = emit.Substring(0, emit.IndexOf("ElectronBridge.EmitMenu(menu);", StringComparison.Ordinal));
        var labels = Regex.Matches(emit, @"Label = ([^,]+),").Select(m => m.Groups[1].Value).ToList();
        labels.Should().HaveCountGreaterThan(7);
        labels.Should().OnlyContain(l => l.StartsWith("Loc.Get("), "every Electron menu label is keyed");
    }

    /// <summary>Every key the four files name, with the ones built at run time.</summary>
    private static List<string> UsedKeys()
    {
        var files = new[] { Src("Locations", "InnLocation.cs"), Src("Locations", "BankLocation.cs"), Src("Locations", "LoveStreetLocation.cs"),
            Src("Systems", "StreetEncounterSystem.cs") };
        const string ns = "(?:inn|bank|love_street|street_encounter|street|love_corner|anchor_road|base|ui|dungeon|ability|magic_shop|race)";
        var used = files.SelectMany(f => Regex.Matches(f, "Loc\\.Get(?:In)?\\((?:\\w+, )?\"(" + ns + "\\.[a-z0-9_.]+)\"")
                .Concat(Regex.Matches(f, "\"(street_encounter\\.foe\\.[a-z_]+|love_street\\.gift_[a-z_]+)\""))
                .Select(m => m.Groups[1].Value))
            .Concat(WorkerKeys())
            .Concat(Enum.GetValues<CombatRole>().Select(r => $"inn.role_{r.ToString().ToLowerInvariant()}"))
            .Concat(Enumerable.Range(1, StreetEncounterSystem.HostilePhraseCount).Select(n => $"street.hostile_phrase_{n}"))
            .Concat(Enumerable.Range(1, StreetEncounterSystem.ChallengePhraseCount).Select(n => $"street.challenge_phrase_{n}"))
            .Distinct().ToList();
        return used;
    }

    private static readonly string[] NewKeys =
    {
        "bank.account_status_line", "bank.news_big_deposit", "bank.news_wired", "bank.news_hired_guard", "bank.news_robbery",
        "bank.news_rob_defeated", "bank.news_rob_fled", "bank.news_seized", "bank.news_collectors", "bank.news_confiscated",
        "bank.news_fired", "bank.guard_captain_name", "bank.guard_name", "bank.war_hound_name",
        "street_encounter.gang.news_header", "street_encounter.news.murdered", "street_encounter.news.throne_seized",
        "street_encounter.news.turf_lost", "street_encounter.news.grudge_won", "street_encounter.foe.footpad",
        "love_street.gift_red_roses", "love_street.gift_star_of_eternity", "love_street.electron_mingle", "love_street.bbs_gifts",
        "inn.recruit_req_trust", "inn.quest_row_unlocked", "inn.skills_options", "inn.mail_sleep_murder_item", "inn.electron_sleep",
        "inn.role_healer", "inn.skill_off",
    };

    [Fact]
    public void Keys_AreInEveryLanguage_WithTheSamePlaceholders_AndNoDashes()
    {
        var langs = AllLanguages.ToDictionary(l => l, l => JsonDocument.Parse(File.ReadAllText(
            Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Localization", l + ".json"))).RootElement);
        var used = UsedKeys();
        used.Count.Should().BeGreaterThan(400);
        // The keys this change added (pre-existing keys keep their own placeholder choices, as a plural suffix some drop).
        var added = new HashSet<string>(NewKeys.Concat(WorkerKeys()).Concat(used.Where(k => k.StartsWith("street_encounter.foe.")
            || k.StartsWith("street_encounter.news.") || k.StartsWith("bank.news_") || k.StartsWith("inn.skill") || k.StartsWith("inn.quest_row_")
            || k.StartsWith("inn.role_") || k.StartsWith("inn.sleeper_") || k.StartsWith("love_street.gift_") || k.StartsWith("love_street.bbs_")
            || k.StartsWith("love_street.npc_level") || k.StartsWith("inn.electron_") || k.StartsWith("love_street.electron_"))));
        foreach (var key in used.Concat(NewKeys).Distinct())
        {
            langs["en"].TryGetProperty(key, out var e).Should().BeTrue($"{key} is in en.json");
            var holes = Regex.Matches(e.GetString()!, @"\{\d+\}").Select(m => m.Value).Distinct().OrderBy(x => x).ToList();
            foreach (var lang in AllLanguages.Skip(1))
            {
                langs[lang].TryGetProperty(key, out var v).Should().BeTrue($"{key} is in {lang}.json");
                v.GetString().Should().NotBeNullOrWhiteSpace();
                if (added.Contains(key))
                    Regex.Matches(v.GetString()!, @"\{\d+\}").Select(m => m.Value).Distinct().OrderBy(x => x).Should().Equal(holes, $"{lang} {key} keeps the placeholders");
            }
        }
        foreach (var key in NewKeys.Concat(WorkerKeys()))
        {
            foreach (var lang in AllLanguages)
                langs[lang].GetProperty(key).GetString().Should().NotContain("\u2014").And.NotContain("\u2013", $"{lang} {key}");
            langs["hu"].GetProperty(key).GetString().Should().NotBe(langs["en"].GetProperty(key).GetString(), $"{key} is translated");
        }
    }
}

/// <summary>v1.2.5: the court role a street negotiation writes stays English whatever the player's language.</summary>
public partial class OwnerProcessConflictTests
{
    [Fact]
    public async Task TheStreetAdvisorSeat_IsStoredInEnglish_InAHungarianSession()
    {
        var prev = GameConfig.Language;
        try
        {
            GameConfig.Language = "hu";
            await WithKing("Kim", 1000, async _ => await AsTheWorldSim(async () =>
            {
                await _db.SaveWorldState("royal_court", CourtWith("Kim", 1000, ("Gerald", 60)));
                await NewOsm(_db).LoadRoyalCourtFromWorldState();
                var kim = PlayerKing("Kim");
                kim.Charisma = 50;
                await StreetThroneChallenge(kim, Npc("npc_st_vex_hu", "Vex"), "N");
                var stored = await StoredCourt();
                stored.CourtMembers.Should().Contain(m => m.Name == "Vex" && m.Role == "Advisor",
                    "CourtMemberSaveData.Role is saved and compared (CastleLocation), so it stays English");
            }));
        }
        finally { GameConfig.Language = prev; }
    }
}
