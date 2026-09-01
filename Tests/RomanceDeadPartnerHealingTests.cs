using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using UsurperRemake.Systems;
using Xunit;

namespace UsurperRemake.Tests
{
    /// <summary>
    /// xUnit runs test CLASSES in parallel by default, and several game systems
    /// are process-wide singletons (RomanceTracker.Instance,
    /// DailySystemManager.Instance, NPCSpawnSystem.Instance). Classes that touch
    /// them must not run concurrently with each other or they clobber each
    /// other's state. Members of a named collection are serialized against one
    /// another, which is exactly what is needed here.
    ///
    /// Added in v1.0.2 after the dead-partner tests below started failing
    /// TeamSystemRecruitmentTests.GetRecruitmentBand_LoverIsHidden: the romance
    /// death path reads DailySystemManager.Instance, and that class asserts on
    /// RomanceTracker.Instance. The production code was proven clean (the whole
    /// suite passed with the fix applied and these tests excluded); the race was
    /// purely between test classes.
    /// </summary>
    [CollectionDefinition("SharedGameSingletons")]
    public class SharedGameSingletonsCollection { }

    /// <summary>
    /// Regression tests for v1.0.2 dead-partner login healing.
    ///
    /// Reported live: a player stayed married to an NPC whose id was absent
    /// from the entire world roster, with an empty ExSpouses list. The death
    /// cascade had correctly cleaned the marriage registry and the
    /// RelationshipSystem record, but the player's own romanceData was never
    /// touched, because the NPC died during a world-sim tick that ran with no
    /// session attached to that player. Login reloaded the stale record
    /// verbatim, so the ghost marriage survived indefinitely.
    /// </summary>
    [Collection("SharedGameSingletons")]
    public class RomanceDeadPartnerHealingTests
    {
        private static NPC MakeNpc(string id, string name, bool dead)
        {
            var npc = new NPC();
            npc.ID = id;
            npc.Name2 = name;
            // v1.0.4: "dead" here means permanently dead. IsDead alone is the
            // transient knockdown state that the world sim respawns.
            npc.IsDead = dead;
            npc.IsPermaDead = dead;
            return npc;
        }

        /// <summary>
        /// Build a roster that is plausibly complete. v1.0.2 added a
        /// plausibility floor so a truncated snapshot can never prove an NPC is
        /// absent (see RosterRebuildInFlight_RetiresNothing). Tests that WANT
        /// the retirement path must therefore hand over a realistically sized
        /// roster; real worlds run to roughly 150 NPCs.
        /// </summary>
        private static List<NPC> PlausibleRoster(params NPC[] real)
        {
            var list = new List<NPC>(real);
            for (int i = list.Count; i < 60; i++)
                list.Add(MakeNpc($"npc_filler_{i}", $"Filler {i}", dead: false));
            return list;
        }

        private static RomanceTracker TrackerWithSpouse(string id, string name)
        {
            var t = new RomanceTracker();
            t.Spouses.Add(new Spouse { NPCId = id, NPCName = name });
            return t;
        }

        [Fact]
        public void SpouseMissingFromRoster_IsRetiredAndBecomesExSpouse()
        {
            // The exact reported shape: married to an NPC id that no longer
            // exists anywhere in the world.
            var t = TrackerWithSpouse("npc_imm_mirena_5c7ec5c7", "Mirena Kettleburn");
            var roster = PlausibleRoster(MakeNpc("npc_someone_else", "Someone Else", dead: false));

            int healed = t.SyncDeadPartners(roster);

            healed.Should().Be(1);
            t.Spouses.Should().BeEmpty("a spouse who is gone from the world is not a spouse");
            t.ExSpouses.Should().ContainSingle(e => e.NPCId == "npc_imm_mirena_5c7ec5c7",
                "the marriage must be preserved as history, making the player a widow or widower");
        }

        [Fact]
        public void SpouseFlaggedDead_IsRetired()
        {
            var t = TrackerWithSpouse("npc_dead", "Dead Spouse");
            var roster = PlausibleRoster(MakeNpc("npc_dead", "Dead Spouse", dead: true));

            t.SyncDeadPartners(roster).Should().Be(1);
            t.Spouses.Should().BeEmpty();
            t.ExSpouses.Should().ContainSingle(e => e.NPCId == "npc_dead");
        }

        /// <summary>
        /// v1.0.4: a spouse knocked down by the world sim (IsDead, not
        /// IsPermaDead) respawns in about ten minutes and is still married.
        /// Retiring them on a relog inside that window produced a respawned
        /// spouse who was suddenly a stranger.
        /// </summary>
        [Fact]
        public void SpouseTemporarilyDead_AwaitingRespawn_IsNotRetired()
        {
            var t = TrackerWithSpouse("npc_downed", "Downed Spouse");
            var downed = MakeNpc("npc_downed", "Downed Spouse", dead: false);
            downed.IsDead = true;
            var roster = PlausibleRoster(downed);

            t.SyncDeadPartners(roster).Should().Be(0);
            t.Spouses.Should().ContainSingle(s => s.NPCId == "npc_downed");
            t.ExSpouses.Should().BeEmpty();
        }

