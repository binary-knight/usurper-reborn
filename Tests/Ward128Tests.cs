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
using UsurperRemake.Systems;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8 (H3): the ward on hired NPCs and dungeon party members. Rows 1 to 8 of the brief. The world and
/// fight scenarios run in a copy of the game of their own (IsolatedGame) on H2's seeded world harness; the
/// Team Corner and dungeon rows run on the scripted terminals of the existing rigs. No live data.
/// </summary>
[Collection("SharedGameSingletons")]
public class Ward128Tests
{
    private const BindingFlags F = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
    private static void Run(string scenario, string arg = "") => IsolatedGame.Run(scenario, arg);

    // GameConfig.MaxNameLength (30) characters, the longest name an NPC can have.
    private const string LongName = "Eszterhazy Kalandor Bajnoka Ur";
    private static readonly string[] Languages = { "en", "es", "fr", "hu", "it" };
    private static readonly string[] Keys =
    {
        "oath_mark.hire_laid", "oath_mark.hire_holds", "oath_mark.team_view", "oath_mark.party_joins", "oath_mark.caught", "oath_mark.flares", "oath_mark.breaks",
        "oath_mark.last_1", "oath_mark.last_2", "oath_mark.last_3", "oath_mark.gone", "oath_mark.memory", "oath_mark.lifts", "oath_mark.lifts_team", "oath_mark.years_return",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    private static string Code(params string[] parts)
    {
        string src = File.ReadAllText(Path.Combine(new[] { RepoRoot(), "Scripts" }.Concat(parts).ToArray()));
        src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(src, @"//[^\n]*", "");
    }

    private static string Body(string src, string signature)
    {
        int i = src.IndexOf(signature, StringComparison.Ordinal);
        i.Should().BeGreaterThan(0, signature);
        int open = src.IndexOf('{', i), depth = 0, j = open;
        for (; j < src.Length; j++)
        {
            if (src[j] == '{') depth++;
            else if (src[j] == '}' && --depth == 0) break;
        }
        return src.Substring(open, j - open + 1);
    }

    // ---------- row 1: away from the player a team NPC never dies permanently ----------

    [Fact] public void Row1_ATeamNpc_IsNeverPermanentAtAWorldSite_ItsTwinIsAtTodaysRate() => Run(nameof(RecruitPoolRuns.WardRow1));

    [Fact]
    public void Row1_EveryWorldSite_GoesThroughTheOnePlace_AndOnlyThePlayersFightIsAtThePlayersSide()
    {
        string sim = Code("Systems", "WorldSimulator.cs");
        // the eight world simulator sites, each through MarkNPCDead with no fifth argument
        var calls = Regex.Matches(sim, @"(?<!bool )\bMarkNPCDead\(([^;]*)\);").Select(m => m.Groups[1].Value).ToList();
        calls.Count.Should().Be(8);
        calls.Should().OnlyContain(c => !c.Contains("atPlayersSide"));
        // one roll, called by the one place under the ward, called by MarkNPCDead alone
        Regex.Matches(sim, @"\bRollPermadeath\(").Count.Should().Be(2);
        Regex.Matches(sim, @"\bRollPermadeathUnderWard\(").Count.Should().Be(2);
        Body(sim, "internal bool RollPermadeathUnderWard(").Should().Contain("bool permanent = RollPermadeath(npc, baseChance);")
            .And.Contain("if (permanent && !atPlayersSide && WardSystem.IsWarded(npc)) return false;");
        Body(sim, "public bool MarkNPCDead(").Should().Contain("bool permadeath = RollPermadeathUnderWard(npc, basePermadeathChance, atPlayersSide);");
        Regex.Matches(sim, @"IsPermaDead = true;").Count.Should().Be(1);
        // no other file rolls, marks a death at the player's side, or sets the permanent flag
        foreach (var file in Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories))
        {
            string name = Path.GetFileName(file);
            string src = Regex.Replace(File.ReadAllText(file), @"//[^\n]*", "");
            if (name != "WorldSimulator.cs")
            {
                src.Should().NotContain("RollPermadeath", name);
                Regex.IsMatch(src, @"IsPermaDead = true;").Should().BeFalse(name);
            }
            if (name != "WorldSimulator.cs" && name != "CombatEngine.cs") src.Should().NotContain("MarkNPCDead(", name);
        }
        string fight = Code("Systems", "CombatEngine.cs");
        Regex.Matches(fight, @"\bMarkNPCDead\(").Count.Should().Be(1);
        Body(fight, "private async Task HandleNpcTeammateDeath(").Should().Contain("killerName, deathLocation, atPlayersSide: true)");
    }

    // ---------- row 2: a party-only member gets nothing in the world or from old age ----------

    [Fact] public void Row2_APartyOnlyNpc_GetsNoProtectionInTheWorld_OrFromOldAge() => Run(nameof(RecruitPoolRuns.WardRow2));

    // ---------- row 3: the chance at the player's side is unchanged ----------

    private static Dictionary<string, string> Fields(string line) =>
        line.Split(' ').Where(p => p.Contains('=')).ToDictionary(p => p.Split('=')[0], p => p.Split('=')[1]);

    [Theory]
    // today's formula: 2% x (1 - level x 1.5%), never below 1%. Level 5: 1.85%, 370 of 20,000 (sigma 19).
    // Level 40: 0.8%, lifted to the 1% floor, 200 of 20,000 (sigma 14). Bands of five sigma.
    [InlineData(5, 275, 465)]
    [InlineData(40, 130, 270)]
    public void Row3_TwentyThousandFalls_ArePermanentAtTodaysRate_AndTheWardTakesNoDraw(int level, int low, int high)
    {
        var on = Fields(IsolatedGame.Run(nameof(RecruitPoolRuns.WardFight), $"77,{level},20000,on"));
        var off = Fields(IsolatedGame.Run(nameof(RecruitPoolRuns.WardFight), $"77,{level},20000,off"));
        int.Parse(on["perm"]).Should().BeInRange(low, high);
        // the same seeded falls with the switch off: the same permanent deaths, the same draws in the same
        // order from the world simulator's random, and the shared random left where it was
        on["perm"].Should().Be(off["perm"]);
        on["simDraws"].Should().Be(off["simDraws"]);
        on["simHash"].Should().Be(off["simHash"]);
        on["shared"].Should().Be(off["shared"]);
        on["rows"].Should().NotBe(off["rows"], "the ward rows are shown with the switch on");
    }

    /// <summary>Three seeded fights of 300 falls of a level 5 hire, made on the base b07b3596 with this harness
    /// (receipts/golden-fights-base.txt) and again, equal, on a90e7274 after the F5 merge: permanent deaths, draws and
    /// every row shown.</summary>
    private static readonly Dictionary<string, string> FightGolden = new()
    {
        ["101,5,300"] = "perm=5 simDraws=300 simHash=55ffeec56c8a7bdf shared=f8cb1457666357fc rows=19ae2b594182f0e6",
        ["202,5,300"] = "perm=6 simDraws=300 simHash=b1b8e6e51359ba80 shared=716233e6aa407607 rows=19ae2b594182f0e6",
        ["303,12,300"] = "perm=5 simDraws=300 simHash=d006b56abf2e893e shared=0efb426bca944c98 rows=19ae2b594182f0e6",
    };

    [Theory]
    [InlineData("101,5,300")]
    [InlineData("202,5,300")]
    [InlineData("303,12,300")]
    public void Row3_ASeededFight_DrawsAsAtTheBase_WithTheSwitchOn(string fight)
    {
        var golden = Fields(FightGolden[fight]);
        var on = Fields(IsolatedGame.Run(nameof(RecruitPoolRuns.WardFight), fight + ",on"));
        int.Parse(on["perm"]).Should().BeGreaterThan(0, "the fight must hold a permanent fall, or the scene is not reached");
        foreach (var field in new[] { "perm", "simDraws", "simHash", "shared" })
            on[field].Should().Be(golden[field], field);
    }

    // ---------- row 4: the rows of a fall ----------

