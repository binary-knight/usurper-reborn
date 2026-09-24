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

    // ---------- 3. the announcement ----------

    private static void RaiseTo(int stage)
    {
        for (int i = 0; Ocean.AwakeningLevel < stage; i++) Ocean.GainInsight("rise" + i);
    }

    [Fact]
    public void ARise_IsQueued_NotShownAtOnce()
    {
        RaiseTo(1);
        Ocean.PendingAnnouncementStage.Should().Be(1);
        Ocean.PendingAnnouncementFromStage.Should().Be(0);
        RaiseTo(2);
        Ocean.PendingAnnouncementStage.Should().Be(2, "rises before the next safe point fold into one screen");
        Ocean.PendingAnnouncementFromStage.Should().Be(0);
    }

    [Fact]
    public async Task TheAnnouncement_ShowsOnce_WithStageLoreBoostAndWhatOpened()
    {
        RaiseTo(2);
        var hero = Hero();
        var (term, output) = Terminal("\n\n\n");
        (await AwakeningScreens.ShowPending(term, hero)).Should().BeTrue();
        string shown = Shown(term, output);
        shown.Should().Contain(Loc.Get("ocean.awakening_header"));
        shown.Should().Contain(Loc.Get("ocean.awakening_stage", 2, 7, Loc.Get("base.awakening_aware")));
        shown.Should().Contain(Loc.Get("ocean.stage.2.lore.1"));
        shown.Should().Contain(AwakeningBonus.GainedAt(1)).And.Contain(AwakeningBonus.GainedAt(2));
        shown.Should().Contain(Loc.Get("ocean.stage.2.opens"));
        hero.HintsShown.Should().Contain(HintSystem.HINT_AWAKENING, "the first rise explains the awakening");

        var (term2, _) = Terminal("\n\n");
        (await AwakeningScreens.ShowPending(term2, hero)).Should().BeFalse("shown once");
    }

    [Fact]
    public void TheAnnouncement_WaitsForTheLocationLoop()
    {
        string loop = Source("Scripts/Locations/BaseLocation.cs");
        // inside the per-command loop, before the command runs: every dungeon room action comes back here
        int loopStart = loop.IndexOf("while (!exitLocation && currentPlayer.IsAlive)");
        int show = loop.IndexOf("await AwakeningScreens.ShowPending(terminal, currentPlayer);");
        int process = loop.IndexOf("exitLocation = await ProcessChoice(choice);");
        loopStart.Should().BeGreaterThan(0);
        show.Should().BeGreaterThan(loopStart).And.BeLessThan(process);
        Source("Scripts/Locations/DungeonLocation.cs").Should().NotContain("override async Task LocationLoop");
        Source("Scripts/Systems/CombatEngine.cs").Should().NotContain("AwakeningScreens.ShowPending");
        Source("Scripts/Systems/DialogueSystem.cs").Should().NotContain("AwakeningScreens.ShowPending");
    }

    [Fact]
    public void EveryStage_HasLore_AndWhatItOpens()
    {
        for (int stage = 1; stage <= 7; stage++)
        {
            for (int i = 1; i <= AwakeningScreens.LoreLinesPerStage; i++)
                Loc.Get($"ocean.stage.{stage}.lore.{i}").Should().NotBe($"ocean.stage.{stage}.lore.{i}");
            Loc.Get($"ocean.stage.{stage}.opens").Should().NotBe($"ocean.stage.{stage}.opens");
            AwakeningBonus.GainedAt(stage).Should().NotBeNullOrEmpty();
        }
        // only stages 6 and 7 speak of the Creator, and only 7 names him
        for (int stage = 1; stage <= 6; stage++)
            for (int i = 1; i <= AwakeningScreens.LoreLinesPerStage; i++)
                Loc.Get($"ocean.stage.{stage}.lore.{i}").Should().NotContain("Manwe");
        Enumerable.Range(1, 4).Select(i => Loc.Get($"ocean.stage.7.lore.{i}")).Should().Contain(l => l.Contains("Manwe"));
    }

    [Fact]
    public void ANewCharacter_IsToldSomethingSleeps_AndTheStatusLinePointsToTheJournal()
    {
        Source("Scripts/Systems/OpeningStorySystem.cs").Should().Contain("opening_story.dream_asleep");
        Loc.Get("opening_story.dream_asleep").Should().NotContain("Manwe");
        string status = Source("Scripts/Locations/BaseLocation.cs");
        status.Should().Contain("base.stat_awakening_hint");
        status.Should().Contain("HintSystem.Instance.TryShowHint(HintSystem.HINT_AWAKENING, terminal, currentPlayer?.HintsShown)");
    }

    [Fact]
    public void TheAwakeningHint_IsRegistered_AndShownOnce()
    {
        var hero = Hero();
        var (term, output) = Terminal();
        HintSystem.Instance.TryShowHint(HintSystem.HINT_AWAKENING, term, hero.HintsShown).Should().BeTrue();
        HintSystem.Instance.TryShowHint(HintSystem.HINT_AWAKENING, term, hero.HintsShown).Should().BeFalse();
        Shown(term, output).Should().Contain(Loc.Get("hint.awakening.title"));
    }

    // ---------- 4. the Ocean Journal ----------

    [Fact]
    public void TheJournalSummary_UsesTheSevenStageScale()
    {
        Ocean.CollectFragment(WaveFragment.Origin);
        Ocean.ExperienceMoment(AwakeningMoment.SparedAnEnemy);
        var (term, output) = Terminal();
        AwakeningScreens.WriteJournalSummary(term);
        string shown = Shown(term, output);
        shown.Should().Contain(Loc.Get("ocean.journal_stage", 1, 7, Loc.Get("base.awakening_stirring")));
        shown.Should().Contain(Loc.Get("ocean.journal_points", 5, 12));
        shown.Should().Contain(Loc.Get("ocean.journal_fragments", 1, 10));
        shown.Should().Contain(Loc.Get("ocean.journal_moments", 1));
        shown.Should().NotContain("/5");
    }

    [Fact]
    public async Task TheJournalPages_ShowEachFragmentsLore_AndTheMomentsLived()
    {
        Ocean.CollectFragment(WaveFragment.TheSevenDrops);
        Ocean.ExperienceMoment(AwakeningMoment.SacrificedForAnother);
        var (term, output) = Terminal("\n");
        await AwakeningScreens.ShowJournal(term);
        string shown = Shown(term, output);
        shown.Should().Contain(Loc.Get("ocean.fragment.TheSevenDrops.title"));
        shown.Should().Contain(Loc.Get("ocean.fragment.TheSevenDrops.text").Split(' ').Take(6).Aggregate((a, b) => a + " " + b));
        shown.Should().NotContain(Loc.Get("ocean.fragment.Origin.title"), "only found fragments are readable");
        shown.Should().Contain(Loc.Get("ocean.journal_hidden", 9));
        shown.Should().Contain(Loc.Get("ocean.moment.SacrificedForAnother"));
    }

    [Fact]
    public void EveryFragmentAndMoment_HasJournalText()
    {
        foreach (WaveFragment f in Enum.GetValues(typeof(WaveFragment)))
        {
            Loc.Get($"ocean.fragment.{f}.title").Should().NotBe($"ocean.fragment.{f}.title");
            Loc.Get($"ocean.fragment.{f}.text").Should().NotBe($"ocean.fragment.{f}.text");
        }
        foreach (AwakeningMoment m in Enum.GetValues(typeof(AwakeningMoment)))
            Loc.Get($"ocean.moment.{m}").Should().NotBe($"ocean.moment.{m}");
    }

    [Fact]
    public void TheFiveScale_IsGone()
    {
        string src = Source("Scripts/Locations/MainStreetLocation.cs");
        src.Should().NotContain("main_street.awakening_level").And.NotContain("main_street.awakening_0");
        src.Should().Contain("AwakeningScreens.WriteJournalSummary(terminal)").And.Contain("AwakeningScreens.ShowJournal(terminal)");
        string en = Source("Localization/en.json");
        en.Should().NotContain("\"main_street.awakening_level\"").And.NotContain("\"main_street.story_awakening_");
        en.Should().NotContain("\"main_street.awakening_0\"").And.NotContain("\"main_street.awakening_5\"");
    }

    // ---------- 5. the boons ----------

    private static Character Caster() => new()
    {
        Name1 = "tide", Name2 = "Tide", Class = CharacterClass.Magician, Level = 20, AI = CharacterAI.Human,
        BaseStrength = 10, BaseDexterity = 10, BaseConstitution = 10, BaseIntelligence = 20, BaseWisdom = 20,
        BaseCharisma = 10, BaseMaxHP = 400, BaseMaxMana = 200, BaseDefence = 5, BaseStamina = 10, BaseAgility = 10,
        HP = 400, Mana = 200
    };

    private static T AsPlayer<T>(Character hero, Func<T> body)
    {
        var engine = GameEngine.Instance;
        var old = engine.CurrentPlayer;
        engine.CurrentPlayer = hero;
        try { return body(); }
        finally { engine.CurrentPlayer = old; }
    }

    private static async Task AsPlayerAsync(Character hero, Func<Task> body)
    {
        var engine = GameEngine.Instance;
        var old = engine.CurrentPlayer;
        engine.CurrentPlayer = hero;
        try { await body(); }
        finally { engine.CurrentPlayer = old; }
    }

    private static void ToStage(int stage)
    {
        if (stage >= 7) Ocean.ExperienceMoment(AwakeningMoment.TrueIdentityRevealed);
        else RaiseTo(stage);
        Ocean.AwakeningLevel.Should().Be(stage);
    }

    [Fact]
    public void Stage1_AddsWisdom_AtRecalculation_NeverToTheBase()
    {
        var hero = Caster();
        AsPlayer(hero, () =>
        {
            hero.RecalculateStats();
            long before = hero.Wisdom;
            ToStage(1);
            hero.RecalculateStats();
            hero.RecalculateStats();
            hero.Wisdom.Should().Be(before + 3);
            hero.BaseWisdom.Should().Be(20, "nothing is stored on the character");
            return 0;
        });
    }

    [Fact]
    public void Stage2_AddsFivePercentMaxMana_Stage7Ten()
    {
        var hero = Caster();
        AsPlayer(hero, () =>
        {
            ToStage(1);
            hero.RecalculateStats();
            long m1 = hero.MaxMana;
            m1.Should().BeGreaterThan(0);
            ToStage(2);
            hero.RecalculateStats();
            hero.MaxMana.Should().Be(m1 + (long)(m1 * 0.05));
            ToStage(7);
            hero.RecalculateStats();
            hero.MaxMana.Should().Be(m1 + (long)(m1 * 0.10));
            return 0;
        });
    }

    [Fact]
    public void Stage4_AddsFivePercentMaxHP_AndSurvivesRecalculation()
    {
        var hero = Caster();
        AsPlayer(hero, () =>
        {
            hero.RecalculateStats();
            long h0 = hero.MaxHP;
            ToStage(4);
            hero.RecalculateStats();
            hero.RecalculateStats();
            hero.MaxHP.Should().Be(h0 + (long)(h0 * 0.05));
            return 0;
        });
    }

    [Fact]
    public void Stage3_AddsFivePercentCombatXP_WhereTheTrainingBonusApplies()
    {
        var hero = Caster();
        AsPlayer(hero, () =>
        {
            TeamHQBonus.ApplyXP(hero, 1000).Should().Be(1000);
            ToStage(2);
            TeamHQBonus.ApplyXP(hero, 1000).Should().Be(1000);
            ToStage(3);
            TeamHQBonus.ApplyXP(hero, 1000).Should().Be(1050);
            ToStage(7);
            TeamHQBonus.ApplyXP(hero, 1000).Should().Be(1100);
            return 0;
        });
    }

    [Fact]
    public void Stage5_AddsThreePercentDamage_WhereTheArmoryApplies()
    {
        var hero = Caster();
        AsPlayer(hero, () =>
        {
            ToStage(4);
            TeamHQBonus.ApplyAttack(hero, 1000).Should().Be(1000);
            ToStage(5);
            TeamHQBonus.ApplyAttack(hero, 1000).Should().Be(1030);
            ToStage(7);
            TeamHQBonus.ApplyAttack(hero, 1000).Should().Be(1080);
            return 0;
        });
    }

    [Fact]
    public void Stage6_TakesThreePercentLessDamage_WhereTheBarracksApplies()
    {
        var hero = Caster();
        AsPlayer(hero, () =>
        {
            ToStage(5);
            TeamHQBonus.ApplyDefense(hero, 1000).Should().Be(1000);
            ToStage(6);
            TeamHQBonus.ApplyDefense(hero, 1000).Should().Be(970);
            ToStage(7);
            TeamHQBonus.ApplyDefense(hero, 1000).Should().Be(920);
            return 0;
        });
    }

    [Fact]
    public async Task ARise_RecalculatesAtTheSafePoint_NotAtOnce()
    {
        var hero = Caster();
        await AsPlayerAsync(hero, async () =>
        {
            hero.RecalculateStats();
            long h0 = hero.MaxHP;
            ToStage(4);
            hero.MaxHP.Should().Be(h0, "a rise mid-fight leaves the stats alone");
            var (term, _) = Terminal("\n\n\n");
            await AwakeningScreens.ShowPending(term, hero);
            hero.MaxHP.Should().Be(h0 + (long)(h0 * 0.05));
        });
    }

    [Fact]
    public void OnlyTheSessionsPlayer_IsAwakened()
    {
        var hero = Caster();
        var companion = Caster();
        var npc = new NPC { ID = "npc_awake", Name1 = "N", Name2 = "N", Level = 10, BaseMaxHP = 100, BaseWisdom = 10 };
        AsPlayer(hero, () =>
        {
            ToStage(7);
            AwakeningBonus.StageOf(hero).Should().Be(7);
            AwakeningBonus.StageOf(companion).Should().Be(0);
            AwakeningBonus.StageOf(npc).Should().Be(0);
            TeamHQBonus.ApplyAttack(companion, 1000).Should().Be(1000);
            TeamHQBonus.ApplyDefense(npc, 1000).Should().Be(1000);
            TeamHQBonus.ApplyXP(companion, 1000).Should().Be(1000);
            return 0;
        });
    }

    [Fact]
    public void ALoadedPlayer_GetsTheBoons_AndKeepsTheSavedHP()
    {
        var hero = Caster();
        AsPlayer(hero, () =>
        {
            hero.RecalculateStats();                 // the load recalculates before the story systems
            long savedHP = hero.MaxHP + 10;          // saved with the stage 4 bonus
            hero.HP = hero.MaxHP;
            Ocean.RestoreFromSave(Array.Empty<WaveFragment>(), Array.Empty<AwakeningMoment>(), null, savedLevel: 4);
            AwakeningBonus.RecalculateAfterRestore(hero, savedHP, 0);
            hero.MaxHP.Should().BeGreaterThanOrEqualTo(savedHP);
            hero.HP.Should().Be(savedHP);
            return 0;
        });
        Source("Scripts/Core/GameEngine.cs").Split("AwakeningBonus.RecalculateAfterRestore(currentPlayer").Length.Should().Be(3, "the offline and the online load");
    }

    [Fact]
    public void TheStatusScreen_AndTheJournal_ShowTheBoons()
    {
        Source("Scripts/Locations/BaseLocation.cs").Should().Contain("AwakeningBonus.ActiveAt(awakeningLevel)");
        var lines = AwakeningBonus.ActiveAt(7);
        lines.Should().HaveCount(7);
        lines.Should().Contain(Loc.Get("ocean.title_awakened_line"));
        lines.Should().Contain(Loc.Get("ocean.boost.5", 8));
        AwakeningBonus.ActiveAt(0).Should().BeEmpty();
    }

    // ---------- 6. chapter 1 ----------

    [Theory]
    [InlineData("en", "The Drowning", "THE DROWNING")]
    [InlineData("es", "El Ahogamiento", "EL AHOGAMIENTO")]
    [InlineData("fr", "La Noyade", "LA NOYADE")]
    [InlineData("hu", "A Fulladás", "A FULLADÁS")]
    [InlineData("it", "L'Annegamento", "L'ANNEGAMENTO")]
    public void Chapter1_IsTheDrowning_NotTheMetersName(string lang, string chapter, string banner)
    {
        var json = JsonDocument.Parse(Source($"Localization/{lang}.json")).RootElement;
        json.GetProperty("dungeon.chapter_awakening").GetString().Should().StartWith(chapter);
        json.GetProperty("opening_story.the_awakening").GetString().Should().Be(banner);
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
