using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using UsurperRemake.Data;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.10 story picks (P1, P2, P3). The four floor paradoxes wait for a seal, an artifact or a god's fate instead
/// of a chapter, and their floors offer them on any entry; waking Terravok leaves him to be met on floor 95.
/// Manwe's last answer weighs the ending (walk away: Defiant; take the burden: Savior, or Usurper when the
/// Usurper test holds), and the alliance sets manwe_saved. The ending screen gives one line for each paradox
/// answer and for Mira's choice of mercy. State is built in memory; every swapped singleton is put back.
/// </summary>
[Collection("SharedGameSingletons")]
public class StoryPicks1310Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static readonly Type[] Swapped =
    {
        typeof(StoryProgressionSystem), typeof(OceanPhilosophySystem), typeof(GriefSystem), typeof(CycleSystem),
        typeof(MoralParadoxSystem), typeof(CompanionSystem), typeof(AmnesiaSystem),
    };
    private readonly List<(FieldInfo Field, object? Old)> _old;
    private readonly string _language = GameConfig.Language;

    public StoryPicks1310Tests()
    {
        GameConfig.Language = "en";
        _old = Swapped.Select(t => t.GetField("_fallbackInstance", SNP)!).Select(f => (f, f.GetValue(null))).ToList();
        foreach (var t in Swapped)
            t.GetField("_fallbackInstance", SNP)!.SetValue(null, Activator.CreateInstance(t));
    }

    public void Dispose()
    {
        foreach (var (f, v) in _old) f.SetValue(null, v);
        GameConfig.Language = _language;
    }

    // ---------- helpers ----------

    private static StoryProgressionSystem Story => StoryProgressionSystem.Instance;
    private static MoralParadoxSystem Moral => MoralParadoxSystem.Instance;

    private static Character Hero(long chivalry = 0, long darkness = 0) => new()
    {
        Name1 = "PicksHero", Name2 = "PicksHero", Class = CharacterClass.Warrior, Level = 100, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human, Chivalry = chivalry, Darkness = darkness, Wisdom = 40, BaseWisdom = 40,
    };

    private static void LyrisTravels()
    {
        var lyris = CompanionSystem.Instance.GetCompanion(CompanionId.Lyris)!;
        lyris.IsRecruited = true;
        lyris.IsDead = false;
    }

    private static string Src(params string[] path) =>
        File.ReadAllText(Path.Combine(new[] { UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot() }.Concat(path).ToArray()));

    private static readonly string[] Langs = { "en", "es", "fr", "hu", "it" };

    // The 1.3.10 members are reached by reflection, so this file also builds against older code (the red run).
    private static string? FloorParadoxId(int floor) =>
        (string?)typeof(DungeonLocation).GetMethod("FloorParadoxId", SNP)!.Invoke(null, new object[] { floor });

    private static EndingType? EndingToResumeOnLogin(Character c) =>
        (EndingType?)typeof(GameEngine).GetMethod("EndingToResumeOnLogin", SNP)!.Invoke(null, new object[] { c });

    private static string[] ChoiceEchoFlags =>
        (string[])typeof(EndingsSystem).GetField("ChoiceEchoFlags", SNP)!.GetValue(null)!;

    private static List<string> ChoiceEchoKeys(StoryProgressionSystem story) =>
        (List<string>)typeof(EndingsSystem).GetMethod("ChoiceEchoKeys", SNP)!.Invoke(null, new object[] { story })!;

    private static JsonObject Lang(string lang) =>
        JsonNode.Parse(Src("Localization", lang + ".json"))!.AsObject();

    private static ParadoxOption Option(string paradoxId, string optionId)
    {
        var all = (Dictionary<string, MoralParadox>)typeof(MoralParadoxSystem).GetField("paradoxes", F)!.GetValue(Moral)!;
        return all[paradoxId].Choices.Single(o => o.Id == optionId);
    }

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

    private static Screen NewScreen()
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 40)), s.Output);
        return s;
    }

    // ---------- P1: prerequisites, not chapters ----------

    [Fact]
    public void PossessedChild_IsOffered_OnceTheFloor15SealIsHeld_AtAnyChapter()
    {
        var hero = Hero();
        Story.CurrentChapter.Should().Be(StoryChapter.Awakening);
        Moral.IsParadoxAvailable("possessed_child", hero).Should().BeFalse("the seal of floor 15 is not held yet");
        Story.CollectSeal(SealType.FirstWar);
        Moral.IsParadoxAvailable("possessed_child", hero).Should().BeTrue("a descent that found the floor 15 seal reaches it");
    }

    [Fact]
    public void VelourasCure_NeedsTheLoom_VelouraSaved_AndLyrisTravelling()
    {
        var hero = Hero();
        Story.CollectArtifact(ArtifactType.SoulweaversLoom);
        Story.UpdateGodState(OldGodType.Veloura, GodStatus.Saved);
        Moral.IsParadoxAvailable("velouras_cure", hero).Should().BeFalse("Lyris does not travel with the player");
        LyrisTravels();
        Moral.IsParadoxAvailable("velouras_cure", hero).Should().BeTrue();

        Story.UpdateGodState(OldGodType.Veloura, GodStatus.Defeated);
        Moral.IsParadoxAvailable("velouras_cure", hero).Should().BeFalse("a goddess killed is not cured");

        Story.UpdateGodState(OldGodType.Veloura, GodStatus.Saved);
        CompanionSystem.Instance.GetCompanion(CompanionId.Lyris)!.IsDead = true;
        Moral.IsParadoxAvailable("velouras_cure", hero).Should().BeFalse("Lyris is dead");
    }

    [Fact]
    public void VelourasCure_WithoutTheLoom_IsNotOffered()
    {
        Story.UpdateGodState(OldGodType.Veloura, GodStatus.Saved);
        LyrisTravels();
        Moral.IsParadoxAvailable("velouras_cure", Hero()).Should().BeFalse();
    }

    [Fact]
    public void FreeTerravok_IsOffered_WhileHeSleeps_AndNotOnceHisFateIsDecided()
    {
        var hero = Hero();
        Story.OldGodStates[OldGodType.Terravok].Status.Should().Be(GodStatus.Dormant);
        Moral.IsParadoxAvailable("free_terravok", hero).Should().BeTrue("Terravok still sleeps");
        Story.UpdateGodState(OldGodType.Terravok, GodStatus.Defeated);
        Moral.IsParadoxAvailable("free_terravok", hero).Should().BeFalse("Terravok's fate is decided");
    }

    [Fact]
    public void DestroyDarkness_IsOffered_WithTheBlade_AtAnyChapter()
    {
        var hero = Hero();
        Moral.IsParadoxAvailable("destroy_darkness", hero).Should().BeFalse();
        Story.CollectArtifact(ArtifactType.SunforgedBlade);
        Moral.IsParadoxAvailable("destroy_darkness", hero).Should().BeTrue();
    }

    [Fact]
    public void FinalChoice_StillWaitsForItsChapter()
    {
        Moral.IsParadoxAvailable("final_choice", Hero()).Should().BeFalse("no code offers it; its chapter gate stays");
    }

    [Fact]
    public void FloorParadoxes_AreOfferedOnEveryEntry_NotOnlyTheFirstVisit()
    {
        FloorParadoxId(25).Should().Be("possessed_child");
        FloorParadoxId(65).Should().Be("velouras_cure");
        FloorParadoxId(80).Should().Be("free_terravok");
        FloorParadoxId(95).Should().BeNull("floor 95 keeps its own checks");

        var src = Src("Scripts", "Locations", "DungeonLocation.cs");
        int every = src.IndexOf("private async Task CheckQuestDependentFloorEvents", StringComparison.Ordinal);
        int first = src.IndexOf("private async Task TriggerFloorStoryEvent", StringComparison.Ordinal);
        every.Should().BePositive();
        first.Should().BePositive();
        var everyBody = src.Substring(every, src.IndexOf("switch (currentDungeonLevel)", every, StringComparison.Ordinal) - every);
        everyBody.Should().Contain("await OfferFloorParadox(player, term);");
        var firstBody = src.Substring(first, src.IndexOf("/// <summary>", first, StringComparison.Ordinal) - first);
        foreach (var id in new[] { "possessed_child", "velouras_cure", "free_terravok" })
            firstBody.Should().NotContain($"\"{id}\"", "the first visit only path no longer offers it");
    }

    [Fact]
    public void WakingTerravok_LeavesHimToBeMetOnFloor95()
    {
        var hero = Hero();
        hero.Level = 95;
        foreach (var g in new[] { OldGodType.Maelketh, OldGodType.Veloura, OldGodType.Thorgrim, OldGodType.Noctura })
            Story.UpdateGodState(g, GodStatus.Defeated);

        Moral.ApplyChoiceEffects(Option("free_terravok", "wake_terravok"), hero);

        Story.HasStoryFlag("woke_terravok_early").Should().BeTrue();
        Story.OldGodStates[OldGodType.Terravok].Status.Should().NotBe(GodStatus.Awakened);
        Story.IsGodResolved(OldGodType.Terravok).Should().BeFalse("he is not resolved until he is met");
        OldGodBossSystem.Instance.CanEncounterBoss(hero, OldGodType.Terravok).Should().BeTrue("floor 95 still meets him");
    }

    [Fact]
    public void VelourasCure_Outcomes_NoLongerSendThePlayerBackToFloor40()
    {
        var gone = new Dictionary<string, string[]>
        {
            ["en"] = new[] { "Return to Veloura on floor 40", "Is already dying", "A goddess can be saved." },
            ["es"] = new[] { "Vuelve con Veloura en el piso 40", "Ya se está muriendo", "Una diosa puede ser salvada." },
            ["fr"] = new[] { "Retournez voir Veloura", "Elle se meurt déjà", "Une déesse peut être sauvée." },
            ["hu"] = new[] { "Térj vissza Velourához", "Már most is haldoklik", "Egy istennőt meg lehet menteni." },
            ["it"] = new[] { "Torna da Veloura al piano 40", "Sta già morendo", "Una dea può essere salvata." },
        };
        foreach (var lang in Langs)
        {
            var d = Lang(lang);
            foreach (var part in new[] { "setup", "sacrifice_lyris.outcome", "refuse_sacrifice.outcome", "offer_own_soul.outcome" })
            {
                var text = (string)d[$"moral.velouras_cure.{part}"]!;
                foreach (var g in gone[lang])
                    text.Should().NotContain(g, $"{lang} {part}: Veloura is already saved when the cure is offered");
                foreach (var row in text.Split('\n'))
                    (row.Length + 2).Should().BeLessOrEqualTo(79, $"{lang} {part}: {row}");
            }
        }
    }

    // ---------- P2: Manwe's answer weighs the ending ----------

    private static EndingType EndingFor(long chivalry, long darkness, params string[] flags)
    {
        foreach (var f in flags) Story.SetStoryFlag(f, true);
        return EndingsSystem.Instance.DetermineEnding(Hero(chivalry, darkness));
    }

    [Fact]
    public void WalkAway_IsDefiant_InEveryBand()
    {
        EndingFor(0, 0, "manwe_ally").Should().Be(EndingType.Defiant);
        Story.FullReset();
        EndingFor(400, 0, "manwe_ally").Should().Be(EndingType.Defiant, "walking away beats a light alignment");
        Story.FullReset();
        EndingFor(0, 400, "manwe_ally").Should().Be(EndingType.Defiant, "walking away beats a dark alignment");
    }

    [Fact]
    public void TakeTheBurden_IsSavior_OrUsurperWhenTheUsurperTestHolds()
    {
        EndingFor(0, 0, "manwe_compassion").Should().Be(EndingType.Savior);
        Story.FullReset();
        EndingFor(400, 0, "manwe_compassion").Should().Be(EndingType.Savior);
        Story.FullReset();
        EndingFor(0, 400, "manwe_compassion").Should().Be(EndingType.Usurper, "alignment below -300");
        Story.FullReset();
        EndingFor(0, 0, "manwe_compassion", "maelketh_destroyed", "thorgrim_destroyed", "veloura_destroyed",
            "noctura_destroyed", "aurelion_destroyed").Should().Be(EndingType.Usurper, "5 gods destroyed");
    }

    [Fact]
    public void OtherAnswers_KeepTodaysRule()
    {
        EndingFor(0, 0).Should().Be(EndingType.Defiant);
        Story.FullReset();
        EndingFor(400, 0, "manwe_righteous").Should().Be(EndingType.Savior);
        Story.FullReset();
        EndingFor(0, 400).Should().Be(EndingType.Usurper);
        Story.FullReset();
        EndingFor(0, 0, "veloura_saved", "aurelion_saved", "terravok_saved").Should().Be(EndingType.Savior);
    }

    [Fact]
    public void TheTrueEnding_StaysFirst_OverWalkingAway()
    {
        foreach (SealType s in Enum.GetValues(typeof(SealType))) Story.CollectSeal(s);
        OceanPhilosophySystem.Instance.RestoreFromSave(Array.Empty<WaveFragment>(),
            new[] { AwakeningMoment.FirstCompanionDeath }, null, 7);
        EndingFor(0, 0, "veloura_saved", "aurelion_saved", "manwe_ally").Should().Be(EndingType.TrueEnding);
    }

    [Fact]
    public void LoginFailsafe_WithManweAllied_PlaysTheDefiantEnding()
    {
        var hero = Hero(400, 0);
        EndingToResumeOnLogin(hero).Should().BeNull("Manwe is not resolved");
        Story.UpdateGodState(OldGodType.Manwe, GodStatus.Allied);
        Story.SetStoryFlag("manwe_ally", true);
        EndingToResumeOnLogin(hero).Should().Be(EndingType.Defiant);
        Story.SetStoryFlag("ending_sequence_completed", true);
        EndingToResumeOnLogin(hero).Should().BeNull("the ending already played");
    }

    [Fact]
    public void AllianceWithManwe_SetsManweSaved_AndNoOtherGodDoes()
    {
        var boss = (Dictionary<OldGodType, OldGodBossData>)typeof(OldGodBossSystem).GetField("bossData", F)!
            .GetValue(OldGodBossSystem.Instance)!;
        var allied = typeof(OldGodBossSystem).GetMethod("HandleBossAllied", F)!;
        var s = NewScreen();

        ((Task)allied.Invoke(OldGodBossSystem.Instance, new object[] { Hero(), boss[OldGodType.Thorgrim], s.Term })!)
            .GetAwaiter().GetResult();
        Story.HasStoryFlag("manwe_saved").Should().BeFalse();
        Story.HasStoryFlag("thorgrim_saved").Should().BeFalse();

        ((Task)allied.Invoke(OldGodBossSystem.Instance, new object[] { Hero(), boss[OldGodType.Manwe], s.Term })!)
            .GetAwaiter().GetResult();
        Story.HasStoryFlag("manwe_saved").Should().BeTrue();
        Story.OldGodStates[OldGodType.Manwe].Status.Should().Be(GodStatus.Allied);
        EndingsSystem.CountGodsByFlags(Story).Should().Be((0, 0), "Manwe is not counted among the gods");
    }

    // ---------- P3: one echo for each answer ----------

    [Fact]
    public void EachParadoxAnswerAndMiraChoice_HasAnEndingLine_InFiveLanguages()
    {
        ChoiceEchoFlags.Should().HaveCount(15);
        foreach (var lang in Langs)
        {
            var d = Lang(lang);
            foreach (var flag in ChoiceEchoFlags)
            {
                var key = "ending.echo_" + flag;
                d.ContainsKey(key).Should().BeTrue($"{lang} has {key}");
                ((string)d[key]!).Should().NotBeNullOrWhiteSpace();
                ((string)d[key]!).Length.Should().BeLessOrEqualTo(77, $"{lang} {key} fits a row after its indent");
            }
        }
    }

    [Fact]
    public void EveryParadoxOption_WritesAFlagThatHasAnEcho()
    {
        var all = (Dictionary<string, MoralParadox>)typeof(MoralParadoxSystem).GetField("paradoxes", F)!.GetValue(Moral)!;
        foreach (var id in new[] { "possessed_child", "velouras_cure", "free_terravok", "destroy_darkness" })
            foreach (var o in all[id].Choices)
                ChoiceEchoFlags.Should().Contain(o.StoryFlag, $"{id}.{o.Id}");
    }

    [Fact]
    public void TheEndingScreen_ShowsTheLinesOfTheChoicesMade_AndNoOthers()
    {
        Story.SetStoryFlag("carries_demon", true);
        Story.SetStoryFlag("mira_chose_peace", true);

        var s = NewScreen();
        var show = typeof(EndingsSystem).GetMethod("ShowWorldImpact", F)!;
        ((Task)show.Invoke(EndingsSystem.Instance, new object[] { Hero(), EndingType.Defiant, Story, s.Term })!)
            .GetAwaiter().GetResult();
        var text = s.Text;
        text.Should().Contain(Loc.GetIn("en", "ending.echo_carries_demon"));
        text.Should().Contain(Loc.GetIn("en", "ending.echo_mira_chose_peace"));
        text.Should().NotContain(Loc.GetIn("en", "ending.echo_village_consumed"));
        text.Should().NotContain(Loc.GetIn("en", "ending.echo_mira_chose_life"));
        ChoiceEchoKeys(Story).Should().Equal("ending.echo_carries_demon", "ending.echo_mira_chose_peace");
    }
}