    [Fact] public void Row4_AFallThatIsNotPermanent_ShowsTheCaughtRowOnce_AndNoScene() => Run(nameof(RecruitPoolRuns.WardRow4), "caught");
    [Fact] public void Row4_APermanentFall_ShowsTheSceneTheNewsAndTheMemory_OnceEach() => Run(nameof(RecruitPoolRuns.WardRow4), "permanent");
    [Fact] public void Row4_ASpouseTheWardCaught_GetsNoRowOfLoss_AndWithTheSwitchOffTheBaseRows() => Run(nameof(RecruitPoolRuns.WardRow4), "spouse");
    [Theory]
    [InlineData("spouse")]
    [InlineData("lover")]
    [InlineData("affair")]
    public void Row9_Caught_TheRomanceRecordIsKept_LostForGood_ItIsCleared_SwitchOff_AsAtTheBase(string bond) =>
        Run(nameof(RecruitPoolRuns.WardRow9), bond);

    [Fact]
    public void Row9_TheCleanup_HasOneSiteInTheFight_UnderTheCaughtTest()
    {
        string body = Body(Code("Systems", "CombatEngine.cs"), "private async Task HandleNpcTeammateDeath(");
        Regex.Matches(body, @"OnNPCPermadied\(").Count.Should().Be(2);
        Regex.IsMatch(body, @"if \(!caughtByWard\)\s*\{\s*romanceTracker\.OnNPCPermadied\(npcId\);\s*NPCMarriageRegistry\.Instance\.OnNPCPermadied\(npcId\);\s*\}").Should().BeTrue();
        body.Should().Contain("bool caughtByWard = warded && !wasPermadeath;");
    }

    [Fact] public void Row4_APartyOnlyNpc_IsWardedInTheFight_AsATeamMemberIs() => Run(nameof(RecruitPoolRuns.WardRow4), "party");
    [Fact] public void Row4_TheGriefForACloseFriend_BeginsAfterTheScene_AsBefore() => Run(nameof(RecruitPoolRuns.WardRow4), "friend");

    [Fact]
    public void Row3_TheLastLine_IsPickedByAStableHashOfTheId_TakesNoDraw_AndEveryLineIsReached()
    {
        // no draw: the shared random of this thread stands where it stood, and the same ID gives the same rows.
        // (The fight digests above count every draw of the world simulator's random; their "shared" field sees
        // the shared random only up to the fight's first pause, after which the fight runs on another thread.
        // The scene comes after that pause, so its rows are asked here, on this thread.)
        RecruitPoolWorld.SeedSharedRandomOnThisThread(5);
        ulong sharedBefore = RecruitPoolWorld.SharedState();
        for (int i = 0; i < 50; i++)
            WardSystem.BreakSceneRows("A Name", "npc_" + i).Should().Equal(WardSystem.BreakSceneRows("A Name", "npc_" + i));
        RecruitPoolWorld.SharedState().Should().Be(sharedBefore);

        // FNV-1a, 32 bit, of the ID: fixed values, the same in every process
        WardSystem.LastLineVariant("npc_h2_1").Should().Be(WardSystem.LastLineVariant("npc_h2_1"));
        WardSystem.LastLineVariant("").Should().Be(2);          // 2166136261 % 3 = 1
        WardSystem.LastLineVariant(null).Should().Be(2);
        WardSystem.LastLineVariant("a").Should().Be((int)(unchecked((2166136261u ^ 'a') * 16777619u) % 3) + 1);
        var seen = Enumerable.Range(0, 200).Select(i => WardSystem.LastLineVariant("npc_" + i)).Distinct().OrderBy(v => v).ToList();
        seen.Should().Equal(1, 2, 3);
        WardSystem.LastLineKey("a").Should().Be("oath_mark.last_" + WardSystem.LastLineVariant("a"));
        string src = Code("Systems", "WardSystem.cs");
        src.Should().NotContain("Random").And.NotContain("GetHashCode");
    }

    // ---------- row 5: hiring, the Team Corner, joining the party, leaving ----------

    private static async Task WithNpcs(IEnumerable<NPC> npcs, Func<Task> body)
    {
        var list = npcs.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(list);
        try { await body(); }
        finally { foreach (var n in list) NPCSpawnSystem.Instance.ActiveNPCs.Remove(n); }
    }

    private static async Task WithTeam(string team, bool wardOn, Func<Task> body)
    {
        bool before = GameConfig.WardEnabled;
        string lang = GameConfig.Language;
        RelationshipSystem.Instance.Reset();
        RomanceTracker.Instance.Reset();
        GameConfig.WardEnabled = wardOn;
        GameConfig.Language = "en";
        WorldSimulator.RegisterPlayerTeam(team);
        try { await body(); }
        finally
        {
            WorldSimulator.UnregisterPlayerTeam(team);
            GameConfig.WardEnabled = before;
            GameConfig.Language = lang;
        }
    }

    private static NPC Recruit(string id, string name, string team = "") => new NPC
    {
        ID = id, Name1 = name, Name2 = name, Level = 10, Class = CharacterClass.Warrior,
        HP = 100, MaxHP = 100, Strength = 84, Defence = 0, Agility = 0, Team = team, TeamPW = team.Length > 0 ? "pw" : "",
        CurrentLocation = "Main Street",
    };

    private static int Count(string shown, string row) => Regex.Matches(shown, Regex.Escape(row)).Count;

    private static async Task<string> HireShown(NPC recruit, string team, string boss = "Ward Boss")
    {
        var hero = TeamCornerRig.Hero(name: boss, team: team, gold: 1_000_000);
        hero.ID = "ward_boss";
        hero.Level = 10;
        string shown = "";
        await WithNpcs(new[] { recruit }, async () =>
        {
            var rig = new TeamCornerRig(hero, new[] { "Y", "", "", "" });
            var (band, mult) = TeamSystem.GetRecruitmentBand(hero, recruit);
            shown = await rig.Run("ConfirmAndRecruit", recruit, band, mult, TeamSystem.GetRecruitmentCost(hero, recruit));
        });
        return shown;
    }

    [Fact]
    public async Task Row5_AHire_ShowsTheTwoWardRows_AfterTheWage()
    {
        await WithTeam("Ward Hall", true, async () =>
        {
            var recruit = Recruit("ward_hire_1", "Hired Hand");
            string shown = await HireShown(recruit, "Ward Hall");
            recruit.Team.Should().Be("Ward Hall");
            Count(shown, Loc.Get("oath_mark.hire_laid", "Hired Hand")).Should().Be(1);
            Count(shown, Loc.Get("oath_mark.hire_holds")).Should().Be(1);
            int wage = shown.IndexOf(Loc.Get("team.daily_wage", $"{TeamSystem.GetDailyWage(recruit):N0}"), StringComparison.Ordinal);
            int laid = shown.IndexOf(Loc.Get("oath_mark.hire_laid", "Hired Hand"), StringComparison.Ordinal);
            int holds = shown.IndexOf(Loc.Get("oath_mark.hire_holds"), StringComparison.Ordinal);
            int quote = shown.IndexOf(Loc.Get("team.recruit_quote", "Hired Hand"), StringComparison.Ordinal);
            wage.Should().BeGreaterThan(0);
            laid.Should().BeGreaterThan(wage);
            holds.Should().BeGreaterThan(laid);
            quote.Should().BeGreaterThan(holds);
        });
    }

    [Fact]
    public async Task Row5_TheTeamView_NamesTheWard_WhenAHiredMemberIsUnderIt()
    {
        await WithTeam("Ward Hall", true, async () =>
        {
            var member = Recruit("ward_view_1", "View Hand", "Ward Hall");
            await WithNpcs(new[] { member }, async () =>
            {
                string shown = await new TeamCornerRig(TeamCornerRig.Hero(team: "Ward Hall"), new[] { "" }).Run("ShowYourTeamStatus");
                Count(shown, Loc.Get("oath_mark.team_view")).Should().Be(1);
            });
            // a team with no hired member: nothing to name
            string alone = await new TeamCornerRig(TeamCornerRig.Hero(team: "Ward Hall"), new[] { "" }).Run("ShowYourTeamStatus");
            alone.Should().NotContain(Loc.Get("oath_mark.team_view"));
        });
    }

