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
/// v1.2.5: the NPC dialogue generator, the greeting lines and the shop mood lines are Loc keys. A line is
/// picked once (the same keys in every language) and written in the reader's language, every piece of it
/// in that one language; pieces that go into a sentence are whole-sentence templates. Only line ids are
/// stored (RecentDialogueIds).
/// </summary>
[Collection("SharedGameSingletons")]
public class DataNpcDialogue125Tests : IDisposable
{
    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    /// <summary>A 30 character name, the longest a player can take.</summary>
    private const string LongName = "Eszterhazy Kalandor Bajnoka Ur";

    private static readonly FieldInfo AllLines = typeof(NPCDialogueDatabase).GetField("_allLines", BindingFlags.NonPublic | BindingFlags.Static)!;

    private readonly object? _prevLines;
    private readonly Random _prevRng;
    private readonly Func<int> _prevHour;
    private readonly string _prevLang;

    public DataNpcDialogue125Tests()
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

    private static List<NPCDialogueDatabase.DialogueLine> OwnedLines()
        => DialogueLines_Greetings.GetLines().Concat(DialogueLines_MoodPrefixes.GetLines()).ToList();

    private static readonly string[] Tods = { "night", "morning", "afternoon", "evening" };

    private static IEnumerable<string> DbKeys()
    {
        foreach (var l in OwnedLines()) yield return "npc_dialogue." + l.Id;
        yield return "npc_dialogue.ph.majesty";
        yield return "npc_dialogue.ph.adventurer";
        foreach (var t in Tods)
        {
            yield return "npc_dialogue.tod." + t;
            yield return "npc_dialogue.tod_salute." + t;
            yield return "npc_dialogue.tod_all." + t;
        }
    }

    private static List<string> GenKeys() => NPCDialogueGenerator.AllKeys().ToList();

    /// <summary>Keys whose text is the same as English in a language, and why: joins, and words shared with English.</summary>
    private static readonly HashSet<string> SameAsEnglish = new()
    {
        "*:npc_gen.join", "*:npc_gen.join_emote", "*:npc_gen.with_title",
        "es:npc_gen.mod.low_aggression.1", "fr:npc_gen.mod.low_aggression.1", "it:npc_gen.mod.low_aggression.1",   // "Oh, {0}"
        "es:npc_gen.mod.high_romanticism.1", "fr:npc_gen.mod.high_romanticism.1", "it:npc_gen.mod.high_romanticism.1", // "Ah, {0}"
        "es:npc_gen.title.mystic.2", // "Mortal" is Spanish too
        "hu:npc_gen.react_none",     // "Hm." is Hungarian too
    };

    private static bool MaySameAsEnglish(string lang, string key)
        => SameAsEnglish.Contains("*:" + key) || SameAsEnglish.Contains(lang + ":" + key);

    private static void WithLines(IEnumerable<NPCDialogueDatabase.DialogueLine> lines, Action body)
    {
        var prev = AllLines.GetValue(null);
        try { AllLines.SetValue(null, lines.ToList()); body(); }
        finally { AllLines.SetValue(null, prev); }
    }

    private static readonly string[] Archetypes = { "guard", "merchant", "thief", "assassin", "priest", "noble", "thug", "citizen", "mystic" };
    private static readonly EmotionType[] Emotions =
        { EmotionType.Joy, EmotionType.Sadness, EmotionType.Anger, EmotionType.Fear, EmotionType.Confidence, EmotionType.Loneliness, EmotionType.Hope, EmotionType.Peace };
    private static readonly MemoryType[] Memories =
        { MemoryType.Helped, MemoryType.Attacked, MemoryType.Betrayed, MemoryType.Traded, MemoryType.SharedDrink, MemoryType.Defended, MemoryType.Saved, MemoryType.Insulted, MemoryType.Complimented, MemoryType.SharedItem };

    private static readonly Dictionary<int, NPC> NpcCache = new();
    private static readonly Dictionary<int, Player> HeroCache = new();

