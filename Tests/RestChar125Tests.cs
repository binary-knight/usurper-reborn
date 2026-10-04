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
using UsurperRemake.Data;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the remaining output text of the VN dialogue files (companion deaths, dialogue effects, the
/// conversation screen, romance, family and marriage news), the inventory and character files (Player,
/// achievements, founder statues, factions, Level Master, character creation, loot, training, alignment
/// news) in the player's language, news in the writer's language. What is stored or matched stays English:
/// the companion's saved last words, achievement ids, the founder records, the wedding place word, the
/// merc contract result codes, the slash commands of the dungeon help.
/// Every changed row fits 79 columns in all five languages with a 30-character name.
/// </summary>
[Collection("SharedGameSingletons")]
public class RestChar125Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const int MaxWidth = 79;

    // GameConfig.MaxNameLength (30) characters, the longest name a player can have.
    private const string LongName = "Aranyszivu Hosszunevu Kalandor";

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] OtherLanguages = { "es", "fr", "hu", "it" };

    // The dialogue effects and the deaths touch these singletons; the tests run on fresh ones and the tests
    // that follow in the collection get the ones they had.
    private static readonly (Type Type, string Field)[] Singletons =
    {
        (typeof(OceanPhilosophySystem), "_fallbackInstance"),
        (typeof(AmnesiaSystem), "_fallbackInstance"),
        (typeof(StoryProgressionSystem), "_fallbackInstance"),
        (typeof(CompanionSystem), "_fallbackInstance"),
        (typeof(AlignmentSystem), "_fallbackInstance"),
        (typeof(FactionSystem), "_fallbackInstance"),
    };
    private readonly Dictionary<FieldInfo, object?> _saved = new();

    public RestChar125Tests()
    {
        foreach (var (type, name) in Singletons)
        {
            var field = type.GetField(name, SNP)!;
            _saved[field] = field.GetValue(null);
            field.SetValue(null, null);
        }
        NewsSystem.Instance.ClearCatchUpBuffer();
    }

    public void Dispose()
    {
        NewsSystem.Instance.ClearCatchUpBuffer();
        foreach (var (field, value) in _saved) field.SetValue(null, value);
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

    /// <summary>
    /// The Hungarian screen holds none of the English text of these keys: the Hungarian value differs, and no
    /// piece of the English value (split at its placeholders, 5 letters or more) is on screen unless the
    /// Hungarian value has it too.
    /// </summary>
    private static void NoEnglishLeft(string huText, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            string en = Loc.GetIn("en", key), hu = Loc.GetIn("hu", key);
            hu.Should().NotBe(en, $"{key} has a Hungarian text");
            foreach (var piece in Regex.Split(en, @"\{\d+(?:,-?\d+)?\}|\n").Select(p => p.Trim()).Where(p => p.Count(char.IsLetter) >= 5))
                if (!hu.Contains(piece))
                    huText.Should().NotContain(piece, $"{key} is shown in Hungarian");
        }
    }

    /// <summary>Every key has a value in all five languages that is not the key itself.</summary>
    private static void InAllLanguages(IEnumerable<string> keys)
    {
        foreach (var key in keys)
            foreach (var lang in AllLanguages)
                L(lang, key).Should().NotBe(key, $"{key} has a {lang} text").And.NotBeNullOrWhiteSpace();
    }

    /// <summary>The catch-up bucket of a news text written in each language is the bucket of the English one.</summary>
    private static void SameCatchUpBucket(string key, params object[] args)
    {
        string en = L("en", key, args);
        int expected = GameEngine.CatchUpBucket(en);
        foreach (var lang in OtherLanguages)
            GameEngine.CatchUpBucket(L(lang, key, args)).Should().Be(expected,
                $"{key} written in {lang} (\"{L(lang, key, args)}\") sorts into the catch-up heading of the English \"{en}\"");
    }

    /// <summary>A news text written in each language is gossip for a reader of that language exactly when the English one is.</summary>
    private static void SameGossip(string key, params object[] args)
    {
        bool expected = InLang("en", () => NewsSystem.GossipKeywordsForReader()).Any(k => L("en", key, args).Contains(k, StringComparison.OrdinalIgnoreCase));
        foreach (var lang in OtherLanguages)
        {
            string text = L(lang, key, args);
            InLang(lang, () => NewsSystem.GossipKeywordsForReader()).Any(k => text.Contains(k, StringComparison.OrdinalIgnoreCase))
                .Should().Be(expected, $"{key} written in {lang} (\"{text}\") is gossip for a {lang} reader as the English is ({expected})");
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

    private static string Source(string rel)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, rel));
    }

    // ---------- companion deaths ----------

    private static readonly (CompanionId Id, DeathType Type)[] Deaths =
    {
        (CompanionId.Lyris, DeathType.Sacrifice), (CompanionId.Lyris, DeathType.ChoiceBased),
        (CompanionId.Aldric, DeathType.MoralTrigger), (CompanionId.Aldric, DeathType.Sacrifice),
        (CompanionId.Mira, DeathType.Sacrifice), (CompanionId.Mira, DeathType.QuestRelated),
        (CompanionId.Vex, DeathType.Inevitable), (CompanionId.Vex, DeathType.Sacrifice),
        (CompanionId.Melodia, DeathType.Combat),
    };

    private static IEnumerable<string> PhilosophyKeys =>
        new[] { ("lyris", 6), ("aldric", 6), ("mira", 6), ("vex", 7) }
            .SelectMany(c => Enumerable.Range(1, c.Item2).Select(i => $"companion.philosophy_{c.Item1}_{i}"));

    private static IEnumerable<string> LastWordsKeys => new[]
    {
        "companion.last_words_lyris_sacrifice", "companion.last_words_lyris_choice", "companion.last_words_aldric_moral",
        "companion.last_words_aldric_sacrifice", "companion.last_words_mira_sacrifice", "companion.last_words_mira_quest",
        "companion.last_words_vex_inevitable", "companion.last_words_vex_sacrifice", "companion.last_words_other",
    };

    private static IEnumerable<string> MemoryKeys => new[]
    {
        "companion.memory_lyris", "companion.memory_aldric", "companion.memory_mira", "companion.memory_vex", "companion.memory_other",
    };

    private static async Task<string> DeathScreen(string lang, CompanionId id, DeathType type, bool screenReader)
    {
        var s = NewScreen();
        var sys = new CompanionSystem();
        var companion = sys.GetCompanion(id)!;
        var m = typeof(CompanionSystem).GetMethod("DisplayDeathScene", F)!;
        await InLanguage(lang, async () =>
        {
            await (Task)m.Invoke(sys, new object[] { companion, type, "", s.Term })!;
            return 0;
        }, screenReader);
        return s.Text;
    }

    [Fact]
    public void LongName_IsTheLongestName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
    }

    [Fact]
    public async Task CompanionDeath_InHungarian_HasNoEnglishLeft()
    {
        foreach (var (id, type) in Deaths)
            foreach (var sr in new[] { false, true })
            {
                var text = await DeathScreen("hu", id, type, sr);
                Capture($"companion-death-{id}-{type}-hu{(sr ? "-sr" : "")}.txt", text);
                NoEnglishLeft(text, PhilosophyKeys.Concat(LastWordsKeys).Concat(MemoryKeys));
                text.Should().Contain(L("hu", CompanionSystem.LastWordsKey(id, type)));
                if (!sr)
                    text.Should().Contain("E   L   E   S   E   T   T").And.NotContain("F   A   L   L   E   N");
            }
        var lyris = await DeathScreen("hu", CompanionId.Lyris, DeathType.Sacrifice, false);
        lyris.Should().Contain(L("hu", "companion.philosophy_lyris_1")).And.Contain(L("hu", "companion.memory_lyris"));
    }

    [Fact]
    public async Task CompanionDeath_EveryRowFits_InEveryLanguage()
    {
        foreach (var lang in AllLanguages)
            foreach (var (id, type) in Deaths)
                foreach (var sr in new[] { false, true })
                    EveryRowFits(await DeathScreen(lang, id, type, sr), $"[{lang}{(sr ? " sr" : "")}] {id} death");
        foreach (var key in PhilosophyKeys.Concat(LastWordsKeys).Concat(MemoryKeys))
            foreach (var lang in AllLanguages)
                ("  \"" + L(lang, key) + "\"").Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {key} fits");
    }

    [Fact]
    public async Task CompanionDeath_InEnglish_KeepsItsRows_AndTheFrameIsWhole()
    {
        var rows = Rows(await DeathScreen("en", CompanionId.Vex, DeathType.Inevitable, false));
        rows.Should().Contain("  He was dying the whole time you knew him.");
        rows.Should().Contain("  You keep expecting to hear a bad joke around the next corner.");
        rows.Should().Contain("  You can still hear him laughing. Somehow.");
        rows.Should().Contain("  \"Heh... beat the schedule... by a few hours... not bad...\"");
        rows.Should().Contain("║                      F   A   L   L   E   N                       ║");
        foreach (var row in rows.Where(r => r.StartsWith("║") || r.StartsWith("╔") || r.StartsWith("╚")))
            row.Length.Should().Be(68, $"the FALLEN frame is 68 columns: \"{row}\"");
        var aldric = Rows(await DeathScreen("en", CompanionId.Aldric, DeathType.Sacrifice, false));
        aldric.Should().Contain("  One fight where he didnt have to watch someone else die.");
        aldric.Should().Contain("  He died doing what he always did. Standing in front of someone.");
    }

    [Fact]
    public void CompanionLastWords_AreSavedInEnglish_InEveryLanguage()
    {
        var sys = new CompanionSystem();
        var m = typeof(CompanionSystem).GetMethod("GetLastWords", F)!;
        foreach (var lang in AllLanguages)
            InLang(lang, () => (string)m.Invoke(sys, new object[] { sys.GetCompanion(CompanionId.Lyris)!, DeathType.Sacrifice })!)
                .Should().Be("I knew... when I met you... I knew this is how it ends...", $"[{lang}] the save keeps the English last words");
        InLang("hu", () => (string)m.Invoke(sys, new object[] { sys.GetCompanion(CompanionId.Vex)!, DeathType.Combat })!)
            .Should().Be("Hey... not bad... for a... last day...");
    }

    // ---------- dialogue effects ----------

    private static readonly (EffectType Type, int Int, string? Str, string Key)[] Effects =
    {
        (EffectType.AddChivalry, 5, null, "dialogue.effect_chivalry"),
        (EffectType.AddDarkness, 5, null, "dialogue.effect_darkness"),
        (EffectType.AddGold, 40, null, "dialogue.effect_gold_received"),
        (EffectType.AddGold, -10, null, "dialogue.effect_gold_lost"),
        (EffectType.AddExperience, 100, null, "dialogue.effect_experience"),
        (EffectType.Heal, 20, null, "dialogue.effect_healed"),
        (EffectType.Damage, 7, null, "dialogue.effect_damage"),
        (EffectType.GiveItem, 0, "Shadow Cloak", "dialogue.effect_item"),
        (EffectType.ModifyCompanionLoyalty, 5, "Aldric", "dialogue.effect_loyalty_up"),
        (EffectType.ModifyCompanionLoyalty, -5, "Aldric", "dialogue.effect_loyalty_down"),
        (EffectType.ModifyCompanionTrust, 5, "Aldric", "dialogue.effect_trust_up"),
        (EffectType.ModifyCompanionTrust, -5, "Aldric", "dialogue.effect_trust_down"),
        (EffectType.AdvanceRomance, 0, "Lyris", "dialogue.effect_romance"),
        (EffectType.GainOceanInsight, 0, null, "dialogue.effect_insight"),
        (EffectType.CollectWaveFragment, 0, "FirstSeparation", "dialogue.effect_wave_fragment"),
        (EffectType.TriggerAwakeningMoment, 0, "SparedAnEnemy", "dialogue.effect_awakening"),
        (EffectType.RevealMemory, 0, "the_first_death", "dialogue.effect_memory"),
    };

    private static string EffectScreen(string lang, (EffectType Type, int Int, string? Str, string Key) e)
    {
        var s = NewScreen();
        var sys = (DialogueSystem)Activator.CreateInstance(typeof(DialogueSystem), nonPublic: true)!;
        var player = new Character { Name1 = LongName, Name2 = LongName, Level = 10, HP = 50, MaxHP = 100, Gold = 1000 };
        typeof(DialogueSystem).GetField("terminal", F)!.SetValue(sys, s.Term);
        typeof(DialogueSystem).GetField("currentPlayer", F)!.SetValue(sys, player);
        var effect = new DialogueEffect { Type = e.Type, IntValue = e.Int, StringValue = e.Str };
        var m = typeof(DialogueSystem).GetMethod("ApplyEffect", F)!;
        InLang(lang, () => m.Invoke(sys, new object[] { effect, "test_node" }));
        return s.Text;
    }

    [Fact]
    public void DialogueEffects_InHungarian_HaveNoEnglishLeft_AndFit()
    {
        foreach (var e in Effects)
        {
            var hu = EffectScreen("hu", e);
            // v1.2.5: a dialogue reward shows its item name in the reader's language (DataDialogue125Tests)
            object arg = e.Type == EffectType.GiveItem ? L("hu", "item.shadow_cloak") : e.Str != null ? e.Str : Math.Abs(e.Int);
            hu.Should().Contain(L("hu", e.Key, arg), $"{e.Key} is shown");
            NoEnglishLeft(hu, new[] { e.Key });
            foreach (var lang in AllLanguages)
                EveryRowFits(EffectScreen(lang, e), $"[{lang}] {e.Key}");
        }
        InAllLanguages(Effects.Select(e => e.Key));
    }

    [Fact]
    public void DialogueEffects_InEnglish_KeepTheirWords()
    {
        EffectScreen("en", Effects[0]).Should().Contain("(+5 Chivalry)");
        EffectScreen("en", Effects[3]).Should().Contain("(Lost 10 gold)");
        EffectScreen("en", Effects[2]).Should().Contain("(Received 40 gold)");
        EffectScreen("en", Effects[7]).Should().Contain("(Received: Shadow Cloak)");
        EffectScreen("en", Effects[8]).Should().Contain("(Aldric's loyalty increased)");
        EffectScreen("en", Effects[12]).Should().Contain("(Your relationship with Lyris deepens)");
        EffectScreen("en", Effects[16]).Should().Contain("(A memory surfaces from the depths...)");
    }

    // ---------- conversation screen ----------

    private static NPC TalkNpc(CharacterSex sex, CharacterRace race, bool traits)
    {
        var npc = new NPC { Name1 = LongName, Name2 = LongName, Level = 12, Race = race, Class = CharacterClass.Warrior, Sex = sex };
        if (traits)
        {
            var profile = PersonalityProfile.GenerateForArchetype("commoner");
            profile.Sensuality = 0.9f; profile.Passion = 0.9f; profile.Aggression = 0.9f; profile.Sociability = 0.9f; profile.Intelligence = 0.9f;
            npc.Personality = profile;
            npc.Brain = new NPCBrain(npc, profile);
        }
        return npc;
    }

    private static async Task<string> HeaderScreen(string lang, NPC npc, RomanceRelationType romance, bool screenReader)
    {
        var s = NewScreen();
        var vn = (VisualNovelDialogueSystem)Activator.CreateInstance(typeof(VisualNovelDialogueSystem), nonPublic: true)!;
        typeof(VisualNovelDialogueSystem).GetField("terminal", F)!.SetValue(vn, s.Term);
        var m = typeof(VisualNovelDialogueSystem).GetMethod("ShowConversationHeader", F)!;
        await InLanguage(lang, async () => { await (Task)m.Invoke(vn, new object[] { npc, 50, romance })!; return 0; }, screenReader);
        return s.Text;
    }

    private static IEnumerable<string> PhysicalKeys =>
        new[] { "alluring", "intense", "fierce", "approachable", "sharp", "unremarkable" }
            .SelectMany(a => new[] { $"dialogue.phys_adj_{a}_she", $"dialogue.phys_adj_{a}_he" })
            .Concat(new[] { "elf", "dwarf", "orc", "hobbit", "troll", "other" }.Select(r => $"dialogue.phys_race_{r}"))
            .Concat(new[] { "dialogue.phys_appears_she", "dialogue.phys_appears_he", "dialogue.npc_profile_line" });

    [Fact]
    public async Task ConversationHeader_InHungarian_HasNoEnglishLeft()
    {
        foreach (var sr in new[] { false, true })
        {
            var text = await HeaderScreen("hu", TalkNpc(CharacterSex.Female, CharacterRace.Elf, true), RomanceRelationType.Spouse, sr);
            Capture($"conversation-header-hu{(sr ? "-sr" : "")}.txt", text);
            text.Should().Contain(L("hu", "dialogue.npc_profile_line", 12, L("hu", "race.elf"), L("hu", "class.warrior")));
            text.Should().Contain($"[{L("hu", "love_street.tag_spouse")}]").And.NotContain("[Spouse]");
            text.Should().Contain(L("hu", "dialogue.phys_race_elf"));
            NoEnglishLeft(text, new[] { "dialogue.phys_race_elf", "dialogue.phys_adj_alluring_she", "dialogue.phys_adj_sharp_she", "dialogue.npc_profile_line" });
            text.Should().NotContain("appears").And.NotContain("Level 12");
        }
        var plain = await HeaderScreen("hu", TalkNpc(CharacterSex.Male, CharacterRace.Troll, false), RomanceRelationType.Ex, true);
        plain.Should().Contain(L("hu", "dialogue.phys_adj_unremarkable_he")).And.Contain(L("hu", "dialogue.phys_race_troll"))
            .And.Contain($"[{L("hu", "dialogue.romance_tag_ex")}]");
        InAllLanguages(PhysicalKeys.Append("dialogue.romance_tag_ex"));
    }

    [Fact]
    public async Task ConversationHeader_InEnglish_KeepsItsWords()
    {
        var text = await HeaderScreen("en", TalkNpc(CharacterSex.Female, CharacterRace.Elf, true), RomanceRelationType.Lover, true);
        text.Should().Contain("  Level 12 Elf Warrior");
        // The description wraps at 79 columns: compare its words.
        string.Join(" ", Rows(text).Select(r => r.Trim()).Where(r => r.Length > 0))
            .Should().Contain("She appears alluring, intense-eyed, fierce-looking, approachable, sharp-witted with graceful elven features.");
        text.Should().Contain($"{LongName} [Lover]");
        var plain = await HeaderScreen("en", TalkNpc(CharacterSex.Male, CharacterRace.Human, false), RomanceRelationType.None, true);
        plain.Should().Contain("  He appears unremarkable of average build.");
        Source("Scripts/Systems/VisualNovelDialogueSystem.cs").Should().Contain("terminal.WriteLine(Loc.Get(\"base.npc_says\", npc.Name2));");
        L("en", "base.npc_says", "Bo").Should().Be("  Bo says:");
    }

    [Fact]
    public async Task ConversationHeader_EveryRowFits_InEveryLanguage_AndTheFrameIsWhole()
    {
        foreach (var lang in AllLanguages)
            foreach (var romance in Enum.GetValues<RomanceRelationType>())
                foreach (var sr in new[] { false, true })
                    foreach (var sex in new[] { CharacterSex.Female, CharacterSex.Male })
                    {
                        var text = await HeaderScreen(lang, TalkNpc(sex, CharacterRace.Troll, true), romance, sr);
                        EveryRowFits(text, $"[{lang}{(sr ? " sr" : "")}] {romance} conversation header");
                        foreach (var row in Rows(text).Where(r => r.StartsWith("║") || r.StartsWith("╔") || r.StartsWith("╚")))
                            row.Length.Should().Be(MaxWidth, $"[{lang}] the header frame is 79 columns: \"{row}\"");
                        if (!sr)
                            Rows(text).Should().Contain(r => r.StartsWith("║  " + LongName), "the name row is inside the frame");
                    }
        // The English description wraps at its words.
        var en = await HeaderScreen("en", TalkNpc(CharacterSex.Male, CharacterRace.Troll, true), RomanceRelationType.None, true);
        string.Join(" ", Rows(en).Select(r => r.Trim()).Where(r => r.Length > 0))
            .Should().Contain("He appears alluring, intense-eyed, fierce-looking, approachable, sharp-witted with massive, intimidating stature.");
    }

    [Fact]
    public void FactionRefusals_WrapAt79_WhereTheyAreShown()
    {
        foreach (var file in new[] { "Scripts/Locations/CastleLocation.cs", "Scripts/Locations/DarkAlleyLocation.cs", "Scripts/Locations/TempleLocation.cs" })
        {
            var src = Source(file);
            src.Should().Contain("UsurperRemake.UI.UIHelper.WriteWrapped(terminal, reason);", $"{file} wraps the join refusal");
            src.Should().NotContain("terminal.WriteLine(reason);", $"{file} has no unwrapped refusal row");
        }
        // The English refusals that ran past 79 columns.
        L("en", "faction.join_faith_devotion").Length.Should().BeGreaterThan(MaxWidth);
        foreach (var lang in AllLanguages)
            foreach (var key in new[] { "faction.join_faith_devotion", "faction.join_shadows_darkness" })
                UIHelper.WordWrap(L(lang, key), UIHelper.WrapWidth).Should().OnlyContain(r => r.Length <= MaxWidth);
        var s = NewScreen();
        UIHelper.WriteWrapped(s.Term, L("en", "faction.join_rejected", "The Shadows", "Unfriendly", "-50"));
        EveryRowFits(s.Text, "faction refusal");
    }

    // ---------- news ----------

    private static readonly string[] RomanceNewsKeys =
    {
        "dialogue.news_left_spouse_for", "dialogue.news_left_spouse_scandal", "dialogue.news_left_spouse_alone", "dialogue.news_married",
        "family.news_wed", "family.news_custody_awarded_one", "family.news_custody_awarded_many", "family.news_custody_granted",
        "romance.news_divorced_infidelity",
    };

    [Fact]
    public void RomanceNews_SortsAndIsGossip_AsTheEnglish_InEveryLanguage()
    {
        foreach (var key in RomanceNewsKeys)
        {
            SameCatchUpBucket(key, LongName, "Bo", "Al");
            SameGossip(key, LongName, "Bo", "Al");
            SameCatchUpBucket(key, LongName, 2, "Al");
        }
        foreach (var key in new[] { "achievement.news_unlocked", "alignment.news_noble_deed", "alignment.news_dark_act" })
        {
            SameCatchUpBucket(key, LongName, "Bo");
            SameGossip(key, LongName, "Bo");
        }
        InAllLanguages(RomanceNewsKeys);
        L("en", "dialogue.news_married", "Bo", "Al").Should().Be("Bo and Al have gotten married! Congratulations to the happy couple!");
        L("en", "family.news_custody_awarded_one", "Bo", 1, "Al").Should().Be("Bo was awarded custody of 1 child in the divorce from Al.");
        L("en", "family.news_custody_awarded_many", "Bo", 3, "Al").Should().Be("Bo was awarded custody of 3 children in the divorce from Al.");
        L("en", "family.news_custody_granted", "Bo", 2).Should().Be("Bo has been granted custody of 2 child(ren) in the divorce.");
        L("en", "romance.news_divorced_infidelity", "Bo").Should().Be("Bo has divorced their partner due to infidelity!");
        L("en", "achievement.news_unlocked", "Bo", "Dragon Slayer").Should().Be("Bo unlocked \"Dragon Slayer\"!");
    }

    [Fact]
    public void AlignmentNews_IsWrittenInTheWritersLanguage()
    {
        var who = new Character { Name1 = LongName, Name2 = LongName };
        var hu = NewsWritten("hu", () => new AlignmentSystem().ModifyAlignment(who, 30, 0, "defended an innocent"));
        hu.Should().ContainSingle().Which.Should().StartWith(L("hu", "alignment.news_noble_deed", LongName, "").TrimEnd());
        NoEnglishLeft(string.Join("\n", hu), new[] { "alignment.news_noble_deed" });
        var en = NewsWritten("en", () => new AlignmentSystem().ModifyAlignment(who, 0, 40, "bank robbery"));
        en.Should().Equal($"{LongName} committed a dark act: bank robbery");
    }

    /// <summary>Every reason literal the callers of ChangeAlignment and ModifyAlignment pass, from the sources.</summary>
    private static List<string> AlignmentReasons()
    {
        var reasons = new List<string>();
        foreach (var file in Directory.GetFiles(Path.Combine(Source2Root(), "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            var text = File.ReadAllText(file);
            foreach (Match m in Regex.Matches(text, @"(?:ChangeAlignment|ModifyAlignment)\((?:[^;]*?)(?:reason:\s*)?\$?""([^""]*)""\s*\)\s*;", RegexOptions.Singleline))
                reasons.Add(m.Groups[1].Value);
        }
        return reasons.Distinct().ToList();
    }

    private static string Source2Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }

    [Fact]
    public void AlignmentNews_NamesTheDeed_InTheWritersLanguage_AndNeverATag()
    {
        var reasons = AlignmentReasons();
        reasons.Should().Contain("bank robbery").And.Contain("castle.knighthood").And.Contain("spared_npc_in_pvp")
            .And.Contain("attacked sleeping {npcName}").And.Contain("Pilgrimage to {selected.LocName()}");
        foreach (var reason in reasons)
        {
            string sample = Regex.Replace(reason, @"\{[^}]*\}", "Bo");
            bool tag = !sample.Contains(' ') && (sample.Contains('.') || sample.Contains('_'));
            var en = InLang("en", () => AlignmentSystem.DeedLabel(sample));
            var hu = InLang("hu", () => AlignmentSystem.DeedLabel(sample));
            if (tag)
            {
                en.Should().BeNull($"the tag \"{sample}\" is not a deed the news can name");
                continue;
            }
            en.Should().Be(sample, $"the English news names \"{sample}\" as before");
            hu.Should().NotBe(sample, $"\"{sample}\" has a Hungarian deed");
        }
        var who = new Character { Name1 = LongName, Name2 = LongName };
        NewsWritten("en", () => new AlignmentSystem().ModifyAlignment(who, 30, 0, "castle.knighthood"))
            .Should().Equal($"{LongName} performed a noble deed.");
        NewsWritten("en", () => new AlignmentSystem().ModifyAlignment(who, 0, 30, "dungeon.merchant_rob_leader"))
            .Should().Equal($"{LongName} committed a dark act.");
        NewsWritten("hu", () => new AlignmentSystem().ModifyAlignment(who, 0, 30, "attacked sleeping Bo"))
            .Should().Equal(L("hu", "alignment.news_dark_act", LongName, L("hu", "alignment.deed_attacked_sleeping", "Bo")));
        NewsWritten("hu", () => new AlignmentSystem().ModifyAlignment(who, 30, 0, "defended an innocent"))
            .Should().Equal(L("hu", "alignment.news_noble_deed", LongName, L("hu", "alignment.deed_defended_innocent")));
        foreach (var key in new[] { "alignment.news_noble_deed_plain", "alignment.news_dark_act_plain" })
        {
            SameCatchUpBucket(key, LongName);
            SameGossip(key, LongName);
        }
    }

    [Fact]
    public void MercTurnInRefusal_NamesTheReason_NotTheCode()
    {
        Source("Scripts/Locations/AnchorRoadLocation.cs").Should().Contain("Loc.Get(\"merc.turnin_failed\", QuestSystem.MercTurnInReasonLabel(reason))");
        foreach (var code in new[] { "null", "not_merc", "not_yours", "incomplete" })
            foreach (var lang in AllLanguages)
            {
                var label = InLang(lang, () => QuestSystem.MercTurnInReasonLabel(code));
                label.Should().Be(L(lang, $"merc.turnin_reason_{code}")).And.NotBe(code);
                ("  " + L(lang, "merc.turnin_failed", label)).Length.Should().BeLessOrEqualTo(MaxWidth);
            }
        InLang("en", () => QuestSystem.MercTurnInReasonLabel("incomplete")).Should().Be("the contract is not complete yet.");
        NoEnglishLeft(InLang("hu", () => QuestSystem.MercTurnInReasonLabel("not_merc")), new[] { "merc.turnin_reason_not_merc" });
    }

    [Fact]
    public void RomanceTracker_JealousyLines_AreKeyed()
    {
        var src = Source("Scripts/Systems/RomanceTracker.cs");
        foreach (var key in new[] { "romance.jealous_enough", "romance.jealous_demands_divorce", "romance.jealous_heartbroken",
                     "romance.jealous_confronts", "romance.jealous_cry", "romance.jealous_suspicious", "romance.news_divorced_infidelity" })
        {
            src.Should().Contain($"Loc.Get(\"{key}\"");
            InAllLanguages(new[] { key });
            if (key.StartsWith("romance.news_")) continue; // news, not a row of the maintenance screen
            foreach (var lang in AllLanguages)
                ("  " + L(lang, key, LongName)).Length.Should().BeLessOrEqualTo(MaxWidth, $"[{lang}] {key} fits with a long name");
        }
        // The divorce reason is stored with the ex-spouse record: it stays English.
        src.Should().Contain("Divorce(npcId, \"Infidelity -- jealousy\", playerInitiated: false);");
        Regex.Matches(src, "messages\\.Add\\(\\$?\"").Count.Should().Be(0, "every jealousy line is a key");
    }

    // ---------- achievements ----------

    private static Achievement Ach(string name, long gold, long xp) => new()
    {
        Id = "test_" + name, Name = name, Description = "Test description", Tier = AchievementTier.Gold,
        GoldReward = gold, ExperienceReward = xp, UnlockMessage = "Well done",
    };

    private static async Task<string> AchievementScreen(string lang, List<Achievement> list, bool screenReader)
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

    private static readonly string[] AchievementKeys =
    {
        "achievement.unlocked_header", "achievement.unlocked_many", "achievement.rewards", "achievement.total_rewards",
        "achievement.and_more",
    };

    [Fact]
    public async Task AchievementPopups_InHungarian_HaveNoEnglishLeft_AndKeepTheBox()
    {
        var one = new List<Achievement> { Ach("One", 100000, 500000) };
        var many = Enumerable.Range(1, 11).Select(i => Ach($"Name {i}", 1000000, 2000000)).ToList();
        foreach (var lang in AllLanguages)
            foreach (var sr in new[] { false, true })
                foreach (var list in new[] { one, many })
                {
                    var text = await AchievementScreen(lang, list, sr);
                    if (lang == "hu") { Capture($"achievement-{list.Count}-hu{(sr ? "-sr" : "")}.txt", text); NoEnglishLeft(text, AchievementKeys); }
                    EveryRowFits(text, $"[{lang}] achievements");
                    foreach (var row in Rows(text).Where(r => r.StartsWith("║") || r.StartsWith("╔") || r.StartsWith("╠") || r.StartsWith("╚")))
                        row.Length.Should().Be(60, $"[{lang}] the achievement box keeps its 60 columns: \"{row}\"");
                    if (lang != "en") text.Should().NotContain("Gold").And.NotContain("UNLOCKED").And.NotContain("Rewards");
                }
        (await AchievementScreen("hu", one, true)).Should().Contain(L("hu", "achievement.rewards",
            $"{L("hu", "achievement.reward_gold", "100000")} {L("hu", "achievement.reward_xp", "500000")}"));
        InAllLanguages(AchievementKeys.Concat(new[] { "achievement.reward_gold", "achievement.reward_xp", "achievement.news_unlocked" }));
    }

    [Fact]
    public async Task AchievementPopups_InEnglish_KeepTheirWords()
    {
        var sr = Rows(await AchievementScreen("en", new List<Achievement> { Ach("One", 100, 50) }, true));
        sr.Should().Contain("* ACHIEVEMENT UNLOCKED! *").And.Contain("  Rewards: +100 Gold +50 XP");
        var many = Enumerable.Range(1, 10).Select(i => Ach($"Name {i}", 1000, 2000)).ToList();
        var text = Rows(await AchievementScreen("en", many, true));
        text.Should().Contain("* 10 ACHIEVEMENTS UNLOCKED! *").And.Contain("  ... and 2 more!").And.Contain("  Total Rewards: +10,000 Gold +20,000 XP");
    }

    // ---------- founder statues ----------

    private static async Task<string> StatueScreen(string lang, FounderStatueData.StatueLocationTag where, bool screenReader)
    {
        int count = FounderStatueData.GetStatuesAt(where).Count();
        var input = Enumerable.Range(1, count).SelectMany(i => new[] { i.ToString(), "" }).Append("R").ToArray();
        var s = NewScreen(input);
        await InLanguage(lang, async () => { await FounderStatueSystem.ShowStatuesAt(where, s.Term); return 0; }, screenReader);
        return s.Text;
    }

    private static readonly string[] FounderKeys =
    {
        "founder.place_pantheon", "founder.place_castle", "founder.place_main_street", "founder.commemorated",
        "founder.examine_prompt", "founder.record_line", "founder.record_lost", "founder.known_to_heavens", "founder.statue_art_sr",
        "founder.cracked_tag",
    };

    private static readonly FounderStatueData.StatueLocationTag[] Places =
    {
        FounderStatueData.StatueLocationTag.Pantheon, FounderStatueData.StatueLocationTag.Castle, FounderStatueData.StatueLocationTag.MainStreetMini,
    };

    [Fact]
    public async Task FounderStatues_InHungarian_HaveNoEnglishLeft_AndFit()
    {
        var hu = new StringBuilder();
        foreach (var where in Places)
            foreach (var sr in new[] { false, true })
            {
                var text = await StatueScreen("hu", where, sr);
                Capture($"statues-{where}-hu{(sr ? "-sr" : "")}.txt", text);
                hu.Append(text);
            }
        NoEnglishLeft(hu.ToString(), FounderKeys);
        hu.ToString().Should().Contain(L("hu", "race.half_elf")).And.Contain(L("hu", "founder.ending_savior"))
            .And.NotContain(", Cycle ").And.NotContain("Half-Elf Barbarian");
        foreach (var lang in AllLanguages)
            foreach (var where in Places)
                foreach (var sr in new[] { false, true })
                    EveryRowFits(await StatueScreen(lang, where, sr), $"[{lang}] {where} statues");
        InAllLanguages(FounderKeys.Concat(new[] { "founder.no_statues", "founder.place_other", "founder.level_class",
            "founder.ending_savior", "founder.ending_usurper", "founder.ending_multiple", "founder.ending_pre_ng", "founder.ending_lost" }));
    }

    [Fact]
    public async Task FounderStatues_StoredRecords_StayEnglish_AndEnglishKeepsItsRows()
    {
        var all = Places.SelectMany(FounderStatueData.GetStatuesAt).ToList();
        all.Select(s => s.ClassName).Should().Contain("Unknown").And.Contain("Mystic Shaman");
        all.Select(s => s.RaceName).Should().Contain("Half-Elf");
        all.Select(s => s.EndingTag).Should().Contain("Savior").And.Contain("Pre-NG+");
        // The display maps them; a value it does not know is shown as stored.
        InLang("hu", () => FounderStatueSystem.ClassLabel("Mystic Shaman")).Should().Be(L("hu", "class.mystic_shaman"));
        InLang("hu", () => FounderStatueSystem.RaceLabel("Half-Elf")).Should().Be(L("hu", "race.half_elf"));
        InLang("hu", () => FounderStatueSystem.EndingLabel("Usurper")).Should().Be(L("hu", "founder.ending_usurper"));
        InLang("hu", () => FounderStatueSystem.ClassLabel("Unknown")).Should().Be(L("hu", "founder.unknown"));   // v1.2.5 (data D6): "Unknown" is keyed
        InLang("hu", () => FounderStatueSystem.ClassLabel("Not A Class")).Should().Be("Not A Class");
        InLang("en", () => FounderStatueSystem.ClassLabel("Mystic Shaman")).Should().Be("Mystic Shaman");
        InLang("en", () => FounderStatueSystem.RaceLabel("Half-Elf")).Should().Be("Half-Elf");
        Source("Scripts/Systems/FounderStatueSystem.cs").Should().Contain("statue.ClassName != \"Unknown\"");

        var en = await StatueScreen("en", FounderStatueData.StatueLocationTag.Castle, true);
        en.Should().Contain("Castle Courtyard: The Slayers of Manwe").And.Contain("alpha-era founders are commemorated across the world.")
            .And.Contain("Examine which statue? (1-");
        var s0 = FounderStatueData.GetStatuesAt(FounderStatueData.StatueLocationTag.Castle).First(s => s.FinalLevel > 0 && s.ClassName != "Unknown");
        en.Should().Contain($"  Lv.{s0.FinalLevel} {s0.RaceName} {s0.ClassName}, Cycle {s0.CycleReached} {s0.EndingTag}");
    }

    // ---------- factions, Level Master, creation, loot, training, Player ----------

    [Fact]
    public void FactionRefusals_AreInThePlayersLanguage()
    {
        var factions = new FactionSystem();
        var low = new Character { Name1 = LongName, Name2 = LongName, Level = 5 };
        InLang("hu", () => factions.CanJoinFaction(Faction.TheCrown, low).reason).Should().Be(L("hu", "faction.join_level_10"));
        InLang("en", () => factions.CanJoinFaction(Faction.TheCrown, low).reason).Should().Be("You must reach Level 10 before joining any faction.");
        var hi = new Character { Name1 = LongName, Name2 = LongName, Level = 20 };
        factions.FactionStanding[Faction.TheFaith] = -120;
        InLang("hu", () => factions.CanJoinFaction(Faction.TheFaith, hi).reason).Should()
            .Be(L("hu", "faction.join_rejected", L("hu", "faction.name_faith"), L("hu", "faction.standing_hated"), (-120).ToString("N0")));
        InLang("en", () => factions.CanJoinFaction(Faction.TheFaith, hi).reason).Should()
            .Be("The Faith won't accept you. Your standing is Hated (-120). Improve your reputation first.");
        factions.FactionStanding[Faction.TheFaith] = 10;
        InLang("hu", () => factions.CanJoinFaction(Faction.TheFaith, hi).reason).Should().Be(L("hu", "faction.join_faith_devotion"));
        InAllLanguages(new[] { "faction.join_already_member", "faction.join_leave_first", "faction.join_level_10", "faction.join_rejected",
            "faction.join_crown_dark", "faction.join_crown_chivalry", "faction.join_shadows_darkness", "faction.join_faith_devotion",
            "faction.join_unknown", "faction.standing_hated", "faction.standing_hostile", "faction.standing_unfriendly" });
        foreach (var lang in OtherLanguages)
            L(lang, "faction.join_level_10").Should().NotBe(L("en", "faction.join_level_10"));
    }

    [Fact]
    public void LootClassReason_IsInThePlayersLanguage_AndEnglishKeepsItsWords()
    {
        var stiletto = new Item { Name = "Stiletto", Type = ObjType.Weapon };
        InLang("en", () => LootGenerator.CanClassUseLootItem(CharacterClass.Warrior, stiletto).reason)
            .Should().Be("Only Assassin, Ranger, Jester can equip this weapon.");
        InLang("hu", () => LootGenerator.CanClassUseLootItem(CharacterClass.Warrior, stiletto).reason).Should().Be(L("hu", "item.class_only_weapon",
            string.Join(", ", new[] { "class.assassin", "class.ranger", "class.jester" }.Select(k => L("hu", k)))));
        InAllLanguages(new[] { "item.class_only_weapon", "item.class_only_shield" });
    }

    [Fact]
    public void SmallRows_AreKeyed_InEveryLanguage()
    {
        // Level Master BBS XP label and press enter, character creation height, weight and error, training reset row,
        // the child naming prompt.
        var lm = Source("Scripts/Locations/LevelMasterLocation.cs");
        lm.Should().Contain("terminal.Write(Loc.Get(\"level_master.bbs_xp\"));").And.Contain("PressAnyKey(\"  \" + Loc.Get(\"ui.press_enter\"))")
            .And.Contain("Loc.Get(\"level_master.reached_level_news\", displayName, player.Level), \"combat\"");
        L("en", "level_master.bbs_xp").Should().Be("  XP:");
        L("en", "character_creation.height_cm", 180).Should().Be("180 cm");
        L("en", "character_creation.error_creation", "boom").Should().Be("Error during character creation: boom");
        L("en", "training.reset_row", " 1", "Swordsmanship".PadRight(24), "cyan", "Expert".PadRight(13), 12)
            .Should().Be($" {1,2}  {"Swordsmanship",-24} [cyan]{"Expert",-13}[/] 12 pts", "the row the code wrote before");
        L("en", "intimacy.child_name_input").Should().Be("  Name: ");
        foreach (var lang in OtherLanguages)
        {
            L(lang, "training.reset_row", " 1", "x", "cyan", "y", 3).Should().Contain("[cyan]y[/]");
            L(lang, "intimacy.child_name_input").Should().NotBe("  Name: ");
        }
        InAllLanguages(new[] { "level_master.bbs_xp", "character_creation.height_cm", "character_creation.weight_kg",
            "character_creation.error_creation", "training.reset_row", "intimacy.child_name_input" });
    }

    private static IEnumerable<string> PlayerAchievementIds => new[]
    {
        "first_level", "experienced", "veteran", "master", "legendary", "wealthy", "rich", "tycoon", "monster_hunter",
        "monster_slayer", "monster_bane", "pvp_warrior", "pvp_champion", "pvp_legend", "ruler", "persistent_ruler",
        "deep_explorer", "depth_seeker", "abyss_walker",
    };

    [Fact]
    public void PlayerDeath_KeepsItsAchievementIds_AndItsLinesAreKeyed()
    {
        var p = new Player { Name1 = LongName, Name2 = LongName, Level = 12, Gold = 60000 };
        InLang("hu", () => { p.Die(); return 0; });
        p.Achievements.Keys.Should().Contain("first_level").And.Contain("experienced").And.Contain("wealthy").And.Contain("rich");
        p.Achievements.Keys.Should().OnlyContain(k => PlayerAchievementIds.Contains(k), "the ids stay English");
        InAllLanguages(PlayerAchievementIds.Select(id => $"player.achievement_{id}")
            .Concat(new[] { "player.achievement_unlocked", "player.death_penalty", "player.permadeath_deleted", "player.resurrected_temple" }));
        L("en", "player.achievement_unlocked", L("en", "player.achievement_wealthy")).Should().Be("Achievement Unlocked: Accumulated 10,000 gold!");
        L("en", "player.death_penalty", 10, 20).Should().Be("Death penalty: Lost 10 experience and 20 gold!");
        foreach (var lang in AllLanguages)
            foreach (var id in PlayerAchievementIds)
                L(lang, "player.achievement_unlocked", L(lang, $"player.achievement_{id}")).Length.Should().BeLessOrEqualTo(MaxWidth);
        p.CurrentLocation.Should().Be("Temple", "the respawn place is stored English");
    }

    // ---------- identifiers kept English and the ratchet ----------

    [Fact]
    public void StoredWords_StayEnglish_AndStillMatchTheirReaders()
    {
        // The wedding place RelationshipSystem passes is matched by NewsSystem.MarriagePlace.
        Source("Scripts/Systems/RelationshipSystem.cs").Should().Contain("WriteMarriageNews(character1.Name, character2.Name, \"Church\");");
        InLang("hu", () => NewsSystem.MarriagePlace("Church")).Should().Be(L("hu", "news.place_church"));
        // The merc contract result codes are compared by AnchorRoadLocation.
        var quest = Source("Scripts/Systems/QuestSystem.cs");
        foreach (var code in new[] { "\"null\"", "\"not_merc\"", "\"not_yours\"", "\"incomplete\"", "\"ok\"" })
            quest.Should().Contain(code);
        // The Steam achievement marker is a protocol string the client relay reads.
        Source("Scripts/Systems/AchievementSystem.cs").Should().Contain("term?.WriteRawAnsi($\"\\x1B]99;ACH:{achievementId}\\x07\");");
        // The dungeon follower help lists typed slash commands, the same in every language.
        Source("Scripts/Locations/DungeonLocation.cs").Should().Contain("term.WriteLine(\"    /party  /stats  /health  /gold  /quests\");");
    }

    private static Dictionary<string, int> Counts(string file, bool data)
    {
        var json = JsonDocument.Parse(Source($"Tests/Localization/{file}"));
        var result = new Dictionary<string, int>();
        foreach (var p in json.RootElement.EnumerateObject())
            result[p.Name] = data
                ? p.Value.EnumerateObject().Sum(c => c.Value.GetProperty("count").GetInt32())
                : p.Value.GetProperty("count").GetInt32();
        return result;
    }

    private static readonly string[] OwnedFiles =
    {
        "Scripts/Systems/CompanionSystem.cs", "Scripts/Systems/DialogueSystem.cs", "Scripts/Systems/VisualNovelDialogueSystem.cs",
        "Scripts/Systems/RelationshipSystem.cs", "Scripts/Systems/FamilySystem.cs", "Scripts/Systems/IntimacySystem.cs",
        "Scripts/Systems/RomanceTracker.cs", "Scripts/Core/Player.cs", "Scripts/Systems/AchievementSystem.cs",
        "Scripts/Systems/FounderStatueSystem.cs", "Scripts/Systems/QuestSystem.cs", "Scripts/Systems/FactionSystem.cs",
        "Scripts/Locations/LevelMasterLocation.cs", "Scripts/Systems/CharacterCreationSystem.cs", "Scripts/Systems/AlignmentSystem.cs",
        "Scripts/Systems/LootGenerator.cs", "Scripts/Systems/TrainingSystem.cs", "Scripts/Locations/DungeonLocation.cs",
    };

    [Fact]
    public void OutputBaseline_KeepsOnlyTheEnglishIdentifiers_AndTheDataBaselineOnlyLostTheKeyedRows()
    {
        var output = Counts("hardcoded-baseline.json", data: false);
        var kept = new Dictionary<string, int>
        {
            ["Scripts/Locations/DungeonLocation.cs"] = 2, ["Scripts/Systems/QuestSystem.cs"] = 5,
            ["Scripts/Systems/AchievementSystem.cs"] = 1, ["Scripts/Systems/RelationshipSystem.cs"] = 1,
        };
        foreach (var f in OwnedFiles)
            (output.TryGetValue(f, out var n) ? n : 0).Should().Be(kept.TryGetValue(f, out var k) ? k : 0, $"{f} output sites");
        var data = Counts("hardcoded-data-baseline.json", data: true);
        data.Should().NotContainKey("Scripts/Systems/AchievementSystem.cs", "the 12 popup rows are keyed; v1.2.5: the achievement table too (DataAchieve125Tests)");
        data.Should().NotContainKey("Scripts/Systems/FactionSystem.cs", "the 9 join refusals are keyed; v1.2.5: the names and ranks too (DataAchieve125Tests)");
        data["Scripts/Systems/VisualNovelDialogueSystem.cs"].Should().Be(2, "the two pronouns are keyed");
        data["Scripts/Systems/CompanionSystem.cs"].Should().Be(94);
        data.Should().NotContainKey("Scripts/Systems/DialogueSystem.cs", "v1.2.5: the dialogue trees are keyed (DataDialogue125Tests)");
        data["Scripts/Locations/DungeonLocation.cs"].Should().Be(77);
    }
}
