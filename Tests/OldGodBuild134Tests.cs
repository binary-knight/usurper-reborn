using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake.Data;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.4: one build path for Old God fights. BuildBossFight is the build RunBossCombat and the balance
/// simulator share. These hold it to the fixture recorded through RunBossCombat at 7ef83c36 for every
/// boss row, artifact counts 0 and 4 and two dialogues; check it reads no singleton and leaves the player
/// as it was; and check RunBossCombat still hands the fight what the entry builds.
/// </summary>
[Collection("SharedGameSingletons")]
public class OldGodBuild134Tests
{
    private const BindingFlags Inst = BindingFlags.NonPublic | BindingFlags.Instance;

    private static BossDialogue Dialogue((string Field, object Value)[] fields)
    {
        var d = new BossDialogue();
        foreach (var (field, value) in fields) typeof(BossDialogue).GetProperty(field)!.SetValue(d, value);
        return d;
    }

    private static (string Field, object Value)[] DialogueFields(string name) =>
        OldGodBuild134Support.Dialogues.Single(d => d.Name == name).Fields;

    private static string[] Build(OldGodBossData data, int artifacts, string dialogue, out BossFight fight)
    {
        var hero = OldGodBuild134Support.Hero();
        hero.RecalculateStats();   // as RunBossCombat does before the build
        fight = OldGodBossSystem.BuildBossFight(data, artifacts, Dialogue(DialogueFields(dialogue)), hero);
        return OldGodBuild134Support.Describe(fight.Monster, fight.Context, hero);
    }

    private static string[] FightOnly(string[] lines) => lines.Where(l => !l.StartsWith("hero.")).ToArray();

    // ---------- OG2: the entry equals the fixture ----------

    [Fact]
    public void TheEntry_BuildsEveryCase_AsRunBossCombatDidAt7ef83c36()
    {
        var fixture = OldGodBuild134Support.ReadFixture();
        fixture.Should().HaveCount(32);
        foreach (var (row, data) in OldGodBuild134Support.Rows())
            foreach (var n in OldGodBuild134Support.ArtifactCounts)
                foreach (var (name, _) in OldGodBuild134Support.Dialogues)
                {
                    var key = OldGodBuild134Support.CaseKey(row, n, name);
                    var before = OldGodBuild134Support.SetArtifacts(n);   // the hero's recalculation sees what the recording saw
                    try
                    {
                        var lines = Build(data, n, name, out var fight);
                        lines.Should().Equal(fixture[key], $"{key}: the entry builds the fight RunBossCombat set up");
                        fight.Context.BossData.Should().BeSameAs(data, $"{key}: the context carries the row it was built from");
                    }
                    finally { OldGodBuild134Support.RestoreArtifacts(before); }
                }
    }

    [Fact]
    public void TheTypeEntry_BuildsTheGodsOwnRow()
    {
        var fixture = OldGodBuild134Support.ReadFixture();
        foreach (var type in Enum.GetValues<OldGodType>())
        {
            var hero = OldGodBuild134Support.Hero();
            hero.RecalculateStats();
            var fight = OldGodBossSystem.BuildBossFight(type, 4, Dialogue(DialogueFields("all")), hero);
            FightOnly(OldGodBuild134Support.Describe(fight.Monster, fight.Context, hero))
                .Should().Equal(FightOnly(fixture[OldGodBuild134Support.CaseKey(type.ToString(), 4, "all")]), type.ToString());
        }
    }