    [Fact]
    public async Task Row5_ASack_ShowsTheLiftRows_WithTheDaysOfTheGrace()
    {
        await WithTeam("Ward Hall", true, async () =>
        {
            var member = Recruit("ward_sack_1", "Sack Hand", "Ward Hall");
            await WithNpcs(new[] { member }, async () =>
            {
                string shown = await new TeamCornerRig(TeamCornerRig.Hero(team: "Ward Hall"), new[] { "sack hand", "y", "" }).Run("SackMember");
                member.Team.Should().BeEmpty();
                // two rows: the mark lifts at once; death by old age is held for the days of the grace (7)
                Count(shown, "The oath-mark lifts from Sack Hand.").Should().Be(1);
                Count(shown, "Their years will find them again in 7 days.").Should().Be(1);
                WardSystem.LiftRows("Sack Hand").Should().Equal("The oath-mark lifts from Sack Hand.",
                    $"Their years will find them again in {GameConfig.RecruitOldAgeGraceDays} days.");
                int sacked = shown.IndexOf(Loc.Get("team.member_sacked", "Sack Hand"), StringComparison.Ordinal);
                int lifts = shown.IndexOf("The oath-mark lifts from Sack Hand.", StringComparison.Ordinal);
                lifts.Should().BeGreaterThan(sacked);
                shown.IndexOf("Their years will find them again", StringComparison.Ordinal).Should().BeGreaterThan(lifts);
            });
        });
    }

    [Fact]
    public async Task Row5_TheLastPlayerLeaving_ShowsTheLiftRowsForThoseWhoStay()
    {
        await WithTeam("Ward Hall", true, async () =>
        {
            var member = Recruit("ward_quit_1", "Stay Hand", "Ward Hall");
            await WithNpcs(new[] { member }, async () =>
            {
                var hero = TeamCornerRig.Hero(name: "Quit Boss", team: "Ward Hall");
                string shown = await new TeamCornerRig(hero, new[] { "y", "" }).Run("QuitTeam");
                hero.Team.Should().BeEmpty();
                Count(shown, "The oath-mark lifts from those who stay behind.").Should().Be(1);
                Count(shown, "Their years will find them again in 7 days.").Should().Be(1);
            });
            // nobody hired: no row
            WorldSimulator.RegisterPlayerTeam("Ward Hall");
            var lone = TeamCornerRig.Hero(name: "Lone Boss", team: "Ward Hall");
            string alone = await new TeamCornerRig(lone, new[] { "y", "" }).Run("QuitTeam");
            alone.Should().NotContain("oath-mark").And.NotContain("Their years");
        });
    }

    private static async Task<string> JoinPartyShown(Character hero, NPC npc, DungeonLocation? dungeon = null, TerminalEmulator? term = null, MemoryStream? output = null)
    {
        output ??= new MemoryStream();
        term ??= new TerminalEmulator(new LineStream(new[] { "1" }), output);
        dungeon ??= new DungeonLocation();
        const BindingFlags NP = BindingFlags.NonPublic | BindingFlags.Instance;
        typeof(BaseLocation).GetField("terminal", NP)!.SetValue(dungeon, term);
        typeof(BaseLocation).GetField("currentPlayer", NP)!.SetValue(dungeon, hero);
        bool pacing = Pacing.Disabled;
        Pacing.Disabled = true;
        try
        {
            await (Task)typeof(DungeonLocation).GetMethod("AddTeammateToParty", NP)!.Invoke(dungeon, new object[] { new List<NPC> { npc } })!;
        }
        finally { Pacing.Disabled = pacing; }
        term.StreamWriterInternal!.Flush();
        return Regex.Replace(Encoding.UTF8.GetString(output.ToArray()), "\u001b\\[[0-9;]*[A-Za-z]", "");
    }

    [Fact]
    public async Task Row5_AnNpcNotOnTheTeam_JoiningTheParty_IsToldOfTheWardOnce_ATeamMemberIsNot()
    {
        await WithTeam("Ward Hall", true, async () =>
        {
            var hero = TeamCornerRig.Hero(name: "Party Boss", team: "Ward Hall");
            hero.Level = 20;
            var spouse = Recruit("ward_party_1", "Party Only");
            var hired = Recruit("ward_party_2", "Party Hired", "Ward Hall");
            await WithNpcs(new[] { spouse, hired }, async () =>
            {
                string shown = await JoinPartyShown(hero, spouse);
                Count(shown, Loc.Get("oath_mark.party_joins", "Party Only")).Should().Be(1);
                shown.IndexOf(Loc.Get("oath_mark.party_joins", "Party Only"), StringComparison.Ordinal)
                    .Should().BeGreaterThan(shown.IndexOf(Loc.Get("dungeon.fight_alongside"), StringComparison.Ordinal));

                string team = await JoinPartyShown(hero, hired);
                team.Should().Contain(Loc.Get("dungeon.npc_joins", "Party Hired"));
                team.Should().NotContain(Loc.Get("oath_mark.party_joins", "Party Hired"), "a team member is under the ward already");
            });
        });
    }

    [Fact]
    public void Row5_ComingBackIntoTheDungeon_IsNotAJoining_TheRowHasOneSite()
    {
        string dungeon = Code("Locations", "DungeonLocation.cs");
        Regex.Matches(dungeon, @"WardSystem\.PartyJoinRow\(").Count.Should().Be(1);
        Body(dungeon, "private async Task AddTeammateToParty(").Should().Contain("WardSystem.PartyJoinRow(");
        Body(dungeon, "private async Task RestoreNPCTeammates(").Should().NotContain("Ward");
    }

    // ---------- row 6: companions ----------

    [Fact]
    public void Row6_TheHelper_RefusesACompanion_AnEcho_AMercenaryAndAPlayer()
    {
        bool before = GameConfig.WardEnabled;
        GameConfig.WardEnabled = true;
        WorldSimulator.RegisterPlayerTeam("Ward Hall");
        try
        {
            var hired = Recruit("ward_c_1", "Hired", "Ward Hall");
            WardSystem.IsWarded(hired).Should().BeTrue();
            // a story companion in a fight is a plain Character with both marks (CompanionSystem builds it)
            var companion = new Character { Name2 = "Lyris", IsCompanion = true, CompanionId = CompanionId.Lyris, Team = "Ward Hall" };
            var party = new List<Character> { hired, companion };
            WardSystem.IsWarded(companion, party).Should().BeFalse();
            // and an NPC object that carries either mark is refused too, on the team and in the party
            var marked = Recruit("ward_c_2", "Marked", "Ward Hall");
            marked.IsCompanion = true;
            WardSystem.IsWarded(marked, new List<Character> { marked }, new[] { "ward_c_2" }).Should().BeFalse();
            var numbered = Recruit("ward_c_3", "Numbered", "Ward Hall");
            numbered.CompanionId = CompanionId.Aldric;
            WardSystem.IsWarded(numbered, new List<Character> { numbered }, new[] { "ward_c_3" }).Should().BeFalse();
            var echo = Recruit("ward_c_4", "Echo", "Ward Hall");
            echo.IsEcho = true;
            WardSystem.IsWarded(echo).Should().BeFalse();
            var mercenary = Recruit("ward_c_5", "Mercenary", "Ward Hall");
            mercenary.IsMercenary = true;
            WardSystem.IsWarded(mercenary).Should().BeFalse();
            WardSystem.IsWarded(new Character { Name2 = "A Player", Team = "Ward Hall" }, party).Should().BeFalse();
            WardSystem.IsWarded(null).Should().BeFalse();
        }
        finally { WorldSimulator.UnregisterPlayerTeam("Ward Hall"); GameConfig.WardEnabled = before; }
    }

