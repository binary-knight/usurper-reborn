using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.UI;
using UsurperReborn.Tests.Localization;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.5: the DialogueSystem trees (the Mysterious Stranger and the seven Old Gods) are shown in the reader's
/// language. Node and choice text lives under keys built from the tree, node and choice ids; ids, conditions,
/// flags, recorded choices and the stored speaker stay English, so a tree walks the same way and saves the same
/// state in every language. Every row fits 79 columns in all five languages with a 30-character name.
/// </summary>
[Collection("SharedGameSingletons")]
public class DataDialogue125Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const int MaxWidth = 79;

    private static readonly string[] AllLanguages = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] OtherLanguages = { "es", "fr", "hu", "it" };

    // A name as long as a player name may be.
    private static readonly string LongName = "Wilhelmina Thornbury Ashcombe";
    private static string Name30 => LongName + new string('x', GameConfig.MaxNameLength - LongName.Length);

    // ---------- helpers ----------

    private static Dictionary<string, DialogueTree> Trees(DialogueSystem ds) =>
        (Dictionary<string, DialogueTree>)typeof(DialogueSystem).GetField("dialogueTrees", F)!.GetValue(ds)!;

    private static IEnumerable<(DialogueTree Tree, DialogueNode Node)> AllNodes(DialogueSystem ds) =>
        Trees(ds).Values.SelectMany(t => t.AllNodes.Values.Select(n => (t, n)));

    private static Dictionary<string, string> LoadLang(string lang) =>
        JsonSerializer.Deserialize<Dictionary<string, string>>(
            File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Localization", lang + ".json")))!;

    private static List<string> Tokens(string s) =>
        Regex.Matches(s, @"\{[A-Za-z]\w*\}").Select(m => m.Value).OrderBy(x => x, StringComparer.Ordinal).ToList();

    private static Character Hero() => new()
    {
        Name1 = "tester", Name2 = Name30, Class = CharacterClass.Warrior, Level = 100, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human, Gold = 1000, Chivalry = 1000, Darkness = 0,
    };

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

    /// <summary>Input that answers each choice menu as it is shown: the picker sees the node and the
    /// choices on offer and returns the menu number. After 60 answers it says nothing (0).</summary>
    private sealed class ChoiceStream : Stream
    {
        private readonly Func<string> _next;
        private byte[] _pending = Array.Empty<byte>();
        private int _pos, _count;
        public ChoiceStream(Func<string> next) { _next = next; }
        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_pos >= _pending.Length)
            {
                _pending = Encoding.UTF8.GetBytes((++_count > 60 ? "0" : _next()) + "\n");
                _pos = 0;
            }
            int n = Math.Min(count, _pending.Length - _pos);
            Array.Copy(_pending, _pos, buffer, offset, n);
            _pos += n;
            return n;
        }
        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken ct) => Task.FromResult(Read(buffer, offset, count));
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken ct = default)
        {
            var tmp = new byte[buffer.Length];
            int n = Read(tmp, 0, tmp.Length);
            tmp.AsMemory(0, n).CopyTo(buffer);
            return ValueTask.FromResult(n);
        }
        public override bool CanRead => true; public override bool CanSeek => false; public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private sealed record Run(string Text, List<string> Trail, List<string> Heard, string Saved, long Chivalry, long Darkness, long Experience,
        List<int> Offered);

    private static void ResetWorld(int receptivity, int encounters)
    {
        StoryProgressionSystem.Instance.FullReset();
        OceanPhilosophySystem.Instance.Reset();
        StrangerEncounterSystem.Instance.Reset();
        typeof(StrangerEncounterSystem).GetProperty("Receptivity")!.SetValue(StrangerEncounterSystem.Instance, receptivity);
        typeof(StrangerEncounterSystem).GetProperty("EncountersHad")!.SetValue(StrangerEncounterSystem.Instance, encounters);
    }

    /// <summary>The saved dialogue state: story flags, fragments, insights and moments, as the save writes them.</summary>
    private static string SavedState()
    {
        var data = SaveSystem.Instance.SerializeStorySystemsPublic();
        return JsonSerializer.Serialize(new
        {
            Flags = data.StoryFlags.OrderBy(k => k.Key, StringComparer.Ordinal).ToList(),
            data.CollectedFragments, data.OceanInsightIds, data.ExperiencedMoments, data.CollectedArtifacts,
        });
    }

    /// <summary>Walks a tree in a language. At the n-th menu the choice at <paramref name="path"/>[n] of those on
    /// offer is taken; past the end of the path, the first on offer.</summary>
    private static Run Walk(string lang, string treeId, IReadOnlyList<int> path, int receptivity = 0, int encounters = 0)
    {
        return InLang(lang, () =>
        {
            ResetWorld(receptivity, encounters);
            var ds = new DialogueSystem();
            var hero = Hero();
            var trail = new List<string>();
            var heard = new List<string>();
            var counts = new List<int>();
            ds.OnChoiceMade += (nodeId, choice) => heard.Add(nodeId + ":" + choice.Id);
            var output = new MemoryStream();
            TerminalEmulator term = null!;
            term = new TerminalEmulator(new ChoiceStream(() =>
            {
                var node = (DialogueNode)typeof(DialogueSystem).GetField("currentNode", F)!.GetValue(ds)!;
                var offered = (List<DialogueChoice>)typeof(DialogueSystem).GetMethod("GetAvailableChoices", F)!
                    .Invoke(ds, new object[] { node })!;
                int index = counts.Count < path.Count ? path[counts.Count] : 0;
                counts.Add(offered.Count);
                trail.Add(node.Id + ":" + offered[index].Id);
                return (index + 1).ToString();
            }), output);
            ds.StartDialogue(hero, treeId, term).GetAwaiter().GetResult();
            term.StreamWriterInternal?.Flush();
            var text = UIHelper.StripAnsi(Encoding.UTF8.GetString(output.ToArray()));
            return new Run(text, trail, heard, SavedState(), hero.Chivalry, hero.Darkness, hero.Experience, counts);
        });
    }

    // The world states a walk runs in: the Noctura intro offers its choices by the Stranger's receptivity
    // and encounters; the other trees ignore them.
    private static readonly (int Receptivity, int Encounters)[] States = { (0, 0), (-10, 0), (30, 0), (60, 0), (30, 3) };

    /// <summary>Every path through every tree in every world state, found by walking it in English: each menu's
    /// other choices start a new path.</summary>
    private static List<(string Tree, int[] Path, int Rec, int Enc)> Walks()
    {
        var walks = new List<(string, int[], int, int)>();
        var ds = new DialogueSystem();
        foreach (var tree in Trees(ds).Values)
        {
            var states = tree.Id == "noctura_encounter" ? States : States.Take(1).ToArray();
            foreach (var (rec, enc) in states)
            {
                var todo = new Queue<int[]>();
                todo.Enqueue(Array.Empty<int>());
                while (todo.Count > 0)
                {
                    var path = todo.Dequeue();
                    walks.Add((tree.Id, path, rec, enc));
                    var counts = Walk("en", tree.Id, path, rec, enc).Offered;
                    for (int step = path.Length; step < counts.Count; step++)
                        for (int alt = 1; alt < counts[step]; alt++)
                            todo.Enqueue(path.Concat(Enumerable.Repeat(0, step - path.Length)).Append(alt).ToArray());
                }
            }
        }
        return walks;
    }

    private static readonly Lazy<List<(string Tree, int[] Path, int Rec, int Enc)>> AllWalks = new(Walks);

    // ---------- 1. the keys ----------

    [Fact]
    public void EveryNodeAndChoice_HasItsKey_InFiveLanguages_WithRealTranslations()
    {
        var ds = new DialogueSystem();
        var langs = AllLanguages.ToDictionary(l => l, LoadLang);
        int nodes = 0, choices = 0;
        foreach (var (tree, node) in AllNodes(ds))
        {
            node.TreeId.Should().Be(tree.Id);
            var keys = new List<string> { DialogueSystem.NodeTextKey(tree.Id, node.Id) };
            keys.AddRange(node.Choices.Select(c => c.TextKey));
            nodes++;
            choices += node.Choices.Count;
            node.Choices.Select(c => c.Id).Should().OnlyHaveUniqueItems($"{node.Id} has one id per choice");
            foreach (var c in node.Choices) c.Id.Should().MatchRegex("^choice_[0-9]+$", "a choice id is a stable token");
            foreach (var key in keys)
            {
                var en = langs["en"].GetValueOrDefault(key);
                en.Should().NotBeNullOrWhiteSpace($"{key} is in en.json");
                foreach (var lang in OtherLanguages)
                {
                    var text = langs[lang].GetValueOrDefault(key);
                    text.Should().NotBeNullOrWhiteSpace($"{key} is in {lang}.json");
                    text.Should().NotBe(en, $"{key} is translated in {lang}");
                    Tokens(text!).Should().Equal(Tokens(en!), $"{key} in {lang} keeps the placeholders of English");
                    text!.Split('\n').Count(l => l.Trim().Length > 0).Should().BeGreaterThan(0);
                }
            }
        }
        nodes.Should().Be(58);
        choices.Should().Be(58);
    }

    [Fact]
    public void NoTwoNodesOrChoices_ShareAKey_AndEveryTreeKeyIsUsed()
    {
        var ds = new DialogueSystem();
        var keys = new List<string>();
        foreach (var (tree, node) in AllNodes(ds))
        {
            keys.Add(DialogueSystem.NodeTextKey(node.TreeId, node.Id));
            keys.AddRange(node.Choices.Select(c => c.TextKey));
        }
        keys.Should().OnlyHaveUniqueItems("each node and choice reads its own key");
        keys.Should().HaveCount(116);
        var treePrefixes = Trees(ds).Keys.Select(t => "dialogue." + t + ".").ToList();
        var inFile = LoadLang("en").Keys.Where(k => treePrefixes.Any(p => k.StartsWith(p, StringComparison.Ordinal))).ToList();
        inFile.Should().BeEquivalentTo(keys, "every tree key in en.json belongs to one node or choice");
    }

    [Fact]
    public void Keys_AreBuiltFromTheIds_NotTheText()
    {
        DialogueSystem.NodeTextKey("manwe_encounter", "manwe_alliance").Should().Be("dialogue.manwe_encounter.manwe_alliance.text");
        DialogueSystem.ChoiceKey("mysterious_stranger_intro", "stranger_approach", "choice_3")
            .Should().Be("dialogue.mysterious_stranger_intro.stranger_approach.choice_3");
        var ds = new DialogueSystem();
        var intro = Trees(ds)["mysterious_stranger_intro"].AllNodes["stranger_approach"];
        intro.Choices[2].TextKey.Should().Be("dialogue.mysterious_stranger_intro.stranger_approach.choice_3");
        Loc.GetIn("en", intro.Choices[2].TextKey).Should().Be("I bow to no gods, old or new. Speak plainly!");
        Loc.GetIn("hu", intro.Choices[2].TextKey).Should().Be("Nem hajolok meg semmilyen isten előtt, se régi, se új. Beszélj világosan!");
        Loc.GetIn("en", DialogueSystem.NodeTextKey("mysterious_stranger_intro", "stranger_approach")).Split('\n')[0]
            .Should().Be("A cloaked figure emerges from the shadows, their face hidden.");
    }

    [Fact]
    public void TheSourceHoldsNoNodeOrChoiceText()
    {
        var src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "DialogueSystem.cs"));
        src.Should().NotContain("Text = new[]");
        src.Should().NotContain("I bow to no gods");
        Regex.Matches(src, "Id = \"choice_[0-9]+\"").Count.Should().Be(58);
    }

    // ---------- 2. walking the trees ----------

    [Fact]
    public void EveryTree_WalksTheSameNodes_AndSavesTheSameState_InEveryLanguage()
    {
        var covered = new HashSet<string>();
        int runs = 0;
        foreach (var (tree, path, rec, enc) in AllWalks.Value)
        {
            var en = Walk("en", tree, path, rec, enc);
            covered.UnionWith(en.Trail);
            en.Trail.Should().NotBeEmpty($"{tree} offers a choice");
            en.Heard.Should().Equal(en.Trail, "the choice listener hears every node and choice id");
            foreach (var lang in OtherLanguages)
            {
                var other = Walk(lang, tree, path, rec, enc);
                other.Trail.Should().Equal(en.Trail, $"{tree} at {string.Join(",", path)} in {lang} takes the same path");
                other.Heard.Should().Equal(en.Heard, $"{tree} in {lang}: the listener hears the same ids");
                other.Saved.Should().Be(en.Saved, $"{tree} at {string.Join(",", path)} in {lang} saves the same state");
                (other.Chivalry, other.Darkness, other.Experience).Should().Be((en.Chivalry, en.Darkness, en.Experience));
                runs++;
            }
        }
        var ds = new DialogueSystem();
        var all = AllNodes(ds).SelectMany(x => x.Node.Choices.Select(c => x.Node.Id + ":" + c.Id)).ToList();
        all.Except(covered).Should().BeEmpty("every choice of every tree is taken by some walk");
        runs.Should().BeGreaterThan(200);
    }

    [Fact]
    public void EveryTree_InHungarian_ShowsNoEnglishFromTheTables()
    {
        var en = LoadLang("en");
        var hu = LoadLang("hu");
        var ds = new DialogueSystem();
        var english = new HashSet<string>();
        foreach (var (tree, node) in AllNodes(ds))
            foreach (var key in node.Choices.Select(c => c.TextKey).Append(DialogueSystem.NodeTextKey(tree.Id, node.Id)))
                foreach (var line in en[key].Split('\n').Select(l => l.Replace("{PlayerName}", Name30).Trim()))
                    if (line.Length >= 10 && !hu.Values.Any(v => v.Contains(line, StringComparison.Ordinal)))
                        english.Add(line);
        english.Should().HaveCountGreaterThan(400);

        var seen = new StringBuilder();
        foreach (var (tree, path, rec, enc) in AllWalks.Value)
        {
            var run = Walk("hu", tree, path, rec, enc);
            seen.AppendLine(run.Text);
            foreach (var line in english)
                run.Text.Should().NotContain(line, $"{tree} at {string.Join(",", path)} shows only Hungarian");
        }
        var text = seen.ToString();
        text.Should().Contain("[Rejtélyes Idegen]", "the Stranger's title is translated");
        text.Should().Contain("[Noctura]", "a god's name stays as it is");
        text.Should().Contain(Loc.GetIn("hu", "dialogue.effect_item", "Ősi Vaskulcs"));
        text.Should().Contain(Loc.GetIn("hu", "dialogue.effect_item", "Árnyék Köpeny"));
        text.Should().Contain("Á... " + Name30 + ". Már vártalak.");
        text.Should().NotContain("Mysterious Stranger").And.NotContain("Ancient Iron Key").And.NotContain("Shadow Cloak");
    }

    [Fact]
    public void EveryShownRow_FitsTheWidth_InFiveLanguages()
    {
        foreach (var lang in AllLanguages)
            foreach (var (tree, path, rec, enc) in AllWalks.Value)
            {
                var run = Walk(lang, tree, path, rec, enc);
                foreach (var row in run.Text.Replace("\r", "").Split('\n'))
                    row.Length.Should().BeLessOrEqualTo(MaxWidth, $"{lang} {tree}: \"{row}\"");
            }
    }

    // ---------- 3. width ----------

    [Fact]
    public void EveryNodeAndChoiceRow_FitsWithoutWrapping_InFiveLanguages_WithA30CharacterName()
    {
        GameConfig.MaxNameLength.Should().Be(30);
        Name30.Length.Should().Be(GameConfig.MaxNameLength);
        var ds = new DialogueSystem();
        var over = new List<string>();
        foreach (var lang in AllLanguages)
            foreach (var (tree, node) in AllNodes(ds))
            {
                foreach (var line in Loc.GetIn(lang, DialogueSystem.NodeTextKey(tree.Id, node.Id)).Split('\n'))
                {
                    var row = line.Replace("{PlayerName}", Name30);
                    if (row.Length > MaxWidth) over.Add($"{lang} {node.Id} {row.Length}: {row}");
                }
                for (int i = 0; i < node.Choices.Count; i++)
                {
                    var row = $"[{i + 1}] " + Loc.GetIn(lang, node.Choices[i].TextKey);
                    if (row.Length > MaxWidth) over.Add($"{lang} {node.Choices[i].TextKey} {row.Length}: {row}");
                }
                var speaker = InLang(lang, () => "[" + DialogueSystem.SpeakerLabel(node.Speaker) + "]");
                if (speaker.Length > MaxWidth) over.Add($"{lang} speaker {speaker}");
            }
        over.Should().BeEmpty();
    }

    [Fact]
    public void ALongRow_IsWrappedAtSpaces_WithTheChoiceIndent()
    {
        var words = string.Join(" ", Enumerable.Repeat("szövetségesedként", 12));
        var rows = DialogueSystem.FitRows("[2] " + words, "    ");
        rows.Should().HaveCountGreaterThan(1);
        rows.Should().OnlyContain(r => r.Length <= MaxWidth);
        rows.Skip(1).Should().OnlyContain(r => r.StartsWith("    ", StringComparison.Ordinal) && r[4] != ' ');
        string.Join(" ", rows.Select(r => r.Trim())).Should().Be("[2] " + words);
        DialogueSystem.FitRows("  two  spaces kept", "").Should().Equal("  two  spaces kept");
    }

    // ---------- 4. Electron ----------

    [Fact]
    public void Electron_SendsTheReadersText_AndKeepsTheIds()
    {
        var ds = new DialogueSystem();
        var node = Trees(ds)["mysterious_stranger_intro"].AllNodes["stranger_approach"];
        typeof(DialogueSystem).GetField("currentPlayer", F)!.SetValue(ds, Hero());
        var oldOut = Console.Out;
        bool electron = GameConfig.ElectronMode;
        var sw = new StringWriter();
        try
        {
            GameConfig.ElectronMode = true;
            Console.SetOut(sw);
            InLang("hu", () => typeof(DialogueSystem).GetMethod("EmitDialogueNode", F)!
                .Invoke(ds, new object[] { node, node.Choices, false }));
        }
        finally { Console.SetOut(oldOut); GameConfig.ElectronMode = electron; }

        var raw = sw.ToString();
        int start = raw.IndexOf("usurper:{\"e\":\"dialogue\"", StringComparison.Ordinal);
        start.Should().BeGreaterThan(-1, "a dialogue event was sent");
        var json = raw.Substring(start + "usurper:".Length);
        json = json.Substring(0, json.IndexOf('\a'));
        var d = JsonDocument.Parse(json).RootElement.GetProperty("d");
        d.GetProperty("speaker").GetString().Should().Be("Rejtélyes Idegen");
        d.GetProperty("portraitKey").GetString().Should().Be("npc:Mysterious Stranger", "the portrait id stays English");
        d.GetProperty("text").GetString().Should().Be(Loc.GetIn("hu", DialogueSystem.NodeTextKey("mysterious_stranger_intro", "stranger_approach"))
            .Replace("{PlayerName}", Name30));
        var choices = d.GetProperty("choices").EnumerateArray().ToList();
        choices.Select(c => c.GetProperty("key").GetString()).Should().Equal("1", "2", "3", "4", "0");
        for (int i = 0; i < 4; i++)
            choices[i].GetProperty("text").GetString().Should().Be(Loc.GetIn("hu", node.Choices[i].TextKey));
    }

    // ---------- 5. saves ----------

    [Fact]
    public void TheSavedDialogueState_IsTheSame_AfterSaveAndReload_InEveryLanguage()
    {
        string? enJson = null;
        foreach (var lang in AllLanguages)
        {
            var run = Walk(lang, "mysterious_stranger_intro", new[] { 2 });
            run.Trail.Should().StartWith("stranger_approach:choice_3");
            var data = SaveSystem.Instance.SerializeStorySystemsPublic();
            var json = JsonSerializer.Serialize(data.StoryFlags.OrderBy(k => k.Key, StringComparer.Ordinal));
            enJson ??= json;
            json.Should().Be(enJson, $"{lang} saves the flags English saves");

            var back = JsonSerializer.Deserialize<StorySystemsData>(JsonSerializer.Serialize(data))!;
            StoryProgressionSystem.Instance.FullReset();
            SaveSystem.Instance.RestoreStorySystems(back);
            var story = StoryProgressionSystem.Instance;
            story.HasStoryFlag("defiant_to_stranger").Should().BeTrue("the defiant answer's node sets it");
            story.HasStoryFlag("met_mysterious_stranger").Should().BeTrue();
            story.HasStoryFlag("has_ancient_key").Should().BeTrue();
            story.ExportStringFlags().Keys.Should().OnlyContain(k => Regex.IsMatch(k, "^[a-z0-9_]+$"), $"{lang}: flags are ids");
        }

        // The recorded choice and the speaker are stored as English ids.
        foreach (var lang in AllLanguages)
        {
            Walk(lang, "mysterious_stranger_intro", Array.Empty<int>());
            StoryProgressionSystem.Instance.MajorChoices["stranger_intro"].SelectedOption.Should().Be("completed", lang);
        }
    }

    [Fact]
    public void ANodePassedOnTheWay_AppliesItsEffects_SoTheStrangerAnswersSetTheirFlags()
    {
        foreach (var lang in AllLanguages)
        {
            Walk(lang, "mysterious_stranger_intro", new[] { 2 });
            StoryProgressionSystem.Instance.HasStoryFlag("defiant_to_stranger").Should().BeTrue($"{lang}: the defiant answer");
            StoryProgressionSystem.Instance.HasStoryFlag("willing_hero").Should().BeFalse(lang);

            Walk(lang, "mysterious_stranger_intro", new[] { 3 });
            StoryProgressionSystem.Instance.HasStoryFlag("willing_hero").Should().BeTrue($"{lang}: the willing answer");
            StoryProgressionSystem.Instance.HasStoryFlag("defiant_to_stranger").Should().BeFalse(lang);

            Walk(lang, "mysterious_stranger_intro", new[] { 0 });
            StoryProgressionSystem.Instance.HasStoryFlag("willing_hero").Should().BeFalse(lang);
            StoryProgressionSystem.Instance.HasStoryFlag("defiant_to_stranger").Should().BeFalse(lang);
            StoryProgressionSystem.Instance.HasStoryFlag("met_mysterious_stranger").Should().BeTrue(lang);
        }
    }

    [Fact]
    public void TheStoredSpeakerAndRewards_StayEnglish_AndShowTranslated()
    {
        var ds = new DialogueSystem();
        var nodes = AllNodes(ds).Select(x => x.Node).ToList();
        nodes.Select(n => n.Speaker).Distinct().Should().BeEquivalentTo(
            "Mysterious Stranger", "Maelketh", "Veloura", "Thorgrim", "Noctura", "Aurelion", "Terravok", "Manwe");
        var gifts = nodes.SelectMany(n => n.Effects.Concat(n.Choices.SelectMany(c => c.Effects)))
            .Where(e => e.Type == EffectType.GiveItem).Select(e => e.StringValue).Distinct().ToList();
        gifts.Should().BeEquivalentTo("Ancient Iron Key", "Shadow Cloak");
        foreach (var lang in AllLanguages)
        {
            InLang(lang, () => DialogueSystem.SpeakerLabel("Mysterious Stranger")).Should().Be(Loc.GetIn(lang, "dialogue.speaker.mysterious_stranger"));
            InLang(lang, () => DialogueSystem.SpeakerLabel("Noctura")).Should().Be("Noctura");
            InLang(lang, () => DialogueSystem.RewardName("Ancient Iron Key")).Should().Be(Loc.GetIn(lang, "dialogue.reward.ancient_iron_key"));
            InLang(lang, () => DialogueSystem.RewardName("Shadow Cloak")).Should().Be(Loc.GetIn(lang, "item.shadow_cloak"));
            if (lang != "en")
            {
                Loc.GetIn(lang, "dialogue.speaker.mysterious_stranger").Should().NotBe("Mysterious Stranger");
                Loc.GetIn(lang, "dialogue.reward.ancient_iron_key").Should().NotBe("Ancient Iron Key");
            }
        }
    }

    [Fact]
    public void TheWikiExporter_DoesNotReadTheDialogueTrees()
    {
        var src = File.ReadAllText(Path.Combine(HardcodedTextScannerTests.RepoRoot(), "Scripts", "Systems", "WikiDataExporter.cs"));
        src.Should().NotContain("DialogueSystem").And.NotContain("DialogueTree").And.NotContain("dialogue.");
    }
}
