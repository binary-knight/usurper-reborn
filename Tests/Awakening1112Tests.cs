using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.1.12: the awakening points model, its saved insights, the wired moments, the announcement,
/// the Ocean Journal and the stage boosts.
/// </summary>
[Collection("SharedGameSingletons")]
public class Awakening1112Tests : IDisposable
{
    private static OceanPhilosophySystem Ocean => OceanPhilosophySystem.Instance;

    public Awakening1112Tests() => Ocean.Reset();
    public void Dispose() => Ocean.Reset();

    // ---------- 1. points model ----------

    [Fact]
    public void Points_AreThreePerMoment_TwoPerFragment_OnePerDistinctInsight()
    {
        Ocean.ExperienceMoment(AwakeningMoment.SparedAnEnemy);
        Ocean.CollectFragment(WaveFragment.Origin);
        Ocean.GainInsight("dream:a");
        Ocean.GainInsight("dream:a");
        Ocean.GainInsight("dream:b");
        Ocean.Points.Should().Be(3 + 2 + 2, "the same insight counts once");
    }

    [Fact]
    public void Stages_FollowTheThresholds()
    {
        Ocean.GainInsight("i1"); Ocean.GainInsight("i2"); Ocean.GainInsight("i3");
        Ocean.AwakeningLevel.Should().Be(0);
        Ocean.GainInsight("i4");
        Ocean.AwakeningLevel.Should().Be(1, "4 points is stage 1");
        for (int i = 5; i <= 12; i++) Ocean.GainInsight("i" + i);
        Ocean.AwakeningLevel.Should().Be(2, "12 points is stage 2");
        for (int i = 13; i <= 62; i++) Ocean.GainInsight("i" + i);
        Ocean.AwakeningLevel.Should().Be(6, "stage 7 needs more than points");
    }

    [Fact]
    public void Stage7_NeedsTheStage6Points_AndAllSeals()
    {
        Ocean.ExperienceMoment(AwakeningMoment.AllSealsCollected);
        Ocean.AwakeningLevel.Should().BeLessThan(7, "the seals alone are not enough");
        for (int i = 0; Ocean.Points < OceanPhilosophySystem.StageThresholds[6]; i++) Ocean.GainInsight("i" + i);
        Ocean.AwakeningLevel.Should().Be(7);
    }

    [Fact]
    public void TrueIdentityRevealed_StillGrants7AtOnce()
    {
        Ocean.ExperienceMoment(AwakeningMoment.TrueIdentityRevealed);
        Ocean.AwakeningLevel.Should().Be(7);
    }

    [Fact]
    public void Restore_NeverLowersTheSavedLevel_AndAnnouncesNothing()
    {
        // an old save: level 4 from the old formula (4 fragments + 2 moments), 14 points now
        Ocean.RestoreFromSave(
            new[] { WaveFragment.Origin, WaveFragment.FirstSeparation, WaveFragment.TheForgetting, WaveFragment.TheCycle },
            new[] { AwakeningMoment.FirstCompanionDeath, AwakeningMoment.SacrificedForAnother },
            null, savedLevel: 4);
        Ocean.AwakeningLevel.Should().Be(4);
        Ocean.PendingAnnouncementStage.Should().Be(0, "a restore replays stages already announced");
        Ocean.TakePendingAnnouncement().Should().BeNull();
    }

    [Fact]
    public void Restore_RaisesAboveTheSavedLevel_WhenThePointsReachHigher()
    {
        var insights = Enumerable.Range(0, 30).Select(i => "i" + i).ToList();
        Ocean.RestoreFromSave(Array.Empty<WaveFragment>(), Array.Empty<AwakeningMoment>(), insights, savedLevel: 1);
        Ocean.AwakeningLevel.Should().Be(3, "30 points is stage 3");
        Ocean.InsightIds.Should().HaveCount(30);
    }

    [Fact]
    public void Insights_SurviveTheSave()
    {
        Ocean.GainInsight("dream:dream_mirror");
        Ocean.GainInsight("feature:wave_self");
        Ocean.CollectFragment(WaveFragment.Origin);
        var data = SaveSystem.Instance.SerializeStorySystemsPublic();
        data.OceanInsightIds.Should().BeEquivalentTo(new[] { "dream:dream_mirror", "feature:wave_self" });

        var json = JsonSerializer.Serialize(data);
        var back = JsonSerializer.Deserialize<StorySystemsData>(json)!;
        Ocean.Reset();
        SaveSystem.Instance.RestoreStorySystems(back);
        Ocean.InsightIds.Should().BeEquivalentTo(new[] { "dream:dream_mirror", "feature:wave_self" });
        Ocean.Points.Should().Be(4);
        Ocean.AwakeningLevel.Should().Be(1);
    }