    [Fact]
    public void Row6_ACompanionsDeath_HasNoWardRow_AndItsRulesAreUntouched()
    {
        string fight = Code("Systems", "CombatEngine.cs");
        Body(fight, "private async Task HandleCompanionDeath(").Should().NotContain("Ward").And.NotContain("oath_mark.");
        Body(fight, "private async Task HandleMercenaryDeath(").Should().NotContain("Ward").And.NotContain("oath_mark.");
        // a companion is routed to its own handler before the NPC handler, at each of the three dispatch sites
        Regex.Matches(fight, @"else if \((companion|tm)\.IsCompanion && (companion|tm)\.CompanionId\.HasValue\)\s*\{[^}]*await HandleCompanionDeath\(").Count.Should().Be(3);
        Code("Systems", "CompanionSystem.cs").Should().NotContain("Ward");
        // the ward is asked in exactly these files
        var askers = Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories)
            .Where(f => Regex.Replace(File.ReadAllText(f), @"//[^\n]*", "").Contains("WardSystem."))
            .Select(Path.GetFileName).OrderBy(n => n, StringComparer.Ordinal).ToList();
        askers.Should().Equal("CombatEngine.cs", "DungeonLocation.cs", "TeamCornerLocation.cs", "WorldSimulator.cs");
    }

    [Fact] public void Row6_ACompanionThatFalls_ShowsNoWardRow() => Run(nameof(RecruitPoolRuns.WardRow6));

    // ---------- row 7: text ----------

    private static Dictionary<string, string> Lang(string lang) =>
        JsonDocument.Parse(File.ReadAllText(Path.Combine(RepoRoot(), "Localization", lang + ".json"))).RootElement
            .EnumerateObject().Where(p => p.Name.StartsWith("oath_mark.", StringComparison.Ordinal)).ToDictionary(p => p.Name, p => p.Value.GetString()!);

    [Fact]
    public void Row7_FiveLanguages_HoldEveryKey_Translated_WithTheSamePlaceholders()
    {
        var en = Lang("en");
        en.Keys.OrderBy(k => k, StringComparer.Ordinal).Should().Equal(Keys.OrderBy(k => k, StringComparer.Ordinal));
        foreach (var lang in Languages)
        {
            var rows = Lang(lang);
            rows.Keys.OrderBy(k => k, StringComparer.Ordinal).Should().Equal(Keys.OrderBy(k => k, StringComparer.Ordinal), lang);
            foreach (var key in Keys)
            {
                rows[key].Should().NotBeNullOrWhiteSpace($"{lang} {key}");
                if (lang != "en") rows[key].Should().NotBe(en[key], $"{lang} {key} must be translated");
                Regex.Matches(rows[key], @"\{\d\}").Select(m => m.Value).OrderBy(v => v).Should()
                    .Equal(Regex.Matches(en[key], @"\{\d\}").Select(m => m.Value).OrderBy(v => v), $"{lang} {key}");
                rows[key].Any(c => c == (char)0x2013 || c == (char)0x2014).Should().BeFalse($"{lang} {key}");
                // the leading indent of a fight row is the same in every language
                (rows[key].Length - rows[key].TrimStart().Length).Should().Be(en[key].Length - en[key].TrimStart().Length, $"{lang} {key}");
            }
        }
    }

    /// <summary>Every row as the game renders it, with the longest NPC name, in a language.</summary>
    private static List<string> RenderedRows(string lang)
    {
        string before = GameConfig.Language;
        GameConfig.Language = lang;
        try
        {
            var rows = new List<string>();
            rows.AddRange(WardSystem.HireRows(LongName));
            rows.Add(WardSystem.TeamViewRow());
            rows.Add(WardSystem.PartyJoinRow(LongName));
            rows.Add(WardSystem.CaughtRow(LongName));
            foreach (var id in new[] { "npc_0", "npc_1", "npc_2", "npc_3", "npc_4", "npc_5" })
                rows.AddRange(WardSystem.BreakSceneRows(LongName, id));
            rows.Add("  " + WardSystem.MemoryText(LongName));   // as the memories screen indents it
            rows.AddRange(WardSystem.LiftRows(LongName));
            rows.AddRange(WardSystem.LiftTeamRows());
            return rows.Distinct().ToList();
        }
        finally { GameConfig.Language = before; }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("hu")]
    public void Row7_EveryRenderedRow_FitsSeventyNineColumns_WithTheLongestName(string lang)
    {
        LongName.Length.Should().Be(GameConfig.MaxNameLength);
        var rows = RenderedRows(lang);
        rows.Count.Should().Be(15, "two hire rows, the team view, the joining, the caught row, six scene rows, the memory, three leaving rows");
        foreach (var row in rows)
        {
            row.Length.Should().BeLessOrEqualTo(79, row);
            row.Should().NotContain("{").And.NotContain("\n");
        }
    }

    [Theory]
    [InlineData("en")]
    [InlineData("es")]
    [InlineData("fr")]
    [InlineData("hu")]
    [InlineData("it")]
    public void Row7_EveryRow_IsPlainText_ForAScreenReaderAndTheBbsPaths(string lang)
    {
        foreach (var row in RenderedRows(lang))
        {
            // letters, digits, spaces and plain punctuation (the hyphen of a French verb form) only: no box
            // drawing, no symbols, no colour codes
            row.Should().MatchRegex(@"^[\p{L}\p{N} .,;:'""!?\-]+$", row);
        }
    }

    [Fact] public void Row7_AScreenReader_ReadsTheSameWardRows() => Run(nameof(RecruitPoolRuns.WardRow7ScreenReader));
    [Fact] public void Row7_AnotherReaderOfTheFight_ReadsTheRowsInTheirOwnLanguage() => Run(nameof(RecruitPoolRuns.WardRow7Readers));

    [Fact]
    public void Row7_NoFightRow_AddressesThePlayer_SoEveryReaderGetsTheSameRow()
    {
        var en = Lang("en");
        foreach (var key in new[] { "oath_mark.caught", "oath_mark.flares", "oath_mark.breaks", "oath_mark.last_1", "oath_mark.last_2", "oath_mark.last_3", "oath_mark.gone" })
            Regex.IsMatch(en[key], @"\b(you|your|yours|yourself)\b", RegexOptions.IgnoreCase).Should().BeFalse(key);
    }

    // ---------- row 8: the switch off ----------

    [Theory]
    [InlineData("101,5,300")]
    [InlineData("202,5,300")]
    [InlineData("303,12,300")]
    public void Row8_SwitchOff_ASeededFightWithFalls_EqualsTheBase_DrawForDrawAndRowForRow(string fight) =>
        IsolatedGame.Run(nameof(RecruitPoolRuns.WardFight), fight + ",off").Should().Be(FightGolden[fight]);

    [Fact]
    public void Row8_SwitchOff_NoWardRowIsShown_AtTheHireTheViewTheSackTheLeavingOrTheJoining()
    {
        WithTeam("Off Hall", false, async () =>
        {
            var recruit = Recruit("off_1", "Off Hand");
            string hire = await HireShown(recruit, "Off Hall", "Off Boss");
            hire.Should().Contain(Loc.Get("team.npc_joined", "Off Hand"));
            var spouse = Recruit("off_2", "Off Party");
            string shown = hire;
            await WithNpcs(new[] { recruit, spouse }, async () =>
            {
                shown += await new TeamCornerRig(TeamCornerRig.Hero(team: "Off Hall"), new[] { "" }).Run("ShowYourTeamStatus");
                shown += await JoinPartyShown(TeamCornerRig.Hero(name: "Off Boss", team: "Off Hall"), spouse);
                shown += await new TeamCornerRig(TeamCornerRig.Hero(team: "Off Hall"), new[] { "off hand", "y", "" }).Run("SackMember");
                recruit.Team = "Off Hall";
                shown += await new TeamCornerRig(TeamCornerRig.Hero(name: "Off Quit", team: "Off Hall"), new[] { "y", "" }).Run("QuitTeam");
            });
            shown.Should().Contain(Loc.Get("team.member_sacked", "Off Hand")).And.Contain(Loc.Get("team.left_team"))
                .And.Contain(Loc.Get("dungeon.npc_joins", "Off Party"));
            shown.ToLowerInvariant().Should().NotContain("oath-mark").And.NotContain(Loc.Get("oath_mark.hire_holds").ToLowerInvariant())
                .And.NotContain("their years will find them");
            recruit.Team = "Off Hall";
            WorldSimulator.RegisterPlayerTeam("Off Hall");
            WorldSimulator.IsOnPlayerTeam(recruit).Should().BeTrue();
            WardSystem.IsWarded(recruit).Should().BeFalse("the switch is off");
            WardSystem.IsWarded(spouse, new List<Character> { spouse }, new[] { "off_2" }).Should().BeFalse("the switch is off");
        }).GetAwaiter().GetResult();
    }

    /// <summary>
    /// Row 8, the always-run form: twin worlds inside one test, the same seed, a player's team of five hired
    /// NPCs, 2,000 ticks, the switch on and off. The ward changes no draw and no outcome in the world. The
    /// world simulator reads the hour of the wall clock, so the twins are compared only when both runs fell in
    /// the same part of the day (they run seconds apart).
    /// </summary>
    [Fact]
    public void Row8_TwinWorlds_SwitchOnAndOff_RunTheSameTwoThousandTicks()
    {
        string off = IsolatedGame.Run(nameof(RecruitPoolRuns.WardGolden), "4321,2000,off");
        string on = IsolatedGame.Run(nameof(RecruitPoolRuns.WardGolden), "4321,2000,on");
        on.Should().Contain("teamPermanent=0");
        if (!off.StartsWith("straddle") && off.Split(' ')[0] == on.Split(' ')[0]) on.Should().Be(off);
    }

    /// <summary>
    /// Row 8, the stored golden: OPT-IN (H3_GOLDEN), because the run reads the wall clock. Seed 4321, 2,000
    /// ticks, the switch off; one golden for each part of the day, made on the base b07b3596 with this harness
    /// (receipts/golden-world-base.txt) and again, equal, on a90e7274 after the F5 merge.
    /// </summary>
    [Fact]
    public void Row8_SwitchOff_TheWorldRun_EqualsTheBase_OptIn()
    {
        var golden = new Dictionary<string, string>
        {
            ["morning"] = "shared=95d789b1fc410fb6 npcs=107 alive=88 roster=ec78f1a5fd5780c8 simDraws=387476 simHash=65e79773195d1366 spawnDraws=3462 spawnHash=847960ff99106888 onTeam=5 teamPermanent=0",
            ["afternoon"] = "shared=19dcaa1d1f4b9d71 npcs=111 alive=100 roster=e14f2c6683f1b235 simDraws=414124 simHash=a15c8964f4271e01 spawnDraws=3532 spawnHash=7abb01ca9ba80be0 onTeam=5 teamPermanent=0",
            ["evening"] = "shared=51a95dfa60197a5d npcs=111 alive=98 roster=3e0351917d925e29 simDraws=379007 simHash=0047e345cd4f9728 spawnDraws=3532 spawnHash=05fac42e83e64ba2 onTeam=5 teamPermanent=0",
            ["night"] = "shared=21abd0bf866601dd npcs=114 alive=102 roster=7c59e3ac259e0697 simDraws=404408 simHash=8d4797ed68f1e18f spawnDraws=3575 spawnHash=ac37350a666c6f36 onTeam=5 teamPermanent=0",
        };
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("H3_GOLDEN"))) return;
        string sw = Environment.GetEnvironmentVariable("H3_GOLDEN_SWITCH") == "on" ? "on" : "off";
        string got = "";
        for (int attempt = 0; attempt < 3; attempt++)
        {
            got = IsolatedGame.Run(nameof(RecruitPoolRuns.WardGolden), "4321,2000," + sw);
            if (!got.StartsWith("straddle")) break;
        }
        if (Environment.GetEnvironmentVariable("H3_GOLDEN_OUT") is string receipt && receipt.Length > 0) File.AppendAllText(receipt, got + "\n");
        string part = got.Split(' ')[0];
        golden.Should().ContainKey(part);
        got.Should().Be(part + " " + golden[part]);
    }

    [Fact]
    public void TheSwitch_IsOnByDefault_Internal_AndNotASysopOrBalanceSetting()
    {
        var p = typeof(GameConfig).GetProperty("WardEnabled", BindingFlags.Static | BindingFlags.Public)!;
        p.SetMethod!.IsAssembly.Should().BeTrue();
        Code("Core", "GameConfig.cs").Should().Contain("public static bool WardEnabled { get; internal set; } = true;");
        // read in one place only: the helper
        var readers = Directory.GetFiles(Path.Combine(RepoRoot(), "Scripts"), "*.cs", SearchOption.AllDirectories)
            .Where(f => File.ReadAllText(f).Contains("WardEnabled")).Select(Path.GetFileName).OrderBy(n => n).ToList();
        readers.Should().Equal("GameConfig.cs", "WardSystem.cs");
        Regex.Matches(Code("Systems", "WardSystem.cs"), @"\bWardEnabled\b").Count.Should().Be(1);
    }
}

