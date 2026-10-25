using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.4.0: a single player load starts from a clean story. The story (seals, artifacts, flags, endings,
/// Old God states, cycle) and the narrative systems restored with it equal a fresh process's load of the
/// same save. An online session (SessionContext) keeps its own systems untouched. NG+ is a new game, not a
/// load, and keeps its cycle and endings.
/// </summary>
[Collection("SharedGameSingletons")]
public class SL140StoryLoad : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags INP = BindingFlags.NonPublic | BindingFlags.Instance;

    // every fallback singleton the reset or RestoreStorySystems writes in these tests
    private static readonly Type[] Swapped =
    {
        typeof(StoryProgressionSystem), typeof(OceanPhilosophySystem), typeof(TownNPCStorySystem),
        typeof(StrangerEncounterSystem), typeof(DreamSystem), typeof(GriefSystem), typeof(MoralParadoxSystem),
        typeof(BetrayalSystem), typeof(AmnesiaSystem),
    };

    // fallback singletons a new SessionContext's InitializeSystems also replaces (their constructors set the fallback)
    private static readonly Type[] SessionTouched =
    {
        typeof(CompanionSystem), typeof(CycleDialogueSystem), typeof(FactionSystem), typeof(IntimacySystem), typeof(RomanceTracker),
    };

    private static readonly FieldInfo MemeField = typeof(CulturalMemeSystem).GetField("_instance", SNP)!;

    private readonly List<(FieldInfo Field, object? Value)> _old;
    private readonly object? _oldMeme;
    private readonly SessionContext? _oldSession;
    private readonly Character? _oldPlayer;

    public SL140StoryLoad()
    {
        _old = Swapped.Concat(SessionTouched).Select(t => t.GetField("_fallbackInstance", SNP)!).Select(f => (f, f.GetValue(null))).ToList();
        _oldMeme = MemeField.GetValue(null);
        _oldSession = SessionContext.Current;
        SessionContext.Current = null;
        _oldPlayer = GameEngine.Instance.CurrentPlayer;
    }

    public void Dispose()
    {
        SessionContext.Current = null;
        GameEngine.Instance.CurrentPlayer = _oldPlayer;
        foreach (var (f, v) in _old) f.SetValue(null, v);
        MemeField.SetValue(null, _oldMeme);
        SessionContext.Current = _oldSession;
    }

    // ---------- helpers ----------

    /// <summary>Fresh fallback instances, as a new process has them.</summary>
    private static void FreshProcess()
    {
        SessionContext.Current = null;
        GameEngine.Instance.CurrentPlayer = null;
        MemeField.SetValue(null, null);
        foreach (var t in Swapped) t.GetField("_fallbackInstance", SNP)!.SetValue(null, Activator.CreateInstance(t));
    }

    private static void Restore(StorySystemsData data) => SaveSystem.Instance.RestoreStorySystems(data);

    /// <summary>Save A: a character far into the story.</summary>
    private static StorySystemsData SaveA() => new()
    {
        CollectedSeals = new() { (int)SealType.Creation, (int)SealType.FirstWar },
        CollectedArtifacts = new() { (int)ArtifactType.CreatorsEye },
        StoryFlags = new() { ["sl140_flag_a"] = true },
        CurrentCycle = 3,
        CompletedEndings = new() { (int)EndingType.Savior },
        OldGodStates = new() { [(int)OldGodType.Maelketh] = (int)GodStatus.Defeated, [(int)OldGodType.Veloura] = (int)GodStatus.Saved },
        OldGodsEncountered = new() { (int)OldGodType.Maelketh, (int)OldGodType.Veloura },

        CollectedFragments = new() { (int)WaveFragment.Origin, (int)WaveFragment.FirstSeparation },
        ExperiencedMoments = new() { (int)AwakeningMoment.SparedAnEnemy },
        OceanInsightIds = new() { "sl140_insight" },
        AwakeningLevel = 2,

        CompletedParadoxIds = new() { "destroy_darkness" },
        MoralVirtueChoices = 2,

        TownNPCStories = new TownNPCStorySaveData
        {
            NPCStates = new() { new NPCStateSaveData { NPCId = "Marcus_WoundedSoldier", CurrentStage = 2, CompletedStages = new() { 0, 1 } } },
            NPCRelationship = new() { ["Marcus_WoundedSoldier"] = 10 },
        },
        StrangerEncounters = new StrangerEncounterData
        {
            EncountersHad = 3, PlayerSuspectsStranger = true, Receptivity = 20,
            EncounteredDisguises = new() { (int)StrangerDisguise.HoodedTraveler },
            UsedDialogueIds = new() { "sl140_line" },
        },
        Dreams = new DreamSaveData { ExperiencedDreams = new() { "sl140_dream" }, RestsSinceLastDream = 4 },
        ActiveGriefs = new() { new GriefStateSaveData { CompanionId = (int)CompanionId.Lyris, CompanionName = "Lyris", CurrentStage = 2, GriefStartDay = 1, StageStartDay = 1 } },
        GriefMemories = new() { new GriefMemorySaveData { CompanionId = (int)CompanionId.Lyris, CompanionName = "Lyris", MemoryText = "sl140 memory", CreatedDay = 1 } },
        Betrayal = new BetrayalSaveData { DepartedCompanionId = (int)CompanionId.Aldric },
    };

    /// <summary>Save B: a newer character. It lists one god as Unknown, leaves the others out and has no
    /// narrative sections, as an early save has them.</summary>
    private static StorySystemsData SaveB() => new()
    {
        CollectedSeals = new() { (int)SealType.Prophecy },
        StoryFlags = new() { ["sl140_flag_b"] = true },
        CurrentCycle = 1,
        OldGodStates = new() { [(int)OldGodType.Maelketh] = (int)GodStatus.Unknown },
        OldGodsEncountered = new(),
        TownNPCStories = new TownNPCStorySaveData
        {
            NPCStates = new() { new NPCStateSaveData { NPCId = "Elena_GrievingMother", CurrentStage = 1, CompletedStages = new() { 0 } } },
            NPCRelationship = new() { ["Elena_GrievingMother"] = 5 },
        },
    };

    private static string Sorted<T>(IEnumerable<T> xs) => string.Join(",", xs.Select(x => x!.ToString()).OrderBy(s => s, StringComparer.Ordinal));

    private static string StoryView()
    {
        var s = StoryProgressionSystem.Instance;
        return string.Join(" | ", new[]
        {
            $"chapter {s.CurrentChapter}", $"act {s.CurrentAct}", $"flags {s.StoryFlags}", $"cycle {s.CurrentCycle}", $"day {s.CurrentGameDay}",
            $"seals {Sorted(s.CollectedSeals)}", $"artifacts {Sorted(s.CollectedArtifacts)}",
            $"string flags {Sorted(s.ExportStringFlags().Where(k => k.Value).Select(k => k.Key))}",
            $"endings {Sorted(s.CompletedEndings)}",
            $"gods {Sorted(s.OldGodStates.Select(g => $"{g.Key}:{g.Value.Status}:{g.Value.HasBeenEncountered}"))}",
            $"key npcs {Sorted(s.KeyNPCRelationships.Select(k => $"{k.Key}={k.Value}"))}",
            $"choices {Sorted(s.MajorChoices.Keys)}", $"carryover {Sorted(s.CycleCarryoverItems)}", $"events {s.EventLog.Count}",
        });
    }

    private static string OceanView()
    {
        var o = OceanPhilosophySystem.Instance;
        return $"level {o.AwakeningLevel} | fragments {Sorted(o.CollectedFragments)} | moments {Sorted(o.ExperiencedMoments)} | " +
               $"insights {Sorted(o.InsightIds)} | insight rows {o.Insights.Count} | pending {o.PendingAnnouncementStage}/{o.PendingAnnouncementFromStage}";
    }

    private static string TownView()
    {
        var t = TownNPCStorySystem.Instance;
        return $"states {Sorted(t.NPCStates.Select(k => $"{k.Key}:{k.Value.CurrentStage}:{Sorted(k.Value.CompletedStages)}:{Sorted(k.Value.ChoicesMade.Keys)}"))} | " +
               $"relationship {Sorted(t.NPCRelationship.Select(k => $"{k.Key}={k.Value}"))}";
    }

    private static string StrangerView()
    {
        var s = StrangerEncounterSystem.Instance;
        int actions = (int)typeof(StrangerEncounterSystem).GetField("_actionsSinceLastEncounter", INP)!.GetValue(s)!;
        return $"had {s.EncountersHad} | disguises {Sorted(s.EncounteredDisguises)} | log {s.EncounterLog.Count} | suspects {s.PlayerSuspectsStranger} | " +
               $"knows {s.PlayerKnowsTruth} | receptivity {s.Receptivity} | responses {s.ResponseHistory.Count} | " +
               $"scripted {Sorted(s.CompletedScriptedEncounters)}/{Sorted(s.PendingScriptedEncounters)} | lines {Sorted(s.UsedDialogueIds)} | " +
               $"recent {s.RecentGameEvents.Count} | actions {actions}";
    }

    private static string DreamView()
    {
        var d = DreamSystem.Instance;
        var last = (string)typeof(DreamSystem).GetField("_lastDreamId", INP)!.GetValue(d)!;
        int rests = (int)typeof(DreamSystem).GetField("_restsSinceLastDream", INP)!.GetValue(d)!;
        return $"dreams {Sorted(d.ExperiencedDreams)} | visions {Sorted(d.SeenDungeonVisions)} | last '{last}' | rests {rests}";
    }

    private static string GriefView()
    {
        var g = GriefSystem.Instance.Serialize();
        return $"grief {Sorted(g.ActiveGrief.Select(x => $"{x.CompanionId}:{x.CurrentStage}"))} | npc grief {g.ActiveNpcGrief.Count} | " +
               $"memories {Sorted(g.Memories.Select(m => m.MemoryText))} | grieving {GriefSystem.Instance.IsGrieving}";
    }

    private static string MoralView()
    {
        var m = MoralParadoxSystem.Instance;
        return $"answered {Sorted(m.CompletedParadoxIds)} | {m.UtilitarianChoices}/{m.DeontologicalChoices}/{m.VirtueChoices}/{m.NihilistChoices}";
    }

    private static string BetrayalView()
    {
        var b = BetrayalSystem.Instance.Serialize();
        return $"departed {b.DepartedCompanionId?.ToString() ?? "none"} | records {b.Records.Count} | " +
               $"profiles {Sorted(b.Profiles.Select(p => $"{p.NPCId}:{p.BetrayalPoints}:{p.HasBetrayed}"))}";
    }

    private static Dictionary<string, string> Views() => new()
    {
        ["story"] = StoryView(), ["ocean"] = OceanView(), ["town"] = TownView(), ["stranger"] = StrangerView(),
        ["dream"] = DreamView(), ["grief"] = GriefView(), ["moral"] = MoralView(), ["betrayal"] = BetrayalView(),
    };

    /// <summary>A fresh process loads save B.</summary>
    private static Dictionary<string, string> FreshLoadOfB()
    {
        FreshProcess();
        GameEngine.ResetStoryForSinglePlayerLoad().Should().BeTrue();
        Restore(SaveB());
        return Views();
    }

    /// <summary>One process loads save A, then save B; with the load reset, or the restore alone.</summary>
    private static Dictionary<string, string> LoadBAfterA(bool withReset)
    {
        FreshProcess();
        GameEngine.ResetStoryForSinglePlayerLoad();
        Restore(SaveA());
        var a = Views();
        a["story"].Should().Contain("CreatorsEye", "save A is restored before save B");
        if (withReset) GameEngine.ResetStoryForSinglePlayerLoad().Should().BeTrue("a single player load has no session");
        Restore(SaveB());
        return Views();
    }

    // ---------- the story ----------

    [Fact]
    public void Story_AfterAnotherSave_EqualsAFreshLoad()
    {
        var expected = FreshLoadOfB()["story"];
        var got = LoadBAfterA(withReset: true)["story"];
        got.Should().Be(expected);
        ArtifactSystem.Instance.HasCreatorsEye().Should().BeFalse("save B never found the Creator's Eye");
        StoryProgressionSystem.Instance.HasStoryFlag("sl140_flag_a").Should().BeFalse();
        StoryProgressionSystem.Instance.CompletedEndings.Should().BeEmpty();
        StoryProgressionSystem.Instance.OldGodStates[OldGodType.Maelketh].Status.Should().Be(GodStatus.Corrupted, "Unknown in save B keeps the starting state");
        StoryProgressionSystem.Instance.OldGodStates[OldGodType.Veloura].Status.Should().Be(GodStatus.Dying, "save B does not list Veloura");
        StoryProgressionSystem.Instance.CurrentCycle.Should().Be(1);
        StoryProgressionSystem.Instance.CollectedSeals.Should().BeEquivalentTo(new[] { SealType.Prophecy });
    }

    [Fact]
    public void FullReset_EqualsANewStory()
    {
        var fresh = new StoryProgressionSystem();
        typeof(StoryProgressionSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, fresh);
        var expected = StoryView();
        FreshProcess();
        Restore(SaveA());
        StoryProgressionSystem.Instance.RecordChoice("sl140_choice", "yes");
        StoryProgressionSystem.Instance.AdvanceChapter(StoryChapter.TheChoice);
        StoryProgressionSystem.Instance.CurrentGameDay = 40;
        StoryProgressionSystem.Instance.FullReset();
        StoryView().Should().Be(expected);
    }

    // ---------- the audited systems that only added: fixed ----------

    [Fact]
    public void Ocean_AfterAnotherSave_EqualsAFreshLoad()
    {
        var expected = FreshLoadOfB()["ocean"];
        LoadBAfterA(withReset: true)["ocean"].Should().Be(expected);
        OceanPhilosophySystem.Instance.AwakeningLevel.Should().Be(0);
    }

    [Fact]
    public void TownNpcStories_AfterAnotherSave_EqualsAFreshLoad()
    {
        var expected = FreshLoadOfB()["town"];
        LoadBAfterA(withReset: true)["town"].Should().Be(expected);
        TownNPCStorySystem.Instance.NPCStates["Marcus_WoundedSoldier"].CurrentStage.Should().Be(0);
    }

    [Fact]
    public void Stranger_AfterAnotherSave_EqualsAFreshLoad()
    {
        var expected = FreshLoadOfB()["stranger"];
        LoadBAfterA(withReset: true)["stranger"].Should().Be(expected);
        StrangerEncounterSystem.Instance.EncountersHad.Should().Be(0);
    }

    [Fact]
    public void Dreams_AfterAnotherSave_EqualsAFreshLoad()
    {
        var expected = FreshLoadOfB()["dream"];
        LoadBAfterA(withReset: true)["dream"].Should().Be(expected);
        DreamSystem.Instance.ExperiencedDreams.Should().BeEmpty();
    }

    [Fact]
    public void Grief_AfterAnotherSave_EqualsAFreshLoad()
    {
        var expected = FreshLoadOfB()["grief"];
        LoadBAfterA(withReset: true)["grief"].Should().Be(expected);
        GriefSystem.Instance.IsGrieving.Should().BeFalse();
    }

    // ---------- the audited systems that replace already: pinned without the reset ----------

    [Fact]
    public void MoralParadox_Restore_Replaces()
    {
        var expected = FreshLoadOfB()["moral"];
        LoadBAfterA(withReset: false)["moral"].Should().Be(expected);
        MoralParadoxSystem.Instance.CompletedParadoxIds.Should().BeEmpty();
    }

    [Fact]
    public void Betrayal_Restore_Replaces()
    {
        var expected = FreshLoadOfB()["betrayal"];
        LoadBAfterA(withReset: false)["betrayal"].Should().Be(expected);
        BetrayalSystem.Instance.DepartedCompanionId.Should().BeNull();
    }

    // ---------- the real load paths ----------

    private static string GameEngineSource() =>
        File.ReadAllText(Path.Combine(UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Scripts", "Core", "GameEngine.cs"));

    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BePositive(signature);
        int next = src.IndexOf("\n    private ", start + 1, StringComparison.Ordinal);
        return next > start ? src.Substring(start, next - start) : src.Substring(start);
    }

    [Theory]
    [InlineData("private async Task LoadSaveByFileName(string fileName)")]
    [InlineData("private async Task LoadExistingGame(string playerName)")]
    public void LoadPath_ResetsBeforeAnyRestoreStep(string signature)
    {
        var body = Body(GameEngineSource(), signature);
        int reset = body.IndexOf("ResetStoryForSinglePlayerLoad();", StringComparison.Ordinal);
        reset.Should().BePositive("the load path runs the reset");
        foreach (var step in new[] { "RestorePlayerFromSaveData(", "LoadFromSaveData(", "RestoreWorldState(", "RestoreNPCs(", "RestoreStorySystems(" })
        {
            int at = body.IndexOf(step, StringComparison.Ordinal);
            at.Should().BePositive(step);
            reset.Should().BeLessThan(at, $"the reset runs before {step}");
        }
    }

    // ---------- no save before the story is restored (SL-A) ----------

    private static readonly string[] PlayerSaves = { "SaveCurrentGame(", ".AutoSave(", "SaveSystem.Instance.SaveGame(" };

    /// <summary>Single player and online sessions share these load paths: no player save runs before the
    /// story is restored, so a save never writes a story that is not loaded yet.</summary>
    [Theory]
    [InlineData("private async Task LoadSaveByFileName(string fileName)")]
    [InlineData("private async Task LoadExistingGame(string playerName)")]
    public void LoadPath_NoSaveBeforeTheStoryIsRestored(string signature)
    {
        var body = Body(GameEngineSource(), signature);
        int restore = body.IndexOf("RestoreStorySystems(", StringComparison.Ordinal);
        restore.Should().BePositive();
        var before = body.Substring(0, restore);
        foreach (var save in PlayerSaves)
            before.Should().NotContain(save, $"no {save} runs before the story is restored");
    }

    [Fact]
    public void RankMessage_NeverSaves_AndRunsOnTheLoginPath()
    {
        var src = GameEngineSource();
        int start = src.IndexOf("internal static bool ShowWeeklyRankChange(", StringComparison.Ordinal);
        start.Should().BePositive();
        int end = src.IndexOf("\n    }\n", start, StringComparison.Ordinal);
        var method = src.Substring(start, end - start);
        foreach (var save in PlayerSaves) method.Should().NotContain(save);

        var login = Body(src, "private async Task LoadSaveByFileName(string fileName)");
        int shown = login.IndexOf("ShowWeeklyRankChange(terminal, currentPlayer);", StringComparison.Ordinal);
        shown.Should().BePositive("the login still shows the rank change");
        shown.Should().BeLessThan(login.IndexOf("RestoreStorySystems(", StringComparison.Ordinal));
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
                return System.Text.RegularExpressions.Regex.Replace(System.Text.Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "");
            }
        }
    }

    private static Screen NewScreen()
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 10), i =>
        {
            if (i >= 5) throw new InvalidOperationException("the rank message asked for input");
        }), s.Output);
        return s;
    }

    /// <summary>The rank change shows once and is consumed, in single player and in an online session.</summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RankChange_ShowsOnce_AndIsConsumed(bool online)
    {
        var language = GameConfig.Language;
        GameConfig.Language = "en";
        if (online)
        {
            var ctx = new SessionContext { Username = "sl140rank", CharacterKey = "sl140rank" };
            ctx.InitializeSystems();
            SessionContext.Current = ctx;
        }
        try
        {
            var player = new Character { Name1 = "sl140rank", Name2 = "sl140rank", WeeklyRank = 14, PreviousWeeklyRank = 42 };
            var line = Loc.Get("engine.rank_moved_up_many", 28, 14);

            var first = NewScreen();
            GameEngine.ShowWeeklyRankChange(first.Term, player).Should().BeTrue();
            first.Text.Should().Contain(line);
            player.PreviousWeeklyRank.Should().Be(14, "the change is consumed once shown");

            var second = NewScreen();
            GameEngine.ShowWeeklyRankChange(second.Term, player).Should().BeFalse();
            second.Text.Should().NotContain(line, "the same change does not show again");
        }
        finally
        {
            SessionContext.Current = null;
            GameConfig.Language = language;
        }
    }

    // ---------- an online session ----------

    [Fact]
    public void Session_IsLeftAlone()
    {
        var ctx = new SessionContext { Username = "sl140", CharacterKey = "sl140" };
        ctx.InitializeSystems();
        FreshProcess();
        StoryProgressionSystem.Instance.CollectSeal(SealType.Truth);
        var fallbackStory = StoryProgressionSystem.Instance;
        ctx.Story.CollectSeal(SealType.Creation);
        ctx.Story.CollectArtifact(ArtifactType.CreatorsEye);
        ctx.Story.SetStoryFlag("sl140_session", true);
        ctx.Ocean.CollectFragment(WaveFragment.Origin);
        try
        {
            SessionContext.Current = ctx;
            GameEngine.ResetStoryForSinglePlayerLoad().Should().BeFalse("a session has its own systems");
            ctx.Story.CollectedSeals.Should().Contain(SealType.Creation);
            ctx.Story.CollectedArtifacts.Should().Contain(ArtifactType.CreatorsEye);
            ctx.Story.HasStoryFlag("sl140_session").Should().BeTrue();
            ctx.Ocean.CollectedFragments.Should().Contain(WaveFragment.Origin);
        }
        finally { SessionContext.Current = null; }
        fallbackStory.CollectedSeals.Should().Contain(SealType.Truth, "the fallback story is not touched from a session");
    }

    // ---------- NG+ ----------

    [Fact]
    public void NewGamePlus_KeepsCycleAndEndings()
    {
        FreshProcess();
        Restore(SaveA());
        StoryProgressionSystem.Instance.StartNewCycle(EndingType.Defiant);
        var s = StoryProgressionSystem.Instance;
        s.CurrentCycle.Should().Be(4);
        s.CompletedEndings.Should().BeEquivalentTo(new[] { EndingType.Savior, EndingType.Defiant });
        s.CollectedArtifacts.Should().BeEmpty();

        var src = GameEngineSource();
        Body(src, "private async Task CreateNewGame(").Should().NotContain("ResetStoryForSinglePlayerLoad", "NG+ is a new game, not a load");
        Body(src, "private async Task CreateNewGame(").Should().Contain("if (isNgPlus)");
    }
}