    [Fact]
    public void AnOldSave_WithoutInsightIds_Loads()
    {
        var back = JsonSerializer.Deserialize<StorySystemsData>("{\"AwakeningLevel\":2,\"CollectedFragments\":[0],\"ExperiencedMoments\":[0]}")!;
        SaveSystem.Instance.RestoreStorySystems(back);
        Ocean.AwakeningLevel.Should().Be(2);
        Ocean.InsightIds.Should().BeEmpty();
    }

    // ---------- 2. the moments ----------

    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static (TerminalEmulator term, MemoryStream output) Terminal(string input = "")
    {
        var output = new MemoryStream();
        return (new TerminalEmulator(new MemoryStream(Encoding.UTF8.GetBytes(input)), output), output);
    }

    private static string Shown(TerminalEmulator term, MemoryStream output)
    {
        term.StreamWriterInternal?.Flush();
        return Encoding.UTF8.GetString(output.ToArray());
    }

    private static Character Hero() => new()
    {
        Name1 = "wave", Name2 = "Wave", Class = CharacterClass.Warrior, Level = 8, HP = 500, MaxHP = 500,
        BaseMaxHP = 500, BaseWisdom = 10, Wisdom = 10, AI = CharacterAI.Human
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Source(string relative) => File.ReadAllText(Path.Combine(RepoRoot(), relative));

    [Fact]
    public async Task SparingASurrenderedFoe_RecordsSparedAnEnemy()
    {
        var (term, _) = Terminal("1\n");
        var engine = new CombatEngine(term);
        var hero = Hero();
        var npc = new NPC { ID = "npc_spare", Name1 = "Foe", Name2 = "Foe", Level = 5, HP = 0, MaxHP = 100, HpAtRoundStart = 100 };
        var result = new CombatResult { Player = hero, Opponent = npc };
        var offer = typeof(CombatEngine).GetMethod("OfferNPCSurrenderAsync", F)!;
        bool spared = await (Task<bool>)offer.Invoke(engine, new object[] { hero, npc, result })!;
        spared.Should().BeTrue();
        Ocean.ExperiencedMoments.Should().Contain(AwakeningMoment.SparedAnEnemy);
    }

    [Fact]
    public async Task FinishingASurrenderedFoe_RecordsNothing()
    {
        var (term, _) = Terminal("2\n");
        var engine = new CombatEngine(term);
        var hero = Hero();
        var npc = new NPC { ID = "npc_finish", Name1 = "Foe", Name2 = "Foe", Level = 5, HP = 0, MaxHP = 100, HpAtRoundStart = 100 };
        var offer = typeof(CombatEngine).GetMethod("OfferNPCSurrenderAsync", F)!;
        await (Task<bool>)offer.Invoke(engine, new object[] { hero, npc, new CombatResult { Player = hero, Opponent = npc } })!;
        Ocean.ExperiencedMoments.Should().NotContain(AwakeningMoment.SparedAnEnemy);
    }

    [Fact]
    public void TheManweDialogue_RecordsMetManwe_AndStartDialogueCallsIt()
    {
        DialogueSystem.RecordDialogueMoments("veloura_encounter");
        Ocean.ExperiencedMoments.Should().NotContain(AwakeningMoment.MetManwe);
        DialogueSystem.RecordDialogueMoments("manwe_encounter");
        Ocean.ExperiencedMoments.Should().Contain(AwakeningMoment.MetManwe);
        Source("Scripts/Systems/DialogueSystem.cs").Should().Contain("RecordDialogueMoments(treeId);");
    }

    [Fact]
    public void TheManweAlliance_GrantsARealFragment()
    {
        var ds = DialogueSystem.Instance;
        var trees = (System.Collections.IDictionary)typeof(DialogueSystem).GetField("dialogueTrees", F)!.GetValue(ds)!;
        var tree = (DialogueTree)trees["manwe_encounter"]!;
        var node = tree.AllNodes["manwe_alliance"];
        var old = typeof(DialogueSystem).GetField("currentPlayer", F)!.GetValue(ds);
        try
        {
            typeof(DialogueSystem).GetField("currentPlayer", F)!.SetValue(ds, Hero());
            typeof(DialogueSystem).GetMethod("ApplyNodeEffects", F)!.Invoke(ds, new object[] { node });
            Ocean.CollectedFragments.Should().Contain(WaveFragment.TheChoice);
            Ocean.InsightIds.Should().Contain("dialogue:manwe_alliance");
        }
        finally { typeof(DialogueSystem).GetField("currentPlayer", F)!.SetValue(ds, old); }
    }

    [Theory]
    [InlineData("refuse_paradise", AwakeningMoment.RejectedParadise)]
    [InlineData("take_darkness", AwakeningMoment.AbsorbedDarkness)]
    [InlineData("refuse_power", AwakeningMoment.LetGoOfPower)]
    public void TheParadoxChoices_RecordTheirMoments(string optionId, AwakeningMoment moment)
    {
        // the real options carry these ids
        var paradoxes = (System.Collections.IDictionary)typeof(MoralParadoxSystem).GetField("paradoxes", F)!.GetValue(MoralParadoxSystem.Instance)!;
        paradoxes.Values.Cast<MoralParadox>().SelectMany(p => p.Choices).Select(c => c.Id).Should().Contain(optionId);

        MoralParadoxSystem.Instance.ApplyChoiceEffects(new ParadoxOption { Id = optionId }, Hero());
        Ocean.ExperiencedMoments.Should().Contain(moment);
    }

    [Fact]
    public void AcceptingManwesOffer_RecordsLetGoOfPower()
    {
        // the Offer is mid-fight against the Creator; the recording sits in the accept branch
        string src = Source("Scripts/Systems/CombatEngine.cs");
        int accept = src.IndexOf("Player accepts The Offer");
        int record = src.LastIndexOf("ExperienceMoment(AwakeningMoment.LetGoOfPower)", accept);
        record.Should().BeGreaterThan(src.LastIndexOf("if (GameConfig.IsAffirmative(response))", accept));
    }

    [Fact]
    public async Task ALoreSong_RecordsHeardOldGodLoreSong()
    {
        var shop = new MusicShopLocation();
        var hero = Hero();
        var (term, _) = Terminal("\n\n");
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(shop, term);
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(shop, hero);
        var play = typeof(MusicShopLocation).GetMethod("PlayLoreSong", F)!;
        await (Task)play.Invoke(shop, new object[] { OldGodType.Maelketh, "t", "white", new[] { "v" } })!;
        Ocean.ExperiencedMoments.Should().Contain(AwakeningMoment.HeardOldGodLoreSong);
        Ocean.InsightIds.Should().Contain("song:Maelketh");
    }

    [Fact]
    public void ReachingAcceptance_RecordsAcceptedGrief()
    {
        var grief = GriefSystem.Instance;
        grief.Reset();
        try
        {
            grief.BeginNpcGrief("npc_grief_test", "Friend", DeathType.Combat);
            for (int day = 1; day <= 6; day++) grief.UpdateGrief(day * 10000);
            Ocean.ExperiencedMoments.Should().Contain(AwakeningMoment.AcceptedGrief);
        }
        finally { grief.Reset(); }
    }

    [Fact]
    public void FourMemories_RecordMemoriesRecovered()
    {
        var amnesia = AmnesiaSystem.Instance;
        var saved = amnesia.Serialize();
        try
        {
            amnesia.Deserialize(new AmnesiaData());
            amnesia.RecoverMemory(MemoryFragment.TheFirstThought);
            amnesia.RecoverMemory(MemoryFragment.CreatingLight);
            amnesia.RecoverMemory(MemoryFragment.WatchingThem);
            Ocean.ExperiencedMoments.Should().NotContain(AwakeningMoment.MemoriesRecovered);
            amnesia.RecoverMemory(MemoryFragment.TheDecision);
            Ocean.ExperiencedMoments.Should().Contain(AwakeningMoment.MemoriesRecovered);
            amnesia.TruthRevealed.Should().BeFalse("the floor memories are not the full truth");
        }
        finally { amnesia.Deserialize(saved); }
    }

    [Fact]
    public void Reset_ClearsInsights()
    {
        Ocean.GainInsight("x");
        Ocean.Reset();
        Ocean.InsightIds.Should().BeEmpty();
        Ocean.Points.Should().Be(0);
    }
}