/// <summary>The world and fight scenarios of the ward tests; each runs inside IsolatedGame.</summary>
internal static partial class RecruitPoolRuns
{
    /// <summary>A random whose NextDouble is fixed: 0 makes every permadeath roll permanent, 0.999 none.</summary>
    private sealed class FixedRandom : Random
    {
        private readonly double _value;
        public int Doubles;
        public FixedRandom(double value) { _value = value; }
        public override double NextDouble() { Doubles++; return _value; }
    }

    private static void SetSimRandom(RecruitPoolWorld w, Random r) =>
        typeof(WorldSimulator).GetField("random", WardF)!.SetValue(w.Sim, r);

    private static Dictionary<string, int> RespawnTimers(RecruitPoolWorld w) =>
        (Dictionary<string, int>)typeof(WorldSimulator).GetField("deadNPCRespawnTimers", WardF)!.GetValue(w.Sim)!;

    private static void StandUp(NPC n)
    {
        n.IsDead = false; n.IsPermaDead = false; n.DeathDate = null; n.HP = n.MaxHP;
    }

    /// <summary>The eight world simulator sites: the base chance and the place each hands to MarkNPCDead.</summary>
    private static readonly (string Site, float Chance, string Place)[] WorldSites =
    {
        ("team dungeon, the party wiped", 0.05f, "the dungeon"),
        ("team dungeon, a member falls", GameConfig.PermadeathChanceDungeonTeam, "the Dungeon"),
        ("solo dungeon, the real fight", 0.05f, "the dungeon"),
        ("solo dungeon", GameConfig.PermadeathChanceDungeonSolo, "the Dungeon"),
        ("brawl, the target", GameConfig.PermadeathChanceNPCvsNPC, "Inn"),
        ("brawl, the attacker", GameConfig.PermadeathChanceNPCvsNPC, "Inn"),
        ("team war, the first side", GameConfig.PermadeathChanceTeamWar, "battle"),
        ("team war, the second side", GameConfig.PermadeathChanceTeamWar, "battle"),
    };

    /// <summary>Today's formula at level 5: the base chance x (1 - 5 x 1.5%), and five sigma of 2,000 deaths.</summary>
    private static (int Low, int High) Band(float chance, int deaths)
    {
        double p = Math.Max(0.01, chance * (1 - 5 * 0.015));
        double mean = deaths * p, sigma = Math.Sqrt(deaths * p * (1 - p));
        return ((int)Math.Floor(mean - 5 * sigma), (int)Math.Ceiling(mean + 5 * sigma));
    }

