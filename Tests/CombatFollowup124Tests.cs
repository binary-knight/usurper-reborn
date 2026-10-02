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
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.4: an NPC ally's victory reaction that falls back (no dialogue line fits) reaches each group
/// member in their own language, like the dialogue lines do.
/// </summary>
[Collection("SharedGameSingletons")]
public class CombatFollowup124Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    /// <summary>A 30 character name, the longest a player can take.</summary>
    private const string LongName = "Eszterhazy Kalandor Bajnoka Ur";

    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    private static readonly string[] FallbackKeys =
    {
        "npc_dialogue.rx_cv_fb_aggressive", "npc_dialogue.rx_cv_fb_brave",
        "npc_dialogue.rx_cv_fb_social", "npc_dialogue.rx_cv_fb_plain",
    };

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static List<string> Rows(string text) => text.Replace("\r", "").TrimEnd('\n').Split('\n').ToList();

    private static string Joined(IEnumerable<string> rows) => string.Join(" ", rows.Select(r => r.Trim()));

    private static NPC Ally(string name, float aggression, float courage, float sociability)
    {
        var npc = new NPC { Name1 = name, Name2 = name, Level = 20, HP = 300, MaxHP = 400, BaseMaxHP = 400 };
        var profile = PersonalityProfile.GenerateForArchetype("commoner");
        profile.Aggression = aggression;
        profile.Courage = courage;
        profile.Sociability = sociability;
        npc.Personality = profile;
        npc.Brain = new NPCBrain(npc, profile);
        npc.EmotionalState = npc.Brain.Emotions;
        return npc;
    }

    private static readonly FieldInfo AllLines = typeof(NPCDialogueDatabase).GetField("_allLines", BindingFlags.NonPublic | BindingFlags.Static)!;

    /// <summary>Runs `body` with a dialogue database that has no reaction lines, so every reaction falls back.</summary>
    private static void WithNoReactionLines(Action body)
    {
        NPCDialogueDatabase.Initialize();
        var prev = AllLines.GetValue(null);
        try
        {
            AllLines.SetValue(null, NPCDialogueDatabase.GetAllBuiltInLines().Where(l => l.Category != "reaction").ToList());
            body();
        }
        finally { AllLines.SetValue(null, prev); }
    }

    [Fact]
    public void FallbackVictoryReaction_LeaderEn_FollowersHuAndFr_EachReadTheirOwn()
    {
        var prevSink = CombatEngine.GroupBroadcastSink;
        var prevLang = GameConfig.Language;
        try
        {
            WithNoReactionLines(() =>
            {
                GameConfig.Language = "en";   // the leader
                var sent = new List<Func<string, string>>();
                CombatEngine.GroupBroadcastSink = (_, build) => sent.Add(build);
                var output = new MemoryStream();
                var term = new TerminalEmulator(new MemoryStream(), output);
                var engine = new CombatEngine(term);
                var leader = new Player { Name1 = "Leader", Name2 = "Leader", AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human };
                var ally = Ally("Npcr Ally", 0.9f, 0.5f, 0.5f);
                NPCDialogueGenerator.CombatVictoryFallbackKey(ally.Personality).Should().Be("npc_dialogue.rx_cv_fb_aggressive");
                var result = new CombatResult { Player = leader, Teammates = new List<Character> { ally } };

                typeof(CombatEngine).GetMethod("ShowVictoryReactions", F)!.Invoke(engine, new object[] { result });

                sent.Should().HaveCount(1, "one broadcast, built for each reader");
                string key = "npc_dialogue.rx_cv_fb_aggressive";
                string en = Loc.GetIn("en", key);
                foreach (var lang in new[] { "hu", "fr" })
                {
                    string body = Loc.GetIn(lang, key);
                    body.Should().NotBe(en, $"{key} has its own {lang} text");
                    string text = Strip(sent[0](lang));
                    Rows(text).Should().Equal(CombatEngine.NpcReactionRows(lang, "Npcr Ally", body), $"the {lang} follower reads it in {lang}");
                    Joined(Rows(text)).Should().NotContain(en, $"no English reaction for the {lang} follower");
                }
                Rows(Strip(sent[0]("en"))).Should().Equal(CombatEngine.NpcReactionRows("en", "Npcr Ally", en));
                GameConfig.Language.Should().Be("en", "building for the followers leaves the leader's language");

                term.StreamWriterInternal?.Flush();
                string shown = Strip(Encoding.UTF8.GetString(output.ToArray()));
                foreach (var row in CombatEngine.NpcReactionRows("en", "Npcr Ally", en))
                    shown.Should().Contain(row, "the leader reads it in English");
            });
        }
        finally { CombatEngine.GroupBroadcastSink = prevSink; GameConfig.Language = prevLang; }
    }

    [Theory]
    [InlineData(0.9f, 0.5f, 0.5f, "npc_dialogue.rx_cv_fb_aggressive")]
    [InlineData(0.5f, 0.9f, 0.5f, "npc_dialogue.rx_cv_fb_brave")]
    [InlineData(0.5f, 0.5f, 0.9f, "npc_dialogue.rx_cv_fb_social")]
    [InlineData(0.5f, 0.5f, 0.5f, "npc_dialogue.rx_cv_fb_plain")]
    public void EachFallbackLine_IsWrittenInTheReadersLanguage(float aggression, float courage, float sociability, string key)
    {
        WithNoReactionLines(() =>
        {
            var ally = Ally("Npcr Ally", aggression, courage, sociability);
            var say = NPCDialogueGenerator.ReactionInLanguage(ally, new Player { Name2 = "Leader" }, "combat_victory");
            foreach (var lang in Langs)
                CombatEngine.InLanguage(lang, say).Should().Be(Loc.GetIn(lang, key), $"{key} in {lang}");
            CombatEngine.InLanguage("hu", say).Should().NotBe(Loc.GetIn("en", key));
        });
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    [InlineData("fr")]
    [InlineData("es")]
    [InlineData("it")]
    public void EveryFallbackReaction_Fits79Columns_WithA30CharacterName(string lang)
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        foreach (var key in FallbackKeys)
        {
            Loc.HasIn(lang, key).Should().BeTrue($"{key} in {lang}");
            string body = Loc.GetIn(lang, key);
            var rows = CombatEngine.NpcReactionRows(lang, LongName, body);
            foreach (var row in rows)
                row.Length.Should().BeLessThanOrEqualTo(79, $"{lang}: \"{row}\"");
            Joined(rows).Should().Be(Loc.GetIn(lang, "combat.npc_reaction", LongName, body).Trim(), "wrapping loses no word");
        }
    }
}
