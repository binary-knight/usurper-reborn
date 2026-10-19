using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using UsurperRemake;
using UsurperRemake.Systems;
using Xunit;
using static UsurperReborn.Tests.PartySurvival133Harness;

namespace UsurperReborn.Tests;

/// <summary>
/// 1.3.5: a living companion takes the same 0.75 share of non combat experience as an NPC teammate,
/// paid to the Companion's own record with its level up check: the feature outcome split
/// (DungeonLocation.SplitFeatureOutcome) and the grouped party split of AwardDungeonReward
/// (SplitPartyRewards, which also carries the floor boss bonus). The fight's slot share is unchanged.
/// All names and numbers are synthetic.
/// </summary>
[Collection("SharedGameSingletons")]
public class CompanionFeatureXp135Tests
{
    private const BindingFlags F = BindingFlags.NonPublic | BindingFlags.Instance;

    private static TerminalEmulator Term() =>
        new(new ScriptedStream(string.Concat(Enumerable.Repeat("\n", 40))), new MemoryStream());

    private static Character Leader(int level) => new Character
    {
        Name1 = "leader", Name2 = "Leader", AI = CharacterAI.Human, Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Level = level, HP = 5000, MaxHP = 5000, Experience = 0, Gold = 0, AutoLevelUp = false, CombatSpeed = CombatSpeed.Instant,
    };

    private static Character Grouped(int level)
    {
        var g = Leader(level);
        g.Name1 = "follower"; g.Name2 = "Follower";
        g.RemoteTerminal = Term();   // IsGroupedPlayer
        return g;
    }

    private static NPC Npc(int level) => new NPC
    {
        Name1 = "mate", Name2 = "Mate", Class = CharacterClass.Warrior, Race = CharacterRace.Human,
        Level = level, HP = 3000, MaxHP = 3000, Experience = 0,
    };

    /// <summary>A recruited, active companion at level 20, at the start of that level; returns its record and wrapper.</summary>
    private static (Companion rec, Character wrapper) Companion20()
    {
        FreshCompanion(CompanionId.Aldric);
        var rec = CompanionSystem.Instance.GetCompanion(CompanionId.Aldric)!;
        rec.Level = 20;
        rec.Experience = GameConfig.GetExperienceForLevel(20);
        var wrapper = CompanionSystem.Instance.GetCompanionsAsCharacters().Single(w => w.CompanionId == CompanionId.Aldric);
        return (rec, wrapper);
    }

    private static DungeonLocation Dungeon(Character leader, IEnumerable<Character> party)
    {
        var d = new DungeonLocation();
        typeof(BaseLocation).GetField("terminal", F)!.SetValue(d, Term());
        typeof(BaseLocation).GetField("currentPlayer", F)!.SetValue(d, leader);
        d.teammates.AddRange(party);
        return d;
    }

