using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Editor;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.4: the editor's Export Defaults prompt counts the files the export writes, and an NPC ally's
/// victory reaction in group combat reaches each member in their own language.
/// </summary>
[Collection("SharedGameSingletons")]
public class NpcReactions124Tests
{
    [Fact]
    public void ExportDefaultsPrompt_CountsTheFilesTheExportWrites()
    {
        var m = Regex.Match(EditorMain.ExportPrompt, @"Write all (\d+) default JSON files");
        m.Success.Should().BeTrue(EditorMain.ExportPrompt);
        int promised = int.Parse(m.Groups[1].Value);

        var dir = Path.Combine(Path.GetTempPath(), "npcr124-export-" + Guid.NewGuid().ToString("N"));
        try
        {
            GameDataLoader.ExportDefaults(dir);
            var written = Directory.GetFiles(dir, "*.json");
            written.Should().HaveCount(GameDataLoader.DefaultExports.Count);
            promised.Should().Be(written.Length, "the prompt names the number of files written");
            foreach (var f in written) new FileInfo(f).Length.Should().BeGreaterThan(2, Path.GetFileName(f));
        }
        finally { if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }

    // ---------- NPC ally victory reactions ----------

    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>A 30 character name, the longest a player can take.</summary>
    private const string LongName = "Eszterhazy Kalandor Bajnoka Ur";

    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static List<string> Rows(string text) => text.Replace("\r", "").TrimEnd('\n').Split('\n').ToList();

    private static string Joined(IEnumerable<string> rows) => string.Join(" ", rows.Select(r => r.Trim()));

    private static List<NPCDialogueDatabase.DialogueLine> VictoryLines() => NPCDialogueDatabase.GetAllBuiltInLines()
        .Where(l => l.Category == "reaction" && l.EventType == "combat_victory").ToList();

    private static NPC Ally(string name)
    {
        var npc = new NPC { Name1 = name, Name2 = name, Level = 20, HP = 300, MaxHP = 400, BaseMaxHP = 400 };
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        npc.EmotionalState = npc.Brain.Emotions;
        return npc;
    }

    [Fact]
    public void VictoryReaction_LeaderEn_FollowersHuAndFr_EachReadTheirOwn()
    {
        var prevSink = CombatEngine.GroupBroadcastSink;
        var prevLang = GameConfig.Language;
        try
        {
            GameConfig.Language = "en";   // the leader
            var sent = new List<Func<string, string>>();
            CombatEngine.GroupBroadcastSink = (_, build) => sent.Add(build);
            var output = new MemoryStream();
            var term = new TerminalEmulator(new MemoryStream(), output);
            var engine = new CombatEngine(term);
            var leader = new Player { Name1 = "Leader", Name2 = "Leader", AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human };
            var result = new CombatResult { Player = leader, Teammates = new List<Character> { Ally("Npcr Ally") } };

            typeof(CombatEngine).GetMethod("ShowVictoryReactions", F)!.Invoke(engine, new object[] { result });

            sent.Should().HaveCount(1, "one broadcast, built for each reader");
            // the reaction is picked once; read it back from the English rows and find its line
            string en = Joined(Rows(Strip(sent[0]("en"))));
            var m = Regex.Match(en, "^Npcr Ally: \"(.*)\"$");
            m.Success.Should().BeTrue(en);
            var line = VictoryLines().SingleOrDefault(l => l.Text.Replace("{player_name}", "Leader") == m.Groups[1].Value);
            line.Should().NotBeNull($"the reaction is a built-in victory line: {m.Groups[1].Value}");
            string key = "npc_dialogue." + line!.Id;

            foreach (var lang in new[] { "hu", "fr" })
            {
                string body = Loc.GetIn(lang, key, "Leader");
                body.Should().NotBe(Loc.GetIn("en", key, "Leader"), $"{key} has its own {lang} text");
                string text = Strip(sent[0](lang));
                Rows(text).Should().Equal(CombatEngine.NpcReactionRows(lang, "Npcr Ally", body), $"the {lang} follower reads it in {lang}");
                Joined(Rows(text)).Should().NotContain(m.Groups[1].Value, $"no English reaction for the {lang} follower");
            }
            Strip(sent[0]("fr")).Should().StartWith("  Npcr Ally : \"", "the French row has its own spacing");
            GameConfig.Language.Should().Be("en", "building for the followers leaves the leader's language");

            term.StreamWriterInternal?.Flush();
            string shown = Strip(Encoding.UTF8.GetString(output.ToArray()));
            foreach (var row in CombatEngine.NpcReactionRows("en", "Npcr Ally", m.Groups[1].Value))
                shown.Should().Contain(row, "the leader reads it in English");
        }
        finally { CombatEngine.GroupBroadcastSink = prevSink; GameConfig.Language = prevLang; }
    }

    [Fact]
    public void ADeadAlly_SaysNothing_AndNothingIsSent()
    {
        var prevSink = CombatEngine.GroupBroadcastSink;
        try
        {
            var sent = new List<Func<string, string>>();
            CombatEngine.GroupBroadcastSink = (_, build) => sent.Add(build);
            var engine = new CombatEngine(new TerminalEmulator(new MemoryStream(), new MemoryStream()));
            var ally = Ally("Npcr Fallen");
            ally.HP = 0;
            var result = new CombatResult { Player = new Player { Name2 = "Leader" }, Teammates = new List<Character> { ally } };
            typeof(CombatEngine).GetMethod("ShowVictoryReactions", F)!.Invoke(engine, new object[] { result });
            sent.Should().BeEmpty();
        }
        finally { CombatEngine.GroupBroadcastSink = prevSink; }
    }

    [Fact]
    public void EveryVictoryLine_HasItsTextInEachLanguage_AndEnglishMatchesTheData()
    {
        var leader = new Player { Name1 = "Leader", Name2 = "Leader" };
        var npc = Ally("Npcr Ally");
        var lines = VictoryLines();
        lines.Should().HaveCount(18);
        foreach (var line in lines)
        {
            string key = "npc_dialogue." + line.Id;
            foreach (var lang in Langs)
            {
                Loc.HasIn(lang, key).Should().BeTrue($"{key} in {lang}");
                CombatEngine.InLanguage(lang, () => NPCDialogueDatabase.RenderLine(line, npc, leader))
                    .Should().Be(Loc.GetIn(lang, key, "Leader"), $"{key} in {lang}");
            }
            CombatEngine.InLanguage("en", () => NPCDialogueDatabase.RenderLine(line, npc, leader))
                .Should().Be(line.Text.Replace("{player_name}", "Leader"), "the English text is the data's own");
        }
    }

    [Fact]
    public void AVictoryLineAModChanged_KeepsTheModsText()
    {
        var modded = new NPCDialogueDatabase.DialogueLine { Id = "rx_cv_st2", Text = "Npcr modded words.", Category = "reaction", EventType = "combat_victory" };
        CombatEngine.InLanguage("hu", () => NPCDialogueDatabase.RenderLine(modded, Ally("Npcr Ally"), new Player { Name2 = "Leader" }))
            .Should().Be("Npcr modded words.");
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("it")]
    public void EveryVictoryReaction_Fits79Columns_WithA30CharacterName(string lang)
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        foreach (var line in VictoryLines())
        {
            string body = Loc.GetIn(lang, "npc_dialogue." + line.Id, LongName);
            var rows = CombatEngine.NpcReactionRows(lang, LongName, body);
            foreach (var row in rows)
                row.Length.Should().BeLessThanOrEqualTo(79, $"{lang}: \"{row}\"");
            Joined(rows).Should().Be(Loc.GetIn(lang, "combat.npc_reaction", LongName, body).Trim(), "wrapping loses no word");
        }
        VictoryLines().Max(l => Loc.GetIn(lang, "npc_dialogue." + l.Id, LongName).Length + LongName.Length + 6)
            .Should().BeGreaterThan(79, "the longest reaction needs wrapping");
    }
}
