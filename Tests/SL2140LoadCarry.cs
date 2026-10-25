using System;
using System.Collections;
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
/// 1.3.8: a single player load starts clean beyond the story too. The companions, the dungeon party, the
/// faction, archetype and memory state and the god worship entry of the character played before the load
/// equal a fresh process's load of the same save. An online session (SessionContext) is left untouched.
/// NG+ is a new game, not a load, and keeps its cycle and endings.
/// </summary>
[Collection("SharedGameSingletons")]
public class SL2140LoadCarry : IDisposable
{
    private const BindingFlags SNP = BindingFlags.NonPublic | BindingFlags.Static;
    private const BindingFlags INP = BindingFlags.NonPublic | BindingFlags.Instance;

    // every fallback singleton the load reset or RestoreStorySystems writes in these tests
    private static readonly Type[] Swapped =
    {
        typeof(StoryProgressionSystem), typeof(OceanPhilosophySystem), typeof(TownNPCStorySystem),
        typeof(StrangerEncounterSystem), typeof(DreamSystem), typeof(GriefSystem), typeof(MoralParadoxSystem),
        typeof(BetrayalSystem), typeof(AmnesiaSystem), typeof(CompanionSystem), typeof(FactionSystem), typeof(ArchetypeTracker),
    };

    // fallback singletons a new SessionContext's InitializeSystems also replaces (their constructors set the fallback)
    private static readonly Type[] SessionTouched =
    {
        typeof(CycleDialogueSystem), typeof(IntimacySystem), typeof(RomanceTracker),
    };

    private static readonly FieldInfo MemeField = typeof(CulturalMemeSystem).GetField("_instance", SNP)!;
    private static readonly FieldInfo GodsField = typeof(GodSystemSingleton).GetField("_instance", SNP)!;

    private readonly List<(FieldInfo Field, object? Value)> _old;
    private readonly object? _oldMeme;
    private readonly object? _oldGods;
    private readonly SessionContext? _oldSession;
    private readonly Character? _oldPlayer;
    private readonly List<string> _oldPartyNpcs;
    private readonly List<string> _oldPartyPlayers;

    public SL2140LoadCarry()
    {
        _old = Swapped.Concat(SessionTouched).Select(t => t.GetField("_fallbackInstance", SNP)!).Select(f => (f, f.GetValue(null))).ToList();
        _oldMeme = MemeField.GetValue(null);
        _oldGods = GodsField.GetValue(null);
        _oldSession = SessionContext.Current;
        SessionContext.Current = null;
        _oldPlayer = GameEngine.Instance.CurrentPlayer;
        _oldPartyNpcs = GameEngine.Instance.DungeonPartyNPCIds.ToList();
        _oldPartyPlayers = GameEngine.Instance.DungeonPartyPlayerNames.ToList();
    }

    public void Dispose()
    {
        SessionContext.Current = null;
        GameEngine.Instance.CurrentPlayer = _oldPlayer;
        GameEngine.Instance.SetDungeonPartyNPCs(_oldPartyNpcs);
        GameEngine.Instance.SetDungeonPartyPlayers(_oldPartyPlayers);
        foreach (var (f, v) in _old) f.SetValue(null, v);
        MemeField.SetValue(null, _oldMeme);
        GodsField.SetValue(null, _oldGods);
        SessionContext.Current = _oldSession;
    }

    // ---------- helpers ----------

    private static readonly Character PlayerA = new() { Name1 = "sl2alpha", Name2 = "SL2 Alpha" };
    private static readonly Character PlayerB = new() { Name1 = "sl2beta", Name2 = "SL2 Beta" };

    /// <summary>Fresh fallback instances, as a new process has them.</summary>
    private static void FreshProcess()
    {
        SessionContext.Current = null;
        GameEngine.Instance.CurrentPlayer = null;
        GameEngine.Instance.ClearDungeonParty();
        MemeField.SetValue(null, null);
        GodsField.SetValue(null, new GodSystem());
        foreach (var t in Swapped) t.GetField("_fallbackInstance", SNP)!.SetValue(null, Activator.CreateInstance(t));
    }

    /// <summary>The single player load's steps for these systems: the reset first, then the player, then the restore.</summary>
    private static void Load(StorySystemsData data, Character player)
    {
        GameEngine.ResetStoryForSinglePlayerLoad().Should().BeTrue("a single player load has no session");
        GameEngine.Instance.CurrentPlayer = player;
        SaveSystem.Instance.RestoreStorySystems(data, GameEngine.GodRestoreFilterFor(player));
    }

