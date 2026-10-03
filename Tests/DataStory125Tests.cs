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
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the story system tables (the moral paradoxes, the amnesia memories and dreams, the Ocean's ambient
/// wisdom and Wave Fragments, the Jungian archetypes and the story betrayers) are shown in the reader's language.
/// The shown text lives under keys built from the stable ids (paradox and option ids, the enum names, the
/// betrayer ids); ids, story flags, recorded options, moral types, the saved enums and the stored NPC names stay
/// English, so every choice leads to the same outcome and saves the same state in every language. Every row fits
/// 79 columns in all five languages with a 30-character name.
/// </summary>
[Collection("SharedGameSingletons")]
public class DataStory125Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] OtherLanguages = { "es", "fr", "hu", "it" };

    // A name as long as a player name may be (GameConfig.MaxNameLength).
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    // The story singletons the flows write to: each test runs on fresh copies and puts the old ones back.
    private static readonly FieldInfo StoryField = typeof(StoryProgressionSystem).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo OceanField = typeof(OceanPhilosophySystem).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo AmnesiaField = typeof(AmnesiaSystem).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo CompanionField = typeof(CompanionSystem).GetField("_fallbackInstance", SNP)!;
    private static readonly FieldInfo ArchetypeField = typeof(ArchetypeTracker).GetField("_fallbackInstance", SNP)!;
    private readonly object? _oldStory = StoryField.GetValue(null);
    private readonly object? _oldOcean = OceanField.GetValue(null);
    private readonly object? _oldAmnesia = AmnesiaField.GetValue(null);
    private readonly object? _oldCompanion = CompanionField.GetValue(null);
    private readonly object? _oldArchetype = ArchetypeField.GetValue(null);

    public void Dispose()
    {
        StoryField.SetValue(null, _oldStory);
        OceanField.SetValue(null, _oldOcean);
        AmnesiaField.SetValue(null, _oldAmnesia);
        CompanionField.SetValue(null, _oldCompanion);
        ArchetypeField.SetValue(null, _oldArchetype);
    }

    private static void FreshWorld(int awakening = 3)
    {
        StoryField.SetValue(null, new StoryProgressionSystem());
        var ocean = new OceanPhilosophySystem();
        ocean.RestoreFromSave(Array.Empty<WaveFragment>(), Array.Empty<AwakeningMoment>(), null, awakening);
        OceanField.SetValue(null, ocean);
        AmnesiaField.SetValue(null, new AmnesiaSystem());
        CompanionField.SetValue(null, new CompanionSystem());
        ArchetypeField.SetValue(null, new ArchetypeTracker());
    }

    // ---------- helpers ----------

    private static Dictionary<string, string> LoadLang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Localization", lang + ".json")))!;

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

    /// <summary>A terminal that answers with the given lines, then empty lines; a flow that asks more than 40
    /// times (a choice loop that never accepts) stops with an exception instead of hanging the run.</summary>
    private static Screen NewScreen(params string[] lines)
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 60)), i =>
        {
            if (i >= 40) throw new InvalidOperationException("the screen asked for input more than 40 times");
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    private static T InLang<T>(string lang, Func<T> body)
    {
        var prev = GameConfig.Language;
        bool sr = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.Language = lang;
            GameConfig.ScreenReaderMode = false;
            return body();
        }
        finally { GameConfig.Language = prev; GameConfig.ScreenReaderMode = sr; }
    }

    private static List<string> Rows(string text) => text.Replace("\r", "").Split('\n').ToList();

    private static void EveryRowFits(string text, string screen)
    {
        foreach (var row in Rows(text))
            row.Length.Should().BeLessOrEqualTo(MaxWidth, $"every row of the {screen} fits in {MaxWidth} columns: \"{row}\"");
    }

    /// <summary>The Hungarian screen holds none of the English text of these keys: no line of the English value
    /// (5 letters or more) is on screen unless the Hungarian value has it too.</summary>
    private static void NoEnglishLeft(string huText, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            string en = Loc.GetIn("en", key), hu = Loc.GetIn("hu", key);
            hu.Should().NotBe(en, $"{key} has a Hungarian text");
            foreach (var piece in Regex.Split(en, @"\{\d+\}|\n").Select(p => p.Trim()).Where(p => p.Count(char.IsLetter) >= 5))
                if (!hu.Contains(piece))
                    huText.Should().NotContain(piece, $"{key} is shown in Hungarian");
        }
    }

    private static Character Hero() => new()
    {
        Name1 = LongName, Name2 = LongName, Class = CharacterClass.Warrior, Level = 100, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human, Gold = 1000, Chivalry = 300, Darkness = 300, Wisdom = 40, BaseWisdom = 40,
    };

    private static Dictionary<string, MoralParadox> Paradoxes(MoralParadoxSystem s) =>
        (Dictionary<string, MoralParadox>)typeof(MoralParadoxSystem).GetField("paradoxes", F)!.GetValue(s)!;

    private static Dictionary<string, BetrayalProfile> Profiles(BetrayalSystem s) =>
        (Dictionary<string, BetrayalProfile>)typeof(BetrayalSystem).GetField("betrayalProfiles", F)!.GetValue(s)!;

    private static IEnumerable<string> ParadoxScreenKeys(MoralParadox p) =>
        new[] { MoralParadoxSystem.ParadoxKey(p.Id, "name"), MoralParadoxSystem.ParadoxKey(p.Id, "setup"), MoralParadoxSystem.ParadoxKey(p.Id, "reflection") }
            .Concat(p.Choices.SelectMany(o => new[] { MoralParadoxSystem.OptionKey(p.Id, o.Id, "label"), MoralParadoxSystem.OptionKey(p.Id, o.Id, "outcome") }));

    private static IEnumerable<string> BetrayalKeys(BetrayalProfile p) =>
        new[] { "name", "dialogue", "motivations", "redemption" }.Select(part => BetrayalSystem.TextKey(p.NPCId, part))
            .Where(k => Loc.HasIn("en", k));

    private static readonly JungianArchetype[] Archetypes = Enum.GetValues<JungianArchetype>();

    /// <summary>Every key the five tables show, with the six reused ones.</summary>
    private static List<string> AllTableKeys()
    {
        var keys = new List<string>();
        foreach (var p in Paradoxes(new MoralParadoxSystem()).Values) keys.AddRange(ParadoxScreenKeys(p));
        foreach (var m in Enum.GetValues<MemoryFragment>()) { keys.Add(MemoryFragmentData.TitleKey(m)); keys.Add($"amnesia.memory.{m}.lines"); }
        foreach (var d in Enum.GetValues<DreamSequence>()) { keys.Add(DreamData.TitleKey(d)); keys.Add($"amnesia.dream.{d}.lines"); }
        for (int level = 0; level <= OceanPhilosophySystem.MaxStage; level++)
            for (int n = 0; n < OceanPhilosophySystem.WisdomPhrasesPerLevel; n++) keys.Add(OceanPhilosophySystem.WisdomKey(level, n));
        foreach (var id in Archetypes.Select(a => a.ToString()).Append("unknown"))
            foreach (var part in new[] { "name", "title", "description", "quote" }) keys.Add($"archetype.{id}.{part}");
        foreach (var p in Profiles(new BetrayalSystem()).Values.Where(p => p.Keyed)) keys.AddRange(BetrayalKeys(p));
        return keys;
    }

    private static readonly string[] ReusedKeys =
    {
        "dungeon.story_soulweaver_price_title", "dungeon.story_purging_title", "ocean.fragment.TheForgetting.title",
        "ocean.fragment.TheReturn.title", "dream.dream_drowning.title", "dream.dream_mirror.title",
    };

    // ---------- 1. the keys ----------

    [Fact]
    public void LongName_IsTheLongestName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
    }

    [Fact]
    public void EveryTableKey_IsInFiveLanguages_WithRealTranslations_AndNoDashes()
    {
        var keys = AllTableKeys();
        keys.Should().OnlyHaveUniqueItems();
        keys.Should().HaveCount(171, "165 new keys and 6 reused ones");
        keys.Where(k => ReusedKeys.Contains(k)).Should().HaveCount(6);
        var langs = AllLanguages.ToDictionary(l => l, LoadLang);
        foreach (var key in keys)
        {
            var en = langs["en"].GetValueOrDefault(key);
            en.Should().NotBeNullOrWhiteSpace($"{key} is in en.json");
            foreach (var lang in OtherLanguages)
            {
                var text = langs[lang].GetValueOrDefault(key);
                text.Should().NotBeNullOrWhiteSpace($"{key} is in {lang}.json");
                text.Should().NotBe(en, $"{key} is translated in {lang}");
            }
            foreach (var lang in AllLanguages)
                langs[lang][key].Should().NotContain("—").And.NotContain("–", $"{key} in {lang} has no dashes");
        }
    }

    [Fact]
    public void Keys_AreBuiltFromTheIds_NotTheText()
    {
        MoralParadoxSystem.ParadoxKey("possessed_child", "setup").Should().Be("moral.possessed_child.setup");
        MoralParadoxSystem.ParadoxKey("velouras_cure", "name").Should().Be("dungeon.story_soulweaver_price_title");
        MoralParadoxSystem.ParadoxKey("destroy_darkness", "name").Should().Be("dungeon.story_purging_title");
        MoralParadoxSystem.OptionKey("final_choice", "remember_truth", "label").Should().Be("moral.final_choice.remember_truth.label");
        var ps = new MoralParadoxSystem();
        foreach (var p in Paradoxes(ps).Values)
            foreach (var o in p.Choices) o.ParadoxId.Should().Be(p.Id, "each option is stamped with its paradox");
        Paradoxes(ps)["possessed_child"].Choices.Select(o => o.Id).Should().Equal("kill_child", "spare_child", "sacrifice_self");
        MemoryFragmentData.TitleKey(MemoryFragment.TheSeven).Should().Be("amnesia.memory.TheSeven.title");
        DreamData.TitleKey(DreamSequence.TheWar).Should().Be("amnesia.dream.TheWar.title");
        OceanPhilosophySystem.WisdomKey(5, 2).Should().Be("ocean.wisdom.5.2");
        ArchetypeTracker.KeyId(JungianArchetype.Sage).Should().Be("Sage");
        ArchetypeTracker.KeyId((JungianArchetype)99).Should().Be("unknown");
        BetrayalSystem.TextKey("KingsAdvisor", "dialogue").Should().Be("betrayal.KingsAdvisor.dialogue");
    }

    [Fact]
    public void EnglishText_IsTheTextTheTablesHeld()
    {
        string E(string key) => Loc.GetIn("en", key);
        E("moral.possessed_child.name").Should().Be("The Innocent Vessel");
        E("moral.possessed_child.setup").Split('\n').Should().HaveCount(14).And.StartWith("A village has been placed under quarantine.");
        E("moral.final_choice.setup").Split('\n')[3].Should().Be("'Ready to become a god. Ready to 'win.''");
        E("moral.free_terravok.wake_terravok.label").Should().Be("Wake Terravok - end the war at any cost");
        E("moral.final_choice.remember_truth.label").Should().Be("[REQUIRES ALL SEALS] 'I remember who I am.'");
        E("moral.destroy_darkness.reflection").Split('\n').Last().Should().Be("It is the weight that teaches the wave to rise.");
        E("amnesia.memory.TheFullTruth.title").Should().Be("I AM");
        E("amnesia.dream.TheDormitory.lines").Split('\n').Should().HaveCount(7).And.EndWith("'Who am I?'");
        E("ocean.wisdom.1.1").Should().Be("An old saying: 'The river does not push the river.'");
        E("archetype.Ruler.description").Should().Be("You accumulated power, wealth, and influence. Leadership came naturally. " +
            "Whether as king or kingmaker, you understood that true strength lies in control.");
        E("archetype.Hero.quote").Should().Be("\"A hero is someone who has given their life to something bigger than themselves.\" - Joseph Campbell");
        E("archetype.unknown.title").Should().Be("Mysterious One");
        E("betrayal.KingsAdvisor.redemption").Should().Be("Prove your commitment to the realm's wellbeing over personal glory");
        // the six reused keys hold the same English the tables held
        E("dungeon.story_soulweaver_price_title").Should().Be("The Soulweaver's Price");
        E("dungeon.story_purging_title").Should().Be("The Purging Light");
        E("ocean.fragment.TheForgetting.title").Should().Be("The Forgetting");
        E("ocean.fragment.TheReturn.title").Should().Be("The Return");
        E("dream.dream_drowning.title").Should().Be("Drowning in Light");
        E("dream.dream_mirror.title").Should().Be("The Mirror");
    }

    // ---------- 2. the moral paradoxes ----------

    private sealed record ParadoxRun(string Text, string? OptionId, MoralType? Type, string State);

    /// <summary>Presents a paradox in a language on a fresh world and takes the option at menu number
    /// <paramref name="pick"/>; returns the screen and the state the choice left (flags, gods, awakening,
    /// memories, companions, the hero's alignment and wisdom).</summary>
    private static ParadoxRun Present(string lang, string paradoxId, int pick, bool allSeals = false)
    {
        return InLang(lang, () =>
        {
            FreshWorld();
            if (allSeals)
                foreach (var seal in Enum.GetValues<SealType>()) StoryProgressionSystem.Instance.CollectSeal(seal);
            var ps = new MoralParadoxSystem();
            var hero = Hero();
            var s = NewScreen(pick.ToString());
            var choice = ps.PresentParadox(paradoxId, hero, s.Term).GetAwaiter().GetResult();
            var story = SaveSystem.Instance.SerializeStorySystemsPublic();
            var state = JsonSerializer.Serialize(new
            {
                Flags = story.StoryFlags.OrderBy(k => k.Key, StringComparer.Ordinal).ToList(),
                Gods = story.OldGodStates.OrderBy(k => k.Key).ToList(),
                story.AwakeningLevel, story.CollectedFragments, story.ExperiencedMoments, story.OceanInsightIds,
                Fallen = story.FallenCompanions.Select(c => c.CompanionId).OrderBy(x => x).ToList(),
                Amnesia = AmnesiaSystem.Instance.Serialize(),
                hero.Chivalry, hero.Darkness, hero.Wisdom, hero.BaseWisdom,
                Saved = ps.GetChoice(paradoxId)?.OptionId,
                Moral = ps.GetDominantMoralType(),
            });
            return new ParadoxRun(s.Text, choice?.OptionId, choice?.MoralType, state);
        });
    }

    private static IEnumerable<(string Paradox, int Pick, string OptionId, bool AllSeals)> ParadoxPaths()
    {
        foreach (var p in Paradoxes(new MoralParadoxSystem()).Values)
        {
            bool seals = p.Choices.Any(o => o.RequiresAllSeals);
            for (int i = 0; i < p.Choices.Count; i++) yield return (p.Id, i + 1, p.Choices[i].Id, seals);
        }
    }

    [Fact]
    public void EveryParadoxOption_LeadsToTheSameOutcome_AndSavesTheSameState_InEveryLanguage()
    {
        int paths = 0;
        foreach (var (paradox, pick, optionId, seals) in ParadoxPaths())
        {
            var en = Present("en", paradox, pick, seals);
            en.OptionId.Should().Be(optionId, $"menu number {pick} of {paradox} is {optionId}");
            foreach (var lang in OtherLanguages)
            {
                var run = Present(lang, paradox, pick, seals);
                run.OptionId.Should().Be(optionId, $"[{lang}] {paradox} {pick} records the English option id");
                run.Type.Should().Be(en.Type);
                run.State.Should().Be(en.State, $"[{lang}] {paradox}:{optionId} saves the same state as English");
            }
            paths++;
        }
        paths.Should().Be(15);
        // the true ending option is only offered with all seven seals
        var noSeals = Present("en", "final_choice", 1);
        noSeals.Text.Should().NotContain("[REQUIRES ALL SEALS]");
        noSeals.OptionId.Should().Be("claim_power");
    }

    [Fact]
    public void EveryParadoxScreen_IsInHungarian_AndEveryRowFits_InFiveLanguages()
    {
        var all = Paradoxes(new MoralParadoxSystem());
        foreach (var (paradox, pick, optionId, seals) in ParadoxPaths())
        {
            var p = all[paradox];
            foreach (var lang in AllLanguages)
            {
                var run = Present(lang, paradox, pick, seals);
                EveryRowFits(run.Text, $"[{lang}] {paradox}:{optionId}");
                foreach (var key in ParadoxScreenKeys(p).Where(k => !k.EndsWith(".outcome") || k.Contains("." + optionId + ".")))
                    foreach (var line in Loc.GetIn(lang, key).Split('\n').Where(l => l.Length > 0))
                        run.Text.Should().Contain(line, $"[{lang}] {key} is on the {paradox} screen");
                if (lang == "hu")
                    NoEnglishLeft(run.Text, ParadoxScreenKeys(p).Where(k => !k.EndsWith(".outcome") || k.Contains("." + optionId + ".")));
            }
        }
    }

    [Fact]
    public void EveryParadoxRow_FitsUnwrapped_InFiveLanguages()
    {
        foreach (var p in Paradoxes(new MoralParadoxSystem()).Values)
            foreach (var lang in AllLanguages)
            {
                foreach (var key in ParadoxScreenKeys(p))
                {
                    int prefix = key.EndsWith(".label") ? "  [9] ".Length : 2;
                    foreach (var line in Loc.GetIn(lang, key).Split('\n'))
                        (prefix + line.Length).Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {key}: \"{line}\"");
                }
            }
    }

    // ---------- 3. amnesia, ocean ----------

    [Fact]
    public void MemoriesAndDreams_AreShownInTheReadersLanguage_AndFit()
    {
        foreach (var lang in AllLanguages)
            InLang(lang, () =>
            {
                foreach (var (fragment, data) in AmnesiaSystem.MemoryData)
                {
                    data.Fragment.Should().Be(fragment);
                    data.Title.Should().Be(Loc.GetIn(lang, MemoryFragmentData.TitleKey(fragment)));
                    data.Lines.Should().Equal(Loc.GetIn(lang, $"amnesia.memory.{fragment}.lines").Split('\n'));
                    // DungeonLocation: "A memory surfaces: ..." then each line under two spaces
                    Loc.Get("dungeon.memory_surfaces", data.Title).Length.Should().BeLessOrEqualTo(MaxWidth);
                    foreach (var line in data.Lines) ("  " + line).Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {fragment}: {line}");
                }
                foreach (var (dream, data) in AmnesiaSystem.DreamSequences)
                {
                    data.Dream.Should().Be(dream);
                    data.Title.Should().Be(Loc.GetIn(lang, DreamData.TitleKey(dream)));
                    ("    " + data.Title).Length.Should().BeLessOrEqualTo(MaxWidth);
                    foreach (var line in data.Lines) ("  " + line).Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {dream}: {line}");
                }
                return 0;
            });
        // level and trigger tables are unchanged
        AmnesiaSystem.MemoryData[MemoryFragment.TheDecision].RequiredLevel.Should().Be(35);
        AmnesiaSystem.MemoryData[MemoryFragment.TheDecision].Trigger.Should().Be(TriggerType.DungeonFloor75);
        AmnesiaSystem.DreamSequences[DreamSequence.TheOcean].MinLevel.Should().Be(76);
        AmnesiaSystem.DreamSequences[DreamSequence.TheOcean].MaxLevel.Should().Be(100);
    }

    [Fact]
    public void TheDreamScreen_IsInHungarian()
    {
        foreach (var lang in AllLanguages)
        {
            var text = InLang(lang, () =>
            {
                FreshWorld();
                var amnesia = new AmnesiaSystem();
                var s = NewScreen();
                var hero = Hero(); // level 100: only The Ocean is in range
                ((Task)typeof(AmnesiaSystem).GetMethod("PlayDreamSequence", F)!.Invoke(amnesia, new object[] { s.Term, hero })!).GetAwaiter().GetResult();
                amnesia.ExperiencedDreams.Should().Equal(DreamSequence.TheOcean);
                return s.Text;
            });
            EveryRowFits(text, $"[{lang}] dream");
            text.Should().Contain("    " + Loc.GetIn(lang, "amnesia.dream.TheOcean.title"));
            foreach (var line in Loc.GetIn(lang, "amnesia.dream.TheOcean.lines").Split('\n')) text.Should().Contain("  " + line);
            if (lang == "hu") NoEnglishLeft(text, new[] { "amnesia.dream.TheOcean.title", "amnesia.dream.TheOcean.lines" });
        }
    }

    [Fact]
    public void AmbientWisdomAndWaveFragments_AreInTheReadersLanguage_AndFit()
    {
        for (int level = 0; level <= OceanPhilosophySystem.MaxStage; level++)
        {
            int lv = level;
            foreach (var lang in AllLanguages)
            {
                var options = Enumerable.Range(0, 3).Select(n => Loc.GetIn(lang, OceanPhilosophySystem.WisdomKey(lv, n))).ToList();
                foreach (var phrase in options)
                    Loc.GetIn(lang, "magic_shop.talk_wisdom", phrase).Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {phrase}");
                InLang(lang, () =>
                {
                    var ocean = new OceanPhilosophySystem();
                    ocean.RestoreFromSave(Array.Empty<WaveFragment>(), Array.Empty<AwakeningMoment>(), null, lv);
                    ocean.AwakeningLevel.Should().Be(lv);
                    for (int i = 0; i < 12; i++)
                    {
                        options.Should().Contain(ocean.GetAmbientWisdom(), $"[{lang}] level {lv}");
                        options.Should().Contain(ocean.GetNPCWisdom(), $"[{lang}] level {lv}");
                    }
                    return 0;
                });
            }
        }
        foreach (var lang in AllLanguages)
            InLang(lang, () =>
            {
                foreach (var (fragment, data) in OceanPhilosophySystem.FragmentData)
                {
                    data.Fragment.Should().Be(fragment);
                    data.Title.Should().Be(Loc.GetIn(lang, $"ocean.fragment.{fragment}.title"));
                    data.Text.Should().Be(Loc.GetIn(lang, $"ocean.fragment.{fragment}.text"));
                    ("    - " + data.Title).Length.Should().BeLessOrEqualTo(MaxWidth);
                    ("  \"" + data.Title + "\"").Length.Should().BeLessOrEqualTo(MaxWidth);
                }
                return 0;
            });
        OceanPhilosophySystem.FragmentData[WaveFragment.TheTruth].RequiredAwakening.Should().Be(7);
        OceanPhilosophySystem.FragmentData.Should().HaveCount(10);
    }

    // ---------- 4. archetypes ----------

    private static string ArchetypeScreen(string lang, JungianArchetype dominant, JungianArchetype secondary)
    {
        return InLang(lang, () =>
        {
            FreshWorld();
            var tracker = new ArchetypeTracker();
            tracker.AddScore(dominant, 100);
            tracker.AddScore(secondary, 50);
            tracker.RecordMonsterKill(10);
            ArchetypeField.SetValue(null, tracker);
            var s = NewScreen();
            ((Task)typeof(EndingsSystem).GetMethod("ShowArchetypeReveal", F)!.Invoke(new EndingsSystem(), new object[] { Hero(), s.Term })!).GetAwaiter().GetResult();
            return s.Text;
        });
    }

    [Fact]
    public void TheArchetypeReveal_IsInHungarian_AndFits_ForEveryArchetype()
    {
        foreach (var a in Archetypes)
        {
            var second = a == JungianArchetype.Everyman ? JungianArchetype.Hero : a + 1;
            foreach (var lang in AllLanguages)
            {
                var text = ArchetypeScreen(lang, a, second);
                EveryRowFits(text, $"[{lang}] archetype {a}");
                text.Should().Contain(Loc.GetIn(lang, $"archetype.{a}.title"));
                text.Should().Contain(Loc.GetIn(lang, $"archetype.{second}.name"));
                if (lang == "hu")
                    NoEnglishLeft(text, new[] { $"archetype.{a}.name", $"archetype.{a}.title", $"archetype.{a}.quote", $"archetype.{second}.name", $"archetype.{second}.title" });
            }
        }
        var (name, title, description, color) = InLang("hu", () => ArchetypeTracker.GetArchetypeInfo((JungianArchetype)99));
        (name, title, description, color).Should().Be((Loc.GetIn("hu", "archetype.unknown.name"), Loc.GetIn("hu", "archetype.unknown.title"),
            Loc.GetIn("hu", "archetype.unknown.description"), "white"));
        ArchetypeTracker.GetArchetypeInfo(JungianArchetype.Sage).color.Should().Be("bright_blue");
        ArchetypeTracker.GetArchetypeInfo(JungianArchetype.Everyman).color.Should().Be("gray");
    }

    [Fact]
    public void TheArchetypeScores_AreSavedTheSame_InEveryLanguage()
    {
        string Saved(string lang) => InLang(lang, () =>
        {
            var tracker = new ArchetypeTracker();
            tracker.AddScore(JungianArchetype.Sage, 40);
            tracker.RecordMarriage();
            var json = JsonSerializer.Serialize(tracker.Serialize());
            var back = new ArchetypeTracker();
            back.Deserialize(JsonSerializer.Deserialize<ArchetypeTrackerData>(json)!);
            back.GetDominantArchetype().Should().Be(JungianArchetype.Sage);
            return json;
        });
        var en = Saved("en");
        foreach (var lang in OtherLanguages) Saved(lang).Should().Be(en, $"[{lang}] the scores are saved by number");
        en.Should().Contain("\"4\":40", "the Sage is stored as its enum number");
    }

    // ---------- 5. betrayals ----------

    [Fact]
    public void TheBetrayalScenes_AreInHungarian_AndFit()
    {
        var keyed = Profiles(new BetrayalSystem()).Values.Where(p => p.Keyed).Select(p => p.NPCId).ToList();
        keyed.Should().Equal("TheStranger", "TeamBetrayal", "RomanticBetrayal", "KingsAdvisor", "Lyris");
        foreach (var id in keyed)
            foreach (var lang in AllLanguages)
            {
                var text = InLang(lang, () =>
                {
                    FreshWorld();
                    var bs = new BetrayalSystem();
                    var s = NewScreen();
                    ((Task)typeof(BetrayalSystem).GetMethod("DisplayBetrayalScene", F)!
                        .Invoke(bs, new object[] { Profiles(bs)[id], Hero(), s.Term })!).GetAwaiter().GetResult();
                    return s.Text;
                });
                EveryRowFits(text, $"[{lang}] betrayal {id}");
                foreach (var line in Loc.GetIn(lang, BetrayalSystem.TextKey(id, "dialogue")).Split('\n')) text.Should().Contain($"  \"{line}\"");
                foreach (var line in Loc.GetIn(lang, BetrayalSystem.TextKey(id, "motivations")).Split('\n')) text.Should().Contain($"  - {line}");
                if (lang == "hu")
                    NoEnglishLeft(text, new[] { "dialogue", "motivations", "name" }.Select(part => BetrayalSystem.TextKey(id, part)).Where(k => Loc.HasIn("en", k)));
            }
        // Lyris is a name and has no name key; the others show a translated description
        Loc.HasIn("en", BetrayalSystem.TextKey("Lyris", "name")).Should().BeFalse();
        Loc.HasIn("en", BetrayalSystem.TextKey("RomanticBetrayal", "redemption")).Should().BeFalse("the beloved cannot be forgiven");
        var generic = new BetrayalProfile { NPCId = "some_npc", NPCName = "some_npc", Motivations = new List<string> { "m" } };
        BetrayalSystem.DisplayName(generic).Should().Be("some_npc");
        BetrayalSystem.MotivationRows(generic).Should().Equal("m");
        BetrayalSystem.DialogueRows(generic).Should().BeNull();
        BetrayalSystem.RedemptionText(generic).Should().BeNull();
    }

    [Fact]
    public void ABetrayal_SetsTheSameFlags_AndKeepsTheStoredNames_InEveryLanguage()
    {
        (string Flags, string Name, List<string> Motivations) Run(string lang, string id) => InLang(lang, () =>
        {
            FreshWorld();
            var bs = new BetrayalSystem();
            Profiles(bs)[id].IsPendingBetrayal = true;
            var result = bs.ExecuteBetrayal(id, Hero(), NewScreen().Term).GetAwaiter().GetResult();
            result.Occurred.Should().BeTrue();
            var story = SaveSystem.Instance.SerializeStorySystemsPublic();
            var flags = JsonSerializer.Serialize(story.StoryFlags.OrderBy(k => k.Key, StringComparer.Ordinal).ToList());
            bs.GetActiveBetrays().Single().NPCName.Should().Be(Profiles(bs)[id].NPCName, "the stored name is the English one");
            return (flags, result.NPCName, result.Motivations);
        });
        foreach (var id in new[] { "TheStranger", "KingsAdvisor", "Lyris" })
        {
            var en = Run("en", id);
            en.Flags.Should().Contain($"betrayed_by_{id}");
            foreach (var lang in OtherLanguages)
            {
                var r = Run(lang, id);
                r.Flags.Should().Be(en.Flags, $"[{lang}] {id} sets the same story flags");
                r.Name.Should().Be(en.Name, $"[{lang}] the result carries the stored English name");
                r.Motivations.Should().Equal(Loc.GetIn(lang, BetrayalSystem.TextKey(id, "motivations")).Split('\n'));
            }
        }
    }

    // ---------- 6. saved state ----------

    [Fact]
    public void ASaveWrittenBefore125_LoadsAndSavesTheSame_AndShowsInTheReadersLanguage()
    {
        // As 1.2.4 wrote them: enums as numbers, flags and ids in English.
        const string oldAmnesia = "{\"RecoveredMemories\":[0,5],\"ExperiencedDreams\":[1,5],\"RestCount\":4,\"TruthRevealed\":false}";
        const string oldArchetype = "{\"Scores\":{\"0\":12,\"4\":30,\"9\":3},\"BossesDefeated\":2,\"MonstersKilled\":41}";
        foreach (var lang in AllLanguages)
            InLang(lang, () =>
            {
                FreshWorld();
                var amnesia = new AmnesiaSystem();
                amnesia.Deserialize(JsonSerializer.Deserialize<AmnesiaData>(oldAmnesia)!);
                JsonSerializer.Serialize(amnesia.Serialize()).Should().Be(oldAmnesia, $"[{lang}] the amnesia state saves as it was read");
                amnesia.RecoveredMemories.Should().BeEquivalentTo(new[] { MemoryFragment.Emptiness, MemoryFragment.TheDecision });
                AmnesiaSystem.MemoryData[MemoryFragment.TheDecision].Title.Should().Be(Loc.GetIn(lang, "amnesia.memory.TheDecision.title"));
                AmnesiaSystem.DreamSequences[DreamSequence.TheMirror].Title.Should().Be(Loc.GetIn(lang, "dream.dream_mirror.title"));

                var tracker = new ArchetypeTracker();
                tracker.Deserialize(JsonSerializer.Deserialize<ArchetypeTrackerData>(oldArchetype)!);
                tracker.GetDominantArchetype().Should().Be(JungianArchetype.Sage);
                ArchetypeTracker.GetArchetypeInfo(tracker.GetDominantArchetype()).name.Should().Be(Loc.GetIn(lang, "archetype.Sage.name"));
                var again = JsonSerializer.Deserialize<ArchetypeTrackerData>(JsonSerializer.Serialize(tracker.Serialize()))!;
                again.Scores[4].Should().Be(30);
                again.MonstersKilled.Should().Be(41);

                // a paradox flag from an old save keeps the paradox closed in any language (by its English flag name)
                var story = StoryProgressionSystem.Instance;
                story.SetStoryFlag("killed_possessed_child", true);
                story.HasStoryFlag("killed_possessed_child").Should().BeTrue();
                return 0;
            });
    }

    [Fact]
    public void TheStoryFlagsAndIds_StayEnglish_InTheSource()
    {
        var root = HardcodedTextScannerTests.RepoRoot();
        string Src(string f) => File.ReadAllText(Path.Combine(root, "Scripts", "Systems", f));
        var moral = Src("MoralParadoxSystem.cs");
        foreach (var flag in new[] { "killed_possessed_child", "village_consumed", "carries_demon", "lyris_sacrificed_for_veloura",
                     "refused_lyris_sacrifice", "soul_rejected_by_loom", "woke_terravok_early", "let_terravok_sleep", "spoke_to_terravok",
                     "created_paradise", "refused_paradise", "absorbed_world_darkness", "claimed_divine_power", "refused_divine_power", "remembered_truth" })
            moral.Should().Contain($"StoryFlag = \"{flag}\"");
        moral.Should().Contain("CompanionDeath = \"Lyris\"", "CompanionSystem finds the companion by its English name");
        moral.Should().Contain("OptionId = choice.Id,").And.Contain("MomentForChoice(choice.Id)").And.Contain("\"paradox:\" + choice.Id");
        Src("BetrayalSystem.cs").Should().Contain("SetStoryFlag($\"betrayed_by_{npcId}\", true)");
        Src("AmnesiaSystem.cs").Should().Contain("SetStoryFlag($\"memory_{memoryKey}\", true)");
    }

    [Fact]
    public void TheWikiExporter_DoesNotReadTheseTables()
    {
        var src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "WikiDataExporter.cs"));
        foreach (var name in new[] { "MoralParadox", "Amnesia", "MemoryFragment", "DreamSequence", "OceanPhilosophy", "WaveFragment",
                     "Archetype", "Betrayal" })
            src.Should().NotContain(name, "the English wiki export does not include the story tables");
    }
}