    [Fact]
    public void TheDialogueValue_CarriesEveryCombatModifiersField()
    {
        var modsType = typeof(OldGodBossSystem).GetNestedType("CombatModifiers", BindingFlags.NonPublic)!;
        var modsProps = modsType.GetProperties().Select(p => p.Name).OrderBy(n => n).ToArray();
        typeof(BossDialogue).GetProperties().Where(p => p.Name != "EqualityContract").Select(p => p.Name).OrderBy(n => n)
            .Should().Equal(modsProps, "a modifier the dialogue sets must reach the build");
        DialogueFields("all").Select(f => f.Field).OrderBy(n => n).Should().Equal(modsProps, "the moved dialogue moves every field");

        var mods = Activator.CreateInstance(modsType, nonPublic: true)!;
        foreach (var (field, value) in DialogueFields("all")) modsType.GetProperty(field)!.SetValue(mods, value);
        var copied = (BossDialogue)modsType.GetMethod("ToDialogue")!.Invoke(mods, null)!;
        copied.Should().Be(Dialogue(DialogueFields("all")));
        var fresh = Activator.CreateInstance(modsType, nonPublic: true)!;
        ((BossDialogue)modsType.GetMethod("ToDialogue")!.Invoke(fresh, null)!).Should().Be(BossDialogue.None, "no answer is no modifier");
        DialogueFields("all").Should().OnlyContain(f => !Equals(typeof(BossDialogue).GetProperty(f.Field)!.GetValue(BossDialogue.None), f.Value));
    }

    // ---------- OG3: the entry is pure ----------