    /// <summary>Save A: a character with companions, a party, a faction, archetype scores, memories and a god.</summary>
    private static StorySystemsData SaveA() => new()
    {
        Companions = new()
        {
            new CompanionSaveInfo
            {
                Id = (int)CompanionId.Lyris, IsRecruited = true, IsActive = true, LoyaltyLevel = 80, TrustLevel = 70, RomanceLevel = 3,
                PersonalQuestStarted = true, RecruitedDay = 4, HealingPotions = 3, ManaPotions = 2, Level = 40, Experience = 999999,
                BaseStatsHP = 900, BaseStatsAttack = 90, BaseStatsDefense = 60, BaseStatsMagicPower = 30, BaseStatsSpeed = 70, BaseStatsHealingPower = 40,
                Constitution = 40, Intelligence = 30, Wisdom = 20, Charisma = 25, Dexterity = 90, Agility = 70,
                DisabledAbilities = new() { "sl2_ability" },
                SkillProficiencies = new() { ["sl2_skill"] = 3 },
                SkillTrainingProgress = new() { ["sl2_skill"] = 5 },
            },
        },
        ActiveCompanionIds = new() { (int)CompanionId.Lyris },
        FallenCompanions = new() { new CompanionDeathInfo { CompanionId = (int)CompanionId.Vex, DeathType = (int)DeathType.Combat, Circumstance = "sl2", DeathDay = 3 } },
        DungeonPartyNPCIds = new() { "sl2_npc_a" },
        DungeonPartyPlayerNames = new() { "sl2_echo_a" },
        Factions = new FactionSaveData
        {
            PlayerFaction = (int)Faction.TheShadows, FactionRank = 3, FactionReputation = 40,
            FactionStanding = new() { [(int)Faction.TheCrown] = -20, [(int)Faction.TheShadows] = 60, [(int)Faction.TheFaith] = 5 },
            CompletedFactionQuests = new() { "sl2_quest" }, HasBetrayedFaction = true, BetrayedFaction = (int)Faction.TheCrown,
        },
        ArchetypeTracker = new ArchetypeTrackerData
        {
            Scores = new() { [(int)JungianArchetype.Hero] = 30, [(int)JungianArchetype.Rebel] = 12 },
            MonstersKilled = 120, BossesDefeated = 4, SealsCollected = 2, PvPVictories = 1,
        },
        Amnesia = new AmnesiaData
        {
            RecoveredMemories = new() { MemoryFragment.Emptiness, MemoryFragment.TheFirstThought },
            ExperiencedDreams = new() { DreamSequence.Drowning }, RestCount = 6, TruthRevealed = true,
        },
        PlayerGods = new() { [PlayerA.Name2] = "Solarius" },
    };

    /// <summary>Save B: an early character. No companion, party, faction, archetype or memory section, as an early save has them.</summary>
    private static StorySystemsData SaveB() => new()
    {
        PlayerGods = new() { [PlayerB.Name2] = "Valorian" },
    };

    /// <summary>Save B with a companion section of its own.</summary>
    private static StorySystemsData SaveBWithCompanions()
    {
        var b = SaveB();
        b.Companions = new()
        {
            new CompanionSaveInfo
            {
                Id = (int)CompanionId.Mira, IsRecruited = true, LoyaltyLevel = 55, TrustLevel = 50, Level = 25, Experience = 50000,
                BaseStatsHP = 300, BaseStatsAttack = 15, BaseStatsDefense = 20, BaseStatsMagicPower = 50, BaseStatsSpeed = 30, BaseStatsHealingPower = 80,
                Constitution = 20, Intelligence = 40, Wisdom = 60, Charisma = 10, Dexterity = 10, Agility = 10,
            },
        };
        b.ActiveCompanionIds = new() { (int)CompanionId.Mira };
        return b;
    }

    /// <summary>What save A's character changes while it plays, beyond what the save holds.</summary>
    private static void PlayA()
    {
        var aldric = CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!;
        aldric.PersonalQuestAvailable = true;   // set when loyalty first reaches 50 (CompanionSystem.ModifyLoyalty)
        aldric.BaseStats.HP += 500;
        aldric.HealingPotions = 4;
        aldric.DisabledSpells.Add("sl2_spell");
    }