    /// <summary>
    /// One world, the switch on or off: at each of the eight sites 2,000 deaths of a hired NPC through
    /// MarkNPCDead, 2,000 rolls of it through the one place under the ward, and 2,000 deaths of a twin that
    /// is on no team. Returns a line per site: what happened and the draws it took.
    /// </summary>
    private static string WardWorldSites(bool on)
    {
        SetWardSwitch(on);
        SilenceNews();
        using var w = new RecruitPoolWorld(31);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        var sb = new StringBuilder();
        try
        {
            var race = CommonRace(w);
            var hired = Make("Hired Hand", race, 5, team: PlayerTeam);
            var twin = Make("Free Hand", race, 5);
            WardSystem.IsWarded(hired).Should().Be(on);
            WardSystem.IsWarded(twin).Should().BeFalse();
            const int deaths = 2000;
            foreach (var (site, chance, place) in WorldSites)
            {
                // the hired NPC at the site: never dead for good. As at the base, MarkNPCDead holds an NPC of
                // a player's team on its feet (healed to a quarter) before the roll, so no draw is taken.
                long before = w.SimRandom.Draws;
                for (int i = 0; i < deaths; i++)
                {
                    hired.HP = 0;
                    bool permanent = w.Sim.MarkNPCDead(hired, chance, "A Killer", place);
                    permanent.Should().BeFalse(site);
                    hired.IsPermaDead.Should().BeFalse(site);
                    hired.IsDead.Should().BeFalse(site);
                    hired.HP.Should().Be(Math.Max(1, hired.MaxHP / 4), site);
                }
                long siteDraws = w.SimRandom.Draws - before;

                // the one place under the ward, asked directly: the roll is drawn every time and, warded,
                // never stands; unwarded (the switch off) it stands at today's rate
                before = w.SimRandom.Draws;
                int stood = 0;
                for (int i = 0; i < deaths; i++)
                    if (w.Sim.RollPermadeathUnderWard(hired, chance, atPlayersSide: false)) stood++;
                long rollDraws = w.SimRandom.Draws - before;
                rollDraws.Should().Be(deaths, site);
                var (low, high) = Band(chance, deaths);
                if (on) stood.Should().Be(0, site);
                else stood.Should().BeInRange(low, high, site);
                // and asked as the player's own fight asks it, the roll stands for the hired NPC, warded or not
                int stoodAtThePlayersSide = 0;
                for (int i = 0; i < deaths; i++)
                    if (w.Sim.RollPermadeathUnderWard(hired, chance, atPlayersSide: true)) stoodAtThePlayersSide++;
                stoodAtThePlayersSide.Should().BeInRange(low, high, site);

                // the twin on no team: it dies, it waits for its respawn, and it is permanent at today's rate
                int twinPermanent = 0;
                for (int i = 0; i < deaths; i++)
                {
                    StandUp(twin);
                    RespawnTimers(w).Remove(twin.Id);
                    bool permanent = w.Sim.MarkNPCDead(twin, chance, "A Killer", place);
                    twin.IsDead.Should().BeTrue(site);
                    twin.HP.Should().Be(0, site);
                    twin.IsPermaDead.Should().Be(permanent, site);
                    RespawnTimers(w).ContainsKey(twin.Id).Should().Be(!permanent, site);
                    if (permanent) twinPermanent++;
                }
                twinPermanent.Should().BeInRange(low, high, site);
                sb.Append($"{site}: siteDraws={siteDraws} rollDraws={rollDraws} twinPermanent={twinPermanent} ")
                  .Append($"simDraws={w.SimRandom.Draws} simHash={w.SimRandom.Hash:x16} shared={RecruitPoolWorld.SharedState():x16}\n");
            }
            return sb.ToString();
        }
        finally { WorldSimulator.UnregisterPlayerTeam(PlayerTeam); }
    }

    public static string WardRow1(string _)
    {
        string on = WardWorldSites(true);
        string off = WardWorldSites(false);
        // the warded world and its unwarded twin world: the same draws in the same order at every site
        on.Should().Be(off);
        on.Split('\n').Count(l => l.Contains("siteDraws=0 rollDraws=2000")).Should().Be(8);
        return "ok";
    }

    public static string WardRow2(string _)
    {
        SetWardSwitch(true);
        SilenceNews();
        using var w = new RecruitPoolWorld(32);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        try
        {
            var race = CommonRace(w);
            // in the player's dungeon party, on no team (a spouse who came along), and one on an NPC team
            var partyOnly = Make("Party Only", race, 5);
            var onNpcTeam = Make("Party Tide", race, 5, team: NpcTeam);
            var hired = Make("Hired Hand", race, 5, team: PlayerTeam);
            var stranger = Make("Stranger", race, 5);
            var party = new List<Character> { partyOnly, onNpcTeam, hired };
            var ids = new List<string> { partyOnly.ID, onNpcTeam.ID, hired.ID };

            // at the player's side, for the length of the run: warded with the party, by object or by ID
            WardSystem.IsWarded(partyOnly, party).Should().BeTrue();
            WardSystem.IsWarded(partyOnly, null, ids).Should().BeTrue();
            WardSystem.IsWarded(onNpcTeam, party, ids).Should().BeTrue();
            WardSystem.IsWarded(stranger, party, ids).Should().BeFalse();
            // away from the player nobody passes a party: only the team member is warded
            WardSystem.IsWarded(partyOnly).Should().BeFalse();
            WardSystem.IsWarded(onNpcTeam).Should().BeFalse();
            WardSystem.IsWarded(hired).Should().BeTrue();

            // the world: 2,000 deaths at the brawl rate, permanent at today's rate, as a stranger's are
            const int deaths = 2000;
            var (low, high) = Band(GameConfig.PermadeathChanceNPCvsNPC, deaths);
            foreach (var npc in new[] { partyOnly, onNpcTeam, stranger })
            {
                int permanent = 0, stood = 0;
                for (int i = 0; i < deaths; i++)
                {
                    StandUp(npc);
                    if (w.Sim.MarkNPCDead(npc, GameConfig.PermadeathChanceNPCvsNPC, "A Killer", "Inn")) permanent++;
                    npc.IsDead.Should().BeTrue();
                    if (w.Sim.RollPermadeathUnderWard(npc, GameConfig.PermadeathChanceNPCvsNPC, atPlayersSide: false)) stood++;
                }
                permanent.Should().BeInRange(low, high, npc.Name2);
                stood.Should().BeInRange(low, high, npc.Name2);
                StandUp(npc);
            }

            // old age: past the Gnoll's 50 years the party-only member dies at the check; the hired one does not
            var oldParty = Make("Old Party Gnoll", CharacterRace.Gnoll, 12, age: 55);
            var oldHired = Make("Old Hired Gnoll", CharacterRace.Gnoll, 12, team: PlayerTeam, age: 55);
            for (int i = 0; i < 8; i++) Make($"Young Gnoll {i}", CharacterRace.Gnoll, 40, age: 20);
            oldParty.IsInConversation = true;   // as a party member is marked during the run
            WardSystem.IsWarded(oldParty, new List<Character> { oldParty }).Should().BeTrue();
            WorldSimulator.HoldAgeOnPlayerTeam(oldParty, DateTime.UtcNow).Should().BeFalse();
            AgeCheck(w);
            oldParty.IsAgedDeath.Should().BeTrue("old age comes for a party-only member as before");
            Living(oldHired).Should().BeTrue();
            return "ok";
        }
        finally { WorldSimulator.UnregisterPlayerTeam(PlayerTeam); }
    }

    private static readonly string[] SceneKeys = { "oath_mark.flares", "oath_mark.breaks", "oath_mark.gone" };

    private static int Times(string shown, string row) => Regex.Matches(shown, Regex.Escape(row)).Count;

    private static List<CompanionMemory> WardMemories(string npcId) =>
        GriefSystem.Instance.Serialize().Memories.Where(m => m.NpcId == npcId).ToList();

