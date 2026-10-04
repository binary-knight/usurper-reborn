using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the achievement, founder statue, quest and faction data in the player's language. What is stored,
/// compared or sent stays English: achievement ids (saves, Steam), the founder records, quest ids and the
/// bounty board initiator, faction names and rank numbers. Each shown text is looked up by a stable id.
/// Every row fits 79 columns in all five languages with a 30-character name and the longest names.
/// </summary>
[Collection("SharedGameSingletons")]
public class DataAchieve125Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] OtherLanguages = { "es", "fr", "hu", "it" };

    // Names that are the same word in Hungarian as in English.
    private static readonly string[] SameInHungarian = { "???", "MVP" };

    private static readonly FieldInfo QuestDb = typeof(QuestSystem).GetField("questDatabase", SNP)!;
    private readonly List<Quest> _savedQuests;
    private readonly FieldInfo _factionFallback = typeof(FactionSystem).GetField("_fallbackInstance", SNP)!;
    private readonly object? _savedFaction;

    public DataAchieve125Tests()
    {
        _savedQuests = new List<Quest>((List<Quest>)QuestDb.GetValue(null)!);
        _savedFaction = _factionFallback.GetValue(null);
        _factionFallback.SetValue(null, null);
    }

    public void Dispose()
    {
        var db = (List<Quest>)QuestDb.GetValue(null)!;
        db.Clear();
        db.AddRange(_savedQuests);
        _factionFallback.SetValue(null, _savedFaction);
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

    private static async Task<T> InLanguage<T>(string lang, Func<Task<T>> body, bool screenReader = false)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = screenReader;
            return await body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static T InLang<T>(string lang, Func<T> body, bool screenReader = false) =>
        InLanguage(lang, () => Task.FromResult(body()), screenReader).GetAwaiter().GetResult();

    private static string L(string lang, string key, params object[] args) => Loc.GetIn(lang, key, args);

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static void Capture(string name, string text)
    {
        var dir = Environment.GetEnvironmentVariable("USURPER_EVIDENCE_DIR");
        if (!string.IsNullOrEmpty(dir)) File.WriteAllText(Path.Combine(dir, name), text);
    }

    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in Rows(text))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
    }

    /// <summary>The English text is not on the screen as a whole phrase (word bounded), unless it is the same word in Hungarian.</summary>
    private static void NotOnScreen(string screen, string english, string hungarian, string what)
    {
        if (string.IsNullOrEmpty(english) || english == hungarian) return;
        Regex.IsMatch(screen, $@"(?<![\p{{L}}]){Regex.Escape(english)}(?![\p{{L}}])").Should().BeFalse($"{what} \"{english}\" is shown in Hungarian");
    }

    private static List<Achievement> BuiltIn() => AchievementSystem.GetBuiltInAchievements();

    private static async Task<string> Popup(string lang, List<Achievement> list, bool screenReader)
    {
        var s = NewScreen();
        string name = list.Count == 1 ? "ShowAchievementUnlock" : "ShowMultipleAchievements";
        var m = typeof(AchievementSystem).GetMethod(name, SNP)!;
        await InLanguage(lang, async () =>
        {
            await (Task)m.Invoke(null, list.Count == 1 ? new object[] { s.Term, list[0] } : new object[] { s.Term, list })!;
            return 0;
        }, screenReader);
        return s.Text;
    }

    private static string Source(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, rel));
    }

    /// <summary>A timer-less DebugLogger swapped in as the instance, so the Steam unlock line can be read.</summary>
    private sealed class LogCapture : IDisposable
    {
        private const BindingFlags Priv = BindingFlags.NonPublic | BindingFlags.Instance;
        private static readonly FieldInfo InstanceField = typeof(DebugLogger).GetField("_instance", SNP)!;
        private readonly object? _previous;
        private readonly ConcurrentQueue<string> _queue = new();

        public LogCapture()
        {
            _previous = InstanceField.GetValue(null);
            var logger = (DebugLogger)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(DebugLogger));
            typeof(DebugLogger).GetField("logQueue", Priv)!.SetValue(logger, _queue);
            typeof(DebugLogger).GetField("isEnabled", Priv)!.SetValue(logger, true);
            typeof(DebugLogger).GetField("minimumLevel", Priv)!.SetValue(logger, DebugLogger.LogLevel.Debug);
            InstanceField.SetValue(null, logger);
        }

        public string[] SteamUnlocks => _queue.ToArray().Where(l => l.Contains("UnlockAchievement(")).ToArray();

        public void Dispose() => InstanceField.SetValue(null, _previous);
    }

    // ---------- achievements ----------

    [Fact]
    public void Achievements_HaveTheirTextInFiveLanguages_KeyedByTheEnglishId()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        var list = BuiltIn();
        list.Should().HaveCount(79);
        foreach (var a in list)
        {
            a.Id.Should().MatchRegex("^[a-z0-9_]+$", "the id is the saved and Steam name, English");
            foreach (var (field, stored) in new[] { ("name", a.Name), ("desc", a.Description), ("unlock", a.UnlockMessage ?? ""), ("hint", a.SecretHint) })
            {
                string key = $"achievement.{a.Id}.{field}";
                if (string.IsNullOrEmpty(stored)) { Loc.HasIn("en", key).Should().BeFalse($"{key} has no text to key"); continue; }
                L("en", key).Should().Be(stored, $"{key} is the English of the data");
                foreach (var lang in OtherLanguages)
                {
                    Loc.HasIn(lang, key).Should().BeTrue($"{key} has a {lang} text");
                    L(lang, key).Should().NotBeNullOrWhiteSpace();
                    L(lang, key).Should().NotContain("—").And.NotContain("–");
                }
                if (!SameInHungarian.Contains(stored))
                    L("hu", key).Should().NotBe(stored, $"{key} is translated into Hungarian");
            }
            InLang("hu", () => a.LocName()).Should().Be(L("hu", $"achievement.{a.Id}.name"));
            InLang("en", () => a.LocName()).Should().Be(a.Name);
        }
        // An edited achievements.json shows its own text, not the built-in key's.
        var edited = new Achievement { Id = "first_blood", Name = "My Own Name", Description = "My own goal" };
        InLang("hu", () => edited.LocName()).Should().Be("My Own Name");
        InLang("hu", () => edited.LocDescription()).Should().Be("My own goal");
    }

    [Fact]
    public async Task AchievementPopups_InHungarian_HaveNoEnglish_AndEveryRowFits_InFiveLanguages()
    {
        var list = BuiltIn();
        var hu = new StringBuilder();
        foreach (var lang in AllLanguages)
            foreach (var sr in new[] { false, true })
                foreach (var a in list)
                {
                    var text = await Popup(lang, new List<Achievement> { a }, sr);
                    EveryRowFits(text, $"[{lang}] {a.Id} popup");
                    foreach (var row in Rows(text).Where(r => r.StartsWith("║") || r.StartsWith("╔") || r.StartsWith("╠") || r.StartsWith("╚")))
                        row.Length.Should().Be(60, $"[{lang}] the {a.Id} popup keeps its 60-column box: \"{row}\"");
                    text.Should().Contain(InLang(lang, () => a.LocName()));
                    if (lang == "hu")
                    {
                        hu.Append(text);
                        NotOnScreen(text, a.Name, L("hu", $"achievement.{a.Id}.name"), $"{a.Id} name");
                        NotOnScreen(text, a.Description, L("hu", $"achievement.{a.Id}.desc"), $"{a.Id} goal");
                        if (!string.IsNullOrEmpty(a.UnlockMessage))
                            NotOnScreen(text, a.UnlockMessage, L("hu", $"achievement.{a.Id}.unlock"), $"{a.Id} unlock message");
                    }
                }
        Capture("achievement-popups-hu.txt", hu.ToString());
        // The longest names in the many-unlocks summary, in every language.
        foreach (var lang in AllLanguages)
        {
            var longest = list.OrderByDescending(a => InLang(lang, () => a.LocName()).Length).Take(10).ToList();
            foreach (var sr in new[] { false, true })
            {
                var text = await Popup(lang, longest, sr);
                EveryRowFits(text, $"[{lang}] many-unlocks summary");
                foreach (var row in Rows(text).Where(r => r.StartsWith("║")))
                    row.Length.Should().Be(60, $"[{lang}] the summary box keeps its 60 columns: \"{row}\"");
            }
        }
    }

    [Fact]
    public async Task AchievementPopups_InEnglish_KeepTheirWords_AndWrapTheLongGoal()
    {
        var first = BuiltIn().Single(a => a.Id == "first_blood");
        var rows = Rows(await Popup("en", new List<Achievement> { first }, true));
        rows.Should().Contain("  [B] First Blood").And.Contain("  Defeat your first monster").And.Contain("  \"Your journey as a warrior begins!\"");
        // The Gauntlet goal (104 columns) used to run past the box; it wraps inside it now.
        var grand = BuiltIn().Single(a => a.Id == "grand_champion");
        var box = Rows(await Popup("en", new List<Achievement> { grand }, false));
        var goalRows = box.Where(r => r.StartsWith("║  ") && !r.Contains($"{grand.GetTierSymbol()} ") && !r.Contains("Rewards:") && !r.Contains("UNLOCKED")).ToList();
        goalRows.Should().HaveCountGreaterThan(1);
        string.Join(" ", goalRows.Select(r => r.Trim('║').Trim())).Should().Be(grand.Description);
    }

    [Fact]
    public void AchievementLists_HomeAndMainStreet_FitIn79Columns_InFiveLanguages()
    {
        foreach (var lang in AllLanguages)
            foreach (var a in BuiltIn())
            {
                // Home trophies: "    [B] [X] Name" then the goal rows.
                string name = InLang(lang, () => a.LocName());
                var goal = InLang(lang, () => AchievementSystem.TrophyGoalRows(a));
                ($"    {a.GetTierSymbol()} [X] {name}" + goal[0]).Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {a.Id} trophy row");
                foreach (var row in goal.Skip(1)) row.Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {a.Id} trophy goal row");
                string.Join(" ", goal.Select(r => r.TrimStart(' ', '-').Trim())).Should().Be(InLang(lang, () => a.LocDescription()));
            }
        InLang("hu", () => MainStreetLocation.AchievementCategoryLabel(AchievementCategory.Combat)).Should().Be(L("hu", "main_street.achieve_cat_combat"));
        Source("Scripts/Locations/HomeLocation.cs").Should().Contain("[X] {achievement.LocName()}").And.NotContain("{achievement.Name}");
        Source("Scripts/Locations/MainStreetLocation.cs").Should().NotContain("achievement.Name").And.NotContain("achievement.Description")
            .And.NotContain("achievement.SecretHint");
    }

    [Fact]
    public void UnlockingInHungarian_RecordsTheSameId_AndSendsSteamTheSameName()
    {
        AchievementSystem.Initialize();
        var recorded = new Dictionary<string, (string[] Ids, string[] Steam)>();
        foreach (var lang in new[] { "en", "hu" })
        {
            var player = new Character { Name1 = LongName, Name2 = LongName };
            using var log = new LogCapture();
            InLang(lang, () => AchievementSystem.TryUnlock(player, "first_blood")).Should().BeTrue();
            recorded[lang] = (player.Achievements.UnlockedAchievements.ToArray(), log.SteamUnlocks);
        }
        recorded["hu"].Ids.Should().Equal("first_blood");
        recorded["hu"].Ids.Should().Equal(recorded["en"].Ids);
        recorded["en"].Steam.Should().ContainSingle().Which.Should().Contain("UnlockAchievement(first_blood)");
        recorded["hu"].Steam.Should().ContainSingle().Which.Should().Contain("UnlockAchievement(first_blood)");
        // The client relay marker carries the id.
        Source("Scripts/Systems/AchievementSystem.cs").Should().Contain("$\"\\x1B]99;ACH:{achievementId}\\x07\"");
    }

    [Fact]
    public void BroadcastAndNews_NameTheAchievementInTheirLanguage_AndFit()
    {
        var list = BuiltIn();
        foreach (var lang in AllLanguages)
        {
            var longest = list.OrderByDescending(a => a.NameIn(lang).Length).First();
            foreach (var sr in new[] { false, true })
            {
                string text = Regex.Replace(AchievementSystem.BroadcastLine(lang, LongName, longest, sr), "\u001b\\[[0-9;]*m", "");
                var lines = text.Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
                Regex.Replace(string.Join(" ", lines.Select(l => l.Trim())), " +", " ").Should().Contain(longest.NameIn(lang)).And.Contain(LongName);
                foreach (var line in lines)
                    line.Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] broadcast \"{line}\"");
            }
            string news = InLang(lang, () => Loc.Get("achievement.news_unlocked", LongName, longest.LocName()));
            news.Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] news \"{news}\"");
        }
        var mvp = list.Single(a => a.Id == "first_blood");
        AchievementSystem.BroadcastLine("hu", null, mvp, false).Should().Contain(L("hu", "achievement.broadcast_someone"))
            .And.Contain(L("hu", "achievement.first_blood.name")).And.NotContain("has earned").And.NotContain("First Blood");
        AchievementSystem.BroadcastLine("en", LongName, mvp, false).Should().Be($"\r\n\u001b[1;33m  ★ {LongName} has earned [First Blood]!\u001b[0m\r\n");
        AchievementSystem.BroadcastLine("en", LongName, mvp, true).Should().Be($"\r\n  [Achievement] {LongName} has earned [First Blood]!\r\n");
        // News is written in the writer's language, the name looked up by the id.
        InLang("hu", () => Loc.Get("achievement.news_unlocked", LongName, mvp.LocName())).Should().Contain(L("hu", "achievement.first_blood.name"));
    }

    [Fact]
    public void WikiExport_EnglishIsUnchanged_AndOtherLanguagesUseTheirKeys()
    {
        var export = (System.Collections.IEnumerable)typeof(WikiDataExporter).GetMethod("Achievements", SNP)!.Invoke(null, null)!;
        var options = (JsonSerializerOptions)typeof(WikiDataExporter).GetField("JsonOptions", SNP)!.GetValue(null)!;
        var json = JsonSerializer.SerializeToElement(export, options);
        var byId = BuiltIn().ToDictionary(a => a.Id);
        int count = 0;
        foreach (var e in json.EnumerateArray())
        {
            var a = byId[e.GetProperty("id").GetString()!];
            e.GetProperty("name").GetProperty("en").GetString().Should().Be(a.Name);
            e.GetProperty("description").GetProperty("en").GetString().Should().Be(a.Description);
            e.GetProperty("secretHint").GetString().Should().Be(a.SecretHint);
            foreach (var lang in OtherLanguages)
            {
                e.GetProperty("name").GetProperty(lang).GetString().Should().Be(L(lang, $"achievement.{a.Id}.name"));
                e.GetProperty("description").GetProperty(lang).GetString().Should().Be(L(lang, $"achievement.{a.Id}.desc"));
            }
            count++;
        }
        count.Should().Be(79);
        // The English part, serialized, is what the 1.2.4 exporter wrote (names and goals as {"en": ...}).
        var englishOnly = BuiltIn().OrderBy(a => a.Id).Select(a => new
        {
            id = a.Id, name = new Dictionary<string, string> { ["en"] = a.Name }, description = new Dictionary<string, string> { ["en"] = a.Description },
            a.Category, a.Tier, a.IsSecret, spoiler = a.IsSecret, a.SecretHint, a.PointValue, a.GoldReward, a.ExperienceReward
        }).ToArray();
        var reduced = json.EnumerateArray().Select(e =>
        {
            var o = System.Text.Json.Nodes.JsonNode.Parse(e.GetRawText())!.AsObject();
            foreach (var f in new[] { "name", "description" })
                foreach (var lang in OtherLanguages) o[f]!.AsObject().Remove(lang);
            return o;
        }).ToArray();
        JsonSerializer.Serialize(reduced, options).Should().Be(JsonSerializer.Serialize(englishOnly, options));
    }

    // ---------- founder statues ----------

    private static readonly FounderStatueData.StatueLocationTag[] Places =
    {
        FounderStatueData.StatueLocationTag.Pantheon, FounderStatueData.StatueLocationTag.Castle, FounderStatueData.StatueLocationTag.MainStreetMini,
    };

    private static async Task<string> StatueScreen(string lang, FounderStatueData.StatueLocationTag where, bool screenReader)
    {
        int count = FounderStatueData.GetStatuesAt(where).Count();
        var input = Enumerable.Range(1, count).SelectMany(i => new[] { i.ToString(), "" }).Append("R").ToArray();
        var s = NewScreen(input);
        await InLanguage(lang, async () => { await FounderStatueSystem.ShowStatuesAt(where, s.Term); return 0; }, screenReader);
        return s.Text;
    }

    private static string Snapshot() => JsonSerializer.Serialize(FounderStatueData.Statues);

    [Fact]
    public async Task FounderStatues_InHungarian_ShowTagsAndInscriptionsInHungarian_AndFit()
    {
        var hu = new StringBuilder();
        foreach (var where in Places)
            foreach (var sr in new[] { false, true })
                hu.Append(await StatueScreen("hu", where, sr));
        string text = hu.ToString();
        Capture("founder-statues-hu.txt", text);
        foreach (var s in FounderStatueData.Statues)
        {
            NotOnScreen(text, s.AchievementTag, InLang("hu", () => FounderStatueSystem.TagLabel(s.AchievementTag)), "plaque header");
            var key = FounderStatueSystem.InscriptionKey(s);
            key.Should().NotBeNull($"every inscription is keyed: \"{s.Inscription}\"");
            L("hu", key!).Should().NotBe(s.Inscription);
            // The first words of the English inscription are not on screen.
            text.Should().NotContain(string.Join(" ", s.Inscription.Split(' ').Take(4)));
            text.Should().Contain(string.Join(" ", L("hu", key!).Split(' ').Take(3)));
        }
        text.Should().NotContain(" Unknown").And.Contain(L("hu", "founder.unknown"));
        foreach (var lang in AllLanguages)
            foreach (var where in Places)
                foreach (var sr in new[] { false, true })
                    EveryRowFits(await StatueScreen(lang, where, sr), $"[{lang}] {where} statues");
        foreach (var lang in AllLanguages)
            foreach (var s in FounderStatueData.Statues)
            {
                foreach (var row in InLang(lang, () => FounderStatueSystem.InscriptionRows(FounderStatueSystem.InscriptionText(s), 72)))
                    row.Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] inscription row \"{row}\"");
                ("  [9] " + InLang(lang, () => FounderStatueSystem.ListRow(s))).Length.Should().BeLessOrEqualTo(MaxWidth);
                InLang(lang, () => $"  === {FounderStatueSystem.TagLabel(s.AchievementTag)} ===").Length.Should().BeLessOrEqualTo(MaxWidth);
            }
        foreach (var key in new[] { "founder.unknown", "founder.tag_immortal", "founder.tag_immortal_lost", "founder.tag_manwe_slayer",
                     "founder.tag_manwe_slayer_lost", "founder.tag_long_walker", "founder.tag_cycle_walker", "founder.tag_first_shaman", "founder.tag_lv100_founder" })
            foreach (var lang in OtherLanguages)
                L(lang, key).Should().NotBe(L("en", key), $"{key} is translated into {lang}");
    }

    [Fact]
    public async Task FounderRecords_StayEnglish_WhateverTheReadersLanguage()
    {
        string before = Snapshot();
        foreach (var lang in AllLanguages)
            foreach (var where in Places)
                await StatueScreen(lang, where, false);
        Snapshot().Should().Be(before, "showing the statues writes nothing into the records");
        // The records are static data, saved nowhere; written and read back as JSON they stay English.
        var back = JsonSerializer.Deserialize<List<FounderStatueData.FounderStatue>>(before)!;
        back.Select(s => s.Inscription).Should().Equal(FounderStatueData.Statues.Select(s => s.Inscription));
        back.Select(s => s.AchievementTag).Should().Contain("Manwe-Slayer").And.Contain("Lv.100 Founder");
        // The English screen keeps its words.
        var en = await StatueScreen("en", FounderStatueData.StatueLocationTag.MainStreetMini, false);
        en.Should().Contain(": Lv.100 Founder").And.Contain("  === Lv.100 Founder ===").And.Contain("\"The shortest path ever walked.\"");
    }

    // ---------- quests ----------

    [Fact]
    public void StarterQuests_KeepTheirIds_AndCompleteById_InEveryLanguage()
    {
        string[]? englishIds = null;
        foreach (var lang in AllLanguages)
        {
            QuestSystem.ClearAllQuests();
            InLang(lang, () => { QuestSystem.InitializeStarterQuests(); return 0; });
            var ids = QuestSystem.GetAllQuests().Select(q => q.Id).OrderBy(x => x).ToArray();
            ids.Should().Contain("STARTER_WOLF_PACK");
            englishIds ??= ids;
            ids.Should().Equal(englishIds, $"[{lang}] quest ids do not depend on the language");

            var player = new Player { Name1 = LongName, Name2 = LongName, Level = 5 };
            var quest = QuestSystem.GetQuestById("STARTER_WOLF_PACK");
            QuestSystem.ClaimQuest(player, quest).Should().Be(QuestClaimResult.CanClaim);
            InLang(lang, () =>
            {
                for (int i = 0; i < 5; i++) QuestSystem.OnMonsterKilled(player, "Wolf");
                for (int i = 0; i < 2; i++) QuestSystem.OnMonsterKilled(player, "Dire Wolf");
                return 0;
            });
            var s = NewScreen();
            InLang(lang, () => QuestSystem.CompleteQuest(player, "STARTER_WOLF_PACK", s.Term)).Should().Be(QuestCompletionResult.Success, $"[{lang}]");
        }
    }

    [Fact]
    public void BountyBoard_StoresEnglish_AndShowsTheInitiatorInThePlayersLanguage()
    {
        QuestSystem.ClearAllQuests();
        var q = InLang("hu", () => QuestSystem.CreateDungeonQuest(QuestTarget.ReachFloor, 1, null, 10, 5));
        q.Initiator.Should().Be("Bounty Board");
        InLang("hu", () => q.GetDisplayInitiator()).Should().Be(L("hu", "quest.initiator.bounty_board")).And.NotBe("Bounty Board");
        InLang("en", () => q.GetDisplayInitiator()).Should().Be("Bounty Board");
        q.Comment.Should().Be(L("hu", "quest.dungeon_quest_comment", L("hu", "quest.dungeon_name")));
        QuestSystem.GetBountyBoardQuests(new Character { Name2 = LongName, Level = 10 }).Should().Contain(q);
        // A royal floor target is stored English and shown as dungeon.floor.
        InLang("hu", () => CastleLocation.QuestTargetLabel("Floor 12")).Should().Be(L("hu", "dungeon.floor", 12));
    }

    [Fact]
    public void QuestHall_ElectronStatus_IsInThePlayersLanguage()
    {
        var active = new Quest { Occupier = LongName };
        var open = new Quest();
        var dropped = new Quest { IsAbandoned = true };
        InLang("hu", () => QuestHallLocation.StatusLabel(active)).Should().Be(L("hu", "quest_hall.status_active"));
        InLang("hu", () => QuestHallLocation.StatusLabel(open)).Should().Be(InLang("hu", () => open.IsAvailable)
            ? L("hu", "quest_hall.status_available") : L("hu", "quest_hall.status_unknown"));
        InLang("hu", () => QuestHallLocation.StatusLabel(dropped)).Should().Be(L("hu", "quest_hall.status_abandoned"));
        InLang("en", () => QuestHallLocation.StatusLabel(active)).Should().Be("Active");
        foreach (var key in new[] { "quest_hall.status_abandoned", "quest_hall.status_available", "quest_hall.status_unknown",
                     "quest_hall.objective_defeat", "quest_hall.time_limit_days" })
            L("hu", key).Should().NotBe(L("en", key), $"{key} is translated");
        L("en", "quest_hall.objective_defeat", "Wolf", 5).Should().Be("Defeat Wolf x5");
        L("en", "quest_hall.time_limit_days", 7).Should().Be("7 days");
    }

    // ---------- factions ----------

    [Fact]
    public void FactionRanksAndGreetings_AreInThePlayersLanguage_AndFit()
    {
        var factions = new FactionSystem();
        var player = new Character { Name1 = LongName, Name2 = LongName, Level = 20 };
        foreach (var faction in new[] { Faction.TheCrown, Faction.TheShadows, Faction.TheFaith })
        {
            var ranks = FactionSystem.Factions[faction].Ranks;
            for (int i = 0; i < ranks.Length; i++)
            {
                InLang("en", () => FactionSystem.RankLabel(faction, i)).Should().Be(ranks[i]);
                foreach (var lang in AllLanguages)
                {
                    string label = InLang(lang, () => FactionSystem.RankLabel(faction, i));
                    label.Should().NotBeNullOrWhiteSpace();
                    // The character sheet row and the reputation row with the longest name and rank.
                    InLang(lang, () => "    " + Loc.Get("reputation.faction_member", FactionSystem.NameLabel(faction), label, -1000000)).Length
                        .Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] reputation row with {label}");
                }
            }
        }
        InLang("hu", () => FactionSystem.RankLabel(Faction.TheFaith, 8)).Should().Be(L("hu", "faction.rank_faith_8")).And.NotBe("Voice of the Seven");
        InLang("hu", () => factions.GetCurrentRankTitle()).Should().Be(L("hu", "faction.unaffiliated"));

        // Greetings: same faction (with the rank), rival faction (one line per pair), no faction.
        factions.Deserialize(new FactionSaveData { PlayerFaction = (int)Faction.TheCrown, FactionRank = 3, BetrayedFaction = -1 });
        InLang("hu", () => factions.GetFactionGreeting(Faction.TheCrown, player)).Should().Be(L("hu", "faction.greet_same_crown", L("hu", "faction.rank_crown_3")));
        InLang("en", () => factions.GetFactionGreeting(Faction.TheCrown, player)).Should().Be("Hail, Knight. The Crown endures.");
        InLang("hu", () => factions.GetFactionGreeting(Faction.TheShadows, player)).Should().Be(L("hu", "faction.greet_rival_shadows_crown"));
        InLang("en", () => factions.GetFactionGreeting(Faction.TheShadows, player)).Should().Be("One of The Crown? Interesting...");
        InLang("en", () => factions.GetFactionGreeting(Faction.TheFaith, player)).Should().Be("Even those who follow The Crown may find redemption.");
        factions.Deserialize(new FactionSaveData { PlayerFaction = (int)Faction.TheFaith, FactionRank = 0, BetrayedFaction = -1 });
        InLang("en", () => factions.GetFactionGreeting(Faction.TheCrown, player)).Should().Be("The Faith sympathizer. Watch yourself.");
        foreach (var lang in AllLanguages)
            foreach (var key in new[] { "faction.greet_rival_crown_shadows", "faction.greet_rival_crown_faith", "faction.greet_rival_shadows_crown",
                         "faction.greet_rival_shadows_faith", "faction.greet_rival_faith_crown", "faction.greet_rival_faith_shadows",
                         "faction.greet_none_crown", "faction.greet_none_shadows", "faction.greet_none_faith", "faction.greet_same_crown",
                         "faction.greet_same_shadows", "faction.greet_same_faith", "faction.unaffiliated" })
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{key} in {lang}");
                if (lang == "hu") L(lang, key).Should().NotBe(L("en", key));
                L(lang, key).Should().NotContain("The The").And.NotContain("the The");
            }
        // No screen shows the English data name of a faction.
        foreach (var file in new[] { "Scripts/Locations/DarkAlleyLocation.cs", "Scripts/Locations/DormitoryLocation.cs", "Scripts/Locations/InnLocation.cs",
                     "Scripts/Locations/TempleLocation.cs", "Scripts/Locations/CastleLocation.cs", "Scripts/Locations/BaseLocation.cs", "Scripts/Systems/FactionSystem.cs" })
            Rows(Source(file)).Where(l => !l.TrimStart().StartsWith("//")).Any(l => Regex.IsMatch(l, @"Factions\[[^\]]+\]\.Name"))
                .Should().BeFalse($"{file} shows NameLabel, not the English data name");
    }
}