    private static string Sorted<T>(IEnumerable<T> xs) => string.Join(",", xs.Select(x => x!.ToString()).OrderBy(s => s, StringComparer.Ordinal));

    /// <summary>Every public property of an object, sorted by name; collections by their sorted items.</summary>
    private static string Dump(object? o, int depth = 0, params string[] skip)
    {
        if (o == null) return "null";
        var t = o.GetType();
        if (o is string || t.IsPrimitive || t.IsEnum || o is decimal || o is DateTime) return o.ToString()!;
        if (o is IDictionary d)
            return "{" + Sorted(d.Keys.Cast<object>().Select(k => $"{k}={Dump(d[k], depth + 1)}")) + "}";
        if (o is IEnumerable e)
            return "[" + Sorted(e.Cast<object?>().Select(x => Dump(x, depth + 1))) + "]";
        if (depth > 2) return t.Name;
        return t.Name + "(" + string.Join(";", t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.GetIndexParameters().Length == 0 && !skip.Contains(p.Name))
            .OrderBy(p => p.Name, StringComparer.Ordinal)
            .Select(p => $"{p.Name}:{Dump(p.GetValue(o), depth + 1)}")) + ")";
    }

    /// <summary>Every instance field of an object, public or not.</summary>
    private static string Fields(object o) =>
        string.Join(";", o.GetType().GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            .OrderBy(f => f.Name, StringComparer.Ordinal).Select(f => $"{f.Name}:{Dump(f.GetValue(o))}"));

    private static string CompanionView()
    {
        var c = CompanionSystem.Instance;
        // RecruitedDate: a restore without one stamps the clock (CompanionSystem.Deserialize)
        var each = c.GetAllCompanions().OrderBy(x => x.Id).Select(x => Dump(x, 0, "RecruitedDate"));
        var current = (IDictionary)typeof(CompanionSystem).GetField("companionCurrentHP", INP)!.GetValue(c)!;
        return string.Join(" | ", each) + $" | active {Sorted(c.GetActiveCompanions().Select(x => x.Id))} | " +
               $"fallen {Dump(typeof(CompanionSystem).GetField("fallenCompanions", INP)!.GetValue(c))} | hp {current.Count} | " +
               $"notes {c.HasPendingNotifications}";
    }

    private static string PartyView()
    {
        var e = GameEngine.Instance;
        return $"npcs {Sorted(e.DungeonPartyNPCIds)} | players {Sorted(e.DungeonPartyPlayerNames)}";
    }

    private static string FactionView() => Fields(FactionSystem.Instance);
    private static string ArchetypeView() => Fields(ArchetypeTracker.Instance);
    private static string AmnesiaView() => Fields(AmnesiaSystem.Instance);

    private static string GodView()
    {
        var g = GodSystemSingleton.Instance;
        var entries = (Dictionary<string, string>)typeof(GodSystem).GetField("playerGods", INP)!.GetValue(g)!;
        var gods = (List<God>)typeof(GodSystem).GetField("gods", INP)!.GetValue(g)!;
        return $"entries {Sorted(entries.Select(k => $"{k.Key}={k.Value}"))} | " +
               $"gods {Sorted(gods.Select(x => $"{x.Name}:{x.Believers}:{Sorted(x.Disciples)}"))}";
    }

    private static Dictionary<string, string> Views() => new()
    {
        ["companion"] = CompanionView(), ["party"] = PartyView(), ["faction"] = FactionView(),
        ["archetype"] = ArchetypeView(), ["amnesia"] = AmnesiaView(), ["god"] = GodView(),
    };

    /// <summary>A fresh process loads save B.</summary>
    private static Dictionary<string, string> FreshLoadOf(StorySystemsData b)
    {
        FreshProcess();
        Load(b, PlayerB);
        return Views();
    }

    /// <summary>One process loads save A, plays, then loads save B.</summary>
    private static Dictionary<string, string> LoadAfterA(StorySystemsData b)
    {
        FreshProcess();
        Load(SaveA(), PlayerA);
        PlayA();
        var a = Views();
        a["companion"].Should().Contain("IsRecruited:True", "save A is restored before save B");
        a["party"].Should().Contain("sl2_npc_a");
        a["god"].Should().Contain("SL2 Alpha=Solarius");
        Load(b, PlayerB);
        return Views();
    }

    // ---------- one row per system: a load after another save equals a fresh load ----------

    [Fact]
    public void Companions_AfterAnotherSave_EqualAFreshLoad()
    {
        var expected = FreshLoadOf(SaveB())["companion"];
        LoadAfterA(SaveB())["companion"].Should().Be(expected);
        var c = CompanionSystem.Instance;
        c.GetAllCompanions().Should().OnlyContain(x => !x.IsRecruited);
        c.GetCompanion(CompanionId.Lyris)!.BaseStats.HP.Should().Be(220, "the definition's base stats");
        c.GetCompanion(CompanionId.Aldric)!.PersonalQuestAvailable.Should().BeFalse();
        c.GetActiveCompanions().Should().BeEmpty();
    }

    [Fact]
    public void Companions_WithASection_AfterAnotherSave_EqualAFreshLoad()
    {
        var expected = FreshLoadOf(SaveBWithCompanions())["companion"];
        LoadAfterA(SaveBWithCompanions())["companion"].Should().Be(expected);
        var c = CompanionSystem.Instance;
        c.GetCompanion(CompanionId.Mira)!.IsRecruited.Should().BeTrue("save B's own companion is restored");
        c.GetCompanion(CompanionId.Aldric)!.PersonalQuestAvailable.Should().BeFalse("the save does not hold it, so a fresh load has none");
        c.GetCompanion(CompanionId.Aldric)!.BaseStats.HP.Should().Be(350);
    }

    [Fact]
    public void DungeonParty_AfterAnotherSave_EqualsAFreshLoad()
    {
        var expected = FreshLoadOf(SaveB())["party"];
        LoadAfterA(SaveB())["party"].Should().Be(expected);
        GameEngine.Instance.DungeonPartyNPCIds.Should().BeEmpty();
        GameEngine.Instance.DungeonPartyPlayerNames.Should().BeEmpty();
    }

    [Fact]
    public void Faction_AfterAnotherSave_EqualsAFreshLoad()
    {
        var expected = FreshLoadOf(SaveB())["faction"];
        LoadAfterA(SaveB())["faction"].Should().Be(expected);
        FactionSystem.Instance.PlayerFaction.Should().BeNull();
        FactionSystem.Instance.HasBetrayedFaction.Should().BeFalse();
    }

    [Fact]
    public void Archetype_AfterAnotherSave_EqualsAFreshLoad()
    {
        var expected = FreshLoadOf(SaveB())["archetype"];
        LoadAfterA(SaveB())["archetype"].Should().Be(expected);
        ArchetypeTracker.Instance.MonstersKilled.Should().Be(0);
    }

    [Fact]
    public void Amnesia_AfterAnotherSave_EqualsAFreshLoad()
    {
        var expected = FreshLoadOf(SaveB())["amnesia"];
        LoadAfterA(SaveB())["amnesia"].Should().Be(expected);
        AmnesiaSystem.Instance.RecoveredMemories.Should().BeEmpty();
        AmnesiaSystem.Instance.TruthRevealed.Should().BeFalse();
    }

    [Fact]
    public void GodEntry_AfterAnotherSave_EqualsAFreshLoad()
    {
        var expected = FreshLoadOf(SaveB())["god"];
        LoadAfterA(SaveB())["god"].Should().Be(expected);
        var g = GodSystemSingleton.Instance;
        g.GetPlayerGod(PlayerA.Name2).Should().BeEmpty("the character played before the load has no entry in a fresh process");
        g.GetPlayerGod(PlayerB.Name2).Should().Be("Valorian");
    }

    // ---------- the resets equal a new system ----------

    [Fact]
    public void AmnesiaReset_EqualsANewSystem()
    {
        var expected = Fields(new AmnesiaSystem());
        var a = new AmnesiaSystem();
        a.Deserialize(SaveA().Amnesia!);
        a.RecoverMemory(MemoryFragment.TheSeven);
        Fields(a).Should().NotBe(expected);
        a.Reset();
        Fields(a).Should().Be(expected);
    }

    [Fact]
    public void FactionAndArchetypeResets_EqualANewSystem()
    {
        var f = new FactionSystem();
        var freshFaction = Fields(new FactionSystem());
        f.Deserialize(SaveA().Factions);
        f.Reset();
        Fields(f).Should().Be(freshFaction);

        var t = new ArchetypeTracker();
        var freshTracker = Fields(new ArchetypeTracker());
        t.Deserialize(SaveA().ArchetypeTracker!);
        t.Reset();
        Fields(t).Should().Be(freshTracker);
    }

    // ---------- an online session ----------

    [Fact]
    public void Session_IsLeftAlone()
    {
        var ctx = new SessionContext { Username = "sl2140", CharacterKey = "sl2140" };
        ctx.InitializeSystems();
        FreshProcess();
        Load(SaveA(), PlayerA);
        var fallback = Views();
        ctx.Factions.Deserialize(SaveA().Factions);
        ctx.Amnesia.Deserialize(SaveA().Amnesia!);
        ctx.Archetype.Deserialize(SaveA().ArchetypeTracker!);
        ctx.Companions.GetCompanion(CompanionId.Aldric)!.PersonalQuestAvailable = true;
        try
        {
            SessionContext.Current = ctx;
            GameEngine.ResetStoryForSinglePlayerLoad().Should().BeFalse("a session has its own systems");
            ctx.Factions.PlayerFaction.Should().Be(Faction.TheShadows);
            ctx.Amnesia.TruthRevealed.Should().BeTrue();
            ctx.Archetype.MonstersKilled.Should().Be(120);
            ctx.Companions.GetCompanion(CompanionId.Aldric)!.PersonalQuestAvailable.Should().BeTrue();
        }
        finally { SessionContext.Current = null; }
        Views().Should().BeEquivalentTo(fallback, "nothing single player holds is reset from a session, the god entry included");
    }

    // ---------- the companion rebuild runs on a single player load only ----------

    private static string Source(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot() }.Concat(parts).ToArray()));

    private static string Method(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BePositive(signature);
        int brace = src.IndexOf('{', start);
        int depth = 0;
        for (int i = brace; i < src.Length; i++)
        {
            if (src[i] == '{') depth++;
            else if (src[i] == '}' && --depth == 0) return src.Substring(start, i - start + 1);
        }
        throw new InvalidOperationException(signature);
    }

    [Fact]
    public void CompanionRebuild_RunsOnTheLoadOnly()
    {
        var root = UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot();
        var sites = Directory.GetFiles(Path.Combine(root, "Scripts"), "*.cs", SearchOption.AllDirectories)
            .SelectMany(f => File.ReadAllLines(f).Where(l => l.Contains("RebuildForSinglePlayerLoad(") && !l.TrimStart().StartsWith("//"))
                .Select(l => $"{Path.GetFileName(f)}|{l.Trim()}"))
            .ToList();
        sites.Should().BeEquivalentTo(new[]
        {
            "CompanionSystem.cs|internal void RebuildForSinglePlayerLoad()",
            "GameEngine.cs|UsurperRemake.Systems.CompanionSystem.Instance.RebuildForSinglePlayerLoad();",
        }, "the definition and the one call");

        var engine = Source("Scripts", "Core", "GameEngine.cs");
        Method(engine, "internal static bool ResetStoryForSinglePlayerLoad()").Should().Contain("RebuildForSinglePlayerLoad();", "a load runs the rebuild");
        var create = Method(engine, "private async Task CreateNewGame(");
        create.Should().NotContain("RebuildForSinglePlayerLoad").And.NotContain("InitializeCompanions");
        create.Should().Contain("CompanionSystem.Instance?.ResetAllCompanions();", "a new game and NG+ keep the reset they had");
        Method(Source("Scripts", "Systems", "CompanionSystem.cs"), "public void ResetAllCompanions()")
            .Should().NotContain("InitializeCompanions", "a new game, NG+, a restore and the SysOp resets do not rebuild");
    }

    [Fact]
    public void ResetAllCompanions_Unchanged_RebuildStartsFromTheDefinitions()
    {
        FreshProcess();
        var c = CompanionSystem.Instance;
        var aldric = c.GetCompanion(CompanionId.Aldric)!;
        aldric.BaseStats.HP += 500;
        aldric.PersonalQuestAvailable = true;
        c.ResetAllCompanions();
        c.GetCompanion(CompanionId.Aldric)!.BaseStats.HP.Should().Be(850, "ResetAllCompanions does not name base stats, as before");
        c.GetCompanion(CompanionId.Aldric)!.PersonalQuestAvailable.Should().BeTrue();

        var fresh = new CompanionSystem();
        fresh.ResetAllCompanions();
        var expected = string.Join(" | ", fresh.GetAllCompanions().OrderBy(x => x.Id).Select(x => Dump(x, 0, "RecruitedDate")));
        typeof(CompanionSystem).GetField("_fallbackInstance", SNP)!.SetValue(null, c);
        c.RebuildForSinglePlayerLoad();
        string.Join(" | ", c.GetAllCompanions().OrderBy(x => x.Id).Select(x => Dump(x, 0, "RecruitedDate"))).Should().Be(expected);
    }

    // ---------- a load that fails ----------

    /// <summary>A fresh process has no character, so after the reset none is current until the load restores
    /// one: a load that fails leaves nothing for the exit save (Program.cs) to write under the previous name.</summary>
    [Fact]
    public void FailedLoad_LeavesNoCharacterToSave()
    {
        FreshProcess();
        Load(SaveA(), PlayerA);
        GameEngine.Instance.CurrentPlayer.Should().BeSameAs(PlayerA);
        GameEngine.ResetStoryForSinglePlayerLoad().Should().BeTrue();
        GameEngine.Instance.CurrentPlayer.Should().BeNull("the load has not restored a character yet");
        GodSystemSingleton.Instance.GetPlayerGod(PlayerA.Name2).Should().BeEmpty("the previous entry is still cleared by its name");

        Source("Console", "Bootstrap", "Program.cs").Should().Contain("var player = GameEngine.Instance?.CurrentPlayer;",
            "the exit save writes the current character only");
        var load = Method(Source("Scripts", "Core", "GameEngine.cs"), "private async Task LoadSaveByFileName(string fileName)");
        load.IndexOf("ResetStoryForSinglePlayerLoad();", StringComparison.Ordinal)
            .Should().BeLessThan(load.IndexOf("currentPlayer = RestorePlayerFromSaveData(", StringComparison.Ordinal));
    }

    // ---------- NG+ ----------

    private static string GameEngineSource() =>
        File.ReadAllText(Path.Combine(UsurperReborn.Tests.Localization.HardcodedTextScannerTests.RepoRoot(), "Scripts", "Core", "GameEngine.cs"));

    [Fact]
    public void NewGamePlus_KeepsCycleAndEndings()
    {
        FreshProcess();
        var a = SaveA();
        a.CurrentCycle = 2;
        a.CompletedEndings = new() { (int)EndingType.Savior };
        Load(a, PlayerA);
        StoryProgressionSystem.Instance.StartNewCycle(EndingType.Defiant);
        StoryProgressionSystem.Instance.CurrentCycle.Should().Be(3);
        StoryProgressionSystem.Instance.CompletedEndings.Should().BeEquivalentTo(new[] { EndingType.Savior, EndingType.Defiant });

        var src = GameEngineSource();
        int start = src.IndexOf("private async Task CreateNewGame(", StringComparison.Ordinal);
        start.Should().BePositive();
        var body = src.Substring(start, src.IndexOf("\n    private ", start + 1, StringComparison.Ordinal) - start);
        body.Should().NotContain("ResetStoryForSinglePlayerLoad", "NG+ is a new game, not a load");
        // 1.3.8: the memories reset only in the branch that is not NG+; NG+ keeps them
        int ngPlus = body.IndexOf("if (isNgPlus)", StringComparison.Ordinal);
        int notNgPlus = body.IndexOf("else", ngPlus, StringComparison.Ordinal);
        int branchEnd = body.IndexOf('{', notNgPlus);
        for (int depth = 0; branchEnd < body.Length; branchEnd++)
        {
            if (body[branchEnd] == '{') depth++;
            else if (body[branchEnd] == '}' && --depth == 0) { branchEnd++; break; }
        }
        notNgPlus.Should().BeLessThan(body.IndexOf("NOT NG+", StringComparison.Ordinal), "the else is the branch that is not NG+");
        body.Substring(0, notNgPlus).Should().NotContain("AmnesiaSystem", "NG+ keeps the memories");
        body.Substring(notNgPlus, branchEnd - notNgPlus).Should().Contain("AmnesiaSystem.Instance.Reset();");
        body.Substring(branchEnd).Should().NotContain("AmnesiaSystem", "the reset sits in that branch only");
        body.Should().Contain("if (isNgPlus)");
    }
}