    private static string Snapshot(object o)
    {
        var lines = new List<string>();
        foreach (var p in o.GetType().GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (!p.CanRead || p.GetIndexParameters().Length > 0 || typeof(Delegate).IsAssignableFrom(p.PropertyType)) continue;
            object? v;
            try { v = p.GetValue(o); } catch (TargetInvocationException) { v = "<throws>"; }
            lines.Add(p.Name + "=" + OldGodBuild134Support.Format(v));
        }
        foreach (var f in o.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
            if (!typeof(Delegate).IsAssignableFrom(f.FieldType)) lines.Add(f.Name + "=" + OldGodBuild134Support.Format(f.GetValue(o)));
        return string.Join("\n", lines);
    }

    private static string SingletonState()
    {
        var mods = typeof(OldGodBossSystem).GetField("activeCombatModifiers", Inst)!.GetValue(OldGodBossSystem.Instance)!;
        var dialogue = (BossDialogue)mods.GetType().GetMethod("ToDialogue")!.Invoke(mods, null)!;
        var story = StoryProgressionSystem.Instance;
        return string.Join("\n",
            "artifacts=" + string.Join(",", story.CollectedArtifacts.OrderBy(a => a)),
            "artifactCount=" + ArtifactSystem.Instance.GetCollectedCount(),
            "godStates=" + string.Join(",", Enum.GetValues<OldGodType>().Select(t => t + ":" + OldGodBuild134Support.Format(story.OldGodStates.TryGetValue(t, out var st) ? (object?)st.Status : null))),
            "instanceDialogue=" + dialogue,
            "isManweBattle=" + CombatEngine.IsManweBattle);
    }

    [Fact]
    public void TheEntry_ReadsNoSingleton_AndLeavesThePlayerAsItWas()
    {
        var fixture = OldGodBuild134Support.ReadFixture();
        var heldBefore = OldGodBuild134Support.SetArtifacts(0);
        try
        {
            // the singletons say one thing, the arguments another: the build follows the arguments
            foreach (var (singletonCount, singletonDialogue, argCount, argDialogue) in new[] { (4, "all", 0, "none"), (0, "none", 4, "all") })
            {
                OldGodBuild134Support.SetArtifacts(singletonCount);
                OldGodBuild134Support.SetModifiers(OldGodBossSystem.Instance, DialogueFields(singletonDialogue));
                foreach (var (row, data) in OldGodBuild134Support.Rows())
                {
                    var hero = OldGodBuild134Support.Hero();
                    hero.RecalculateStats();
                    string heroBefore = Snapshot(hero), stateBefore = SingletonState();

                    var fight = OldGodBossSystem.BuildBossFight(data, argCount, Dialogue(DialogueFields(argDialogue)), hero);

                    var key = OldGodBuild134Support.CaseKey(row, argCount, argDialogue);
                    FightOnly(OldGodBuild134Support.Describe(fight.Monster, fight.Context, hero))
                        .Should().Equal(FightOnly(fixture[key]), $"{key}: built from the arguments, with the singletons at {singletonCount} artifacts and dialogue {singletonDialogue}");
                    Snapshot(hero).Should().Be(heroBefore, $"{key}: the build reads the player and writes nothing on it");
                    SingletonState().Should().Be(stateBefore, $"{key}: the build writes no singleton");
                }
            }
        }
        finally
        {
            OldGodBuild134Support.SetModifiers(OldGodBossSystem.Instance, DialogueFields("none"));
            OldGodBuild134Support.RestoreArtifacts(heldBefore);
        }
    }

    [Fact]
    public void TheEntry_DoesNotRecalculateThePlayer()
    {
        // a hero whose stats a recalculation would change (a blessing on top of BaseStrength 100): the build
        // reads them as they are; recalculating is the caller's step, as RunBossCombat does it
        var hero = OldGodBuild134Support.Hero();
        hero.Strength = 205;
        hero.TempCritChanceBonus = 3; hero.HasBloodlust = false;
        string before = Snapshot(hero);
        foreach (var (_, data) in OldGodBuild134Support.Rows())
            OldGodBossSystem.BuildBossFight(data, 4, Dialogue(DialogueFields("all")), hero);
        Snapshot(hero).Should().Be(before, "the build leaves every field of the player as it was");
    }

    // ---------- RunBossCombat goes through the entry ----------

    [Fact]
    public async Task RunBossCombat_HandsTheFight_WhatTheEntryBuilds()
    {
        foreach (var (row, data) in OldGodBuild134Support.Rows())
        {
            var viaGame = await OldGodBuild134Support.RecordThroughRunBossCombat(data, 4, DialogueFields("all"));
            var before = OldGodBuild134Support.SetArtifacts(4);
            try { Build(data, 4, "all", out _).Should().Equal(viaGame, row); }
            finally { OldGodBuild134Support.RestoreArtifacts(before); }
        }
        var body = RunBossCombatSource();
        body.Should().Contain("BuildBossFight(boss, ArtifactSystem.Instance.GetCollectedCount(), activeCombatModifiers.ToDialogue(), player, CountLivingFighters(player, dungeonTeammates))");
        body.Should().NotContain("new BossCombatContext", "the context is built in one place");
        body.Should().NotContain("CreateBossMonster(", "the god is built in one place");
    }

    [Fact]
    public async Task RunBossCombat_SetsCanSave_FromTheSaveArtifactHeld()
    {
        // the one field the build leaves to its caller: Veloura's save needs the Soulweaver's Loom
        var veloura = OldGodsData.GetGodBossData(OldGodType.Veloura);
        var heldBefore = new HashSet<ArtifactType>(StoryProgressionSystem.Instance.CollectedArtifacts);
        bool? canSave = null;
        try
        {
            StoryProgressionSystem.Instance.CollectedArtifacts.Clear();
            StoryProgressionSystem.Instance.CollectedArtifacts.Add(ArtifactType.SoulweaversLoom);
            OldGodBossSystem.FightSeamForTests = (_, ctx) => canSave = ctx.CanSave;
            var hero = OldGodBuild134Support.Hero();
            await (Task<BossEncounterResult>)typeof(OldGodBossSystem).GetMethod("RunBossCombat", Inst)!
                .Invoke(new OldGodBossSystem(), new object[] { hero, veloura, new TerminalEmulator(new MemoryStream(), new MemoryStream()) })!;
        }
        finally
        {
            OldGodBossSystem.FightSeamForTests = null;
            OldGodBuild134Support.RestoreArtifacts(heldBefore);
        }
        canSave.Should().BeTrue("the Loom is held");
        var plain = OldGodBuild134Support.Hero();
        OldGodBossSystem.BuildBossFight(veloura, 1, BossDialogue.None, plain).Context.CanSave
            .Should().BeFalse("the build counts a save artifact as not held");
        OldGodBossSystem.BuildBossFight(OldGodType.Noctura, 0, BossDialogue.None, plain).Context.CanSave
            .Should().BeTrue("a god whose save needs no artifact");
    }

    private static string RunBossCombatSource()
    {
        var src = File.ReadAllText(Path.Combine(OldGodBuild134Support.Root(), "Scripts", "Systems", "OldGodBossSystem.cs"));
        int start = src.IndexOf("private async Task<BossEncounterResult> RunBossCombat(", StringComparison.Ordinal);
        int end = src.IndexOf("return await ConvertToBossResult(", start, StringComparison.Ordinal);
        start.Should().BeGreaterThan(0); end.Should().BeGreaterThan(start);
        return src[start..end];
    }
}