    /// <summary>An NPC that can take every branch of the generator, varied by `n`; the same instance for the same n
    /// (its profile has random traits), so each language sees the same NPC.</summary>
    private static NPC Npc(int n)
    {
        lock (NpcCache)
        {
            if (!NpcCache.TryGetValue(n, out var npc)) NpcCache[n] = npc = NewNpc(n);
            return npc;
        }
    }

    private static NPC NewNpc(int n)
    {
        var npc = new NPC { Name1 = LongName, Name2 = LongName, Level = 20, HP = 100, MaxHP = 100 };
        npc.Archetype = Archetypes[n % Archetypes.Length];
        var p = PersonalityProfile.GenerateForArchetype("commoner");
        p.Aggression = 0.5f; p.Intelligence = 0.5f; p.Greed = 0.5f; p.Romanticism = 0.5f; p.Sociability = 0.5f;
        p.Loyalty = 0.5f; p.Courage = 0.5f; p.Trustworthiness = 0.5f; p.Vengefulness = 0.5f;
        switch (n % 10)
        {
            case 0: p.Aggression = 0.9f; break;
            case 1: p.Aggression = 0.1f; break;
            case 2: p.Intelligence = 0.9f; break;
            case 3: p.Greed = 0.9f; break;
            case 4: p.Romanticism = 0.9f; break;
            case 5: p.Sociability = 0.9f; break;
            case 6: p.Loyalty = 0.9f; break;
            case 7: p.Courage = 0.9f; break;
            case 8: p.Trustworthiness = 0.1f; break;
        }
        npc.Personality = p;
        npc.Memory = new MemorySystem();
        npc.Memory.RecordEvent(new MemoryEvent
        {
            Type = Memories[n % Memories.Length], InvolvedCharacter = LongName, Importance = 0.9f,
            Timestamp = DateTime.Now.AddDays(-(new[] { 0.5, 2, 5, 20, 45, 90 }[n % 6])),
        }, keepTimestamp: true);
        npc.EmotionalState = new EmotionalState();
        npc.EmotionalState.AddEmotion(Emotions[n % Emotions.Length], 0.9f, 600);
        return npc;
    }

    private static Player Hero(int n)
    {
        lock (HeroCache)
        {
            if (!HeroCache.TryGetValue(n, out var hero)) HeroCache[n] = hero = NewHero(n);
            return hero;
        }
    }

    private static Player NewHero(int n) => new()
    {
        Name1 = LongName, Name2 = LongName, Class = (CharacterClass)(n % 11), Level = n % 3 == 0 ? 60 : 2,
        HP = n % 2 == 0 ? 10 : 100, MaxHP = 100, Gold = n % 4 == 0 ? 20000 : 50, King = n % 5 == 0,
    };

    private static readonly Regex Placeholder = new(@"\{(\d)\}");

    private static HashSet<string> Args(string s) => Placeholder.Matches(s).Select(m => m.Groups[1].Value).ToHashSet();

    /// <summary>The literal words of an English value (between placeholders), each long enough to be English, not a shared mark.</summary>
    private static IEnumerable<string> EnglishSegments(string key)
    {
        foreach (var part in Placeholder.Split(Loc.GetIn("en", key)).Where((_, i) => i % 2 == 0))
        {
            string seg = part.Trim().Trim(',', '.', '!', '?', '*', ' ', '"');
            if (seg.Length >= 4) yield return seg;
        }
    }

    // ---------------------------------------------------------------- keys

