using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Data;
using UsurperRemake.Server;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.2: an alliance with Noctura hands over the Shadow Crown, through the call the saved and the
/// defeated paths use, so the Void Key is reachable whichever way she is dealt with. A save already
/// allied with her and without the Crown gets it once at load, on the single-player and the online
/// (per-session story) path.
/// </summary>
[Collection("SharedGameSingletons")]
public class NocturaCrown132Tests : IDisposable
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;
    private const string Key = "old_god.ally_gives_crown";
    private static readonly string[] Langs = { "en", "es", "fr", "it", "hu" };

    private static readonly ArtifactType[] OtherFive =
    {
        ArtifactType.CreatorsEye, ArtifactType.SoulweaversLoom, ArtifactType.ScalesOfLaw,
        ArtifactType.SunforgedBlade, ArtifactType.Worldstone
    };

    private readonly SessionContext? _session = SessionContext.Current;
    private readonly string _lang = GameConfig.Language;

    public NocturaCrown132Tests()
    {
        SessionContext.Current = null;
        StoryProgressionSystem.Instance.FullReset();
    }

    public void Dispose()
    {
        SessionContext.Current = null;
        StoryProgressionSystem.Instance.FullReset();
        GameConfig.Language = _lang;
        SessionContext.Current = _session;
    }

    private static StoryProgressionSystem Story => StoryProgressionSystem.Instance;

    private static Character Hero(string name) => StatRewards1115Tests.Fresh(name);

    private static TerminalEmulator Term(MemoryStream output) =>
        new TerminalEmulator(new StatRewardsDecisions1115Tests.ScriptedStream(new string('\n', 200)), output);

    private static string Strip(string s) => Regex.Replace(s, "\u001b\\[[0-9;?]*[A-Za-z]", "");

    private static long Expected(StatKind stat) =>
        ArtifactSystem.ArtifactStatGrants(ArtifactSystem.Instance.GetArtifact(ArtifactType.ShadowCrown)!,
                MetaProgressionSystem.Instance.GetArtifactMultiplier())
            .Where(g => g.stat == stat).Sum(g => g.amount);

    private static async Task<BossEncounterResult> Invoke(string handler, Character player, OldGodType god, TerminalEmulator term)
    {
        var method = typeof(OldGodBossSystem).GetMethod(handler, F);
        method.Should().NotBeNull($"{handler} handles that outcome");
        var boss = OldGodsData.GetGodBossData(god);
        var args = method!.GetParameters().Length == 4
            ? new object[] { player, boss, term, false }
            : new object[] { player, boss, term };
        try { return await (Task<BossEncounterResult>)method.Invoke(OldGodBossSystem.Instance, args)!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static Task<BossEncounterResult> Ally(Character player, OldGodType god, MemoryStream? output = null) =>
        Invoke("HandleBossAllied", player, god, Term(output ?? new MemoryStream()));

    private static string Source(string relative) =>
        File.ReadAllText(Path.Combine(StatRewardsDecisions1115Tests.RepoRoot(), relative));

    // ---------------- 1. the ally path ----------------

    [Fact]
    public async Task TheAlliance_GivesTheShadowCrown_WithItsStats()
    {
        Story.SetStoryFlag("noctura_ally", true);
        var c = Hero("NxAlly");
        long agi = c.GetBaseStat(StatKind.Agility), dex = c.GetBaseStat(StatKind.Dexterity);

        var result = await Ally(c, OldGodType.Noctura);

        result.Success.Should().BeTrue();
        result.Outcome.Should().Be(BossOutcome.Allied);
        result.God.Should().Be(OldGodType.Noctura);
        result.ApproachType.Should().Be("allied");
        Story.OldGodStates[OldGodType.Noctura].Status.Should().Be(GodStatus.Allied);
        Story.CollectedArtifacts.Should().Contain(ArtifactType.ShadowCrown, "an alliance hands over the Crown");
        c.GetBaseStat(StatKind.Agility).Should().Be(agi + Expected(StatKind.Agility), "the Crown's stats come with it, once");
        c.GetBaseStat(StatKind.Dexterity).Should().Be(dex + Expected(StatKind.Dexterity));
    }

    [Fact]
    public async Task TheAllyBranch_UsesTheSparedBranchesCall()
    {
        string src = Source("Scripts/Systems/OldGodBossSystem.cs");
        string start = StatRewardsDecisions1115Tests.Body(src, "public async Task<BossEncounterResult> StartBossEncounter(");
        start.Should().MatchRegex(@"_ally""\)\)\s*\n\s*\{?\s*return await HandleBossAllied\(player, boss, terminal\);",
            "the dialogue's ally flag goes to the ally handler");
        string allied = StatRewardsDecisions1115Tests.Body(src, "private async Task<BossEncounterResult> HandleBossAllied(");
        string saved = StatRewardsDecisions1115Tests.Body(src, "private async Task<BossEncounterResult> HandleBossSaved(");
        const string call = "await ArtifactSystem.Instance.CollectArtifact(player, boss.ArtifactDropped, terminal);";
        saved.Should().Contain(call);
        allied.Should().Contain(call, "the alliance grants through the same call as the save");
        allied.Should().NotContain("CollectedArtifacts.Add(");

        // the second alliance grants nothing more
        Story.SetStoryFlag("noctura_ally", true);
        var c = Hero("NxAllyTwice");
        await Ally(c, OldGodType.Noctura);
        long agi = c.GetBaseStat(StatKind.Agility);
        await Ally(c, OldGodType.Noctura);
        c.GetBaseStat(StatKind.Agility).Should().Be(agi, "a Crown already held is not granted again");
        Story.CollectedArtifacts.Count(a => a == ArtifactType.ShadowCrown).Should().Be(1);
    }

    [Fact]
    public async Task AManweAlliance_GrantsNoArtifact()
    {
        Story.SetStoryFlag("manwe_ally", true);
        var c = Hero("NxManwe");
        var result = await Ally(c, OldGodType.Manwe);
        result.Outcome.Should().Be(BossOutcome.Allied);
        Story.OldGodStates[OldGodType.Manwe].Status.Should().Be(GodStatus.Allied);
        Story.CollectedArtifacts.Should().BeEmpty("Manwe drops no artifact");
    }

    [Fact]
    public async Task TheAllianceLine_IsShown_AndFits79Columns_InEnglishAndHungarian()
    {
        foreach (var lang in new[] { "en", "hu" })
        {
            Story.FullReset();
            Story.SetStoryFlag("noctura_ally", true);
            GameConfig.Language = lang;
            var output = new MemoryStream();
            await Ally(Hero("NxLine" + lang), OldGodType.Noctura, output);
            string line = Loc.GetIn(lang, Key);
            line.Should().NotBe(Key, $"the {lang} text exists");
            var rows = Strip(Encoding.UTF8.GetString(output.ToArray())).Replace("\r", "").Split('\n');
            var lineRows = rows.Where(r => r.Trim().Length > 0 && line.Contains(r.Trim())).ToList();
            string.Join(" ", lineRows.Select(r => r.Trim())).Should().Be(line, $"the {lang} alliance line is shown whole");
            foreach (var row in lineRows)
                row.TrimEnd().Length.Should().BeLessOrEqualTo(79, $"{lang} row \"{row}\"");
        }
    }

    [Fact]
    public void TheAllianceLine_IsKeyedInAllFiveLanguages()
    {
        string en = null!;
        foreach (var lang in Langs)
        {
            var json = JsonDocument.Parse(Source($"Localization/{lang}.json")).RootElement;
            json.TryGetProperty(Key, out var v).Should().BeTrue($"{lang}.json has {Key}");
            string text = v.GetString()!;
            text.Should().NotBeNullOrWhiteSpace();
            text.Should().NotContain("\u2014").And.NotContain("\u2013");
            if (lang == "en") en = text;
            else text.Should().NotBe(en, $"{lang} is translated");
        }
    }

    // ---------------- 2. the load grant ----------------

    /// <summary>A story block as an older version saved it: allied with Noctura, no Crown.</summary>
    private static StorySystemsData OldAlliedSave(bool allied = true, params ArtifactType[] held)
    {
        var data = new StorySystemsData
        {
            CollectedArtifacts = held.Select(a => (int)a).ToList(),
            StoryFlags = new Dictionary<string, bool> { ["noctura_ally"] = allied, ["noctura_pact_sealed"] = true }
        };
        return JsonSerializer.Deserialize<StorySystemsData>(JsonSerializer.Serialize(data))!;
    }

    /// <summary>The story block the next save writes, from the live story.</summary>
    private static StorySystemsData Resaved(StorySystemsData loaded)
    {
        var json = JsonSerializer.Deserialize<StorySystemsData>(JsonSerializer.Serialize(loaded))!;
        json.CollectedArtifacts = Story.CollectedArtifacts.Select(a => (int)a).ToList();
        return json;
    }

    private static Dictionary<StatKind, long> Bases(Character c) =>
        Enum.GetValues<StatKind>().ToDictionary(s => s, s => c.GetBaseStat(s));

    /// <summary>Loads the save twice as a login does, and checks the Crown came once and nothing else moved.</summary>
    private static void LoadTwice_GrantsTheCrownOnce(string name)
    {
        var save = OldAlliedSave(true, ArtifactType.CreatorsEye);
        SaveSystem.Instance.RestoreStorySystems(save);
        var c = Hero(name);
        c.ArtifactStatsApplied.Should().BeTrue("a save from 1.2.0 on holds its artifact stats");
        var before = Bases(c);
        long gold = c.Gold, xp = c.Experience;
        var flags = save.StoryFlags.Where(f => f.Value).Select(f => f.Key).ToList();
        var states = Story.OldGodStates.ToDictionary(s => s.Key, s => s.Value.Status);

        GameEngine.RunStatRewardMigrations(c, save);   // first login

        Story.CollectedArtifacts.Should().BeEquivalentTo(new[] { ArtifactType.CreatorsEye, ArtifactType.ShadowCrown },
            "an allied save without the Crown gets it at load");
        foreach (var s in Enum.GetValues<StatKind>())
            c.GetBaseStat(s).Should().Be(before[s] + Expected(s), $"{s} gets the Crown's amount once");
        (c.Gold, c.Experience).Should().Be((gold, xp));
        flags.Should().OnlyContain(f => Story.HasStoryFlag(f));
        Story.HasStoryFlag("noctura_ally").Should().BeTrue("the alliance stands");
        Story.HasStoryFlag("void_key_obtained").Should().BeFalse();
        Story.OldGodStates.ToDictionary(s => s.Key, s => s.Value.Status).Should().Equal(states, "no god state changes");
        var afterFirst = Bases(c);

        var next = Resaved(save);                       // the save written after that login
        var again = StatRewards1115Tests.RoundTrip(c);
        SaveSystem.Instance.RestoreStorySystems(next);  // second login
        GameEngine.RunStatRewardMigrations(again, next);
        Bases(again).Should().Equal(afterFirst, "the second load grants nothing");
        GameEngine.RunStatRewardMigrations(again, save); // even the older block again
        Bases(again).Should().Equal(afterFirst);
        Story.CollectedArtifacts.Should().HaveCount(2);
    }

    [Fact]
    public void AnAlliedSave_GetsTheCrownOnce_OnTheSinglePlayerPath()
    {
        SessionContext.Current.Should().BeNull();
        LoadTwice_GrantsTheCrownOnce("NxLoadSingle");
    }

    [Fact]
    public void AnAlliedSave_GetsTheCrownOnce_OnTheOnlinePath_InItsOwnSessionStory()
    {
        var fallback = StoryProgressionSystem.Instance;
        var ctx = new SessionContext { Username = "nx_online", CharacterKey = "nx_online" };
        ctx.InitializeSystems();
        SessionContext.Current = ctx;
        try
        {
            StoryProgressionSystem.Instance.Should().BeSameAs(ctx.Story);
            LoadTwice_GrantsTheCrownOnce("NxLoadOnline");
            ctx.Story.CollectedArtifacts.Should().Contain(ArtifactType.ShadowCrown);
        }
        finally { SessionContext.Current = null; }
        fallback.CollectedArtifacts.Should().BeEmpty("the grant goes to the session's own story");
    }

    [Fact]
    public void ASaveNotAllied_OrHoldingTheCrown_IsNotTouched()
    {
        foreach (var save in new[]
                 {
                     OldAlliedSave(false, ArtifactType.CreatorsEye),         // betrayed: the alliance is over
                     OldAlliedSave(true, ArtifactType.ShadowCrown),          // allied and already holding it
                     new StorySystemsData { CollectedArtifacts = new List<int> { (int)ArtifactType.CreatorsEye } }
                 })
        {
            Story.FullReset();
            SaveSystem.Instance.RestoreStorySystems(save);
            var c = Hero("NxNone");
            var before = Bases(c);
            var held = Story.CollectedArtifacts.ToList();
            GameEngine.RunStatRewardMigrations(c, save);
            Story.CollectedArtifacts.Should().BeEquivalentTo(held);
            Bases(c).Should().Equal(before);
        }
    }

    [Fact]
    public void BothLoadPaths_RunTheLoadGrant_AfterTheStoryIsRestored()
    {
        string engine = Source("Scripts/Core/GameEngine.cs");
        Regex.Matches(engine,
                @"RestoreStorySystems\(saveData\.StorySystems, \w+\);(\s*\n[^\n]*){0,3}\s*\n\s*RunStatRewardMigrations\(currentPlayer, saveData\.StorySystems\);")
            .Count.Should().Be(2, "both load paths run the load steps right after the story is restored");
        string steps = StatRewardsDecisions1115Tests.Body(engine, "internal static void RunStatRewardMigrations(");
        int restore = steps.IndexOf("RestoreMissingArtifactStats", StringComparison.Ordinal);
        int crown = steps.IndexOf("GrantNocturaAllianceCrown", StringComparison.Ordinal);
        crown.Should().BeGreaterThan(restore, "the Crown is granted after the stat restore, so its stats never come twice");
        restore.Should().BeGreaterThan(0);
    }

    // ---------------- 3. the kill and spare paths, unchanged ----------------

    [Fact]
    public async Task TheKillAndSparePaths_StillGiveTheCrown()
    {
        string src = Source("Scripts/Systems/OldGodBossSystem.cs");
        string defeated = StatRewardsDecisions1115Tests.Body(src, "private async Task<BossEncounterResult> HandleBossDefeated(");
        defeated.Should().Contain("await ArtifactSystem.Instance.CollectArtifact(player, boss.ArtifactDropped, terminal);");
        defeated.Should().Contain("story.UpdateGodState(boss.Type, GodStatus.Defeated);");
        string start = StatRewardsDecisions1115Tests.Body(src, "public async Task<BossEncounterResult> StartBossEncounter(");
        start.Should().Contain("return await HandleBossSaved(player, boss, terminal);");

        var killer = Hero("NxKill");
        var kill = await Invoke("HandleBossDefeated", killer, OldGodType.Noctura, Term(new MemoryStream()));
        kill.Outcome.Should().Be(BossOutcome.Defeated);
        Story.OldGodStates[OldGodType.Noctura].Status.Should().Be(GodStatus.Defeated);
        Story.HasStoryFlag("noctura_destroyed").Should().BeTrue();
        Story.CollectedArtifacts.Should().Equal(ArtifactType.ShadowCrown);

        Story.FullReset();
        var saver = Hero("NxSpare");
        long agi = saver.GetBaseStat(StatKind.Agility);
        var spare = await Invoke("HandleBossSaved", saver, OldGodType.Noctura, Term(new MemoryStream()));
        spare.Outcome.Should().Be(BossOutcome.Saved);
        Story.OldGodStates[OldGodType.Noctura].Status.Should().Be(GodStatus.Saved);
        Story.HasStoryFlag("noctura_saved").Should().BeTrue();
        Story.CollectedArtifacts.Should().Equal(ArtifactType.ShadowCrown);
        saver.GetBaseStat(StatKind.Agility).Should().Be(agi + Expected(StatKind.Agility));
    }

    // ---------------- 4. the Void Key by the ally path ----------------

    [Fact]
    public async Task AllSixArtifacts_ByTheAllyPath_ReachTheVoidKey()
    {
        foreach (var a in OtherFive) Story.CollectArtifact(a);
        Story.CollectedArtifacts.Should().HaveCount(5);
        Story.SetStoryFlag("noctura_ally", true);

        await Ally(Hero("NxVoid"), OldGodType.Noctura);

        Story.CollectedArtifacts.Should().Contain(ArtifactType.ShadowCrown);
        Story.CollectedArtifacts.Should().Contain(ArtifactType.VoidKey, "the sixth artifact opens the way to the Void Key");
        Story.HasStoryFlag("void_key_obtained").Should().BeTrue();
        ArtifactSystem.Instance.HasVoidKey().Should().BeTrue();
    }

    // ---------------- NX-A: the Void Key at the first location entry after load ----------------

    private static async Task<bool> EntryCheck(Character player)
    {
        var method = typeof(ArtifactSystem).GetMethod("CheckVoidKeyOnEntry", BindingFlags.Public | BindingFlags.Instance);
        method.Should().NotBeNull("a location entry checks for six artifacts and no Void Key");
        try { return await (Task<bool>)method!.Invoke(ArtifactSystem.Instance, new object[] { player, Term(new MemoryStream()) })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    /// <summary>An allied save holding the other five artifacts, loaded: six artifacts after the Crown, no Void Key.</summary>
    private static Character LoadFiveArtifactAlliedSave(string name)
    {
        var save = OldAlliedSave(true, OtherFive);
        SaveSystem.Instance.RestoreStorySystems(save);
        var c = Hero(name);
        GameEngine.RunStatRewardMigrations(c, save);
        Story.CollectedArtifacts.Should().HaveCount(6).And.Contain(ArtifactType.ShadowCrown);
        Story.CollectedArtifacts.Should().NotContain(ArtifactType.VoidKey, "the load grant shows no screen");
        return c;
    }

    [Fact]
    public async Task AFiveArtifactAlliedSave_GetsTheCrownAtLoad_ThenTheVoidKeyOnTheNextLocationEntry()
    {
        string manager = Source("Scripts/Systems/LocationManager.cs");
        string enter = StatRewardsDecisions1115Tests.Body(manager, "public async Task EnterLocation(GameLocation locationId, Character player)");
        int check = enter.IndexOf("await ArtifactSystem.Instance.CheckVoidKeyOnEntry(player, terminal);", StringComparison.Ordinal);
        check.Should().BeGreaterThan(0, "every location entry runs the check with the session's terminal");
        check.Should().BeLessThan(enter.IndexOf("await currentLocation.EnterLocation(player, terminal);", StringComparison.Ordinal),
            "the check runs before the location does");

        var c = LoadFiveArtifactAlliedSave("NxEntry");
        long dex = c.GetBaseStat(StatKind.Dexterity);
        (await EntryCheck(c)).Should().BeTrue();
        Story.CollectedArtifacts.Should().Contain(ArtifactType.VoidKey);
        Story.CollectedArtifacts.Should().HaveCount(7);
        Story.HasStoryFlag("void_key_obtained").Should().BeTrue();
        Story.CurrentChapter.Should().Be(StoryChapter.TheFinalConfrontation);
        c.GetBaseStat(StatKind.Dexterity).Should().BeGreaterThan(dex, "the Void Key's stats come with it");
    }

    [Fact]
    public async Task ASecondLoad_AndASecondEntry_DoNothingMore()
    {
        var c = LoadFiveArtifactAlliedSave("NxEntryTwice");
        (await EntryCheck(c)).Should().BeTrue();
        (await EntryCheck(c)).Should().BeFalse("the held Void Key stops a second appearance");
        var afterFirst = Bases(c);
        var chapter = Story.CurrentChapter;

        var next = SaveSystem.Instance.SerializeStorySystemsPublic();   // the save written after that session
        var again = StatRewards1115Tests.RoundTrip(c);
        SaveSystem.Instance.RestoreStorySystems(next);
        GameEngine.RunStatRewardMigrations(again, next);
        (await EntryCheck(again)).Should().BeFalse();
        Bases(again).Should().Equal(afterFirst, "the second load and entry grant nothing");
        Story.CollectedArtifacts.Should().HaveCount(7);
        Story.CurrentChapter.Should().Be(chapter);
    }

    [Fact]
    public async Task ASaveHoldingTheVoidKey_IsNotTouched()
    {
        var save = OldAlliedSave(true, OtherFive.Append(ArtifactType.ShadowCrown).Append(ArtifactType.VoidKey).ToArray());
        SaveSystem.Instance.RestoreStorySystems(save);
        var c = Hero("NxHasKey");
        var before = Bases(c);
        var chapter = Story.CurrentChapter;
        GameEngine.RunStatRewardMigrations(c, save);
        (await EntryCheck(c)).Should().BeFalse();
        Bases(c).Should().Equal(before);
        Story.CollectedArtifacts.Should().HaveCount(7);
        Story.CurrentChapter.Should().Be(chapter);
        Story.HasStoryFlag("void_key_obtained").Should().BeFalse("nothing is written");
    }

    [Fact]
    public async Task TheEntryCheck_ReadsOnlyTheSessionsOwnStory()
    {
        foreach (var a in OtherFive.Append(ArtifactType.ShadowCrown)) Story.CollectArtifact(a);   // the fallback story: six, no Key
        var fallback = StoryProgressionSystem.Instance;
        var ctx = new SessionContext { Username = "nx_entry", CharacterKey = "nx_entry" };
        ctx.InitializeSystems();
        SessionContext.Current = ctx;
        try
        {
            var c = Hero("NxEntryOnline");
            (await EntryCheck(c)).Should().BeFalse("this session's story holds no artifacts");
            foreach (var a in OtherFive.Append(ArtifactType.ShadowCrown)) ctx.Story.CollectArtifact(a);
            (await EntryCheck(c)).Should().BeTrue();
            ctx.Story.CollectedArtifacts.Should().Contain(ArtifactType.VoidKey);
        }
        finally { SessionContext.Current = null; }
        fallback.CollectedArtifacts.Should().NotContain(ArtifactType.VoidKey, "another story is never written");
        fallback.HasStoryFlag("void_key_obtained").Should().BeFalse();
    }

    // ---------------- NX-B: a Manwe save collects no artifact ----------------

    [Fact]
    public async Task SavingManwe_ShowsNoRelic_AndCollectsNothing()
    {
        Story.CollectArtifact(ArtifactType.CreatorsEye);   // Maelketh is always defeated by the time Manwe is met
        var c = Hero("NxManweSaved");
        var before = Bases(c);
        GameConfig.Language = "en";
        var output = new MemoryStream();
        var result = await Invoke("HandleBossSaved", c, OldGodType.Manwe, Term(output));
        result.Outcome.Should().Be(BossOutcome.Saved);
        Story.OldGodStates[OldGodType.Manwe].Status.Should().Be(GodStatus.Saved);
        Story.HasStoryFlag("manwe_saved").Should().BeTrue();
        string text = Strip(Encoding.UTF8.GetString(output.ToArray()));
        var boss = OldGodsData.GetGodBossData(OldGodType.Manwe);
        text.Should().NotContain(Loc.GetIn("en", "old_god.entrusts_relic", boss.LocName()), "Manwe has no artifact of his own");
        text.Should().NotContain(Loc.GetIn("en", "artifact.already_possess",
            ArtifactSystem.Instance.GetArtifact(ArtifactType.CreatorsEye)!.LocName()));
        Story.CollectedArtifacts.Should().Equal(ArtifactType.CreatorsEye);
        Bases(c).Should().Equal(before);
    }
}
