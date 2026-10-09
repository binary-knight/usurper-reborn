using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Locations;
using UsurperRemake.Systems;
using UsurperRemake.Utils;
using Xunit;

namespace UsurperReborn.Tests;

/// <summary>
/// v1.2.8: hire prices and wages of team NPCs. The per-level part of the price is the lower of
/// level * 2000 and level * level * 80, the wage the lower of level * 100 and level * level * 4 (both
/// unchanged from level 25 up), and an NPC above the recruiter's level costs 5% more per level above,
/// capped at x1.5. The expected numbers in this file are literals, not the GameConfig constants, so a
/// changed constant shows here.
/// </summary>
[Collection("SharedGameSingletons")]
public class HirePrices128Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static Character Recruiter(int level, string name = "Price Tester") =>
        new Character { Name1 = name, Name2 = name, ID = name.ToLowerInvariant().Replace(' ', '_'), Level = level };

    /// <summary>An NPC of a level whose STR + DEF + AGI is the given sum.</summary>
    private static NPC Hire(string id, int level, long statSum, string team = "") => new NPC
    {
        ID = id, Name1 = "Npc " + id, Name2 = "Npc " + id, Level = level, Class = CharacterClass.Warrior,
        HP = 100, MaxHP = 100, Strength = statSum, Defence = 0, Agility = 0, Team = team,
        CurrentLocation = "Main Street",
    };

    private static void SetScore(Character a, Character b, int score)
    {
        var rec = RelationshipSystem.GetOrCreateRelationship(a, b);
        if (rec.Name1 == a.Name) rec.Relation1 = score; else rec.Relation2 = score;
    }

    private static void ResetWorld()
    {
        RelationshipSystem.Instance.Reset();
        RomanceTracker.Instance.Reset();
    }

    private static async Task WithNpcs(IEnumerable<NPC> npcs, Func<Task> body)
    {
        var list = npcs.ToList();
        NPCSpawnSystem.Instance.ActiveNPCs.AddRange(list);
        try { await body(); }
        finally { foreach (var n in list) NPCSpawnSystem.Instance.ActiveNPCs.Remove(n); }
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "Scripts")) && Directory.Exists(Path.Combine(dir.FullName, "Localization"))))
            dir = dir.Parent;
        return dir!.FullName;
    }

    /// <summary>A source file without its comments.</summary>
    private static string Code(params string[] parts)
    {
        string src = File.ReadAllText(Path.Combine(new[] { RepoRoot(), "Scripts" }.Concat(parts).ToArray()));
        src = Regex.Replace(src, @"/\*.*?\*/", "", RegexOptions.Singleline);
        return Regex.Replace(src, @"//[^\n]*", "");
    }

    /// <summary>The rule before 1.2.8, written out: level * 2000 plus the stat part.</summary>
    private static long OldBaseAtLevel(int level, long statSum) => level * 2000L + statSum * 20;

    // ---------- row 1: the price table ----------

    [Theory]
    [InlineData(1, 48, 1040)]        // 80 + 960 (was 2,960)
    [InlineData(5, 60, 3200)]        // 2,000 + 1,200 (was 11,200)
    [InlineData(10, 84, 9680)]       // 8,000 + 1,680 (was 21,680)
    [InlineData(13, 123, 15980)]     // 13,520 + 2,460 (was 28,460)
    [InlineData(19, 146, 31800)]     // 28,880 + 2,920 (was 40,920)
    [InlineData(24, 200, 50080)]     // 46,080 + 4,000 (was 52,000)
    [InlineData(25, 200, 54000)]     // 50,000 + 4,000, the two terms meet
    [InlineData(26, 203, 56060)]     // unchanged
    [InlineData(33, 368, 73360)]     // unchanged
    [InlineData(46, 1385, 119700)]   // unchanged
    public void Row1_PriceAtTheRecruitersLevel_NeutralRelationship(int level, long statSum, long expected)
    {
        ResetWorld();
        var player = Recruiter(level);
        var npc = Hire("r1_" + level, level, statSum);

        TeamSystem.GetRecruitmentBaseCost(npc, player).Should().Be(expected);
        TeamSystem.GetRecruitmentCost(player, npc).Should().Be(expected, "a stranger pays the base price");
    }

    [Theory]
    [InlineData(1, 80)]
    [InlineData(2, 320)]
    [InlineData(10, 8000)]
    [InlineData(13, 13520)]
    [InlineData(19, 28880)]
    [InlineData(24, 46080)]
    [InlineData(25, 50000)]
    [InlineData(26, 52000)]
    [InlineData(100, 200000)]
    public void Row1_ThePerLevelPart_IsTheLowerOfTheTwoTerms(int level, long expected) =>
        TeamSystem.GetRecruitmentLevelCost(level).Should().Be(expected);

    [Theory]
    [InlineData(10, 84, 21680)]
    [InlineData(13, 123, 28460)]
    [InlineData(19, 146, 40920)]
    public void Row1_BelowLevel25_ThePriceIsLowerThanBefore(int level, long statSum, long before)
    {
        OldBaseAtLevel(level, statSum).Should().Be(before, "the old rule as the data table has it");
        TeamSystem.GetRecruitmentBaseCost(Hire("r1b_" + level, level, statSum), Recruiter(level)).Should().BeLessThan(before);
    }

    // ---------- row 2: nothing changes from level 25 up ----------

    [Fact]
    public void Row2_FromLevel25Up_ThePriceIsTheOldPrice()
    {
        for (int level = 25; level <= 100; level++)
            foreach (long statSum in new long[] { 0, 203, 1385 })
            {
                var npc = Hire("r2_" + level, level, statSum);
                TeamSystem.GetRecruitmentLevelCost(level).Should().Be(level * 2000L, $"level {level}");
                TeamSystem.GetRecruitmentBaseCost(npc, Recruiter(level)).Should().Be(OldBaseAtLevel(level, statSum), $"level {level}, at the recruiter's level");
                // more than 5 below the recruiter: the old x0.7
                TeamSystem.GetRecruitmentBaseCost(npc, Recruiter(level + 6)).Should().Be((long)(OldBaseAtLevel(level, statSum) * 0.7), $"level {level}, 6 below");
                // 10 or more above the recruiter: the old x1.5
                TeamSystem.GetRecruitmentBaseCost(npc, Recruiter(level - 10)).Should().Be((long)(OldBaseAtLevel(level, statSum) * 1.5), $"level {level}, 10 above");
            }
    }

    [Fact]
    public void Row2_FromLevel25Up_TheWageIsTheOldWage()
    {
        for (int level = 25; level <= 100; level++)
        {
            TeamSystem.GetDailyWage(level).Should().Be(level * 100L, $"level {level}");
            TeamSystem.GetDailyWage(Hire("r2w_" + level, level, 0)).Should().Be(level * 100L, $"level {level}");
        }
    }

    [Fact]
    public void Row2_BelowLevel25_PriceAndWageAreBelowTheOldOnes_AndNeverAboveThem()
    {
        for (int level = 1; level <= 24; level++)
        {
            TeamSystem.GetRecruitmentLevelCost(level).Should().BeLessThan(level * 2000L, $"level {level}");
            TeamSystem.GetDailyWage(level).Should().BeLessThan(level * 100L, $"level {level}");
        }
        for (int level = 1; level < 100; level++)
        {
            TeamSystem.GetRecruitmentLevelCost(level + 1).Should().BeGreaterThan(TeamSystem.GetRecruitmentLevelCost(level), "a higher level never costs less");
            TeamSystem.GetDailyWage(level + 1).Should().BeGreaterThan(TeamSystem.GetDailyWage(level), "a higher level never earns less");
        }
    }

    // ---------- row 3: the graded surcharge ----------

    [Theory]
    [InlineData(0, 100)]
    [InlineData(1, 105)]
    [InlineData(3, 115)]
    [InlineData(5, 125)]
    [InlineData(9, 145)]
    [InlineData(10, 150)]
    [InlineData(20, 150)]
    [InlineData(99, 150)]
    public void Row3_ThePercent_Is5PerLevelAbove_CappedAt150(int above, int percent) =>
        TeamSystem.GetRecruitmentAboveLevelPercent(30 + above, 30).Should().Be(percent);

    [Theory]
    [InlineData(1, 58863)]    // 56,060 x 1.05
    [InlineData(3, 64469)]    // x 1.15
    [InlineData(9, 81287)]    // x 1.45
    [InlineData(10, 84090)]   // x 1.5, the old flat price
    [InlineData(20, 84090)]   // x 1.5
    public void Row3_AnNpcAboveTheRecruiter_PaysTheGradedSurcharge(int above, long expected)
    {
        var npc = Hire("r3_" + above, 26, 203);   // base 56,060
        TeamSystem.GetRecruitmentBaseCost(npc, Recruiter(26 - above)).Should().Be(expected);
    }

    [Theory]
    [InlineData(13, 123, 10, 18377)]   // 15,980 x 1.15, 3 above
    [InlineData(10, 84, 9, 10164)]     // 9,680 x 1.05, 1 above
    [InlineData(19, 146, 10, 46110)]   // 31,800 x 1.45, 9 above
    [InlineData(19, 146, 9, 47700)]    // 31,800 x 1.5, 10 above
    public void Row3_TheSurcharge_AppliesToTheNewLowPrices(int level, long statSum, int recruiterLevel, long expected) =>
        TeamSystem.GetRecruitmentBaseCost(Hire("r3b_" + level, level, statSum), Recruiter(recruiterLevel)).Should().Be(expected);

    [Theory]
    [InlineData(5)]
    [InlineData(10)]
    public void Row3_TheCommonCase_ALowRecruiterAgainstTheMedianNpc_IsUnchanged(int recruiterLevel)
    {
        // the live data's common case: the median available NPC (level 26, stats 203) is 10 or more
        // levels above a level 5 or level 10 recruiter, so the cap applies: 56,060 x 1.5, as before
        var npc = Hire("r3c_" + recruiterLevel, 26, 203);
        TeamSystem.GetRecruitmentBaseCost(npc, Recruiter(recruiterLevel)).Should().Be(84090);
        ((long)(OldBaseAtLevel(26, 203) * 1.5)).Should().Be(84090);
    }

    // ---------- row 4: the wage, one method at three sites ----------

    [Theory]
    [InlineData(1, 4)]
    [InlineData(2, 16)]
    [InlineData(3, 36)]
    [InlineData(5, 100)]
    [InlineData(10, 400)]      // was 1,000
    [InlineData(13, 676)]      // was 1,300
    [InlineData(19, 1444)]     // was 1,900
    [InlineData(24, 2304)]     // was 2,400
    [InlineData(25, 2500)]
    [InlineData(26, 2600)]
    [InlineData(33, 3300)]
    [InlineData(46, 4600)]
    [InlineData(100, 10000)]
    public void Row4_TheWageTable(int level, long expected)
    {
        TeamSystem.GetDailyWage(level).Should().Be(expected);
        TeamSystem.GetDailyWage(Hire("r4_" + level, level, 0)).Should().Be(expected);
    }

    [Fact]
    public void Row4_ALevel1Npc_IsNeverFree_AndNoLevelPaysANegativeWage()
    {
        TeamSystem.GetDailyWage(1).Should().BeGreaterOrEqualTo(1);
        TeamSystem.GetDailyWage(0).Should().Be(0);
        TeamSystem.GetDailyWage(-3).Should().Be(0);
    }

    [Fact]
    public void Row4_TheThreeSites_CallTheOneMethod_AndNoneHoldsTheOldExpression()
    {
        string maint = Code("Systems", "MaintenanceSystem.cs");
        string corner = Code("Locations", "TeamCornerLocation.cs");
        maint.Should().Contain("totalWages += TeamSystem.GetDailyWage(npc);", "the daily charge");
        corner.Should().Contain("DailyWage = TeamSystem.GetDailyWage(npc),", "the hire list");
        corner.Should().Contain("long wage = TeamSystem.GetDailyWage(recruit);", "the line after hiring");
        maint.Should().NotContain("NpcDailyWagePerLevel");
        corner.Should().NotContain("NpcDailyWagePerLevel");

        // the wage constants are read in TeamSystem only, and the old product is nowhere in Scripts
        string scripts = Path.Combine(RepoRoot(), "Scripts");
        var users = Directory.GetFiles(scripts, "*.cs", SearchOption.AllDirectories)
            .Where(f => Regex.IsMatch(Regex.Replace(File.ReadAllText(f), @"//[^\n]*", ""), @"\bNpcDailyWagePerLevel(Squared)?\b"))
            .Select(f => Path.GetFileName(f)).OrderBy(n => n).ToList();
        users.Should().Equal("GameConfig.cs", "TeamSystem.cs");
    }

    [Fact]
    public async Task Row4_TheHireList_ShowsTheWageOfTheOneMethod()
    {
        ResetWorld();
        var low = Hire("r4_list_low", 10, 84);
        var high = Hire("r4_list_high", 30, 300);
        await WithNpcs(new[] { low, high }, () =>
        {
            var rig = new TeamCornerRig(TeamCornerRig.Hero(name: "List Reader", team: "List Hall"), Array.Empty<string>());
            var build = typeof(TeamCornerLocation).GetMethod("BuildRecruitmentCandidates", F)!;
            var rows = ((IEnumerable)build.Invoke(rig.Loc, new object?[] { new List<NPC> { low, high }, null })!).Cast<object>().ToList();
            rows.Should().HaveCount(2);
            long WageOf(NPC n)
            {
                var row = rows.Single(r => ReferenceEquals(r.GetType().GetProperty("Npc")!.GetValue(r), n));
                return (long)row.GetType().GetProperty("DailyWage")!.GetValue(row)!;
            }
            WageOf(low).Should().Be(400);
            WageOf(high).Should().Be(3000);
            return Task.CompletedTask;
        });
    }

    [Fact]
    public async Task Row4_TheDailyCharge_TakesTheWageOfTheOneMethod_FromAnExistingTeamMember()
    {
        // NPCs hired before 1.2.8 keep their place; the next daily charge takes the new wage
        var veteran = Hire("r4_charge_10", 10, 84, team: "Old Guard");     // 400 (was 1,000)
        var senior = Hire("r4_charge_30", 30, 300, team: "Old Guard");     // 3,000 (unchanged)
        var other = Hire("r4_charge_other", 10, 84, team: "Another Team");
        await WithNpcs(new[] { veteran, senior, other }, async () =>
        {
            var hero = TeamCornerRig.Hero(name: "Old Leader", team: "Old Guard", gold: 10_000);
            var maint = new MaintenanceSystem(new TerminalUI());
            var charge = typeof(MaintenanceSystem).GetMethod("ProcessTeamWages", F)!;
            await (Task)charge.Invoke(maint, new object[] { hero })!;
            hero.Gold.Should().Be(10_000 - 400 - 3000);
            veteran.Team.Should().Be("Old Guard");
            senior.Team.Should().Be("Old Guard");
        });
    }

    [Fact]
    public async Task Row4_TheDailyCharge_IsPaidInFull_ByGoldThatTheOldWageWouldHaveEmptied()
    {
        var veteran = Hire("r4_poor_10", 10, 84, team: "Thin Purse");
        await WithNpcs(new[] { veteran }, async () =>
        {
            var hero = TeamCornerRig.Hero(name: "Thin Leader", team: "Thin Purse", gold: 500);
            hero.UnpaidWageDays = new Dictionary<string, int>();
            var maint = new MaintenanceSystem(new TerminalUI());
            await (Task)typeof(MaintenanceSystem).GetMethod("ProcessTeamWages", F)!.Invoke(maint, new object[] { hero })!;
            hero.Gold.Should().Be(100, "400 is charged, where 1,000 would have taken all 500 and left the wage unpaid");
            hero.UnpaidWageDays.Should().BeEmpty();
        });
    }

    // ---------- row 5: the other rules keep their ratio to the base ----------

    [Theory]
    [InlineData(10, 84, 16, 6776)]      // 9,680 x 0.7, 6 below
    [InlineData(10, 84, 15, 9680)]      // 5 below: no discount
    [InlineData(13, 123, 30, 11186)]    // 15,980 x 0.7
    [InlineData(26, 203, 40, 39242)]    // 56,060 x 0.7, as before
    public void Row5_MoreThanFiveBelowTheRecruiter_Pays70Percent(int level, long statSum, int recruiterLevel, long expected) =>
        TeamSystem.GetRecruitmentBaseCost(Hire("r5a_" + level, level, statSum), Recruiter(recruiterLevel)).Should().Be(expected);

    [Fact]
    public void Row5_TheFloorOf100_Holds()
    {
        var bare = Hire("r5_floor", 1, 0);   // 80 before the floor
        TeamSystem.GetRecruitmentBaseCost(bare, Recruiter(1)).Should().Be(100);
        TeamSystem.GetRecruitmentBaseCost(bare, Recruiter(100)).Should().Be(100, "80 x 0.7 is 56, floored");
        ResetWorld();
        var friend = Recruiter(1, "Floor Friend");
        SetScore(friend, bare, GameConfig.RelationFriendship);
        TeamSystem.GetRecruitmentCost(friend, bare).Should().Be(100, "a friend's 75 is floored too");
    }

    [Theory]
    [InlineData(GameConfig.RelationFriendship, 7260)]    // x 0.75
    [InlineData(GameConfig.RelationTrust, 8228)]         // x 0.85
    [InlineData(GameConfig.RelationRespect, 9196)]       // x 0.95
    [InlineData(GameConfig.RelationNormal, 9680)]        // x 1.00
    [InlineData(GameConfig.RelationSuspicious, 11132)]   // x 1.15
    [InlineData(GameConfig.RelationAnger, 13552)]        // x 1.40
    public void Row5_TheRelationship_KeepsItsRatioToTheNewBase(int score, long expected)
    {
        ResetWorld();
        var player = Recruiter(10, "Ratio Tester");
        var npc = Hire("r5_rel_" + score, 10, 84);   // base 9,680
        SetScore(player, npc, score);

        var (_, mult) = TeamSystem.GetRecruitmentBand(player, npc);
        TeamSystem.GetRecruitmentCost(player, npc).Should().Be(expected);
        TeamSystem.GetRecruitmentCost(player, npc).Should().Be((long)(TeamSystem.GetRecruitmentBaseCost(npc, player) * mult));
    }

    [Fact]
    public void Row5_AFriendAboveTheRecruiter_PaysBothRules()
    {
        ResetWorld();
        var player = Recruiter(10, "Both Rules");
        var npc = Hire("r5_both", 13, 123);   // 15,980 x 1.15 = 18,377 ; x 0.75 = 13,782
        SetScore(player, npc, GameConfig.RelationFriendship);
        TeamSystem.GetRecruitmentCost(player, npc).Should().Be(13782);
    }

    [Fact]
    public void Row5_AnAdultChild_PaysHalfTheNewBase()
    {
        ResetWorld();
        var parent = Recruiter(10, "Family Head");
        var child = Hire("r5_child", 10, 84);
        child.FatherID = parent.ID;

        var (band, mult) = TeamSystem.GetRecruitmentBand(parent, child);
        band.Should().Be(TeamSystem.RecruitmentBand.Family);
        mult.Should().Be(0.5);
        TeamSystem.GetRecruitmentCost(parent, child).Should().Be(4840);
    }

    [Fact]
    public void Row5_AnEnemy_StillRefuses()
    {
        ResetWorld();
        var player = Recruiter(10, "Refused Tester");
        var npc = Hire("r5_enemy", 10, 84);
        SetScore(player, npc, GameConfig.RelationHate);
        TeamSystem.GetRecruitmentCost(player, npc).Should().Be(-1);
    }

    // ---------- row 6: the real hiring path (ConfirmAndRecruit) ----------

    /// <summary>
    /// Runs TeamCornerLocation.ConfirmAndRecruit on a scripted terminal: "Y" at the confirmation, then
    /// Enter at each pause. The NPC is in the live list, so the method's own live lookup, its
    /// recruitable check and its live cost (computed again inside the method) all run. The cost handed in
    /// is only what the confirmation screen prints.
    /// </summary>
    private static async Task<(string shown, Character hero)> HireThroughTheRealPath(NPC recruit, int heroLevel, long gold, Action<Character>? prepare = null)
    {
        var hero = TeamCornerRig.Hero(name: "Hire Boss", team: "Price Hall", gold: gold);
        hero.ID = "hire_boss";
        hero.Level = heroLevel;
        prepare?.Invoke(hero);
        string shown = "";
        await WithNpcs(new[] { recruit }, async () =>
        {
            var rig = new TeamCornerRig(hero, new[] { "Y", "", "", "" });
            var (band, mult) = TeamSystem.GetRecruitmentBand(hero, recruit);
            long cost = TeamSystem.GetRecruitmentCost(hero, recruit);
            shown = await rig.Run("ConfirmAndRecruit", recruit, band, mult, cost);
        });
        return (shown, hero);
    }

    [Fact]
    public async Task Row6_OneGoldShort_IsRefused_AtTheNewPrice()
    {
        ResetWorld();
        var recruit = Hire("r6_short", 10, 84);   // 9,680 (was 21,680)
        var (shown, hero) = await HireThroughTheRealPath(recruit, heroLevel: 10, gold: 9679);
        shown.Should().Contain(Loc.Get("team.need_gold_recruit", "9,680", "9,679"));
        shown.Should().NotContain(Loc.Get("team.npc_joined", recruit.DisplayName));
        hero.Gold.Should().Be(9679);
        recruit.Team.Should().BeEmpty();
    }

    [Fact]
    public async Task Row6_TheExactPrice_Hires_AndLeavesNothing()
    {
        ResetWorld();
        var recruit = Hire("r6_exact", 10, 84);
        var (shown, hero) = await HireThroughTheRealPath(recruit, heroLevel: 10, gold: 9680);
        shown.Should().Contain(Loc.Get("team.npc_joined", recruit.DisplayName));
        hero.Gold.Should().Be(0);
        recruit.Team.Should().Be("Price Hall");
        WorldSimulator.UnregisterPlayerTeam("Price Hall");
    }

    [Fact]
    public async Task Row6_OneGoldAbove_Hires_PaysTheNewPrice_AndShowsTheNewWage()
    {
        ResetWorld();
        var recruit = Hire("r6_above", 10, 84);
        var (shown, hero) = await HireThroughTheRealPath(recruit, heroLevel: 10, gold: 9681);
        shown.Should().Contain(Loc.Get("team.recruit_confirm_base_cost", "9,680"));
        shown.Should().Contain(Loc.Get("team.npc_joined", recruit.DisplayName));
        shown.Should().Contain(Loc.Get("team.paid_recruitment", "9,680"));
        shown.Should().Contain(Loc.Get("team.daily_wage", "400"));
        hero.Gold.Should().Be(1);
        recruit.Team.Should().Be("Price Hall");
        WorldSimulator.UnregisterPlayerTeam("Price Hall");
    }

    [Fact]
    public async Task Row6_AnNpcThreeLevelsAbove_IsRefusedOneGoldShortOfTheGradedPrice_AndHiredAtIt()
    {
        ResetWorld();
        var recruit = Hire("r6_graded_a", 13, 123);   // 15,980 x 1.15 = 18,377 (was 28,460 x 1.5 = 42,690)
        var (shown, hero) = await HireThroughTheRealPath(recruit, heroLevel: 10, gold: 18376);
        shown.Should().Contain(Loc.Get("team.need_gold_recruit", "18,377", "18,376"));
        hero.Gold.Should().Be(18376);
        recruit.Team.Should().BeEmpty();

        var second = Hire("r6_graded_b", 13, 123);
        var (shown2, hero2) = await HireThroughTheRealPath(second, heroLevel: 10, gold: 18378);
        shown2.Should().Contain(Loc.Get("team.npc_joined", second.DisplayName));
        shown2.Should().Contain(Loc.Get("team.daily_wage", "676"));
        hero2.Gold.Should().Be(1);
        second.Team.Should().Be("Price Hall");
        WorldSimulator.UnregisterPlayerTeam("Price Hall");
    }

    [Fact]
    public async Task Row6_AFriend_IsHiredThroughTheRealPath_At75PercentOfTheNewBase()
    {
        ResetWorld();
        var recruit = Hire("r6_friend", 10, 84);   // 9,680 x 0.75 = 7,260
        var (shown, hero) = await HireThroughTheRealPath(recruit, heroLevel: 10, gold: 7259,
            prepare: h => SetScore(h, recruit, GameConfig.RelationFriendship));
        shown.Should().Contain(Loc.Get("team.need_gold_recruit", "7,260", "7,259"));
        hero.Gold.Should().Be(7259);

        ResetWorld();
        var second = Hire("r6_friend_b", 10, 84);
        var (shown2, hero2) = await HireThroughTheRealPath(second, heroLevel: 10, gold: 7261,
            prepare: h => SetScore(h, second, GameConfig.RelationFriendship));
        shown2.Should().Contain(Loc.Get("team.paid_recruitment", "7,260"));
        hero2.Gold.Should().Be(1);
        second.Team.Should().Be("Price Hall");
        WorldSimulator.UnregisterPlayerTeam("Price Hall");
    }
}
