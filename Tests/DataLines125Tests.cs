using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the story NPC, small talk, reaction, farewell and memory line tables are Loc keys
/// (npc_dialogue.{Id}). A line is picked once by its id, the same in every language, and written in the
/// reader's language. Only line ids are stored (RecentDialogueIds); NPC memories keep their own English
/// text and type, and a memory line is chosen by the type and written per reader.
/// </summary>
[Collection("SharedGameSingletons")]
public class DataLines125Tests : IDisposable
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    /// <summary>A 30 character name, the longest a player can take.</summary>
    private const string LongName = "Eszterhazy Kalandor Bajnoka Ur";

    private static readonly FieldInfo AllLines = typeof(NPCDialogueDatabase).GetField("_allLines", BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly object? _prevLines;
    private readonly Random _prevRng;
    private readonly Func<int> _prevHour;
    private readonly string _prevLang;

    public DataLines125Tests()
    {
        NPCDialogueDatabase.Initialize();
        _prevLines = AllLines.GetValue(null);
        _prevRng = NPCDialogueGenerator.Rng;
        _prevHour = NPCDialogueDatabase.Hour;
        _prevLang = GameConfig.Language;
        NPCDialogueDatabase.Hour = () => 7;
        NPCDialogueDatabase.ClearAllTracking();
    }

    public void Dispose()
    {
        AllLines.SetValue(null, _prevLines);
        NPCDialogueGenerator.Rng = _prevRng;
        NPCDialogueDatabase.Hour = _prevHour;
        GameConfig.Language = _prevLang;
        NPCDialogueDatabase.ClearAllTracking();
    }

    // ---------------------------------------------------------------- helpers

    /// <summary>The five tables, in the order GetAllBuiltInLines (and so the modder export) lists them.</summary>
    private static List<NPCDialogueDatabase.DialogueLine> OwnedLines()
        => DialogueLines_SmallTalk.GetLines()
            .Concat(DialogueLines_Farewells.GetLines())
            .Concat(DialogueLines_Reactions.GetLines())
            .Concat(DialogueLines_Memory.GetLines())
            .Concat(DialogueLines_StoryNPCs.GetLines()).ToList();

    private static void WithLines(IEnumerable<NPCDialogueDatabase.DialogueLine> lines, Action body)
    {
        var prev = AllLines.GetValue(null);
        try { AllLines.SetValue(null, lines.ToList()); body(); }
        finally { AllLines.SetValue(null, prev); }
    }

    private static readonly Regex Placeholder = new(@"\{(\d)\}");

    private static HashSet<string> Args(string s) => Placeholder.Matches(s).Select(m => m.Groups[1].Value).ToHashSet();

    /// <summary>The literal words of an English value (between placeholders), long enough to be English.</summary>
    private static IEnumerable<string> EnglishSegments(string key)
    {
        foreach (var part in Placeholder.Split(Loc.GetIn("en", key)).Where((_, i) => i % 2 == 0))
        {
            string seg = part.Trim().Trim(',', '.', '!', '?', '*', ' ', '"');
            if (seg.Length >= 12) yield return seg;
        }
    }

    private static readonly string[] Archetypes = { "guard", "merchant", "thief", "assassin", "priest", "noble", "thug", "citizen", "mystic" };
    private static readonly string[] PersonalityArchetypes = { "fierce", "honorable", "cunning", "pious", "wise", "bitter", "charming", "silent" };
    private static readonly EmotionType[] Emotions =
        { EmotionType.Joy, EmotionType.Sadness, EmotionType.Anger, EmotionType.Fear, EmotionType.Confidence, EmotionType.Loneliness, EmotionType.Hope, EmotionType.Peace };
    private static readonly MemoryType[] Memories =
        { MemoryType.Helped, MemoryType.Attacked, MemoryType.Betrayed, MemoryType.Saved, MemoryType.Defended, MemoryType.Traded, MemoryType.Insulted, MemoryType.Complimented };
    private static readonly string[] Events = { "combat_victory", "combat_defeat", "combat_flee", "ally_death" };

    private static readonly Dictionary<string, NPC> NpcCache = new();

    /// <summary>An NPC varied by `n` (personality type, memory of the player, emotion); `name` null for a generic NPC,
    /// or a story NPC's name. The same instance for the same arguments, so each language sees the same NPC.</summary>
    private static NPC Npc(int n, string? name = null)
    {
        string cacheKey = n + "|" + name;
        lock (NpcCache)
        {
            if (NpcCache.TryGetValue(cacheKey, out var cached)) return cached;
            var npc = new NPC { Name1 = name ?? LongName, Name2 = name ?? LongName, Level = 20, HP = 100, MaxHP = 100 };
            npc.Archetype = Archetypes[n % Archetypes.Length];
            var p = PersonalityProfile.GenerateForArchetype("commoner");
            p.Archetype = PersonalityArchetypes[n % PersonalityArchetypes.Length];
            npc.Personality = p;
            npc.Memory = new MemorySystem();
            npc.Memory.RecordEvent(new MemoryEvent
            {
                Type = Memories[n % Memories.Length], InvolvedCharacter = LongName, Importance = 0.9f,
                Description = "An old memory, kept in English", Timestamp = DateTime.Now.AddDays(-2),
            }, keepTimestamp: true);
            npc.EmotionalState = new EmotionalState();
            npc.EmotionalState.AddEmotion(Emotions[n % Emotions.Length], 0.9f, 600);
            NpcCache[cacheKey] = npc;
            return npc;
        }
    }

    private static Player Hero(int n) => new()
    {
        Name1 = LongName, Name2 = LongName, Class = (CharacterClass)(n % 11), Level = n % 3 == 0 ? 60 : 2,
        HP = n % 2 == 0 ? 10 : 100, MaxHP = 100, Gold = n % 4 == 0 ? 20000 : 50, King = n % 5 == 0,
    };

    private static List<string> StoryNames() => DialogueLines_StoryNPCs.GetLines().Select(l => l.NpcName!).Distinct().ToList();

    /// <summary>Every (category, event, story NPC name) a pick can be asked for in these tables.</summary>
    private static IEnumerable<(string Category, string? Event, string? Name)> Asks()
    {
        foreach (var c in new[] { "smalltalk", "farewell", "memory" }) yield return (c, null, null);
        foreach (var e in Events) yield return ("reaction", e, null);
        foreach (var name in StoryNames())
            foreach (var c in new[] { "greeting", "smalltalk", "farewell" }) yield return (c, null, name);
    }

    /// <summary>The line ids PickLine chooses for `seed` in `lang`, ten picks in a row (the recent-line memory steers later picks).</summary>
    private static List<string?> PickIds(string lang, int seed, (string Category, string? Event, string? Name) ask)
    {
        NPCDialogueDatabase.ClearAllTracking();
        var rng = new Random(seed);
        return CombatEngine.InLanguage(lang, () => Enumerable.Range(0, 10)
            .Select(_ => NPCDialogueDatabase.PickLine(ask.Category, Npc(seed, ask.Name), Hero(seed), ask.Event, rng)?.Id).ToList());
    }

    private static string Render(string lang, NPCDialogueDatabase.DialogueLine line, NPC npc, Player player)
        => CombatEngine.InLanguage(lang, () => NPCDialogueDatabase.RenderLine(line, npc, player));

    // ---------------------------------------------------------------- keys

    [Fact]
    public void EveryLine_IsAKey_InFiveLanguages_Translated_WithoutDashes_KeepingItsPlaceholders()
    {
        var lines = OwnedLines();
        lines.Should().HaveCount(331);
        lines.Select(l => l.Id).Should().OnlyHaveUniqueItems();
        foreach (var line in lines)
        {
            string key = "npc_dialogue." + line.Id;
            foreach (var lang in Langs)
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{key} in {lang}");
                string v = Loc.GetIn(lang, key);
                v.Should().NotContain("—").And.NotContain("–").And.NotContain("…");
                if (lang == "en") continue;
                v.Should().NotBe(Loc.GetIn("en", key), $"{key} has its own {lang} text");
                v.Should().NotContain("--", $"{key} in {lang}");
                Args(v).Should().BeEquivalentTo(Args(Loc.GetIn("en", key)), $"{key} in {lang}");
            }
        }
    }

    [Fact]
    public void OwnedTables_HaveNoTextInSource_AndKeepTheirEnglishText()
    {
        foreach (var line in OwnedLines())
        {
            line.Text.Should().NotBeNullOrEmpty();
            NPCDialogueDatabase.ToTemplate(line.Text).Should().Be(Loc.GetIn("en", "npc_dialogue." + line.Id));
            line.Text.Should().NotMatchRegex(@"\{\d\}", "the table text keeps its named placeholders for modders");
        }
        // the table text is English whatever the language (export, editor and mod comparison read it)
        GameConfig.Language = "hu";
        OwnedLines().Select(l => l.Text).Should().Equal(CombatEngine.InLanguage("en", () => OwnedLines().Select(l => l.Text).ToList()));
        DialogueLines_StoryNPCs.GetLines().First().Text.Should().StartWith("HA! {player_name}! Grok was just telling someone");
        DialogueLines_Memory.GetLines().First().Text.Should().StartWith("You helped me out before.");
    }

    /// <summary>
    /// The slots these tables held English in (removed from hardcoded-data-sources.txt when they emptied)
    /// still hold none: a row written back as an English literal is found here.
    /// </summary>
    [Fact]
    public void FormerTextSlots_StayEmpty()
    {
        var files = new[] { "StoryNPCs", "SmallTalk", "Reactions", "Farewells", "Memory" }
            .Select(f => $"Scripts/Data/DialogueLines_{f}.cs").ToList();
        var sources = files.Select(f => new UsurperReborn.Tests.Localization.DataTextScanner.Source("table", f, "init.Text", "former table")).ToList();
        string root = UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot();
        foreach (var file in files)
        {
            var result = UsurperReborn.Tests.Localization.DataTextScanner.ScanSource(file, File.ReadAllText(Path.Combine(root, file)),
                Array.Empty<UsurperReborn.Tests.Localization.HardcodedTextScanner.Exclusion>(), sources);
            result.Sites.Should().BeEmpty($"{file} keeps its text in Localization/*.json");
        }
    }

    // ---------------------------------------------------------------- selection

    [Fact]
    public void SameSeed_PicksTheSameLineId_InEveryLanguage()
    {
        WithLines(OwnedLines(), () =>
        {
            int storyPicked = 0;
            foreach (var ask in Asks())
            {
                int picked = 0;
                for (int seed = 0; seed < 24; seed++)
                {
                    var en = PickIds("en", seed, ask);
                    picked += en.Count(id => id != null);
                    foreach (var lang in Langs.Skip(1))
                        PickIds(lang, seed, ask).Should().Equal(en, $"{ask} seed {seed} in {lang}");
                }
                // a story NPC whose lines all ask for a closer relationship picks none at the default one
                if (ask.Name == null) picked.Should().BeGreaterThan(0, $"{ask} picks a line");
                else storyPicked += picked;
            }
            storyPicked.Should().BeGreaterThan(100, "story NPC lines are picked");
        });
    }

    [Fact]
    public void GeneratorPicks_FromTheseTables_AreOneLineId_TheSameInEveryLanguage()
    {
        WithLines(OwnedLines(), () =>
        {
            var pickers = new Func<NPC, Player, NPCDialogueGenerator.NpcLine>[] { NPCDialogueGenerator.PickSmallTalk, NPCDialogueGenerator.PickFarewell };
            foreach (var pick in pickers)
                for (int seed = 0; seed < 60; seed++)
                {
                    var byLang = Langs.ToDictionary(lang => lang, lang =>
                    {
                        NPCDialogueDatabase.ClearAllTracking();
                        NPCDialogueGenerator.Rng = new Random(seed);
                        return CombatEngine.InLanguage(lang, () => pick(Npc(seed), Hero(seed)).Keys.ToList());
                    });
                    byLang["en"].Should().ContainSingle().Which.Should().StartWith("npc_dialogue.");
                    foreach (var lang in Langs.Skip(1)) byLang[lang].Should().Equal(byLang["en"], $"seed {seed} in {lang}");
                }
        });
    }

    // ---------------------------------------------------------------- no English in the reader's language

    [Fact]
    public void EveryLine_InEachLanguage_IsThatLanguage_WithNamesStandingAlone()
    {
        var player = new Player { Name1 = LongName, Name2 = LongName, Class = CharacterClass.Warrior };
        var npc = new NPC { Name1 = "Zz Speaker", Name2 = "Zz Speaker" };
        foreach (var line in OwnedLines())
        {
            string key = "npc_dialogue." + line.Id;
            string en = Render("en", line, npc, player);
            foreach (var lang in Langs.Skip(1))
            {
                string text = Render(lang, line, npc, player);
                text.Should().Be(string.Format(Loc.GetIn(lang, key), LongName, "Zz Speaker",
                    Loc.GetIn(lang, "npc_dialogue.ph.adventurer"), Loc.GetIn(lang, "npc_dialogue.tod.morning"),
                    CombatEngine.InLanguage(lang, () => player.ClassName), Loc.GetIn(lang, "npc_dialogue.tod_salute.morning"),
                    Loc.GetIn(lang, "npc_dialogue.tod_all.morning")), $"{line.Id} in {lang}");
                text.Should().NotBe(en).And.NotContain("{");
                foreach (var seg in EnglishSegments(key))
                    text.Should().NotContain(seg, $"{line.Id} in {lang}");
            }
            string hu = Render("hu", line, npc, player);
            // a name is never glued to a suffix in hu
            Regex.IsMatch(hu, Regex.Escape(LongName) + @"(-|\w)").Should().BeFalse($"{line.Id}: {hu}");
            string huClass = CombatEngine.InLanguage("hu", () => player.ClassName);
            if (Loc.GetIn("hu", key).Contains("{4}"))
                Regex.IsMatch(hu, Regex.Escape(huClass) + @"(-|\w)").Should().BeFalse($"{line.Id}: {hu}");
        }
    }

    [Fact]
    public void AssembledLines_InHungarian_HaveNoEnglishFragment()
    {
        WithLines(OwnedLines(), () =>
        {
            var leader = Hero(1);
            int seen = 0;
            for (int seed = 0; seed < 60; seed++)
            {
                foreach (var pick in new Func<NPC, Player, NPCDialogueGenerator.NpcLine>[] { NPCDialogueGenerator.PickSmallTalk, NPCDialogueGenerator.PickFarewell, NPCDialogueGenerator.PickGreeting })
                {
                    NPCDialogueGenerator.Rng = new Random(seed);
                    var npc = seed % 2 == 0 ? Npc(seed) : Npc(seed, StoryNames()[seed % StoryNames().Count]);
                    var line = CombatEngine.InLanguage("en", () => pick(npc, Hero(seed)));
                    string hu = CombatEngine.InLanguage("hu", line.Render);
                    foreach (var key in line.Keys)
                        foreach (var seg in EnglishSegments(key))
                            hu.Should().NotContain(seg, $"seed {seed}: {hu}");
                    seen++;
                }
                foreach (var ev in Events)
                {
                    NPCDialogueGenerator.Rng = new Random(seed);
                    var say = NPCDialogueGenerator.ReactionInLanguage(Npc(seed), leader, ev);
                    string rows = string.Join(" ", CombatEngine.NpcReactionRows("hu", "Zz Ally", CombatEngine.InLanguage("hu", say)));
                    foreach (var line in OwnedLines().Where(l => l.EventType == ev))
                        foreach (var seg in EnglishSegments("npc_dialogue." + line.Id))
                            rows.Should().NotContain(seg, $"{ev} seed {seed}: {rows}");
                    seen++;
                }
            }
            seen.Should().BeGreaterThan(100);
        });
    }

    /// <summary>
    /// No line in these tables is joined to a mood or emote prefix (the generator's emote prefix is on its own
    /// template lines only): a picked line is one key and renders alone. The one prefix these lines get is the
    /// speaker's name before a reaction, and it comes from the reader's language with the line.
    /// </summary>
    [Fact]
    public void SpeakerPrefix_AndLine_AreFromTheSameLanguage()
    {
        WithLines(OwnedLines(), () =>
        {
            int fromTables = 0;
            for (int seed = 0; seed < 40; seed++)
            {
                NPCDialogueGenerator.Rng = new Random(seed);
                var npc = Npc(seed, StoryNames()[seed % StoryNames().Count]);
                var line = NPCDialogueGenerator.PickGreeting(npc, Hero(seed));
                if (!line.Keys[0].StartsWith("npc_dialogue.")) continue;   // no story line fits: the generator's own (D3)
                line.Keys.Should().ContainSingle();
                fromTables++;
                var picked = OwnedLines().Single(l => "npc_dialogue." + l.Id == line.Keys[0]);
                foreach (var lang in Langs)
                    CombatEngine.InLanguage(lang, line.Render).Should().Be(Render(lang, picked, npc, Hero(seed)), lang);
            }
            fromTables.Should().BeGreaterThan(10);
        });
        GameConfig.Language = "en";
        var leader = new Player { Name2 = "Leader" };
        foreach (var line in OwnedLines().Where(l => l.Category == "reaction"))
            foreach (var lang in Langs)
            {
                string said = Render(lang, line, Npc(0), leader);
                var rows = CombatEngine.NpcReactionRows(lang, "Zz Ally", said);
                string whole = string.Join(" ", rows.Select(r => r.Trim()));
                whole.Should().Be(Loc.GetIn(lang, "combat.npc_reaction", "Zz Ally", said).Trim(), $"{line.Id} in {lang}");
            }
    }

    // ---------------------------------------------------------------- width

    [Fact]
    public void EveryRow_Fits79Columns_InFiveLanguages_WithA30CharacterName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        var player = new Player { Name1 = LongName, Name2 = LongName, King = true };
        var npc = new NPC { Name1 = LongName, Name2 = LongName };
        foreach (var lang in Langs)
        {
            string longestClass = Enum.GetValues<CharacterClass>()
                .Select(c => CombatEngine.InLanguage(lang, () => GameConfig.GetLocalizedClassName(c))).OrderByDescending(s => s.Length).First();
            foreach (var line in OwnedLines())
            {
                string text = Render(lang, line, npc, player);
                if (Loc.GetIn(lang, "npc_dialogue." + line.Id).Contains("{4}"))
                    text = string.Format(Loc.GetIn(lang, "npc_dialogue." + line.Id), LongName, LongName, "", "", longestClass, "", "");
                var rows = NPCDialogueGenerator.QuotedRows(text);
                rows.Should().OnlyContain(r => r.Length <= 79, $"{line.Id} in {lang}");
                rows.Count.Should().BeLessThanOrEqualTo(MaxRows, $"{line.Id} in {lang}: {text}");
                if (line.Category == "reaction")
                    CombatEngine.NpcReactionRows(lang, LongName, text).Should().OnlyContain(r => r.Length <= 79, $"{line.Id} in {lang}");
            }
        }
    }

    /// <summary>The most rows a line takes in any language, as measured (the longest English story lines take three).</summary>
    private const int MaxRows = 3;

    // ---------------------------------------------------------------- stored

    private static readonly MethodInfo SerializeMemories = typeof(SaveSystem).GetMethod("SerializeMemories", BindingFlags.NonPublic | BindingFlags.Instance)!;

    /// <summary>
    /// NPC memories keep their type and their own English text; a memory line is chosen by the type and written
    /// in the reader's language. Saving in any language writes the same bytes, an old save's English memory loads
    /// as it was, and the picked line ids (the only thing stored about lines) survive save and reload.
    /// </summary>
    [Fact]
    public void StoredMemories_AndLineIds_AreUnchanged_OnSaveAndReload()
    {
        WithLines(OwnedLines(), () =>
        {
            const string oldText = "Was helped by a stranger at the gate";
            var npc = new NPC { Name1 = "Zz Keeper", Name2 = "Zz Keeper" };
            var p = PersonalityProfile.GenerateForArchetype("commoner");
            p.Archetype = "honorable";
            npc.Personality = p;
            npc.Memory = new MemorySystem();
            npc.Memory.RecordEvent(new MemoryEvent
            {
                Type = MemoryType.Helped, Description = oldText, InvolvedCharacter = LongName, Importance = 0.9f,
                Timestamp = new DateTime(2026, 9, 1, 12, 0, 0, DateTimeKind.Utc),
            }, keepTimestamp: true);
            var hero = new Player { Name1 = LongName, Name2 = LongName };

            // the memory line is picked by the memory's type: the same id in every language, and the memory is untouched
            var saved = new Dictionary<string, string>();
            string? id = null;
            foreach (var lang in Langs)
            {
                NPCDialogueDatabase.ClearAllTracking();
                var line = CombatEngine.InLanguage(lang, () => NPCDialogueDatabase.PickLine("memory", npc, hero, null, new Random(5)));
                line.Should().NotBeNull();
                line!.MemoryType.Should().Be("helped");
                (id ??= line.Id).Should().Be(line.Id, lang);
                Render(lang, line, npc, hero).Should().Be(string.Format(Loc.GetIn(lang, "npc_dialogue." + id),
                    LongName, "Zz Keeper", "", "", "", "", ""));
                var data = new NPCData
                {
                    Name = "Zz Keeper",
                    Memories = CombatEngine.InLanguage(lang, () => (List<MemoryData>)SerializeMemories.Invoke(SaveSystem.Instance, new object?[] { npc.Memory })!),
                    RecentDialogueIds = NPCDialogueDatabase.GetRecentlyUsedIds("Zz Keeper")!.ToList(),
                };
                saved[lang] = JsonSerializer.Serialize(data);
            }
            foreach (var lang in Langs) saved[lang].Should().Be(saved["en"], $"a save in {lang} stores what a save in en stores");
            npc.Memory.AllMemories.Should().ContainSingle().Which.Description.Should().Be(oldText);

            // reload in hu, the way GameEngine restores memories: the English text is kept, the line is written in hu
            GameConfig.Language = "hu";
            var loaded = JsonSerializer.Deserialize<NPCData>(saved["en"])!;
            loaded.RecentDialogueIds.Should().Equal(id);
            loaded.Memories.Should().ContainSingle().Which.Description.Should().Be(oldText);
            var back = new NPC { Name1 = "Zz Keeper", Name2 = "Zz Keeper", Personality = p, Memory = new MemorySystem() };
            foreach (var m in loaded.Memories)
                back.Memory.RecordEvent(new MemoryEvent
                {
                    Type = Enum.Parse<MemoryType>(m.Type), Description = m.Description, InvolvedCharacter = m.InvolvedCharacter,
                    Importance = m.Importance, EmotionalImpact = m.EmotionalImpact, Timestamp = m.Timestamp,
                }, keepTimestamp: true);
            NPCDialogueDatabase.ClearAllTracking();
            NPCDialogueDatabase.RestoreRecentlyUsedIds("Zz Keeper", loaded.RecentDialogueIds);
            NPCDialogueDatabase.GetRecentlyUsedIds("Zz Keeper").Should().Equal(id);
            back.Memory.AllMemories.Single().Description.Should().Be(oldText);
            var again = NPCDialogueDatabase.PickLine("memory", back, hero, null, new Random(5))!;
            again.MemoryType.Should().Be("helped");
            NPCDialogueDatabase.RenderLine(again, back, hero).Should().Be(string.Format(Loc.GetIn("hu", "npc_dialogue." + again.Id),
                LongName, "Zz Keeper", "", "", "", "", ""));
            GameConfig.Language = "en";
        });
    }

    /// <summary>
    /// The lines exported for modders (dialogue.json) are byte-identical to 1.2.4's in any language: the hash is of
    /// these five tables as GameDataLoader writes them (the full export was compared with the base export by cmp,
    /// receipts/export-cmp.log).
    /// </summary>
    [Fact]
    public void ModderExport_OfTheseTables_IsUnchanged_InAnyLanguage()
    {
        foreach (var lang in new[] { "en", "hu" })
        {
            GameConfig.Language = lang;
            var dir = Path.Combine(Path.GetTempPath(), "datalines125-" + Guid.NewGuid().ToString("N"));
            try
            {
                GameDataLoader.ExportDefaults(dir);
                var all = JsonSerializer.Deserialize<List<NPCDialogueDatabase.DialogueLine>>(File.ReadAllText(Path.Combine(dir, "dialogue.json")),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                var ids = OwnedLines().Select(l => l.Id).ToHashSet();
                var mine = all.Where(l => ids.Contains(l.Id)).Select(l => l.Id + "\u0001" + l.Text).ToList();
                mine.Should().HaveCount(331);
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", mine))));
                hash.Should().Be(ExportHash, lang);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
        GameConfig.Language = "en";
    }

    private const string ExportHash = "E80A0348BA28E4E8E9DFAF04925FCB09C3214724E402EBD1CD6209E03688FA8B";
}