    /// <summary>
    /// Row 4. "caught": a fall that is not permanent. "permanent": one that is. "party": an NPC that is in the
    /// party and on no team, both outcomes. "friend": the grief for a close friend after the scene. "spouse": a
    /// spouse the ward caught, and the same fall with the switch off.
    /// </summary>
    public static string WardRow4(string mode)
    {
        SetWardSwitch(true);
        var news = new List<string>();
        NewsSystem.Instance.SetCatchUpBuffer(news);
        GameConfig.Language = "en";
        using var w = new RecruitPoolWorld(33);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        try
        {
            GriefSystem.Instance.Reset();
            RomanceTracker.Instance.Reset();
            var race = CommonRace(w);
            var ally = mode == "party" ? Make("Party Only", race, 5) : Make("Hired Hand", race, 5, team: PlayerTeam);
            string name = ally.DisplayName;
            string caught = Loc.Get("oath_mark.caught", name);
            string banner = Loc.Get("combat.npc_fallen", name);
            string lastLine = Loc.Get(WardSystem.LastLineKey(ally.ID), name);
            string permadeathNews = Loc.Get("worldsim.news_permadeath", ally.Name, "Deep Thing");

            string sacrifice = Loc.Get("combat.teammate_sacrifice", name);
            string[] spouseRows = { Loc.Get("combat.spouse_fallen", name), Loc.Get("combat.perhaps_saved"), Loc.Get("combat.lover_fallen", name) };
            if (mode == "spouse")
            {
                // a spouse on the team, caught: the banner and the caught row, no row of loss
                RomanceTracker.Instance.Spouses.Add(new Spouse { NPCId = ally.ID, NPCName = ally.Name2 });
                RomanceTracker.Instance.IsPlayerMarriedTo(ally.ID).Should().BeTrue();
                SetSimRandom(w, new FixedRandom(0.999));
                var rig = new WardFightRig(PlayerTeam);
                rig.Fall(ally);
                string shown = rig.Shown();
                Times(shown, banner).Should().Be(1);
                Times(shown, caught).Should().Be(1);
                foreach (var row in spouseRows) shown.Should().NotContain(row);
                shown.Should().NotContain(sacrifice);

                // the switch off: the same fall shows the base rows, and no caught row
                SetWardSwitch(false);
                RomanceTracker.Instance.Reset();
                RomanceTracker.Instance.Spouses.Add(new Spouse { NPCId = ally.ID, NPCName = ally.Name2 });
                StandUp(ally);
                var offRig = new WardFightRig(PlayerTeam);
                offRig.Fall(ally);
                string off = offRig.Shown();
                Times(off, banner).Should().Be(1);
                off.Should().NotContain(caught);
                Times(off, spouseRows[0]).Should().Be(1);
                Times(off, spouseRows[1]).Should().Be(1);
                return "ok";
            }

            if (mode == "caught" || mode == "party")
            {
                var roll = new FixedRandom(0.999);
                SetSimRandom(w, roll);
                var rig = new WardFightRig(PlayerTeam);
                rig.Fall(ally);
                string shown = rig.Shown();
                roll.Doubles.Should().Be(1, "the roll is the one draw");
                ally.IsPermaDead.Should().BeFalse();
                ally.IsDead.Should().BeTrue();
                RespawnTimers(w).ContainsKey(ally.Id).Should().BeTrue("the fall is as before: it waits for its respawn");
                Times(shown, banner).Should().Be(1);
                Times(shown, caught).Should().Be(1);
                shown.IndexOf(caught, StringComparison.Ordinal).Should().BeGreaterThan(shown.IndexOf(banner, StringComparison.Ordinal));
                foreach (var key in SceneKeys) shown.Should().NotContain(Loc.Get(key, name));
                foreach (var key in new[] { "oath_mark.last_1", "oath_mark.last_2", "oath_mark.last_3" }) shown.Should().NotContain(Loc.Get(key, name));
                WardMemories(ally.ID).Should().BeEmpty();
                news.Count(n => n.Contains(permadeathNews)).Should().Be(0);
                rig.Result.Teammates.Should().NotContain(ally);
                // the banner and the caught row only: no row of loss
                shown.Should().NotContain(sacrifice);
                foreach (var row in spouseRows) shown.Should().NotContain(row);
                if (mode == "caught")
                {
                    // the switch off: the same fall ends with the base row
                    SetWardSwitch(false);
                    StandUp(ally);
                    var offRig = new WardFightRig(PlayerTeam);
                    offRig.Fall(ally);
                    Times(offRig.Shown(), sacrifice).Should().Be(1);
                    offRig.Shown().Should().NotContain(caught);
                    return "ok";
                }
            }

            WardFightRig? friendRig = null;
            if (mode == "friend")
            {
                friendRig = new WardFightRig(PlayerTeam);
                var bond = RelationshipSystem.GetOrCreateRelationship(friendRig.Hero, ally);
                bond.Relation1 = GameConfig.RelationFriendship;
                bond.Relation2 = GameConfig.RelationFriendship;
                RelationshipSystem.GetRelationshipStatus(friendRig.Hero, ally).Should().Be(GameConfig.RelationFriendship);
            }

            {
                var roll = new FixedRandom(0.0);
                SetSimRandom(w, roll);
                StandUp(ally);
                RespawnTimers(w).Remove(ally.Id);
                news.Clear();
                var rig = friendRig ?? new WardFightRig(PlayerTeam);
                ulong sharedBefore = RecruitPoolWorld.SharedState();
                rig.Fall(ally);
                string shown = rig.Shown();
                roll.Doubles.Should().Be(1, "the scene takes no draw from the world simulator's random");
                if (mode != "friend") RecruitPoolWorld.SharedState().Should().Be(sharedBefore, "nor from the shared random");
                ally.IsPermaDead.Should().BeTrue();
                Times(shown, banner).Should().Be(1);
                shown.Should().NotContain(caught);
                // the scene: the ward flares and breaks, the last line, the closing row; once each, in order
                var scene = new[] { Loc.Get("oath_mark.flares", name), Loc.Get("oath_mark.breaks"), lastLine, Loc.Get("oath_mark.gone", name) };
                int at = shown.IndexOf(banner, StringComparison.Ordinal);
                foreach (var row in scene)
                {
                    Times(shown, row).Should().Be(1, row);
                    int next = shown.IndexOf(row, StringComparison.Ordinal);
                    next.Should().BeGreaterThan(at, row);
                    at = next;
                }
                // one last line only
                new[] { "oath_mark.last_1", "oath_mark.last_2", "oath_mark.last_3" }.Sum(k => Times(shown, Loc.Get(k, name))).Should().Be(1);
                // the news entry, once (the existing permanent death line of the realm)
                news.Count(n => n.Contains(permadeathNews)).Should().Be(1);
                // the record that lasts, once: the player's memories of the fallen
                var memories = WardMemories(ally.ID).Where(m => m.MemoryText == Loc.Get("oath_mark.memory", name)).ToList();
                memories.Count.Should().Be(1);
                memories[0].CompanionName.Should().Be(name);
                // and it is saved with the player: the grief memories round trip
                var saved = GriefSystem.Instance.Serialize();
                GriefSystem.Instance.Reset();
                GriefSystem.Instance.Deserialize(saved);
                WardMemories(ally.ID).Count(m => m.MemoryText == Loc.Get("oath_mark.memory", name)).Should().Be(1);

                // the row of loss follows the scene, as before
                Times(shown, sacrifice).Should().Be(1);
                shown.IndexOf(sacrifice, StringComparison.Ordinal).Should().BeGreaterThan(at);
                if (mode == "friend")
                {
                    // the grief trigger is as before, and it runs after the scene
                    int grief = shown.IndexOf(Loc.Get("combat.grief_onset"), StringComparison.Ordinal);
                    grief.Should().BeGreaterThan(at, "grief begins after the closing row of the scene");
                    Times(shown, Loc.Get("combat.grief_onset")).Should().Be(1);
                }
                else
                {
                    shown.Should().NotContain(Loc.Get("combat.grief_onset"), "a hired hand who is no friend brings no grief, as before");
                }
            }
            return "ok";
        }
        finally
        {
            WorldSimulator.UnregisterPlayerTeam(PlayerTeam);
            NewsSystem.Instance.ClearCatchUpBuffer();
        }
    }

