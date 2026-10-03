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
/// v1.2.5: the rest of the castle system in the player's language: the prison (menu, bail, petitions to the
/// crown, maximum security, activities, Electron menu), the prison walk (prompt, prisoner list, guards,
/// Electron menu), the home (herb, parenting, upgrade, pet roster and partner rows, Electron menu), the news
/// the petitions, the throne and city challenges, the city control and the court upkeep write (in the
/// writer's language), and the prison petitions mailed to a human monarch (in the monarch's language).
/// What is stored or matched stays English: the guard monster names, the fallback king name "Unknown Ruler",
/// the court role "Advisor", the prison crimes and every Electron key, category and icon. Every changed row
/// fits 79 columns in English and Hungarian with a 30-character name.
/// </summary>
[Collection("SharedGameSingletons")]
public class CastleRest125Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags S = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player, NPC or monarch can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };

    private readonly string _path = Path.Combine(Path.GetTempPath(), $"usurper-castle-rest-{Guid.NewGuid():N}.db");
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

    private static (PrisonLocation Prison, Screen Screen) Prison(params string[] lines)
    {
        var s = NewScreen(lines);
        var prison = new PrisonLocation(null!, s.Term);
        // BaseLocation.IsScreenReader reads the base player
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(prison, new Character { Name2 = LongName, ScreenReaderMode = GameConfig.ScreenReaderMode });
        return (prison, s);
    }

    private static (PrisonWalkLocation Walk, Screen Screen) Walk(params string[] lines)
    {
        var s = NewScreen(lines);
        return (new PrisonWalkLocation(GameEngine.Instance, s.Term), s);
    }

    private static (HomeLocation Home, Screen Screen) Home(Character hero, params string[] lines)
    {
        var s = NewScreen(lines);
        var home = new HomeLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(home, s.Term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(home, hero);
        return (home, s);
    }

    private static async Task Run(object target, string method, params object?[] args)
    {
        var m = target.GetType().GetMethod(method, F)!;
        var r = m.Invoke(target, args);
        if (r is Task t) await t;
    }

    private static async Task<T> InLanguage<T>(string lang, Func<Task<T>> body, bool screenReader = false)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode, cm = GameConfig.CompactMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = screenReader;
            GameConfig.CompactMode = false;
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
    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in Rows(text))
        {
            if (row.Length > 0 && "╔║╚".IndexOf(row[0]) >= 0)
                row.Length.Should().BeLessOrEqualTo(MaxWidth + 1, $"the {screen} box keeps its width: \"{row}\"");
            else
                row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
        }
    }

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

    private static Character Hero(long gold = 2_000_000) => new()
    {
        Name1 = "tester", Name2 = LongName, Class = CharacterClass.Warrior, Race = CharacterRace.Human, Level = 40,
        HP = 500, MaxHP = 500, BaseMaxHP = 500, Mental = 100, Dexterity = 50, Charisma = 10,
        AI = CharacterAI.Human, Gold = gold, Sex = CharacterSex.Male, DaysInPrison = 5, PrisonEscapes = 1,
    };

    private static King LongKing(CharacterAI ai = CharacterAI.Computer, CharacterSex sex = CharacterSex.Female)
    {
        var king = King.CreateNewKing(LongName, ai, sex);
        king.Treasury = 1_000_000;
        return king;
    }

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { HardcodedTextScannerTests.RepoRoot(), "Scripts" }.Concat(path).ToArray()));

    private static string ClientSrc(string file) =>
        File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "electron-client", "src", file));

    private static IEnumerable<string> KeysIn(string src, string prefix) =>
        Regex.Matches(src, "Loc\\.Get\\(\"(" + Regex.Escape(prefix) + "[a-z0-9_.]+)\"").Select(m => m.Groups[1].Value).Distinct();

    [Fact]
    public void LongName_IsTheLongestPlayerName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
    }

    // ================= the prison =================

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task PrisonMenu_Fits_AndIsInThePlayersLanguage(string lang)
    {
        string full = await InLanguage(lang, async () =>
        {
            var (prison, s) = Prison();
            await Run(prison, "ShowPrisonMenuFull");
            return s.Text;
        });
        string sr = await InLanguage(lang, async () =>
        {
            var (prison, s) = Prison();
            await Run(prison, "ShowPrisonMenuFull");
            return s.Text;
        }, screenReader: true);
        Capture($"prison-menu-{lang}.txt", full);
        Capture($"prison-menu-sr-{lang}.txt", sr);
        EveryRowFits(full, $"{lang} prison menu");
        EveryRowFits(sr, $"{lang} screen reader prison menu");
        full.Should().Contain(L(lang, "prison.menu_row4"));
        sr.Should().Contain(L(lang, "prison.sr_menu_bail")).And.Contain(L(lang, "prison.sr_menu_petition"));
        string title = L(lang, "prison.title");
        full.Should().Contain($"III {title} III");
        var bars = Rows(full).Where(r => r.Length > 0 && r.All(c => c == 'I')).ToList();
        bars.Should().HaveCount(2);
        bars.Should().OnlyContain(b => b.Length >= title.Length + 8, "the bar spans the framed title");
        if (lang == "hu")
        {
            NoEnglishLeft(full, new[] { "prison.menu_row4" });
            NoEnglishLeft(sr, new[] { "prison.sr_menu_bail", "prison.sr_menu_petition" });
        }
        else
        {
            full.Should().Contain("(B)ail Payment              (P)etition for Release");
            bars.Should().OnlyContain(b => b.Length == 24, "the English bar is the 24 it was");
            sr.Should().Contain("B. Pay Bail (if bail is set)").And.Contain("P. Petition the King for release");
        }
    }

    private static readonly string[] BailKeys =
        { "prison.bail_amount", "prison.bail_you_have", "prison.bail_confirm", "prison.bail_keep_gold" };

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task Bail_Fits_AndIsInThePlayersLanguage(string lang)
    {
        CastleLocation.SetKing(LongKing());
        var hero = Hero();
        CastleLocation.GetCurrentKing().Prisoners[LongName] = new PrisonRecord { CharacterName = LongName, Crime = "Theft", Sentence = 5, BailAmount = 1_234_567 };
        string text = await InLanguage(lang, async () =>
        {
            var (prison, s) = Prison("N");
            await Run(prison, "HandlePayBail", hero);
            return s.Text;
        });
        Capture($"prison-bail-{lang}.txt", text);
        EveryRowFits(text, $"{lang} bail");
        text.Should().Contain(L(lang, "prison.bail_amount", "1,234,567")).And.Contain(L(lang, "prison.bail_you_have", "2,000,000"))
            .And.Contain(L(lang, "prison.bail_confirm", "1,234,567").TrimEnd()).And.Contain(L(lang, "prison.bail_keep_gold"));
        hero.Gold.Should().Be(2_000_000, "the player declined");
        if (lang == "hu") NoEnglishLeft(text, BailKeys);
        else text.Should().Contain("  Bail is set at 1,234,567 gold.").And.Contain("  Pay 1,234,567 gold for your freedom? (Y/N):");

        // every guard on the way in, in the player's language
        foreach (var (setup, key) in new (Action, string)[]
        {
            (() => { hero.IsMurderConvict = true; }, "prison.bail_murder"),
            (() => { hero.IsMurderConvict = false; CastleLocation.SetKing(null!); }, "prison.bail_no_king"),
            (() => { CastleLocation.SetKing(LongKing()); }, "prison.bail_none"),
            (() => { CastleLocation.GetCurrentKing().Prisoners[LongName] = new PrisonRecord { CharacterName = LongName, Crime = "Theft" }; }, "prison.bail_not_set"),
        })
        {
            setup();
            string guard = await InLanguage(lang, async () =>
            {
                var (prison, s) = Prison();
                await Run(prison, "HandlePayBail", hero);
                return s.Text;
            });
            guard.Should().Contain("  " + L(lang, key), key);
            EveryRowFits(guard, $"{lang} {key}");
            if (lang == "hu") NoEnglishLeft(guard, new[] { key });
        }
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("fr")]
    public async Task ACourtChangeThatFails_IsWrappedTo79Columns(string lang)
    {
        CastleLocation.SetKing(LongKing());
        var hero = Hero();
        // the record under the player's name holds another name, so the court change finds nothing to clear
        CastleLocation.GetCurrentKing().Prisoners[LongName] = new PrisonRecord { CharacterName = "Someone Else", Crime = "Theft", Sentence = 5, BailAmount = 500 };
        string text = await InLanguage(lang, async () =>
        {
            var (prison, s) = Prison("Y");
            await Run(prison, "HandlePayBail", hero);
            return s.Text;
        });
        Capture($"prison-bail-failed-{lang}.txt", text);
        hero.Gold.Should().Be(2_000_000, "nothing was paid");
        string message = L(lang, "castle.court_change_failed");
        message.Length.Should().BeGreaterThan(MaxWidth - 2, "the message is longer than one row");
        var rows = Rows(text).SkipWhile(r => !message.StartsWith(r.Trim().Split(' ')[0]) || r.Trim().Length == 0).ToList();
        string joined = string.Join(" ", Rows(text).Where(r => r.StartsWith("  ")).Select(r => r.Trim()));
        joined.Should().Contain(message, "the whole message is shown");
        EveryRowFits(text, $"{lang} failed bail");
    }

    private static readonly string[] PetitionKeys =
    {
        "prison.petition_header", "prison.petition_intro", "prison.petition_opt_bail", "prison.petition_opt_clemency",
        "prison.petition_considers", "prison.bail_set_now", "prison.bail_use_b",
    };

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task PetitionToTheCrown_Fits_AndIsInThePlayersLanguage(string lang)
    {
        CastleLocation.SetKing(LongKing());
        var hero = Hero();
        CastleLocation.GetCurrentKing().Prisoners[LongName] = new PrisonRecord { CharacterName = LongName, Crime = "Theft", Sentence = 5 };
        string text = await InLanguage(lang, async () =>
        {
            var (prison, s) = Prison("1");
            await Run(prison, "HandlePetitionKing", hero);
            return s.Text;
        });
        Capture($"prison-petition-{lang}.txt", text);
        EveryRowFits(text, $"{lang} petition");
        long bail = 1000 + hero.Level * 500;
        CastleLocation.GetCurrentKing().Prisoners[LongName].BailAmount.Should().Be(bail);
        text.Should().Contain($"  ═══ {L(lang, "prison.petition_header")} ═══").And.Contain("  0. " + L(lang, "ui.cancel"))
            .And.Contain(L(lang, "prison.bail_set_now", $"{bail:N0}"));
        if (lang == "hu") NoEnglishLeft(text, PetitionKeys.Append("ui.cancel"));
        else text.Should().Contain("  1. Request bail be set (if none is set)").And.Contain("  Bail has been set at 21,000 gold.");

        // a human monarch: the plea goes by mail, the prisoner waits
        CastleLocation.SetKing(LongKing(CharacterAI.Human));
        string plea = await InLanguage(lang, async () =>
        {
            var (prison, s) = Prison("2");
            await Run(prison, "HandlePetitionKing", hero);
            return s.Text;
        });
        EveryRowFits(plea, $"{lang} plea");
        plea.Should().Contain(L(lang, "prison.plea_sent")).And.Contain(L(lang, "prison.petition_await"));
        if (lang == "hu") NoEnglishLeft(plea, new[] { "prison.plea_sent", "prison.petition_await" });

        // no monarch at all
        CastleLocation.SetKing(null!);
        string none = await InLanguage(lang, async () =>
        {
            var (prison, s) = Prison();
            await Run(prison, "HandlePetitionKing", hero);
            return s.Text;
        });
        none.Should().Contain("  " + L(lang, "prison.petition_no_king"));
    }

    private static readonly string[] MaximumSecurityKeys =
    {
        "prison.door_max_security", "prison.demand_murderer", "prison.escape_max_security", "prison.escape_no_chance",
        "prison.escape_full_sentence",
    };

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task MaximumSecurity_IsInThePlayersLanguage(string lang)
    {
        var hero = Hero();
        hero.IsMurderConvict = true;
        string text = await InLanguage(lang, async () =>
        {
            var (prison, s) = Prison();
            await Run(prison, "HandleOpenCellDoor", hero);
            await Run(prison, "HandleDemandRelease", hero);
            await Run(prison, "HandleEscapeAttempt", hero);
            return s.Text;
        });
        Capture($"prison-max-security-{lang}.txt", text);
        EveryRowFits(text, $"{lang} maximum security");
        foreach (var key in MaximumSecurityKeys) text.Should().Contain("  " + L(lang, key), key);
        hero.PrisonEscapes.Should().Be(1, "a murder convict spends no escape attempt");
        if (lang == "hu") NoEnglishLeft(text, MaximumSecurityKeys);
        else text.Should().Contain("  The guards laugh. \"Murderers don't make demands.\"");
    }

    [Fact]
    public void TheGuardsAnswer_IsOneOfTheFive_InThePlayersLanguage()
    {
        string[] english = { GameConfig.PrisonDemandResponse1, GameConfig.PrisonDemandResponse2, GameConfig.PrisonDemandResponse3,
            GameConfig.PrisonDemandResponse4, GameConfig.PrisonDemandResponse5 };
        for (int i = 1; i <= 5; i++)
        {
            L("en", $"prison.demand_response_{i}").Should().Be(english[i - 1], "the English answer is the one it was");
            foreach (var lang in AllLanguages) L(lang, $"prison.demand_response_{i}").Should().NotBe($"prison.demand_response_{i}");
        }
        Src("Locations", "PrisonLocation.cs").Should().Contain("Loc.Get($\"prison.demand_response_{random.Next(5) + 1}\")");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("es")]
    public async Task Activities_Fit_AndAreInThePlayersLanguage(string lang)
    {
        var hero = Hero();
        string text = await InLanguage(lang, async () =>
        {
            var (prison, s) = Prison("0");
            await Run(prison, "HandleActivities", hero);
            return s.Text;
        });
        Capture($"prison-activities-{lang}.txt", text);
        EveryRowFits(text, $"{lang} activities");
        foreach (var activity in PrisonActivitySystem.Instance.GetAvailableActivities())
        {
            var info = PrisonActivitySystem.ActivityInfo[activity];
            string id = PrisonActivitySystem.ActivityKeyId(activity);
            string name = L(lang, $"prison.activity_{id}_name"), effect = L(lang, $"prison.activity_{id}_effect"), desc = L(lang, $"prison.activity_{id}_desc");
            text.Should().Contain(name).And.Contain(effect).And.Contain("    " + desc);
            L("en", $"prison.activity_{id}_name").Should().Be(info.Name, "the table is the English source");
            L("en", $"prison.activity_{id}_desc").Should().Be(info.Description);
            L("en", $"prison.activity_{id}_effect").Should().Be(info.Effect);
            if (lang == "hu")
            {
                NoEnglishLeft(text, new[] { $"prison.activity_{id}_desc" });
                if (info.Name != "Yoga") text.Should().NotContain(info.Name + " ", $"{info.Name} is shown in Hungarian");
            }
        }
        if (lang == "en") text.Should().Contain("Pushups         +1-2 Strength").And.Contain("Shadow Boxing   +1 Attack, +1 Defence");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public void PrisonersRace_IsInThePlayersLanguage(string lang)
    {
        var (prison, _) = Prison();
        var (walk, _) = Walk();
        foreach (var race in new[] { CharacterRace.Human, CharacterRace.Elf, CharacterRace.Troll })
        {
            string expected = InLang(lang, () => GameConfig.GetLocalizedRaceName(race));
            InLang(lang, () => (string)typeof(PrisonLocation).GetMethod("GetRaceDisplay", F)!.Invoke(prison, new object[] { race })!).Should().Be(expected);
            InLang(lang, () => (string)typeof(PrisonWalkLocation).GetMethod("GetRaceDisplay", F)!.Invoke(walk, new object[] { race })!).Should().Be(expected);
        }
        if (lang == "hu") InLang("hu", () => GameConfig.GetLocalizedRaceName(CharacterRace.Human)).Should().NotBe("Human");
    }

    // ================= the prison walk =================

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task PrisonWalk_PromptAndPrisonerRow_AreInThePlayersLanguage_AndFit(string lang)
    {
        var hero = Hero();
        hero.DaysInPrison = 0;
        var prisoner = new NPC { Name1 = LongName, Name2 = LongName, Race = CharacterRace.Gnoll, DaysInPrison = 200, HP = 10, MaxHP = 10 };
        string text = await InLanguage(lang, async () =>
        {
            var (walk, s) = Walk();
            typeof(PrisonWalkLocation).GetField("refreshMenu", F)!.SetValue(walk, false);
            await Run(walk, "DisplayPrisonWalkMenu", hero, false, true);
            await s.Term.WriteLineAsync();
            await Run(walk, "ShowPrisonerInfo", prisoner);
            return s.Text;
        });
        Capture($"prison-walk-{lang}.txt", text);
        EveryRowFits(text, $"{lang} prison walk");
        text.Should().Contain($"{L(lang, "prison_walk.prompt")} (?{L(lang, "prison.prompt_suffix")}");
        text.Should().Contain($"{LongName} {L(lang, "prison.the_race", InLang(lang, () => GameConfig.GetLocalizedRaceName(CharacterRace.Gnoll)))}");
        if (lang == "hu") text.Should().NotContain(" for menu) :").And.NotContain(" the Gnoll");
        else text.Should().Contain("Prison walk (? for menu) :").And.Contain($"{LongName} the Gnoll");
    }

    [Fact]
    public async Task PrisonGuards_AreNamedInThePlayersLanguage_AndLiveOnlyInTheFight()
    {
        string[] english = { "Royal Guard", "Prison Warden", "Iron Fist Guard", "Dungeon Keeper", "Jailer", "Tower Guard", "Cell Block Guardian", "Sheriff's Deputy" };
        for (int i = 0; i < english.Length; i++)
        {
            InLang("en", () => PrisonWalkLocation.GetGuardName(i)).Should().Be(english[i], "the English names are the ones they were");
            string hu = InLang("hu", () => PrisonWalkLocation.GetGuardName(i));
            hu.Should().NotBe(english[i]);
            foreach (var lang in AllLanguages) InLang(lang, () => PrisonWalkLocation.GetGuardName(i)).Should().NotContain("prison_walk.");
        }
        var guards = await InLanguage("hu", async () =>
        {
            var (walk, _) = Walk();
            var m = typeof(PrisonWalkLocation).GetMethod("GatherPrisonGuards", F)!;
            return await (Task<List<Character>>)m.Invoke(walk, Array.Empty<object>())!;
        });
        guards.Should().NotBeEmpty();
        guards.Select(g => g.Name2).Should().OnlyContain(n => !english.Contains(n), "the guards are named in Hungarian");
        // nothing outside the fight reads a guard by name: the names are only printed in BattlePrisonGuards
        string src = Src("Locations", "PrisonWalkLocation.cs");
        Regex.Matches(src, @"guard\.Name\d?").Count.Should().Be(3);
        src.Should().NotContain("guards.Where(").And.NotContain("Name2 ==");
    }

    // ================= the home =================

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task HomeUpgrades_Fit_AndAreInThePlayersLanguage(string lang)
    {
        var hero = Hero(5_000_000_000);
        hero.HomeLevel = 2; hero.BedLevel = 1; hero.ChestLevel = 3; hero.HearthLevel = 0; hero.GardenLevel = 4;
        string text = await InLanguage(lang, async () =>
        {
            var (home, s) = Home(hero, "0");
            await Run(home, "ShowHomeUpgrades");
            return s.Text;
        });
        Capture($"home-upgrades-{lang}.txt", text);
        EveryRowFits(text, $"{lang} home upgrades");
        string quarters = L(lang, "home.upgrade_type.quarters");
        text.Should().Contain(L(lang, "home.upgrade_next_label", quarters, 3, "").TrimEnd());
        // the garden row is the longest: its price goes on the next row when the row would pass 79
        var gardenRow = Rows(text).First(r => r.StartsWith("  [5] "));
        if (gardenRow.Length < 60) Rows(text)[Rows(text).IndexOf(gardenRow) + 1].Should().StartWith("      ").And.Contain("g  [");

        // every room maxed, and the one-time purchases owned or not
        var maxed = Hero(1);
        maxed.HomeLevel = 5; maxed.BedLevel = 5; maxed.ChestLevel = 5; maxed.HearthLevel = 5; maxed.GardenLevel = 5; maxed.HasStudy = true;
        foreach (bool sr in new[] { false, true })
        {
            maxed.ScreenReaderMode = sr;
            string other = await InLanguage(lang, async () =>
            {
                var (home, s) = Home(maxed, "0");
                await Run(home, "ShowHomeUpgrades");
                return s.Text;
            }, screenReader: sr);
            Capture($"home-upgrades-maxed{(sr ? "-sr" : "")}-{lang}.txt", other);
            EveryRowFits(other, $"{lang} home upgrades maxed{(sr ? " (screen reader)" : "")}");
        }
        if (lang == "hu") text.Should().NotContain(" Lv 3").And.Contain($"{quarters} 3. szint");
        else text.Should().Contain("Living Quarters Lv 3");
    }

    [Theory]
    [InlineData("en")] [InlineData("hu")]
    public async Task PetRoster_Fits_AndIsInThePlayersLanguage(string lang)
    {
        var hero = Hero();
        hero.PetRoster.Add(new UsurperRemake.Data.Pet { Id = "dire_wolf", Name = "Hosszunevu Farkas Ezustbunda", Level = 12 });
        hero.PetRoster.Add(new UsurperRemake.Data.Pet { Id = "forest_hawk", Name = "Kis Solyom", Level = 3 });
        string text = await InLanguage(lang, async () =>
        {
            var (home, s) = Home(hero, "");
            await Run(home, "ShowPetRoster");
            return s.Text;
        });
        Capture($"home-pets-{lang}.txt", text);
        EveryRowFits(text, $"{lang} pet roster");
        text.Should().Contain("  [0] " + L(lang, "ui.cancel"));
        text.Should().Contain(L(lang, "home.pet_roster_level", 12));
        bool anyCombat = text.Contains("[" + L(lang, "home.pet_role_combat") + "]"), anyPassive = text.Contains("[" + L(lang, "home.pet_role_passive") + "]");
        (anyCombat || anyPassive).Should().BeTrue("each pet shows its role");
        if (lang == "hu") text.Should().NotContain("[Combat]").And.NotContain("[Passive]").And.NotContain("Cancel").And.NotContain(" Lv12");
        else text.Should().Contain("  Lv12 ");
    }

    [Fact]
    public void HomeRows_AreKeyed_AndTheEnglishReadsAsBefore()
    {
        L("en", "home.herb_full_tag").Should().Be("FULL");
        L("en", "home.parenting_child_age", 7).Should().Be("age 7");
        L("en", "home.upgrade_next_label", "Bed", 4, ": Silk").Should().Be("Bed Lv 4: Silk");
        L("en", "home.pet_roster_level", 3).Should().Be("Lv3 ");
        L("en", "home.news_resurrected", "Al", "Bo").Should().Be("Al was resurrected by their ally 'Bo'!");
        L("en", "inn.relationship_spouse").Should().Be("Spouse");
        L("en", "inn.relationship_lover").Should().Be("Lover");
        foreach (var key in new[] { "home.herb_full_tag", "home.parenting_child_age", "home.upgrade_next_label", "home.pet_role_combat",
            "home.pet_role_passive", "home.news_resurrected", "inn.relationship_spouse", "inn.relationship_lover" })
            L("hu", key).Should().NotBe(L("en", key), key);
        // the longest herb row: "  [5] <herb> (99/99) [FULL]" in Hungarian
        foreach (HerbType type in Enum.GetValues(typeof(HerbType)))
        {
            if ((int)type < 1) continue;
            string row = InLang("hu", () => $"  [5] {HerbData.LocName(type)} (99/99) [{Loc.Get("home.herb_full_tag")}]");
            row.Length.Should().BeLessOrEqualTo(MaxWidth, row);
        }
        string src = Src("Locations", "HomeLocation.cs");
        src.Should().Contain("partners.Add((npc, Loc.Get(\"inn.relationship_spouse\")));").And.Contain("partners.Add((npc, Loc.Get(\"inn.relationship_lover\")));")
            .And.Contain("$\" ({Loc.Get(\"home.parenting_child_age\", c.Age)}, \"")
            .And.Contain("Newsy(true, Loc.Get(\"home.news_resurrected\", toResurrect.DisplayName, currentPlayer.Name))");
    }

    // ================= Electron =================

    private static readonly Regex MenuItem = new(@"Key = ([^,]+), Label = (.+?), Category = ""([^""]*)"", Icon = ""([^""]*)""");

    // The ids at d4cca0b: Key|Category|Icon, in order.
    private const string PrisonIds = "\"W\"|info|list;\"D\"|social|shout;\"O\"|action|door;\"E\"|danger|escape;\"S\"|info|info;\"A\"|action|activity;\"B\"|shop|gold;\"P\"|social|petition;\"V\"|social|vex;\"Q\"|navigate|back;";
    private const string WalkIds = "\"P\"|info|list;\"F\"|danger|escape;\"S\"|info|info;\"R\"|navigate|back;";
    private const string HomeIds = "\"E\"|service|rest;\"U\"|service|upgrade;\"D\"|storage|chest;\"W\"|storage|chest;\"L\"|storage|chest;\"A\"|service|herb;\"J\"|service|herb;\"T\"|info|trophy;\"F\"|social|family;\"C\"|social|children;\"P\"|social|love;\"B\"|social|bedroom;\"X\"|service|resurrect;\"Y\"|social|pet;\"I\"|info|inventory;\"G\"|service|gear;\"V\"|info|party;\"H\"|service|potion;\"Z\"|service|sleep;\"S\"|info|info;\"R\"|navigate|back;";

    [Theory]
    [InlineData("PrisonLocation.cs", PrisonIds, "name: Loc.Get(\"prison.title\"),")]
    [InlineData("PrisonWalkLocation.cs", WalkIds, "name: Loc.Get(\"prison_walk.title\"),")]
    [InlineData("HomeLocation.cs", HomeIds, "name: Loc.Get(\"home.header\"),")]
    public void ElectronMenus_LabelsAreKeys_AndTheIdsAreUnchanged(string file, string ids, string locationName)
    {
        string src = Src("Locations", file);
        var items = MenuItem.Matches(src).ToList();
        string.Concat(items.Select(m => $"{m.Groups[1].Value}|{m.Groups[3].Value}|{m.Groups[4].Value};"))
            .Should().Be(ids, "the keys the client sends and the categories and icons it styles by stay as they were");
        items.Select(m => m.Groups[2].Value).Should().OnlyContain(l => l.StartsWith("Loc.Get(\""), $"every {file} Electron label is keyed");
        foreach (var m in items)
        {
            string key = Regex.Match(m.Groups[2].Value, "Loc.Get\\(\"([^\"]+)\"").Groups[1].Value;
            foreach (var lang in AllLanguages) L(lang, key).Should().NotBe(key, $"{lang} {key} exists");
            L("hu", key).Should().NotBe(L("en", key), $"{key} has a Hungarian label");
        }
        src.Should().Contain(locationName, "the location name is as it was at d4cca0b");
    }

    [Fact]
    public void ElectronLabels_KeepTheirEnglish_AndThePrisonDaysAreKeyed()
    {
        string[] prison = { "Who is here?", "Demand release", "Open the cell door", "Escape attempt", "Status", "Activities", "Pay Bail", "Petition the King", "Speak with Vex", "Wait / Quit" };
        string[] walk = { "List prisoners", "Free a prisoner", "Status" };
        string[] home = { "Rest & Recover", "Upgrade Home", "Deposit to Chest", "Withdraw from Chest", "List Chest", "Gather Herbs", "Use Herb", "Trophies", "Family",
            "Spend Time with Children", "Spend Time with Spouse", "Bedroom", "Resurrect Partner", "Tamed Beasts", "Inventory", "Gear for Partner", "Party Inventory",
            "Healing Potion", "Sleep / Wait Night", "Status" };
        foreach (var (file, english) in new[] { ("PrisonLocation.cs", prison), ("PrisonWalkLocation.cs", walk), ("HomeLocation.cs", home) })
        {
            var labels = MenuItem.Matches(Src("Locations", file)).Select(m => Regex.Match(m.Groups[2].Value, "Loc.Get\\(\"([^\"]+)\"").Groups[1].Value)
                .Where(k => k != "ui.return").Select(k => L("en", k)).ToList();
            labels.Should().Equal(english, $"the English {file} labels read as before");
        }
        L("en", "engine.days_remaining", 4).Should().Be("Days remaining: 4");
        Src("Locations", "PrisonLocation.cs").Should().Contain("description: Loc.Get(\"engine.days_remaining\", player.DaysInPrison),");
        // the client keys its scene on the location name; the prison and home names were already keyed
        ClientSrc("game-ui.js").Should().Contain("keywords: ['home']");
    }

    // ================= news, in the writer's language =================

    private static readonly string[] NewsFiles =
    {
        "Locations/PrisonLocation.cs", "Systems/NPCPetitionSystem.cs", "Systems/ChallengeSystem.cs", "Locations/HomeLocation.cs",
        "Systems/CityControlSystem.cs", "Systems/PrisonActivitySystem.cs", "Core/King.cs",
    };

    [Fact]
    public void News_IsKeyed_InEveryLanguage()
    {
        foreach (var file in NewsFiles)
        {
            string src = Src(file.Split('/'));
            Regex.IsMatch(src, @"Newsy\((?:(?:true|false),\s*)?\$""").Should().BeFalse($"{file} writes no interpolated English news");
            Regex.IsMatch(src, @"news\.Add\(\((?:true|false),\s*\$""").Should().BeFalse($"{file} queues no interpolated English news");
        }
        var keys = NewsFiles.SelectMany(f => Regex.Matches(Src(f.Split('/')), "Loc\\.Get\\(\"((?:prison|petition|challenge|city|castle|home)\\.news_[a-z_]+)\"")
            .Select(m => m.Groups[1].Value)).Distinct().ToList();
        keys.Should().HaveCount(5 + 22 + 21 + 1 + 5 + 1 + 4, "every news line of these files is keyed");
        foreach (var key in keys)
        {
            foreach (var lang in AllLanguages) L(lang, key).Should().NotBe(key, $"{lang} {key}");
            L("hu", key).Should().NotBe(L("en", key), key);
            foreach (var lang in AllLanguages)
                Regex.Matches(L(lang, key), @"\{\d+\}").Select(m => m.Value).Distinct().OrderBy(v => v)
                    .Should().Equal(Regex.Matches(L("en", key), @"\{\d+\}").Select(m => m.Value).Distinct().OrderBy(v => v), $"{lang} {key} keeps its arguments");
        }
    }

    [Fact]
    public void News_ReadsAsBeforeInEnglish()
    {
        L("en", "petition.news_tax_granted", "King", "Al", "Bo").Should().Be("King Al granted tax relief to Bo. The people approve!");
        L("en", "petition.news_scandal", "Al", "Bo").Should().Be("Scandalous rumors swirl about Al and Bo...");
        L("en", "challenge.news_storms_castle", "Al", "Queen", "Bo").Should().Be("Al storms the castle to challenge Queen Bo!");
        L("en", "challenge.news_threat", "Al", 40).Should().Be("THREAT: Al (Level 40) has declared intent to challenge for the throne!");
        L("en", "challenge.news_heir_claims", "Al", "King", "Al").Should().Be("The designated heir Al has claimed the throne! ALL HAIL King Al!");
        L("en", "city.news_defeated_control", "Wolves", "Bears").Should().Be("'Wolves' defeated 'Bears' and now controls the city!");
        L("en", "prison.news_bail_paid", "Al", "5,000").Should().Be("Al paid 5,000 gold bail and was released from prison.");
        L("en", "prison.news_vex_escape", "Al", "Vex").Should().Be("Al escaped from the Royal Prison with Vex's help!");
        L("en", "castle.news_monster_escaped", "Wyvern").Should().Be("The unfed Wyvern has escaped from the castle moat!");
        L("en", "home.news_resurrected", "Al", "Bo").Should().Be("Al was resurrected by their ally 'Bo'!");
    }

    [Fact]
    public void CourtUpkeepNews_IsInTheWritersLanguage_AndTheCourtKeepsItsEnglishNames()
    {
        var court = new RoyalCourtSaveData { KingName = LongName, Treasury = 0 };
        court.Guards.Add(new RoyalGuardSaveData { Name = "Deserter", Loyalty = 5 });
        var monsters = MonsterGuardTypes.AvailableMonsters.Select(m => m.Name).ToList();
        court.MonsterGuards.AddRange(Enumerable.Range(0, 60).Select(i => new MonsterGuardSaveData { Name = monsters[i % monsters.Count], Level = 5, HP = 10, MaxHP = 10 }));
        var news = InLang("hu", () => King.ApplyGuardUpkeep(court, new Dictionary<string, DateTime>(), new Random(1)));
        news.Select(n => n.Text).Should().Contain(L("hu", "castle.news_guard_deserted", "Deserter")).And.Contain(L("hu", "castle.news_treasury_crisis"));
        news.Where(n => n.Text.Contains("unfed") || n.Text.Contains("Royal treasury")).Should().BeEmpty("the news is in the writer's language");
        news.Any(n => monsters.Any(m => n.Text == L("hu", "castle.news_monster_escaped", m))).Should().BeTrue("an unfed monster escaped");
    }

    [Fact]
    public void CityControlNews_IsInTheWritersLanguage()
    {
        var buffer = new List<string>();
        NewsSystem.Instance.SetCatchUpBuffer(buffer);
        try
        {
            var c = new Character { Name1 = LongName, Name2 = LongName, Team = "Wolves" };
            InLang("hu", () => { CityControlSystem.Instance.ForceLeaveTeam(c); return 0; });
            InLang("hu", () => { CityControlSystem.Instance.RemoveCityControl("Nobody Here"); return 0; });
        }
        finally { NewsSystem.Instance.ClearCatchUpBuffer(); }
        string all = string.Join("\n", buffer);
        all.Should().Contain(L("hu", "city.news_left_team", LongName, "Wolves"));
        all.Should().NotContain("has left").And.NotContain("no longer under team control");
    }

    [Fact]
    public void ThroneTitles_InNews_AreShownKeyed_AndGetTitleStaysTheStoredEnglish()
    {
        var queen = LongKing(sex: CharacterSex.Female);
        queen.GetTitle().Should().Be("Queen");
        InLang("hu", () => queen.TitleLabel()).Should().Be(L("hu", "castle.queen"));
        InLang("en", () => queen.TitleLabel()).Should().Be("Queen");
        InLang("hu", () => King.CreateNewKing("X", CharacterAI.Human, CharacterSex.Male).TitleLabel()).Should().Be(L("hu", "castle.king"));
        Rows(Src("Systems", "ChallengeSystem.cs")).Where(r => !r.TrimStart().StartsWith("//"))
            .Should().NotContain(r => r.Contains(".GetTitle()"), "the news shows TitleLabel(); GetTitle feeds stored values only");
    }

    // ================= stored and matched values stay English =================

    [Theory]
    [InlineData("en")] [InlineData("hu")] [InlineData("fr")]
    public void StoredCourtValues_StayEnglish_WhateverTheLanguage_AndSurviveASave(string lang)
    {
        var king = InLang(lang, () => King.CreateNewKing("", CharacterAI.Computer, CharacterSex.Male));
        king.Name.Should().Be("Unknown Ruler", "the fallback name is stored and matched by name");
        InLang(lang, () => new CourtMember().Role).Should().Be("Advisor", "the role is saved and shown through CourtRoleLabel");
        MonsterGuardTypes.AvailableMonsters.Select(m => m.Name).Should().Equal(
            "War Hound", "Cave Troll", "Giant Spider", "Dire Wolf", "Stone Golem", "Hellhound", "Manticore", "Wyvern", "Basilisk", "Iron Golem",
            "Elder Drake", "Abyssal Fiend", "Storm Titan", "Void Wyrm", "Champion of Maelketh");

        // a monster bought in any language is stored under its English name, survives a save, and the
        // defence losses still find it by that name
        var court = new RoyalCourtSaveData { KingName = king.Name, Treasury = 1_000_000 };
        var (name, level, cost) = MonsterGuardTypes.AvailableMonsters[7];
        InLang(lang, () => King.AddMonsterGuard(court, name, level, cost)).Should().BeTrue();
        var opts = new JsonSerializerOptions { IncludeFields = true };
        var reloaded = JsonSerializer.Deserialize<RoyalCourtSaveData>(JsonSerializer.Serialize(court, opts), opts)!;
        reloaded.KingName.Should().Be("Unknown Ruler");
        reloaded.MonsterGuards.Should().ContainSingle(m => m.Name == "Wyvern");
        var losses = new DefenceLosses();
        losses.MonstersSlain.Add("Wyvern");
        losses.ApplyTo(reloaded);
        reloaded.MonsterGuards.Should().BeEmpty("the slain monster is matched by its English name");

        // the crimes a failed challenge stores are English
        string src = Src("Systems", "ChallengeSystem.cs");
        src.Should().Contain("ImprisonChallenger(challenger, FailedThroneChallengerSentence, \"Failed throne challenge\", losses: losses);")
            .And.Contain("CastleLocation.PrisonerRecord(oldKing.Name, 14, \"Deposed monarch\")")
            .And.Contain("ImprisonChallenger(teamLeader as NPC, FailedCityChallengerSentence, \"Failed city takeover\");");
    }

    // ================= mail to the monarch, in the monarch's language =================

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
    public async Task PrisonPetitions_ReachTheMonarch_InTheMonarchsLanguage()
    {
        Player("queen_hu", "QueenHu", "hu");
        Player("king_en", "KingEn", "en");
        await InLanguage("fr", async () =>
        {
            foreach (var to in new[] { "QueenHu", "KingEn" })
            {
                await Db.SendMessageLocalized(LongName, to, "petition", lang => PrisonLocation.BailPetitionMail(lang, LongName));
                await Db.SendMessageLocalized(LongName, to, "petition", lang => PrisonLocation.ClemencyPleaMail(lang, LongName));
                await Db.SendMessageLocalized(LongName, to, "petition", lang => PrisonLocation.DemandReleaseMail(lang, LongName));
            }
            return 0;
        });
        var mails = Mails();
        mails.Should().HaveCount(6);
        mails.Take(3).Select(m => m.Message).Should().Equal(
            L("hu", "prison.mail_petition_bail", LongName), L("hu", "prison.mail_plea_clemency", LongName), L("hu", "prison.mail_demand_release", LongName));
        mails.Skip(3).Select(m => m.Message).Should().Equal(new[]
        {
            $"{LongName} petitions from prison: \"Please set bail for my release! I await your mercy, Your Majesty.\"",
            $"{LongName} pleads from prison: \"I beg for clemency! Please pardon my crimes, Your Majesty.\"",
            $"{LongName} demands release from prison: \"LET ME OUT! I demand to be freed at once!\"",
        }, "the English mail reads as before");
        mails.Should().OnlyContain(m => m.From == LongName && m.Type == "petition");
        string src = Src("Locations", "PrisonLocation.cs");
        src.Should().NotContain("backend.SendMessage(playerName", "every petition mail is built in the monarch's language")
            .And.Contain("lang => BailPetitionMail(lang, playerName)")
            .And.Contain("lang => ClemencyPleaMail(lang, playerName)")
            .And.Contain("lang => DemandReleaseMail(lang, playerName)");
    }
}