    [Fact]
    public void EveryKey_IsInFiveLanguages_Translated_WithoutDashes()
    {
        var keys = GenKeys().Concat(DbKeys()).ToList();
        keys.Should().OnlyHaveUniqueItems();
        keys.Count.Should().Be(628);
        foreach (var lang in Langs)
            foreach (var key in keys)
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{key} in {lang}");
                string v = Loc.GetIn(lang, key);
                v.Should().NotContain("\u2014").And.NotContain("\u2013").And.NotContain("\u2026");
                if (lang != "en" && !MaySameAsEnglish(lang, key))
                    v.Should().NotBe(Loc.GetIn("en", key), $"{key} has its own {lang} text");
            }
    }

    [Fact]
    public void EveryKey_KeepsItsPlaceholders()
    {
        foreach (var lang in Langs.Skip(1))
        {
            foreach (var key in GenKeys())
                Args(Loc.GetIn(lang, key)).Should().BeEquivalentTo(Args(Loc.GetIn("en", key)), $"{key} in {lang}");
            foreach (var key in DbKeys())
            {
                var en = Args(Loc.GetIn("en", key));
                var tr = Args(Loc.GetIn(lang, key));
                tr.Except(new[] { "3", "5", "6" }).Should().BeEquivalentTo(en.Except(new[] { "3" }), $"{key} in {lang}");
                if (en.Contains("3")) tr.Intersect(new[] { "3", "5", "6" }).Should().NotBeEmpty($"{key} in {lang} says the time");
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
        DialogueLines_MoodPrefixes.GetLines().Select(l => l.Text).Should().Equal(
            CombatEngine.InLanguage("en", () => DialogueLines_MoodPrefixes.GetLines().Select(l => l.Text).ToList()));
        DialogueLines_MoodPrefixes.GetLines().First().Text.Should().StartWith("{npc_name} is humming a tune");
    }

    // ---------------------------------------------------------------- selection

    private static List<string> PickKeys(string lang, int seed, Func<NPC, Player, NPCDialogueGenerator.NpcLine> pick)
    {
        NPCDialogueDatabase.ClearAllTracking();
        NPCDialogueGenerator.Rng = new Random(seed);
        return CombatEngine.InLanguage(lang, () => pick(Npc(seed), Hero(seed)).Keys.ToList());
    }

    public static IEnumerable<object[]> Pickers() => new[]
    {
        new object[] { "greeting" }, new object[] { "farewell" }, new object[] { "smalltalk" },
    };

    private static Func<NPC, Player, NPCDialogueGenerator.NpcLine> Picker(string what) => what switch
    {
        "greeting" => NPCDialogueGenerator.PickGreeting,
        "farewell" => NPCDialogueGenerator.PickFarewell,
        _ => NPCDialogueGenerator.PickSmallTalk,
    };

    [Theory]
    [MemberData(nameof(Pickers))]
    public void SameSeed_PicksTheSameKeys_InEveryLanguage(string what)
    {
        foreach (bool templates in new[] { true, false })
        {
            var lines = templates ? new List<NPCDialogueDatabase.DialogueLine>() : NPCDialogueDatabase.GetAllBuiltInLines();
            WithLines(lines, () =>
            {
                for (int seed = 0; seed < 150; seed++)
                {
                    var en = PickKeys("en", seed, Picker(what));
                    en.Should().NotBeEmpty();
                    foreach (var lang in Langs.Skip(1))
                        PickKeys(lang, seed, Picker(what)).Should().Equal(en, $"{what} seed {seed} in {lang}");
                }
            });
        }
    }

    [Fact]
    public void DatabaseGreeting_SameSeed_SameLineId_InEveryLanguage()
    {
        WithLines(OwnedLines(), () =>
        {
            for (int seed = 0; seed < 100; seed++)
            {
                var en = PickKeys("en", seed, NPCDialogueGenerator.PickGreeting);
                en.Should().ContainSingle().Which.Should().StartWith("npc_dialogue.");
                foreach (var lang in Langs.Skip(1))
                    PickKeys(lang, seed, NPCDialogueGenerator.PickGreeting).Should().Equal(en);
            }
        });
    }

    // ---------------------------------------------------------------- no English in hu

    /// <summary>Picks with no database lines (the generator's own templates), for many seeds, and every key used.</summary>
    private static List<(NPCDialogueGenerator.NpcLine Line, int Seed)> TemplateLines()
    {
        var result = new List<(NPCDialogueGenerator.NpcLine, int)>();
        for (int seed = 0; seed < 600; seed++)
        {
            NPCDialogueGenerator.Rng = new Random(seed);
            NPCDialogueDatabase.Hour = () => new[] { 3, 7, 13, 19, 23 }[seed % 5];
            var npc = Npc(seed);
            var hero = Hero(seed);
            result.Add((NPCDialogueGenerator.PickGreeting(npc, hero), seed));
            result.Add((NPCDialogueGenerator.PickFarewell(npc, hero), seed));
            result.Add((NPCDialogueGenerator.PickSmallTalk(npc, hero), seed));
        }
        NPCDialogueDatabase.Hour = () => 7;
        return result;
    }

    [Fact]
    public void GeneratedLines_InHungarian_HaveNoEnglishPiece()
    {
        WithLines(new List<NPCDialogueDatabase.DialogueLine>(), () =>
        {
            var lines = TemplateLines();
            var used = lines.SelectMany(l => l.Line.Keys).ToHashSet();
            foreach (var family in new[] { "greet.", "title.", "phrase.", "mod.", "memory.", "time.", "context.", "emote.", "farewell.", "farewell_add.", "topic.", "topic_line." })
                used.Should().Contain(k => k.StartsWith("npc_gen." + family), $"the walk reaches {family}");
            used.Should().Contain(k => k.Contains(".mod.") && k.EndsWith(".2"), "a comma suffix is joined");

            foreach (var (line, seed) in lines)
            {
                string hu = CombatEngine.InLanguage("hu", line.Render);
                hu.Should().NotContain("{").And.NotBeNullOrWhiteSpace();
                foreach (var key in line.Keys.Append("npc_gen.with_title"))
                    foreach (var seg in EnglishSegments(key))
                        if (!Loc.GetIn("hu", key).Contains(seg))
                            hu.Should().NotContain(seg, $"seed {seed}: no English from {key} in hu ({hu})");
            }
        });
    }

    [Fact]
    public void DatabaseLines_InHungarian_AreHungarian_WithNamesStandingAlone()
    {
        var player = new Player { Name1 = LongName, Name2 = LongName, Class = CharacterClass.Warrior };
        var npc = new NPC { Name1 = "Zz Shopkeeper", Name2 = "Zz Shopkeeper" };
        foreach (var hour in new[] { 3, 9, 15, 21 })
        {
            NPCDialogueDatabase.Hour = () => hour;
            foreach (var line in OwnedLines())
            {
                string en = CombatEngine.InLanguage("en", () => NPCDialogueDatabase.RenderLine(line, npc, player));
                string hu = CombatEngine.InLanguage("hu", () => NPCDialogueDatabase.RenderLine(line, npc, player));
                hu.Should().Be(string.Format(Loc.GetIn("hu", "npc_dialogue." + line.Id), LongName, "Zz Shopkeeper",
                    Loc.GetIn("hu", "npc_dialogue.ph.adventurer"), Loc.GetIn("hu", "npc_dialogue.tod." + NPCDialogueDatabase.TimeOfDay()),
                    CombatEngine.InLanguage("hu", () => player.ClassName), Loc.GetIn("hu", "npc_dialogue.tod_salute." + NPCDialogueDatabase.TimeOfDay()),
                    Loc.GetIn("hu", "npc_dialogue.tod_all." + NPCDialogueDatabase.TimeOfDay())));
                hu.Should().NotBe(en).And.NotContain("{");
                foreach (var seg in EnglishSegments("npc_dialogue." + line.Id).Where(s => s.Length >= 12))
                    hu.Should().NotContain(seg, line.Id);
                // a name is never glued to a suffix in hu
                Regex.IsMatch(hu, Regex.Escape(LongName) + @"(-|\w)").Should().BeFalse($"{line.Id}: {hu}");
                Regex.IsMatch(hu, "Zz Shopkeeper(-|\\w)").Should().BeFalse($"{line.Id}: {hu}");
            }
        }
        NPCDialogueDatabase.Hour = () => 7;
    }

    [Fact]
    public void Greeting_ClassAndTime_AreWrittenInTheReadersLanguage()
    {
        var player = new Player { Name1 = "Hero", Name2 = "Hero", Class = CharacterClass.MysticShaman };
        var line = DialogueLines_Greetings.GetLines().Single(l => l.Id == "ch_f2");
        string hu = CombatEngine.InLanguage("hu", () => NPCDialogueDatabase.RenderLine(line, new NPC { Name2 = "N" }, player));
        hu.Should().Contain(Loc.GetIn("hu", "class.mysticshaman") is var c && c != "class.mysticshaman" ? c : CombatEngine.InLanguage("hu", () => player.ClassName));
        hu.Should().NotContain("MysticShaman");

        NPCDialogueDatabase.Hour = () => 9;
        var cu = DialogueLines_Greetings.GetLines().Single(l => l.Id == "cu_l3");
        CombatEngine.InLanguage("en", () => NPCDialogueDatabase.RenderLine(cu, new NPC { Name2 = "N" }, player))
            .Should().StartWith("I've been watching the door all morning.");
        CombatEngine.InLanguage("hu", () => NPCDialogueDatabase.RenderLine(cu, new NPC { Name2 = "N" }, player))
            .Should().Contain(Loc.GetIn("hu", "npc_dialogue.tod_all.morning")).And.NotContain("morning");

        // the generator's {player_class} is the shown class name, not the enum
        WithLines(new List<NPCDialogueDatabase.DialogueLine>(), () =>
        {
            string text = CombatEngine.InLanguage("en", () => Loc.Get("npc_gen.greet.friendship.5", player.ClassName, "x"));
            text.Should().Be("There's my favorite " + CombatEngine.InLanguage("en", () => player.ClassName) + "!");
        });
    }

    [Fact]
    public void ShopMood_InHungarian_ComesFromTheHungarianKey()
    {
        WithLines(DialogueLines_MoodPrefixes.GetLines(), () =>
        {
            NPCDialogueGenerator.Rng = new Random(1);
            var npc = new NPC { Name1 = "Zz Smith", Name2 = "Zz Smith" };
            var player = new Player { Name1 = "Hero", Name2 = "Hero", Class = CharacterClass.Warrior, HP = 100, MaxHP = 100 };
            string hu = CombatEngine.InLanguage("hu", () => npc.GetMoodPrefix(player));
            var ids = NPCDialogueDatabase.GetRecentlyUsedIds("Zz Smith")!;
            hu.Should().Be(CombatEngine.InLanguage("hu", () => NPCDialogueDatabase.RenderLine(
                DialogueLines_MoodPrefixes.GetLines().Single(l => l.Id == ids.Last()), npc, player)));
            hu.Should().NotBe(CombatEngine.InLanguage("en", () => NPCDialogueDatabase.RenderLine(
                DialogueLines_MoodPrefixes.GetLines().Single(l => l.Id == ids.Last()), npc, player)));
        });
    }

    [Fact]
    public void ModdedLine_KeepsItsOwnText()
    {
        var modded = new NPCDialogueDatabase.DialogueLine { Id = "ag_m1", Text = "Modded hello, {player_name}.", Category = "greeting" };
        CombatEngine.InLanguage("hu", () => NPCDialogueDatabase.RenderLine(modded, new NPC { Name2 = "N" }, new Player { Name2 = "Hero" }))
            .Should().Be("Modded hello, Hero.");
    }

    [Theory]
    [InlineData("combat_defeat")]
    [InlineData("combat_flee")]
    [InlineData("ally_death")]
    [InlineData("gift_received")]
    [InlineData("insult")]
    [InlineData("compliment")]
    [InlineData("threat")]
    [InlineData("something_else")]
    public void FallbackReactions_AreWrittenInEachReadersLanguage(string eventType)
    {
        WithLines(new List<NPCDialogueDatabase.DialogueLine>(), () =>
        {
            for (int n = 0; n < 10; n++)
            {
                var npc = Npc(n);
                var leader = new Player { Name2 = "Leader" };
                var say = NPCDialogueGenerator.ReactionInLanguage(npc, leader, eventType);
                string key = NPCDialogueGenerator.FallbackReactionKey(npc, leader, eventType);
                foreach (var lang in Langs)
                    CombatEngine.InLanguage(lang, say).Should().Be(Loc.GetIn(lang, key));
                CombatEngine.InLanguage("hu", say).Should().NotBe(Loc.GetIn("en", key));
            }
        });
    }

    // ---------------------------------------------------------------- joining rules

    [Fact]
    public void JoiningRules_PerLanguage()
    {
        // en: a prefix ending in a comma lower-cases the line; "I" stays
        CombatEngine.InLanguage("en", () => NPCDialogueGenerator.Wrap("npc_gen.mod.high_aggression.1", "Greetings, traveler."))
            .Should().Be("Look, greetings, traveler.");
        CombatEngine.InLanguage("en", () => NPCDialogueGenerator.Wrap("npc_gen.mod.high_aggression.1", "I've been thinking of you..."))
            .Should().Be("Look, I've been thinking of you...");
        CombatEngine.InLanguage("en", () => NPCDialogueGenerator.Wrap("npc_gen.mod.high_aggression.1", "What NOW?"))
            .Should().Be("Look, what NOW?");
        // a sentence prefix keeps the capital
        CombatEngine.InLanguage("en", () => NPCDialogueGenerator.Wrap("npc_gen.mod.high_humor.1", "Hello there."))
            .Should().Be("Ha! Hello there.");
        // a comma suffix and a title go before the closing punctuation
        CombatEngine.InLanguage("en", () => NPCDialogueGenerator.Wrap("npc_gen.mod.low_aggression.2", "What brings you here?"))
            .Should().Be("What brings you here, if you don't mind?");
        CombatEngine.InLanguage("en", () => NPCDialogueGenerator.Wrap("npc_gen.with_title", "Hello there.", "Citizen"))
            .Should().Be("Hello there, Citizen.");
        CombatEngine.InLanguage("en", () => NPCDialogueGenerator.Wrap("npc_gen.with_title", "I've been thinking of you...", "mate"))
            .Should().Be("I've been thinking of you, mate...");
        // fr: the space before ? stays with the ?, after the suffix
        string fr = CombatEngine.InLanguage("fr", () => NPCDialogueGenerator.Wrap("npc_gen.mod.low_aggression.2", "Qu'est-ce qui vous amène ?"));
        fr.Should().StartWith("Qu'est-ce qui vous amène, ").And.EndWith(" ?").And.NotContain(" ?,");
        // es: the capital after an opening mark is lowered
        NPCDialogueGenerator.LowerFirst("¡Cariño!").Should().Be("¡cariño!");
        CombatEngine.InLanguage("es", () => NPCDialogueGenerator.Wrap("npc_gen.mod.high_aggression.1", "¿Qué te trae por aquí?"))
            .Should().EndWith("¿qué te trae por aquí?");
        // hu: a topic first in the sentence gets the sentence's capital
        WithLines(new List<NPCDialogueDatabase.DialogueLine>(), () =>
        {
            for (int seed = 0; seed < 40; seed++)
            {
                NPCDialogueGenerator.Rng = new Random(seed);
                var line = NPCDialogueGenerator.PickSmallTalk(new NPC { Name2 = "N", Archetype = "merchant" }, new Player { Name2 = "P" });
                foreach (var lang in Langs)
                {
                    string s = CombatEngine.InLanguage(lang, line.Render);
                    char first = s.First(char.IsLetter);
                    char.IsUpper(first).Should().BeTrue($"{lang}: {s}");
                }
            }
        });
    }

    [Fact]
    public void EmoteAndLine_AreFromTheSameLanguage()
    {
        WithLines(new List<NPCDialogueDatabase.DialogueLine>(), () =>
        {
            int checkedLines = 0;
            for (int seed = 0; seed < 300; seed++)
            {
                NPCDialogueGenerator.Rng = new Random(seed);
                var line = NPCDialogueGenerator.PickGreeting(Npc(seed), Hero(seed));
                string emote = line.Keys.FirstOrDefault(k => k.Contains(".emote."));
                if (emote == null) continue;
                foreach (var lang in Langs.Skip(1))
                {
                    string s = CombatEngine.InLanguage(lang, line.Render);
                    s.Should().StartWith(Loc.GetIn(lang, emote) + " ", $"{lang} emote first");
                    s.Should().NotContain(Loc.GetIn("en", emote));
                    s.Should().NotContain(Loc.GetIn("en", line.Keys[0]).Split(' ')[0] + " " + Loc.GetIn("en", line.Keys[0]).Split(' ').ElementAtOrDefault(1));
                }
                checkedLines++;
            }
            checkedLines.Should().BeGreaterThan(10);
        });
    }

    // ---------------------------------------------------------------- width

    [Fact]
    public void EveryRow_Fits79Columns_InFiveLanguages_WithA30CharacterName()
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        foreach (var lang in Langs)
        {
            string longestClass = Enum.GetValues<CharacterClass>()
                .Select(c => CombatEngine.InLanguage(lang, () => GameConfig.GetLocalizedClassName(c))).OrderByDescending(s => s.Length).First();
            string Longest(string prefix) => GenKeys().Where(k => k.StartsWith(prefix))
                .Select(k => Loc.GetIn(lang, k)).OrderByDescending(s => s.Length).First();
            string longestTitle = new[] { Longest("npc_gen.title."), Loc.GetIn(lang, "npc_dialogue.ph.majesty"), longestClass }
                .OrderByDescending(s => s.Length).First();
            // every generator piece fits one quoted row on its own, with its longest argument
            foreach (var key in GenKeys())
            {
                object[] args = key switch
                {
                    _ when key.StartsWith("npc_gen.memory.") => new object[] { Longest("npc_gen.time.") },
                    _ when key.StartsWith("npc_gen.topic_line.") => new object[] { Longest("npc_gen.topic.") },
                    _ when key == "npc_gen.with_title" => new object[] { "", "", "", longestTitle },
                    _ when key.StartsWith("npc_gen.mod.") || key.StartsWith("npc_gen.join") => new object[] { "", "", "" },
                    _ => new object[] { longestClass, longestTitle },
                };
                string text = CombatEngine.InLanguage(lang, () => Loc.Get(key, args));
                text.Length.Should().BeLessThanOrEqualTo(75, $"{key} in {lang}: {text}");
            }
            // database lines wrap into at most three rows
            var player = new Player { Name1 = LongName, Name2 = LongName, King = true };
            var npc = new NPC { Name1 = LongName, Name2 = LongName };
            foreach (var line in OwnedLines())
            {
                string text = CombatEngine.InLanguage(lang, () => NPCDialogueDatabase.RenderLine(line, npc, player));
                var rows = line.Category == "mood_prefix" ? NPCDialogueGenerator.NarrationRows(text) : NPCDialogueGenerator.QuotedRows(text);
                rows.Should().OnlyContain(r => r.Length <= 79, $"{line.Id} in {lang}");
                rows.Count.Should().BeLessThanOrEqualTo(3, $"{line.Id} in {lang}: {text}");
            }
        }
        // assembled lines, every branch, wrap within 79
        WithLines(new List<NPCDialogueDatabase.DialogueLine>(), () =>
        {
            foreach (var (line, seed) in TemplateLines())
                foreach (var lang in Langs)
                    NPCDialogueGenerator.QuotedRows(CombatEngine.InLanguage(lang, line.Render))
                        .Should().OnlyContain(r => r.Length <= 79, $"seed {seed} in {lang}");
        });
    }

    [Fact]
    public void QuotedRows_OpenAndCloseTheQuote()
    {
        var rows = NPCDialogueGenerator.QuotedRows(string.Join(" ", Enumerable.Repeat("word", 40)));
        rows.Count.Should().BeGreaterThan(1);
        rows[0].Should().StartWith("  \"word");
        rows.Skip(1).Should().OnlyContain(r => r.StartsWith("   word"));
        rows.Last().Should().EndWith("word\"");
        NPCDialogueGenerator.QuotedRows("Hello there.").Should().Equal("  \"Hello there.\"");
    }

    // ---------------------------------------------------------------- stored

    [Fact]
    public void StoredLineIds_AreLanguageNeutral_AndSurviveSaveAndReload()
    {
        WithLines(OwnedLines(), () =>
        {
            var byLang = new Dictionary<string, List<string>>();
            foreach (var lang in Langs)
            {
                NPCDialogueDatabase.ClearAllTracking();
                NPCDialogueGenerator.Rng = new Random(42);
                var npc = Npc(3);
                var hero = Hero(3);
                CombatEngine.InLanguage(lang, () =>
                {
                    for (int i = 0; i < 6; i++) NPCDialogueGenerator.GenerateGreeting(npc, hero);
                    npc.GetMoodPrefix(hero);
                    return 0;
                });
                byLang[lang] = NPCDialogueDatabase.GetRecentlyUsedIds(LongName)!.ToList();
            }
            var ids = byLang["en"];
            ids.Should().HaveCount(7);
            var known = OwnedLines().Select(l => l.Id).ToHashSet();
            ids.Should().OnlyContain(id => known.Contains(id), "only line ids are stored");
            foreach (var lang in Langs) byLang[lang].Should().Equal(ids, lang);

            // save and reload, the way SaveSystem writes NPCData
            var json = JsonSerializer.Serialize(new UsurperRemake.Systems.NPCData { RecentDialogueIds = ids.ToList() });
            NPCDialogueDatabase.ClearAllTracking();
            GameConfig.Language = "hu";
            var loaded = JsonSerializer.Deserialize<UsurperRemake.Systems.NPCData>(json)!;
            NPCDialogueDatabase.RestoreRecentlyUsedIds(LongName, loaded.RecentDialogueIds);
            NPCDialogueDatabase.GetRecentlyUsedIds(LongName).Should().Equal(ids);
            GameConfig.Language = "en";
        });
    }

    /// <summary>
    /// The greeting and mood lines exported for modders (dialogue.json) are byte-identical to 1.2.4's:
    /// the hash is of these two tables serialized as GameDataLoader writes them (the full export was
    /// compared with the base export by cmp, receipts/export-cmp.log).
    /// </summary>
    [Fact]
    public void ModderExport_OfTheseTables_IsUnchanged_InAnyLanguage()
    {
        foreach (var lang in new[] { "en", "hu" })
        {
            GameConfig.Language = lang;
            var dir = Path.Combine(Path.GetTempPath(), "npcdlg125-" + Guid.NewGuid().ToString("N"));
            try
            {
                GameDataLoader.ExportDefaults(dir);
                var all = JsonSerializer.Deserialize<List<NPCDialogueDatabase.DialogueLine>>(File.ReadAllText(Path.Combine(dir, "dialogue.json")),
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
                var ids = OwnedLines().Select(l => l.Id).ToHashSet();
                var mine = all.Where(l => ids.Contains(l.Id)).Select(l => l.Id + "\u0001" + l.Text).ToList();
                mine.Should().HaveCount(231);
                string hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n", mine))));
                hash.Should().Be(ExportHash, lang);
            }
            finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        }
        GameConfig.Language = "en";
    }

    private const string ExportHash = "AC39AEA72D8FECE6189E5EEE5E9E9E2451E02FC16988B8A82EAA2B2BF033B42E";
}