    /// <summary>
    /// Row 9 (the user's "Keep the marriage"): "spouse", "lover" or "affair". One NPC with that bond to the
    /// player falls three times: caught with the switch on (the record, the jealousy and the affair entry are
    /// kept), caught with the switch off (cleared, as at the base), and for good with the switch on (cleared,
    /// as today).
    /// </summary>
    public static string WardRow9(string bond)
    {
        SilenceNews();
        GameConfig.Language = "en";
        using var w = new RecruitPoolWorld(37);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        try
        {
            GriefSystem.Instance.Reset();
            var ally = Make("Bound Hand", CommonRace(w), 5, team: PlayerTeam);
            var rig = new WardFightRig(PlayerTeam);
            string id = ally.ID;
            string seducer = rig.Hero.ID ?? "hero";
            var romance = RomanceTracker.Instance;
            var registry = NPCMarriageRegistry.Instance;

            void Bind()
            {
                romance.Reset();
                registry.OnNPCPermadied(id);
                if (bond == "spouse") romance.Spouses.Add(new Spouse { NPCId = id, NPCName = ally.Name2 });
                if (bond == "lover") romance.CurrentLovers.Add(new Lover { NPCId = id, NPCName = ally.Name2 });
                if (bond == "affair") { romance.FriendsWithBenefits.Add(id); registry.GetOrCreateAffair(id, seducer); }
                romance.JealousyLevels[id] = 7;
                registry.GetOrCreateAffair("someone else", id);   // an entry where this NPC is the other side
                StandUp(ally);
            }
            bool Kept() =>
                (bond == "spouse" ? romance.Spouses.Any(s => s.NPCId == id)
                 : bond == "lover" ? romance.CurrentLovers.Any(l => l.NPCId == id)
                 : romance.FriendsWithBenefits.Contains(id) && registry.GetAffair(id, seducer) != null);
            bool Cleared() =>
                !romance.Spouses.Any(s => s.NPCId == id) && !romance.CurrentLovers.Any(l => l.NPCId == id)
                && !romance.FriendsWithBenefits.Contains(id) && registry.GetAffair(id, seducer) == null;

            // caught, the switch on: everything stays as it was
            SetWardSwitch(true);
            Bind();
            Kept().Should().BeTrue("the bond is set up");
            SetSimRandom(w, new FixedRandom(0.999));
            rig.Fall(ally);
            ally.IsPermaDead.Should().BeFalse();
            Kept().Should().BeTrue("a caught fall keeps the " + bond);
            romance.JealousyLevels.Should().ContainKey(id).WhoseValue.Should().Be(7);
            registry.GetAffair("someone else", id).Should().NotBeNull("the affair registry is left alone on a caught fall");
            romance.ExSpouses.Should().NotContain(e => e.NPCId == id);
            GriefSystem.Instance.Serialize().ActiveNpcGrief.Should().BeEmpty("no grief begins on a fall that is not permanent");
            string shown = rig.Shown();
            shown.Should().Contain(Loc.Get("oath_mark.caught", ally.DisplayName));
            shown.Should().NotContain(Loc.Get("combat.spouse_fallen", ally.DisplayName)).And.NotContain(Loc.Get("combat.lover_fallen", ally.DisplayName))
                .And.NotContain(Loc.Get("combat.perhaps_saved")).And.NotContain(Loc.Get("combat.grief_onset"));

            // the same fall with the switch off: the base behaviour, the cleanup runs
            SetWardSwitch(false);
            Bind();
            rig.Fall(ally);
            ally.IsPermaDead.Should().BeFalse();
            Cleared().Should().BeTrue("with the switch off a fall clears the " + bond + ", as at the base");
            romance.JealousyLevels.Should().NotContainKey(id);
            registry.GetAffair("someone else", id).Should().BeNull();

            // lost for good, the switch on: cleared as today
            SetWardSwitch(true);
            Bind();
            SetSimRandom(w, new FixedRandom(0.0));
            rig.Fall(ally);
            ally.IsPermaDead.Should().BeTrue();
            Cleared().Should().BeTrue("a permanent fall clears the " + bond);
            romance.JealousyLevels.Should().NotContainKey(id);
            registry.GetAffair("someone else", id).Should().BeNull();
            if (bond == "spouse") romance.ExSpouses.Should().Contain(e => e.NPCId == id);
            return "ok";
        }
        finally { WorldSimulator.UnregisterPlayerTeam(PlayerTeam); }
    }

    /// <summary>Row 6: a story companion that falls in the fight goes its own way: no ward row is shown.</summary>
    public static string WardRow6(string _)
    {
        SetWardSwitch(true);
        SilenceNews();
        GameConfig.Language = "en";
        using var w = new RecruitPoolWorld(34);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        try
        {
            var rig = new WardFightRig(PlayerTeam, Enumerable.Repeat("", 40));
            // the fight's Character of a story companion, as CompanionSystem builds it; it carries the team too
            var companion = new Character
            {
                Name1 = "Vex", Name2 = "Vex", IsCompanion = true, CompanionId = CompanionId.Vex, Team = PlayerTeam,
                Class = CharacterClass.Assassin, Level = 10, HP = 0, MaxHP = 100,
            };
            rig.Result.Teammates.Add(companion);
            WardSystem.IsWarded(companion, rig.Result.Teammates).Should().BeFalse();
            SetSimRandom(w, new FixedRandom(0.999));
            rig.Fall(companion);
            string shown = rig.Shown();
            shown.Should().Contain(Loc.Get("combat.companion_falls", "Vex"));
            shown.Should().NotContain("oath-mark");
            GriefSystem.Instance.Serialize().Memories.Should().NotContain(m => m.MemoryText.Contains("oath-mark"));
            return "ok";
        }
        finally { WorldSimulator.UnregisterPlayerTeam(PlayerTeam); }
    }

    /// <summary>Row 7: in screen reader mode the ward rows are the same plain rows, and no rule line is drawn.</summary>
    public static string WardRow7ScreenReader(string _)
    {
        SetWardSwitch(true);
        SilenceNews();
        GameConfig.Language = "en";
        using var w = new RecruitPoolWorld(35);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        bool before = GameConfig.ScreenReaderMode;
        try
        {
            GameConfig.ScreenReaderMode = true;
            var race = CommonRace(w);
            var caughtAlly = Make("Caught Hand", race, 5, team: PlayerTeam);
            var lostAlly = Make("Lost Hand", race, 5, team: PlayerTeam);
            var rig = new WardFightRig(PlayerTeam);
            SetSimRandom(w, new FixedRandom(0.999));
            rig.Fall(caughtAlly);
            SetSimRandom(w, new FixedRandom(0.0));
            rig.Fall(lostAlly);
            string shown = rig.Shown();
            shown.Should().Contain(Loc.Get("oath_mark.caught", "Caught Hand"));
            foreach (var row in WardSystem.BreakSceneRows("Lost Hand", lostAlly.ID)) shown.Should().Contain(row);
            Regex.IsMatch(shown, "[─-▟]").Should().BeFalse("no box drawing reaches a screen reader");
            return "ok";
        }
        finally { GameConfig.ScreenReaderMode = before; WorldSimulator.UnregisterPlayerTeam(PlayerTeam); }
    }

    /// <summary>
    /// Row 7: another reader of the fight. The leader's screen is captured and recorded as the fight does it
    /// (StartCapture, Loc.BeginRecording), and CombatEngine.CapturedInLanguage gives each reader the stretch in
    /// their own language: the ward rows come out in that language, none falls back to English.
    /// </summary>
    public static string WardRow7Readers(string _)
    {
        SetWardSwitch(true);
        SilenceNews();
        GameConfig.Language = "en";
        using var w = new RecruitPoolWorld(36);
        WorldSimulator.RegisterPlayerTeam(PlayerTeam);
        try
        {
            var race = CommonRace(w);
            var caughtAlly = Make("Caught Hand", race, 5, team: PlayerTeam);
            var lostAlly = Make("Lost Hand", race, 5, team: PlayerTeam);
            var rig = new WardFightRig(PlayerTeam);
            rig.Term.StartCapture();
            var recording = Loc.BeginRecording();
            try
            {
                SetSimRandom(w, new FixedRandom(0.999));
                rig.Fall(caughtAlly);
                SetSimRandom(w, new FixedRandom(0.0));
                rig.Fall(lostAlly);
            }
            finally { Loc.EndRecording(recording); }
            string captured = rig.Term.StopCapture() ?? "";
            captured.Should().Contain(Loc.GetIn("en", "oath_mark.caught", "Caught Hand"));
            foreach (var lang in new[] { "en", "es", "fr", "hu", "it" })
            {
                string read = Regex.Replace(CombatEngine.CapturedInLanguage(recording, captured, lang, "Ward Hero"), "\u001b\\[[0-9;]*[A-Za-z]", "");
                read.Should().Contain(Loc.GetIn(lang, "oath_mark.caught", "Caught Hand"), lang);
                read.Should().Contain(Loc.GetIn(lang, "oath_mark.flares", "Lost Hand"), lang);
                read.Should().Contain(Loc.GetIn(lang, "oath_mark.breaks"), lang);
                read.Should().Contain(Loc.GetIn(lang, WardSystem.LastLineKey(lostAlly.ID), "Lost Hand"), lang);
                read.Should().Contain(Loc.GetIn(lang, "oath_mark.gone", "Lost Hand"), lang);
                if (lang != "en") read.Should().NotContain(Loc.GetIn("en", "oath_mark.caught", "Caught Hand"), lang);
            }
            return "ok";
        }
        finally { WorldSimulator.UnregisterPlayerTeam(PlayerTeam); }
    }
}
