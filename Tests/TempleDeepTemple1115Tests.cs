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
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.2.0 Temple gods piece 7, Aurelion: the story fought him on dungeon floor 85 (the boss room)
/// and, looser, at the Temple's Deep Temple. The Deep Temple is now his one site: it shows only when
/// the story's own gate (OldGodBossSystem.CanEncounterBoss) lets the fight start, its result is
/// resolved by the dungeon's Old God handling, floor 85 points back to the Temple, and a save that
/// already resolved him finds a Halls of Memory entry, never a fight.
/// </summary>
[Collection("SharedGameSingletons")]
public class TempleDeepTemple1115Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private sealed class Rig : IDisposable
    {
        public TempleLocation Temple = null!;
        public MemoryStream Output = null!;
        public TerminalEmulator Term = null!;
        public Character Hero = null!;
        public string Text()
        {
            Term.StreamWriterInternal?.Flush();
            return Regex.Replace(Encoding.UTF8.GetString(Output.ToArray()), "\u001b\\[[0-9;?]*[A-Za-z]", "").Replace("\r", "");
        }
        public void Dispose() => GodRegistry.SetWorshippedGod(Hero, null);
    }

    private static Rig Make(string name, int level, params string[] lines)
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(lines.Concat(Enumerable.Repeat("", 40))), output);
        var temple = new TempleLocation(term, null!, UsurperRemake.GodSystemSingleton.Instance);
        var hero = StatRewards1115Tests.Fresh(name);
        hero.Level = level;
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(temple, hero);
        return new Rig { Temple = temple, Output = output, Term = term, Hero = hero };
    }

    /// <summary>Sets Old God statuses for one test and puts the story back afterwards.</summary>
    private sealed class StoryScope : IDisposable
    {
        private readonly Dictionary<OldGodType, GodStatus> _before;
        private readonly bool _flag;
        public StoryScope(params (OldGodType God, GodStatus Status)[] states)
        {
            var story = StoryProgressionSystem.Instance;
            _before = story.OldGodStates.ToDictionary(kv => kv.Key, kv => kv.Value.Status);
            _flag = story.HasStoryFlag("aurelion_encountered");
            foreach (var god in story.OldGodStates.Keys.ToList()) story.OldGodStates[god].Status = GodStatus.Imprisoned;
            foreach (var (god, status) in states)
            {
                if (!story.OldGodStates.TryGetValue(god, out var s))
                    story.OldGodStates[god] = s = new OldGodState { Name = god.ToString(), CanBeSaved = true };
                s.Status = status;
            }
        }
        public void Dispose()
        {
            var story = StoryProgressionSystem.Instance;
            foreach (var god in story.OldGodStates.Keys.ToList())
            {
                if (_before.TryGetValue(god, out var st)) story.OldGodStates[god].Status = st;
                else story.OldGodStates.Remove(god);
            }
            story.SetStoryFlag("aurelion_encountered", _flag);
        }
    }

    private static readonly (OldGodType, GodStatus)[] ThreeFaced =
    {
        (OldGodType.Maelketh, GodStatus.Defeated), (OldGodType.Veloura, GodStatus.Saved), (OldGodType.Thorgrim, GodStatus.Defeated)
    };

    private static object? Invoke(object target, string method, params object[] args)
    {
        var m = target.GetType().GetMethod(method, F) ?? target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Static);
        m.Should().NotBeNull($"{method} must exist");
        try { return m!.Invoke(target, args); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static List<string> TopKeys(Rig r) =>
        ((List<TempleLocation.TempleMenuItem>)Invoke(r.Temple, "TopMenuItems")!).Select(i => i.Key).ToList();

    private static bool CanEnter(Rig r) => (bool)Invoke(r.Temple, "CanEnterDeepTemple")!;

    private static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "usurper-reloaded.csproj"))) dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Src(string rel) => File.ReadAllText(Path.Combine(Root(), rel));

    private static string Body(string src, string signature)
    {
        int start = src.IndexOf(signature, StringComparison.Ordinal);
        start.Should().BeGreaterThanOrEqualTo(0, signature);
        var next = new Regex(@"\n    (private|public|internal|protected) ").Match(src, start + signature.Length);
        return next.Success ? src.Substring(start, next.Index - start) : src.Substring(start);
    }

    // ---------------- The gate is the story's own ----------------

    [Theory]
    [InlineData(60, 3, false)]   // the old gate (level 55) let this in, then Aurelion refused the fight
    [InlineData(80, 3, true)]
    [InlineData(80, 2, false)]
    public void TheDeepTemple_ShowsOnlyWhenTheFightCanStart(int level, int faced, bool shown)
    {
        using var story = new StoryScope(ThreeFaced.Take(faced).ToArray());
        using var r = Make("TdtGate" + level + faced, level);
        CanEnter(r).Should().Be(shown);
        CanEnter(r).Should().Be(OldGodBossSystem.Instance.CanEncounterBoss(r.Hero, OldGodType.Aurelion), "the story's own gate");
        TopKeys(r).Contains("T").Should().Be(shown);
    }

    [Theory]
    [InlineData(GodStatus.Defeated, "temple.aurelion_altar_dark")]
    [InlineData(GodStatus.Saved, "temple.aurelion_warm_light")]
    [InlineData(GodStatus.Allied, "temple.aurelion_warm_light")]
    public async Task AResolvedAurelion_IsAHallsOfMemoryEntry_NeverAFight(GodStatus status, string memoryKey)
    {
        using var story = new StoryScope(ThreeFaced.Append((OldGodType.Aurelion, status)).ToArray());
        StoryProgressionSystem.Instance.SetStoryFlag("aurelion_encountered", false);
        using var r = Make("TdtDone" + status, 90);
        CanEnter(r).Should().BeFalse();
        TopKeys(r).Should().Contain("H").And.NotContain("T");
        var memory = await (Task<List<TempleLocation.TempleMenuItem>>)Invoke(r.Temple, "RoomItems", TempleLocation.TempleRoom.Memory)!;
        memory.Select(i => i.Key).Should().Contain("A");
        ((Func<Task>?)Invoke(r.Temple, "RoomAction", TempleLocation.TempleRoom.Memory, "A")).Should().NotBeNull();

        bool leaves = await (Task<bool>)Invoke(r.Temple, "RouteTopLevel", "T")!;
        leaves.Should().BeFalse();
        string text = Regex.Replace(r.Text(), @"\s+", " ");
        text.Should().Contain(Regex.Replace(Loc.Get("temple.moved.aurelion"), @"\s+", " "));
        text.Should().NotContain(Loc.Get("temple.approach_light").Trim()).And.NotContain(Loc.Get("temple.aurelion_glow"));
        StoryProgressionSystem.Instance.HasStoryFlag("aurelion_encountered").Should().BeFalse("no fight begins");

        await (Task)Invoke(r.Temple, "ShowAurelionMemory")!;
        r.Text().Should().Contain(Loc.Get(memoryKey));
        StoryProgressionSystem.Instance.OldGodStates[OldGodType.Aurelion].Status.Should().Be(status, "memory only");
    }

    // ---------------- Floor 85 points back to the Temple ----------------

    [Theory]
    [InlineData(GodStatus.Imprisoned, true)]
    [InlineData(GodStatus.Dormant, true)]
    [InlineData(GodStatus.Defeated, false)]
    [InlineData(GodStatus.Saved, false)]
    [InlineData(GodStatus.Allied, false)]
    [InlineData(GodStatus.Consumed, false)]
    [InlineData(GodStatus.Awakened, false)]   // an older save's quest still ends on his floor
    public void Floor85_PointsBackToTheTemple_WhileAurelionWaitsThere(GodStatus status, bool awaits)
    {
        using var story = new StoryScope((OldGodType.Aurelion, status));
        DungeonLocation.AurelionAwaitsAtTemple().Should().Be(awaits);
    }

    [Fact]
    public void Floor85_TheBossRoomPointsBackBeforeAnyFight()
    {
        var body = Body(Src("Scripts/Locations/DungeonLocation.cs"), "private async Task FightRoomMonsters(");
        int check = body.IndexOf("if (currentDungeonLevel == 85 && AurelionAwaitsAtTemple())", StringComparison.Ordinal);
        int fight = body.IndexOf("await TryOldGodBossEncounter(player!, room);", StringComparison.Ordinal);
        check.Should().BeGreaterThan(0);
        fight.Should().BeGreaterThan(check, "the pointer comes before the Old God fight");
        var branch = body.Substring(check, fight - check);
        branch.Should().Contain("Loc.Get(\"dungeon.aurelion_at_temple\")").And.Contain("Loc.Get(\"dungeon.aurelion_at_temple_hint\")").And.Contain("return;");
    }

    // ---------------- The Temple's fight resolves as the dungeon's does ----------------

    [Fact]
    public void TheDeepTempleFight_IsResolvedByTheDungeonsOldGodHandling()
    {
        var temple = Src("Scripts/Locations/TempleLocation.cs");
        var enter = Body(temple, "private async Task EnterDeepTemple(");
        enter.Should().Contain("await bossSystem.CompleteSaveQuest(currentPlayer, OldGodType.Aurelion, terminal);")
            .And.Contain("await ResolveAurelionAtTemple(saveResult);")
            .And.Contain("await bossSystem.StartBossEncounter(currentPlayer, OldGodType.Aurelion, terminal);")
            .And.Contain("await ResolveAurelionAtTemple(result);");
        enter.IndexOf("await ResolveAurelionAtTemple(result);", StringComparison.Ordinal)
            .Should().BeGreaterThan(enter.IndexOf("StartBossEncounter", StringComparison.Ordinal));
        Body(temple, "private async Task ResolveAurelionAtTemple(").Should().Contain("await dungeon.ResolveOldGodAtTemple(result, currentPlayer, terminal);");
        Body(Src("Scripts/Locations/DungeonLocation.cs"), "internal async Task ResolveOldGodAtTemple(")
            .Should().Contain("await HandleGodEncounterResult(result, player, term);");
        Body(temple, "private bool CanEnterDeepTemple(").Should()
            .Contain("OldGodBossSystem.Instance.CanEncounterBoss(currentPlayer, OldGodType.Aurelion)");
    }

    [Fact]
    public async Task ResolveOldGodAtTemple_RunsTheDungeonHandling()
    {
        var output = new MemoryStream();
        var term = new TerminalEmulator(new LineStream(Enumerable.Repeat("", 10)), output);
        var hero = StatRewards1115Tests.Fresh("TdtResolve");
        var result = new BossEncounterResult { Success = true, Outcome = BossOutcome.Fled, God = OldGodType.Aurelion };
        await new DungeonLocation().ResolveOldGodAtTemple(result, hero, term);
        term.StreamWriterInternal?.Flush();
        Encoding.UTF8.GetString(output.ToArray()).Should().Contain(Loc.Get("dungeon.god_fled_retreat"));
    }

    // ---------------- The journal and the hints send the player to the Temple ----------------

    [Fact]
    public void TheJournal_SendsThePlayerToTheDeepTempleForAurelion()
    {
        var journal = Src("Scripts/Systems/JournalSystem.cs");
        journal.Should().Contain("if (god.Value.God == OldGodType.Aurelion)   // 1.2.0 Temple gods piece 7: fought in the Deep Temple\n                    return new JournalNextStep { LocKey = \"journal.next_god_temple\", Args = new object[] { godName } };");
        journal.Should().Contain("? Loc.Get(\"journal.line_next_god_temple\", godName)");
        Loc.Get("dungeon.guide_aurelion").Should().Contain("Deep Temple");
        Loc.Get("dungeon.hint_aurelion").Should().Contain("Deep Temple");
    }

    [Fact]
    public void TheAurelionKeys_AreInAllFiveFiles()
    {
        string[] keys =
        {
            "temple.memory.aurelion", "temple.moved.aurelion", "dungeon.aurelion_at_temple", "dungeon.aurelion_at_temple_hint",
            "journal.next_god_temple", "journal.line_next_god_temple"
        };
        foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(Root(), "Localization", lang + ".json")));
            foreach (var k in keys)
                doc.RootElement.TryGetProperty(k, out _).Should().BeTrue($"{lang} has {k}");
        }
    }
}