        [Fact]
        public void LivingSpouse_IsLeftAlone()
        {
            var t = TrackerWithSpouse("npc_alive", "Living Spouse");
            var roster = PlausibleRoster(MakeNpc("npc_alive", "Living Spouse", dead: false));

            t.SyncDeadPartners(roster).Should().Be(0);
            t.Spouses.Should().ContainSingle(s => s.NPCId == "npc_alive");
            t.ExSpouses.Should().BeEmpty();
        }

        [Fact]
        public void EmptyRoster_ChangesNothing_TheGuardThatPreventsMassWidowing()
        {
            // If this pass ever ran before the NPC roster was restored, every
            // partner would look missing and the whole server would be widowed
            // in a single login. This guard is load-bearing.
            var t = TrackerWithSpouse("npc_alive", "Living Spouse");

            t.SyncDeadPartners(new List<NPC>()).Should().Be(0);
            t.SyncDeadPartners(null).Should().Be(0);

            t.Spouses.Should().ContainSingle("an unloaded roster must never be read as 'everyone died'");
            t.ExSpouses.Should().BeEmpty();
        }

        [Fact]
        public void RosterRebuildInFlight_RetiresNothing()
        {
            // The failure the reviewer caught (B1). The NPC roster singleton is
            // process-wide and rebuilt non-atomically: ClearAllNPCs, then 151
            // separate adds. A concurrent login could observe a valid, non-empty,
            // HALF-BUILT roster, fail to find a living spouse in it, and widow
            // that player permanently with no undo. "Not empty" was never a
            // sufficient guard; the roster must be settled.
            var spawner = NPCSpawnSystem.Instance;
            var t = TrackerWithSpouse("npc_alive", "Living Spouse");
            var partialRoster = PlausibleRoster(MakeNpc("npc_alive", "Living Spouse", dead: false));

            bool previous = spawner.IsRebuilding;
            try
            {
                spawner.IsRebuilding = true;
                t.SyncDeadPartners(partialRoster).Should().Be(0,
                    "no partner may be retired while the roster is mid-rebuild");
            }
            finally
            {
                spawner.IsRebuilding = previous;
            }

            t.Spouses.Should().ContainSingle("a living spouse must survive a rebuild window");
            t.ExSpouses.Should().BeEmpty();
        }

        [Fact]
        public void ImplausiblySmallRoster_IsNotAuthoritativeForDeletion()
        {
            // Defense in depth for the same race: even with no rebuild flag set,
            // a snapshot holding a handful of NPCs must never be trusted to prove
            // an NPC does not exist. Real rosters run to ~150.
            var t = TrackerWithSpouse("npc_missing_from_tiny_roster", "Someone");
            var tinyRoster = new List<NPC>
            {
                MakeNpc("npc_a", "A", dead: false),
                MakeNpc("npc_b", "B", dead: false),
                MakeNpc("npc_c", "C", dead: false),
            };

            t.SyncDeadPartners(tinyRoster).Should().Be(0,
                "a 3-NPC snapshot cannot prove a partner is gone from a 150-NPC world");
            t.Spouses.Should().ContainSingle();
        }

        [Fact]
        public void IsIdempotent_RepeatedLoginsDoNotDuplicateHistory()
        {
            var t = TrackerWithSpouse("npc_gone", "Gone Spouse");
            var roster = PlausibleRoster(MakeNpc("npc_other", "Other", dead: false));

            t.SyncDeadPartners(roster).Should().Be(1);
            t.SyncDeadPartners(roster).Should().Be(0, "already-retired partners are no longer in the live list");
            t.ExSpouses.Count(e => e.NPCId == "npc_gone").Should().Be(1);
        }

        [Fact]
        public void DeadLoverAndFwb_AreAlsoRetired()
        {
            // Same root cause reaches lovers and FWB, not just spouses.
            var t = new RomanceTracker();
            t.CurrentLovers.Add(new Lover { NPCId = "npc_lover", NPCName = "Lost Lover" });
            t.FriendsWithBenefits.Add("npc_fwb");
            var roster = PlausibleRoster(MakeNpc("npc_unrelated", "Unrelated", dead: false));

            t.SyncDeadPartners(roster).Should().Be(2);
            t.CurrentLovers.Should().BeEmpty();
            t.FriendsWithBenefits.Should().NotContain("npc_fwb");
        }

        [Fact]
        public void MixedRoster_RetiresOnlyTheDeadOnes()
        {
            var t = new RomanceTracker();
            t.Spouses.Add(new Spouse { NPCId = "npc_alive", NPCName = "Alive" });
            t.Spouses.Add(new Spouse { NPCId = "npc_dead", NPCName = "Dead" });
            t.Spouses.Add(new Spouse { NPCId = "npc_gone", NPCName = "Gone" });
            var roster = PlausibleRoster(
                MakeNpc("npc_alive", "Alive", dead: false),
                MakeNpc("npc_dead", "Dead", dead: true));

            t.SyncDeadPartners(roster).Should().Be(2);
            t.Spouses.Should().ContainSingle(s => s.NPCId == "npc_alive");
        }
    }
}
