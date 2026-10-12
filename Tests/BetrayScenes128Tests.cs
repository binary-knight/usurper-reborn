using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.BBS;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.8 betrayal piece 2: the Stranger's scene at the Noctura fight, Lyris's farewell in the Loom paradox
/// and the beloved's betrayal after a new lover, shown at the town safe point. Each shows once, grants
/// nothing twice, never touches a marriage, a lover, the jealousy, a relationship record or an NPC, never
/// comes from an old save's state, never shows in a dungeon, in prison or mid fight, and never runs on a
/// thread with no player session in a server that serves many players.
/// </summary>
[Collection("SharedGameSingletons")]
public class BetrayScenes128Tests : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags INP = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string PlayerName = "BetraySceneHero";
    private const string SpouseId = "npc_betray_scene_spouse";
    private const string LoverId = "npc_betray_scene_lover";

    private static readonly Type[] Swapped =
    {
        typeof(StoryProgressionSystem), typeof(OceanPhilosophySystem), typeof(AmnesiaSystem), typeof(MoralParadoxSystem),
        typeof(BetrayalSystem), typeof(CompanionSystem), typeof(StrangerEncounterSystem), typeof(TownNPCStorySystem),
        typeof(DreamSystem), typeof(GriefSystem), typeof(RomanceTracker), typeof(RelationshipSystem), typeof(ArchetypeTracker),
    };
    private static readonly FieldInfo OnlineField = typeof(DoorMode).GetField("_onlineMode", SNP)!;
    private static readonly FieldInfo WorldSimField = typeof(DoorMode).GetField("_worldSimMode", SNP)!;
    private static readonly FieldInfo MudField = typeof(DoorMode).GetField("_mudServerMode", SNP)!;

    private readonly List<(FieldInfo Field, object? Old)> _old;
    private readonly object? _online = OnlineField.GetValue(null);
    private readonly object? _worldSim = WorldSimField.GetValue(null);
    private readonly object? _mud = MudField.GetValue(null);
    private readonly SessionContext? _session = SessionContext.Current;
    private readonly string _language = GameConfig.Language;
    private readonly List<NPC> _npcs = new();

    public BetrayScenes128Tests()
    {
        _old = Swapped.Select(t => t.GetField("_fallbackInstance", SNP)!).Select(f => (f, f.GetValue(null))).ToList();
        GameConfig.Language = "en";
        SessionContext.Current = null;
    }

    public void Dispose()
    {
        SessionContext.Current = _session;
        OnlineField.SetValue(null, _online);
        WorldSimField.SetValue(null, _worldSim);
        MudField.SetValue(null, _mud);
        foreach (var npc in _npcs) NPCSpawnSystem.Instance.ActiveNPCs.Remove(npc);
        foreach (var (f, v) in _old) f.SetValue(null, v);
        GameConfig.Language = _language;
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
        s.Term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 60)), i =>
        {
            if (i >= 40) throw new InvalidOperationException("the screen asked for input more than 40 times");
            s.Term.StreamWriterInternal?.Flush();
            s.Output.WriteByte((byte)'\n');
        }), s.Output);
        return s;
    }

    /// <summary>A screen whose player drops at the first key prompt.</summary>
    private static Screen DroppingScreen()
    {
        var s = new Screen();
        s.Term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 5), _ =>
            throw new IOException("the player dropped at the key prompt")), s.Output);
        return s;
    }

    private static Character Hero() => new()
    {
        Name1 = PlayerName, Name2 = PlayerName, Class = CharacterClass.Warrior, Level = 100, HP = 5000, MaxHP = 5000,
        AI = CharacterAI.Human, Gold = 1000, Chivalry = 300, Darkness = 100, Wisdom = 40, BaseWisdom = 40, Fame = 50,
        Experience = 123456,
    };

    /// <summary>Fresh story, ocean, paradox, companion, betrayal and romance singletons (single player process).</summary>
    private static void FreshWorld(int awakening = 0)
    {
        typeof(StoryProgressionSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new StoryProgressionSystem());
        var ocean = new OceanPhilosophySystem();
        ocean.RestoreFromSave(Array.Empty<WaveFragment>(), Array.Empty<AwakeningMoment>(), null, awakening);
        typeof(OceanPhilosophySystem).GetField("_fallbackInstance", SNP)!.SetValue(null, ocean);
        typeof(AmnesiaSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new AmnesiaSystem());
        typeof(MoralParadoxSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new MoralParadoxSystem());
        typeof(CompanionSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new CompanionSystem());
        typeof(GriefSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new GriefSystem());
        typeof(BetrayalSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new BetrayalSystem());
        typeof(RelationshipSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, new RelationshipSystem());
        typeof(ArchetypeTracker).GetField("_fallbackInstance", SNP)!.SetValue(null, new ArchetypeTracker());
        _ = new RomanceTracker(); // becomes the fallback
    }

    private static BetrayalSystem B => BetrayalSystem.Instance;
    private static StoryProgressionSystem Story => StoryProgressionSystem.Instance;

    private static Dictionary<string, BetrayalProfile> Profiles(BetrayalSystem s) =>
        (Dictionary<string, BetrayalProfile>)typeof(BetrayalSystem).GetField("betrayalProfiles", INP)!.GetValue(s)!;

    private static List<ActiveBetrayal> Records(BetrayalSystem s) =>
        (List<ActiveBetrayal>)typeof(BetrayalSystem).GetField("activeBetrays", INP)!.GetValue(s)!;

    private NPC AddNpc(string id, string name)
    {
        var npc = new NPC { ID = id, Name1 = name, Name2 = name, Level = 20, HP = 300, MaxHP = 300 };
        NPCSpawnSystem.Instance.ActiveNPCs.Add(npc);
        _npcs.Add(npc);
        return npc;
    }

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { AppContext.BaseDirectory, "..", "..", "..", "..", "Scripts" }.Concat(parts).ToArray()));

    /// <summary>The source without comments, so a scan sees calls only.</summary>
    private static string CodeOnly(string src) =>
        Regex.Replace(Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline), @"//[^\n]*", "");

    private static string MethodBody(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0, $"{signature} is in the source");
        int open = src.IndexOf('{', start);
        int depth = 0;
        for (int i = open; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException("unbalanced method");
    }

    private static int Count(string text, string part) => Regex.Matches(text, Regex.Escape(part)).Count;

    private static string FirstRow(string key) => Loc.GetIn("en", key).Split('\n')[0];

    private static (long Gold, long Experience, int Fame, long Darkness, long Chivalry, long Wisdom) Purse(Character c) =>
        (c.Gold, c.Experience, c.Fame, c.Darkness, c.Chivalry, (long)c.Wisdom);

    private static string SafePoint(Character hero, bool isDungeon = false, bool inPrison = false)
    {
        var s = NewScreen();
        B.ShowPendingAtSafePoint(hero, s.Term, isDungeon, inPrison).GetAwaiter().GetResult();
        return s.Text;
    }

    /// <summary>A married player (spouse an NPC who is not polyamorous and not told) and an NPC to take as lover.</summary>
    private (NPC Spouse, NPC Lover) MarriedToAnNpc(bool acceptsPoly = false, bool knows = false)
    {
        var spouse = AddNpc(SpouseId, "Ilvane");
        var lover = AddNpc(LoverId, "Corrow");
        RomanceTracker.Instance.Spouses.Add(new Spouse
        {
            NPCId = SpouseId, NPCName = "Ilvane", AcceptsPolyamory = acceptsPoly, KnowsAboutOthers = knows, LoveLevel = 80,
        });
        return (spouse, lover);
    }

    // ---------- row 16: the Stranger's trigger ----------

    [Fact]
    public void TheNocturaFight_RunsTheStrangerScene_AfterTheIntro_AndBeforeTheFight()
    {
        var body = CodeOnly(MethodBody(Source("Systems", "OldGodBossSystem.cs"), "private async Task<bool> HandleNocturaBetrayal("));
        int intro = body.IndexOf("betrayalData.LocIntro()", StringComparison.Ordinal);
        int key = body.IndexOf("await terminal.PressAnyKey();", intro, StringComparison.Ordinal);
        int scene = body.IndexOf("await BetrayalSystem.Instance.RunStrangerRevelation(player, terminal);", StringComparison.Ordinal);
        int heal = body.IndexOf("long healAmount", StringComparison.Ordinal);
        int fight = body.IndexOf(".PlayerVsMonsters(", StringComparison.Ordinal);
        intro.Should().BeGreaterThan(0);
        scene.Should().BeGreaterThan(key, "the scene follows the intro and its key wait").And.BeLessThan(heal);
        heal.Should().BeLessThan(fight);
        Count(body, "RunStrangerRevelation(").Should().Be(1);
    }

    [Fact]
    public void TheStrangerScene_SetsPending_AndShowsHerBetrayal_Once()
    {
        FreshWorld();
        var hero = Hero();
        B.HasPendingBetrayal("TheStranger").Should().BeFalse("nothing is pending before her fight");
        var s = NewScreen();
        B.RunStrangerRevelation(hero, s.Term).GetAwaiter().GetResult().Should().BeTrue();

        s.Text.Should().Contain(Loc.GetIn("en", "betrayal.turns_to_face", Loc.GetIn("en", "betrayal.TheStranger.name")));
        s.Text.Should().Contain($"  \"{FirstRow("betrayal.TheStranger.dialogue")}\"");
        Profiles(B)["TheStranger"].HasBetrayed.Should().BeTrue();
        Story.HasStoryFlag("betrayed_by_TheStranger").Should().BeTrue();
        Story.HasFlag(StoryFlag.KnowsNocturaTruth).Should().BeTrue();
        var record = Records(B).Should().ContainSingle().Subject;
        record.NPCId.Should().Be("TheStranger");
        record.CanBeResolved.Should().BeTrue();
        record.IsResolved.Should().BeFalse();
    }

    [Fact]
    public void ASceneFailure_IsCaught_AndTheStateIsKept()
    {
        FreshWorld();
        var hero = Hero();
        bool shown = true;
        var act = () => { shown = B.RunStrangerRevelation(hero, DroppingScreen().Term).GetAwaiter().GetResult(); };
        act.Should().NotThrow("a failure in the scene never reaches the fight's catch, which records an escape");
        shown.Should().BeFalse();
        Profiles(B)["TheStranger"].HasBetrayed.Should().BeTrue("the state was written before the scene");
        Story.HasStoryFlag("betrayed_by_TheStranger").Should().BeTrue();
        Records(B).Should().ContainSingle();
    }

    // ---------- row 17: no double grant ----------

    [Fact]
    public void TheStrangerScene_GrantsNothing_AndTheFightRewardsAreOnce()
    {
        FreshWorld();
        var hero = Hero();
        var before = Purse(hero);
        B.RunStrangerRevelation(hero, NewScreen().Term).GetAwaiter().GetResult().Should().BeTrue();
        Purse(hero).Should().Be(before, "no gold, experience, fame, alignment or wisdom from the scene");

        var body = CodeOnly(MethodBody(Source("Systems", "OldGodBossSystem.cs"), "private async Task<bool> HandleNocturaBetrayal("));
        Count(body, "player.Experience += xpReward;").Should().Be(1);
        Count(body, "player.Gold += goldReward;").Should().Be(1);
        Count(body, "player.Fame += 100;").Should().Be(1);
    }

    // ---------- row 18: once ----------

    [Fact]
    public void ASecondPassThroughTheFight_ShowsNoScene()
    {
        FreshWorld();
        var hero = Hero();
        B.RunStrangerRevelation(hero, NewScreen().Term).GetAwaiter().GetResult().Should().BeTrue();
        var s = NewScreen();
        B.RunStrangerRevelation(hero, s.Term).GetAwaiter().GetResult().Should().BeFalse();
        s.Text.Should().NotContain(FirstRow("betrayal.TheStranger.dialogue"));
        Records(B).Should().ContainSingle("one record");
        B.HasPendingBetrayal("TheStranger").Should().BeFalse();

        // an old save where she already turned (noctura_betrayed) shows nothing either
        FreshWorld();
        Story.SetStoryFlag("noctura_betrayed", true);
        B.RestoreFromSave(null);
        var old = NewScreen();
        B.RunStrangerRevelation(hero, old.Term).GetAwaiter().GetResult().Should().BeFalse();
        old.Text.Should().NotContain(FirstRow("betrayal.TheStranger.dialogue"));
        Records(B).Should().BeEmpty();
    }

    // ---------- row 19: the Stranger never at the safe point ----------

    [Fact]
    public void APendingStranger_IsNotShownAtTheSafePoint()
    {
        FreshWorld();
        var hero = Hero();
        B.TriggerStrangerRevelation();
        B.HasPendingBetrayal("TheStranger").Should().BeTrue();
        SafePoint(hero).Should().NotContain(FirstRow("betrayal.TheStranger.dialogue"));
        Profiles(B)["TheStranger"].HasBetrayed.Should().BeFalse("she turns at her fight, not in town");
        BetrayalSystem.SafePointIds.Should().Equal("TeamBetrayal", "RomanticBetrayal");
    }

    // ---------- rows 20 to 22: Lyris ----------

    private const string Loom = "velouras_cure";

    private static string PresentLoom(Character hero, int pick)
    {
        var s = NewScreen(pick.ToString());
        var choice = MoralParadoxSystem.Instance.PresentParadox(Loom, hero, s.Term).GetAwaiter().GetResult();
        choice.Should().NotBeNull();
        return s.Text;
    }

    private static void RecruitLyris()
    {
        var lyris = CompanionSystem.Instance.GetCompanion(CompanionId.Lyris)!;
        lyris.IsRecruited = true;
        lyris.IsDead = false;
    }

    private static string LyrisLine => FirstRow("betrayal.Lyris.dialogue");

    [Fact]
    public void SacrificingLyris_ShowsHerFarewell_BeforeTheOutcome_Once()
    {
        FreshWorld();
        RecruitLyris();
        var hero = Hero();
        var text = PresentLoom(hero, 1);

        var outcome = Loc.GetIn("en", MoralParadoxSystem.OptionKey(Loom, "sacrifice_lyris", "outcome")).Split('\n').First(r => r.Length > 0);
        int farewell = text.IndexOf($"  \"{LyrisLine}\"", StringComparison.Ordinal);
        farewell.Should().BeGreaterThan(0, "her lines are shown");
        farewell.Should().BeLessThan(text.IndexOf(outcome, StringComparison.Ordinal), "she speaks before she dies");
        Count(text, LyrisLine).Should().Be(1);
        text.Should().Contain(Loc.GetIn("en", "betrayal.sacrifice_not_malice"));
        Profiles(B)["Lyris"].HasBetrayed.Should().BeTrue();
        Story.HasStoryFlag("betrayed_by_Lyris").Should().BeTrue();
        Story.HasStoryFlag("Lyris_sacrificed").Should().BeTrue();
        var record = Records(B).Should().ContainSingle().Subject;
        record.IsSacrifice.Should().BeTrue();
        B.GetActiveBetrays().Where(r => r.CanBeResolved && !r.IsSacrifice).Should().BeEmpty("nothing to forgive or avenge");
        CompanionSystem.Instance.GetCompanion(CompanionId.Lyris)!.IsDead.Should().BeTrue("the paradox still takes her");
    }

    [Fact]
    public void NoFarewell_WhenLyrisIsNotWithThePlayer_OrIsSpared_OrHasGone()
    {
        var hero = Hero();

        FreshWorld();
        PresentLoom(hero, 1).Should().NotContain(LyrisLine, "never recruited");
        Profiles(B)["Lyris"].HasBetrayed.Should().BeFalse();

        FreshWorld();
        RecruitLyris();
        CompanionSystem.Instance.GetCompanion(CompanionId.Lyris)!.IsDead = true;
        PresentLoom(hero, 1).Should().NotContain(LyrisLine, "already dead");

        FreshWorld();
        RecruitLyris();
        PresentLoom(hero, 2).Should().NotContain(LyrisLine, "refuse_sacrifice spares her");
        Profiles(B)["Lyris"].HasBetrayed.Should().BeFalse();
        Records(B).Should().BeEmpty();

        FreshWorld();
        RecruitLyris();
        Profiles(B)["Lyris"].HasBetrayed = true;
        PresentLoom(hero, 1).Should().NotContain(LyrisLine, "once per character");
        Records(B).Should().BeEmpty();
    }

    [Fact]
    public void TheParadoxAndTheFarewell_GiveWisdomFiveOnce_AndOneMoment()
    {
        FreshWorld();
        RecruitLyris();
        var hero = Hero();
        PresentLoom(hero, 1).Should().Contain(LyrisLine);
        ((int)hero.Wisdom).Should().Be(45, "the paradox gives +5 and the scene nothing");
        ((int)hero.BaseWisdom).Should().Be(45);
        OceanPhilosophySystem.Instance.ExperiencedMoments.Should().Contain(AwakeningMoment.CompanionSacrifice);
    }

    // ---------- rows 23 to 27: the beloved ----------

    [Fact]
    public void ANewLover_WhileMarriedToAnUnawareNpc_ShowsTheBelovedAtTheSafePoint()
    {
        FreshWorld();
        var hero = Hero();
        MarriedToAnNpc();
        RomanceTracker.Instance.AddLover(LoverId, 50).Should().BeTrue();
        B.HasPendingBetrayal("RomanticBetrayal").Should().BeTrue();
        Profiles(B)["RomanticBetrayal"].BetrayalPoints.Should().Be(100, "one act");

        var text = SafePoint(hero);
        text.Should().Contain($"  \"{FirstRow("betrayal.RomanticBetrayal.dialogue")}\"");
        text.Should().Contain(Loc.GetIn("en", "betrayal.wounds_too_deep"));
        ((int)hero.Wisdom).Should().Be(43, "the pinned Wisdom +3");
        Profiles(B)["RomanticBetrayal"].HasBetrayed.Should().BeTrue();
        Records(B).Single().CanBeResolved.Should().BeFalse("the beloved cannot be forgiven");

        SafePoint(hero).Should().BeEmpty("once");
        RomanceTracker.Instance.AddLover("npc_another", 30);
        SafePoint(hero).Should().BeEmpty("a second lover brings no second betrayal");
        ((int)hero.Wisdom).Should().Be(43);
    }

    [Fact]
    public void NoBetrayal_WhenThePartnerConsentsKnowsOrDoesNotExist()
    {
        int Points() => Profiles(B)["RomanticBetrayal"].BetrayalPoints;

        FreshWorld();
        MarriedToAnNpc(acceptsPoly: true);
        RomanceTracker.Instance.AddLover(LoverId);
        Points().Should().Be(0, "a polyamorous spouse");

        FreshWorld();
        MarriedToAnNpc(knows: true);
        RomanceTracker.Instance.AddLover(LoverId);
        Points().Should().Be(0, "a spouse who knows");

        FreshWorld();
        AddNpc(LoverId, "Corrow");
        RomanceTracker.Instance.AddLover(LoverId);
        Points().Should().Be(0, "no spouse");

        FreshWorld();
        AddNpc(SpouseId, "Ilvane");
        RomanceTracker.Instance.AddLover(SpouseId, isExclusive: false);
        RomanceTracker.Instance.AddLover(LoverId);
        Points().Should().Be(0, "a lover who is not exclusive");

        FreshWorld();
        AddNpc(SpouseId, "Ilvane");
        RomanceTracker.Instance.AddLover(SpouseId, isExclusive: true);
        RomanceTracker.Instance.CurrentLovers.Single().KnowsAboutOthers = true;
        RomanceTracker.Instance.AddLover(LoverId);
        Points().Should().Be(0, "an exclusive lover who knows");

        FreshWorld();
        AddNpc(SpouseId, "Ilvane");
        RomanceTracker.Instance.AddLover(SpouseId, isExclusive: true);
        RomanceTracker.Instance.AddLover(LoverId);
        Points().Should().Be(100, "an exclusive lover who was not told is wronged");
    }

    private static string Relations() =>
        JsonSerializer.Serialize(typeof(RelationshipSystem).GetField("_instanceRelationships", INP)!.GetValue(RelationshipSystem.Instance));

    private static string Partners(RomanceTracker r) => JsonSerializer.Serialize(new
    {
        Spouses = r.Spouses.Select(s => new { s.NPCId, s.NPCName, s.AcceptsPolyamory, s.KnowsAboutOthers, s.LoveLevel, s.Children, s.MarriedGameDay }),
        Ex = r.ExSpouses.Select(e => new { e.NPCId, e.DivorceReason }),
        r.Exes,
    });

    private static string NpcState(NPC npc) => JsonSerializer.Serialize(new
    {
        npc.Married, npc.IsMarried, npc.SpouseName, npc.IsAlive, npc.HP, npc.Darkness, npc.Chivalry,
        Registry = NPCMarriageRegistry.Instance.GetSpouseId(npc.ID),
    });

    private static string Everything(RomanceTracker r, NPC spouse, NPC lover) => JsonSerializer.Serialize(new
    {
        P = Partners(r),
        Lovers = r.CurrentLovers.Select(l => new { l.NPCId, l.IsExclusive, l.KnowsAboutOthers, l.LoveLevel }),
        Jealousy = r.JealousyLevels.OrderBy(k => k.Key).ToList(),
        R = Relations(), S = NpcState(spouse), L = NpcState(lover),
    });

    [Fact]
    public void TheBelovedBranch_NeverTouchesTheMarriage_TheLovers_TheJealousy_OrTheNpc()
    {
        FreshWorld();
        var hero = Hero();
        var (spouse, lover) = MarriedToAnNpc();
        var partners = Partners(RomanceTracker.Instance);
        var relations = Relations();
        var spouseState = NpcState(spouse);
        var loverState = NpcState(lover);

        RomanceTracker.Instance.AddLover(LoverId, 50);
        B.HasPendingBetrayal("RomanticBetrayal").Should().BeTrue();
        Partners(RomanceTracker.Instance).Should().Be(partners, "the spouse and the ex records are unchanged");
        RomanceTracker.Instance.JealousyLevels.Should().Equal(new Dictionary<string, int> { [SpouseId] = 30 },
            "only the jealousy AddLover always adds");
        RomanceTracker.Instance.CurrentLovers.Select(l => l.NPCId).Should().Equal(LoverId);
        Relations().Should().Be(relations);
        NpcState(spouse).Should().Be(spouseState);
        NpcState(lover).Should().Be(loverState);

        var before = Everything(RomanceTracker.Instance, spouse, lover);
        SafePoint(hero).Should().Contain(FirstRow("betrayal.RomanticBetrayal.dialogue"));
        Everything(RomanceTracker.Instance, spouse, lover).Should().Be(before, "the scene changes no partner, record or NPC");
    }

    [Fact]
    public void APlayerPartner_IsNeverWronged()
    {
        FreshWorld();
        AddNpc(LoverId, "Corrow");
        RomanceTracker.Instance.Spouses.Add(new Spouse { NPCId = "player_partner_key", NPCName = "SomePlayer" });
        RomanceTracker.Instance.AddLover(LoverId);
        Profiles(B)["RomanticBetrayal"].BetrayalPoints.Should().Be(0, "a partner who is no NPC (a player) never counts");
        B.HasPendingBetrayal("RomanticBetrayal").Should().BeFalse();
    }

    [Fact]
    public void Online_TheBelovedBranch_WritesOnlyThePlayersOwnState()
    {
        FreshWorld();
        var spouse = AddNpc(SpouseId, "Ilvane");
        var lover = AddNpc(LoverId, "Corrow");
        var fallbackBetrayal = B;
        var fallbackRelations = Relations();
        var ctx = new SessionContext { Username = PlayerName, CharacterKey = PlayerName };
        ctx.InitializeSystems();
        OnlineField.SetValue(null, true);
        SessionContext.Current = ctx;
        try
        {
            var hero = Hero();
            RomanceTracker.Instance.Should().BeSameAs(ctx.Romance);
            RomanceTracker.Instance.Spouses.Add(new Spouse { NPCId = SpouseId, NPCName = "Ilvane", LoveLevel = 80 });
            var partners = Partners(ctx.Romance);
            var relations = Relations();
            var spouseState = NpcState(spouse);
            var loverState = NpcState(lover);

            RomanceTracker.Instance.AddLover(LoverId, 50).Should().BeTrue();
            ctx.Betrayal.HasPendingBetrayal("RomanticBetrayal").Should().BeTrue("the branch runs online too");
            SafePoint(hero).Should().Contain(FirstRow("betrayal.RomanticBetrayal.dialogue"));
            ctx.Betrayal.HasBetrayed("RomanticBetrayal").Should().BeTrue();

            Partners(ctx.Romance).Should().Be(partners);
            Relations().Should().Be(relations);
            NpcState(spouse).Should().Be(spouseState, "a shared NPC is never written");
            NpcState(lover).Should().Be(loverState, "a shared NPC is never written");
        }
        finally { SessionContext.Current = null; }

        fallbackBetrayal.HasPendingBetrayal("RomanticBetrayal").Should().BeFalse("the shared fallback is untouched");
        fallbackBetrayal.HasBetrayed("RomanticBetrayal").Should().BeFalse();
        Relations().Should().Be(fallbackRelations);
    }

    // ---------- row 28: an old save never fires ----------

    private static readonly JsonSerializerOptions SaveJson = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        IncludeFields = true,
    };

    [Fact]
    public void AnOldSave_WithJealousyLowLoyaltyAndFinishedBeats_ShowsNothingOnTheFirstTownLoop()
    {
        FreshWorld();
        var hero = Hero();
        Story.SetStoryFlag("lyris_sacrificed_for_veloura", true);
        Story.SetStoryFlag("noctura_ally", true);
        var story = SaveSystem.Instance.SerializeStorySystemsPublic();
        var aldric = story.Companions.Single(c => c.Id == (int)CompanionId.Aldric);
        aldric.IsRecruited = true;
        aldric.IsActive = true;
        aldric.LoyaltyLevel = 0;
        var root = JsonNode.Parse(JsonSerializer.Serialize(story, SaveJson))!.AsObject();
        root.Remove("betrayal").Should().BeTrue("a save written before 1.2.8 has no betrayal state");
        var loaded = JsonSerializer.Deserialize<StorySystemsData>(root.ToJsonString(), SaveJson)!;

        // the player's live romance: an unaware NPC spouse at full jealousy
        FreshWorld();
        AddNpc(SpouseId, "Ilvane");
        RomanceTracker.Instance.Spouses.Add(new Spouse { NPCId = SpouseId, NPCName = "Ilvane" });
        RomanceTracker.Instance.JealousyLevels[SpouseId] = 100;
        SaveSystem.Instance.RestoreStorySystems(loaded);
        // the bit flags are not in the story save; a player who knows Noctura's truth is set live
        Story.SetFlag(StoryFlag.KnowsNocturaTruth);

        CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!.LoyaltyLevel.Should().Be(0);
        SafePoint(hero).Should().BeEmpty("no login straight into a betrayal");
        foreach (var p in Profiles(B).Values)
        {
            p.IsPendingBetrayal.Should().BeFalse($"{p.NPCId} is not pending");
            p.BetrayalPoints.Should().Be(0, $"{p.NPCId} counts from the update forward");
        }
        Profiles(B)["Lyris"].HasBetrayed.Should().BeTrue("her sacrifice is a finished beat");
        Records(B).Should().BeEmpty();
    }

    // ---------- rows 29 and 30: the safe point ----------

    [Fact]
    public void TheSafePoint_ShowsNothingInADungeonInPrisonOrDead_AndShowsInTown()
    {
        FreshWorld();
        var hero = Hero();
        B.RecordInfidelity(SpouseId);
        var line = FirstRow("betrayal.RomanticBetrayal.dialogue");

        SafePoint(hero, isDungeon: true).Should().BeEmpty("never in a dungeon");
        SafePoint(hero, inPrison: true).Should().BeEmpty("never in prison");
        hero.HP = 0;
        SafePoint(hero).Should().BeEmpty("never for a dead player");
        B.HasPendingBetrayal("RomanticBetrayal").Should().BeTrue("it waits");
        hero.HP = 5000;
        SafePoint(hero).Should().Contain(line, "the next town loop shows it");
    }

    [Fact]
    public void TheSafePointCall_IsInTheLocationLoop_AfterTheCompanionDeathCheck_AndKnowsTheDungeonAndPrison()
    {
        var src = CodeOnly(Source("Locations", "BaseLocation.cs"));
        int death = src.IndexOf("CompanionSystem.Instance.CheckDeathTriggers(currentPlayer)", StringComparison.Ordinal);
        int call = src.IndexOf("BetrayalSystem.Instance.ShowPendingAtSafePoint(", StringComparison.Ordinal);
        int display = src.IndexOf("bool showDisplay = !_skipNextRedraw", StringComparison.Ordinal);
        death.Should().BeGreaterThan(0);
        call.Should().BeGreaterThan(death).And.BeLessThan(display);
        Count(src, "ShowPendingAtSafePoint(").Should().Be(1);
        src.Should().Contain("ShowPendingAtSafePoint(currentPlayer, terminal, isDungeon: this is DungeonLocation, inPrison: inPrison)");
        src.Should().Contain("bool inPrison = currentPlayer.DaysInPrison > 0 || this is PrisonLocation || this is PrisonWalkLocation;");
    }

    [Fact]
    public void NoSceneMidFight_APendingSetMeanwhileWaitsForTheTownLoop()
    {
        var combat = CodeOnly(Source("Systems", "CombatEngine.cs"));
        foreach (var call in new[] { "ExecuteBetrayal(", "ShowPendingAtSafePoint(", "RunStrangerRevelation(", "RunLyrisSacrifice(", "BetrayalSystem" })
            combat.Should().NotContain(call, "the fight never shows a betrayal");

        // the scenes run only from their own moments
        var scripts = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Scripts");
        var callers = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories)
            .Select(f => (Name: Path.GetFileName(f), Code: CodeOnly(File.ReadAllText(f))))
            .ToList();
        callers.Where(f => f.Code.Contains("ExecuteBetrayal(")).Select(f => f.Name).Should().Equal("BetrayalSystem.cs");
        callers.Where(f => f.Code.Contains(".ShowPendingAtSafePoint(")).Select(f => f.Name).Should().Equal("BaseLocation.cs");
        callers.Where(f => f.Code.Contains(".RunStrangerRevelation(")).Select(f => f.Name).Should().Equal("OldGodBossSystem.cs");
        callers.Where(f => f.Code.Contains(".RunLyrisSacrifice(")).Select(f => f.Name).Should().Equal("MoralParadoxSystem.cs");

        FreshWorld();
        var hero = Hero();
        B.RecordInfidelity(SpouseId); // as if during a fight
        B.HasPendingBetrayal("RomanticBetrayal").Should().BeTrue();
        Profiles(B)["RomanticBetrayal"].HasBetrayed.Should().BeFalse("nothing shows until the town loop");
        SafePoint(hero).Should().Contain(FirstRow("betrayal.RomanticBetrayal.dialogue"));
    }

    // ---------- row 31: the King's advisor stays deferred ----------

    [Fact]
    public void APendingKingsAdvisor_IsNeverShown()
    {
        FreshWorld();
        var hero = Hero();
        Profiles(B)["KingsAdvisor"].IsPendingBetrayal = true;
        SafePoint(hero).Should().BeEmpty();
        SafePoint(hero).Should().BeEmpty();
        Profiles(B)["KingsAdvisor"].HasBetrayed.Should().BeFalse();
        Story.HasStoryFlag("betrayed_by_KingsAdvisor").Should().BeFalse();
        hero.Darkness.Should().Be(100, "no political effect");
    }

    // ---------- row 32: no world simulator thread ----------

    [Fact]
    public void WithNoPlayerSession_InAServerForManyPlayers_TheHooksDoNothing_ButADoorPlayerIsServed()
    {
        var hero = Hero();
        try
        {
            // the world simulator (no session): the fallback instance is untouched
            FreshWorld();
            RecruitLyris();
            OnlineField.SetValue(null, true);
            WorldSimField.SetValue(null, true);
            MarriedToAnNpc();
            RomanceTracker.Instance.AddLover(LoverId);
            Profiles(B)["RomanticBetrayal"].BetrayalPoints.Should().Be(0, "no session: nothing is recorded");
            B.RunStrangerRevelation(hero, NewScreen().Term).GetAwaiter().GetResult().Should().BeFalse();
            B.HasPendingBetrayal("TheStranger").Should().BeFalse();
            B.RunLyrisSacrifice(hero, NewScreen().Term).GetAwaiter().GetResult().Should().BeFalse();
            Profiles(B)["RomanticBetrayal"].IsPendingBetrayal = true;
            SafePoint(hero).Should().BeEmpty();
            Profiles(B).Values.Should().OnlyContain(p => !p.HasBetrayed);
            Records(B).Should().BeEmpty();

            // a BBS door or --online player: one player in the process, no session; served
            WorldSimField.SetValue(null, false);
            MudField.SetValue(null, false);
            FreshWorld();
            foreach (var npc in _npcs) NPCSpawnSystem.Instance.ActiveNPCs.Remove(npc);
            _npcs.Clear();
            MarriedToAnNpc();
            RomanceTracker.Instance.AddLover(LoverId);
            B.HasPendingBetrayal("RomanticBetrayal").Should().BeTrue("a door player has the betrayal");
            SafePoint(hero).Should().Contain(FirstRow("betrayal.RomanticBetrayal.dialogue"));
        }
        finally
        {
            OnlineField.SetValue(null, _online);
            WorldSimField.SetValue(null, _worldSim);
            MudField.SetValue(null, _mud);
        }
    }

    // ---------- width: every row of the three scenes fits 79 columns in en and hu ----------

    [Fact]
    public void TheThreeScenes_FitSeventyNineColumns_InEnglishAndHungarian()
    {
        foreach (var lang in new[] { "en", "hu" })
        {
            GameConfig.Language = lang;
            try
            {
                var texts = new List<(string Name, string Text)>();
                FreshWorld(awakening: 3);
                var s = NewScreen();
                B.RunStrangerRevelation(Hero(), s.Term).GetAwaiter().GetResult().Should().BeTrue();
                texts.Add(("stranger", s.Text));

                FreshWorld(awakening: 3);
                RecruitLyris();
                texts.Add(("lyris", PresentLoom(Hero(), 1)));

                FreshWorld(awakening: 3);
                B.RecordInfidelity(SpouseId);
                texts.Add(("beloved", SafePoint(Hero())));

                foreach (var (name, text) in texts)
                {
                    text.Should().Contain(Loc.GetIn(lang, "betrayal.ocean_understanding"), $"[{lang}] {name} shows the ocean lines at awakening 3");
                    foreach (var row in text.Split('\n'))
                        row.TrimEnd('\r').Length.Should().BeLessThanOrEqualTo(79, $"[{lang}] {name}: '{row}'");
                }
            }
            finally { GameConfig.Language = "en"; }
        }
    }
}