    private static void SplitFeature(DungeonLocation d, Character leader, long xp)
    {
        var outcome = new FeatureOutcome { ExperienceGained = xp };
        var feature = new RoomFeature("Synthetic altar", "a synthetic test feature", FeatureInteraction.Examine);
        leader.Experience += xp;   // the feature system pays the leader in full first
        try { typeof(DungeonLocation).GetMethod("SplitFeatureOutcome", F)!.Invoke(d, new object[] { leader, feature, outcome }); }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    private static (long xp, long gold) Award(DungeonLocation d, long xp, string key, bool fromCombat)
    {
        try { return ((long, long))typeof(DungeonLocation).GetMethod("AwardDungeonReward", F)!.Invoke(d, new object[] { xp, 0L, key, fromCombat })!; }
        catch (TargetInvocationException ex) when (ex.InnerException != null) { throw ex.InnerException; }
    }

    [Fact]
    public void AFeatureOutcome_PaysTheCompanion750_OnItsRecord_AsTheNpcTeammate()
    {
        try
        {
            var (rec, wrapper) = Companion20();
            long before = rec.Experience;
            var leader = Leader(40);
            var npc = Npc(30);
            var echo = Npc(30); echo.Name2 = "Echo"; echo.IsEcho = true;
            SplitFeature(Dungeon(leader, new Character[] { wrapper, npc, echo }), leader, 1000);

            (rec.Experience - before).Should().Be(750, "a companion takes the NPC teammate's 0.75 share of a feature's experience");
            npc.Experience.Should().Be(750, "the NPC teammate's share is unchanged");
            wrapper.Experience.Should().Be(0, "the share goes to the Companion record, never the combat wrapper");
            echo.Experience.Should().Be(0, "echoes keep their own rule");
            leader.Experience.Should().Be(1000, "the leader keeps the full amount");
        }
        finally { RestoreSilence(); }
    }

    [Fact]
    public void AFeatureOutcome_RunsTheCompanionsLevelUpCheck()
    {
        try
        {
            var (rec, wrapper) = Companion20();
            rec.Experience = GameConfig.GetExperienceForLevel(21) - 1;
            var leader = Leader(40);
            SplitFeature(Dungeon(leader, new[] { wrapper }), leader, 1000);
            rec.Level.Should().Be(21, "a companion one experience short of level 21 crosses it");
        }
        finally { RestoreSilence(); }
    }

    [Fact]
    public void ADeadCompanion_TakesNothing()
    {
        try
        {
            var (rec, wrapper) = Companion20();
            long before = rec.Experience;
            var leader = Leader(40);
            var npc = Npc(30);
            wrapper.HP = 0;
            rec.IsDead = true;
            var d = Dungeon(leader, new Character[] { wrapper, npc, Grouped(40) });
            SplitFeature(d, leader, 1000);
            Award(d, 10_000, "dungeon.reward_src_floor_cleared", fromCombat: false);
            rec.Experience.Should().Be(before, "a dead companion takes nothing, as a dead NPC teammate");
            npc.Experience.Should().Be(750 + 7_500);

            wrapper.HP = wrapper.MaxHP;   // a stale wrapper still standing: the record's death still counts
            SplitFeature(d, leader, 1000);
            rec.Experience.Should().Be(before);
        }
        finally { RestoreSilence(); }
    }

    [Fact]
    public void TheGroupedSplit_PaysTheCompanion75Percent_AndLeavesThePlayersSharesAlone()
    {
        try
        {
            var (rec, wrapper) = Companion20();
            long before = rec.Experience;
            var leader = Leader(40);
            var follower = Grouped(40);
            var npc = Npc(30);
            var r = Award(Dungeon(leader, new Character[] { follower, wrapper, npc }), 10_000, "dungeon.reward_src_treasure_chest", fromCombat: false);

            (rec.Experience - before).Should().Be(7_500, "a companion takes 0.75 of the grouped split's total");
            npc.Experience.Should().Be(7_500);
            wrapper.Experience.Should().Be(0);
            r.xp.Should().Be(10_000, "the leader's share is unchanged");
            leader.Experience.Should().Be(10_000);
            follower.Experience.Should().Be(10_000, "the grouped player's share is unchanged");
        }
        finally { RestoreSilence(); }
    }

    private static long FightSlotShare(Character leader, List<Character> team, long pot)
    {
        var engine = new CombatEngine(Term());
        typeof(CombatEngine).GetField("currentPlayer", F)!.SetValue(engine, leader);
        typeof(CombatEngine).GetMethod("DistributeTeamSlotXP", F)!
            .Invoke(engine, new object[] { leader, team, pot, Term(), new[] { 50, 50, 0, 0, 0 } });
        return pot / 2;
    }

    [Fact]
    public void TheFloorBossBonus_PaysTheCompanion75PercentOnce_BesideItsUnchangedFightShare()
    {
        try
        {
            var (rec, wrapper) = Companion20();
            long start = rec.Experience;
            var leader = Leader(20);       // the companion's level: no catch up bonus on its slot
            var follower = Grouped(20);
            var npc = Npc(20);

            // The won fight's slot share, as the victory screen pays it.
            long slot = FightSlotShare(leader, new List<Character> { wrapper }, 20_000);
            (rec.Experience - start).Should().Be(slot, "the companion's fight share is unchanged");

            // Then the floor boss bonus, through the dungeon reward from combat.
            const long bossExp = 20 * 300;
            long afterFight = rec.Experience;
            var d = Dungeon(leader, new Character[] { follower, wrapper, npc });
            var r = Award(d, bossExp, "dungeon.reward_src_boss_defeated", fromCombat: true);

            (rec.Experience - afterFight).Should().Be(4_500, "a companion takes 0.75 of the boss bonus exactly once");
            (rec.Experience - start).Should().Be(slot + 4_500);
            npc.Experience.Should().Be(4_500, "the NPC teammate's 0.75 of the bonus is unchanged");
            r.xp.Should().Be(bossExp);
            follower.Experience.Should().Be(bossExp);
        }
        finally { RestoreSilence(); }
    }
}
